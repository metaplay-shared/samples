// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using Metaplay.Core.Activables;
using Metaplay.Core.Config;
using Metaplay.Core.Math;
using Metaplay.Core.Model;
using Metaplay.Core.Player;
using Metaplay.Core.Schedule;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.Logic
{
    /// <summary>
    /// This metadata declaration lets the Metaplay SDK know about
    /// Idler's "Happy Hour" kind of activables.
    ///
    /// Associated attributes are found on game config and player sub-model types:
    /// A <see cref="MetaActivableConfigDataAttribute"/> on <see cref="HappyHourInfo"/>,
    /// and a <see cref="MetaActivableSetAttribute"/> on <see cref="PlayerHappyHoursModel"/>.
    /// These attributes tell the SDK where configuration and player state
    /// can be found for Happy Hours, which allows it to operate on them in an
    /// abstract manner, for example in order to display them in the dashboard.
    /// </summary>
    [MetaActivableKindMetadata(
        id:             "HappyHour",
        displayName:    "Happy Hour",
        description:    "Simple events that reduce producer cost",
        categoryId:     "Event")]
    public static class ActivableKindMetadataHappyHour
    {
    }

    [MetaSerializable]
    public class HappyHourId : StringId<HappyHourId> { }

    /// <summary>
    /// Config data for a happy hour event.
    /// A happy hour is an event that, while active, reduces the unlock and upgrade costs of a specific producer.
    /// A happy hour becomes available according to the configured <see cref="ActivableParams"/>.
    /// </summary>
    /// <remarks>
    /// <c>HappyHourInfo</c> is not parsed from config directly; instead <see cref="HappyHourSourceConfigItem"/>
    /// is parsed and converted to <c>HappyHourInfo</c>.
    /// </remarks>
    [MetaSerializable]
    [MetaActivableConfigData("HappyHour")]
    public class HappyHourInfo : IMetaActivableConfigData<HappyHourId>
    {
        [MetaMember(1)] public HappyHourId                HappyHourId     { get; private set; }
        [MetaMember(2)] public string                     DisplayName     { get; private set; }
        [MetaMember(3)] public string                     Description     { get; private set; }
        [MetaMember(7), ServerOnly] public MetaActivableTimelineSettings Timeline { get; private set; }
        [MetaMember(4)] public MetaConfigId<ProducerInfo> Producer        { get; private set; }
        [MetaMember(5)] public F64                        CostFactor      { get; private set; }
        [MetaMember(6)] public MetaActivableParams        ActivableParams { get; private set; }

        public HappyHourId ActivableId   => HappyHourId;
        public HappyHourId ConfigKey     => HappyHourId;

        public string DisplayShortInfo => null;

        public HappyHourInfo(){ }
        
        [MetaGameConfigBuildConstructor]
        public HappyHourInfo(HappyHourId happyHourId, string displayName, string description, MetaActivableTimelineSettings timeline, MetaConfigId<ProducerInfo> producer, F64 costFactor, MetaActivableParams activableParams)
        {
            HappyHourId = happyHourId ?? throw new ArgumentNullException(nameof(happyHourId));
            DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
            Description = description ?? throw new ArgumentNullException(nameof(description));
            Timeline = timeline;
            Producer = producer ?? throw new ArgumentNullException(nameof(producer));
            CostFactor = costFactor;
            ActivableParams = activableParams ?? throw new ArgumentNullException(nameof(activableParams));
        }
    }

    /// <summary>
    /// Source-config item for <see cref="HappyHourInfo"/>.
    /// This is parsed from config sheets and converted to <see cref="HappyHourInfo"/> in <see cref="ToConfigData"/>.
    /// The purpose of this intermediate conversion step is to help with parsing the nested <see cref="MetaActivableParams"/> from a flat sheet structure.
    /// </summary>
    public class HappyHourSourceConfigItem : IGameConfigSourceItem<HappyHourId, HappyHourInfo>
    {
        public HappyHourId                          Id;
        public string                               DisplayName;
        public string                               Description;
        public MetaActivableTimelineSettings        Timeline;
        public MetaConfigId<ProducerInfo>           Producer;
        public F64                                  CostFactor;

        public bool                                 IsEnabled       = true;
        public List<MetaRef<PlayerSegmentInfoBase>> Segments;
        public MetaScheduleBase                     Schedule;

        public HappyHourId ConfigKey => Id;

        public HappyHourSourceConfigItem()
        {
        }

        [MetaGameConfigBuildConstructor]
        public HappyHourSourceConfigItem(HappyHourId id, string displayName, string description, MetaConfigId<ProducerInfo> producer, F64 costFactor, bool isEnabled, MetaScheduleBase schedule, List<MetaRef<PlayerSegmentInfoBase>> segments = null, MetaActivableTimelineSettings timeline = null)
        {
            Id = id;
            DisplayName = displayName;
            Description = description;
            Timeline = timeline;
            Producer = producer;
            CostFactor = costFactor;
            IsEnabled = isEnabled;
            Segments = segments;
            Schedule = schedule;
        }

        public HappyHourInfo ToConfigData(GameConfigBuildLog buildLog)
        {
            return new HappyHourInfo(
                happyHourId:        Id,
                displayName:        DisplayName,
                description:        Description,
                timeline:           Timeline,
                producer:           Producer,
                costFactor:         CostFactor,
                activableParams:    new MetaActivableParams(
                    isEnabled:                  IsEnabled,
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
