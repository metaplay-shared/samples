using Metaplay.Core;
using Metaplay.Core.Analytics;
using Metaplay.Core.Config;
using Metaplay.Core.InAppPurchase;
using Metaplay.Core.Model;
using Metaplay.Core.Player;
using Metaplay.Core.Rewards;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.Logic.Tests
{
    /// <summary>
    /// Counts the rows that the Dashboard demo path writes to one player's event log (<c>docs/analytics.md</c>,
    /// "Tests"), by running the shared actions a real client sends. <c>LiveServerAnalyticsDemoTests</c> counts the
    /// same script through a browser against a real server. The count is asserted exactly.
    /// <para>
    /// Only the live fixture catches two changes: a currency added to the Starter Pack in the published config,
    /// because this walk reads <c>TestGameConfig</c>, and a second server-emitted event, because
    /// <see cref="RowsEmittedByTheServer"/> is a constant.
    /// </para>
    /// </summary>
    [TestFixture]
    public class AnalyticsDemoRowCountTests
    {
        static readonly MetaTime Noon = MetaTime.FromDateTime(new DateTime(2026, 3, 10, 12, 0, 0, DateTimeKind.Utc));

        /// <summary>The index of the sector in the fixture's wheel table that pays 100 coins.</summary>
        const int HundredCoinsSector = 0;

        /// <summary>
        /// Runs the analytics demo script against a new player model: account creation, screen views, promoted
        /// entries, a daily reward claim, a wheel spin and the demo purchase. Returns every event the model emitted.
        /// The script leaves out the rename, which <c>PlayerActor</c> emits on the server.
        /// </summary>
        static List<PlayerEventBase> WalkTheDemoPath()
        {
            List<PlayerEventBase> captured = new List<PlayerEventBase>();

            // 1. Account creation. The server calls OnInitialLogin once, on the login that created the account,
            //    and it writes the opening wallet and identity rows.
            PlayerModel player = TestPlayers.New(Noon, captured: captured);
            player.OnInitialLogin();

            // 2. Home, the Events hub, back to Home.
            Observe(player, ShellScreen.Home);
            Observe(player, ShellScreen.Events);
            Observe(player, ShellScreen.Home);

            // 3. The next-action card into the daily reward, and the claim.
            SelectPromotedEntry(player, PromotedEntryPlacement.NextActionCard, ShellScreen.Home, ShellScreen.DailyReward);
            Observe(player, ShellScreen.DailyReward);
            Claim(player);

            // 4. Home, then Profile through Home's identity row. The rename on Profile happens on the server.
            Observe(player, ShellScreen.Home);
            Observe(player, ShellScreen.Profile);

            // 5. The Events hub, the wheel through its feature card, and one spin.
            Observe(player, ShellScreen.Events);
            SelectPromotedEntry(player, PromotedEntryPlacement.FeatureCard, ShellScreen.Events, ShellScreen.SpinWheel);
            Observe(player, ShellScreen.SpinWheel);
            Spin(player);

            // 6. The Shop, and the demo purchase of the Starter Pack through the offer that sells it.
            Observe(player, ShellScreen.Shop);
            Buy(player);

            return captured;
        }

        static void Observe(PlayerModel player, ShellScreen screen) =>
            Assert.That(new PlayerObserveScreenViewed(screen).Execute(player, commit: true), Is.EqualTo(MetaActionResult.Success));

        static void SelectPromotedEntry(PlayerModel player, PromotedEntryPlacement placement, ShellScreen from, ShellScreen destination) =>
            Assert.That(new PlayerObservePromotedEntrySelected(placement, from, destination).Execute(player, commit: true), Is.EqualTo(MetaActionResult.Success));

        static void Claim(PlayerModel player)
        {
            int activation = DailyRewardCalendar.ActivationAt(TestGameConfig.DailyReset(), new PlayerLocalTime(Noon, MetaDuration.Zero)).Index;
            Assert.That(new PlayerDailyRewardClaimed(activation, Noon).Execute(player, commit: true), Is.EqualTo(MetaActionResult.Success));
        }

        static void Spin(PlayerModel player)
        {
            Assert.That(new PlayerWheelSpinResolved(player.SpinWheel.NextOrdinal, HundredCoinsSector, Noon).Execute(player, commit: true), Is.EqualTo(MetaActionResult.Success));
            Assert.That(new PlayerAcknowledgeWheelSpin().Execute(player, commit: true), Is.EqualTo(MetaActionResult.Success));
        }

        /// <summary>
        /// Runs the demo purchase the way the SDK claims it. The product has no fixed contents, so
        /// <see cref="PlayerModel.OnClaimedInAppProduct"/> grants nothing. The offer's rewards move the wallet,
        /// because the SDK's dynamic-content claim path consumes them after the receipt validates.
        /// </summary>
        static void Buy(PlayerModel player)
        {
            DemoInAppProductInfo product = player.GameConfig.InAppProducts[TestGameConfig.DemoStarterPack1];

            // The SDK consumes the offer's rewards before it calls OnClaimedInAppProduct, so this does the same.
            // The offer's rewards do not read the reward source, so it is null.
            foreach (MetaPlayerRewardBase reward in player.GameConfig.Offers[TestGameConfig.StarterPack1Offer].Rewards)
                reward.InvokeConsume(player, source: null);

            player.OnClaimedInAppProduct(
                DemoPurchaseReceipt.CreatePurchaseEvent(product, "demo_txn_1"),
                product,
                out ResolvedPurchaseContentBase _);
        }

        /// <summary>
        /// The number of demo-path rows that this walk cannot produce: the accepted rename, which
        /// <c>PlayerActor</c> emits after the server validates the name.
        /// </summary>
        const int RowsEmittedByTheServer = 1;

        /// <summary>
        /// The row count of the demo path, measured through a browser against a live server, with a spin that
        /// lands on a paying sector. A spin that lands on the blank sector writes one row fewer, and
        /// <c>LiveServerAnalyticsDemoTests</c> adjusts its expected total by the outcome in its log.
        /// </summary>
        const int MeasuredDemoPathRows = 24;

        [Test]
        public void TheDemoPathCostsWhatItIsMeasuredToCost()
        {
            List<PlayerEventBase> captured = WalkTheDemoPath();

            string composition = string.Join(", ", captured
                .GroupBy(e => e.GetType().Name)
                .OrderByDescending(group => group.Count())
                .Select(group => $"{group.Key} x{group.Count()}"));

            Assert.That(captured.Count + RowsEmittedByTheServer, Is.EqualTo(MeasuredDemoPathRows),
                "the demo path's event count moved. Re-measure it with LiveServerAnalyticsDemoTests and update the " +
                "constant there in the same change (docs/analytics.md, \"Tests\"). Walked here: " + composition);
        }

        /// <summary>
        /// Checks the row count per event type. A developer budgeting their own taxonomy needs the cost per
        /// action, not only the total.
        /// </summary>
        [Test]
        public void EachEventTypeWritesTheDocumentedRowCount()
        {
            Dictionary<string, int> byType = WalkTheDemoPath()
                .GroupBy(e => e.GetType().Name)
                .ToDictionary(group => group.Key, group => group.Count());

            // One currency row per currency moved, never a netted summary: the opening balances, the daily
            // reward, the spin's token cost and prize, and the purchased bundle.
            Assert.That(byType[nameof(PlayerEventEconomyTransaction)], Is.EqualTo(9));

            // Screen views and promoted-entry selections are the client-observation events, and the largest share
            // of the log.
            Assert.That(byType[nameof(PlayerEventScreenViewed)], Is.EqualTo(9));
            Assert.That(byType[nameof(PlayerEventPromotedEntrySelected)], Is.EqualTo(2));

            // One row per feature outcome.
            Assert.That(byType[nameof(PlayerEventIdentityInitialized)], Is.EqualTo(1));
            Assert.That(byType[nameof(PlayerEventDailyRewardClaimed)], Is.EqualTo(1));
            Assert.That(byType[nameof(PlayerEventWheelSpinResolved)], Is.EqualTo(1));
        }

        /// <summary>
        /// Checks that the purchase rows come from the offer that sold the pack, not from the product. Both paths
        /// write the same number of rows, so the test checks the reason on each row.
        /// </summary>
        [Test]
        public void ThePurchaseGrantsThroughTheOfferThatSoldIt()
        {
            PlayerEventEconomyTransaction[] purchased = WalkTheDemoPath()
                .OfType<PlayerEventEconomyTransaction>()
                .Where(row => row.Feature == EconomyFeature.Offers)
                .ToArray();

            Assert.That(purchased, Has.Length.EqualTo(3), "one row per currency the Starter Pack granted");
            Assert.That(purchased.Select(row => row.Reason), Is.All.EqualTo(EconomyReason.OfferPurchase));
            Assert.That(purchased.Select(row => row.Correlation).Distinct().Count(), Is.EqualTo(1),
                "every row of one purchase carries the same correlation id");
        }

        /// <summary>
        /// Checks that each currency row has the keyword of the one direction it moved, so the Dashboard's
        /// <c>Sink</c> filter isolates spending. <c>AnalyticsContractTests</c> checks the same rule on rows built
        /// by hand. This test checks it on rows emitted by the game's actions, where a direction read from the
        /// wrong field would show.
        /// </summary>
        [Test]
        public void EveryCurrencyRowInTheWalkIsFiledUnderOneDirection()
        {
            foreach (PlayerEventEconomyTransaction row in WalkTheDemoPath().OfType<PlayerEventEconomyTransaction>())
            {
                string direction = row.Flow == CurrencyFlow.Sink ? AnalyticsKeywords.Sink : AnalyticsKeywords.Source;

                // Keywords is the SDK's merged keyword set, the value the Dashboard filters on.
                Assert.That(row.Keywords, Is.EquivalentTo(new[] { AnalyticsKeywords.Economy, direction }), row.EventDescription);
            }
        }
    }
}
