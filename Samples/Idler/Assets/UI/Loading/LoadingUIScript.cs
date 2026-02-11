// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Session;
using Metaplay.Unity;
using Metaplay.Unity.DefaultIntegration;
using TMPro;
using UnityEngine;

public class LoadingUIScript : MonoBehaviour
{
    public GameObject       ConnectionStatusCanvas;     // Canvas that contains the connection status info. Shown only when no active connection exists.
    public TMP_Text         ConnectionStatusText;       // Text to display status of connection.
    public TMP_Text         ConnectingSpinner;          // Spinner in connecting state.

    public GameObject       MaintenancePopover;

    static LoadingUIScript  _instance;

    void Awake()
    {
        _instance = this;
        MaintenancePopover.SetActive(false);
    }

    void Start()
    {
    }

    /// <summary>
    /// When connection isn't yet established, show the status of connection.
    /// </summary>
    void Update()
    {
        // Show connection status text & progress indicator.
        string statusText;
        switch (MetaplayClient.Connection.State)
        {
            case Metaplay.Core.Session.ConnectionStates.Connecting connecting:
                statusText = $"Connecting ({connecting.Phase})";
                break;

            default:
                statusText = MetaplayClient.Connection.State.Status.ToString();
                break;
        }
        ConnectionStatusText.text = statusText;
        ConnectingSpinner.gameObject.SetActive(MetaplayClient.Connection.State.Status == ConnectionStatus.Connecting);
        ConnectingSpinner.text = new string('.', (int)(Time.time * 3.0f) % 8);
    }

    /// <summary>
    /// Displays a popover for maintenance mode.
    /// </summary>
    public static void ShowMaintenanceMode()
    {
        _instance.MaintenancePopover.SetActive(true);
    }
}
