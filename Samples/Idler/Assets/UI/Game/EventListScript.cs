// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Game.Logic;
using Metaplay.Core;
using Metaplay.Core.Activables;
using Metaplay.Core.LiveOpsEvent;
using Metaplay.Core.Math;
using Metaplay.Unity.DefaultIntegration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using static System.FormattableString;

public class EventListScript : MonoBehaviour
{
    public EventListItemScript      EventListItem;

    EventListItemScript NoEventsUiItem;
    MetaDictionary<EventKey, EventListItemScript> EventUIItems = new MetaDictionary<EventKey, EventListItemScript>();

    Dictionary<MetaActivableKindId, IEventUIDelegate> _activableEventUIDelegates;
    Dictionary<Type, ILiveOpsEventUIDelegate> _liveOpsEventUIDelegates;

    void Start()
    {
        PlayerModel player = MetaplayClient.PlayerModel;

        NoEventsUiItem = Instantiate(EventListItem, transform);
        NoEventsUiItem.TitleText.text = "No events to show";
        NoEventsUiItem.DescriptionText.text = "You can change event schedules or add new ones in the game configs.";

        MetaActivableCategoryId eventCategoryId = MetaActivableCategoryId.FromString("Event");

        _activableEventUIDelegates =
            MetaActivableUtil.GetKindIdsInCategory(eventCategoryId)
            .ToDictionary(
                kindId => kindId,
                kindId => CreateEventUIDelegate(kindId));

        _liveOpsEventUIDelegates = new Dictionary<Type, ILiveOpsEventUIDelegate>()
        {
            { typeof(IdlerTestLiveOpsEvent), new IdlerLiveOpsEventUIDelegate() },
            { typeof(IdlerAnotherTestLiveOpsEvent), new IdlerAnotherLiveOpsEventUIDelegate() },
        };

        foreach (MetaActivableKey activableKey in MetaActivableUtil.GetActivableKeysInCategory(eventCategoryId, player.GameConfig))
        {
            EventListItemScript uiItem = Instantiate(EventListItem, transform);
            uiItem.EventKey = EventKey.ForActivable(activableKey);
            uiItem.OnClickClaimEvent += OnClaimClicked;
            EventUIItems.Add(uiItem.EventKey, uiItem);
        }

        foreach (PlayerLiveOpsEventModel eventModel in player.LiveOpsEvents.EventModels.Values)
        {
            CreateLiveOpsEventUI(eventModel.Content, eventModel.Id);
        }

        ApplicationStateManager.Instance.GotLiveOpsEventUpdate += GotLiveOpsEventUpdate;
    }

    void OnDestroy()
    {
        ApplicationStateManager.Instance.GotLiveOpsEventUpdate -= GotLiveOpsEventUpdate;
    }

    void GotLiveOpsEventUpdate(PlayerLiveOpsEventModel update)
    {
        EventKey eventKey = EventKey.ForLiveOpsEvent(update.Id);

        if (update.Phase == LiveOpsEventPhase.Concluded)
        {
            bool hadUIItem = EventUIItems.Remove(eventKey, out EventListItemScript uiItem);
            if (hadUIItem)
                Destroy(uiItem.gameObject);
        }
        else
        {
            if (!EventUIItems.ContainsKey(eventKey))
                CreateLiveOpsEventUI(update.Content, update.Id);
        }
    }

    void CreateLiveOpsEventUI(LiveOpsEventContent content, MetaGuid eventId)
    {
        if (!_liveOpsEventUIDelegates.ContainsKey(content.GetType()))
            return;
        EventListItemScript uiItem = Instantiate(EventListItem, transform);
        uiItem.EventKey = EventKey.ForLiveOpsEvent(eventId);
        uiItem.OnClickClaimEvent += OnClaimClicked;
        EventUIItems.Add(uiItem.EventKey, uiItem);
    }

    public struct EventDynamicVisuals
    {
        public string   Description;
        public bool     ShowClaimButton;

        public EventDynamicVisuals(string description, bool showClaimButton = false)
        {
            Description = description ?? throw new ArgumentNullException(nameof(description));
            ShowClaimButton = showClaimButton;
        }
    }

