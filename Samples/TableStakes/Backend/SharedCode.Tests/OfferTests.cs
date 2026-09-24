using Metaplay.Core;
using Metaplay.Core.Analytics;
using Metaplay.Core.Config;
using Metaplay.Core.InAppPurchase;
using Metaplay.Core.Model;
using Metaplay.Core.Offers;
using Metaplay.Core.Player;
using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;

namespace Game.Logic.Tests
{
    /// <summary>
    /// Tests for the offer catalogue (<c>docs/offers.md</c>). The tests run the SDK's offer actions
    /// (<see cref="IPlayerModelBase.RefreshMetaOffers"/>, <see cref="PlayerPurchaseInGameCurrencyMetaOffer"/> and
    /// the dynamic-content in-app purchase actions), so they test this game's config and its
    /// <see cref="OfferInfo"/> extension rather than a copy of the SDK's activation logic.
    /// </summary>
    [TestFixture]
    public class OfferTests
    {
        static readonly MetaTime CreatedAt = MetaTime.FromDateTime(new System.DateTime(2026, 9, 1, 12, 0, 0, System.DateTimeKind.Utc));

        static PlayerModel NewPlayer(RewardBundle startingWallet = null)
        {
            SharedGameConfig config = TestGameConfig.Build(startingWallet);

            // The SDK's offer code reads its own copies of the "Offers" and "OfferGroups" entries (such as
            // ISharedGameConfig.MetaOfferGroups). Only OnConfigEntriesPopulated fills them. A real archive import
            // calls it, but a config built in code does not, so call it here. A null importParams is allowed.
            config.OnConfigEntriesPopulated(importParams: null);

            return TestPlayers.New(CreatedAt, config);
        }

        static void AgeTo(PlayerModel player, MetaDuration age) => player.ResetTime(CreatedAt + age);

        static void FinishGames(PlayerModel player, int played)
        {
            for (int index = 0; index < played; index++)
            {
                player.Record.Add(new MatchHistoryEntry(
                    EntityId.Create(EntityKindGame.Match, (ulong)index), CreatedAt, tricksWon: 1,
                    position: 2, isWin: false, humanOpponents: 0, finishedByPlayer: true));
            }

            // The server copies the new counts to the segments with a PlayerTargetingFactsSynced action after
            // recording the games. Do the same here.
            new PlayerTargetingFactsSynced(PlayerTargetingFacts.Of(player)).Execute(player, commit: true);
        }

        /// <summary>Re-evaluate the activation of every offer group against the player's current state.</summary>
        static void Refresh(PlayerModel player) => new PlayerRefreshMetaOffers(null).Execute(player, commit: true);

        /// <summary>The offers currently purchasable on <paramref name="placement"/>, across all its groups.</summary>
        static IReadOnlyList<MetaOfferId> PurchasableOn(PlayerModel player, OfferPlacementId placement)
        {
            List<MetaOfferId> purchasable = new List<MetaOfferId>();
            foreach (OfferGroupInfo groupInfo in player.GameConfig.OfferGroups.Values.Where(g => g.Placement == placement))
            {
                foreach (MetaOfferStatus status in player.MetaOfferGroups.GetOffersInGroup(groupInfo, player))
                {
                    if (player.MetaOfferGroups.OfferIsPurchasable(status))
                        purchasable.Add(status.Info.OfferId);
                }
            }
            return purchasable;
        }

        static IReadOnlyList<MetaOfferId> PurchasableCatalogue(PlayerModel player) => PurchasableOn(player, TestGameConfig.ShopCataloguePlacement);

