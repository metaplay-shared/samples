using Metaplay.Client;
using Metaplay.Core;
using Metaplay.Core.Client;
using Metaplay.Core.Model;
using Metaplay.Core.Player;
using Metaplay.Core.Session;
using Metaplay.Core.Session.ConnectionStates;
using Metaplay.Unity;

namespace WebClientBase.Services;

/// <summary>
/// Base class for the game's Metaplay client service. Runs the connection loop, reconnects with exponential
/// backoff, and runs the SDK frame loop.
/// </summary>
/// <typeparam name="TPlayerModel">The game-specific PlayerModel type.</typeparam>
public abstract class MetaplayClientServiceBase<TPlayerModel> : IMetaplayClientService<TPlayerModel>, IDisposable
    where TPlayerModel : class, IPlayerModelBase
{
    private static bool _coreInitialized = false;
    private static readonly object _initLock = new object();

    private IMetaplayClient? _client;
    private MetaplaySession? _session;

    private ConnectionStatus _connectionStatus = ConnectionStatus.Disconnected;
    private string? _errorMessage;
    private bool _framePumpRunning;
    private int _reconnectAttempts = 0;

    /// <summary>
    /// Whether a connection loop is running. <see cref="_framePumpRunning"/> covers the frame pump, which keeps running
    /// after the connection loop stops on running out of retries. <see cref="RetryNow"/> then starts a new loop.
    /// </summary>
    private bool _connectionLoopActive;

    /// <summary>
    /// The generation of the current connection loop. <see cref="Reset"/>, <see cref="Dispose"/> and
    /// <see cref="ConnectAsync"/> increment it, and a loop with an older generation exits instead of connecting.
    /// </summary>
    private int _connectionLoopGeneration;

    /// <summary>Completing this ends the backoff delay early. Non-null only during a backoff delay.</summary>
    private TaskCompletionSource<bool>? _skipBackoffSignal;

    /// <summary>
    /// Whether a session has ever started since startup or the last <see cref="Reset"/>. Until then,
    /// <see cref="Trouble"/> is <see cref="ConnectionTrouble.None"/> and the screens show their own connecting state.
    /// </summary>
    private bool _hasEverConnected;

    /// <summary>The kind of the last session loss, or <see cref="ConnectionTroubleKind.None"/> in a session.</summary>
    private ConnectionTroubleKind _sessionTroubleKind = ConnectionTroubleKind.None;

    /// <summary>Whether the client is still retrying after the loss in <see cref="_sessionTroubleKind"/>.</summary>
    private bool _sessionTroubleRetrying;

    /// <summary>When the SDK connection last became unhealthy, or null while it is healthy.</summary>
    private DateTime? _linkUnhealthySince;

    /// <summary>Whether the SDK connection has been unhealthy for at least <see cref="LinkStallGrace"/>.</summary>
    private bool _linkStalled;

    /// <summary>Whether the page is visible, as last reported to <see cref="NotifyPageVisibilityChanged"/>.</summary>
    private bool _pageVisible = true;

    /// <summary>When the page last became visible. The Unix epoch until the page has been hidden once.</summary>
    private DateTime _pageForegroundedAt = DateTime.UnixEpoch;

    /// <summary>The trouble kind the frame pump last raised a state change for.</summary>
    private ConnectionTroubleKind _notifiedTroubleKind = ConnectionTroubleKind.None;

    /// <summary>Whether the trouble the frame pump last raised a state change for was quiet.</summary>
    private bool _notifiedTroubleQuiet;

    /// <summary>
    /// Maximum number of reconnect attempts before the connection loop stops.
    /// </summary>
    protected virtual int MaxReconnectAttempts => 10;

    /// <summary>
    /// Delay in milliseconds before the first reconnect attempt. The delay doubles with each attempt.
    /// </summary>
    protected virtual int BaseReconnectDelayMs => 1000;

    /// <summary>
    /// Maximum delay in milliseconds before a reconnect attempt.
    /// </summary>
    protected virtual int MaxReconnectDelayMs => 30000;

    /// <summary>
    /// Delay in milliseconds between frames of the SDK frame loop.
    /// </summary>
    protected virtual int UpdateIntervalMs => 50;

    /// <summary>
    /// How long the SDK connection must be unhealthy before the shell shows it. The SDK often resumes a dropped
    /// session within this time, and a shorter value would show the modal for those short drops.
    /// </summary>
    protected virtual TimeSpan LinkStallGrace => TimeSpan.FromSeconds(2);

    /// <summary>
    /// How long after the page becomes visible a lost connection is still shown as a pill instead of the modal.
    /// It covers the time the client needs to notice the lost connection and reconnect after a tab switch.
    /// </summary>
    protected virtual TimeSpan PageShownQuietPeriod => TimeSpan.FromSeconds(10);

    /// <summary>
    /// Raised when the connection state or the player model changes. Raised on every frame while connected.
    /// </summary>
    public event Action? OnStateChanged;

    /// <summary>
    /// The current connection state.
    /// </summary>
    public ConnectionStatus ConnectionStatus => _connectionStatus;

    /// <summary>
    /// The error message from the last failed connection, or null.
    /// </summary>
    public string? ErrorMessage => _errorMessage;

    /// <summary>
    /// The current session, or null before the first session has started and after <see cref="Reset"/>. Kept
    /// after a session ends, so the UI can render the last known state while reconnecting.
    /// </summary>
    protected MetaplaySession? Session => _session;

    /// <summary>
    /// The current player model, or null if no session has started.
    /// </summary>
    public TPlayerModel? PlayerModel => _session?.PlayerContext?.Model as TPlayerModel;

    /// <summary>
    /// The player ID from <see cref="PlayerModel"/>, or null when there is no player model.
    /// </summary>
    public EntityId? PlayerId => PlayerModel?.PlayerId;

    /// <summary>
    /// True while the page is hidden or less than <see cref="PageShownQuietPeriod"/> after it became visible. A lost
    /// connection in this window is more likely caused by browser timer throttling than by an outage.
    /// </summary>
    private bool IsPageHiddenOrJustShown => !_pageVisible || DateTime.UtcNow - _pageForegroundedAt < PageShownQuietPeriod;

    /// <summary>
    /// What is wrong with the connection. A session loss takes precedence over an unhealthy SDK connection,
    /// because the session loss has a specific kind.
    /// </summary>
    public ConnectionTrouble Trouble
    {
        get
        {
            if (!_hasEverConnected)
                return ConnectionTrouble.None;

            if (_sessionTroubleKind != ConnectionTroubleKind.None)
            {
                bool quiet = _sessionTroubleKind == ConnectionTroubleKind.LinkLost && _sessionTroubleRetrying && IsPageHiddenOrJustShown;
                return new ConnectionTrouble(_sessionTroubleKind, _sessionTroubleRetrying, quiet, _reconnectAttempts, MaxReconnectAttempts);
            }

            // The SDK connection is unhealthy while the SDK tries to resume the session. No player action reaches
            // the server, so the shell shows it now instead of when the SDK gives up on the resume.
            if (_linkStalled)
                return new ConnectionTrouble(ConnectionTroubleKind.LinkLost, isRetrying: true, isQuiet: IsPageHiddenOrJustShown, attemptNumber: 0, maxAttempts: MaxReconnectAttempts);

            return ConnectionTrouble.None;
        }
    }

    protected MetaplayClientServiceBase()
    {
        EnsureCoreInitialized();
    }

    /// <summary>
    /// Initializes MetaplayCore once per process. Override to change the initialization.
    /// </summary>
    protected virtual void EnsureCoreInitialized()
    {
        lock (_initLock)
        {
            if (!_coreInitialized)
            {
                // The integration roots must be set before initialization. The SDK's browser build selects the
                // pre-built serializer assembly and the WebSocket transport by itself.
                MetaplayCore.ClientIntegrationAssemblies = IntegrationAssembly.FindRoots().ToList();
                MetaplayCore.InitializeForClient();
                _coreInitialized = true;
            }
        }
    }

    /// <summary>
    /// Creates the Metaplay client with <see cref="MetaplayClient.Create"/>, passing the game's delegates and
    /// any additional entity sub-clients.
    /// </summary>
    protected abstract IMetaplayClient CreateClient();

    /// <summary>
    /// Initializes the client and connects to the Metaplay server. Starts the frame pump on the first call, and
    /// starts the connection loop when none is running. Does nothing while a connection loop is running.
    /// <para>
    /// Virtual so that a host that renders the app's components without a game server, such as a component
    /// gallery, can override it to return a completed task and open no connection. Screens call this method
    /// when they render.
    /// </para>
    /// </summary>
    public virtual Task ConnectAsync()
    {
        if (_connectionLoopActive)
            return Task.CompletedTask;

        _connectionStatus = ConnectionStatus.Connecting;
        _errorMessage = null;
        _reconnectAttempts = 0;
        ClearSessionTrouble();
        NotifyStateChanged();

        _client ??= CreateClient();

        if (!_framePumpRunning)
        {
            _framePumpRunning = true;
            _ = RunFramePumpAsync();
        }

        _connectionLoopActive = true;
        _connectionLoopGeneration++;
        _ = RunConnectionLoopAsync(_connectionLoopGeneration);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Reconnects now. Ends the current backoff delay early, or starts the connection loop again if it has
    /// stopped. Also resets the attempt count.
    /// </summary>
    public void RetryNow()
    {
        _reconnectAttempts = 0;

        TaskCompletionSource<bool>? skipBackoffSignal = _skipBackoffSignal;
        if (skipBackoffSignal != null)
        {
            skipBackoffSignal.TrySetResult(true);
            return;
        }

        // No backoff delay is running. Either a connection attempt is in progress, and ConnectAsync does nothing,
        // or the loop has stopped, and ConnectAsync starts it again.
        _ = ConnectAsync();
    }

    /// <summary>
    /// Records whether the page is visible. When the page becomes visible during a reconnect, retries at once,
    /// because the browser throttled the backoff timer while the tab was hidden and the remaining delay is not
    /// related to the server's state.
    /// </summary>
    public void NotifyPageVisibilityChanged(bool isVisible)
    {
        if (isVisible == _pageVisible)
            return;

        _pageVisible = isVisible;

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
    /// Called when a session has started, before <c>SessionStartComplete</c> is called. Override to add model
    /// listeners and load game content. An exception here fails the session start.
    /// </summary>
    protected virtual void OnSessionStarted(MetaplaySession session)
    {
    }

    /// <summary>
    /// Called when a session has ended, before any reconnect. Server replies to requests sent in that session
    /// will not arrive. The SDK resuming a session does not end it, so this is not called for a resume.
    /// </summary>
    protected virtual void OnSessionEnded()
    {
    }

    /// <summary>
    /// Resets the client: disconnects, deletes the credentials and resets the state. Also stops the frame pump,
    /// so call <see cref="ConnectAsync"/> or reload the page afterwards. Returns true if the reset succeeded.
    /// </summary>
    public bool Reset()
    {
        try
        {
            StopConnectionLoop();

            // A missing or inaccessible credentials blob must not fail the reset.
            try
            {
                AtomicBlobStore.TryDeleteBlob(GetCredentialsPath());
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{GetType().Name}] Credential cleanup skipped: {ex.Message}");
            }

            _connectionStatus = ConnectionStatus.Disconnected;
            _errorMessage = null;
            _hasEverConnected = false;
            ClearSessionTrouble();
            ClearLinkHealth();
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

    /// <summary>
    /// Returns the path of the credentials blob. In the browser, the path is a key into the SDK's
    /// <c>AtomicBlobStore</c>, which stores the blob in <c>localStorage</c>.
    /// </summary>
    protected virtual string GetCredentialsPath()
    {
        // The SDK integration that owns this path is internal, so the deprecated accessor is the only public way
        // to read it.
        #pragma warning disable CS0618
        return Path.Combine(MetaplaySDK.PersistentDataPath, "MetaplayCredentials.dat");
        #pragma warning restore CS0618
    }

    /// <summary>
    /// Runs the SDK frame loop on the browser thread. WebAssembly is single-threaded, so instead of the SDK's
    /// thread-based frame loop, this method awaits a delay between frames. The await lets the WebSocket
    /// transport's continuations and the connection loop run. Frames cannot overlap on one thread, so no
    /// re-entrancy guard is needed.
    /// </summary>
    private async Task RunFramePumpAsync()
    {
        while (_framePumpRunning)
        {
            try
            {
                FrameLoop.RunFrame();

                UpdateLinkHealth();

                if (_connectionStatus == ConnectionStatus.Connected)
                {
                    NotifyStateChanged();
                }
                else
                {
                    // Without a live session nothing else raises OnStateChanged, so raise it when the shell's
                    // content changes.
                    NotifyTroubleChangedIfNeeded();
                }
            }
            catch (Exception)
            {
                // Ignore errors from a single frame, for example during shutdown or a reconnect.
            }

            await Task.Delay(UpdateIntervalMs);
        }
    }

    /// <summary>
    /// Updates <see cref="_linkStalled"/> from the SDK connection's health. When the socket closes during a
    /// session, the SDK keeps the connection in the <c>Connected</c> state while it tries to resume the session.
    /// Player actions in that time do not reach the server, so the shell must show it before the session ends.
    /// <para>
    /// The SDK's QoS monitor clears <c>Connected.IsHealthy</c> when the transport is detached, when the
    /// connection is lost after the handshake, and while a session resume has no answer.
    /// </para>
    /// </summary>
    private void UpdateLinkHealth()
    {
        if (!_hasEverConnected)
            return;

        Metaplay.Core.Session.ConnectionState? sdkState = MetaplaySDK.Connection?.State;
        if (sdkState is Connected connected && connected.IsHealthy)
        {
            ClearLinkHealth();
            return;
        }

        _linkUnhealthySince ??= DateTime.UtcNow;
        _linkStalled = DateTime.UtcNow - _linkUnhealthySince.Value >= LinkStallGrace;
    }

    /// <summary>Raises <see cref="OnStateChanged"/> when <see cref="Trouble"/>'s kind or quiet flag changed.</summary>
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
    /// Connects, waits for the session to end, then reconnects with exponential backoff. The connection only
    /// makes progress while the frame pump runs, and both loops run on the browser thread.
    /// </summary>
    private async Task RunConnectionLoopAsync(int generation)
    {
        try
        {
            while (_framePumpRunning && generation == _connectionLoopGeneration)
            {
                MetaplaySession session;
                try
                {
                    session = await _client!.ConnectAsync();
                }
                catch (FailedToStartSessionException ex)
                {
                    Console.WriteLine($"[{GetType().Name}] Failed to start session: {ex.Failure.TechnicalError?.GetType().Name} - {ex.Failure.EnglishLocalizedReason}");
                    if (await WaitToReconnectAfterLossAsync(ex.Failure))
                        continue;
                    return;
                }
                catch (OperationCanceledException)
                {
                    // Reset or Dispose disposed the client.
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
                    if (await WaitToReconnectAfterLossAsync(startFailure))
                        continue;
                    return;
                }

                _connectionStatus = ConnectionStatus.Connected;
                _errorMessage = null;
                _reconnectAttempts = 0;
                _hasEverConnected = true;
                ClearSessionTrouble();
                ClearLinkHealth();
                NotifyStateChanged();

                ConnectionLostEvent connectionLost;
                try
                {
                    connectionLost = await session.WaitForSessionEndAsync();
                }
                catch (OperationCanceledException)
                {
                    OnSessionEnded();
                    return;
                }

                OnSessionEnded();

                Console.WriteLine($"[{GetType().Name}] Session ended: {connectionLost.TechnicalError?.GetType().Name} - {connectionLost.EnglishLocalizedReason}");
                if (!await WaitToReconnectAfterLossAsync(connectionLost))
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
    /// Classifies a connection loss and, when the client may retry, waits for the backoff delay.
    /// Returns whether the connection loop should connect again.
    /// </summary>
    private async Task<bool> WaitToReconnectAfterLossAsync(ConnectionLostEvent connectionLost)
    {
        ConnectionTroubleKind troubleKind = ConnectionTroublePolicy.Classify(connectionLost);

        // The client does not retry a session superseded by another tab or a terminal error
        // (ConnectionTroublePolicy.MayRetryAutomatically). The loop stops until the player retries.
        if (!ConnectionTroublePolicy.MayRetryAutomatically(connectionLost))
        {
            Console.WriteLine($"[{GetType().Name}] Not reconnecting: {troubleKind}, {connectionLost.TechnicalError?.GetType().Name ?? "no technical error"}");
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

        return _framePumpRunning;
    }

    /// <summary>
    /// Waits for the backoff delay, or until <see cref="RetryNow"/> ends it early.
    /// </summary>
    private async Task DelayBeforeReconnectAsync(int delayMs)
    {
        TaskCompletionSource<bool> skipBackoffSignal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _skipBackoffSignal = skipBackoffSignal;
        try
        {
            await Task.WhenAny(Task.Delay(delayMs), skipBackoffSignal.Task);
        }
        finally
        {
            _skipBackoffSignal = null;
        }
    }

    /// <summary>Clears the recorded session loss.</summary>
    private void ClearSessionTrouble()
    {
        _sessionTroubleKind = ConnectionTroubleKind.None;
        _sessionTroubleRetrying = false;
    }

    /// <summary>Marks the SDK connection as healthy, clearing <see cref="_linkStalled"/> and its timer.</summary>
    private void ClearLinkHealth()
    {
        _linkUnhealthySince = null;
        _linkStalled = false;
    }

    /// <summary>Records the last session loss and raises <see cref="OnStateChanged"/>.</summary>
    private void SetSessionTrouble(ConnectionTroubleKind kind, bool isRetrying)
    {
        _sessionTroubleKind = kind;
        _sessionTroubleRetrying = isRetrying;
        _notifiedTroubleKind = kind;
        _notifiedTroubleQuiet = Trouble.IsQuiet;
        NotifyStateChanged();
    }

    /// <summary>
    /// Raises <see cref="OnStateChanged"/>.
    /// </summary>
    protected void NotifyStateChanged()
    {
        OnStateChanged?.Invoke();
    }

    /// <summary>
    /// Stops the frame pump and the connection loop, and disposes the client. Shared by <see cref="Reset"/> and
    /// <see cref="Dispose"/>.
    /// </summary>
    private void StopConnectionLoop()
    {
        _framePumpRunning = false;

        // Increment the generation so the running connection loop exits instead of reconnecting.
        _connectionLoopGeneration++;
        _connectionLoopActive = false;
        _skipBackoffSignal?.TrySetResult(true);

        // Disposing the client closes the connection, stops the SDK, and cancels the ConnectAsync or
        // WaitForSessionEndAsync call that the connection loop is awaiting.
        _client?.Dispose();
        _client = null;
        _session = null;
    }

    public void Dispose() => StopConnectionLoop();
}