    void Update()
    {
        PlayerModel player = MetaplayClient.PlayerModel;

        bool anyEventVisible = false;

        foreach ((EventKey eventKey, EventListItemScript uiItem) in EventUIItems)
        {
            if (!eventKey.Activable.HasValue)
                continue;

            IEventUIDelegate            eventUIDelegate = _activableEventUIDelegates[eventKey.Activable.Value.KindId];
            IMetaActivableConfigData    eventInfo       = MetaActivableUtil.GetActivableGameConfigData(eventKey.Activable.Value, player.GameConfig);

            MetaActivableUtil.TryGetVisibleStatus(eventKey.Activable.Value, player, player.GameConfig, out MetaActivableVisibleStatus visibleStatus);

            EventDynamicVisuals? dynamicVisualsMaybe = eventUIDelegate.GetDynamicVisuals(player, visibleStatus, eventInfo);

            uiItem.gameObject.SetActive(dynamicVisualsMaybe.HasValue);

            if (dynamicVisualsMaybe.HasValue)
            {
                EventDynamicVisuals dynamicVisuals = dynamicVisualsMaybe.Value;

                anyEventVisible = true;

                switch (visibleStatus)
                {
                    case MetaActivableVisibleStatus.InPreview preview:
                    {
                        uiItem.PhaseText.text = "Preview";
                        uiItem.PhaseImage.color = uiItem.inactiveColor;
                        uiItem.NextPhaseText.text = $"Active in {(preview.ScheduleEnabledRange.Start - player.CurrentTime).ToSimplifiedString()}";
                        break;
                    }

                    // \note Tentative is here visualized similarly to preview.
                    //       Tentative status can be briefly observed here because event activation is not performed
                    //       on every tick - a "starting soon" visualization is fine for that.
                    case MetaActivableVisibleStatus.Tentative _:
                    {
                        uiItem.PhaseText.text = "Preview";
                        uiItem.PhaseImage.color = uiItem.inactiveColor;
                        uiItem.NextPhaseText.text = $"Active in {MetaDuration.Zero.ToSimplifiedString()}";
                        break;
                    }

                    case MetaActivableVisibleStatus.Active active:
                        uiItem.PhaseText.text = "Active";
                        uiItem.PhaseImage.color = uiItem.activeColor;
                        uiItem.NextPhaseText.text = $"Time left: {(active.ActivationEndsAt - player.CurrentTime)?.ToSimplifiedString()}";
                        break;

                    case MetaActivableVisibleStatus.EndingSoon endingSoon:
                        uiItem.PhaseText.text = "Ending soon";
                        uiItem.PhaseImage.color = uiItem.endingColor;
                        uiItem.NextPhaseText.text = $"Time left: {(endingSoon.ActivationEndsAt - player.CurrentTime)?.ToSimplifiedString()}";
                        break;

                    // \note If eventUIDelegate.GetDynamicVisuals returned a non-null result despite visibleStatus being null,
                    //       it's probably because there's still a reward waiting to get claimed by the player.
                    //       Here we visualize that case (null visibleStatus) the same way as Review.
                    // \todo [nuutti] Could clean this up by separating this top-level visualization from MetaActivableVisibleStatus
                    case MetaActivableVisibleStatus.InReview _:
                    case null:
                        uiItem.PhaseText.text = "Review";
                        uiItem.PhaseImage.color = uiItem.inactiveColor;
                        uiItem.NextPhaseText.text = $"";
                        break;

                    default:
                        // Unhandled status
                        uiItem.PhaseText.text = $"{visibleStatus?.GetType().Name}";
                        uiItem.PhaseImage.color = uiItem.errorColor;
                        break;
                }

                uiItem.TitleText.text = eventUIDelegate.GetTitle(player, eventInfo);
                uiItem.DescriptionText.text = dynamicVisuals.Description;
                uiItem.ClaimButton.gameObject.SetActive(dynamicVisuals.ShowClaimButton);
                uiItem.DescriptionText.gameObject.SetActive(!dynamicVisuals.ShowClaimButton);
            }
        }

        foreach ((EventKey eventKey, EventListItemScript uiItem) in EventUIItems)
        {
            if (!eventKey.LiveOpsEvent.HasValue)
                continue;

            MetaGuid eventId = eventKey.LiveOpsEvent.Value;
            PlayerLiveOpsEventModel eventModel = player.LiveOpsEvents.EventModels[eventId];
            LiveOpsEventPhase eventPhase = eventModel.Phase;

            ILiveOpsEventUIDelegate eventUIDelegate = _liveOpsEventUIDelegates[eventModel.Content.GetType()];

            EventDynamicVisuals? dynamicVisualsMaybe = eventUIDelegate.GetDynamicVisuals(player, eventModel);

            uiItem.gameObject.SetActive(dynamicVisualsMaybe.HasValue);

            if (dynamicVisualsMaybe.HasValue)
            {
                EventDynamicVisuals dynamicVisuals = dynamicVisualsMaybe.Value;
                LiveOpsEventScheduleInfo scheduleMaybe = eventModel.ScheduleMaybe;

                anyEventVisible = true;

                if (eventPhase == LiveOpsEventPhase.Preview)
                {
                    uiItem.PhaseText.text = "Preview";
                    uiItem.PhaseImage.color = uiItem.inactiveColor;
                    uiItem.NextPhaseText.text = $"Active in {DurationUntilStr(player, scheduleMaybe?.GetEnabledStartTime())}";
                }
                else if (eventPhase == LiveOpsEventPhase.NormalActive)
                {
                    uiItem.PhaseText.text = "Active";
                    uiItem.PhaseImage.color = uiItem.activeColor;
                    if (scheduleMaybe != null)
                        uiItem.NextPhaseText.text = $"Time left: {DurationUntilStr(player, scheduleMaybe?.GetEnabledEndTime())}";
                    else
                        uiItem.NextPhaseText.text = "";
                }
                else if (eventPhase == LiveOpsEventPhase.EndingSoon)
                {
                    uiItem.PhaseText.text = "Ending soon";
                    uiItem.PhaseImage.color = uiItem.endingColor;
                    uiItem.NextPhaseText.text = $"Time left: {DurationUntilStr(player, scheduleMaybe?.GetEnabledEndTime())}";
                }
                else if (eventPhase == LiveOpsEventPhase.Review)
                {
                    uiItem.PhaseText.text = "Review";
                    uiItem.PhaseImage.color = uiItem.inactiveColor;
                    uiItem.NextPhaseText.text = $"Review ends in {DurationUntilStr(player, scheduleMaybe?.GetConcludedTime())}";
                }
                else
                {
                    // Unhandled status
                    uiItem.PhaseText.text = $"Unhandled: {eventPhase}";
                    uiItem.PhaseImage.color = uiItem.errorColor;
                }

                uiItem.TitleText.text = eventUIDelegate.GetTitle(player, eventModel);
                uiItem.DescriptionText.text = dynamicVisuals.Description;
                uiItem.ClaimButton.gameObject.SetActive(dynamicVisuals.ShowClaimButton);
                uiItem.DescriptionText.gameObject.SetActive(!dynamicVisuals.ShowClaimButton);
            }
        }

        NoEventsUiItem.gameObject.SetActive(!anyEventVisible);
    }