        /// <summary>
        /// The Featured offer to show, resolved the same way as <c>MetaStateService</c>: the first active group in
        /// <c>MetaOfferGroupsPerPlacementInMostImportantFirstOrder</c> for the placement.
        /// <para>
        /// A transient group can become active again within one activation without the SDK checking
        /// <c>PlacementIsAvailable</c> (see <c>IsTransient</c>), so a placement can have two active groups. Picking
        /// the highest-priority one is the rule <c>TryActivateMetaOfferGroups</c> uses.
        /// </para>
        /// </summary>
        static MetaOfferId FeaturedOffer(PlayerModel player)
        {
            foreach (MetaOfferGroupInfoBase groupInfo in player.GameConfig.MetaOfferGroupsPerPlacementInMostImportantFirstOrder[TestGameConfig.ShopFeaturedPlacement])
            {
                if (!player.MetaOfferGroups.IsActive(groupInfo.GroupId, player))
                    continue;

                foreach (MetaOfferStatus status in player.MetaOfferGroups.GetOffersInGroup(groupInfo, player))
                    return status.Info.OfferId;
            }
            return null;
        }

        /// <summary>A player past the first week who holds exactly the Lucky Spin Bundle's gem price, with offers refreshed.</summary>
        static PlayerModel GemFundedPlayerPastTheFirstWeek()
        {
            PlayerModel player = NewPlayer(new RewardBundle(CurrencyAmount.Gems(TestGameConfig.LuckySpinBundlePriceGems)));
            AgeTo(player, MetaDuration.FromDays(30));
            Refresh(player);
            return player;
        }

        static MetaActionResult PurchaseWallet(PlayerModel player, MetaOfferGroupId groupId, MetaOfferId offerId, bool commit)
        {
            OfferGroupInfo groupInfo = player.GameConfig.OfferGroups[groupId];
            OfferInfo      offerInfo = player.GameConfig.Offers[offerId];
            return new PlayerPurchaseInGameCurrencyMetaOffer(groupInfo, offerInfo, analyticsContext: null).Execute(player, commit);
        }

        /// <summary>
        /// Run every client and server action of one demo in-app offer purchase in order, like the SDK's own
        /// offer tests do. Server-side validation is replaced by executing the action a successful validation
        /// produces (<c>docs/offers.md</c>, "Purchase flow"). Returns the first result that is not a success.
        /// </summary>
        static MetaActionResult PurchaseDemo(PlayerModel player, MetaOfferGroupId groupId, MetaOfferId offerId, string transactionId)
        {
            OfferGroupInfo groupInfo = player.GameConfig.OfferGroups[groupId];
            OfferInfo      offerInfo = player.GameConfig.Offers[offerId];

            MetaActionResult prepareResult = new PlayerPreparePurchaseMetaOffer(groupInfo, offerInfo, analyticsContext: null).Execute(player, commit: true);
            if (prepareResult != MetaActionResult.Success)
                return prepareResult;

            InAppProductId productId = offerInfo.InAppProduct.Ref.ProductId;

            MetaActionResult confirmResult = new PlayerConfirmPendingDynamicPurchaseContent(productId).Execute(player, commit: true);
            if (confirmResult != MetaActionResult.Success)
                return confirmResult;

            InAppProductInfoBase productInfo = player.GameConfig.InAppProducts[productId];
            InAppPurchaseEvent purchaseEvent = InAppPurchaseEvent.CreatePending(
                InAppPurchasePlatformDevelopment.Development,
                transactionId: transactionId,
                productId: productId,
                platformProductId: productInfo.DevelopmentId,
                receipt: "receipt",
                signature: "signature",
                alternativePurchaseId: null,
                platformState: null);

            MetaActionResult purchasedResult = new PlayerInAppPurchased(purchaseEvent).Execute(player, commit: true);
            if (purchasedResult != MetaActionResult.Success)
                return purchasedResult;

            MetaActionResult validatedResult = new PlayerInAppPurchaseValidated(
                transactionId: transactionId,
                status: InAppPurchaseStatus.Successful,
                isDuplicateTransaction: false,
                alternativePurchaseId: null,
                originalTransactionId: transactionId,
                subscription: null,
                paymentType: null,
                newPlatformState: null,
                refundReason: null).Execute(player, commit: true);
            if (validatedResult != MetaActionResult.Success)
                return validatedResult;

            return new PlayerClaimPendingInAppPurchase(transactionId).Execute(player, commit: true);
        }

