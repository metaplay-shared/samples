// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using Metaplay.Core.InGameMail;
using Metaplay.Unity;
using Metaplay.Unity.DefaultIntegration;
using System.Linq;
using TMPro;
using UnityEngine;

public class GameUIScript : MonoBehaviour
{
    public GameObject               MaintenanceModeBannerObject;
    public TextMeshProUGUI          MaintenanceModeBannerText;
    public TextMeshProUGUI          GoldText;
    public TextMeshProUGUI          GemsText;
    public MailPopoverScript        MailDialog;
    public DeletionPopoverScript    DeletionDialog;
    public EventPopoverScript       EventPopover;

    public GameObject WorkScreenContainer;
    public GameObject AccountScreenContainer;
    public GameObject ShopScreenContainer;

    public RectTransform WorkButton;
    public RectTransform AccountButton;
    public RectTransform ShopButton;

    public RectTransform TabBarHighlightObject;

    MaintenanceModeState        _scheduledMaintenanceMode;

    void Start()
    {
        OnMaintenanceModeChanged();
        MetaplaySDK.MaintenanceModeChanged += OnMaintenanceModeChanged;

        // Always start from the work screen
        this.WorkScreenContainer.SetActive(true);
        this.AccountScreenContainer.SetActive(false);
        this.ShopScreenContainer.SetActive(false);

        EventPopover.Init();
    }

    void Update()
    {
        if (MetaplayClient.PlayerModel == null)
            return;

        // Update player stats
        GoldText.text = MetaplayClient.PlayerModel.Wallet.NumGold.ToString();
        GemsText.text = MetaplayClient.PlayerModel.Wallet.NumGems.ToString();

        // Update maintenance mode banner
        switch (_scheduledMaintenanceMode.Status)
        {
            case MaintenanceModeState.ScheduleStatus.Upcoming:
            {
                MetaTime now = MetaTime.Now;
                string bannerText;

                // MaintenanceStartAt can be slightly in the past when maintenance is starting
                MetaDuration durationToMaintenanceStart = _scheduledMaintenanceMode.MaintenanceStartAt - now;
                if (durationToMaintenanceStart > MetaDuration.Zero)
                    bannerText = "Scheduled maintenance due in " + HumanizeDuration(durationToMaintenanceStart);
                else
                    bannerText = "Scheduled maintenance starting NOW!";

                if (_scheduledMaintenanceMode.EstimatedMaintenanceOverAt.HasValue)
                    bannerText += ". Estimated duration is " + HumanizeDuration(_scheduledMaintenanceMode.EstimatedMaintenanceOverAt.Value - _scheduledMaintenanceMode.MaintenanceStartAt);

                MaintenanceModeBannerText.text = bannerText;
                break;
            }

            case MaintenanceModeState.ScheduleStatus.Ongoing:
            {
                MetaTime now = MetaTime.Now;
                string bannerText = "Ongoing maintenance";
                // if estimation, and that is still in the future, show estimation
                if (_scheduledMaintenanceMode.EstimatedMaintenanceOverAt.HasValue && _scheduledMaintenanceMode.EstimatedMaintenanceOverAt.Value > now)
                    bannerText += ". Estimated duration is " + HumanizeDuration(_scheduledMaintenanceMode.EstimatedMaintenanceOverAt.Value - now);
                MaintenanceModeBannerText.text = bannerText;
                break;
            }
        }

        // Update mail dialog (show first mail in inbox, if has any)
        var genericMail = MetaplayClient.PlayerModel.MailInbox.Where(x => x.Contents is SimplePlayerMail);
        if (genericMail.Any())
        {
            MailDialog.ShowMail(genericMail.First());
        }
        else
            MailDialog.Hide();

        DeletionDialog.GameUIUpdate();
    }

    void OnDestroy()
    {
        MetaplaySDK.MaintenanceModeChanged -= OnMaintenanceModeChanged;
    }

    /// <summary>
    /// Simple utility to turn a duration into a more human-friendly string
    /// </summary>
    string HumanizeDuration(MetaDuration duration)
    {
        long totalSeconds = duration.Milliseconds / 1000;
        long seconds = totalSeconds % 60;
        long minutes = (totalSeconds / 60) % 60;
        long hours = (totalSeconds / (60 * 60)) % 24;
        long totalDays = (totalSeconds / (60 * 60 * 24));

        string result = "";
        bool displayLowerUnits = false;
        if (totalDays > 0)
        {
            result += $"{totalDays} day{(totalDays == 1 ? "" : "s")}, ";
            displayLowerUnits = true;
        }
        if (displayLowerUnits || hours > 0)
        {
            result += $"{hours} hour{(hours == 1 ? "" : "s")}, ";
            displayLowerUnits = true;
        }
        if (displayLowerUnits || minutes > 0)
        {
            result += $"{minutes} minute{(minutes == 1 ? "" : "s")}, ";
            displayLowerUnits = true;
        }
        result += $"{seconds} second{(seconds == 1 ? "" : "s")}";
        return result;
    }

    /// <summary>
    /// Handle an event informing us of changes to scheduled maintenance mode
    /// </summary>
    /// <param name="updateScheduledMaintenanceMode"></param>
    void OnMaintenanceModeChanged()
    {
        // Cache the value locally and show or hide the popover banner
        _scheduledMaintenanceMode = MetaplaySDK.MaintenanceMode;
        if (_scheduledMaintenanceMode.Status == MaintenanceModeState.ScheduleStatus.NotScheduled)
            MaintenanceModeBannerObject.SetActive(false);
        else
            MaintenanceModeBannerObject.SetActive(true);
    }

    public void OpenScreen(string screen)
    {
        // Disable all screens
        this.AccountScreenContainer.SetActive(false);
        this.WorkScreenContainer.SetActive(false);
        this.ShopScreenContainer.SetActive(false);

        // Enable the new screen
        switch (screen)
        {
            case "account":
                this.AccountScreenContainer.SetActive(true);
                this.TabBarHighlightObject.position = new Vector3(this.AccountButton.position.x, this.TabBarHighlightObject.position.y, this.TabBarHighlightObject.position.z);
                break;
            case "work":
                this.WorkScreenContainer.SetActive(true);
                this.TabBarHighlightObject.position = new Vector3(this.WorkButton.position.x, this.TabBarHighlightObject.position.y, this.TabBarHighlightObject.position.z);
                break;
            case "shop":
                this.ShopScreenContainer.SetActive(true);
                this.TabBarHighlightObject.position = new Vector3(this.ShopButton.position.x, this.TabBarHighlightObject.position.y, this.TabBarHighlightObject.position.z);
                break;
            default:
                Debug.LogError($"Trying to open a screen that doesn't exist: {screen}");
                break;
        }
    }
}