    static string DurationUntilStr(PlayerModel player, MetaTime? targetTime)
    {
        if (!targetTime.HasValue)
            return "<unknown>";

        MetaDuration duration = Util.Max(MetaDuration.Zero, targetTime.Value - player.CurrentTime);
        return duration.ToSimplifiedString();
    }

    void OnClaimClicked(EventKey eventKey)
    {
        PlayerModel player = MetaplayClient.PlayerModel;

        if (eventKey.Activable.HasValue)
        {
            IMetaActivableInfo eventInfo = MetaActivableUtil.GetActivableGameConfigData(eventKey.Activable.Value, player.GameConfig);
            _activableEventUIDelegates[eventKey.Activable.Value.KindId].OnClaimClicked(MetaplayClient.PlayerModel, eventInfo);
        }
        else
        {
            MetaGuid eventId = eventKey.LiveOpsEvent.Value;
            PlayerLiveOpsEventModel eventModel = player.LiveOpsEvents.EventModels[eventId];
            LiveOpsEventContent eventContent = eventModel.Content;
            _liveOpsEventUIDelegates[eventContent.GetType()].OnClaimClicked(player, eventModel);
        }
    }

    static IEventUIDelegate CreateEventUIDelegate(MetaActivableKindId kindId)
    {
        if (kindId == MetaActivableKindId.FromString("HappyHour"))
            return new HappyHourUIDelegate();
        else if (kindId == MetaActivableKindId.FromString("SpecialProducerEvent"))
            return new SpecialProducerEventUIDelegate();
        else
            throw new InvalidOperationException($"Unhandled kind for event UI delegate: {kindId}");
    }

    public interface IEventUIDelegate
    {
        string                  GetTitle            (PlayerModel player, object eventInfo);
        EventDynamicVisuals?    GetDynamicVisuals   (PlayerModel player, MetaActivableVisibleStatus visibleStatus, object eventInfo);
        void                    OnClaimClicked      (PlayerModel player, object eventInfo);
    }