        #region Featured resolution

        /// <summary>
        /// A player in both the first-week and gem-funded cohorts sees one Featured offer. The first-week group
        /// has the higher priority on the <c>ShopFeatured</c> placement, so it wins
        /// (<c>docs/offers.md</c>, "Resolving the featured offer").
        /// </summary>
        [Test]
        public void OverlappingCohortsStillResolveToOneFeaturedOffer()
        {
            PlayerModel player = NewPlayer(new RewardBundle(CurrencyAmount.Gems(TestGameConfig.LuckySpinBundlePriceGems)));
            Refresh(player);

            Assert.That(FeaturedOffer(player), Is.EqualTo(TestGameConfig.StarterPack1Offer));
        }

        /// <summary>A player past the first week with enough gems gets the gem-funded group's offer.</summary>
        [Test]
        public void AGemFundedPlayerOutsideTheFirstWeekIsFeaturedLuckySpin()
        {
            PlayerModel player = GemFundedPlayerPastTheFirstWeek();

            Assert.That(FeaturedOffer(player), Is.EqualTo(TestGameConfig.LuckySpinBundleOffer));
        }

        /// <summary>An engaged player past the first week without enough gems gets the engaged group's offer.</summary>
        [Test]
        public void AnEngagedPlayerIsFeaturedGemBoosterPack()
        {
            PlayerModel player = NewPlayer();
            AgeTo(player, MetaDuration.FromDays(30));
            FinishGames(player, TestGameConfig.EngagedMinMatchesPlayed);
            Refresh(player);

            Assert.That(FeaturedOffer(player), Is.EqualTo(TestGameConfig.GemBoosterPackOffer));
        }

        /// <summary>A player in no cohort gets the untargeted fallback offer.</summary>
        [Test]
        public void APlayerInNoCohortIsFeaturedTheFallback()
        {
            PlayerModel player = NewPlayer();
            AgeTo(player, MetaDuration.FromDays(30));
            FinishGames(player, 1);
            Refresh(player);

            Assert.That(FeaturedOffer(player), Is.EqualTo(TestGameConfig.DailySpinDealOffer));
        }

        /// <summary>
        /// With personalisation off, no segmented group is active regardless of the player's balances or account
        /// age, so only the untargeted fallback is featured (<c>docs/offers.md</c>). The segment conditions in the
        /// config enforce this, not a separate check in game code.
        /// </summary>
        [Test]
        public void TurningPersonalisationOffLeavesOnlyTheFallbackFeatured()
        {
            PlayerModel player = NewPlayer();
            Refresh(player);
            Assert.That(FeaturedOffer(player), Is.EqualTo(TestGameConfig.StarterPack1Offer));

            player.SetPersonalizedOffersEnabled(false);
            Refresh(player);

            Assert.That(FeaturedOffer(player), Is.EqualTo(TestGameConfig.DailySpinDealOffer));

            player.SetPersonalizedOffersEnabled(true);
            Refresh(player);

            Assert.That(FeaturedOffer(player), Is.EqualTo(TestGameConfig.StarterPack1Offer));
        }

        #endregion

        #region Catalogue eligibility

        /// <summary>
        /// A new player can buy Starter Pack (first-week only) and every unsegmented offer. Starter Pack II waits
        /// for its precursor and Gem Booster Pack requires the engaged cohort, so neither is purchasable.
        /// </summary>
        [Test]
        public void AFreshPlayersCatalogueIsTheOpenOffersPlusStarterPack()
        {
            PlayerModel player = NewPlayer();
            Refresh(player);

            Assert.That(PurchasableCatalogue(player), Is.EquivalentTo(new[]
            {
                TestGameConfig.StarterPack1Offer,
                TestGameConfig.LuckySpinBundleOffer,
                TestGameConfig.CoinVaultOffer,
                TestGameConfig.DailySpinDealOffer,
            }));
        }

