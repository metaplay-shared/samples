using Metaplay.Core;
using Metaplay.Core.Analytics;
using Metaplay.Core.Config;
using Metaplay.Core.Model;
using Metaplay.Core.Player;
using Metaplay.Core.Serialization;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Game.Logic.Tests
{
    /// <summary>
    /// Tests for <see cref="PlayerWalletModel"/>: what it settles, what it refuses, and that a multi-currency
    /// operation either happens completely or not at all. Every other meta feature relies on that.
    /// <para>
    /// Settling is a pure function of a wallet, a transaction and the caps, so these tests need no actor,
    /// session or config archive.
    /// </para>
    /// </summary>
    [TestFixture]
    public class WalletTests
    {
        static readonly WalletCaps Caps = new WalletCaps(coins: 10_000, gems: 1_000, spinTokens: 10);

        /// <summary>
        /// A wallet holding the given balances, built as a starting wallet the way a real wallet is created. Zero
        /// balances are left out of the bundle, because a grant of zero is malformed.
        /// </summary>
        static PlayerWalletModel Wallet(int coins = 0, int gems = 0, int spinTokens = 0)
        {
            List<CurrencyAmount> opening = new List<CurrencyAmount>();
            if (coins > 0)      opening.Add(CurrencyAmount.Coins(coins));
            if (gems > 0)       opening.Add(CurrencyAmount.Gems(gems));
            if (spinTokens > 0) opening.Add(CurrencyAmount.SpinTokens(spinTokens));

            return PlayerWalletModel.Starting(TestGameConfig.GlobalWith(new RewardBundle(opening.ToArray())));
        }

        static WalletTransaction Buy(CurrencyAmount price) =>
            WalletTransaction.Spend(EconomyFeature.Cosmetics, EconomyReason.CosmeticPurchase, price, EconomyContentId.FromString("frame.brass"));

        static WalletTransaction Reward(params CurrencyAmount[] amounts) =>
            WalletTransaction.Grant(EconomyFeature.DailyReward, EconomyReason.DailyReward, new RewardBundle(amounts));

        #region Balances and boundaries

        [Test]
        public void SpendingExactlyTheBalanceIsAllowed()
        {
            WalletSettlement settlement = Wallet(coins: 900).Settle(Buy(CurrencyAmount.Coins(900)), Caps);

            Assert.That(settlement.IsSuccess, Is.True);
            Assert.That(settlement.WalletAfter.Coins, Is.EqualTo(0));
        }

        [Test]
        public void SpendingOneMoreThanTheBalanceIsRefused()
        {
            PlayerWalletModel wallet     = Wallet(coins: 899);
            WalletSettlement  settlement = wallet.Settle(Buy(CurrencyAmount.Coins(900)), Caps);

            Assert.That(settlement.IsSuccess, Is.False);
            Assert.That(settlement.Refusal, Is.EqualTo(WalletRefusal.InsufficientFunds));
            Assert.That(settlement.WalletAfter, Is.Null, "a refusal must not hand back a wallet to commit");
            Assert.That(settlement.Rows, Is.Empty);

            // The shortfall that the shop screen shows and the rejection event reports.
            Assert.That(settlement.RefusedCurrency, Is.EqualTo(CurrencyType.Coins));
            Assert.That(settlement.RefusedAmount, Is.EqualTo(900));
            Assert.That(settlement.RefusedBalance, Is.EqualTo(899));
        }

        [Test]
        public void AGrantThatLandsExactlyOnTheCapIsAllowed()
        {
            WalletSettlement settlement = Wallet(coins: 9_500).Settle(Reward(CurrencyAmount.Coins(500)), Caps);

            Assert.That(settlement.IsSuccess, Is.True);
            Assert.That(settlement.WalletAfter.Coins, Is.EqualTo(10_000));
        }

        [Test]
        public void AGrantOnePastTheCapIsRefusedWholeRatherThanClamped()
        {
            // Clamping would tell the player they earned more than they received. A grant that does not fit is
            // refused and logged, never silently reduced.
            PlayerWalletModel wallet     = Wallet(coins: 9_500);
            WalletSettlement  settlement = wallet.Settle(Reward(CurrencyAmount.Coins(501)), Caps);

            Assert.That(settlement.Refusal, Is.EqualTo(WalletRefusal.CapExceeded));
            Assert.That(settlement.WalletAfter, Is.Null);
            Assert.That(wallet.Coins, Is.EqualTo(9_500));
        }

        [Test]
        public void AGrantLargeEnoughToOverflowDoesNotWrap()
        {
            WalletSettlement settlement = Wallet(coins: 9_000).Settle(Reward(CurrencyAmount.Coins(int.MaxValue)), Caps);

            Assert.That(settlement.Refusal, Is.EqualTo(WalletRefusal.CapExceeded), "a grant was added in 32 bits and wrapped past the cap");
        }

        /// <summary>
        /// A grant the platform has already charged for lands even past the cap, because the SDK records the
        /// purchase whatever the wallet does. The balance stays above the cap, and ordinary grants are refused
        /// until the player spends below it.
        /// </summary>
        [Test]
        public void APaidGrantLandsPastTheCapAndOrdinaryGrantsAreRefusedAfterIt()
        {
            WalletTransaction paid       = WalletTransaction.PaidGrant(EconomyFeature.Iap, EconomyReason.IapGrant, new RewardBundle(CurrencyAmount.Coins(501)));
            WalletSettlement  settlement = Wallet(coins: 9_500).Settle(paid, Caps);

            Assert.That(settlement.IsSuccess, Is.True);
            Assert.That(settlement.WalletAfter.Coins, Is.EqualTo(10_001));
            Assert.That(settlement.WalletAfter.Settle(Reward(CurrencyAmount.Coins(1)), Caps).Refusal, Is.EqualTo(WalletRefusal.CapExceeded));
        }

        [Test]
        public void APaidGrantThatWouldNotFitInAnIntIsRefused()
        {
            WalletTransaction paid       = WalletTransaction.PaidGrant(EconomyFeature.Iap, EconomyReason.IapGrant, new RewardBundle(CurrencyAmount.Coins(int.MaxValue)));
            WalletSettlement  settlement = Wallet(coins: 9_000).Settle(paid, Caps);

            Assert.That(settlement.Refusal, Is.EqualTo(WalletRefusal.CapExceeded), "a paid grant was added in 32 bits and wrapped");
        }

        [TestCase(0)]
        [TestCase(-50)]
        public void AZeroOrNegativeAmountIsRefused(int amount)
        {
            // The config build validates that every price and reward is positive, so a zero or negative amount at
            // runtime is a caller bug. Settling a zero would write a transaction row that records no change.
            Assert.That(Wallet(coins: 100).Settle(Buy(CurrencyAmount.Coins(amount)), Caps).Refusal, Is.EqualTo(WalletRefusal.Malformed));
            Assert.That(Wallet(coins: 100).Settle(Reward(CurrencyAmount.Coins(amount)), Caps).Refusal, Is.EqualTo(WalletRefusal.Malformed));
        }

        [Test]
        public void OneCurrencyNamedTwiceOnOneSideIsRefused()
        {
            // Two entries for one currency is a caller bug. Settling it would write two rows for one balance
            // change, and the log's before and after balances would no longer add up.
            WalletTransaction transaction = WalletTransaction.Spend(
                EconomyFeature.Offers,
                EconomyReason.OfferPurchase,
                new List<CurrencyAmount> { CurrencyAmount.Coins(10), CurrencyAmount.Coins(20) });

            Assert.That(Wallet(coins: 100).Settle(transaction, Caps).Refusal, Is.EqualTo(WalletRefusal.Malformed));
        }

        [Test]
        public void ATransactionWithNoCurrenciesSettlesAndMovesNothing()
        {
            PlayerWalletModel wallet     = Wallet(coins: 100);
            WalletSettlement  settlement = wallet.Settle(Reward(), Caps);

            Assert.That(settlement.IsSuccess, Is.True);
            Assert.That(settlement.MovesBalance, Is.False);
            Assert.That(settlement.Rows, Is.Empty);
            Assert.That(settlement.WalletAfter.Coins, Is.EqualTo(100));
        }

        [Test]
        public void ANullPriceIsRefusedRatherThanTreatedAsFree()
        {
            Assert.That(Wallet(coins: 100).Settle(Buy(null), Caps).Refusal, Is.EqualTo(WalletRefusal.Malformed));
        }

        [Test]
        public void ATransactionWithNoFeatureOrNoReasonIsRefused()
        {
            // A row with no feature or no reason is a balance change that nobody can account for, which the
            // transaction log exists to prevent.
            WalletTransaction noFeature = WalletTransaction.Spend(EconomyFeature.None, EconomyReason.CosmeticPurchase, CurrencyAmount.Coins(10));
            WalletTransaction noReason  = WalletTransaction.Spend(EconomyFeature.Cosmetics, EconomyReason.None, CurrencyAmount.Coins(10));

            Assert.That(Wallet(coins: 100).Settle(noFeature, Caps).Refusal, Is.EqualTo(WalletRefusal.Malformed));
            Assert.That(Wallet(coins: 100).Settle(noReason, Caps).Refusal, Is.EqualTo(WalletRefusal.Malformed));
        }

        #endregion

        #region Atomicity

        /// <summary>
        /// The failure is in the <i>second</i> currency, so a wallet that deducted each currency in turn would
        /// already have charged the first one.
        /// </summary>
        [Test]
        public void AMultiCurrencyPriceThatFailsOnItsSecondCurrencyMovesNothing()
        {
            PlayerWalletModel wallet = Wallet(coins: 5_000, gems: 10);

            WalletTransaction transaction = WalletTransaction.Spend(
                EconomyFeature.Offers,
                EconomyReason.OfferPurchase,
                new List<CurrencyAmount> { CurrencyAmount.Coins(1_000), CurrencyAmount.Gems(500) },
                EconomyContentId.FromString("offer.mixed"));

            WalletSettlement settlement = wallet.Settle(transaction, Caps);

            Assert.That(settlement.Refusal, Is.EqualTo(WalletRefusal.InsufficientFunds));
            Assert.That(settlement.RefusedCurrency, Is.EqualTo(CurrencyType.Gems));
            Assert.That(settlement.Rows, Is.Empty, "a refused settlement offered rows to commit");
            Assert.That(settlement.WalletAfter, Is.Null);

            Assert.That(wallet.Coins, Is.EqualTo(5_000), "the first currency was charged before the second was checked");
            Assert.That(wallet.Gems, Is.EqualTo(10));
        }

        /// <summary>A grant where the second currency exceeds its cap grants neither currency.</summary>
        [Test]
        public void AMultiCurrencyGrantThatOverflowsOneCapMovesNothing()
        {
            PlayerWalletModel wallet = Wallet(coins: 100, gems: 999);

            WalletSettlement settlement = wallet.Settle(Reward(CurrencyAmount.Coins(500), CurrencyAmount.Gems(2)), Caps);

            Assert.That(settlement.Refusal, Is.EqualTo(WalletRefusal.CapExceeded));
            Assert.That(settlement.RefusedCurrency, Is.EqualTo(CurrencyType.Gems));
            Assert.That(settlement.WalletAfter, Is.Null);

            Assert.That(wallet.Coins, Is.EqualTo(100), "the coins landed before the gems were checked");
            Assert.That(wallet.Gems, Is.EqualTo(999));
        }

        /// <summary>
        /// A purchase that pays and receives is one settlement, so the player is never charged without
        /// receiving. The transaction has the same shape as the Lucky Spin Bundle offer.
        /// </summary>
        [Test]
        public void APriceAndWhatItBuysSettleTogetherOrNotAtAll()
        {
            WalletTransaction bundle = WalletTransaction.Exchange(
                EconomyFeature.Offers,
                EconomyReason.OfferPurchase,
                CurrencyAmount.Gems(500),
                new RewardBundle(CurrencyAmount.SpinTokens(5), CurrencyAmount.Coins(2_000)),
                EconomyContentId.FromString("offer.lucky_spin"));

            PlayerWalletModel affordable = Wallet(coins: 0, gems: 500, spinTokens: 0);
            WalletSettlement  bought     = affordable.Settle(bundle, Caps);

            Assert.That(bought.IsSuccess, Is.True);
            Assert.That(bought.WalletAfter.Gems, Is.EqualTo(0));
            Assert.That(bought.WalletAfter.SpinTokens, Is.EqualTo(5));
            Assert.That(bought.WalletAfter.Coins, Is.EqualTo(2_000));

            PlayerWalletModel oneGemShort = Wallet(coins: 0, gems: 499, spinTokens: 0);
            WalletSettlement  refused     = oneGemShort.Settle(bundle, Caps);

            Assert.That(refused.Refusal, Is.EqualTo(WalletRefusal.InsufficientFunds));
            Assert.That(oneGemShort.SpinTokens, Is.EqualTo(0), "the bundle's contents were granted for a price that was never paid");
            Assert.That(oneGemShort.Coins, Is.EqualTo(0));
        }

        /// <summary>
        /// Spends settle before grants, so the cap check on a grant sees the balance after the spend.
        /// </summary>
        [Test]
        public void WhatIsSpentIsTakenBeforeWhatIsGrantedIsAdded()
        {
            // The wallet is at the token cap. Granting first would exceed the cap, and spending first leaves room
            // for the replacement token.
            PlayerWalletModel wallet = Wallet(spinTokens: 10);

            WalletTransaction spin = WalletTransaction.Exchange(
                EconomyFeature.SpinWheel,
                EconomyReason.WheelSpinCost,
                CurrencyAmount.SpinTokens(1),
                new RewardBundle(CurrencyAmount.SpinTokens(1)));

            WalletSettlement settlement = wallet.Settle(spin, Caps);

            Assert.That(settlement.IsSuccess, Is.True);
            Assert.That(settlement.WalletAfter.SpinTokens, Is.EqualTo(10));
        }

        [Test]
        public void SettlingDoesNotTouchTheWalletItSettledAgainst()
        {
            PlayerWalletModel wallet     = Wallet(coins: 1_000);
            WalletSettlement  settlement = wallet.Settle(Buy(CurrencyAmount.Coins(400)), Caps);

            Assert.That(settlement.IsSuccess, Is.True);
            Assert.That(wallet.Coins, Is.EqualTo(1_000), "settling is supposed to decide nothing but the answer");
            Assert.That(settlement.WalletAfter.Coins, Is.EqualTo(600));
            Assert.That(settlement.WalletBefore, Is.SameAs(wallet));
        }

        #endregion

        #region Transaction rows

        [Test]
        public void EachMovedCurrencyGetsOneRowWithItsOwnBeforeAndAfter()
        {
            PlayerWalletModel wallet = Wallet(coins: 1_000, gems: 100);

            WalletTransaction transaction = WalletTransaction.Exchange(
                EconomyFeature.Offers,
                EconomyReason.OfferPurchase,
                CurrencyAmount.Gems(60),
                new RewardBundle(CurrencyAmount.Coins(250)));

            IReadOnlyList<WalletTransactionRow> rows = wallet.Settle(transaction, Caps).Rows;

            Assert.That(rows.Count, Is.EqualTo(2));

            Assert.That(rows[0].Currency, Is.EqualTo(CurrencyType.Gems));
            Assert.That(rows[0].Flow, Is.EqualTo(CurrencyFlow.Sink));
            Assert.That(rows[0].Amount, Is.EqualTo(60));
            Assert.That(rows[0].BalanceBefore, Is.EqualTo(100));
            Assert.That(rows[0].BalanceAfter, Is.EqualTo(40));

            Assert.That(rows[1].Currency, Is.EqualTo(CurrencyType.Coins));
            Assert.That(rows[1].Flow, Is.EqualTo(CurrencyFlow.Source));
            Assert.That(rows[1].Amount, Is.EqualTo(250));
            Assert.That(rows[1].BalanceBefore, Is.EqualTo(1_000));
            Assert.That(rows[1].BalanceAfter, Is.EqualTo(1_250));
        }

        /// <summary>
        /// The wheel's spin-again result. The balance ends where it started, and the log still has both rows,
        /// because combining them would hide the spin.
        /// </summary>
        [Test]
        public void ASourceAndASinkOfOneCurrencyAreNeverNettedIntoOneRow()
        {
            PlayerWalletModel wallet = Wallet(spinTokens: 3);

            WalletTransaction spin = WalletTransaction.Exchange(
                EconomyFeature.SpinWheel,
                EconomyReason.WheelSpinCost,
                CurrencyAmount.SpinTokens(1),
                new RewardBundle(CurrencyAmount.SpinTokens(1)));

            WalletSettlement settlement = wallet.Settle(spin, Caps);

            Assert.That(settlement.WalletAfter.SpinTokens, Is.EqualTo(3));
            Assert.That(settlement.Rows.Count, Is.EqualTo(2));
            Assert.That(settlement.Rows[0].Flow, Is.EqualTo(CurrencyFlow.Sink));
            Assert.That(settlement.Rows[0].BalanceAfter, Is.EqualTo(2));
            Assert.That(settlement.Rows[1].Flow, Is.EqualTo(CurrencyFlow.Source));
            Assert.That(settlement.Rows[1].BalanceBefore, Is.EqualTo(2));
            Assert.That(settlement.Rows[1].BalanceAfter, Is.EqualTo(3));
        }

        #endregion

        [Test]
        public void AWalletDescribesItselfAsTheGrantThatWouldHaveProducedIt()
        {
            // The starting balances are logged as an opening grant, because no action moved them.
            IReadOnlyList<WalletTransactionRow> rows = Wallet(coins: 3_000, spinTokens: 1).AsOpeningSettlement().Rows;

            Assert.That(rows.Count, Is.EqualTo(2), "a currency the wallet does not hold was given a row");
            Assert.That(rows.All(row => row.Flow == CurrencyFlow.Source), Is.True);
            Assert.That(rows.All(row => row.BalanceBefore == 0), Is.True);
            Assert.That(rows.Select(row => row.Currency), Is.EqualTo(new[] { CurrencyType.Coins, CurrencyType.SpinTokens }));
            Assert.That(rows.Select(row => row.BalanceAfter), Is.EqualTo(new[] { 3_000, 1 }));
        }

        /// <summary>
        /// Describing a wallet is not a grant that can be refused. A cap lowered below what a player already
        /// holds must not prevent logging the player's balances.
        /// </summary>
        [Test]
        public void AWalletOverATightenedCapStillDescribesItself()
        {
            PlayerWalletModel wallet = Wallet(coins: 3_000);

            Assert.That(wallet.Settle(Reward(CurrencyAmount.Coins(1)), new WalletCaps(coins: 100, gems: 10, spinTokens: 1)).Refusal,
                Is.EqualTo(WalletRefusal.CapExceeded));
            Assert.That(wallet.AsOpeningSettlement().IsSuccess, Is.True);
            Assert.That(wallet.AsOpeningSettlement().Rows.Single().BalanceAfter, Is.EqualTo(3_000));
        }

        #region Caps from config

        [Test]
        public void TheCapsComeFromThePublishedConfig()
        {
            WalletCaps caps = WalletCaps.From(TestGameConfig.Global());

            Assert.That(caps.CapOf(CurrencyType.Coins), Is.EqualTo(TestGameConfig.MaxCoins));
            Assert.That(caps.CapOf(CurrencyType.Gems), Is.EqualTo(TestGameConfig.MaxGems));
            Assert.That(caps.CapOf(CurrencyType.SpinTokens), Is.EqualTo(TestGameConfig.MaxSpinTokens));
        }

        /// <summary>
        /// An archive without the Global entry loads with caps of zero, which would read as "this player may hold
        /// nothing". The config load check refuses such an archive, and <see cref="WalletCaps.From"/> throws in
        /// case one is loaded anyway.
        /// </summary>
        [Test]
        public void AConfigWithNoCapsIsRefusedRatherThanReadAsNoLimit()
        {
            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => WalletCaps.From(new GlobalConfig()));

            Assert.That(error.Message, Does.Contain("MaxCoins"));
            Assert.That(error.Message, Does.Contain("tools/GameConfigGen"));
        }

        [Test]
        public void AConfigWithNoStartingWalletIsRefusedRatherThanReadAsEmpty()
        {
            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => PlayerWalletModel.Starting(new GlobalConfig()));

            Assert.That(error.Message, Does.Contain("tools/GameConfigGen"));
        }

        #endregion

        #region Serialization

        [Test]
        public void AWalletSurvivesARoundTrip()
        {
            PlayerWalletModel wallet = Wallet(coins: 3_000, gems: 100, spinTokens: 1);

            byte[]            bytes    = MetaSerialization.SerializeTagged(wallet, MetaSerializationFlags.IncludeAll, logicVersion: null);
            PlayerWalletModel restored = MetaSerialization.DeserializeTagged<PlayerWalletModel>(bytes, MetaSerializationFlags.IncludeAll, resolver: null, logicVersion: null);

            Assert.That(restored.Coins, Is.EqualTo(3_000));
            Assert.That(restored.Gems, Is.EqualTo(100));
            Assert.That(restored.SpinTokens, Is.EqualTo(1));
        }

        #endregion
    }

    /// <summary>
    /// Tests for <see cref="PlayerModel.ApplyWallet(WalletTransaction, bool, AnalyticsCorrelationId)"/>, which every
    /// meta feature uses to grant and spend. The action settles on both passes and commits once, the wallet
    /// writes the currency rows, and the feature writes its own event.
    /// <para>
    /// <see cref="TestPurchase"/> is written like a feature action, with the same dry-run and commit contract
    /// and the same steps in the same order, so these tests pin the pattern that feature actions follow. It
    /// calls the wallet first and returns early on any result other than success, as feature actions do.
    /// </para>
    /// </summary>
    [TestFixture]
    public class WalletFeatureActionTests
    {
        /// <summary>Written like a feature action, but not a real model action.</summary>
        sealed class TestPurchase
        {
            readonly CurrencyAmount   _price;
            readonly RewardBundle     _reward;
            readonly EconomyContentId _contentId;

            public TestPurchase(CurrencyAmount price, RewardBundle reward = null, string contentId = "frame.brass")
            {
                _price     = price;
                _reward    = reward;
                _contentId = EconomyContentId.FromString(contentId);
            }

            /// <summary>Whether the feature's own state was written, such as marking a cosmetic owned.</summary>
            public bool Delivered { get; private set; }

            public MetaActionResult Execute(PlayerModel player, bool commit)
            {
                WalletTransaction transaction = _reward == null
                    ? WalletTransaction.Spend(EconomyFeature.Cosmetics, EconomyReason.CosmeticPurchase, _price, _contentId)
                    : WalletTransaction.Exchange(EconomyFeature.Cosmetics, EconomyReason.CosmeticPurchase, _price, _reward, _contentId);

                AnalyticsCorrelationId correlation = AnalyticsCorrelationId.Create(player.PlayerId, player.CurrentTime, "cosmetic_purchase");

                MetaActionResult result = player.ApplyWallet(transaction, commit, correlation);
                if (result != MetaActionResult.Success)
                    return result;

                if (commit)
                    Delivered = true;

                return MetaActionResult.Success;
            }
        }

        static PlayerModel NewPlayer(List<PlayerEventBase> captured, RewardBundle startingWallet = null) =>
            TestPlayers.New(MetaTime.Epoch, TestGameConfig.Build(startingWallet), captured);

        static IEnumerable<PlayerEventEconomyTransaction> Transactions(IEnumerable<PlayerEventBase> events) =>
            events.OfType<PlayerEventEconomyTransaction>();

        /// <summary>
        /// The starting balances are the only balance change with no action behind it, so they are logged at
        /// the first login. Otherwise the log would show balances with no source.
        /// </summary>
        [Test]
        public void TheStartingWalletIsAccountedForAtTheFirstLogin()
        {
            List<PlayerEventBase> captured = new List<PlayerEventBase>();
            PlayerModel           player   = NewPlayer(captured);

            player.OnInitialLogin();

            List<PlayerEventEconomyTransaction> rows = Transactions(captured).ToList();

            Assert.That(rows.Count, Is.EqualTo(3));
            Assert.That(rows.Select(row => row.Currency), Is.EquivalentTo(new[] { CurrencyType.Coins, CurrencyType.Gems, CurrencyType.SpinTokens }));
            Assert.That(rows.All(row => row.Flow == CurrencyFlow.Source), Is.True);
            Assert.That(rows.All(row => row.Reason == EconomyReason.StartingWallet), Is.True);
            Assert.That(rows.All(row => row.BalanceBefore == 0), Is.True);
            Assert.That(rows.Select(row => row.Correlation).Distinct().Count(), Is.EqualTo(1), "the three rows are one cause and share one key");
        }

        [Test]
        public void ACurrencyTheStartingWalletDoesNotGrantGetsNoRow()
        {
            List<PlayerEventBase> captured = new List<PlayerEventBase>();
            PlayerModel           player   = NewPlayer(captured, new RewardBundle(CurrencyAmount.Coins(500), CurrencyAmount.SpinTokens(1)));

            player.OnInitialLogin();

            Assert.That(Transactions(captured).Select(row => row.Currency), Is.EquivalentTo(new[] { CurrencyType.Coins, CurrencyType.SpinTokens }));
        }

        /// <summary>
        /// An action runs as a dry run and then a commit, must return the same result both times, and must write
        /// nothing on the dry run. The test compares the serialized model rather than the balances, because a
        /// dry run that changed any other member, such as a lazily filled field, would also cause a desync.
        /// </summary>
        [Test]
        public void ADryRunMovesNothingAndEmitsNothing()
        {
            List<PlayerEventBase> captured = new List<PlayerEventBase>();
            PlayerModel           player   = NewPlayer(captured);

            byte[] before = TestPlayers.Snapshot(player);

            Assert.That(new TestPurchase(CurrencyAmount.Coins(900)).Execute(player, commit: false), Is.EqualTo(MetaActionResult.Success));

            Assert.That(TestPlayers.Snapshot(player), Is.EqualTo(before), "a dry run moved player state");
            Assert.That(captured, Is.Empty, "a dry run wrote an event, so every replay would double-count it");

            // The commit does change the model, which shows the comparison above can detect a change.
            Assert.That(new TestPurchase(CurrencyAmount.Coins(900)).Execute(player, commit: true), Is.EqualTo(MetaActionResult.Success));
            Assert.That(TestPlayers.Snapshot(player), Is.Not.EqualTo(before));
            Assert.That(player.Wallet.Coins, Is.EqualTo(2_100));
        }

        [Test]
        public void ADryRunOfEveryReadOnTheSeamMovesNothing()
        {
            // PreviewWallet is the preview an insufficient-funds screen uses, and it runs outside any action. It
            // must not change the model.
            List<PlayerEventBase> captured = new List<PlayerEventBase>();
            PlayerModel           player   = NewPlayer(captured);

            byte[] before = TestPlayers.Snapshot(player);

            player.PreviewWallet(WalletTransaction.Spend(EconomyFeature.Cosmetics, EconomyReason.CosmeticPurchase, CurrencyAmount.Coins(10_000)));
            player.PreviewWallet(WalletTransaction.Grant(EconomyFeature.DailyReward, EconomyReason.DailyReward, new RewardBundle(CurrencyAmount.Coins(10))));
            new PlayerPropertyCoins().GetTypedValueForPlayer(player);

            Assert.That(TestPlayers.Snapshot(player), Is.EqualTo(before));
            Assert.That(captured, Is.Empty);
        }

        [Test]
        public void ACommittedPurchaseMovesTheBalanceAndAccountsForIt()
        {
            List<PlayerEventBase> captured = new List<PlayerEventBase>();
            PlayerModel           player   = NewPlayer(captured);

            Assert.That(new TestPurchase(CurrencyAmount.Coins(2_500)).Execute(player, commit: true), Is.EqualTo(MetaActionResult.Success));

            Assert.That(player.Wallet.Coins, Is.EqualTo(500));

            PlayerEventEconomyTransaction row = Transactions(captured).Single();
            Assert.That(row.Currency, Is.EqualTo(CurrencyType.Coins));
            Assert.That(row.Flow, Is.EqualTo(CurrencyFlow.Sink));
            Assert.That(row.Amount, Is.EqualTo(2_500));
            Assert.That(row.BalanceBefore, Is.EqualTo(3_000));
            Assert.That(row.BalanceAfter, Is.EqualTo(500));
            Assert.That(row.Reason, Is.EqualTo(EconomyReason.CosmeticPurchase));
            Assert.That(row.Feature, Is.EqualTo(EconomyFeature.Cosmetics));
            Assert.That(row.ContentId, Is.EqualTo(EconomyContentId.FromString("frame.brass")));
            Assert.That(row.Correlation.IsSet, Is.True);
        }

        [Test]
        public void EveryRowFromOneActionSharesOneCorrelationKey()
        {
            List<PlayerEventBase> captured = new List<PlayerEventBase>();
            PlayerModel           player   = NewPlayer(captured);

            new TestPurchase(CurrencyAmount.Gems(100), new RewardBundle(CurrencyAmount.SpinTokens(5), CurrencyAmount.Coins(2_000))).Execute(player, commit: true);

            List<PlayerEventEconomyTransaction> rows = Transactions(captured).ToList();

            Assert.That(rows.Count, Is.EqualTo(3));
            Assert.That(rows.Select(row => row.Correlation).Distinct().Count(), Is.EqualTo(1));
            Assert.That(rows[0].Correlation.IsSet, Is.True);
        }

        /// <summary>
        /// Feature actions return as soon as the result is not success, and <see cref="TestPurchase"/> calls the
        /// wallet once and returns immediately on failure. So the call that produces the result must also log the
        /// refusal, or the rejection event would never be written and nothing would notice.
        /// </summary>
        [Test]
        public void AnUnaffordablePurchaseChangesNothingAndSaysWhy()
        {
            List<PlayerEventBase> captured = new List<PlayerEventBase>();
            PlayerModel           player   = NewPlayer(captured);

            Assert.That(new TestPurchase(CurrencyAmount.Coins(10_000)).Execute(player, commit: true), Is.EqualTo(ActionResults.InsufficientFunds));

            Assert.That(player.Wallet.Coins, Is.EqualTo(3_000));
            Assert.That(Transactions(captured), Is.Empty, "a refused spend wrote a transaction row");

            PlayerEventEconomySpendRejected rejected = captured.OfType<PlayerEventEconomySpendRejected>().Single();
            Assert.That(rejected.Currency, Is.EqualTo(CurrencyType.Coins));
            Assert.That(rejected.Requested, Is.EqualTo(10_000));
            Assert.That(rejected.Balance, Is.EqualTo(3_000));
            Assert.That(rejected.Feature, Is.EqualTo(EconomyFeature.Cosmetics));
            Assert.That(rejected.ContentId, Is.EqualTo(EconomyContentId.FromString("frame.brass")));
        }

        [Test]
        public void ADryRunOfAnUnaffordablePurchaseIsSilentAndStillRefuses()
        {
            List<PlayerEventBase> captured = new List<PlayerEventBase>();
            PlayerModel           player   = NewPlayer(captured);

            Assert.That(new TestPurchase(CurrencyAmount.Coins(10_000)).Execute(player, commit: false), Is.EqualTo(ActionResults.InsufficientFunds));
            Assert.That(captured, Is.Empty);
        }

        /// <summary>
        /// A cap refusal is a defect, not a player decision. Logging it as a rejected spend would distort the
        /// rate at which players cannot afford things.
        /// </summary>
        [Test]
        public void ACapRefusalIsNotReportedAsARejectedSpend()
        {
            List<PlayerEventBase> captured = new List<PlayerEventBase>();
            PlayerModel           player   = NewPlayer(captured);

            MetaActionResult result = player.ApplyWallet(
                WalletTransaction.Grant(EconomyFeature.DailyReward, EconomyReason.DailyReward, new RewardBundle(CurrencyAmount.Coins(TestGameConfig.MaxCoins))),
                commit: true,
                AnalyticsCorrelationId.Create(player.PlayerId, player.CurrentTime, "daily_reward"),
                out WalletSettlement settlement);

            Assert.That(settlement.Refusal, Is.EqualTo(WalletRefusal.CapExceeded));
            Assert.That(result, Is.EqualTo(ActionResults.WalletCapExceeded));

            Assert.That(captured, Is.Empty);
            Assert.That(player.Wallet.Coins, Is.EqualTo(3_000));
        }

        [Test]
        public void ASettlementThatMovesNothingEmitsNothing()
        {
            List<PlayerEventBase> captured = new List<PlayerEventBase>();
            PlayerModel           player   = NewPlayer(captured);

            byte[] before = TestPlayers.Snapshot(player);

            MetaActionResult result = player.ApplyWallet(
                WalletTransaction.Grant(EconomyFeature.DailyReward, EconomyReason.DailyReward, new RewardBundle()),
                commit: true,
                AnalyticsCorrelationId.Create(player.PlayerId, player.CurrentTime, "daily_reward"));

            // A reward that grants nothing still succeeds. The owning feature, not the wallet, decides whether an
            // empty bundle is a bug.
            Assert.That(result, Is.EqualTo(MetaActionResult.Success));
            Assert.That(TestPlayers.Snapshot(player), Is.EqualTo(before));
            Assert.That(captured, Is.Empty, "a no-op wrote a transaction row");
        }

        /// <summary>
        /// <c>ApplyWallet</c> takes the transaction, not a settlement, and settles it immediately before applying.
        /// So a settlement computed earlier cannot be applied to a changed wallet, and each application charges
        /// the current balance.
        /// </summary>
        [Test]
        public void EachApplicationSettlesAgainstTheBalanceAsItStands()
        {
            List<PlayerEventBase> captured = new List<PlayerEventBase>();
            PlayerModel           player   = NewPlayer(captured);

            WalletTransaction      spend       = WalletTransaction.Spend(EconomyFeature.Cosmetics, EconomyReason.CosmeticPurchase, CurrencyAmount.Coins(1_000));
            AnalyticsCorrelationId correlation = AnalyticsCorrelationId.Create(player.PlayerId, player.CurrentTime, "cosmetic_purchase");

            // Settle once before either application. This settlement is not used afterwards.
            WalletSettlement stale = player.PreviewWallet(spend);
            Assert.That(stale.WalletAfter.Coins, Is.EqualTo(2_000));

            player.ApplyWallet(spend, commit: true, correlation);
            player.ApplyWallet(spend, commit: true, correlation);

            Assert.That(player.Wallet.Coins, Is.EqualTo(1_000), "the second application reused the first one's numbers");

            List<PlayerEventEconomyTransaction> rows = Transactions(captured).ToList();
            Assert.That(rows[1].BalanceBefore, Is.EqualTo(2_000));
            Assert.That(rows[1].BalanceAfter, Is.EqualTo(1_000));
        }

        /// <summary>
        /// <see cref="WalletSettlement"/> has no public member that returns a <see cref="MetaActionResult"/>.
        /// Such a member would let an action refuse a spend without the refusal reaching the log.
        /// </summary>
        [Test]
        public void ASettlementOffersNoPublicRouteToAnActionResult()
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.FlattenHierarchy;

            IEnumerable<string> routes = typeof(WalletSettlement).GetProperties(flags).Where(p => p.PropertyType == typeof(MetaActionResult)).Select(p => p.Name)
                .Concat(typeof(WalletSettlement).GetFields(flags).Where(f => f.FieldType == typeof(MetaActionResult)).Select(f => f.Name))
                .Concat(typeof(WalletSettlement).GetMethods(flags).Where(m => m.ReturnType == typeof(MetaActionResult)).Select(m => m.Name));

            Assert.That(routes, Is.Empty,
                "a settlement handed out an action result, so a feature could refuse a spend without recording the refusal");
        }

        /// <summary>
        /// The feature's own state is written only after the wallet succeeds. An action that delivered first
        /// would deliver on a refused spend.
        /// </summary>
        [Test]
        public void TheFeaturesOwnStateFollowsTheWalletRatherThanPrecedingIt()
        {
            List<PlayerEventBase> captured = new List<PlayerEventBase>();
            PlayerModel           player   = NewPlayer(captured);

            TestPurchase affordable = new TestPurchase(CurrencyAmount.Coins(2_500));
            TestPurchase tooDear    = new TestPurchase(CurrencyAmount.Coins(10_000));

            Assert.That(tooDear.Execute(player, commit: true), Is.EqualTo(ActionResults.InsufficientFunds));
            Assert.That(tooDear.Delivered, Is.False, "a refused purchase delivered its contents");

            Assert.That(affordable.Execute(player, commit: true), Is.EqualTo(MetaActionResult.Success));
            Assert.That(affordable.Delivered, Is.True);
        }

        [Test]
        public void TheTypedPlayerPropertiesReadTheAuthoritativeBalance()
        {
            List<PlayerEventBase> captured = new List<PlayerEventBase>();
            PlayerModel           player   = NewPlayer(captured);

            new TestPurchase(CurrencyAmount.Coins(2_500)).Execute(player, commit: true);

            Assert.That(new PlayerPropertyCoins().GetTypedValueForPlayer(player), Is.EqualTo(500));
            Assert.That(new PlayerPropertyGems().GetTypedValueForPlayer(player), Is.EqualTo(100));
            Assert.That(new PlayerPropertySpinTokens().GetTypedValueForPlayer(player), Is.EqualTo(1));
        }

        /// <summary>A segment condition on a balance reads the current balance, not a copy.</summary>
        [Test]
        public void ATypedPropertyCanBeUsedAsASegmentRequirement()
        {
            List<PlayerEventBase> captured = new List<PlayerEventBase>();
            PlayerModel           player   = NewPlayer(captured);

            PlayerPropertyRequirement richEnough = PlayerPropertyRequirement.ParseFromStrings(new PlayerPropertyCoins(), minStr: "1000", maxStr: null);

            Assert.That(richEnough.MatchesPlayer(player), Is.True);

            new TestPurchase(CurrencyAmount.Coins(2_500)).Execute(player, commit: true);

            Assert.That(richEnough.MatchesPlayer(player), Is.False);
        }
    }
}
