using Game.Logic;
using System.Collections.Generic;
using Metaplay.Core;
using Metaplay.Core.LiveOpsEvent;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class EventPopoverScript : MonoBehaviour
{
    public TextMeshProUGUI HeaderTitleLabel;
    public TextMeshProUGUI TitleLabel;
    public TextMeshProUGUI BodyLabel;
    public Button OkButton;
    private EventPopoverInfo _popupShowing = null;

    public class EventPopoverInfo
    {
        public MetaGuid EventId;
        public string HeaderTitle;
        public string Title;
        public string Body;
        public bool ClearUpdateOnClick;
    }

    public void Init()
    {
        OkButton.onClick.AddListener(OnOkClicked);

        ShowNextEventOrHide();

        ApplicationStateManager.Instance.GotLiveOpsEventUpdate += GotLiveOpsEventUpdate;
    }

    public void OnDestroy()
    {
        ApplicationStateManager.Instance.GotLiveOpsEventUpdate -= GotLiveOpsEventUpdate;
    }

    void GotLiveOpsEventUpdate(PlayerLiveOpsEventModel update)
    {
        // \note update parameter is ignored, we just find the earliest timestamp in player.LiveOpsEvents.LatestUnacknowledgedUpdatePerEvent.

        if (_popupShowing == null)
            ShowNextEventOrHide();
    }

    void ShowNextEventOrHide()
    {
        EventPopoverInfo eventInfo = TryDequeueNextEvent();
        if (eventInfo != null)
            ShowEvent(eventInfo);
        else
            Hide();
    }

    /// <summary>
    /// Gets the next event to show, if any, dequeuing the event update record from the player state.
    /// May dequeue also prior updates that we want to silently discard instead of showing.
    /// "Dequeue" is meant conceptually here, there's no concrete Queue or anything.
    /// </summary>
    EventPopoverInfo TryDequeueNextEvent()
    {
        while (true)
        {
            // Get earliest event update record (if any) of a wanted event type
            PlayerLiveOpsEventModel earliestUpdate = TryGetNextEventUpdate();
            if (earliestUpdate == null)
                return null;

            // Show popup only for certain types of events (where TryCreatePopoverInfo returns null).
            // Other types of events are silently discarded and we keep looping until we either find
            // something to show or we run out of updates.
            EventPopoverInfo eventInfo = TryCreatePopoverInfo(earliestUpdate);
            if (eventInfo != null)
            {
                // Clear update if no acknowledgment is needed
                if (!eventInfo.ClearUpdateOnClick)
                    ClearEventUpdate(earliestUpdate.Id);
                return eventInfo;
            }

            // Clear update record from player state silently
            ClearEventUpdate(earliestUpdate.Id);
        }
    }

    /// <summary>
    /// Returns the next (earliest by timestamp) event update in player.LiveOpsEvents.LatestUnacknowledgedUpdatePerEvent
    /// with content type IdlerLiveOpsEvent, if any (null otherwise).
    /// Doesn't mutate player state.
    /// </summary>
    PlayerLiveOpsEventModel TryGetNextEventUpdate()
    {
        return MetaplayClient.PlayerModel.LiveOpsEvents.TryGetEarliestUpdate(update => update.Content is IdlerTestLiveOpsEvent or SpecialProducerEvent);
    }

    /// <summary>
    /// Mutates player state (via a player action) to clear the update record of the given event.
    /// </summary>
    void ClearEventUpdate(MetaGuid eventId)
    {
        MetaplayClient.PlayerContext.ExecuteAction(new PlayerClearLiveOpsEventUpdates(new List<MetaGuid> { eventId }));
    }

    /// <summary>
    /// Enable the event popup and populate it
    /// </summary>
    public void ShowEvent(EventPopoverInfo eventInfo)
    {
        gameObject.SetActive(true);
        _popupShowing = eventInfo;

        HeaderTitleLabel.text = eventInfo.HeaderTitle;
        TitleLabel.text = eventInfo.Title;
        BodyLabel.text = eventInfo.Body;
    }

    private EventPopoverInfo TryCreatePopoverInfo(PlayerLiveOpsEventModel eventUpdate)
    {
        return eventUpdate switch
        {
            IdlerTestLiveOpsEventModel idlerLiveOpsEvent => CreatePopoverInfoForTestEvent(idlerLiveOpsEvent),
            SpecialProducerLiveOpsEventModel specialProducerEvent => CreatePopoverInfoForSpecialProducerEvent(specialProducerEvent),
            _ => null
        };
    }

    private EventPopoverInfo CreatePopoverInfoForTestEvent(IdlerTestLiveOpsEventModel eventModel)
    {
        return new EventPopoverInfo()
        {
            EventId = eventModel.Id,
            HeaderTitle = $"LiveOps Event in phase '{eventModel.Phase}'!",
            Title = eventModel.Content.TestString,
            Body = eventModel.Content.TestInt.ToString()
        };
    }

    private EventPopoverInfo CreatePopoverInfoForSpecialProducerEvent(SpecialProducerLiveOpsEventModel eventModel)
    {
        // Note: this implementation disregards the event update types and maintains info on what to show to the user within the event model itself.

        if (MetaplayClient.PlayerModel.Producers.TryGetValue(eventModel.Content.ProducerId, out ProducerModel producer))
        {
            // mask popups if associated special producer exists and was created by another source
            if (!(producer.StartedBy is ProducerSourceLiveOpsEvent eventSource && eventSource.EventId == eventModel.Id))
                return null;
        }

        if (eventModel.Phase == LiveOpsEventPhase.Preview && !eventModel.PreviewPopupSeen)
        {
            return new EventPopoverInfo()
            {
                EventId = eventModel.Id,
                HeaderTitle = "Producer event about to start!",
                Title = eventModel.Content.DisplayName,
                Body = $"Special producer {eventModel.Content.ProducerId} will become available in {DurationUntilStr(eventModel.ScheduleMaybe?.GetEnabledStartTime())}",
                ClearUpdateOnClick = false // Automatically clear this update, no need for ack
            };
        }
        if (eventModel.Phase.IsActivePhase() && !eventModel.EventStartPopupSeen)
        {
            string availableForTextMaybe;
            if (eventModel.ScheduleMaybe != null)
                availableForTextMaybe = $" for {DurationUntilStr(eventModel.ScheduleMaybe?.GetEnabledEndTime())}";
            else
                availableForTextMaybe = "";

            return new EventPopoverInfo()
            {
                EventId = eventModel.Id,
                HeaderTitle = "Producer event started!",
                Title = eventModel.Content.DisplayName,
                Body = $"Special producer {eventModel.Content.ProducerId} is now available{availableForTextMaybe}. Reach level {eventModel.Content.ProducerTargetLevel} to get rewards!",
                ClearUpdateOnClick = false // Automatically clear this update, no need for ack
            };
        }
        // Note: concluded dialog only shown if event start was seen by player, otherwise concluding silently.
        if (eventModel.Phase.IsEndedPhase() && eventModel.EventStartPopupSeen && !eventModel.RewardsClaimed)
        {
            return new EventPopoverInfo()
            {
                EventId = eventModel.Id,
                HeaderTitle = "Producer event concluded!",
                Title = eventModel.Content.DisplayName,
                Body = eventModel.LevelReached < eventModel.Content.ProducerTargetLevel ?
                    $"You only got to level {eventModel.LevelReached} and didn't achieve the goal of level {eventModel.Content.ProducerTargetLevel}, better luck next time.." :
                    $"Congratulations! You got to level {eventModel.LevelReached} which exceeds goal of level {eventModel.Content.ProducerTargetLevel}. Click to receive your reward!",
                ClearUpdateOnClick = true
            };
        }

        return null;
    }

    string DurationUntilStr(MetaTime? targetTime)
    {
        if (!targetTime.HasValue)
            return "<unknown>";

        MetaDuration duration = Util.Max(MetaDuration.Zero, targetTime.Value - MetaplayClient.PlayerModel.CurrentTime);
        return duration.ToSimplifiedString();
    }

    public void OnOkClicked()
    {
        if (_popupShowing != null)
        {
            if (_popupShowing.ClearUpdateOnClick)
                ClearEventUpdate(_popupShowing.EventId);
            ShowNextEventOrHide();
        }
    }

    public void Hide()
    {
        gameObject.SetActive(false);
        _popupShowing = null;
    }
}