        /// <summary>
        /// Offers without an offer-level segment stay in the catalogue for every player. Starter Pack's segment
        /// limits who can buy it, not only who it is featured to, so it leaves the catalogue when the first week
        /// ends, just as it leaves the Featured slot.
        /// </summary>
        [Test]
        public void UnsegmentedOffersStayInTheCatalogueForEveryPlayer()
        {
            PlayerModel player = NewPlayer();
            AgeTo(player, MetaDuration.FromDays(30));
            FinishGames(player, 1);
            Refresh(player);

            Assert.That(PurchasableCatalogue(player), Is.EquivalentTo(new[]
            {
                TestGameConfig.LuckySpinBundleOffer,
                TestGameConfig.CoinVaultOffer,
                TestGameConfig.DailySpinDealOffer,
            }));
        }

        #endregion

        #region The precursor chain

        /// <summary>
        /// After Starter Pack is bought and its activation has ended, the <see cref="MetaOfferPrecursorCondition"/>
        /// on Starter Pack II makes it purchasable on the next refresh. Here the activation ends because the
        /// player leaves the first week, which ends both the Featured and the catalogue activation, since both
        /// use the first-week segment.
        /// </summary>
        [Test]
        public void StarterPackTwoUnlocksOnceStarterPackIsBoughtAndItsActivationHasEnded()
        {
            PlayerModel player = NewPlayer();
            Refresh(player);

            Assert.That(PurchaseDemo(player, TestGameConfig.FeaturedFirstWeekGroup, TestGameConfig.StarterPack1Offer, "txn-1"), Is.EqualTo(MetaActionResult.Success));

            AgeTo(player, MetaDuration.FromDays(30));
            Refresh(player);

            Assert.That(PurchasableCatalogue(player), Does.Contain(TestGameConfig.StarterPack2Offer));
        }

        #endregion

        #region Wallet purchasing

        /// <summary>One action takes the price and grants the contents, and the dry run changes nothing.</summary>
        [Test]
        public void AWalletPurchaseSpendsThePriceAndGrantsTheContentsAtomically()
        {
            PlayerModel player = GemFundedPlayerPastTheFirstWeek();
            int coinsBefore = player.Wallet.Coins;

            MetaActionResult dryRun = PurchaseWallet(player, TestGameConfig.FeaturedGemFundedGroup, TestGameConfig.LuckySpinBundleOffer, commit: false);
            Assert.That(dryRun, Is.EqualTo(MetaActionResult.Success));
            Assert.That(player.Wallet.Coins, Is.EqualTo(coinsBefore), "a dry run must not move anything");

            MetaActionResult commit = PurchaseWallet(player, TestGameConfig.FeaturedGemFundedGroup, TestGameConfig.LuckySpinBundleOffer, commit: true);
            Assert.That(commit, Is.EqualTo(MetaActionResult.Success));

            Assert.That(player.Wallet.Gems, Is.EqualTo(0));
            Assert.That(player.Wallet.Coins, Is.EqualTo(coinsBefore + TestGameConfig.LuckySpinBundleCoins));
            Assert.That(player.Wallet.SpinTokens, Is.EqualTo(TestGameConfig.LuckySpinBundleSpinTokens), "0 is the custom starting wallet's spin token balance: only gems were given");
        }

