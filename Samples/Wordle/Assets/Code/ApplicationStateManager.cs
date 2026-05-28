// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Game.Logic;
using Metaplay.Core;
using Metaplay.Core.Message;
using Metaplay.Core.Session;
using Metaplay.Unity;
using Metaplay.Unity.DefaultIntegration;
using System;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

public class MetaplayClient : MetaplayClientBase<PlayerModel> { }

/// <summary>
/// Manages the application's lifecycle, including mock loading state, Metaplay server connectivity, and failure states.
/// This class is a simplified version of a state manager that a real game would have, but in such a manner that the
/// integration of Metaplay into such a state manager is exemplified.
/// </summary>
public class ApplicationStateManager : MonoBehaviour
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

    // When connection to server is not established, display connection status.
    public GameObject       ConnectionStatusCanvas;     // Canvas that contains the connection status info. Shown only when no active connection exists.
    public Text             ConnectionStatusText;       // Text to display status of connection.
    public Text             ConnectingSpinner;          // Spinner in connecting state.
    public GameObject       ConnectionErrorPopup;       // Popup shown when a connection error happens.
    public Text             ConnectionErrorInfoText;    // Info text within ConnectionErrorPopup describing the error.
    public GameManager      GameManagerPrefab;          // Prefab for the in-game state, spawned when a session with the server is established.

    // Runtime state
    GameManager             _gameManager;               // Instance of the GameManager, spawned when the player state is received from the server.

    public ApplicationState CurrentState { get; private set; } = ApplicationState.AppStart;  // Begin in the AppStart state.

    void Awake()
    {
        // When the app starts, make sure the connection error info is hidden.
        ConnectionErrorPopup.SetActive(false);
    }

    void Start()
    {
        // Initialize Metaplay SDK.
        MetaplayClient.Initialize(new MetaplayClientOptions
        {
        });

        _ = SessionLoop();
    }

    void Update()
    {
        // Update connection UI (visible when session is not active)
        UpdateConnectionStatusUI();
    }

    #region doc-application-lifecycle-snippet
    async Task SessionLoop()
    {
        for (;;)
        {
            CurrentState = ApplicationState.Initializing;

            // Simulate the transition away from the Game scene by destroying the GameManager instance.
            // In addition to the Game scene, it's possible to arrive here from Initializing state itself,
            // in case the connection fails before a session was started. In that case there is no
            // GameManager instance.
            if (_gameManager != null)
            {
                Destroy(_gameManager.gameObject);
                _gameManager = null;
            }

            // Make sure connection error info is hidden.
            ConnectionErrorPopup.SetActive(false);

            // Connect to the server
            MetaplaySession session;
            try
            {
                session = await MetaplayClient.ConnectAsync();
            }
            catch (FailedToStartSessionException ex)
            {
                // Metaplay failed to establish a session with the server. Show the connection error and 'Reconnect'
                // button so the player can try again.
                // Note that we're not in the game scene since the error occurred before
                // the session was started. Furthermore, MetaplayClient.PlayerModel is
                // unavailable.
                await ShowConnectionErrorPopup(ex.Failure);
                continue;
            }

            // A session has been successfully negotiated with the server. At this point, we also have the
            // relevant state initialized on the client, so we can move on to the game state.

            try
            {
                // Start the game. Simulate the transition to in-game state by spawning the GameManager.
                // You might want to use scene transition instead.
                _gameManager = Instantiate(GameManagerPrefab);

                // Start has been completed. Game must call SessionStartComplete()
                session.SessionStartComplete();
            }
            catch (Exception ex)
            {
                // Start has failed. Game must call SessionStartFailed()
                ConnectionLostEvent sessionStartFailed = session.SessionStartFailed(ex);
                await ShowConnectionErrorPopup(sessionStartFailed);
                continue;
            }

            CurrentState = ApplicationState.Game;

            // Do nothing until connection is lost
            //
            // The current logical session has been lost and can no longer be resumed. This can happen for multiple
            // reasons, for example, if the network connection is dropped for a sufficient long time, or if the
            // application has been in the background for a long time, or if the server is in a maintenance mode.
            //
            // The application should react to this by showing a 'Connection Lost' dialog and present the player
            // with a 'Reconnect' button.
            // For some types of errors, it may be appropriate to omit the error popup, and auto-reconnect instead.
            ConnectionLostEvent connectionLost = await session.WaitForSessionEndAsync();

            if (connectionLost.AutoReconnectRecommended)
            {
                // For certain errors, we auto-reconnect straight away without
                // prompting the player. Note that AutoReconnectRecommended is
                // just a suggestion by the SDK and is based on the type of the
                // error. The game does not have to obey the suggestion.
            }
            else
            {
                // Otherwise, show the connection error popup, with info text
                // and a reconnect button. Despite losing the session, the game
                // scene will linger until the player clicks on the reconnect
                // button. PlayerModel is still available so that the game scene
                // can continue to access it. It will remain available until
                // the reconnection starts.
                await ShowConnectionErrorPopup(connectionLost);
            }
        }
    }
    #endregion doc-application-lifecycle-snippet

    /// <summary>
    /// When connection isn't yet established, show the status of connection.
    /// </summary>
    void UpdateConnectionStatusUI()
    {
        // Only show connectivity info if we don't have an established connection.
        ConnectionStatusCanvas.SetActive(MetaplayClient.PlayerContext == null);

        // Show connection status text & progress indicator.
        ConnectionStatus connectionStatus = MetaplayClient.Connection.State.Status;
        ConnectionStatusText.text = connectionStatus.ToString();
        ConnectingSpinner.gameObject.SetActive(connectionStatus == ConnectionStatus.Connecting);
        ConnectingSpinner.text = "........".Substring(0, (int)(Time.time * 3.0f) % 8);
    }

    #region Reconnect dialog

    TaskCompletionSource<int> _reconnectButtonCompleteCts;

    /// <summary>
    /// Handler for Reconnect button (shown after a connection attempt has failed).
    /// </summary>
    public void OnClickReconnect()
    {
        _reconnectButtonCompleteCts?.TrySetResult(0);
    }

    /// <summary>
    /// Show a popup with the details of a connection/session error,
    /// and a reconnect button.
    /// </summary>
    /// <param name="connectionLost"></param>
    Task ShowConnectionErrorPopup(ConnectionLostEvent connectionLost)
    {
        _reconnectButtonCompleteCts = new TaskCompletionSource<int>();
        ConnectionErrorInfoText.text = CreateConnectionLostInfoText(connectionLost);
        ConnectionErrorPopup.SetActive(true);
        return _reconnectButtonCompleteCts.Task;
    }

    /// <summary>
    /// Convert a <see cref="ConnectionLostEvent"/> into a human-readable string, for displaying in the UI.
    /// This implementation shows quite a lot of technical detail which is useful for developers, but for
    /// real players, you'd want to show something more compact.
    /// </summary>
    /// <param name="connectionLost"></param>
    /// <returns>Technical description of the connection loss event, mainly intended for developers.</returns>
    static string CreateConnectionLostInfoText(ConnectionLostEvent connectionLost)
    {
        StringBuilder info = new StringBuilder();

        // EnglishLocalizedReason and TechnicalErrorCode should typically be shown to players.
        info.AppendLine($"* Reason: {connectionLost.EnglishLocalizedReason}");
        info.AppendLine($"* Technical code: {connectionLost.TechnicalErrorCode}");

        // TechnicalErrorString and ExtraTechnicalInfo are intended for analytics.
        info.AppendLine();
        info.AppendLine($"* Technical error string: {connectionLost.TechnicalErrorString}");
        if (!string.IsNullOrEmpty(connectionLost.ExtraTechnicalInfo))
            info.AppendLine($"* Additional technical info: {connectionLost.ExtraTechnicalInfo}");

        // More detailed technical info that's mainly useful for developers.
        info.AppendLine();
        info.AppendLine($"* Technical error: {PrettyPrint.Compact(connectionLost.TechnicalError)}");

        return info.ToString();
    }

    #endregion // Reconnect dialog
}
