using Metaplay.Core;
using Metaplay.Core.Analytics;
using Metaplay.Core.Config;
using Metaplay.Core.InAppPurchase;
using Metaplay.Core.Model;
using Metaplay.Core.Player;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Game.Logic.Tests
{
    /// <summary>
    /// Tests the demo purchase (<c>docs/offers.md</c>): the receipt the web client builds for the SDK's Development
    /// purchase platform, and how the player model grants a purchase the server has validated.
    /// <para>
    /// Server-side validation is tested against a live server by <c>LiveServerDemoPurchaseTests</c>. The server
    /// recomputes the signature, checks the transaction id against stored purchases, and refuses the Development
    /// platform outside a development environment. This fixture tests only the receipt format and the grant.
    /// </para>
    /// </summary>
    [TestFixture]
    public class DemoPurchaseTests
    {
        static readonly InAppProductId Bundle = InAppProductId.FromString("gem_booster_pack");

        static DemoInAppProductInfo Product(RewardBundle contents = null) =>
            new DemoInAppProductInfo(
                Bundle,
                "Gem Booster Pack",
                "Power up your game.",
                referencePriceUsd: 4.99,
                contents: contents ?? new RewardBundle(
                    CurrencyAmount.Gems(1_250),
                    CurrencyAmount.Coins(10_000),
                    CurrencyAmount.SpinTokens(3)));

        /// <summary>Creates a new player whose config holds only <paramref name="product"/> as an in-app product.</summary>
        static PlayerModel NewPlayer(List<PlayerEventBase> captured = null, DemoInAppProductInfo product = null)
        {
            SharedGameConfig config = TestGameConfig.Build();
            TestGameConfig.SetEntry(config, "InAppProducts",
                GameConfigLibrary<InAppProductId, DemoInAppProductInfo>.CreateSolo(new[] { product ?? Product() }));

            return TestPlayers.New(MetaTime.Epoch, config, captured);
        }

        /// <summary>
        /// Grants a validated demo purchase of the product in the player's config, with the transaction id
        /// <paramref name="transactionId"/>, and returns the content the player model recorded as granted.
        /// </summary>
        static ResolvedPurchaseContentBase ClaimValidatedPurchase(PlayerModel player, string transactionId)
        {
            DemoInAppProductInfo product = player.GameConfig.InAppProducts[Bundle];
            player.OnClaimedInAppProduct(DemoPurchaseReceipt.CreatePurchaseEvent(product, transactionId), product, out ResolvedPurchaseContentBase resolved);
            return resolved;
        }

        static string ReceiptJsonOf(InAppPurchaseEvent purchase) =>
            Encoding.UTF8.GetString(Convert.FromBase64String(purchase.Receipt));

        #region The receipt

        /// <summary>
        /// The receipt carries the fields the server's Development validator reads. The validator parses the JSON
        /// and compares its transaction id, its platform product id and its SHA-1 with the values on the purchase
        /// event, and refuses the purchase on any mismatch.
        /// </summary>
        [Test]
        public void TheReceiptCarriesWhatTheValidatorComparesAgainst()
        {
            DemoInAppProductInfo product     = Product();
            InAppPurchaseEvent   purchase    = DemoPurchaseReceipt.CreatePurchaseEvent(product, "demo_txn_1");
            string               receiptJson = ReceiptJsonOf(purchase);

            Assert.That(purchase.Platform, Is.EqualTo(InAppPurchasePlatformDevelopment.Development));
            Assert.That(purchase.Status, Is.EqualTo(InAppPurchaseStatus.PendingValidation));
            Assert.That(purchase.ProductId, Is.EqualTo(Bundle));
            Assert.That(purchase.PlatformProductId, Is.EqualTo("dev.gem_booster_pack"), "the fake store's product id is the config's DevelopmentId");

            Assert.That(receiptJson, Does.Contain("\"transactionId\":\"demo_txn_1\""));
            Assert.That(receiptJson, Does.Contain("\"productId\":\"dev.gem_booster_pack\""));
            Assert.That(receiptJson, Does.Contain("\"paymentType\":\"Normal\""));

            // The server recomputes the signature over the JSON before base64 encoding.
            Assert.That(purchase.Signature, Is.EqualTo(Util.ComputeSHA1(receiptJson)));
        }

        /// <summary>
        /// <see cref="InAppPurchaseUtil.IsValidPurchaseEvent"/> accepts the event the client builds.
        /// <c>PlayerInAppPurchased</c> runs this check before the server validates the purchase.
        /// </summary>
        [Test]
        public void TheSdkAcceptsTheEventAsWellFormed()
        {
            InAppPurchaseEvent purchase = DemoPurchaseReceipt.CreatePurchaseEvent(Product(), "demo_txn_1");

            Assert.That(InAppPurchaseUtil.IsValidPurchaseEvent(purchase), Is.True);
        }

        [Test]
        public void EveryAttemptGetsItsOwnTransactionId()
        {
            string first  = DemoPurchaseReceipt.NewTransactionId(Bundle, Guid.NewGuid());
            string second = DemoPurchaseReceipt.NewTransactionId(Bundle, Guid.NewGuid());

            Assert.That(first, Is.Not.EqualTo(second));
            Assert.That(first, Does.StartWith("demo_gem_booster_pack_"));
            Assert.That(Util.GetNumUnicodeCodePointsPermissive(first),
                Is.LessThanOrEqualTo(InAppPurchaseUtil.TransactionIdMaxLengthCodePoints),
                "a transaction id longer than this cannot be a database key");
        }

        [Test]
        public void AReceiptNeedsAProductAndATransaction()
        {
            Assert.Throws<ArgumentException>(() => DemoPurchaseReceipt.BuildReceiptJson(null, "demo_txn_1"));
            Assert.Throws<ArgumentException>(() => DemoPurchaseReceipt.BuildReceiptJson("dev.x", ""));
            Assert.Throws<ArgumentNullException>(() => DemoPurchaseReceipt.CreatePurchaseEvent(null, "demo_txn_1"));
        }

        #endregion

        #region The grant

        /// <summary>
        /// A validated purchase grants its bundle through the wallet and writes one <c>economy_transaction</c>
        /// source row per currency, so an IAP grant reconciles like any other grant (<c>docs/economy.md</c>).
        /// </summary>
        [Test]
        public void AValidatedPurchaseSettlesThroughTheWallet()
        {
            List<PlayerEventBase> captured = new List<PlayerEventBase>();
            PlayerModel           player   = NewPlayer(captured);
            captured.Clear();

            ResolvedPurchaseContentBase resolved = ClaimValidatedPurchase(player, "demo_txn_1");

            Assert.That(player.Wallet.Coins, Is.EqualTo(13_000));
            Assert.That(player.Wallet.Gems, Is.EqualTo(1_350));
            Assert.That(player.Wallet.SpinTokens, Is.EqualTo(4));

            List<PlayerEventEconomyTransaction> rows = captured.OfType<PlayerEventEconomyTransaction>().ToList();
            Assert.That(rows, Has.Count.EqualTo(3), "one row per currency the bundle granted");
            Assert.That(rows.Select(row => row.Flow), Is.All.EqualTo(CurrencyFlow.Source));
            Assert.That(rows.Select(row => row.Reason), Is.All.EqualTo(EconomyReason.IapGrant));
            Assert.That(rows.Select(row => row.Feature), Is.All.EqualTo(EconomyFeature.Iap));
            Assert.That(rows.Select(row => row.ContentId.Value), Is.All.EqualTo("gem_booster_pack"));
            Assert.That(rows.Select(row => row.Correlation).Distinct().Count(), Is.EqualTo(1),
                "every row of one purchase carries the same correlation id");

            // The resolved content records what was granted, so a later config change does not alter the record.
            ResolvedWalletBundle bundle = resolved as ResolvedWalletBundle;
            Assert.That(bundle, Is.Not.Null);
            Assert.That(bundle.Contents.AmountOf(CurrencyType.Gems), Is.EqualTo(1_250));
        }

        [Test]
        public void ThePurchaseIsRecordedSoTheBundleCanBeOfferedOnceOnly()
        {
            PlayerModel player = NewPlayer();

            Assert.That(player.HasPurchased(Bundle), Is.False);

            ClaimValidatedPurchase(player, "demo_txn_1");

            Assert.That(player.HasPurchased(Bundle), Is.True);
            Assert.That(player.DemoPurchases, Is.EquivalentTo(new[] { Bundle }));
        }

        /// <summary>
        /// A second purchase of the same bundle grants nothing, even with a new, validated receipt. The limit is
        /// one purchase per player. The SDK's duplicate check only refuses a reused transaction id, so the player
        /// model enforces this limit.
        /// </summary>
        [Test]
        public void ASecondPurchaseOfTheSameBundleGrantsNothing()
        {
            List<PlayerEventBase> captured = new List<PlayerEventBase>();
            PlayerModel           player   = NewPlayer(captured);

            ClaimValidatedPurchase(player, "demo_txn_1");
            captured.Clear();

            ResolvedPurchaseContentBase resolved = ClaimValidatedPurchase(player, "demo_txn_2");

            Assert.That(player.Wallet.Coins, Is.EqualTo(13_000), "the balances are the ones the first purchase left");
            Assert.That(player.Wallet.Gems, Is.EqualTo(1_350));
            Assert.That(player.Wallet.SpinTokens, Is.EqualTo(4));
            Assert.That(player.DemoPurchases, Has.Count.EqualTo(1));
            Assert.That(resolved, Is.Null, "nothing was granted, so there is nothing to record as granted");
            Assert.That(captured.OfType<PlayerEventEconomyTransaction>(), Is.Empty);
        }

        /// <summary>
        /// A validated purchase is granted in full even when it takes a balance past its cap. The SDK has consumed
        /// the receipt by now, so refusing the grant would take the player's money and grant nothing.
        /// </summary>
        [Test]
        public void AGrantThatWouldPassACapStillLandsInFull()
        {
            DemoInAppProductInfo oversized = Product(new RewardBundle(
                CurrencyAmount.Coins(10),
                CurrencyAmount.SpinTokens(TestGameConfig.MaxSpinTokens)));

            PlayerModel                 player   = NewPlayer(product: oversized);
            ResolvedPurchaseContentBase resolved = ClaimValidatedPurchase(player, "demo_txn_1");

            Assert.That(player.Wallet.Coins, Is.EqualTo(3_010));
            Assert.That(player.Wallet.SpinTokens, Is.EqualTo(1 + TestGameConfig.MaxSpinTokens), "the tokens were not granted past the cap");
            Assert.That(player.HasPurchased(Bundle), Is.True);
            Assert.That(resolved, Is.Not.Null);
        }

        #endregion

        #region The product config

        [Test]
        public void ADemoProductNamesNoRealStore()
        {
            DemoInAppProductInfo product = Product();

            Assert.That(product.DevelopmentId, Is.EqualTo("dev.gem_booster_pack"));
            Assert.That(product.GoogleId, Is.Null);
            Assert.That(product.AppleId, Is.Null);
            Assert.That(product.SteamId, Is.Null);

            // The product is consumable, so the SDK refuses a reused receipt instead of treating it as a restore
            // of an owned product.
            Assert.That(product.Type, Is.EqualTo(InAppProductType.Consumable));
            Assert.That(product.DemoPriceText, Is.EqualTo("$4.99"));
        }

        #endregion
    }
}
