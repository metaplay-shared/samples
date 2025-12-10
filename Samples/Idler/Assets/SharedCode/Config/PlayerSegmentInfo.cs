// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using Metaplay.Core.Model;
using Metaplay.Core.Player;
using System;
using static System.FormattableString;

namespace Game.Logic
{
    [MetaSerializableDerived(1)]
    public class PlayerSegmentInfo : PlayerSegmentInfoBase
    {
        public PlayerSegmentInfo(){ }
        public PlayerSegmentInfo(PlayerSegmentId segmentId, PlayerCondition playerCondition, string displayName, string description)
            : base(segmentId, playerCondition, displayName, description)
        {
        }
    }

    public class PlayerSegmentInfoSourceItem : PlayerSegmentBasicInfoSourceItemBase<PlayerSegmentInfo>
    {
        protected override PlayerSegmentInfo CreateSegmentInfo(PlayerSegmentId segmentId, PlayerSegmentBasicCondition playerCondition, string displayName, string description)
        {
            return new PlayerSegmentInfo(segmentId, playerCondition, displayName, description);
        }
    }

    [MetaSerializableDerived(1)]
    public class PlayerPropertyIdGems : TypedPlayerPropertyId<int>
    {
        public override int GetTypedValueForPlayer(IPlayerModelBase player) => ((PlayerModel)player).Wallet.NumGems;
        public override string DisplayName => "Gems";
    }

    [MetaSerializableDerived(2)]
    public class PlayerPropertyIdGold : TypedPlayerPropertyId<int>
    {
        public override int GetTypedValueForPlayer(IPlayerModelBase player) => ((PlayerModel)player).Wallet.NumGold;
        public override string DisplayName => "Gold";
    }

    [MetaSerializableDerived(3)]
    public class PlayerPropertyIdProducerLevel : TypedPlayerPropertyId<int>
    {
        [MetaMember(1)] public MetaRef<ProducerInfo> ProducerType { get; private set; }

        PlayerPropertyIdProducerLevel(){ }
        public PlayerPropertyIdProducerLevel(MetaRef<ProducerInfo> producerType)
        {
            ProducerType = producerType ?? throw new ArgumentNullException(nameof(producerType));
        }

        public override int GetTypedValueForPlayer(IPlayerModelBase player)
        {
            if (((PlayerModel)player).Producers.TryGetValue(ProducerType.Ref.Id, out ProducerModel producer))
                return producer.Level;

            return 0;
        }
        // \note In some cases, DisplayName is accessed without MetaRefs being resolved. For example during debug pretty printing during the config build step
        public override string DisplayName => $"{(ProducerType.IsResolved ? ProducerType.Ref.Name : Util.ObjectToStringInvariant(ProducerType.KeyObject))} level";
        // \note This uses ProducerType.KeyObject directly, instead of ProducerType.Ref.Id,
        //       because it's desirable for ToString to be usable even if ProducerType is unresolved.
        public override string ToString() => Invariant($"{nameof(PlayerPropertyIdProducerLevel)}({ProducerType.KeyObject})");
    }

    [MetaSerializableDerived(4)]
    public class PlayerPropertyLastKnownCountry : TypedPlayerPropertyId<string>
    {
        public override string GetTypedValueForPlayer(IPlayerModelBase player) => player.LastKnownLocation?.Country.IsoCode;
        public override string DisplayName => $"Last known country";
    }

    [MetaSerializableDerived(5)]
    public class PlayerPropertyAccountCreatedAt : TypedPlayerPropertyId<MetaTime>
    {
        public override MetaTime GetTypedValueForPlayer(IPlayerModelBase player) => player.Stats.CreatedAt;
        public override string DisplayName => $"Account creation time";
    }

    [MetaSerializableDerived(6)]
    public class PlayerPropertyAccountAge : TypedPlayerPropertyId<MetaDuration>
    {
        public override MetaDuration GetTypedValueForPlayer(IPlayerModelBase player) => player.CurrentTime - player.Stats.CreatedAt;
        public override string DisplayName => $"Account age";
    }

    [MetaSerializableDerived(7)]
    public class PlayerPropertyTimeSinceLastLogin : TypedPlayerPropertyId<MetaDuration>
    {
        public override MetaDuration GetTypedValueForPlayer(IPlayerModelBase player) => player.CurrentTime - player.Stats.LastLoginAt;
        public override string DisplayName => $"Time since last login";
    }
}
