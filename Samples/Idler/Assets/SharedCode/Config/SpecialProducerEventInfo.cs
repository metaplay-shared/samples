// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using Metaplay.Core.Activables;
using Metaplay.Core.Config;
using Metaplay.Core.Model;
using Metaplay.Core.Player;
using Metaplay.Core.Schedule;
using System;
using System.Collections.Generic;
using System.Linq;
using Metaplay.Core.LiveOpsEvent;

namespace Game.Logic
{
    /// <summary>
    /// This metadata declaration lets the Metaplay SDK know about
    /// Idler's "Special Producer Event" kind of activables.
    ///
    /// Associated attributes are found on game config and player sub-model types:
    /// A <see cref="MetaActivableConfigDataAttribute"/> on <see cref="SpecialProducerEventInfo"/>,
    /// and a <see cref="MetaActivableSetAttribute"/> on <see cref="PlayerSpecialProducerEventsModel"/>.
    /// These attributes tell the SDK where configuration and player state
    /// can be found for Special Producer Events, which allows it to operate on them in an
    /// abstract manner, for example in order to display them in the dashboard.
    /// </summary>
    [MetaActivableKindMetadata(
        id:             "SpecialProducerEvent",
        displayName:    "Special Producer Event",
        description:    "Events where player attempts to reach a target level for a special producer that is only available during the event",
        categoryId:     "Event")]
    public static class ActivableKindMetadataSpecialProducerEvent
    {
    }

    [MetaSerializable]
    public class SpecialProducerEventId : StringId<SpecialProducerEventId> { }

    /// <summary>
    /// Config data for a special producer event.
    /// A special producer event is an event that, while active, reduces the unlock and upgrade costs of a specific producer.
    /// A special producer event becomes available according to the configured <see cref="ActivableParams"/>.
    /// </summary>
    /// <remarks>
    /// <c>SpecialProducerEventInfo</c> is not parsed from config directly; instead <see cref="SpecialProducerEventSourceConfigItem"/>
    /// is parsed and converted to <c>SpecialProducerEventInfo</c>.
    /// </remarks>
    [MetaSerializable]
    [MetaActivableConfigData("SpecialProducerEvent")]
    public class SpecialProducerEventInfo : IMetaActivableConfigData<SpecialProducerEventId>, ILiveOpsEventTemplate<SpecialProducerEvent>, IGameConfigPostLoad
    {
        [MetaMember(1)]                        public SpecialProducerEventId      EventId             { get; private set; }
        [MetaMember(2)]                        public string                      DisplayName         { get; private set; }
        [MetaMember(3), ServerOnly]            public string                      Description         { get; private set; }
        [MetaMember(4)]                        public MetaRef<ProducerInfo>       Producer            { get; private set; }
        [MetaMember(5)]                        public int                         ProducerTargetLevel { get; private set; }
        [MetaMember(6), MaxCollectionSize(10)] public IReadOnlyList<PlayerReward> Rewards             { get; private set; }
        [MetaMember(7)]                        public MetaActivableParams         ActivableParams     { get; private set; }

        public SpecialProducerEventId ActivableId   => EventId;
        public SpecialProducerEventId ConfigKey     => EventId;

        public string DisplayShortInfo => null;

        // Conversion to LiveOps Event template
        public LiveOpsEventTemplateId TemplateId => LiveOpsEventTemplateId.FromString(EventId.Value);
        public SpecialProducerEvent Content => SpecialProducerEvent.FromTemplate(this);
        string ILiveOpsEventTemplate.DefaultDisplayName => DisplayName;
        string ILiveOpsEventTemplate.DefaultDescription => Description;

        public SpecialProducerEventInfo(){}

        [MetaGameConfigBuildConstructor]
        public SpecialProducerEventInfo(SpecialProducerEventId eventId, string displayName, string description, MetaRef<ProducerInfo> producer, int producerTargetLevel, IReadOnlyList<PlayerReward> rewards, MetaActivableParams activableParams)
        {
            if (producerTargetLevel <= 0)
                throw new ArgumentException($"Producer target level in event {eventId} is {producerTargetLevel}, but it should be positive", nameof(producerTargetLevel));

            EventId = eventId ?? throw new ArgumentNullException(nameof(eventId));
            DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
            Description = description ?? throw new ArgumentNullException(nameof(description));
            Producer = producer ?? throw new ArgumentNullException(nameof(producer));
            ProducerTargetLevel = producerTargetLevel;
            Rewards = rewards ?? throw new ArgumentNullException(nameof(rewards));
            ActivableParams = activableParams ?? throw new ArgumentNullException(nameof(activableParams));
        }

        public void PostLoad()
        {
            if (Producer.Ref.Category != ProducerCategory.Event)
                throw new ArgumentException($"Producer {Producer.Ref.Id} category in event {EventId} is {Producer.Ref.Category}, but it should be {nameof(ProducerCategory.Event)}");
        }
    }

    /// <summary>
    /// Source-config item for <see cref="SpecialProducerEventInfo"/>.
    /// This is parsed from config sheets and converted to <see cref="SpecialProducerEventInfo"/> in <see cref="ToConfigData"/>.
    /// The purpose of this intermediate conversion step is to help with parsing the nested <see cref="MetaActivableParams"/> from a flat sheet structure.
    /// </summary>
    public class SpecialProducerEventSourceConfigItem : IGameConfigSourceItem<SpecialProducerEventId, SpecialProducerEventInfo>
    {
        public SpecialProducerEventId               Id;
        public string                               DisplayName;
        public string                               Description;
        public MetaRef<ProducerInfo>                Producer;
        public int                                  ProducerTargetLevel;
        public List<PlayerReward>                   Rewards;

        public List<MetaRef<PlayerSegmentInfoBase>> Segments;
        public MetaScheduleBase                     Schedule;

        public SpecialProducerEventId ConfigKey => Id;

        public SpecialProducerEventInfo ToConfigData(GameConfigBuildLog buildLog)
        {
            return new SpecialProducerEventInfo(
                eventId:                Id,
                displayName:            DisplayName,
                description:            Description,
                producer:               Producer,
                producerTargetLevel:    ProducerTargetLevel,
                rewards:                Rewards,
                activableParams:        new MetaActivableParams(
                    isEnabled:                  true,
                    segments:                   Segments,
                    additionalConditions:       null,
                    lifetime:                   MetaActivableLifetimeSpec.ScheduleBased.Instance,
                    isTransient:                false,
                    schedule:                   Schedule,
                    maxActivations:             null,
                    maxTotalConsumes:           null,
                    maxConsumesPerActivation:   null,
                    cooldown:                   MetaActivableCooldownSpec.ScheduleBased.Instance,
                    allowActivationAdjustment:  true));
        }
    }
}