    public abstract class EventUIDelegate<TEventInfo> : IEventUIDelegate
    {
        string               IEventUIDelegate.GetTitle            (PlayerModel player, object eventInfo) => GetTitle(player, (TEventInfo)eventInfo);
        EventDynamicVisuals? IEventUIDelegate.GetDynamicVisuals   (PlayerModel player, MetaActivableVisibleStatus visibleStatus, object eventInfo) => GetDynamicVisuals(player, visibleStatus, (TEventInfo)eventInfo);
        void                 IEventUIDelegate.OnClaimClicked      (PlayerModel player, object eventInfo) => OnClaimClicked(player, (TEventInfo)eventInfo);

        public abstract string                  GetTitle            (PlayerModel player, TEventInfo eventInfo);
        public abstract EventDynamicVisuals?    GetDynamicVisuals   (PlayerModel player, MetaActivableVisibleStatus visibleStatus, TEventInfo eventInfo);
        public abstract void                    OnClaimClicked      (PlayerModel player, TEventInfo eventInfo);
    }

    public interface ILiveOpsEventUIDelegate
    {
        string                  GetTitle            (PlayerModel player, PlayerLiveOpsEventModel liveOpsEventModel);
        EventDynamicVisuals?    GetDynamicVisuals   (PlayerModel player, PlayerLiveOpsEventModel liveOpsEventModel);
        void                    OnClaimClicked      (PlayerModel player, PlayerLiveOpsEventModel liveOpsEventModel);
    }

    #region Event kind specific delegates

    public class HappyHourUIDelegate : EventUIDelegate<HappyHourInfo>
    {
        public override string GetTitle(PlayerModel player, HappyHourInfo happyHourInfo)
        {
            return happyHourInfo.DisplayName;
        }

        public override EventDynamicVisuals? GetDynamicVisuals(PlayerModel player, MetaActivableVisibleStatus visibleStatus, HappyHourInfo eventInfo)
        {
            if (visibleStatus == null)
                return null;

            int costReductionPercentage = F64.FloorToInt((F64.One - eventInfo.CostFactor) * 100);

            switch (visibleStatus)
            {
                // \note Tentative is here visualized the same way as preview.
                //       Tentative status can be briefly observed here because event activation is not performed
                //       on every tick - a "starting soon" visualization is fine for that.
                case MetaActivableVisibleStatus.InPreview _:
                case MetaActivableVisibleStatus.Tentative _:
                    return new EventDynamicVisuals(eventInfo.Description);

                case MetaActivableVisibleStatus.Active _:
                    return new EventDynamicVisuals($"{eventInfo.Producer.GetItem(MetaplayClient.PlayerModel.GameConfig).Name} is {costReductionPercentage}% off right now!");

                case MetaActivableVisibleStatus.EndingSoon _:
                    return new EventDynamicVisuals($"{eventInfo.Producer.GetItem(MetaplayClient.PlayerModel.GameConfig).Name} is still {costReductionPercentage}% off for a while longer!");

                case MetaActivableVisibleStatus.InReview _:
                    return new EventDynamicVisuals($"{eventInfo.Producer.GetItem(MetaplayClient.PlayerModel.GameConfig).Name} is now back to its normal cost.");

                default:
                    return new EventDynamicVisuals("");
            }
        }

        public override void OnClaimClicked(PlayerModel player, HappyHourInfo eventInfo)
        {
            Debug.LogWarning("Cannot claim a happy hour event");
        }
    }

    public class SpecialProducerEventUIDelegate : EventUIDelegate<SpecialProducerEventInfo>
    {
        public override string GetTitle(PlayerModel player, SpecialProducerEventInfo eventInfo)
        {
            return eventInfo.DisplayName;
        }

