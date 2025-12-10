// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using Metaplay.Core.Activables;
using Metaplay.Core.Config;
using Metaplay.Core.InAppPurchase;
using Metaplay.Core.Model;
using Metaplay.Core.Offers;
using Metaplay.Core.Player;
using Metaplay.Core.Rewards;
using Metaplay.Core.Serialization;
using System.Collections.Generic;
using System.Linq;

namespace Game.Logic
{
    // This file defines a stripped down version of Idler's old shop offers,
    // which have been replaced by MetaOffers. This file only retains the
    // parts necessary for deserializing the legacy shop offers from existing
    // states and migrating them to the new MetaOffers.
    //
    // - A player's shop offers state is deserialized into a PlayerLegacyShopOffersModel
    //   (previously PlayerShopOffersModel) and that is then migrated into the new
    //   offers in a player entity schema version migration.
    // - Existing DynamicPurchaseContent objects for shop offers, namely
    //   LegacyShopOfferDynamicPurchaseContent (previously ShopOfferDynamicPurchaseContent)
    //   are kept, and have some tricky code to grant the rewards according to the
    //   new offer configs.

    [MetaSerializable]
    public class LegacyShopOfferId : StringId<LegacyShopOfferId> { }

    /// <summary>
    /// This is serialization-compatible with the old ShopOfferModel and
    /// is used to migrate existing players' shop offers into the new offers.
    /// </summary>
    [MetaSerializableDerived(1)]
    public class LegacyShopOfferModel : MetaActivableStateStorage
    {
        [MetaMember(1)] public LegacyShopOfferId ActivableId { get; protected set; }

        LegacyShopOfferModel() { }
    }

    /// <summary>
    /// This is serialization-compatible with the old PlayerShopOffersModel and
    /// is used to migrate existing players' shop offers into the new offers.
    /// </summary>
    [MetaSerializableDerived(1)]
    public class PlayerLegacyShopOffersModel : LegacyMetaActivableSet<LegacyShopOfferId, LegacyShopOfferModel>
    {
        /// <summary>
        /// The ids of the legacy offers that have already been migrated to the new offers.
        /// For now, the states of the migrated legacy offers are not removed, but are
        /// just marked as migrated, in case they're needed again.
        ///
        /// This member does not correspond to any member in the old PlayerShopOffersModel.
        /// </summary>
        [MetaMember(1)] public OrderedSet<LegacyShopOfferId> MigratedOffers { get; private set; } = new OrderedSet<LegacyShopOfferId>();
        /// <summary>
        /// This is used as a just-in-case record of offer purchases that a player started
        /// before the migration, but completed (and were claimed) after the migration.
        ///
        /// This member does not correspond to any member in the old PlayerShopOffersModel.
        /// </summary>
        [MetaMember(2)] public List<LegacyPurchaseRecord> LegacyPurchases { get; private set; } = new List<LegacyPurchaseRecord>();

        [MetaSerializable]
        public class LegacyPurchaseRecord
        {
            [MetaMember(1)] public LegacyShopOfferId            OfferId;
            [MetaMember(2)] public MetaTime                     ContentAssignedAt;
            [MetaMember(3)] public List<MetaPlayerRewardBase>   MigratedRewards;
            [MetaMember(4)] public MetaTime                     ClaimedAt;

            LegacyPurchaseRecord(){ }
            public LegacyPurchaseRecord(LegacyShopOfferId offerId, MetaTime contentAssignedAt, List<MetaPlayerRewardBase> migratedRewards, MetaTime claimedAt)
            {
                OfferId = offerId;
                ContentAssignedAt = contentAssignedAt;
                MigratedRewards = migratedRewards;
                ClaimedAt = claimedAt;
            }
        }

