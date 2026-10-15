using Metaplay.Client;
using Metaplay.Core;
using Metaplay.Core.Client;
using Metaplay.Core.Message;
using Metaplay.Core.Model;
using Metaplay.Core.Player;
using Metaplay.Core.Session;
using Metaplay.Core.Session.ConnectionStates;
using Metaplay.Unity;

namespace WebClientBase.Services;

/// <summary>
/// Abstract base class for Metaplay client services.
/// Handles connection lifecycle, reconnection with exponential backoff, and the frame pump.
/// Game-specific implementations should inherit from this class.
/// </summary>
/// <typeparam name="TPlayerModel">The game-specific PlayerModel type.</typeparam>
public abstract class MetaplayClientServiceBase<TPlayerModel> : IMetaplayConnectionService, IDisposable
    where TPlayerModel : class, IPlayerModelBase
{
    private static bool _coreInitialized = false;
    private static readonly object _initLock = new object();

    private IMetaplayClient? _client;
    private MetaplaySession? _session;

    private ConnectionState _connectionState = ConnectionState.Disconnected;
    private string? _errorMessage;
    private bool _running;
    private int _reconnectAttempts = 0;

    /// <summary>
    /// Maximum number of reconnection attempts before giving up.
    /// </summary>
    protected virtual int MaxReconnectAttempts => 10;

    /// <summary>
    /// Base delay in milliseconds for reconnection attempts (used with exponential backoff).
    /// </summary>
    protected virtual int BaseReconnectDelayMs => 1000;

    /// <summary>
    /// Maximum delay in milliseconds for reconnection attempts.
    /// </summary>
    protected virtual int MaxReconnectDelayMs => 30000;

    /// <summary>
    /// Interval in milliseconds between frames of the SDK frame pump.
    /// </summary>
    protected virtual int UpdateIntervalMs => 50;

    /// <summary>
    /// Event fired when the connection state changes and when the player model changes: an action ran on it,
    /// whether the client's own or one the server sent, or a new model took its place. It is not raised on
    /// frames that changed neither, so anything that changes with time alone needs a trigger of its own.
    /// </summary>
    public event Action? OnStateChanged;

    /// <summary>
    /// Whether an action has run on the player model since listeners were last told. The frame pump raises
    /// <see cref="OnStateChanged"/> once for however many ran in a frame.
    /// </summary>
    private bool _modelChanged;

    /// <summary>The player model listeners were last told about, so a model that replaced it is noticed.</summary>
    private TPlayerModel? _notifiedModel;

    /// <summary>The dispatcher the server's actions are listened for on, so they are listened for once per dispatcher.</summary>
    private IMessageDispatcher? _serverActionDispatcher;

    /// <summary>
    /// Current connection state.
    /// </summary>
    public ConnectionState ConnectionState => _connectionState;

    /// <summary>
    /// Error message if connection failed.
    /// </summary>
    public string? ErrorMessage => _errorMessage;

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
    /// Initialize and connect to the Metaplay server. Starts the frame pump and the connection loop on
    /// the first call; subsequent calls are no-ops while the loop is running.
    /// </summary>
    public Task ConnectAsync()
    {
        if (_running)
            return Task.CompletedTask;

        _running = true;
        _connectionState = ConnectionState.Connecting;
        _errorMessage = null;
        _reconnectAttempts = 0;
        NotifyStateChanged();

        _client ??= CreateClient();

        _ = RunFramePumpAsync();
        _ = RunConnectionLoopAsync();

        return Task.CompletedTask;
    }

    /// <summary>
    /// Execute a player action. Enqueues the action to run on the main thread so it does not interleave
    /// with the tick loop.
    /// </summary>
    public virtual void ExecuteAction(PlayerActionBase action)
    {
        MetaplaySDK.RunOnMainThreadAsync(() =>
        {
            _session?.PlayerContext?.ExecuteAction(action);
            _modelChanged = true;
        });
    }

    /// <summary>
    /// Listen for the actions the server runs on the player model, which the SDK executes on the client's
    /// copy as their messages are dispatched. Only the fact that one ran is kept; the frame pump tells
    /// listeners.
    /// </summary>
    private void ListenForServerActions()
    {
        IMessageDispatcher dispatcher = MetaplaySDK.MessageDispatcher;
        if (dispatcher == null || ReferenceEquals(dispatcher, _serverActionDispatcher))
            return;

        _serverActionDispatcher?.RemoveListener<PlayerExecuteUnsynchronizedServerAction>(OnUnsynchronizedServerAction);
        _serverActionDispatcher?.RemoveListener<PlayerEnqueueSynchronizedServerAction>(OnSynchronizedServerAction);

        dispatcher.AddListener<PlayerExecuteUnsynchronizedServerAction>(OnUnsynchronizedServerAction);
        dispatcher.AddListener<PlayerEnqueueSynchronizedServerAction>(OnSynchronizedServerAction);
        _serverActionDispatcher = dispatcher;
    }

    private void OnUnsynchronizedServerAction(PlayerExecuteUnsynchronizedServerAction message) => _modelChanged = true;

    private void OnSynchronizedServerAction(PlayerEnqueueSynchronizedServerAction message) => _modelChanged = true;

    /// <summary>
    /// Tell listeners when the player model has changed since they were last told: an action ran on it, or a
    /// different model object is the current one.
    /// </summary>
    private void NotifyIfModelChanged()
    {
        TPlayerModel? model = PlayerModel;
        if (!_modelChanged && ReferenceEquals(model, _notifiedModel))
            return;

        _modelChanged  = false;
        _notifiedModel = model;
        NotifyStateChanged();
    }

    /// <summary>
    /// Called when a session has started, before the SDK is told the start completed. Override to wire up
    /// model listeners and load game content.
    /// </summary>
    protected virtual void OnSessionStarted(MetaplaySession session)
    {
    }

    /// <summary>
    /// Reset the client: disconnect, delete credentials, and reset state.
    /// Returns true if reset was successful.
    /// </summary>
    public bool Reset()
    {
        try
        {
            _running = false;

            // Disposing the client closes the connection, stops the SDK, and cancels the pending
            // ConnectAsync / WaitForSessionEndAsync the connection loop may be awaiting.
            _client?.Dispose();
            _client = null;
            _session = null;

            // Best-effort credential cleanup: a missing or inaccessible blob must not abort the reset.
            try
            {
                AtomicBlobStore.TryDeleteBlob(GetCredentialsPath());
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{GetType().Name}] Credential cleanup skipped: {ex.Message}");
            }

            _connectionState = ConnectionState.Disconnected;
            _errorMessage = null;
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
    /// Get the path to the credentials blob. On the browser this is a key into the IndexedDB blob store
    /// rather than a file system path.
    /// </summary>
    protected virtual string GetCredentialsPath()
    {
        // The engine integration that owns this path is SDK-internal, so the deprecated accessor is the
        // only public way to reach it.
        #pragma warning disable CS0618
        return Path.Combine(MetaplaySDK.PersistentDataPath, "MetaplayCredentials.dat");
        #pragma warning restore CS0618
    }

    /// <summary>
    /// Drive the SDK frame pump from the browser's single thread, awaiting between frames so the WebSocket
    /// transport's continuations and the connection loop run in between. A frame that changed the player model
    /// is followed by one <see cref="OnStateChanged"/>.
    /// </summary>
    private async Task RunFramePumpAsync()
    {
        while (_running)
        {
            try
            {
                FrameLoop.RunFrame();

                if (_connectionState == ConnectionState.Connected)
                {
                    NotifyIfModelChanged();
                }
            }
            catch (Exception ex)
            {
                // A failed frame (e.g. during shutdown / reconnect) is logged and the pump carries on.
                Console.WriteLine($"[{GetType().Name}] Frame failed: {ex}");
            }

            await Task.Delay(UpdateIntervalMs);
        }
    }

    /// <summary>
    /// Connect, run the session until it ends, then reconnect with exponential backoff. The connection only
    /// makes progress while the frame pump runs, so the two loops interleave on the browser thread.
    /// </summary>
    private async Task RunConnectionLoopAsync()
    {
        while (_running)
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
                ListenForServerActions();
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

            _connectionState = ConnectionState.Connected;
            _errorMessage = null;
            _reconnectAttempts = 0;
            _modelChanged = false;
            _notifiedModel = PlayerModel;
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

    /// <summary>
    /// Classify a connection loss and, when it is worth retrying, wait out the backoff delay.
    /// Returns whether the connection loop should attempt to connect again.
    /// </summary>
    private async Task<bool> HandleConnectionLostAsync(ConnectionLostEvent connectionLost)
    {
        if (!ShouldReconnect(connectionLost))
        {
            Console.WriteLine($"[{GetType().Name}] Terminal error, not reconnecting");
            _connectionState = ConnectionState.Error;
            _errorMessage = connectionLost.EnglishLocalizedReason;
            _reconnectAttempts = 0;
            NotifyStateChanged();
            return false;
        }

        if (_reconnectAttempts >= MaxReconnectAttempts)
        {
            Console.WriteLine($"[{GetType().Name}] Max reconnect attempts reached, giving up");
            _connectionState = ConnectionState.Error;
            _errorMessage = "Failed to reconnect after multiple attempts";
            _reconnectAttempts = 0;
            NotifyStateChanged();
            return false;
        }

        int delayMs = Math.Min(BaseReconnectDelayMs * (1 << _reconnectAttempts), MaxReconnectDelayMs);
        _reconnectAttempts++;

        Console.WriteLine($"[{GetType().Name}] Reconnect attempt {_reconnectAttempts} in {delayMs}ms");

        _connectionState = ConnectionState.Connecting;
        _errorMessage = null;
        NotifyStateChanged();

        await Task.Delay(delayMs);

        return _running;
    }

    /// <summary>
    /// Determine whether to reconnect after a connection loss.
    /// </summary>
    protected virtual bool ShouldReconnect(ConnectionLostEvent connectionLost)
    {
        if (connectionLost.AutoReconnectRecommended)
            return true;

        // A maintenance break is retried, whether it refused the connection or ended the session, so the
        // client rejoins when the break ends.
        if (connectionLost.TechnicalError is TerminalError.InMaintenance
            or TransientError.SessionForceTerminated { Reason: SessionForceTerminateReason.MaintenanceModeStarted })
            return true;

        // A cluster that is still starting, an admin kick and a game config update are each resolved by
        // connecting again, though the SDK does not recommend reconnecting for them.
        if (connectionLost.TechnicalError is TransientError.ClusterNotReady { Reason: TransientError.ClusterNotReady.ClusterStatus.ClusterStarting }
            or TransientError.GameConfigUpdateRequired
            or TransientError.SessionForceTerminated { Reason: SessionForceTerminateReason.KickedByAdminAction })
            return true;

        return false;
    }

    /// <summary>
    /// Notify listeners that state has changed.
    /// </summary>
    protected void NotifyStateChanged()
    {
        OnStateChanged?.Invoke();
    }

    public void Dispose()
    {
        _running = false;
        _client?.Dispose();
        _client = null;
        _session = null;
    }
}
