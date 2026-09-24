using Metaplay.Core;
using Metaplay.Core.Activables;
using Metaplay.Core.Config;
using Metaplay.Core.InAppPurchase;
using Metaplay.Core.Model;
using Metaplay.Core.Offers;
using Metaplay.Core.Player;
using Metaplay.Core.Rewards;
using Metaplay.Core.Schedule;
using System.Collections.Generic;

namespace Game.Logic
{
    /// <summary>
    /// One Shop offer, built on the SDK's <see cref="MetaOfferInfoBase"/> so it gets the SDK's offer lifecycle,
    /// Dashboard pages and LiveOps timeline entry (<c>docs/offers.md</c>). Wallet-priced and demo-priced (IAP) offers
    /// share this type, and <see cref="HasInGameCurrencyCost"/> tells them apart. They cannot be two subclasses,
    /// because one config library holds both kinds and the SDK's config repository generator requires a concrete type.
    /// The SDK's wallet purchase path calls <see cref="PayInGameCurrencyCost"/> and then consumes the rewards, and the
    /// second step cannot refuse a grant over a cap because the price is already spent. So the price and
    /// <see cref="Contents"/> are applied in one wallet transaction. <see cref="WalletOfferReward"/> therefore grants
    /// nothing, while a demo offer's <see cref="DemoOfferReward"/> grants the contents.
    /// </summary>
    [MetaSerializableDerived(101)]
    public class OfferInfo : MetaOfferInfoBase
    {
        [MetaMember(1)] public string        IconId        { get; private set; }
        [MetaMember(2)] public CurrencyType? PriceCurrency { get; private set; }
        [MetaMember(3)] public int?          PriceAmount   { get; private set; }
        [MetaMember(4)] public RewardBundle  Contents      { get; private set; }

        public OfferInfo() { }

        /// <summary>Creates a wallet-priced offer. The price and contents are applied in one wallet transaction.</summary>
        public static OfferInfo Wallet(
            MetaOfferId  offerId,
            string       displayName,
            string       whyThisOffer,
            string       iconId,
            CurrencyType priceCurrency,
            int          priceAmount,
            RewardBundle contents,
            int?         maxPurchasesPerPlayer     = 1,
            int?         maxPurchasesPerActivation = null,
            List<MetaRef<PlayerSegmentInfoBase>> segments = null) =>
            new OfferInfo(
                offerId, displayName, whyThisOffer, iconId, inAppProduct: null,
                priceCurrency, priceAmount, contents,
                maxPurchasesPerPlayer, maxPurchasesPerActivation, segments, additionalConditions: null);

        /// <summary>
        /// Creates a demo-priced (IAP) offer, bought through the SDK's dynamic-content in-app purchase flow.
        /// <paramref name="inAppProduct"/> must reference a product with
        /// <see cref="InAppProductInfoBase.HasDynamicContent"/> set, such as one created by
        /// <see cref="DemoInAppProductInfo.ForOffer"/>.
        /// </summary>
        public static OfferInfo Demo(
            MetaOfferId                    offerId,
            string                         displayName,
            string                         whyThisOffer,
            string                         iconId,
            MetaRef<InAppProductInfoBase>  inAppProduct,
            RewardBundle                   contents,
            List<PlayerCondition>          additionalConditions  = null,
            List<MetaRef<PlayerSegmentInfoBase>> segments        = null,
            int?                           maxPurchasesPerPlayer = 1) =>
            new OfferInfo(
                offerId, displayName, whyThisOffer, iconId, inAppProduct,
                priceCurrency: null, priceAmount: null, contents,
                maxPurchasesPerPlayer, maxPurchasesPerActivation: null, segments, additionalConditions);

        /// <summary>
        /// Builds an offer from a <c>GameConfigSource/Offers.csv</c> row. A wallet-priced row sets
        /// <paramref name="priceCurrency"/> and <paramref name="priceAmount"/>. A demo-priced row sets
        /// <paramref name="inAppProduct"/>. The price kind decides how the offer is paid for (see the class summary).
        /// <para>
        /// <paramref name="precursorOffer"/> chains offers: this offer is available only after the player bought
        /// the precursor and its activation ended. It becomes an SDK <see cref="MetaOfferPrecursorCondition"/>
        /// instead of a stored field.
        /// </para>
        /// </summary>
        [MetaGameConfigBuildConstructor]
        public OfferInfo(
            MetaOfferId                          offerId,
            string                               displayName,
            string                               whyThisOffer,
            string                               iconId,
            RewardBundle                         contents,
            MetaRef<InAppProductInfoBase>        inAppProduct              = null,
            CurrencyType?                        priceCurrency             = null,
            int?                                 priceAmount               = null,
            int?                                 maxPurchasesPerPlayer     = null,
            int?                                 maxPurchasesPerActivation = null,
            List<MetaRef<PlayerSegmentInfoBase>> segments                  = null,
            MetaOfferId                          precursorOffer            = null)
            : this(
                offerId, displayName, whyThisOffer, iconId, inAppProduct,
                priceCurrency, priceAmount, contents,
                maxPurchasesPerPlayer, maxPurchasesPerActivation, segments,
                additionalConditions: precursorOffer == null
                    ? null
                    : new List<PlayerCondition> { new MetaOfferPrecursorCondition(precursorOffer, purchased: true, delay: MetaDuration.Zero) })
        {
        }

