using Game.Logic;
using Metaplay.Unity;
using UnityEngine;
using UnityEngine.UI;

// This file contains Metaplay sample code. It can be adapted to suit your project's needs or you can
// replace the functionality completely with your own.
namespace Metaplay.Sample
{
    /// <summary>
    /// Represents the in-game application logic. Only gets spawned after a session has been
    /// established with the server, so we can assume all the state has been setup already.
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        public Text     NumClicksText;                      // Text to display the number of clicks so far.
        public Button   ClickMeButton;                      // 'Click Me' button that invokes OnClickButton().
        public Text     UnhealthyConnectionIndicator;       // Indicator to display when connection is in an unhealthy state.

        void Update()
        {
            // Update the number of clicks on UI.
            NumClicksText.text = MetaplayClient.PlayerModel.NumClicks.ToString();

            // Show the unhealthy connection indicator.
            bool connectionIsUnhealthy = MetaplayClient.ConnectionHealth == Metaplay.Unity.DefaultIntegration.ConnectionHealth.Unhealthy;
            UnhealthyConnectionIndicator.gameObject.SetActive(connectionIsUnhealthy);
        }

        public void OnClickButton()
        {
            // Button was clicked, execute the action to bump PlayerModel.NumClicks (on client and server).
            MetaplayClient.PlayerContext.ExecuteAction(new PlayerClickButton());
        }
    }
}