        /// <summary>
        /// Best effort migration of existing legacy offer states into the new MetaOffers.
        /// </summary>
        public void MigrateToMetaOffers(SharedGameConfig sharedGameConfig, PlayerIdlerOfferGroupsModel metaOfferGroups, IPlayerModelBase player)
        {
            int numOffersMigrated               = 0;
            int numActivationsInMigratedOffers  = 0;
            int numPurchasesInMigratedOffers    = 0;

            foreach ((LegacyShopOfferId legacyOfferId, LegacyShopOfferModel legacyOffer) in ActivableStates)
            {
                if (MigratedOffers.Contains(legacyOfferId))
                    continue;

                MetaOfferId newOfferId = MetaOfferId.FromString(legacyOfferId.Value);

                if (sharedGameConfig.Offers.TryGetValue(newOfferId, out IdlerOfferInfo newOfferInfo))
                {
                    MetaOfferPerPlayerStateBase newOfferPerPlayerState = metaOfferGroups.EnsureContainsOfferState(newOfferInfo, player);

                    // Migrate

                    newOfferPerPlayerState.NumActivatedByPlayer += legacyOffer.NumActivated;
                    newOfferPerPlayerState.NumPurchasedByPlayer += legacyOffer.TotalNumConsumed;

                    if (legacyOffer.LatestActivation.HasValue)
                    {
                        MetaActivableState.Activation legacyActivation = legacyOffer.LatestActivation.Value;

                        newOfferPerPlayerState.LatestActivation = new MetaOfferPerPlayerActivation(
                            numPurchased: legacyActivation.NumConsumed,
                            endedAt: legacyActivation.EndAt);
                    }

                    numOffersMigrated++;
                    numActivationsInMigratedOffers += legacyOffer.NumActivated;
                    numPurchasesInMigratedOffers += legacyOffer.TotalNumConsumed;

                    // Remember as migrated

                    MigratedOffers.Add(legacyOfferId);
                }
            }

            player.Log.Info("Migrated {NumOffers} legacy shop offer states into MetaOffers. {NumActivations} activations, {NumPurchases} purchases.",
                numOffersMigrated, numActivationsInMigratedOffers, numPurchasesInMigratedOffers);
        }
    }

    /// <summary>
    /// This is serialization-compatible with the old ShopOfferDynamicPurchaseContent
    /// and is used to retain existing purchase states.
    ///
    /// Note that the old ShopOfferDynamicPurchaseContent held a ShopOfferInfo instead
    /// of just the id; and furthermore didn't persist the rewards, but just accessed
    /// them via the ShopOfferInfo config reference. In hindsight that was a bit fragile,
    /// which now manifests in this class having to pull some tricks (in
    /// <see cref="SetMigratedRewards"/>, a on-deserialized method) to populate the
    /// rewards based on the config of the *new* offer corresponding to the old offer.
    /// </summary>
    [MetaSerializableDerived(1)]
    public class LegacyShopOfferDynamicPurchaseContent : DynamicPurchaseContent
    {
        [MetaMember(1)] public LegacyShopOfferId            OfferId;
        [MetaMember(2)] public MetaTime                     ContentAssignedAt;
        [MetaMember(3)] public List<MetaPlayerRewardBase>   MigratedRewards;

        LegacyShopOfferDynamicPurchaseContent(){ }

        /// <summary>
        /// On deserialization, if <see cref="MigratedRewards"/> is not already set,
        /// attempt to set it based on the config of the new MetaOffer that corresponds
        /// to this old shop offer.
        /// </summary>
        [MetaOnDeserialized]
        public void SetMigratedRewards(MetaOnDeserializedParams par)
        {
            if (MigratedRewards == null)
            {
                if (par.Resolver != null)
                {
                    MetaOfferId         newOfferId      = MetaOfferId.FromString(OfferId.Value);
                    MetaOfferInfoBase   newOfferInfo    = (MetaOfferInfoBase)par.Resolver.TryResolveReference(typeof(MetaOfferInfoBase), newOfferId);

                    if (newOfferInfo != null)
                        MigratedRewards = newOfferInfo.Rewards;
                }
            }
        }

        public override List<MetaPlayerRewardBase> PurchaseRewards => MigratedRewards ?? new List<MetaPlayerRewardBase>();

        public override void OnPurchased(IPlayerModelBase playerBase)
        {
            PlayerModel player = (PlayerModel)playerBase;

            if (MigratedRewards == null || MigratedRewards.Count == 0)
            {
                string badRewardsStr = MigratedRewards == null ? "null" : "zero";
                player.Log.Warning("Purchased legacy shop offer {OfferId} (ContentAssignedAt={ContentAssignedAt}) which has {BadRewards} rewards; something went wrong in migration? A record will be kept.", OfferId, ContentAssignedAt, badRewardsStr);
            }

            // Keep a record in case something goes wrong in the legacy migration.
            // In addition to the usual EventStream, an additional ad-hoc record is kept to ensure retention.
            player.EventStream.Event(new PlayerLegacyShopOfferClaimed(OfferId, ContentAssignedAt, MigratedRewards));
            player.LegacyShopOffers.LegacyPurchases.Add(new PlayerLegacyShopOffersModel.LegacyPurchaseRecord(
                offerId:            OfferId,
                contentAssignedAt:  ContentAssignedAt,
                migratedRewards:    MigratedRewards,
                claimedAt:          player.CurrentTime));
        }
    }
}
