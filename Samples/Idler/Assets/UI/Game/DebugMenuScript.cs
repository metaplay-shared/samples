// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Game.Logic;
using Metaplay.Core;
using Metaplay.Core.InAppPurchase;
using Metaplay.Core.Localization;
using Metaplay.Core.Network;
using Metaplay.Unity;
using Metaplay.Unity.DefaultIntegration;
using Metaplay.Unity.Localization;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Metaplay.Core.Client;
using TMPro;
using UnityEngine;

/// <summary>
/// Simple utility pop-over for triggering things from the game. You should implement
/// a more complicated menu that fits the specific needs of your game. Make sure it's
/// disabled in production builds!
/// </summary>
public class DebugMenuScript : MonoBehaviour
{
    // UI components
    public TextMeshProUGUI BuildLabel;
    public TextMeshProUGUI DeploymentLabel;
    public TextMeshProUGUI PlayerNameLabel;
    public TextMeshProUGUI PlayerIdLabel;
    public TextMeshProUGUI ConnectionInfoLabel;

    // Convenience properties
    public static DebugMenuScript Instance { get; private set; }

    void Awake()
    {
        // Make this instance easily available to other game objects
        Instance = this;

        BuildLabel.text = MetaplayClient.ClientBuildVersion;
        DeploymentLabel.text = IEnvironmentConfigProvider.Get().GetDisplayNameOrId();
        PlayerNameLabel.text = MetaplayClient.PlayerModel.PlayerName;
        PlayerIdLabel.text = MetaplayClient.PlayerModel.PlayerId.ToString();
    }

    private void Update()
    {
        ConnectionInfoLabel.text = ConnectionInfoToString(MetaplayClient.Connection.State, MetaplayClient.Connection.ServerConnectionDebugInformation);
    }

    public void Show()
    {
        // \todo [petri] #idlerport Should disable debug menu when in production and not a developer player
        gameObject.SetActive(true);
    }

    public void Close()
    {
        gameObject.SetActive(false);
    }

    public void OmitIAPClaim(bool omit)
    {
        MetaplayClient.IAPManager.DebugOmitIAPClaim = omit;
    }

    public void SimulateSocialAuthDev1()
    {
        Debug.Log("SIMULATE SOCIAL AUTHENTICATION (dev:0000001)");
        SocialAuthenticationClaimDevelopment claim = new SocialAuthenticationClaimDevelopment("{\"id\":\"dev:0000001\",\"token\":\"dummy-auth-token\"}");
        MetaplayClient.SocialAuthManager.StartValidation(claim);
    }

    public void SimulateSocialAuthDev2()
    {
        Debug.Log("SIMULATE SOCIAL AUTHENTICATION (dev:0000002)");
        // \note: Use \"migration_id\":\"dev:0000001\" to test migrations / multi login
        // \note: Use \"force_failure\":true to test failing request
        SocialAuthenticationClaimDevelopment claim = new SocialAuthenticationClaimDevelopment("{\"id\":\"dev:0000002\",\"token\":\"dummy-auth-token\"}");
        MetaplayClient.SocialAuthManager.StartValidation(claim);
    }

    public void ForceDesync()
    {
        if (IEnvironmentConfigProvider.Get().ConnectionEndpointConfig.IsOfflineMode)
            Debug.LogWarning("Desync forcing not available in Offline Mode");
        else
        {
            Debug.Log("FORCE DESYNC");
            MetaplayClient.PlayerContext.ExecuteAction(new PlayerForceDesync());
        }
    }

    public void SwitchLanguage()
    {
        List<LanguageId> languages = BuiltinLanguageRepository.GetBuiltinLanguages().Keys.ToList();
        int startNdx = languages.FindIndex(language => language == MetaplaySDK.ActiveLanguage.LanguageId);
        int curNdx = startNdx;
        for (;;)
        {
            // go to the next until we loop
            curNdx = (curNdx + 1) % languages.Count;
            if (curNdx == startNdx)
                break;

            MetaplaySDK.LocalizationManager.SetCurrentLanguage(languages[curNdx]);
        }
    }

    public void SleepUnity()
    {
        int numSeconds = 10;
        Debug.Log($"Blocking Unity for {numSeconds} seconds!");
        Thread.Sleep(numSeconds * 1000);
    }

    public void TriggerError()
    {
        GlobalErrorPopoverScript.ShowTransientError("Example error!", "This was triggered from debug controls.");
    }

    public void TriggerNetworkError()
    {
        MetaplayClient.Connection.CloseWithError(flushEnqueuedMessages: true, new Metaplay.Unity.ConnectionStates.TransientError.Closed());
    }

    public void TriggerException()
    {
        // \note Use this to trigger multiple simultaneous exceptions
        //foreach (int ndx in Enumerable.Range(0, 3))
        //    Debug.LogException(new ArgumentException($"Debug error {ndx}"));

        throw new InvalidOperationException("Manually triggered exception from debug menu");
    }

    public void TriggerSessionStartFailure()
    {
        ((GameConnectionDelegate)MetaplayClient.Connection.Delegate).FailNextSessionStart = true;
        MetaplayClient.Connection.CloseWithError(flushEnqueuedMessages: true, new Metaplay.Unity.ConnectionStates.TransientError.Closed());
    }


    public void DuplicateEventLogEvents()
    {
        MetaplayClient.PlayerContext.ExecuteAction(new PlayerDuplicateEventLogEventsDebug(numEventsToDuplicate: 50, numDuplicates: 20));
    }

    public void ClearBloatTestSize()
    {
        MetaplayClient.PlayerContext.ExecuteAction(new PlayerSetBloatTestSize(0));
    }

    public void IncreaseBloatTestSize(int delta)
    {
        MetaplayClient.PlayerContext.ExecuteAction(new PlayerSetBloatTestSize(MetaplayClient.PlayerModel.BloatTest.Length + delta));
    }

    public void CrashPlayerActor()
    {
        MetaplaySDK.MessageDispatcher.SendMessage(new PlayerDebugCrashActor("Triggered from debug menu"));
    }

    public void OnPlayerNameChanged(string newName)
    {
        PlayerNameLabel.text = newName;
    }

    private static string ConnectionInfoToString(Metaplay.Unity.ConnectionState state, ServerConnection.DebugInfo debugInfo)
    {
        DateTime currentTime = DateTime.UtcNow;

        string str = "";

        if (state is Metaplay.Unity.ConnectionStates.Connected connectedState)
        {
            if (str != "")
                str += "; ";

            if (connectedState.IsHealthy)
                str += "healthy";
            else
                str += "unhealthy";
        }

        if (debugInfo != null)
        {
            if (debugInfo.SessionResumptionAttempt != null)
            {
                if (str != "")
                    str += "; ";

                TimeSpan attemptElapsed = currentTime - debugInfo.SessionResumptionAttempt.StartTime;

                str += $"res {debugInfo.SessionResumptionAttempt.NumConnectionAttempts} / {attemptElapsed.TotalSeconds:0.0}";
            }

            if (debugInfo.ConnectionStartTime.HasValue)
            {
                if (str != "")
                    str += "; ";

                TimeSpan connElapsed = currentTime - debugInfo.ConnectionStartTime.Value;
                str += $"conn {connElapsed.TotalSeconds:0.0}";
            }
        }
        else
        {
            if (str != "")
                str += "no ServerConnection";
        }

        return str;
    }
}
