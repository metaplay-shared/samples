// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using Metaplay.Core.Config;
using Metaplay.Core.Math;
using Metaplay.Core.Model;
using System.Collections.Generic;

namespace Game.Logic
{
    /// <summary>
    /// Register a <see cref="StringId{T}"/>-based identifier type for <see cref="ProducerKindInfo"/>.
    /// The registered type can be used as an efficient, type-safe identifier.
    /// </summary>
    [MetaSerializable]
    public class ProducerKindId : StringId<ProducerKindId> { }

    /// <summary>
    /// Register a <see cref="StringId{T}"/>-based identifier type for <see cref="ProducerInfo"/>.
    /// The registered type can be used as an efficient, type-safe identifier.
    /// </summary>
    [MetaSerializable]
    public class ProducerTypeId : StringId<ProducerTypeId>
    {
        public static readonly ProducerTypeId None = FromString("None");
    }

    /// <summary>
    /// Info class for a producer kind (i.e., a family of producers).
    /// Each producer kind is uniquely identified by the `Id` member.
    /// </summary>
    [MetaSerializable]
    public class ProducerKindInfo : IGameConfigData<ProducerKindId>, IGameConfigPostLoad
    {
        [MetaMember(1)] public ProducerKindId   Id      { get; private set; }   // Unique id of the producer kind.
        [MetaMember(2)] public string           Name    { get; private set; }   // Display name for the producer kind.

        public ProducerKindInfo()
        {
            
        }
        
        [MetaGameConfigBuildConstructor]
        public ProducerKindInfo(ProducerKindId id, string name)
        {
            Id = id;
            Name = name;
        }

        public ProducerKindId ConfigKey => Id;

        void IGameConfigPostLoad.PostLoad()
        {
            MetaDebug.Assert(!string.IsNullOrEmpty(Name), "ProducerKindInfo {0} has empty Name", Id);
        }
    }

    /// <summary>
    /// A category defining certain aspects of producer behavior.
    /// </summary>
    [MetaSerializable]
    public enum ProducerCategory
    {
        /// <summary> Normal producer: can be unlocked by normal means. </summary>
        Normal = 0,
        /// <summary> Special event producer: cannot be unlocked normally, only available during a special event. </summary>
        Event = 1,
    }

    /// <summary>
    /// Info class for a producer instance. Uniquely identified by the `Id` member.
    /// </summary>
    /// <remarks>
    /// Note the usage of <see cref="ProducerKindInfo"/> in <see cref="Kind"/>. Config items can refer to each other
    /// using <see cref="MetaRef{TItem}"/>s, which are represented as item ids in the config source data (e.g. spreadsheets).
    /// When the config is loaded at runtime, the reference is automatically resolved to refer to the concrete in-memory item.
    /// </remarks>
    [MetaSerializable]
    public class ProducerInfo : IGameConfigData<ProducerTypeId>, IGameConfigPostLoad
    {
        [MetaMember(1)] public ProducerTypeId               Id                  { get; private set; }   // Unique id of the producer.
        [MetaMember(2)] public string                       Name                { get; private set; }   // Display name of the producer.
        [MetaMember(3)] public MetaRef<ProducerKindInfo>    Kind                { get; private set; }   // Reference to the Kind of the producer. Note that this is automatically resolved when importing from a .csv file.
        [MetaMember(8)] public ProducerCategory             Category            { get; private set; }   // Category: Normal or Event.
        [MetaMember(4)] private int                         UnlockCost          { get; }           // Cost to unlock the producer. \note Private to not confuse modified vs unmodified. Use UnmodifiedUnlockCost or GetUnlockCost.
        [MetaMember(5)] private int                         UpgradeCost         { get; }           // Base cost to upgrade the producer. Goes up based on level. \note Private to not confuse modified vs unmodified. Use UnmodifiedUpgradeCost or GetUpgradeCost.
        [MetaMember(6)] public MetaDuration                 Duration            { get; private set; }   // Duration how often the producer produces gold.
        [MetaMember(7)] public F64                          BaseValue           { get; private set; }   // Base value how much gold the producer produces (in fixed-point, goes up by level).

        public int UnmodifiedUnlockCost => UnlockCost;
        public int UnmodifiedUpgradeCost => UpgradeCost;

        public int GetUnlockCost(IEnumerable<HappyHourModel> activeHappyHours, IGameConfigDataResolver configResolver) => GetCost(activeHappyHours, UnlockCost, configResolver);
        public int GetUpgradeCost(IEnumerable<HappyHourModel> activeHappyHours, IGameConfigDataResolver configResolver) => GetCost(activeHappyHours, UpgradeCost, configResolver);

        public ProducerInfo()
        {
        }

        [MetaGameConfigBuildConstructor, MetaDeserializationConstructor]
        public ProducerInfo(ProducerTypeId id, string name, MetaRef<ProducerKindInfo> kind, ProducerCategory category, int unlockCost, int upgradeCost, MetaDuration duration, F64 baseValue)
        {
            Id = id;
            Name = name;
            Kind = kind;
            Category = category;
            UnlockCost = unlockCost;
            UpgradeCost = upgradeCost;
            Duration = duration;
            BaseValue = baseValue;
        }

        int GetCost(IEnumerable<HappyHourModel> activeHappyHours, int normalCost, IGameConfigDataResolver configResolver)
        {
            F64 costFactor = F64.One;

            foreach (HappyHourModel activeHappyHour in activeHappyHours)
            {
                if (activeHappyHour.Info.Producer.GetItem(configResolver).Id == Id)
                    costFactor *= activeHappyHour.Info.CostFactor;
            }

            return F64.RoundToInt(costFactor * normalCost);
        }

        //ProducerTypeId IGameConfigData<ProducerTypeId>.ConfigKey => Id;
        public ProducerTypeId ConfigKey => Id;

        void IGameConfigPostLoad.PostLoad()
        {
            MetaDebug.Assert(Kind != null, "Producer {0} has no valid kind", Id);
            MetaDebug.Assert(Category == ProducerCategory.Normal || Category == ProducerCategory.Event, "Producer {0} has unknown Category {1}", Id, Category);
        }
    }
}
