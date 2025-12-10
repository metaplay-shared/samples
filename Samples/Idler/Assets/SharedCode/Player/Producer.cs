// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Model;
using Metaplay.Core.Math;
using Metaplay.Core;
using System.Collections.Generic;
using Metaplay.Core.Config;

namespace Game.Logic
{
    /// <summary>
    /// Runtime instance of a <see cref="ProducerInfo"/>.
    /// </summary>
    [MetaSerializable]
    public class ProducerModel
    {
        static readonly F64 ValueMultiplier = F64.Ratio100(0_30);   // 30% per level
        static readonly F64 ValueExponent   = F64.Ratio100(1_05);   // 1.05

        [MetaMember(1)] public ProducerInfo     Info        { get; }
        [MetaMember(2)] public int              Level       { get; set; } = 1;
        [MetaMember(3)] public MetaTime         StartedAt   { get; set; }
        [MetaMember(4)] public ProducerSourceInfo StartedBy { get; set; }

        public MetaTime FinishedAt      => StartedAt + Info.Duration;
        public int      ProduceValue    => F64.RoundToInt(Info.BaseValue * (F64.One + (Level - 1) * ValueMultiplier));

        [MetaDeserializationConstructor]
        public ProducerModel(ProducerInfo info, int level, MetaTime startedAt, ProducerSourceInfo startedBy)
        {
            Info = info;
            Level = level;
            StartedAt = startedAt;
            StartedBy = startedBy;
        }

        public ProducerModel(ProducerInfo info, MetaTime currentTime, ProducerSourceInfo startedBy)
        {
            Info        = info;
            StartedAt   = currentTime;
            StartedBy   = startedBy;
        }

        public int GetUpgradeCost(IEnumerable<HappyHourModel> activeHappyHours, IGameConfigDataResolver configResolver)
        {
            return F64.RoundToInt(Info.GetUpgradeCost(activeHappyHours, configResolver) * F64.Pow(ValueExponent, F64.FromInt(Level)));
        }

        public float GetProgressAt(MetaTime atTime) => (atTime - StartedAt).Milliseconds / (float)Info.Duration.Milliseconds;
    }

    [MetaSerializable]
    public abstract class ProducerSourceInfo {}

    [MetaSerializableDerived(1)]
    public class ProducerSourceLiveOpsEvent : ProducerSourceInfo
    {
        [MetaMember(1)] public MetaGuid EventId { get; }

        [MetaDeserializationConstructor]
        public ProducerSourceLiveOpsEvent(MetaGuid eventId)
        {
            EventId = eventId;
        }
    }
}