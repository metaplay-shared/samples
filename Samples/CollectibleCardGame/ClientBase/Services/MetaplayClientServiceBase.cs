using Metaplay.Client;
using Metaplay.Core;
using Metaplay.Core.Client;
using Metaplay.Core.Model;
using Metaplay.Core.Player;
using Metaplay.Core.Session;
using Metaplay.Core.Session.ConnectionStates;
using Metaplay.Unity;

namespace Game.ClientBase.Services;

/// <summary>
/// Abstract base class for Metaplay client services.
/// Handles connection lifecycle, reconnection with exponential backoff, and the frame pump.
/// Game-specific implementations should inherit from this class.
/// </summary>
/// <typeparam name="TPlayerModel">The game-specific PlayerModel type.</typeparam>
public abstract class MetaplayClientServiceBase<TPlayerModel> : IMetaplayConnectionService, IDisposable
    where TPlayerModel : class, IPlayerModelBase
{
    private static bool _coreInitialized;

    private IMetaplayClient? _client;
    private MetaplaySession? _session;

    private ConnectionStatus _connectionStatus = ConnectionStatus.Disconnected;
    private string? _errorMessage;
    private bool _running;
    private int _reconnectAttempts = 0;

    /// <summary>
    /// Whether a connection loop is running. Separate from <see cref="_running"/>, which covers the frame pump
    /// and the whole service: the loop stops on its own when it runs out of retries, and the player's manual
    /// retry has to be able to start it again.
    /// </summary>
    private bool _connectionLoopActive;

    /// <summary>
    /// Which connection loop is the current one. A loop whose generation has been superseded — by a Reset, or
    /// by a manual retry that raced it — exits rather than connecting alongside its replacement.
    /// </summary>
    private int _connectionLoopGeneration;

    /// <summary>Signals the backoff delay to end early. Non-null only while a delay is being waited out.</summary>
    private TaskCompletionSource<bool>? _retryWake;

    /// <summary>
    /// Whether a session has ever started. Until one has, a connection that is not up is a cold start rather
    /// than an outage, and the screen's own connecting state is the honest thing to show.
    /// </summary>
    private bool _hasEverConnected;

    /// <summary>What the last session loss was, or <see cref="ConnectionTroubleKind.None"/> while in session.</summary>
    private ConnectionTroubleKind _sessionTroubleKind = ConnectionTroubleKind.None;

    /// <summary>Whether the client is still working on the loss in <see cref="_sessionTroubleKind"/>.</summary>
    private bool _sessionTroubleRetrying;

    /// <summary>When the SDK's own transport last became unhealthy, or null while it is up.</summary>
    private DateTime? _linkUnhealthySince;

    /// <summary>Whether the transport has been down long enough to be worth telling the player about.</summary>
    private bool _linkStalled;

    /// <summary>Whether the page is on screen, as last reported by the app shell.</summary>
    private bool _pageVisible = true;

    /// <summary>When the page last came back on screen. Epoch while it has never been away.</summary>
    private DateTime _pageForegroundedAt = DateTime.UnixEpoch;

    /// <summary>The trouble kind the frame pump last raised a state change for.</summary>
    private ConnectionTroubleKind _notifiedTroubleKind = ConnectionTroubleKind.None;

    /// <summary>Whether the trouble the frame pump last raised a state change for was quiet.</summary>
    private bool _notifiedTroubleQuiet;

    /// <summary>How many times each distinct frame failure has been seen, so repeats are counted not logged.</summary>
    private readonly Dictionary<string, int> _frameFailureCounts = new Dictionary<string, int>();

    /// <summary> Reconnect attempts before the loop stops and the shell asks the player. </summary>
    private const int MaxReconnectAttempts = 10;

    /// <summary> The first backoff delay, doubled per attempt up to <see cref="MaxReconnectDelayMs"/>. </summary>
    private const int BaseReconnectDelayMs = 1000;

    private const int MaxReconnectDelayMs = 30000;

    /// <summary> Interval between frames of the SDK frame pump. </summary>
    private const int UpdateIntervalMs = 50;

    /// <summary>
    /// How long the SDK's transport has to be down before the shell says so. The SDK resumes a dropped session
    /// silently and often wins, so a shorter window would flash a modal over blips nobody needed to see.
    /// </summary>
    private static readonly TimeSpan LinkStallGrace = TimeSpan.FromSeconds(2);

    /// <summary>
    /// How long after the page comes back on screen a lost link is still read as a resume rather than an
    /// outage: the round trip the client needs to notice the link is gone and get it back.
    /// </summary>
    private static readonly TimeSpan ResumeWindow = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Event fired when connection state or player model changes.
    /// </summary>
    public event Action? OnStateChanged;

    /// <summary>
    /// Where the connection loop is.
    /// </summary>
    public ConnectionStatus ConnectionStatus => _connectionStatus;

    /// <summary>
    /// Why the connection loop stopped, or null while it is running.
    /// </summary>
    public string? ErrorMessage => _errorMessage;

    /// <summary>
    /// The current player model, or null if no session has started.
    /// </summary>
    public TPlayerModel? PlayerModel => _session?.PlayerContext?.Model as TPlayerModel;

    /// <summary>
    /// Player ID if connected, otherwise null.
    /// </summary>
    public EntityId? PlayerId => PlayerModel?.PlayerId;

    /// <summary>
    /// Whether a lapsed link right now is more likely a throttled tab than an outage: the page is away, or it
    /// has only just come back.
    /// </summary>
    private bool InResumeWindow => !_pageVisible || DateTime.UtcNow - _pageForegroundedAt < ResumeWindow;

    /// <summary>
    /// What is wrong with the connection. A session loss outranks a stalled transport, because the loss has a
    /// named reason and the stall only has a symptom.
    /// </summary>
    public ConnectionTrouble Trouble
    {
        get
        {
            // A cold start that is still trying is the screen's to show; one that has given up is the shell's,
            // because the shell's retry is the only way on.
            if (!_hasEverConnected && (_sessionTroubleKind == ConnectionTroubleKind.None || _sessionTroubleRetrying))
                return ConnectionTrouble.None;

            if (_sessionTroubleKind != ConnectionTroubleKind.None)
            {
                bool quiet = _sessionTroubleKind == ConnectionTroubleKind.LinkLost && _sessionTroubleRetrying && InResumeWindow;
                return new ConnectionTrouble(_sessionTroubleKind, _sessionTroubleRetrying, quiet, _reconnectAttempts, MaxReconnectAttempts);
            }

            // The transport is down while the SDK still holds the session and is trying to resume it. Nothing
            // the player does reaches the server, so the shell says so rather than waiting for the SDK to give
            // up on the resume.
            if (_linkStalled)
                return new ConnectionTrouble(ConnectionTroubleKind.LinkLost, isRetrying: true, isQuiet: InResumeWindow, attempt: 0, maxAttempts: MaxReconnectAttempts);

            return ConnectionTrouble.None;
        }
    }

    protected MetaplayClientServiceBase()
    {
        if (_coreInitialized)
            return;

        // Set the integration roots before init. The platform-specific behavior (loading the pre-built
        // serializer assembly by name rather than generating one, and the WebSocket transport) follows from the
        // browser build of the SDK.
        MetaplayCore.ClientIntegrationAssemblies = IntegrationAssembly.FindRoots().ToList();
        MetaplayCore.InitializeForClient();
        _coreInitialized = true;
    }

    /// <summary>
    /// Create the Metaplay client, via <see cref="MetaplayClient.Create"/>. Implementations pass the
    /// game's delegates and any additional entity sub-clients.
    /// </summary>
    protected abstract IMetaplayClient CreateClient();

    /// <summary>
    /// Connect to the Metaplay server. Called once at startup; starts the frame pump on the first call and the
    /// connection loop whenever one is not already running.
    /// </summary>
    public void Connect()
    {
        if (_connectionLoopActive)
            return;

        _connectionStatus = ConnectionStatus.Connecting;
        _errorMessage = null;
        _reconnectAttempts = 0;
        _sessionTroubleKind = ConnectionTroubleKind.None;
        _sessionTroubleRetrying = false;
        NotifyStateChanged();

        _client ??= CreateClient();

        if (!_running)
        {
            _running = true;
            _ = RunFramePumpAsync();
        }

        _connectionLoopActive = true;
        _connectionLoopGeneration++;
        _ = RunConnectionLoopAsync(_connectionLoopGeneration);
    }

    /// <summary>
    /// Reconnect now. Ends a backoff delay early if one is being waited out, and starts the connection loop
    /// again if it has run out of retries or was parked.
    /// </summary>
    public void RetryNow()
    {
        _reconnectAttempts = 0;

        TaskCompletionSource<bool>? wake = _retryWake;
        if (wake != null)
        {
            wake.TrySetResult(true);
            return;
        }

        // No delay is being waited out: either a connection attempt is already in flight, in which case
        // Connect is a no-op, or the loop has stopped and this starts it again.
        Connect();
    }

    /// <summary>
    /// Record whether the page is on screen. Coming back is also the moment to retry: the backoff was measured
    /// out by a throttled tab's timers, so a player who has just returned would otherwise wait through a delay
    /// that has nothing to do with how long the server has been unreachable.
    /// </summary>
    public void NotifyPageVisibilityChanged(bool isVisible)
    {
        if (isVisible == _pageVisible)
            return;

        _pageVisible = isVisible;

        if (!isVisible)
            OnPageLeavingScreen();

        if (isVisible)
        {
            _pageForegroundedAt = DateTime.UtcNow;

            ConnectionTrouble trouble = Trouble;
            if (trouble.Kind == ConnectionTroubleKind.LinkLost && trouble.IsRetrying)
            {
                Console.WriteLine($"[{GetType().Name}] Page back on screen with the link down; reconnecting now");
                RetryNow();
            }
        }

        NotifyStateChanged();
    }

    /// <summary>
    /// Settle up before the page may stop existing. The browser build of the SDK has no application-lifecycle
    /// source, and a reload tears the runtime down without a connection close, so the page going off screen is
    /// the one moment there is: flush queued player actions, and write the offline player to (synchronous)
    /// localStorage. Skipped after a reset, which would otherwise write the discarded player straight back.
    /// </summary>
    private void OnPageLeavingScreen()
    {
        if (!_running)
            return;

        try
        {
            // The only public route to the SDK's action flush. The pause hint it also records is read by Unity's
            // lifecycle listener only, so in the browser this is the flush and nothing more.
            MetaplaySDK.OnApplicationAboutToBePaused("PageHidden", TimeSpan.FromSeconds(30));

            MetaplayConnection? connection = MetaplaySDK.Connection;
            if (connection != null && connection.Endpoint.IsOfflineMode)
                connection.OfflineServer?.TryPersistState();
        }
        catch (Exception ex)
        {
            // A page that is going away anyway is not worth throwing over.
            Console.WriteLine($"[{GetType().Name}] Settling up before the page left the screen failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Execute a player action on the SDK's next frame. Not inline: a model listener can render a component
    /// synchronously, and an action that render fires would otherwise run inside the action that raised the
    /// listener, which the SDK refuses. The SDK sends the action whatever it returns, so a caller that needs
    /// the verdict asks <see cref="DryExecuteAction"/> first.
    /// </summary>
    public void ExecuteAction(PlayerActionBase action)
    {
        MetaplaySDK.RunOnMainThreadAsync(() =>
        {
            _session?.PlayerContext?.ExecuteAction(action);
        });
    }

    /// <summary>
    /// What the action would return against the current model, without changing it. Null before a session has
    /// started.
    /// </summary>
    public MetaActionResult? DryExecuteAction(PlayerActionBase action) => _session?.PlayerContext?.DryExecuteAction(action);

    /// <summary>
    /// Called when a session has started, before the SDK is told the start completed. Override to wire up
    /// model listeners and load game content.
    /// </summary>
    protected virtual void OnSessionStarted(MetaplaySession session)
    {
    }

    /// <summary>
    /// Reset the client: disconnect, throw the local player away, and reset state. The caller reloads the
    /// page afterwards, which comes back up as a brand-new player.
    /// Returns true if reset was successful.
    /// </summary>
    public bool Reset()
    {
        try
        {
            StopLoops();

            // Both blobs: in offline mode the account itself lives in the persisted offline state, so deleting
            // the credentials alone would bring the old collection back under a new id. Best effort.
            TryDeleteBlob(GetCredentialsPath(), "credentials");
            TryDeleteBlob(DefaultOfflineServer.GetPersistedStatePath(), "offline state");

            _connectionStatus = ConnectionStatus.Disconnected;
            _errorMessage = null;
            _hasEverConnected = false;
            _sessionTroubleKind = ConnectionTroubleKind.None;
            _sessionTroubleRetrying = false;
            _linkUnhealthySince = null;
            _linkStalled = false;
            NotifyStateChanged();

            Console.WriteLine($"[{GetType().Name}] Reset complete");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{GetType().Name}] Reset failed: {ex.Message}");
            return false;
        }
    }

    /// <summary> Delete one blob, reporting rather than throwing when it cannot be reached. </summary>
    private void TryDeleteBlob(string path, string what)
    {
        try
        {
            AtomicBlobStore.TryDeleteBlob(path);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{GetType().Name}] {what} cleanup skipped: {ex.Message}");
        }
    }

    /// <summary>
    /// The path of the credentials blob: in the browser, a key into the localStorage-backed blob store.
    /// </summary>
    private static string GetCredentialsPath()
    {
        // The engine integration that owns this path is SDK-internal, so the deprecated accessor is the
        // only public way to reach it.
        #pragma warning disable CS0618
        return Path.Combine(MetaplaySDK.PersistentDataPath, "MetaplayCredentials.dat");
        #pragma warning restore CS0618
    }

    /// <summary>
    /// Drive the SDK frame pump from the single browser thread. WebAssembly is single-threaded, so instead of
    /// the SDK's thread-based frame loop we await between frames — the yield lets the WebSocket transport's
    /// async continuations and the connection loop run cooperatively. (No re-entrancy guard is needed: frames
    /// never overlap on one thread.)
    /// </summary>
    private async Task RunFramePumpAsync()
    {
        while (_running)
        {
            try
            {
                FrameLoop.RunFrame();

                UpdateLinkHealth();

                // The pump raises state changes only for connection trouble; model changes reach the UI through
                // the model's observers.
                NotifyTroubleChangedIfNeeded();
            }
            catch (Exception ex)
            {
                // Survivable (a shutdown or reconnect can tear state out from under a frame), but a frame that
                // throws every frame stops the SDK, so each distinct failure is logged and repeats are counted.
                ReportFrameFailure(ex);
            }

            await Task.Delay(UpdateIntervalMs);
        }
    }

    /// <summary>
    /// Report a frame failure once per distinct site, with a running count of the repeats. Twenty frames a
    /// second means one unlucky failure would otherwise bury every other line in the log.
    /// </summary>
    private void ReportFrameFailure(Exception ex)
    {
        string key = $"{ex.GetType().FullName}: {ex.Message}";

        if (_frameFailureCounts.TryGetValue(key, out int seen))
        {
            _frameFailureCounts[key] = seen + 1;

            // Every hundredth repeat, so a frame that is failing continuously stays visible without flooding.
            if ((seen + 1) % 100 == 0)
                Console.WriteLine($"[{GetType().Name}] frame failure still recurring ({seen + 1}x): {key}");

            return;
        }

        _frameFailureCounts[key] = 1;
        Console.WriteLine($"[{GetType().Name}] frame failure: {ex}");
    }

    /// <summary>
    /// Watch the SDK's own transport rather than waiting for it to give up. A socket that dies inside a live
    /// session leaves the connection reported as connected while the SDK spends its resume budget — twenty
    /// seconds of it — trying to get the same session back. Every tap made in that window goes nowhere, so it
    /// is the shell's business even though no session has ended.
    /// <para>
    /// The signal is the connection's own health flag: the SDK's QoS monitor clears it when the transport is
    /// detached, when the handshaked connection is lost, and while a session resume is unanswered.
    /// </para>
    /// </summary>
    private void UpdateLinkHealth()
    {
        if (!_hasEverConnected)
            return;

        ConnectionState? sdkState = MetaplaySDK.Connection?.State;
        if (sdkState is Connected connected && connected.IsHealthy)
        {
            _linkUnhealthySince = null;
            _linkStalled = false;
            return;
        }

        _linkUnhealthySince ??= DateTime.UtcNow;
        _linkStalled = DateTime.UtcNow - _linkUnhealthySince.Value >= LinkStallGrace;
    }

    /// <summary>Raise a state change when what the shell should be showing has changed.</summary>
    private void NotifyTroubleChangedIfNeeded()
    {
        ConnectionTrouble trouble = Trouble;
        if (trouble.Kind == _notifiedTroubleKind && trouble.IsQuiet == _notifiedTroubleQuiet)
            return;

        _notifiedTroubleKind = trouble.Kind;
        _notifiedTroubleQuiet = trouble.IsQuiet;
        NotifyStateChanged();
    }

    /// <summary>
    /// Connect, run the session until it ends, then reconnect with exponential backoff. The connection only
    /// makes progress while the frame pump runs, so the two loops interleave on the browser thread.
    /// </summary>
    private async Task RunConnectionLoopAsync(int generation)
    {
        try
        {
            while (_running && generation == _connectionLoopGeneration)
            {
                MetaplaySession session;
                try
                {
                    session = await _client!.ConnectAsync();
                }
                catch (FailedToStartSessionException ex)
                {
                    Console.WriteLine($"[{GetType().Name}] Failed to start session: {ex.Failure.TechnicalError?.GetType().Name} - {ex.Failure.EnglishLocalizedReason}");
                    if (await HandleConnectionLostAsync(ex.Failure))
                        continue;
                    return;
                }
                catch (OperationCanceledException)
                {
                    // The client was disposed (Reset), so there is nothing left to connect.
                    return;
                }

                _session = session;

                try
                {
                    OnSessionStarted(session);
                    session.SessionStartComplete();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[{GetType().Name}] Session start failed: {ex.Message}");
                    ConnectionLostEvent startFailure = session.SessionStartFailed(ex);
                    if (await HandleConnectionLostAsync(startFailure))
                        continue;
                    return;
                }

                _connectionStatus = ConnectionStatus.Connected;
                _errorMessage = null;
                _reconnectAttempts = 0;
                _hasEverConnected = true;
                _sessionTroubleKind = ConnectionTroubleKind.None;
                _sessionTroubleRetrying = false;
                _linkUnhealthySince = null;
                _linkStalled = false;
                NotifyStateChanged();

                ConnectionLostEvent connectionLost;
                try
                {
                    connectionLost = await session.WaitForSessionEndAsync();
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                Console.WriteLine($"[{GetType().Name}] Session ended: {connectionLost.TechnicalError?.GetType().Name} - {connectionLost.EnglishLocalizedReason}");
                if (!await HandleConnectionLostAsync(connectionLost))
                    return;
            }
        }
        finally
        {
            if (generation == _connectionLoopGeneration)
                _connectionLoopActive = false;
        }
    }

    /// <summary>
    /// Classify a connection loss and, when it is worth retrying, wait out the backoff delay.
    /// Returns whether the connection loop should attempt to connect again.
    /// </summary>
    private async Task<bool> HandleConnectionLostAsync(ConnectionLostEvent connectionLost)
    {
        ConnectionTroubleKind troubleKind = ConnectionTroublePolicy.Classify(connectionLost);

        // A superseded session is not retried (two tabs reconnecting would kick each other for as long as both
        // are open), and neither is a terminal error.
        if (!ConnectionTroublePolicy.MayRetryAutomatically(troubleKind) || IsTerminalError(connectionLost))
        {
            Console.WriteLine($"[{GetType().Name}] {troubleKind} ({connectionLost.TechnicalError?.GetType().Name}), not reconnecting");
            _connectionStatus = ConnectionStatus.Error;
            _errorMessage = connectionLost.EnglishLocalizedReason;
            _reconnectAttempts = 0;
            SetSessionTrouble(troubleKind, isRetrying: false);
            return false;
        }

        if (_reconnectAttempts >= MaxReconnectAttempts)
        {
            Console.WriteLine($"[{GetType().Name}] Max reconnect attempts reached, giving up");
            _connectionStatus = ConnectionStatus.Error;
            _errorMessage = "Failed to reconnect after multiple attempts";
            SetSessionTrouble(troubleKind, isRetrying: false);
            return false;
        }

        int delayMs = Math.Min(BaseReconnectDelayMs * (1 << _reconnectAttempts), MaxReconnectDelayMs);
        _reconnectAttempts++;

        Console.WriteLine($"[{GetType().Name}] Reconnect attempt {_reconnectAttempts} in {delayMs}ms");

        _connectionStatus = ConnectionStatus.Connecting;
        _errorMessage = null;
        SetSessionTrouble(troubleKind, isRetrying: true);

        await DelayBeforeReconnectAsync(delayMs);

        return _running;
    }

    /// <summary>
    /// Wait out the backoff delay, or until <see cref="RetryNow"/> cuts it short.
    /// </summary>
    private async Task DelayBeforeReconnectAsync(int delayMs)
    {
        TaskCompletionSource<bool> wake = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _retryWake = wake;
        try
        {
            await Task.WhenAny(Task.Delay(delayMs), wake.Task);
        }
        finally
        {
            _retryWake = null;
        }
    }

    /// <summary>Record what the last session loss was and re-render the shell.</summary>
    private void SetSessionTrouble(ConnectionTroubleKind kind, bool isRetrying)
    {
        _sessionTroubleKind = kind;
        _sessionTroubleRetrying = isRetrying;
        _notifiedTroubleKind = kind;
        _notifiedTroubleQuiet = Trouble.IsQuiet;
        NotifyStateChanged();
    }

    /// <summary>
    /// Whether the connection error is terminal (not retried). A maintenance break is reported as terminal but
    /// is waited out.
    /// </summary>
    private static bool IsTerminalError(ConnectionLostEvent connectionLost)
    {
        if (connectionLost.TechnicalError is TerminalError.InMaintenance)
            return false;

        return connectionLost.TechnicalError is TerminalError;
    }

    /// <summary>
    /// Notify listeners that state has changed.
    /// </summary>
    protected void NotifyStateChanged()
    {
        OnStateChanged?.Invoke();
    }

    public void Dispose() => StopLoops();

    /// <summary>
    /// Stop the frame pump and retire the connection loop, whose superseded generation makes it exit. Disposing
    /// the client closes the connection, stops the SDK and cancels whatever the loop is awaiting.
    /// </summary>
    private void StopLoops()
    {
        _running = false;
        _connectionLoopGeneration++;
        _connectionLoopActive = false;
        _retryWake?.TrySetResult(true);
        _client?.Dispose();
        _client = null;
        _session = null;
    }
}
