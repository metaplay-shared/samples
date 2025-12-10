// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using Metaplay.Core.Network;
using Metaplay.Unity;
using Metaplay.Unity.ConnectionStates;
using Metaplay.Unity.DefaultIntegration;
using System.Collections;
using System.Collections.Generic;
using Metaplay.Core.Client;
using TMPro;
using UnityEngine;

public class ConnectionErrorPopoverScript : MonoBehaviour
{
    // Objects for turning things on and off
    public GameObject Container;
    public GameObject EncodedDetailsContainer;
    public GameObject VerboseDetailsContainer;
    public GameObject InfoButton;

    // Things that we should display on every error to help with debugging
    public TextMeshProUGUI SummaryLabel; // What happened
    public TextMeshProUGUI EncodedDetailsLabel; // Connection diagnostic report in a nutshell
    public TextMeshProUGUI VerboseDetailsLabel; // Long form text dump of the error
    public TextMeshProUGUI PlayerIdLabel; // Player ID is good to have visible on all error screens just in case

    // Sources of data
    private ErrorInfo Error;

    public static ConnectionErrorPopoverScript Instance;

    void Awake()
    {
        Instance = this;
        Container.SetActive(false);
        DontDestroyOnLoad(gameObject);
    }

    void Update()
    {
        // Keep updating the report if it's visible (it can change over time)
        if (Container.activeInHierarchy)
        {
            string verboseDetails;
            if (Error.ShowNetworkDiagnostics)
            {
                NetworkDiagnosticReport diagnosticsReportMaybe = TryGetNetworkDiagnosticsReport(Error);

                EncodedDetailsLabel.text = diagnosticsReportMaybe?.ToEncodedString() ?? "";

                verboseDetails = string.Join("\n", new string[] {
                    Error.ToPlayerFacingString(),
                    "",
                    diagnosticsReportMaybe?.ToPlayerFacingString(richText: true) ?? ""
                });
            }
            else
                verboseDetails = Error.ToPlayerFacingString();

            VerboseDetailsLabel.text = verboseDetails;
        }
    }

    static NetworkDiagnosticReport TryGetNetworkDiagnosticsReport(ErrorInfo error)
    {
        // Get either the latest network diagnostic report, or the report contained in the connection error state.
        // Either or both can be null!
        // \todo Fix so that we only need to get LastNetworkDiagnosticReport?
        //       I.e. make sure that if the error has the report available,
        //       then LastNetworkDiagnosticReport is also assigned.
        return MetaplayClient.NetworkDiagnosticsManager.LastNetworkDiagnosticReport
               ?? (error.TechnicalError as IHasNetworkDiagnosticReport)?.NetworkDiagnosticReport;
    }

    /// <summary>
    /// Helper class to collect context for this error.
    /// </summary>
    private class ErrorInfo
    {
        public string Message; // "Connection lost."
        public string TechnicalCode; // Numerical code that should be player-facing so it can help with debugging when provided via customer support.
        public string GameState; // "Battle"
        public string DeviceTimestamp; // Device local time
        public string PlayerId; // "Player:XXXXX..."
        public string ClientBuild; // "1.0.2"
        public string BackendBuild;
        public string ConfigVersion;
        public string EnvironmentName; // Display name of the environment, eg, "Dev env for feature 1"
        public string Device; // "iPhone6,1"
        public string OperatingSystem; // "iPhone OS 8.4"
        public ConnectionState TechnicalError;
        public string TechnicalDetails; // Verbose dump of TechnicalError.
        public bool ShowNetworkDiagnostics;

        public string ToPlayerFacingString()
        {
            List<string> payload = new List<string>();

            payload.Add("Context:");
            payload.Add($"Game state: {GameState}");
            payload.Add($"Device time: {DeviceTimestamp}");
            payload.Add($"Client version: {ClientBuild}");
            payload.Add($"Backend version: {BackendBuild}");
            payload.Add($"Config version: {ConfigVersion}");
            payload.Add($"Environment: {EnvironmentName}");
            payload.Add($"Device model: {Device}");
            payload.Add($"Operating system: {OperatingSystem}");

            payload.Add("");
            payload.Add("Technical details:");
            payload.Add(TechnicalDetails);

            return string.Join("\n", payload);
        }
    }

    /// <summary>
    /// Adds an error message to the popover que.
    /// <param name="summary">Short error description</param>
    public static void ShowTerminalError(string message, int technicalCode, ConnectionState technicalError)
    {
        ErrorInfo error = new ErrorInfo();
        error.Message = message;
        error.TechnicalCode = technicalCode.ToString();
        error.GameState = ApplicationStateManager.Instance.CurrentState.ToString();
        error.DeviceTimestamp = System.DateTime.Now.ToString();
        error.PlayerId = MetaplaySDK.PlayerId.IsValid ? MetaplaySDK.PlayerId.ToString() : "No player id";
        error.ClientBuild = MetaplayClient.ClientBuildVersion;
        error.BackendBuild = MetaplayClient.BackendBuildVersion;
        error.ConfigVersion = MetaplayClient.ConfigVersion;
        error.EnvironmentName = IEnvironmentConfigProvider.Get().GetDisplayNameOrId();
        error.Device = SystemInfo.deviceModel;
        error.OperatingSystem = SystemInfo.operatingSystem;
        error.TechnicalError = technicalError;
        error.TechnicalDetails = PrettyPrint.Verbose(technicalError).ToString();
        error.ShowNetworkDiagnostics = technicalError is IHasNetworkDiagnosticReport;

        // Cache
        Instance.Error = error;

        // Show the latest network report if needed
        if (error.ShowNetworkDiagnostics)
            Instance.EncodedDetailsContainer.SetActive(true);
        else
            Instance.EncodedDetailsContainer.SetActive(false);

        // Fill labels
        Instance.SummaryLabel.text = $"{error.Message} (#{technicalCode})";
        Instance.PlayerIdLabel.text = error.PlayerId;

        // Display
        Instance.Container.SetActive(true);
        Instance.VerboseDetailsContainer.SetActive(false);
        Instance.InfoButton.SetActive(true);
    }

    public static void Clear()
    {
        if (Instance != null)
        {
            Instance.Container.SetActive(false);
            Instance.Error = null;
        }
    }

    /// <summary>
    /// Handler for the 'Reconnect' button: hides the popup and starts reconnecting to server.
    /// </summary>
    public void OnReconnectClicked()
    {
        // Delay reconnecting to end-of-frame to avoid issues with scripts still running.
        StartCoroutine(DelayedReconnect());
    }

    IEnumerator DelayedReconnect()
    {
        yield return new WaitForEndOfFrame();

        ApplicationStateManager.Instance.ReconnectToServer();
    }

    /// <summary>
    /// Hides the popover and switches back to the loading screen.
    /// </summary>
    public void ShowDetails()
    {
        EncodedDetailsContainer.SetActive(false);
        VerboseDetailsContainer.SetActive(true);
        InfoButton.SetActive(false);
    }
}