        OfferInfo(
            MetaOfferId                          offerId,
            string                                displayName,
            string                                whyThisOffer,
            string                                iconId,
            MetaRef<InAppProductInfoBase>         inAppProduct,
            CurrencyType?                         priceCurrency,
            int?                                  priceAmount,
            RewardBundle                          contents,
            int?                                  maxPurchasesPerPlayer,
            int?                                  maxPurchasesPerActivation,
            List<MetaRef<PlayerSegmentInfoBase>>  segments,
            List<PlayerCondition>                 additionalConditions)
            : base(
                offerId:                        offerId,
                displayName:                     displayName,
                description:                     whyThisOffer,
                inAppProduct:                    inAppProduct,
                rewards:                         new List<MetaPlayerRewardBase> { priceCurrency.HasValue ? new WalletOfferReward(contents) : (MetaPlayerRewardBase)new DemoOfferReward(offerId, contents) },
                maxActivationsPerPlayer:         null,
                maxPurchasesPerPlayer:           maxPurchasesPerPlayer,
                maxPurchasesPerOfferGroup:       null,
                maxPurchasesPerActivation:       maxPurchasesPerActivation,
                segments:                        segments,
                additionalConditions:            additionalConditions,
                isSticky:                        true,
                storeDescriptionTranslationId:   null,
                storeDescription:                null,
                developerOnly:                   false)
        {
            IconId        = iconId;
            PriceCurrency = priceCurrency;
            PriceAmount   = priceAmount;
            Contents      = contents;
        }

        /// <summary>
        /// The wallet price, or null for a demo-priced offer.
        /// <para>
        /// It returns null instead of throwing because the Dashboard and log pretty-printing read every property
        /// by reflection, and a throwing getter would print a stack trace inside each demo-priced offer.
        /// </para>
        /// </summary>
        CurrencyAmount Price => PriceCurrency.HasValue ? new CurrencyAmount(PriceCurrency.Value, PriceAmount!.Value) : null;

        public override bool HasInGameCurrencyCost => PriceCurrency.HasValue;

        /// <summary>The price and the contents as one wallet transaction, so both apply or neither does.</summary>
        WalletTransaction PurchaseTransaction() =>
            WalletTransaction.Exchange(EconomyFeature.Offers, EconomyReason.OfferPurchase, Price, Contents, EconomyContentId.FromString(OfferId.Value));

        /// <summary>
        /// Whether the whole exchange would succeed, not only whether the player can pay the price.
        /// <para>
        /// The SDK records the purchase after <see cref="PayInGameCurrencyCost"/> regardless of what the wallet
        /// did, so a grant the wallet would refuse, such as one that exceeds a balance cap, must be refused here.
        /// Otherwise the offer counts as bought, and a one-time offer sells out, with nothing paid or granted.
        /// </para>
        /// </summary>
        public override bool CanAffordInGameCurrencyCost(IPlayerModelBase player, MetaOfferGroupInfoBase offerGroupInfo) =>
            player is PlayerModel model && model.PreviewWallet(PurchaseTransaction()).IsSuccess;

        /// <summary>
        /// The currency whose cap this offer's contents would exceed, or <see cref="CurrencyType.None"/> when
        /// the purchase would succeed or fails for another reason. <see cref="SpinWheelPolicy.OverflowingCurrency"/>
        /// does the same for the wheel.
        /// <para>
        /// The Shop uses it to tell the player which balance is full, instead of showing a Buy button. For a
        /// wallet-priced offer, <see cref="CanAffordInGameCurrencyCost"/> would refuse that purchase as "cannot
        /// afford". For an in-app purchase offer, the grant would take the balance over its cap
        /// (<c>docs/offers.md</c>).
        /// </para>
        /// </summary>
        public CurrencyType OverflowingCurrency(PlayerModel player)
        {
            if (player == null)
                return CurrencyType.None;

            WalletTransaction transaction = HasInGameCurrencyCost
                ? PurchaseTransaction()
                : WalletTransaction.Grant(EconomyFeature.Offers, EconomyReason.OfferPurchase, Contents, EconomyContentId.FromString(OfferId.Value));

            WalletSettlement settlement = player.PreviewWallet(transaction);
            return settlement.Refusal == WalletRefusal.CapExceeded ? settlement.RefusedCurrency : CurrencyType.None;
        }

