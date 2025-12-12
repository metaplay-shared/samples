using Metaplay.Unity;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Represents the in-game application logic. Only gets spawned after a session has been
/// established with the server, so we can assume all the state has been setup already.
/// </summary>
public class GameManager : MonoBehaviour
{
    public Text     UnhealthyConnectionIndicator;       // Indicator to display when connection is in an unhealthy state.

    void Update()
    {
        // Show the unhealthy connection indicator.
        bool connectionIsUnhealthy = MetaplayClient.ConnectionHealth == Metaplay.Unity.DefaultIntegration.ConnectionHealth.Unhealthy;
        UnhealthyConnectionIndicator.gameObject.SetActive(connectionIsUnhealthy);
    }
}