        /// <summary>
        /// When the wallet would refuse the grant, the purchase is refused. The SDK records a purchase regardless
        /// of what the payment did, so a purchase that took and granted nothing would still sell out the
        /// one-time offer.
        /// </summary>
        [Test]
        public void AWalletPurchaseWhoseGrantWouldPassACapIsRefusedAndDoesNotSellOut()
        {
            PlayerModel player = NewPlayer(new RewardBundle(
                CurrencyAmount.Gems(TestGameConfig.LuckySpinBundlePriceGems),
                CurrencyAmount.SpinTokens(TestGameConfig.MaxSpinTokens)));
            AgeTo(player, MetaDuration.FromDays(30));
            Refresh(player);

            MetaActionResult dryRun = PurchaseWallet(player, TestGameConfig.FeaturedGemFundedGroup, TestGameConfig.LuckySpinBundleOffer, commit: false);
            MetaActionResult commit = PurchaseWallet(player, TestGameConfig.FeaturedGemFundedGroup, TestGameConfig.LuckySpinBundleOffer, commit: true);

            Assert.That(dryRun, Is.EqualTo(MetaActionResult.CannotAfford));
            Assert.That(commit, Is.EqualTo(MetaActionResult.CannotAfford));
            Assert.That(player.Wallet.Gems, Is.EqualTo(TestGameConfig.LuckySpinBundlePriceGems), "the price was taken");
            Assert.That(PurchasableOn(player, TestGameConfig.ShopFeaturedPlacement), Does.Contain(TestGameConfig.LuckySpinBundleOffer),
                "the offer counts as bought though nothing was paid or granted");
        }

        /// <summary>
        /// <see cref="OfferInfo.OverflowingCurrency"/> reports the same refusal before the player buys. The Shop
        /// uses it to name the full balance on the offer card instead of showing a Buy button that the server
        /// would refuse with <see cref="MetaActionResult.CannotAfford"/>, which is misleading for a player who
        /// can pay the price.
        /// </summary>
        [Test]
        public void AnOfferNamesTheBalanceItsContentsWouldOverflow()
        {
            PlayerModel atTheCap = NewPlayer(new RewardBundle(
                CurrencyAmount.Gems(TestGameConfig.LuckySpinBundlePriceGems),
                CurrencyAmount.SpinTokens(TestGameConfig.MaxSpinTokens)));
            OfferInfo bundle = atTheCap.GameConfig.Offers[TestGameConfig.LuckySpinBundleOffer];

            Assert.That(bundle.OverflowingCurrency(atTheCap), Is.EqualTo(CurrencyType.SpinTokens));

            PlayerModel withRoom = NewPlayer(new RewardBundle(CurrencyAmount.Gems(TestGameConfig.LuckySpinBundlePriceGems)));
            Assert.That(bundle.OverflowingCurrency(withRoom), Is.EqualTo(CurrencyType.None));

            // Not affording the price is not an overflow. The card shows the two states differently. A new
            // player cannot afford this bundle.
            Assert.That(bundle.OverflowingCurrency(NewPlayer()), Is.EqualTo(CurrencyType.None));

            // An in-app purchase offer reports the balance its contents would overflow too, so the Shop does not
            // take a payment for a grant past the cap.
            OfferInfo demo = atTheCap.GameConfig.Offers[TestGameConfig.StarterPack1Offer];
            Assert.That(demo.OverflowingCurrency(atTheCap), Is.EqualTo(CurrencyType.SpinTokens));
            Assert.That(demo.OverflowingCurrency(withRoom), Is.EqualTo(CurrencyType.None));
        }

        /// <summary>
        /// This wallet offer sells once per player. A second purchase is refused and changes nothing.
        /// </summary>
        [Test]
        public void ARepeatWalletPurchaseIsRefusedAndChangesNothing()
        {
            PlayerModel player = GemFundedPlayerPastTheFirstWeek();
            PurchaseWallet(player, TestGameConfig.FeaturedGemFundedGroup, TestGameConfig.LuckySpinBundleOffer, commit: true);

            int coinsBefore = player.Wallet.Coins;

            MetaActionResult result = PurchaseWallet(player, TestGameConfig.FeaturedGemFundedGroup, TestGameConfig.LuckySpinBundleOffer, commit: true);

            Assert.That(result, Is.Not.EqualTo(MetaActionResult.Success));
            Assert.That(player.Wallet.Coins, Is.EqualTo(coinsBefore));
        }

