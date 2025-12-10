// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Game.Logic;
using Game.Logic.League;
using Metaplay.Core;
using Metaplay.Core.Analytics;
using Metaplay.Core.Client;
using Metaplay.Core.Config;
using Metaplay.Core.Guild;
using Metaplay.Core.InAppPurchase;
using Metaplay.Core.InGameMail;
using Metaplay.Core.League.Player;
using Metaplay.Core.LiveOpsEvent;
using Metaplay.Core.Message;
using Metaplay.Core.Model;
using Metaplay.Core.Player;
using Metaplay.Unity;
using Metaplay.Unity.DefaultIntegration;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Game.Logic.TypeCodes;
using Metaplay.Core.League;
#if UNITY_STANDALONE
using Steamworks;
#endif
using UnityEngine;
using UnityEngine.SceneManagement;
using System.Threading.Tasks;
using EntityId = Metaplay.Core.EntityId;

public class MetaplayClient : MetaplayClientBase<PlayerModel>
{
    public static IdlerLeagueClient IdlerLeagueClient => ClientStore.TryGetClient<IdlerLeagueClient>(ClientSlotGame.IdlerLeague);
    public static LeagueClient<IdlerPvPDivisionModel> PvpLeagueClient => ClientStore.TryGetClient<
        LeagueClient<IdlerPvPDivisionModel>>(ClientSlotGame.IdlerPvPLeague);
    public static PartyClient PartyClient => ClientStore.TryGetClient<PartyClient>(ClientSlotGame.Party);
}

