using Metaplay.Client;
using Metaplay.Core;
using Metaplay.Core.Client;
using Metaplay.Core.GameEngineIntegration;
using Metaplay.Core.Message;
using Metaplay.Core.Model;
using Metaplay.Core.Player;
using Metaplay.Core.Session;
using Metaplay.Core.Session.ConnectionStates;
using Metaplay.Unity;
using System.Diagnostics;

namespace WebClientBase.Services;

/// <summary>
/// Abstract base class for Metaplay client services.
/// Handles the connection lifecycle, reconnecting after a loss as the SDK recommends, and the per-frame UI updates.
/// Game-specific implementations should inherit from this class.
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

    /// <summary>The loss waiting for the player to reconnect (<see cref="ConnectionLost"/>).</summary>
    private ConnectionLostEvent? _connectionLost;

    /// <summary>Whether <see cref="Start"/> has started the per-frame work and the connection loop.</summary>
    private bool _started;

    /// <summary>Whether <see cref="Reset"/> or <see cref="Dispose"/> has stopped the loops. A stopped service stays stopped.</summary>
    private bool _stopped;

    /// <summary>
    /// Completing this ends the connection loop's wait for the player to reconnect after a loss. Non-null only during
    /// that wait.
    /// </summary>
    private TaskCompletionSource? _reconnectSignal;

    /// <summary>The reconnects in a row that the client has made on its own (<see cref="MaxAutoReconnects"/>).</summary>
    private int _autoReconnects;

    /// <summary>A session that stays connected this long ends a run of reconnects in a row.</summary>
    private static readonly TimeSpan StableSessionDuration = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Whether a render was requested since listeners were last told, when <see cref="RenderEveryFrame"/> is false.
    /// Each frame raises <see cref="OnStateChanged"/> once for however many requests it made.
    /// </summary>
    private bool _renderRequested;

    /// <summary>
    /// Delay in milliseconds before reconnecting on the client's own, so a server that keeps refusing for a reason
    /// the SDK reconnects after, such as one that is starting up, is not retried in a tight loop. Each reconnect in a
    /// row waits twice as long as the one before, up to 32 times this.
    /// </summary>
    protected virtual int AutoReconnectDelayMs => 1000;

    /// <summary>
    /// How many times in a row the client reconnects on its own before it waits for the player instead. A loss that
    /// happens again in each session, such as from an action that throws for this player, then reaches the player
    /// rather than reconnecting forever. The count starts again when a session stays connected for 30 seconds, and
    /// when the player reconnects.
    /// </summary>
    protected virtual int MaxAutoReconnects => 3;

    /// <summary>
    /// Whether <see cref="OnStateChanged"/> is raised on every frame while connected, so the UI renders again on each
    /// of the browser's animation frames whatever changed: the player model, or anything that changes with time
    /// alone, such as an animation or a clock. This is the default.
    /// <para>
    /// A game with little that changes on screen can override it to return false. <see cref="OnStateChanged"/> is
    /// then raised only when the connection status changes, and at the end of a frame in which a client action ran
    /// (<see cref="ExecuteAction"/>) or the game called <see cref="RequestRender"/>. Any other change the UI shows must
    /// then reach it through <see cref="RequestRender"/>: a change a model makes outside a client action, in
    /// <c>GameTick</c> or in a server action, typically from a model client listener, for the player model or any
    /// other entity's model the game shows; and for a change with time alone, a timer of the game's own.
    /// </para>
    /// </summary>
    protected virtual bool RenderEveryFrame => true;

    /// <summary>
    /// Event fired when the connection status changes, and while connected on every frame, or with
    /// <see cref="RenderEveryFrame"/> false, on a frame that requested a render.
    /// </summary>
    public event Action? OnStateChanged;

    /// <summary>
    /// Event fired on each frame, between the SDK's frame start and end, for the game's own per-frame work, such as
    /// advancing what a page shows with time. A routed page gets it as <c>GamePageBase.OnFrame</c>.
    /// </summary>
    public event Action? OnFrame;

    /// <summary>
    /// Current connection state.
    /// </summary>
    public ConnectionStatus ConnectionStatus => _connectionStatus;

    /// <summary>
    /// The connection loss waiting for the player to reconnect (<see cref="Reconnect"/>), for the connection overlay to
    /// show; null while connected or connecting.
    /// </summary>
    public ConnectionLostEvent? ConnectionLost => _connectionLost;

    /// <summary>
    /// The current session, or null before the first session has started. Remains available after a
    /// session ends, so the UI can keep rendering the last known state while reconnecting.
    /// </summary>
    protected MetaplaySession? Session => _session;

    /// <summary>
    /// The current player model, or null if no session has started.
    /// </summary>
    public TPlayerModel? PlayerModel => _session?.PlayerContext?.Model as TPlayerModel;

    /// <summary>
    /// Player ID if connected, otherwise null.
    /// </summary>
    public EntityId? PlayerId => PlayerModel?.PlayerId;

    /// <summary>
    /// Whether the session is running but the SDK reports its connection as unhealthy: the connection is degraded and
    /// the SDK tries to restore it. If it does not succeed, the connection is lost and the session ends.
    /// </summary>
    public bool IsUnhealthy { get; private set; }

    protected MetaplayClientServiceBase()
    {
        EnsureCoreInitialized();
    }

    /// <summary>
    /// Initialize MetaplayCore. Override to customize initialization.
    /// </summary>
    protected virtual void EnsureCoreInitialized()
    {
        lock (_initLock)
        {
            if (!_coreInitialized)
            {
                // Set the integration roots before init. The platform-specific behavior (loading the pre-built
                // serializer assembly by name rather than generating one, and the WebSocket transport) follows
                // from the browser build of the SDK.
                MetaplayCore.ClientIntegrationAssemblies = IntegrationAssembly.FindRoots().ToList();
                MetaplayCore.InitializeForClient();
                _coreInitialized = true;
            }
        }
    }

    /// <summary>
    /// Create the Metaplay client, via <see cref="MetaplayClient.Create"/>. Implementations pass the
    /// game's delegates and any additional entity sub-clients.
    /// </summary>
    protected abstract IMetaplayClient CreateClient();

    /// <summary>
    /// Start the SDK's frames (<see cref="BrowserFrameLoop"/>), with the service's per-frame work, and the connection
    /// loop, which connects to the Metaplay server and runs until <see cref="Reset"/> or <see cref="Dispose"/>. Later
    /// calls do nothing. After a loss, the loop reconnects on its own when the SDK recommends it, and otherwise waits
    /// for <see cref="Reconnect"/>. The connection's progress reaches the UI through <see cref="OnStateChanged"/>.
    /// </summary>
    public void Start()
    {
        if (_started || _stopped)
            return;

        _started = true;
        _connectionStatus = ConnectionStatus.Connecting;
        NotifyStateChanged();

        _client ??= CreateClient();

        BrowserFrameLoop.Start(RunFrame);
        _ = RunConnectionLoopAsync();
    }

    /// <summary>
    /// Reconnect after the loss in <see cref="ConnectionLost"/>, at the player's request, such as to take the session
    /// back from another tab.
    /// </summary>
    public void Reconnect()
    {
        _reconnectSignal?.TrySetResult();
    }

    /// <summary>
    /// Execute a player action. It runs at once, between frames on the browser's one thread, and the SDK sends it at
    /// the start of the next frame. Returns the action's result, or null if there is no session or the action threw.
    /// </summary>
    public virtual MetaActionResult? ExecuteAction(PlayerActionBase action)
    {
        MetaActionResult? result = null;
        try
        {
            result = _session?.PlayerContext?.ExecuteAction(action);
        }
        catch (UnhandledModelExecutionException ex)
        {
            // The action threw, and the SDK has ended the session over it, which the connection overlay shows. Left
            // to escape, the exception would also bring up Blazor's error banner.
            Console.WriteLine($"[{GetType().Name}] Action {action.GetType().Name} failed: {ex.Message}");
        }
        _renderRequested = true;
        return result;
    }

    /// <summary>
    /// Called when a session has started, before the SDK is told the start completed. Override to wire up
    /// model listeners and load game content.
    /// </summary>
    protected virtual void OnSessionStarted(MetaplaySession session)
    {
    }

    /// <summary>
    /// Called when a started session has ended, before the client waits to reconnect. Override to drop what belonged
    /// to the session, such as requests whose responses will not arrive.
    /// </summary>
    protected virtual void OnSessionEnded(ConnectionLostEvent connectionLost)
    {
    }

    /// <summary>
    /// Reset the client: disconnect, and delete the credentials and the offline player. The service stays stopped,
    /// so reload the page afterwards to start as a new player. Returns true if reset was successful.
    /// </summary>
    public bool Reset()
    {
        try
        {
            // Disposing the client saves the offline player, so the blobs are deleted after it.
            Stop();

            // In offline mode the player lives in the saved offline state, so deleting only the credentials would
            // bring the old player back.
            TryDeleteBlob(GetCredentialsPath(), "Credential");
            TryDeleteBlob(DefaultOfflineServer.GetPersistedStatePath(), "Offline state");

            _connectionStatus = ConnectionStatus.Disconnected;
            _connectionLost = null;
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

    /// <summary>Delete one blob. A missing or inaccessible blob must not fail the reset.</summary>
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
    /// Get the path to the credentials blob. On the browser this is a key into the localStorage blob store
    /// rather than a file system path.
    /// </summary>
    protected virtual string GetCredentialsPath()
    {
        string dataPath = IntegrationRegistry.Get<IGameEngineIntegration>().PersistentDataPath;
        return Path.Combine(dataPath, "MetaplayCredentials.dat");
    }

    /// <summary>
    /// The work of each frame, between the SDK's frame start and end: the game's own (<see cref="OnFrame"/>), then the
    /// service's.
    /// </summary>
    private void RunFrame()
    {
        // A game handler that throws does not stop the service's work; BrowserFrameLoop logs the exception.
        try
        {
            OnFrame?.Invoke();
        }
        finally
        {
            bool connected = _connectionStatus == ConnectionStatus.Connected;
            bool unhealthy = connected && MetaplaySDK.Connection?.State is Connected { IsHealthy: false };
            bool healthChanged = unhealthy != IsUnhealthy;
            IsUnhealthy = unhealthy;

            if (healthChanged || (connected && (RenderEveryFrame || _renderRequested)))
                NotifyStateChanged();
        }
    }

    /// <summary>
    /// Connect, run the session until it ends, wait before reconnecting, and repeat until the service is stopped. The
    /// connection only makes progress while frames run, so the loop and the frames interleave on the browser thread.
    /// </summary>
    private async Task RunConnectionLoopAsync()
    {
        while (!_stopped)
        {
            ConnectionLostEvent? connectionLost;
            try
            {
                connectionLost = await RunSessionAsync();
            }
            catch (Exception ex)
            {
                // Nothing observes this loop, so an exception escaping it would end connecting for good, silently. It
                // ends the session instead, which the player can reconnect after.
                Console.WriteLine($"[{GetType().Name}] Session failed: {ex}");
                connectionLost = ConnectionLostEvent.FromConnectionError(
                    new TerminalError.ClientSideConnectionError(ex, nameof(RunSessionAsync)),
                    ConnectionStatistics.ForNewSession());
            }

            if (connectionLost == null || _stopped)
                return;

            await WaitBeforeReconnectAsync(connectionLost);
        }
    }

    /// <summary>
    /// Connect and run one session until it ends. Returns why it ended, or null when the service was stopped.
    /// </summary>
    private async Task<ConnectionLostEvent?> RunSessionAsync()
    {
        MetaplaySession session;
        try
        {
            session = await _client!.ConnectAsync();
        }
        catch (FailedToStartSessionException ex)
        {
            Console.WriteLine($"[{GetType().Name}] Failed to start session: {ex.Failure.TechnicalError?.GetType().Name} - {ex.Failure.EnglishLocalizedReason}");
            return ex.Failure;
        }
        catch (OperationCanceledException)
        {
            // The client was disposed (Reset), so there is nothing left to connect.
            return null;
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
            return session.SessionStartFailed(ex);
        }

        _connectionStatus = ConnectionStatus.Connected;
        NotifyStateChanged();
        long connectedAt = Stopwatch.GetTimestamp();

        ConnectionLostEvent connectionLost;
        try
        {
            connectionLost = await session.WaitForSessionEndAsync();
        }
        catch (OperationCanceledException)
        {
            return null;
        }

        // A session lost soon after connecting, such as to an action that throws for this player, continues the
        // reconnects in a row.
        if (Stopwatch.GetElapsedTime(connectedAt) >= StableSessionDuration)
            _autoReconnects = 0;

        Console.WriteLine($"[{GetType().Name}] Session ended: {connectionLost.TechnicalError?.GetType().Name} - {connectionLost.EnglishLocalizedReason}");
        OnSessionEnded(connectionLost);
        return connectionLost;
    }

    /// <summary>
    /// Wait before the next connection: a moment when the client reconnects on its own
    /// (<see cref="ShouldReconnectAutomatically"/>, at most <see cref="MaxAutoReconnects"/> times in a row), or
    /// otherwise until the player reconnects (<see cref="Reconnect"/>), with the loss in <see cref="ConnectionLost"/>
    /// meanwhile.
    /// </summary>
    private async Task WaitBeforeReconnectAsync(ConnectionLostEvent connectionLost)
    {
        if (ShouldReconnectAutomatically(connectionLost) && _autoReconnects < MaxAutoReconnects)
        {
            int delayMs = AutoReconnectDelayMs << Math.Min(_autoReconnects, 5);
            _autoReconnects++;
            Console.WriteLine($"[{GetType().Name}] Reconnecting in {delayMs}ms");
            _connectionStatus = ConnectionStatus.Connecting;
            NotifyStateChanged();
            await Task.Delay(delayMs);
            return;
        }

        Console.WriteLine($"[{GetType().Name}] Waiting for the player to reconnect ({connectionLost.Reason})");
        _connectionStatus = ConnectionStatus.Error;
        _connectionLost = connectionLost;
        NotifyStateChanged();

        TaskCompletionSource reconnectSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _reconnectSignal = reconnectSignal;
        try
        {
            await reconnectSignal.Task;
        }
        finally
        {
            _reconnectSignal = null;
        }

        if (_stopped)
            return;

        _autoReconnects = 0;
        _connectionStatus = ConnectionStatus.Connecting;
        _connectionLost = null;
        NotifyStateChanged();
    }

    /// <summary>
    /// Whether to reconnect after a connection loss without asking the player. The default follows the SDK's
    /// <see cref="ConnectionLostEvent.AutoReconnectRecommended"/>, which is true for the losses a reconnect quietly
    /// fixes, such as an admin kick or a server that is starting up.
    /// </summary>
    protected virtual bool ShouldReconnectAutomatically(ConnectionLostEvent connectionLost) =>
        connectionLost.AutoReconnectRecommended;

    /// <summary>
    /// Notify listeners now that state has changed. For a change a model makes, use <see cref="RequestRender"/>.
    /// </summary>
    protected void NotifyStateChanged()
    {
        _renderRequested = false;
        OnStateChanged?.Invoke();
    }

    /// <summary>
    /// Ask for the UI to render again, for the game's model listeners to call on a change to any model it shows. With
    /// <see cref="RenderEveryFrame"/> false, listeners are told once at the end of the frame, however many requests it
    /// made, and after the action or tick that made the change has finished; with the default, every frame tells them
    /// anyway.
    /// </summary>
    protected void RequestRender()
    {
        _renderRequested = true;
    }

    /// <summary>
    /// Stop the service's per-frame work and the connection loop, and dispose the client. Disposing the client closes
    /// the connection, stops the SDK, and cancels the client's ConnectAsync or WaitForSessionEndAsync that the loop is
    /// awaiting.
    /// </summary>
    private void Stop()
    {
        _stopped = true;
        _reconnectSignal?.TrySetResult();
        _client?.Dispose();
        _client = null;
        _session = null;
    }

    public void Dispose() => Stop();
}