        public override void PayInGameCurrencyCost(IPlayerModelBase player, MetaOfferGroupInfoBase offerGroupInfo)
        {
            if (player is not PlayerModel model)
                return;

            AnalyticsCorrelationId correlation = AnalyticsCorrelationId.Create(model.PlayerId, model.CurrentTime, "offer_" + OfferId.Value);

            // The SDK calls CanAffordInGameCurrencyCost first, which settles this same transaction, so this succeeds.
            model.ApplyWallet(PurchaseTransaction(), commit: true, correlation);
        }

        public override string GetInGameCurrencyCostForDashboard() => HasInGameCurrencyCost ? $"{PriceAmount} {PriceCurrency}" : null;
    }

    /// <summary>
    /// Describes the <see cref="OfferInfo.Contents"/> of a wallet-priced offer (<see cref="OfferInfo.Wallet"/>)
    /// to the SDK's reward system and Dashboard. <see cref="Consume"/> does nothing, because
    /// <see cref="OfferInfo.PayInGameCurrencyCost"/> already granted the contents together with the price.
    /// </summary>
    [MetaSerializableDerived(1)]
    public class WalletOfferReward : MetaPlayerReward<PlayerModel>
    {
        [MetaMember(1)] public RewardBundle Contents { get; private set; }

        public WalletOfferReward() { }
        public WalletOfferReward(RewardBundle contents) { Contents = contents; }

        public override void Consume(PlayerModel playerModel, IRewardSource source)
        {
            // Intentionally empty. See the class summary.
        }
    }

    /// <summary>
    /// What a demo-priced offer (<see cref="OfferInfo.Demo"/>) grants. Unlike <see cref="WalletOfferReward"/>,
    /// <see cref="Consume"/> grants the contents. After the purchase is validated, the SDK's dynamic-content
    /// claim path calls <see cref="Consume"/> on every reward in <see cref="MetaOfferInfoBase.Rewards"/>, and
    /// that is the only place the contents are granted.
    /// </summary>
    [MetaSerializableDerived(2)]
    public class DemoOfferReward : MetaPlayerReward<PlayerModel>
    {
        [MetaMember(1)] public MetaOfferId  OfferId  { get; private set; }
        [MetaMember(2)] public RewardBundle Contents { get; private set; }

        public DemoOfferReward() { }

        public DemoOfferReward(MetaOfferId offerId, RewardBundle contents)
        {
            OfferId  = offerId;
            Contents = contents;
        }

        public override void Consume(PlayerModel playerModel, IRewardSource source)
        {
            // The purchase transaction ID is not available here (IRewardSource is an empty marker interface),
            // so the correlation ID is built from the player, the current time and the offer ID, as in
            // OfferInfo.PayInGameCurrencyCost. It links this grant's wallet events to each other but not to the
            // SDK's purchase event. docs/offers.md lists this as a known limitation.
            AnalyticsCorrelationId correlation = AnalyticsCorrelationId.Create(playerModel.PlayerId, playerModel.CurrentTime, "offer_" + OfferId.Value);

            // A paid grant, because the SDK has already validated the purchase and records it whatever this does.
            MetaActionResult result = playerModel.ApplyWallet(
                WalletTransaction.PaidGrant(EconomyFeature.Offers, EconomyReason.OfferPurchase, Contents, EconomyContentId.FromString(OfferId.Value)),
                commit: true,
                correlation);

            if (result != MetaActionResult.Success)
                playerModel.Log.Error("Purchase of offer {OfferId} could not be granted: {Result}", OfferId, result);
        }
    }

    /// <summary>
    /// One offer group: its offers, its placement, and the segments or schedule that control when it is active
    /// (<c>docs/offers.md</c>). Each <c>ShopFeatured</c> group has one offer and competes on priority. The
    /// <c>ShopCatalogue</c> group has the whole catalogue. It subclasses <see cref="MetaOfferGroupInfoBase"/> instead
    /// of using <see cref="DefaultMetaOfferGroupInfo"/> because <see cref="OfferGroupsModel"/> is bound to this type.
    /// It adds no fields. Published groups are built from <see cref="OfferGroupSourceItem"/>, and tests use the other
    /// constructors.
    /// </summary>
    [MetaSerializableDerived(101)]
    [MetaActivableConfigData("OfferGroup")]
    public class OfferGroupInfo : MetaOfferGroupInfoBase
    {
        public OfferGroupInfo() { }