        /// <summary>
        /// An offer whose group has never activated for the player is refused before the wallet is checked. A new
        /// player also cannot afford this offer, but the refusal here comes from the inactive group.
        /// </summary>
        [Test]
        public void AnOfferOutsideThePlayersCohortIsRefused()
        {
            PlayerModel player = NewPlayer();
            Refresh(player);

            MetaActionResult result = PurchaseWallet(player, TestGameConfig.FeaturedGemFundedGroup, TestGameConfig.LuckySpinBundleOffer, commit: true);

            Assert.That(result, Is.Not.EqualTo(MetaActionResult.Success));
            Assert.That(player.Wallet.Gems, Is.EqualTo(100), "nothing was spent; 100 is the fixture's starting gem balance");
        }

        /// <summary>
        /// Buying Daily Spin Deal from the catalogue group spends exactly its price and grants exactly its tokens.
        /// </summary>
        [Test]
        public void DailySpinDealSpendsExactlyItsPriceAndGrantsExactlyItsToken()
        {
            PlayerModel player = NewPlayer();
            Refresh(player);
            int coinsBefore = player.Wallet.Coins;
            int tokensBefore = player.Wallet.SpinTokens;

            MetaActionResult result = PurchaseWallet(player, TestGameConfig.CatalogueDailyGroup, TestGameConfig.DailySpinDealOffer, commit: true);

            Assert.That(result, Is.EqualTo(MetaActionResult.Success));
            Assert.That(player.Wallet.Coins, Is.EqualTo(coinsBefore - TestGameConfig.DailySpinDealPriceCoins));
            Assert.That(player.Wallet.SpinTokens, Is.EqualTo(tokensBefore + TestGameConfig.DailySpinDealSpinTokens));

            // The offer is sold out on both placements until its next daily activation.
            Assert.That(PurchasableOn(player, TestGameConfig.ShopFeaturedPlacement), Does.Not.Contain(TestGameConfig.DailySpinDealOffer));
            Assert.That(PurchasableCatalogue(player), Does.Not.Contain(TestGameConfig.DailySpinDealOffer));
        }

        #endregion

        #region Demo-IAP purchasing

        /// <summary>
        /// The full demo purchase flow for Starter Pack. Prepare sets the offer as the product's pending dynamic
        /// content, the server confirms it, and the client claims the validated purchase. The SDK's claim action
        /// grants the reward and marks the offer purchased.
        /// </summary>
        [Test]
        public void ADemoPurchaseGrantsTheOffersRewardOnceClaimed()
        {
            PlayerModel player = NewPlayer();
            Refresh(player);
            int coinsBefore = player.Wallet.Coins;
            int gemsBefore  = player.Wallet.Gems;
            int tokensBefore = player.Wallet.SpinTokens;

            MetaActionResult result = PurchaseDemo(player, TestGameConfig.FeaturedFirstWeekGroup, TestGameConfig.StarterPack1Offer, "txn-1");

            Assert.That(result, Is.EqualTo(MetaActionResult.Success));
            Assert.That(player.Wallet.Coins, Is.EqualTo(coinsBefore + TestGameConfig.StarterPack1Coins));
            Assert.That(player.Wallet.Gems, Is.EqualTo(gemsBefore + TestGameConfig.StarterPack1Gems));
            Assert.That(player.Wallet.SpinTokens, Is.EqualTo(tokensBefore + TestGameConfig.StarterPack1SpinTokens));

            // The SDK's per-player offer state records the purchase, so the offer is sold out on both placements.
            Assert.That(PurchasableOn(player, TestGameConfig.ShopFeaturedPlacement), Does.Not.Contain(TestGameConfig.StarterPack1Offer));
            Assert.That(PurchasableCatalogue(player), Does.Not.Contain(TestGameConfig.StarterPack1Offer));
        }

