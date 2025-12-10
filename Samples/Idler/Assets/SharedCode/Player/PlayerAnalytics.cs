// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using Metaplay.Core.Analytics;
using Metaplay.Core.Model;
using Metaplay.Core.Player;
using Metaplay.Core.Rewards;
using System.Collections.Generic;
using static System.FormattableString;

namespace Game.Logic
{
    // GAME-SPECIFIC PLAYER EVENTS

    public static class PlayerEventCodes
    {
        public const int ResourcesChanged   = 1;    // Obsolete, kept for backward-compatibility

        public const int ProducerUnlocked      = 2001;
        public const int ProducerUpgraded      = 2000;

        public const int AdminUnlockedProducer = 2100;
        public const int AdminSetWallet        = 2101;

        public const int DebugGainGems      = 3000;
        public const int DebugGainGold      = 3001;

        public const int LegacyShopOfferClaimed = 10_000;
    }

    // \todo [petri] obsolete, but need to figure out how to safely remove as instances may be in PlayerModel.EventLog
    [AnalyticsEvent(PlayerEventCodes.ResourcesChanged)]
    public class PlayerEventResourcesChanged : PlayerEventBase
    {
        override public string EventDescription => "OBSOLETE";

        public PlayerEventResourcesChanged() { }
    }

    [AnalyticsEvent(PlayerEventCodes.ProducerUnlocked, canTrigger: true)]
    public class PlayerEventProducerUnlocked : PlayerEventBase
    {
        [MetaMember(1)] public ProducerTypeId   ProducerType    { get; private set; }
        [MetaMember(2)] public int              GoldCost        { get; private set; }

        public override string EventDescription => Invariant($"Unlocked {ProducerType} for {GoldCost} Gold.");

        PlayerEventProducerUnlocked() { }
        public PlayerEventProducerUnlocked(ProducerTypeId producerType, int goldCost)
        {
            ProducerType    = producerType;
            GoldCost        = goldCost;
        }
    }

    [AnalyticsEvent(PlayerEventCodes.ProducerUpgraded, canTrigger: true)]
    public class PlayerEventProducerUpgraded : PlayerEventBase
    {
        [MetaMember(1)] public ProducerTypeId   ProducerType    { get; private set; }
        [MetaMember(2)] public int              ToLevel         { get; private set; }
        [MetaMember(3)] public int              GoldCost        { get; private set; }

        public override string EventDescription => Invariant($"Upgraded {ProducerType} to level {ToLevel} for {GoldCost} Gold.");

        PlayerEventProducerUpgraded() { }
        public PlayerEventProducerUpgraded(ProducerTypeId producerType, int toLevel, int goldCost)
        {
            ProducerType    = producerType;
            ToLevel         = toLevel;
            GoldCost        = goldCost;
        }
    }

    [AnalyticsEvent(PlayerEventCodes.AdminUnlockedProducer, canTrigger: true)]
    public class PlayerEventProducerAdminUnlocked : PlayerEventBase
    {
        [MetaMember(1)] public ProducerTypeId ProducerType { get; private set; }

        public override string EventDescription => Invariant($"Unlocked {ProducerType} for free.");

        PlayerEventProducerAdminUnlocked() { }
        public PlayerEventProducerAdminUnlocked(ProducerTypeId producerType)
        {
            ProducerType = producerType;
        }
    }

    [AnalyticsEvent(PlayerEventCodes.AdminSetWallet, canTrigger: true)]
    public class PlayerEventAdminSetWallet : PlayerEventBase
    {
        [MetaMember(1)] public int? NewGold { get; private set; }
        [MetaMember(2)] public int? NewGems { get; private set; }

        public override string EventDescription
        {
            get
            {
                List<string> changes = new List<string>();
                changes.Add(Invariant($"Player wallet set. "));

                if (NewGold.HasValue)
                    changes.Add(Invariant($"Gold = {NewGold.Value}, "));
                else
                    changes.Add(Invariant($"Gold unchanged, "));

                if (NewGems.HasValue)
                    changes.Add(Invariant($"Gems = {NewGems.Value}."));
                else
                    changes.Add(Invariant($"Gems unchanged."));

                return string.Join("", changes);
            }
        }

        PlayerEventAdminSetWallet() { }
        public PlayerEventAdminSetWallet(int? newGold, int? newGems)
        {
            NewGold = newGold;
            NewGems = newGems;
        }
    }

    [AnalyticsEvent(PlayerEventCodes.LegacyShopOfferClaimed)]
    public class PlayerLegacyShopOfferClaimed : PlayerEventBase
    {
        [MetaMember(1)] public LegacyShopOfferId            OfferId             { get; private set; }
        [MetaMember(2)] public MetaTime                     ContentAssignedAt   { get; private set; }
        [FirebaseAnalyticsIgnore] // unsupported aggregate type
        [MetaMember(3)] public List<MetaPlayerRewardBase>   MigratedRewards     { get; private set; }

        public override string EventDescription => Invariant($"Legacy shop offer {OfferId} purchased with {MigratedRewards?.Count ?? 0} rewards.");

        PlayerLegacyShopOfferClaimed() { }
        public PlayerLegacyShopOfferClaimed(LegacyShopOfferId offerId, MetaTime contentAssignedAt, List<MetaPlayerRewardBase> migratedRewards)
        {
            OfferId = offerId;
            ContentAssignedAt = contentAssignedAt;
            MigratedRewards = migratedRewards;
        }
    }

    [AnalyticsEvent(PlayerEventCodes.DebugGainGems)]
    public class PlayerEventDebugGainGems : PlayerEventBase
    {
        [MetaMember(1)] public int Amount { get; private set; }

        public override string EventDescription => Invariant($"{Amount} Gems.");

        PlayerEventDebugGainGems() { }
        public PlayerEventDebugGainGems(int amount) { Amount = amount; }
    }

    [AnalyticsEvent(PlayerEventCodes.DebugGainGold)]
    public class PlayerEventDebugGainGold : PlayerEventBase
    {
        [MetaMember(1)] public int Amount { get; private set; }

        public override string EventDescription => Invariant($"{Amount} Gold.");

        PlayerEventDebugGainGold() { }
        public PlayerEventDebugGainGold(int amount) { Amount = amount; }
    }
}