        public OfferGroupInfo(
            MetaOfferGroupId     groupId,
            string               displayName,
            string               description,
            OfferPlacementId     placement,
            int                  priority,
            MetaOfferId          offerId,
            MetaActivableParams  activableParams)
            : this(groupId, displayName, description, placement, priority, new List<MetaOfferId> { offerId }, activableParams)
        {
        }

        /// <summary>Creates a group with several offers, as used for <c>ShopCatalogue</c>.</summary>
        public OfferGroupInfo(
            MetaOfferGroupId     groupId,
            string               displayName,
            string               description,
            OfferPlacementId     placement,
            int                  priority,
            List<MetaOfferId>    offerIds,
            MetaActivableParams  activableParams)
            : base(
                groupId,
                displayName,
                description,
                timeline: null,
                placement,
                priority,
                offers: offerIds.ConvertAll(MetaRef<MetaOfferInfoBase>.FromKey),
                activableParams,
                maxOffersActive: null,
                includeSoldOutOffers: true)
        {
        }

        /// <summary>
        /// Builds a group from a <c>GameConfigSource/OfferGroups.csv</c> row, read through
        /// <see cref="OfferGroupSourceItem"/>.
        /// </summary>
        public OfferGroupInfo(MetaOfferGroupSourceConfigItemBase source)
            : base(source)
        {
        }
    }

    /// <summary>
    /// A row of <c>GameConfigSource/OfferGroups.csv</c>: the SDK's columns
    /// (<see cref="MetaOfferGroupSourceConfigItemBase{T}"/>) plus <see cref="IsTransient"/>. The config entry's
    /// <c>GameConfigEntryTransform</c> converts each row to an <see cref="OfferGroupInfo"/>. Segment-targeted groups
    /// need <see cref="IsTransient"/>, so a player who leaves the segment stops seeing the offer instead of keeping it
    /// until its lifetime ends. The SDK's source item always sets it to false, so <see cref="GetActivableParams"/> is
    /// overridden to read it from the sheet.
    /// </summary>
    public class OfferGroupSourceItem : MetaOfferGroupSourceConfigItemBase<OfferGroupInfo>
    {
        /// <summary>Whether the group's activation is re-evaluated on every check instead of being started once.</summary>
        public bool IsTransient { get; private set; }

        public override MetaActivableParams GetActivableParams()
        {
            return new MetaActivableParams(
                isEnabled:                 Enabled,
                developerOnly:             DeveloperOnly,
                segments:                  Segments,
                additionalConditions:      null,
                lifetime:                  GetEffectiveLifetime(),
                isTransient:               IsTransient,
                schedule:                  Schedule,
                // Purchase limits are set on the individual offers, not on the group.
                maxActivations:            null,
                maxTotalConsumes:          null,
                maxConsumesPerActivation:  null,
                cooldown:                  Cooldown,
                allowActivationAdjustment: true);
        }

        public override OfferGroupInfo ToConfigData(GameConfigBuildLog buildLog) => new OfferGroupInfo(this);
    }

    /// <summary>
    /// The player's offer-group state, bound to <see cref="OfferGroupInfo"/> instead of the SDK's default info
    /// type. <see cref="Metaplay.Core.Activables.MetaActivableSet{TId, TInfo, TActivableState}"/> casts every
    /// info object to its <c>TInfo</c>, so this type's <c>TInfo</c> must match the config library's item type.
    /// <para>
    /// It adds no state or logic. The per-group and per-offer state are the SDK's
    /// <see cref="DefaultMetaOfferGroupModel"/> and <see cref="DefaultMetaOfferPerPlayerState"/>.
    /// </para>
    /// </summary>
    [MetaSerializableDerived(101)]
    [MetaActivableSet("OfferGroup")]
    public class OfferGroupsModel : PlayerMetaOfferGroupsModelBase<OfferGroupInfo>
    {
        protected override MetaOfferGroupModelBase CreateActivableState(OfferGroupInfo info, IPlayerModelBase player) =>
            new DefaultMetaOfferGroupModel(info);

        protected override MetaOfferPerPlayerStateBase CreateOfferState(MetaOfferInfoBase offerInfo, IPlayerModelBase player) =>
            new DefaultMetaOfferPerPlayerState();
    }
}