        /// <summary>
        /// A validated in-app purchase of an offer is granted in full even when it takes a balance past its cap,
        /// because the SDK records the purchase and uses up the offer whatever the grant does.
        /// </summary>
        [Test]
        public void ADemoPurchasePastTheCapIsStillGranted()
        {
            PlayerModel player = NewPlayer(new RewardBundle(CurrencyAmount.SpinTokens(TestGameConfig.MaxSpinTokens)));
            Refresh(player);
            int tokensBefore = player.Wallet.SpinTokens;

            MetaActionResult result = PurchaseDemo(player, TestGameConfig.FeaturedFirstWeekGroup, TestGameConfig.StarterPack1Offer, "txn-1");

            Assert.That(result, Is.EqualTo(MetaActionResult.Success));
            Assert.That(player.Wallet.SpinTokens, Is.EqualTo(tokensBefore + TestGameConfig.StarterPack1SpinTokens));
        }

        /// <summary>A second demo purchase of the same offer, with a fresh transaction id, is refused and grants nothing.</summary>
        [Test]
        public void ARepeatDemoPurchaseIsRefusedAndGrantsNothing()
        {
            PlayerModel player = NewPlayer();
            Refresh(player);
            PurchaseDemo(player, TestGameConfig.FeaturedFirstWeekGroup, TestGameConfig.StarterPack1Offer, "txn-1");

            int coinsBefore = player.Wallet.Coins;

            MetaActionResult result = PurchaseDemo(player, TestGameConfig.FeaturedFirstWeekGroup, TestGameConfig.StarterPack1Offer, "txn-2");

            Assert.That(result, Is.Not.EqualTo(MetaActionResult.Success));
            Assert.That(player.Wallet.Coins, Is.EqualTo(coinsBefore));
        }

        /// <summary>
        /// Preparing a purchase grants nothing. The reward is granted only when the purchase is claimed.
        /// </summary>
        [Test]
        public void ADemoPurchaseGrantsNothingBeforeItIsClaimed()
        {
            PlayerModel player = NewPlayer();
            Refresh(player);
            OfferGroupInfo groupInfo = player.GameConfig.OfferGroups[TestGameConfig.FeaturedFirstWeekGroup];
            OfferInfo      offerInfo = player.GameConfig.Offers[TestGameConfig.StarterPack1Offer];
            int coinsBefore = player.Wallet.Coins;

            new PlayerPreparePurchaseMetaOffer(groupInfo, offerInfo, analyticsContext: null).Execute(player, commit: true);

            Assert.That(player.Wallet.Coins, Is.EqualTo(coinsBefore));
            Assert.That(PurchasableOn(player, TestGameConfig.ShopFeaturedPlacement), Does.Contain(TestGameConfig.StarterPack1Offer), "not sold out until the purchase actually claims");
        }

        #endregion

        #region The personalisation preference

        [Test]
        public void PersonalizedOffersDefaultToEnabled()
        {
            PlayerModel player = NewPlayer();

            Assert.That(player.PersonalizedOffersEnabled, Is.True);
            Assert.That(new PlayerPropertyPersonalizedOffersEnabled().GetTypedValueForPlayer(player), Is.True);
        }

        [Test]
        public void TheToggleActionMovesThePreferenceAndNothingElse()
        {
            PlayerModel player = NewPlayer();
            int coinsBefore = player.Wallet.Coins;

            MetaActionResult result = new PlayerSetPersonalizedOffersEnabled(false).Execute(player, commit: true);

            Assert.That(result, Is.EqualTo(MetaActionResult.Success));
            Assert.That(player.PersonalizedOffersEnabled, Is.False);
            Assert.That(player.Wallet.Coins, Is.EqualTo(coinsBefore));

            new PlayerSetPersonalizedOffersEnabled(true).Execute(player, commit: true);
            Assert.That(player.PersonalizedOffersEnabled, Is.True);
        }

        #endregion
    }
}