/// <summary>
/// Manages the application's lifecycle, including mock loading state, Metaplay server connectivity, and failure states.
/// This class is a simplified version of a state manager that a real game would have, but in such a manner that the
/// integration of Metaplay into such a state manager is exemplified.
///
/// Also implements <see cref="IMetaplayLifecycleDelegate"/> to get callbacks from Metaplay on connectivity events and
/// error states.
/// </summary>
public class ApplicationStateManager :
    MonoBehaviour,
    IMetaplayLifecycleDelegate,
    IMetaplayClientAnalyticsDelegate,
    IPlayerModelClientListenerCore,
    IPlayerModelClientListener,
    IMetaplayClientSocialAuthenticationDelegate,
    IMetaplayClientGameConfigDelegate,
    IPartyModelClientListener
{
    /// <summary>
    /// Represents the state of the application.
    /// </summary>
    public enum ApplicationState
    {
        /// <summary>
        /// Application is being started.
        /// </summary>
        AppStart,

        /// <summary>
        /// Connecting to the server. A real application would also load its assets here.
        /// </summary>
        Initializing,

        /// <summary>
        /// Session with server has been established and we're playing the game.
        /// </summary>
        Game,
    }

    public static ApplicationStateManager Instance { get; private set; }

    public ApplicationState CurrentState { get; private set; } = ApplicationState.AppStart;  // Begin in the AppStart state.
    // For managing async state transitions (due to async scene loads)
    public ApplicationState? TransitioningToState { get; private set; } = null;
    Action _onStateTransitionDone = null;

    void Awake()
    {
        Instance = this;
        DontDestroyOnLoad(this);
    }

    void Start()
    {
        #if UNITY_STANDALONE
        InitSteamAPI();
        #endif

        // Initialize Metaplay SDK.
        MetaplayClient.Initialize(new MetaplayClientOptions
        {
            // MetaplaySDKBehavior has been added to the scene already, so no need to auto-create it
            AutoCreateMetaplaySDKBehavior = false,

            // Hook all the lifecycle and connectivity callbacks back to this class.
            LifecycleDelegate = this,

            // Custom connection delegate
            ConnectionDelegate = new GameConnectionDelegate(),

            // Configure connection behavior
            ConnectionConfig = new ConnectionConfig
            {
                // Configure initial connection attempts.
                // These are here as an example - could also use the default values.
                ConnectAttemptsMaxCount   = 5,
                ConnectAttemptInterval    = TimeSpan.FromSeconds(1),
            },

            //OfflineOptions = new MetaplayOfflineOptions
            //{
            //},

            // Register to listen to client-side analytics events
            AnalyticsDelegate = this,

            LocalizationDelegate = new GameLocalizationDelegate(),

            IAPOptions = new MetaplayIAPOptions
            {
                EnableIAPManager = true,
            },

            // Listen to social authentication validation responses from the server
            SocialAuthenticationDelegate = this,

            // Listen to changes of SharedGameConfigs hot updates (only supported in offline mode)
            GameConfigDelegate = this,
            AdditionalClients = new IMetaplaySubClient[]
            {
                new MatchmakingClient(),
                new IdlerLeagueClient(ClientSlotGame.IdlerLeague),
                new LeagueClient<IdlerPvPDivisionModel>(ClientSlotGame.IdlerPvPLeague),
                new PartyClient(),
            },
        });

        // Switch to initializing state, to start connecting to the server.
        _ = SwitchToStateAsync(ApplicationState.Initializing);
    }

    void OnDestroy()
    {
        MetaplayClient.Deinitialize();

        #if UNITY_STANDALONE
        if (_steamAPIInitialized)
            SteamAPI.Shutdown();
        #endif
    }

#if UNITY_STANDALONE
    bool _steamAPIInitialized = false;
    void InitSteamAPI()
    {
        // Note: Steam support works without issue in editor as well, as long as server has been configured
        // appropriately. Currently disabling Steam functionality in editor as most development doesn't need it.
        #if UNITY_EDITOR
        return;
        #else
        // Steam is enabled for the duration of the app lifetime only if Steam is running on startup.
        if (!SteamAPI.IsSteamRunning())
            return;

        SteamAPI.InitEx(out var steamErrorMsg);
        if (!string.IsNullOrEmpty(steamErrorMsg))
        {
            Debug.LogError($"Failed to initialize Steamworks: {steamErrorMsg}");
            return;
        }

        _steamAPIInitialized = true;
        #endif
    }
#endif

    void Update()
    {
        #if UNITY_STANDALONE
        if (_steamAPIInitialized)
        {
            SteamAPI.RunCallbacks();
        }
        #endif
        
        // Update Metaplay connections and game logic
        MetaplayClient.Update();

        // Flush listeners that were delayed (so that they don't run during action execution,
        // in case the listeners themselves might execute new actions).
        FlushDelayedListenerFuncs();
    }

    List<Action> _delayedListenerFuncs = new List<Action>();

    void DelayListenerFunc(Action func)
    {
        _delayedListenerFuncs.Add(func);
    }

    void FlushDelayedListenerFuncs()
    {
        foreach (Action action in _delayedListenerFuncs)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error when trying to run delayed listener func: {ex}");
            }
        }

        _delayedListenerFuncs.Clear();
    }

    /// <summary>
    /// Switch the application's state and perform actions relevant to the state transition.
    /// </summary>
    async Task SwitchToStateAsync(ApplicationState newState)
    {
        if (TransitioningToState.HasValue)
        {
            Debug.LogWarning($"Cannot transition to state {newState}, when already async-transitioning from {CurrentState} to {TransitioningToState.Value}");
            return;
        }

        Debug.Log($"Starting state transition from {CurrentState} to {newState}");

        TransitioningToState = newState;

        switch (newState)
        {
            case ApplicationState.AppStart:
                // Cannot enter, app starts in this state.
                break;

            case ApplicationState.Initializing:
            {
                // Switch to loading scene.
                await SceneManager.LoadSceneAsync("Loading Scene");

                // Make sure connection error info is hidden.
                ConnectionErrorPopoverScript.Clear();
                // Start connecting to the server.
                MetaplayClient.Connect();
                break;
            }

            case ApplicationState.Game:
            {
                // Start the game by loading the scene.
                await SceneManager.LoadSceneAsync("Game Scene");

                // Make sure connection error info is hidden.
                ConnectionErrorPopoverScript.Clear();
                break;
            }
        }

        Debug.Log($"Finished state transition from {CurrentState} to {newState}");
        CurrentState = newState;
        TransitioningToState = null;
        _onStateTransitionDone?.Invoke();
        _onStateTransitionDone = null;
    }

    /// <summary>
    /// Execute <paramref name="action"/> either immediately, or if a state transition
    /// is going on then postpone the action until after the transition.
    /// </summary>
    void ExecuteOrEnqueueAfterStateTransition(Action action)
    {
        if (TransitioningToState.HasValue)
            _onStateTransitionDone += action;
        else
            action?.Invoke();
    }

    /// <summary>
    /// Handler for Reconnect button (shown after a connection attempt has failed).
    /// </summary>
    public void ReconnectToServer()
    {
        // Switch back to initializing state, to start reconnecting.
        _ = SwitchToStateAsync(ApplicationState.Initializing);
    }

    #region IMetaplayLifecycleDelegate

    /// <summary>
    /// A session has been successfully negotiated with the server. At this point, we also have the
    /// relevant state initialized on the client, so we can move on to the game state.
    /// </summary>
    async Task IMetaplayLifecycleDelegate.OnSessionStartedAsync()
    {
        // Hook up to updates in PlayerModel.
        MetaplayClient.PlayerModel.ClientListenerCore = this;
        MetaplayClient.PlayerModel.ClientListener = this;

        MetaplayClient.GuildClient.SetClientListeners((IGuildModelBase model) =>
        {
            model.ClientListenerCore = EmptyGuildModelClientListenerCore.Instance;
            ((GuildModel)model).ClientListener = EmptyGuildModelClientListener.Instance;
        });

        MetaplayClient.IdlerLeagueClient.SetClientListeners((IdlerPlayerDivisionModel model) =>
        {
            model.SetClientListenerCore(EmptyPlayerDivisionModelClientListenerCore.Instance);
            // Client listener if any
            // model.ClientListener = EmptyPlayerDivisionModelClientListener.Instance;
        });

        MetaplayClient.PartyClient.SetClientListeners(model =>
        {
            ((PartyModel)model).ClientListener = this;
        });

        // Trigger party creation if one didn't already exist (this could be an interactive operation)
        if (MetaplayClient.PartyClient.Model == null)
            MetaplayClient.PlayerContext.ExecuteAction(new PlayerCreateParty());

        // Switch to the in-game state.
        await SwitchToStateAsync(ApplicationState.Game);

        // At this point, the player state is available. For example, the following are now valid:
        // Access player state members: MetaplayClient.PlayerModel.CurrentTime
        // Execute player actions: MetaplayClient.PlayerContext.ExecuteAction(..);
    }

    /// <summary>
    /// The current logical session has been lost and can no longer be resumed. This can happen for multiple
    /// reasons, for example, if the network connection is dropped for a sufficient long time, or if the
    /// application has been in the background for a long time, or if the server is in a maintenance mode.
    ///
    /// The application should react to this by showing a 'Connection Lost' dialog and present the player
    /// with a 'Reconnect' button.
    /// For some types of errors, it may be appropriate to omit the error popup, and auto-reconnect instead.
    /// </summary>
    /// <param name="connectionLost">Information about why the session loss happened.</param>
    void IMetaplayLifecycleDelegate.OnSessionLost(ConnectionLostEvent connectionLost)
    {
        // If no state transition is ongoing, handle the connection error immediately.
        // If state transition is ongoing, handle the connection error when the transition is done,
        // because handling the error might involve a new state transition, and we don't
        // want to deal with overlapping state transitions.
        ExecuteOrEnqueueAfterStateTransition(() =>
        {
            if (connectionLost.AutoReconnectRecommended)
            {
                // For certain errors, we auto-reconnect straight away without
                // prompting the player. Note that AutoReconnectRecommended is
                // just a suggestion by the SDK and is based on the type of the
                // error. The game does not have to obey the suggestion.
                ReconnectToServer();
            }
            else
            {
                // Otherwise, show the connection error popup, with info text
                // and a reconnect button.
                // Despite losing the session, the game scene will linger until
                // the player clicks on the reconnect button.
                // MetaplayClient.PlayerModel is still available so that the
                // game scene can continue to access it. It will remain available
                // until the reconnection starts.
                ShowConnectionErrorPopup(connectionLost);
            }
        });
    }

    /// <summary>
    /// Metaplay failed to establish a session with the server. Show the connection error and 'Reconnect'
    /// button so the player can try again.
    /// </summary>
    /// <param name="connectionLost">Information about why the failure happened.</param>
    void IMetaplayLifecycleDelegate.OnFailedToStartSession(ConnectionLostEvent connectionLost)
    {
        // Show the connection error popup, with info text and a reconnect button.
        // Note that we're not in the game scene since the error occurred before
        // the session was started. Furthermore, MetaplayClient.PlayerModel is
        // unavailable.
        ShowConnectionErrorPopup(connectionLost);
    }

    /// <summary>
    /// Show a popup with the details of a connection/session error,
    /// and a reconnect button.
    /// </summary>
    /// <param name="connectionLost"></param>
    void ShowConnectionErrorPopup(ConnectionLostEvent connectionLost)
    {
        Debug.LogWarning($"Terminal connection error: {PrettyPrint.Verbose(connectionLost)}");

        ConnectionErrorPopoverScript.ShowTerminalError(
            message: connectionLost.EnglishLocalizedReason,
            technicalCode: connectionLost.TechnicalErrorCode,
            technicalError: connectionLost.TechnicalError);
    }

    #endregion // IMetaplayLifecycleDelegate

    #region IMetaplayClientAnalyticsDelegate

    public void OnAnalyticsEvent(AnalyticsEventSpec eventSpec, AnalyticsEventBase payload, IModel model)
    {
#if METAPLAY_ENABLE_FIREBASE_ANALYTICS
        // Example for converting and forwarding Metaplay analytics events to Firebase.
        // NOTE: This implementation does not buffer the Firebase events which can cause exceptions to
        // be thrown if analytics events happen before Firebase SDK is fully initialized. You need to
        // implement buffering or drop the events to avoid the exceptions!
        FirebaseAnalyticsFormatter.EventFormatter formatter = null;

        // Route player and client events to Firebase, ignore all others
        if (payload is PlayerEventBase || payload is ClientEventBase)
            formatter = FirebaseAnalyticsFormatter.Instance.TryGetFormatterForEvent(eventSpec.Type);

        if (formatter != null)
        {
            List<(string, FirebaseAnalyticsFormatter.ParameterValue)> parameters = formatter.GetEventParameters(payload);
            Firebase.Analytics.Parameter[] convertedParams = AnalyticsEventFirebaseConverter.ToParameters(parameters);

            Debug.Log($"Forwarding analytics event to Firebase: {formatter.EventName}: {AnalyticsEventFirebaseConverter.ToString(parameters)}");
            Firebase.Analytics.FirebaseAnalytics.LogEvent(formatter.EventName, convertedParams);
        }
        else
            Debug.Log($"Ignoring analytics event: {PrettyPrint.Verbose(payload)}");
#else
        // No Firebase, just log events
        Debug.Log($"Analytics event: {PrettyPrint.Verbose(payload)}");
#endif
    }

    #endregion

    #region IPlayerModelClientListenerCore

    void IPlayerModelClientListenerCore.OnPlayerNameChanged(string newName)
    {
        if (DebugMenuScript.Instance)
            DebugMenuScript.Instance.OnPlayerNameChanged(newName);
    }

    void IPlayerModelClientListenerCore.PendingDynamicPurchaseContentAssigned(InAppProductId productId)
    {
        MetaplayClient.IAPManager.RegisterPendingDynamicPurchase(productId);
    }

    void IPlayerModelClientListenerCore.PendingStaticInAppPurchaseContextAssigned(InAppProductId productId)
    {
        MetaplayClient.IAPManager.RegisterPendingStaticPurchase(productId);
    }

    public delegate void GotLiveOpsEventUpdateDelegate(PlayerLiveOpsEventModel update);
    public event GotLiveOpsEventUpdateDelegate GotLiveOpsEventUpdate;
    void IPlayerModelClientListenerCore.GotLiveOpsEventUpdate(PlayerLiveOpsEventModel update)
    {
        DelayListenerFunc(() => GotLiveOpsEventUpdate?.Invoke(update));
    }

    #endregion // IPlayerModelClientListenerCore

    #region IPlayerModelClientListener

    void IPlayerModelClientListener.OnProducerUnlocked(ProducerModel producer) { }
    void IPlayerModelClientListener.OnProducerCollected(ProducerModel producer) { }

    #endregion // IPlayerModelClientListener

    #region IMetaplayClientSocialAuthenticationDelegate

    void IMetaplayClientSocialAuthenticationDelegate.OnSocialAuthenticationSuccess(AuthenticationPlatform platform)
    {
        Debug.Log($"Social authenticate success for {platform}");
    }

    void IMetaplayClientSocialAuthenticationDelegate.OnSocialAuthenticationFailure(AuthenticationPlatform platform, SocialAuthenticateResult.ResultCode errorCode, string debugOnlyErrorMessage)
    {
        Debug.LogWarning($"Social authenticate failure for {platform} with {errorCode}: {debugOnlyErrorMessage ?? "<hidden>"}");
    }

    void IMetaplayClientSocialAuthenticationDelegate.OnSocialAuthenticationConflict(AuthenticationPlatform platform, int conflictResolutionId, IPlayerModelBase conflictingPlayer)
    {
        Debug.Log($"Social authenticate conflict for {platform} with player: id={conflictingPlayer.PlayerId}, level={conflictingPlayer.PlayerLevel}, name={conflictingPlayer.PlayerName}");

        // \todo Show dialog to user to choose between currently active PlayerModel or conflictingPlayer

        // Based on the player's choice, inform the server which player state we want to keep using:
        //   useOther == false -- keep using the current player state, and attach the social authentication method to this player
        //   useOther == true -- keep the social authentication attached to the conflicting player, and switch this device to use that player
        // Here, we simulate that the player always chooses to continue with the conflicting player state.
        bool useOther = true;
        Debug.Log($"Resolve social platform profile conflict with useOther={useOther}");
        MetaplayClient.SocialAuthManager.ResolveConflict(conflictResolutionId, useOther);
    }

    void IMetaplayClientSocialAuthenticationDelegate.OnSocialAuthenticationConflictWithFailingOtherPlayer(AuthenticationPlatform platform, int conflictResolutionId, EntityId conflictingPlayerId)
    {
        Debug.LogError($"Social authenticate conflict for {platform} with player id={conflictingPlayerId}, but the server failed to get the other player's state");

        // \note This is a rare case and usually happens because the other player failed to deserialize.
        //       The failure is likely a bug and should be invesgiated by the developer.
        // \todo Show dialog and let user choose what to do:
        //       - Leave the auth conflict unresolved, and possibly contact customer support
        //       - Retry, in hopes the failure was transient (could retry by just restarting game)
        //       - Resolve the conflict in favor of the current PlayerModel (shouldn't choose the other state because it's possibly broken)

        // This stub implementation just leaves the conflict unresolved.
    }

    #endregion // IMetaplayClientSocialAuthenticationDelegate

    #region IMetaplayClientGameConfigDelegate

    /// <summary>
    /// The SharedGameConfig has been updated while the game is running. This is currently only
    /// supported in Offline Mode within Unity editor, and happens when the GameConfigs are built
    /// while the game is running.
    /// </summary>
    /// <param name="newConfigArchive"></param>
    void IMetaplayClientGameConfigDelegate.OnSharedGameConfigUpdated(ISharedGameConfig newSharedGameConfig, ContentHash version)
    {
        Debug.Log($"SharedGameConfig updated to version {version}, reloading scene..");

        // Reload scene to refresh the UI
        // Note: we can get away with completely initializing the UI on a very simple game like this
        // but you might want to consider how to best handle it in your game
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    #endregion // IMetaplayClientGameConfigDelegate

    #region IPartyModelClientListener

    void IPartyModelClientListener.NewChatMessage(PartyChatMessage message)
    {
        if (message.FromPlayerId != MetaplayClient.PlayerModel.PlayerId)
            Debug.Log($"New party chat message from {message.FromPlayerId}: {message.Message}");
    }

    void IPartyModelClientListener.MemberUpdated(EntityId member)
    {
    }

    #endregion
}
