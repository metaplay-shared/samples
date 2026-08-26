// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using UnityEngine;
using TMPro;
using System;
using Metaplay.Core;
using Metaplay.Core.Network;
using System.Collections;
using Metaplay.Core.Client;
using Metaplay.Unity;

public class GlobalErrorPopoverScript : MonoBehaviour
{
    public GameObject VisualContainer;
    public TextMeshProUGUI HeaderLabel;
    public TextMeshProUGUI BodyLabel;
    public TextMeshProUGUI BackendBuildLabel;
    public TextMeshProUGUI GameStateLabel;
    public TextMeshProUGUI ClientBuildLabel;
    public TextMeshProUGUI CommitLabel;
    public TextMeshProUGUI ConfigLabel;
    public TextMeshProUGUI DatetimeLabel;
    public TextMeshProUGUI DeploymentLabel;
    public GameObject CloseButton;
    public GameObject ReloadButton;
    public NetworkDiagnosticReport NetworkDiagnosticReport;

    private class PendingMessage
    {
        public string errorHeader;
        public string errorBody;
        public string state;
        public string timestamp;
        public bool isTransient;
        public bool showNetworkDiagnostics;
    }

    static GlobalErrorPopoverScript _instance;
    static object _pendingLock = new object();
    static PendingMessage _pendingMessage;

    // \todo This isn't actually used anywhere so the log errors don't get handled!
    private static readonly Lazy<ErrorLogListener> _handler = new Lazy<ErrorLogListener>();
    public static IMetaLogger ErrorLogger => _handler.Value;

    void Awake()
    {
        _instance = this;
        _instance.VisualContainer.SetActive(false);
        DontDestroyOnLoad(gameObject);
    }

    void Update()
    {
        PendingMessage poppedPendingMessage;

        lock (_pendingLock)
        {
            poppedPendingMessage = _pendingMessage;
            _pendingMessage = null;
        }

        if (poppedPendingMessage != null)
            _instance.InternalShow(poppedPendingMessage);

        if (VisualContainer.activeSelf && NetworkDiagnosticReport != null)
            BodyLabel.text = NetworkDiagnosticReport.ToPlayerFacingString(richText: true);
    }

    /// <summary>
    /// Adds an error message to the popover que. You might want to automatically fill
    /// some more parameters depending on how they are exposed in your game.
    /// </summary>
    /// <param name="errorHeader">Short error description</param>
    /// <param name="errorBody">Any helpful data about the error</param>
    static void AddNewMessage(string errorHeader, string errorBody, bool isTransient)
    {
        PendingMessage message = new PendingMessage();
        message.errorHeader = errorHeader;
        message.errorBody   = errorBody;
        message.state       = ApplicationStateManager.Instance.CurrentState.ToString();
        message.timestamp   = System.DateTime.Now.ToString();
        message.isTransient = isTransient;
        message.showNetworkDiagnostics = !isTransient;

        lock (_pendingLock)
            _pendingMessage = message;
    }

    /// <summary>
    /// Displays a popover that the player can close and continue playing.
    /// </summary>
    /// <param name="errorHeader">Short error description</param>
    /// <param name="errorBody">Any helpful data about the error</param>
    public static void ShowTransientError(string errorHeader, string errorBody)
    {
        AddNewMessage(errorHeader, errorBody, true);
    }

    /// <summary>
    /// Displays a popover that forces the player to reload the game.
    /// </summary>
    /// <param name="errorHeader">Short error description</param>
    /// <param name="errorBody">Any helpful data about the error</param>
    public static void ShowTerminalError(string errorHeader, string errorBody)
    {
        AddNewMessage(errorHeader, errorBody, false);
    }

    private void InternalShow(PendingMessage message)
    {
        if (message.showNetworkDiagnostics && MetaplayClient.NetworkDiagnosticsManager.LastNetworkDiagnosticReport != null)
        {
            _instance.NetworkDiagnosticReport = MetaplayClient.NetworkDiagnosticsManager.LastNetworkDiagnosticReport;
            _instance.BodyLabel.text = _instance.NetworkDiagnosticReport.ToPlayerFacingString(richText: true);
            Debug.Log(_instance.NetworkDiagnosticReport);
        }
        else
            _instance.NetworkDiagnosticReport = null;

        _instance.HeaderLabel.text = message.errorHeader;
        _instance.BodyLabel.text = message.errorBody;
        _instance.BackendBuildLabel.text = MetaplayClient.BackendBuildVersion;
        _instance.GameStateLabel.text = message.state;
        _instance.ClientBuildLabel.text = MetaplayClient.ClientBuildVersion;
        _instance.CommitLabel.text = MetaplaySDK.BuildVersion.CommitId;
        _instance.ConfigLabel.text = MetaplayClient.ConfigVersion;
        _instance.DatetimeLabel.text = message.timestamp;
        _instance.DeploymentLabel.text = IEnvironmentConfigProvider.Get().GetDisplayNameOrId();

        _instance.VisualContainer.SetActive(true);
        _instance.CloseButton.SetActive(false /*message.isTransient*/);
        _instance.ReloadButton.SetActive(true /*!message.isTransient*/);
    }

    /// <summary>
    /// Hides the popover and switches back to the loading screen.
    /// </summary>
    public void HideAndReload()
    {
        Hide();

        MetaplayClient.Connection.CloseWithError(new Metaplay.Core.Session.ConnectionStates.TransientError.Closed());
    }

    /// <summary>
    /// Hides the popover.
    /// </summary>
    public void Hide()
    {
        _instance.VisualContainer.SetActive(false);
    }

    // Hook up to show Unity's native error messages
    class ErrorLogListener : MetaLoggerBase
    {
        public override sealed bool IsLevelEnabled(LogLevel level)
        {
            return level >= LogLevel.Error;
        }

        public override sealed void LogEvent(LogLevel level, Exception ex, string format, params object[] args)
        {
            if (level != LogLevel.Error)
                return;

            ShowTransientError($"Error logged!", LogTemplateFormatter.ToFlatString(format, args));
        }
    }
}