        public override EventDynamicVisuals? GetDynamicVisuals(PlayerModel player, MetaActivableVisibleStatus visibleStatus, SpecialProducerEventInfo eventInfo)
        {
            // Special producer event with unclaimed rewards is handled specially
            {
                SpecialProducerEventModel eventState = player.SpecialProducerEvents.TryGetState(eventInfo);

                if (eventState?.LastResult?.ClaimPending ?? false)
                {
                    return new EventDynamicVisuals(
                        description:        GetSpecialProducerEventResultDescription(eventInfo, eventState.LastResult),
                        showClaimButton:    true);
                }
            }

            if (visibleStatus == null)
                return null;

            bool hasReachedTargetLevel;
            {
                // This is in its own scope just to not leak the producer variable
                hasReachedTargetLevel = player.Producers.TryGetValue(eventInfo.Producer.Ref.Id, out ProducerModel producer)
                                     && producer.Level >= eventInfo.ProducerTargetLevel;
            }

            switch (visibleStatus)
            {
                // \note Tentative is here visualized the same way as preview.
                //       Tentative status can be briefly observed here because event activation is not performed
                //       on every tick - a "starting soon" visualization is fine for that.
                case MetaActivableVisibleStatus.InPreview _:
                case MetaActivableVisibleStatus.Tentative _:
                    return new EventDynamicVisuals($"Special producer {eventInfo.Producer.Ref.Name} incoming soon...");

                case MetaActivableVisibleStatus.Active _:
                {
                    if (hasReachedTargetLevel)
                        return new EventDynamicVisuals($"You reached level {player.Producers[eventInfo.Producer.Ref.Id].Level} (>= {eventInfo.ProducerTargetLevel}) for {eventInfo.Producer.Ref.Name}! You'll get a reward at the end!");
                    else
                        return new EventDynamicVisuals($"{eventInfo.Producer.Ref.Name} is now available! Upgrade it to level {eventInfo.ProducerTargetLevel} for a reward at the end!");
                }

                case MetaActivableVisibleStatus.EndingSoon _:
                {
                    if (hasReachedTargetLevel)
                        return new EventDynamicVisuals($"You reached level {player.Producers[eventInfo.Producer.Ref.Id].Level} (>= {eventInfo.ProducerTargetLevel}) for {eventInfo.Producer.Ref.Name}! You'll get a reward soon!");
                    else
                        return new EventDynamicVisuals($"You still have a while to try and upgrade {eventInfo.Producer.Ref.Name} to level {eventInfo.ProducerTargetLevel}!");
                }

                case MetaActivableVisibleStatus.InReview _:
                {
                    SpecialProducerEventModel eventState = player.SpecialProducerEvents.TryGetState(eventInfo);

                    if (eventState.LastResult == null) // Can happen for a short while after event has ended but before finalization has been done. \todo [nuutti] Better visualization
                        return new EventDynamicVisuals("...");
                    else
                        return new EventDynamicVisuals(GetSpecialProducerEventResultDescription(eventInfo, eventState.LastResult));
                }

                default:
                    return new EventDynamicVisuals("");
            }
        }

        public override void OnClaimClicked(PlayerModel player, SpecialProducerEventInfo eventInfo)
        {
            MetaplayClient.PlayerContext.ExecuteAction(new PlayerClaimSpecialProducerEventRewards(eventInfo));
        }

        static string GetSpecialProducerEventResultDescription(SpecialProducerEventInfo eventInfo, SpecialProducerEventModel.EventResult result)
        {
            if (result.ClaimPending)
                return $"You reached level {result.LevelReached} (>= {eventInfo.ProducerTargetLevel}) for {eventInfo.Producer.Ref.Name}! Claim your rewards!";
            else if (result.LevelReached >= eventInfo.ProducerTargetLevel)
                return $"Congratulations for completing the event! The event is now finished.";
            else
                return $"You only reached level {result.LevelReached} (< {eventInfo.ProducerTargetLevel}) for {eventInfo.Producer.Ref.Name}. Better luck next time!";
        }
    }


    public class IdlerLiveOpsEventUIDelegate : ILiveOpsEventUIDelegate
    {
        public string GetTitle(PlayerModel player, PlayerLiveOpsEventModel liveOpsEventModel)
        {
            IdlerTestLiveOpsEvent content = (IdlerTestLiveOpsEvent)liveOpsEventModel.Content;
            return $"Test: {content.TestString}";
        }

        public EventDynamicVisuals? GetDynamicVisuals(PlayerModel player, PlayerLiveOpsEventModel liveOpsEventModel)
        {
            IdlerTestLiveOpsEventModel model = (IdlerTestLiveOpsEventModel)liveOpsEventModel;

            return new EventDynamicVisuals(Invariant($"State: {model.TestStateInt}"));
        }

        public void OnClaimClicked(PlayerModel player, PlayerLiveOpsEventModel liveOpsEventModel)
        {
            Debug.LogWarning("Cannot claim a test event");
        }
    }

    public class IdlerAnotherLiveOpsEventUIDelegate : ILiveOpsEventUIDelegate
    {
        public string GetTitle(PlayerModel player, PlayerLiveOpsEventModel liveOpsEventModel)
        {
            IdlerAnotherTestLiveOpsEvent content = (IdlerAnotherTestLiveOpsEvent)liveOpsEventModel.Content;
            return $"Test: {content.TestString}";
        }

        public EventDynamicVisuals? GetDynamicVisuals(PlayerModel player, PlayerLiveOpsEventModel liveOpsEventModel)
        {
            return new EventDynamicVisuals("");
        }

        public void OnClaimClicked(PlayerModel player, PlayerLiveOpsEventModel liveOpsEventModel)
        {
            Debug.LogWarning("Cannot claim a test event");
        }
    }

    #endregion
}
