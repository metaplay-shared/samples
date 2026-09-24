using Metaplay.Core;
using Metaplay.Core.Analytics;
using Metaplay.Core.Model;
using Metaplay.Core.Player;
using Metaplay.Core.Serialization;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.Logic.Tests
{
    /// <summary>
    /// Tests for the wheel table in the config and the odds computed from it.
    /// <para>
    /// <b>The pictured wheel is the probability model</b>: the sectors are equal, a reward that appears in more
    /// sectors is more likely, and there are no weights. These tests check that the odds shown to the player are
    /// counted from the same sector list the wheel is drawn from.
    /// </para>
    /// </summary>
    [TestFixture]
    public class WheelTableTests
    {
        static WheelTableInfo Baseline() => TestGameConfig.WheelTable();

        [Test]
        public void TheBaselineTableIsTenEqualSectors()
        {
            WheelTableInfo table = Baseline();

            Assert.That(table.IsComplete, Is.True);
            Assert.That(table.Sectors.Count, Is.EqualTo(WheelTableInfo.NumSectors));
            Assert.That(table.Sectors.Select(sector => sector.Sector), Is.EqualTo(new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 }));
            Assert.That(table.Sectors.Select(sector => sector.Id).Distinct().Count(), Is.EqualTo(WheelTableInfo.NumSectors));

            // The wheel's balance is tuned through how many sectors each prize tier gets.
            Assert.That(table.Sectors.Count(sector => sector.IsBlank), Is.EqualTo(1), "the wheel has exactly one blank");
            Assert.That(table.Sectors.Count(sector => sector.Tier == WheelPrizeTier.SpinAgain), Is.EqualTo(2), "the wheel has exactly two spin-agains");
            Assert.That(table.Sectors.Count(sector => sector.Tier == WheelPrizeTier.Rare), Is.EqualTo(1), "the wheel has exactly one jackpot");
            Assert.That(table.Sectors.Where(sector => sector.Tier == WheelPrizeTier.Rare).Single().Amount,
                Is.EqualTo(table.Sectors.Where(sector => sector.Currency == CurrencyType.Coins).Max(sector => sector.Amount)),
                "the jackpot is the largest coin prize");
        }

        /// <summary>
        /// The odds combine repeated sectors into one odds entry, and list the blank last. No odds are written in
        /// the config: each chance is computed from the sector counts.
        /// </summary>
        [Test]
        public void ThePublishedOddsAreCountedFromTheSectorsAndSumToAHundred()
        {
            IReadOnlyList<WheelOddsEntry> odds = Baseline().Odds();

            Assert.That(odds.Select(o => (o.Currency, o.Amount, o.ChancePercent)), Is.EqualTo(new[]
            {
                (CurrencyType.Coins, 100, 10),
                (CurrencyType.Coins, 250, 20),
                (CurrencyType.Coins, 500, 20),
                (CurrencyType.Coins, 1000, 10),
                (CurrencyType.Gems, 30, 10),
                (CurrencyType.SpinTokens, 1, 20),
                (CurrencyType.None, 0, 10),
            }));

            Assert.That(odds.Sum(o => o.ChancePercent), Is.EqualTo(100));
            Assert.That(odds.Sum(o => o.SectorCount), Is.EqualTo(WheelTableInfo.NumSectors));

            // The two spin-again sectors are published as one spin-token odds entry.
            Assert.That(odds.Single(o => o.Currency == CurrencyType.SpinTokens).SectorCount, Is.EqualTo(2));
            Assert.That(Baseline().ChancePercentOf(CurrencyType.SpinTokens), Is.EqualTo(20));
        }

        /// <summary>
        /// The odds are computed from the table, not stored. Changing what a sector pays changes the odds.
        /// </summary>
        [Test]
        public void ChangingASectorChangesThePublishedOdds()
        {
            List<WheelSectorInfo> sectors = Baseline().Sectors.ToList();
            sectors[1] = TestGameConfig.Sector(2, CurrencyAmount.Coins(100), WheelPrizeTier.Common);

            IReadOnlyList<WheelOddsEntry> odds = new WheelTableInfo(TestGameConfig.Wheel, sectors).Odds();

            Assert.That(odds.First(o => o.Amount == 100 && o.Currency == CurrencyType.Coins).ChancePercent, Is.EqualTo(20));
            Assert.That(odds.First(o => o.Amount == 500 && o.Currency == CurrencyType.Coins).ChancePercent, Is.EqualTo(10));
            Assert.That(odds.Sum(o => o.ChancePercent), Is.EqualTo(100));
        }

        /// <summary>
        /// The expected value of one spin on the baseline table, pinned exactly rather than within a range,
        /// because the first-week economy pacing was checked against it.
        /// </summary>
        [Test]
        public void TheBaselineSpinIsWorthTwoHundredAndSixtyCoinsAndThreeGems()
        {
            WheelTableInfo table = Baseline();

            Assert.That(table.ExpectedHundredthsOf(CurrencyType.Coins), Is.EqualTo(26_000));
            Assert.That(table.ExpectedHundredthsOf(CurrencyType.Gems), Is.EqualTo(300));
            Assert.That(table.ExpectedHundredthsOf(CurrencyType.SpinTokens), Is.EqualTo(20));
        }

        [Test]
        public void EveryNonBlankSectorPaysExactlyOneCurrencyAndExactlyOneSectorIsBlank()
        {
            foreach (WheelSectorInfo sector in Baseline().Sectors.Where(sector => !sector.IsBlank))
            {
                Assert.That(sector.Reward.Amounts.Count, Is.EqualTo(1), $"sector {sector.Sector} pays more than one currency");
                Assert.That(sector.Amount, Is.GreaterThan(0), $"sector {sector.Sector} pays nothing");
                Assert.That(sector.Currency, Is.Not.EqualTo(CurrencyType.None), $"sector {sector.Sector} names no currency");
            }

            // The blank is the only sector that grants nothing. Its reward is an empty bundle, because a null
            // reward is rejected.
            WheelSectorInfo blank = Baseline().Sectors.Single(sector => sector.IsBlank);
            Assert.That(blank.Reward.Amounts, Is.Empty, "the blank grants something");
            Assert.That(blank.Currency, Is.EqualTo(CurrencyType.None));
            Assert.That(blank.Amount, Is.Zero);
        }

        [Test]
        public void ASectorCanBeFoundByItsStableIdAndByItsPosition()
        {
            WheelTableInfo table = Baseline();

            int index = table.IndexOf(table.Sectors[5].Id);

            Assert.That(index, Is.EqualTo(5));
            Assert.That(table.SectorAt(index).Amount, Is.EqualTo(1000));
            Assert.That(table.SectorAt(-1), Is.Null);
            Assert.That(table.SectorAt(WheelTableInfo.NumSectors), Is.Null);
            Assert.That(table.IndexOf(WheelSectorId.FromString("nothing")), Is.EqualTo(-1));
        }
    }

    /// <summary>
    /// Tests for <see cref="SpinWheelPolicy.DrawSectorIndex"/>: the draw is uniform and reproducible from a seed.
    /// <para>
    /// In production the player actor seeds the generator from real entropy, never from client input. These
    /// tests seed it directly, and check uniformity over many draws instead of asserting on a single draw.
    /// </para>
    /// </summary>
    [TestFixture]
    public class WheelDrawTests
    {
        [Test]
        public void TheSameSeedDrawsTheSameSectors()
        {
            WheelTableInfo table = TestGameConfig.WheelTable();

            int[] first  = Draw(RandomPCG.CreateFromSeed(4242UL), table, 20);
            int[] second = Draw(RandomPCG.CreateFromSeed(4242UL), table, 20);

            Assert.That(second, Is.EqualTo(first), "a seeded draw is not reproducible, so no test of a spin can be");
            Assert.That(first.Distinct().Count(), Is.GreaterThan(1), "twenty draws landed on one sector, which is not a draw");
        }

        [Test]
        public void EverySectorIsReachable()
        {
            WheelTableInfo table = TestGameConfig.WheelTable();

            HashSet<int> seen = new HashSet<int>(Draw(RandomPCG.CreateFromSeed(7UL), table, 1_000));

            Assert.That(seen.Count, Is.EqualTo(WheelTableInfo.NumSectors), "a sector on the wheel can never come up");
            Assert.That(seen.Min(), Is.Zero);
            Assert.That(seen.Max(), Is.EqualTo(WheelTableInfo.NumSectors - 1));
        }

        /// <summary>
        /// Draw many times and compare each sector's count with the expected count, within a wide tolerance.
        /// <para>
        /// The seed is fixed and the tolerance is wide on purpose. The test targets a draw that is clearly not
        /// uniform, such as an off-by-one that never reaches the last sector or a modulo that favours the first
        /// sectors. A tight tolerance with a random seed would fail at random.
        /// </para>
        /// </summary>
        [Test]
        public void OverALongRunTheSectorsComeUpAboutEqually()
        {
            const int spins = 100_000;

            WheelTableInfo table  = TestGameConfig.WheelTable();
            int[]          counts = new int[WheelTableInfo.NumSectors];
            foreach (int index in Draw(RandomPCG.CreateFromSeed(20260903UL), table, spins))
                counts[index]++;

            int expected  = spins / WheelTableInfo.NumSectors;
            int tolerance = expected / 5;

            for (int index = 0; index < counts.Length; index++)
            {
                Assert.That(counts[index], Is.EqualTo(expected).Within(tolerance),
                    $"sector {index + 1} came up {counts[index]} times in {spins}, against an expected {expected}");
            }

            // Check one published odds entry the same way: the first sector's share of spins.
            int hundredCoins = counts[0];
            Assert.That(hundredCoins, Is.EqualTo(spins * 10 / 100).Within(spins * 10 / 100 / 5));
        }

        [Test]
        public void AnIncompleteTableIsRefusedRatherThanDrawnFrom()
        {
            WheelTableInfo shortTable = new WheelTableInfo(TestGameConfig.Wheel, TestGameConfig.WheelTable().Sectors.Take(3).ToList());

            Assert.That(SpinWheelPolicy.DrawSectorIndex(RandomPCG.CreateFromSeed(1UL), shortTable), Is.EqualTo(-1));
            Assert.That(SpinWheelPolicy.DrawSectorIndex(RandomPCG.CreateFromSeed(1UL), null), Is.EqualTo(-1));
        }

        static int[] Draw(RandomPCG rng, WheelTableInfo table, int count)
        {
            int[] drawn = new int[count];
            for (int index = 0; index < count; index++)
                drawn[index] = SpinWheelPolicy.DrawSectorIndex(rng, table);
            return drawn;
        }
    }

    /// <summary>
    /// Tests for <see cref="PlayerWheelSpinResolved"/>: what it spends, grants, stores and logs, and what it
    /// refuses to do a second time.
    /// <para>
    /// Idempotence is tested by <b>executing</b> actions: the same action twice, a second action after the
    /// first, and a model serialized and restored in between, because those are the ways a replay arrives.
    /// </para>
    /// </summary>
    [TestFixture]
    public class SpinWheelActionTests
    {
        internal static readonly MetaTime Noon = MetaTime.FromDateTime(new DateTime(2026, 3, 10, 12, 0, 0, DateTimeKind.Utc));

        /// <summary>Sector indices in the baseline table, named by what they pay.</summary>
        const int HundredCoinsSector  = 0;
        const int ThirtyGemsSector    = 3;
        const int ThousandCoinsSector = 5;
        const int OneTokenSector      = 7;
        const int NothingSector       = 8;

        static PlayerModel NewPlayer(List<PlayerEventBase> captured, SharedGameConfig config = null) => TestPlayers.New(Noon, config, captured);

        /// <summary>The fixture config with the starting wallet's spin tokens set to <paramref name="spinTokens"/>.</summary>
        static SharedGameConfig ConfigWithSpinTokens(int spinTokens) =>
            TestGameConfig.Build(startingWallet: new RewardBundle(CurrencyAmount.Coins(3_000), CurrencyAmount.Gems(100), CurrencyAmount.SpinTokens(spinTokens)));

        static PlayerModel Restore(PlayerModel player, List<PlayerEventBase> captured)
        {
            PlayerModel restored = MetaSerialization.DeserializeTagged<PlayerModel>(
                TestPlayers.Snapshot(player), MetaSerializationFlags.IncludeAll, resolver: null, logicVersion: null);

            restored.SetGameConfig(player.GameConfig);
            restored.AnalyticsEventHandler = new AnalyticsEventHandler<IPlayerModelBase, PlayerEventBase>((context, payload) => captured.Add(payload));
            return restored;
        }

        /// <summary>
        /// Run one spin the way the SDK runs an action: a dry run, then a commit. Asserts the two passes return
        /// the same result.
        /// </summary>
        static MetaActionResult Spin(PlayerModel player, int sectorIndex, MetaTime at, int? drawnFor = null)
        {
            // The server draws for the player's next spin ordinal. A test about a draw made for another spin,
            // such as a held-back or replayed draw, passes that ordinal in drawnFor.
            int ordinal = drawnFor ?? player.SpinWheel.NextOrdinal;

            return TestPlayers.DryRunThenCommit(player, new PlayerWheelSpinResolved(ordinal, sectorIndex, at));
        }

        static MetaActionResult Acknowledge(PlayerModel player) => TestPlayers.DryRunThenCommit(player, new PlayerAcknowledgeWheelSpin());

        static IEnumerable<PlayerEventWheelSpinResolved> Spins(IEnumerable<PlayerEventBase> events) =>
            events.OfType<PlayerEventWheelSpinResolved>();

        static IEnumerable<PlayerEventEconomyTransaction> Transactions(IEnumerable<PlayerEventBase> events) =>
            events.OfType<PlayerEventEconomyTransaction>();

        [Test]
        public void AFreshPlayerSpendsTheirStartingTokenAndTheCoinsLand()
        {
            List<PlayerEventBase> captured = new List<PlayerEventBase>();
            PlayerModel           player   = NewPlayer(captured);

            Assert.That(player.Wallet.SpinTokens, Is.EqualTo(1), "a fresh player starts with one token, so the wheel is self-serve on day one");

            Assert.That(Spin(player, ThousandCoinsSector, Noon), Is.EqualTo(MetaActionResult.Success));

            Assert.That(player.Wallet.Coins, Is.EqualTo(4_000), "the starting 3,000 plus the 1,000-coin sector");
            Assert.That(player.Wallet.SpinTokens, Is.Zero, "the token was not spent");
            Assert.That(player.SpinWheel.TotalResolvedSpins, Is.EqualTo(1));
            Assert.That(player.SpinWheel.LastResolvedSpin.SectorIndex, Is.EqualTo(ThousandCoinsSector));
            Assert.That(player.SpinWheel.LastResolvedSpin.Table, Is.EqualTo(TestGameConfig.Wheel));
            Assert.That(player.SpinWheel.LastResolvedSpin.ResolvedAt, Is.EqualTo(Noon));
        }

        /// <summary>
        /// The dry run must leave the model byte-identical and write no analytics events. A dry run that changed
        /// any member would cause a desync.
        /// </summary>
        [Test]
        public void ADryRunMovesNothingAndEmitsNothing()
        {
            List<PlayerEventBase> captured = new List<PlayerEventBase>();
            PlayerModel           player   = NewPlayer(captured);

            byte[] before = TestPlayers.Snapshot(player);
            captured.Clear();

            Assert.That(new PlayerWheelSpinResolved(1, HundredCoinsSector, Noon).Execute(player, commit: false), Is.EqualTo(MetaActionResult.Success));

            Assert.That(TestPlayers.Snapshot(player), Is.EqualTo(before), "a dry run moved player state");
            Assert.That(captured, Is.Empty, "a dry run wrote an event, so every replay would double-count it");

            Assert.That(new PlayerWheelSpinResolved(1, HundredCoinsSector, Noon).Execute(player, commit: true), Is.EqualTo(MetaActionResult.Success));
            Assert.That(TestPlayers.Snapshot(player), Is.Not.EqualTo(before));
        }

        /// <summary>
        /// One spin writes a token sink row and a prize source row with the balances they changed, both under the
        /// spin event's correlation id. The wallet settlement writes these rows, not the wheel code.
        /// </summary>
        [Test]
        public void OneSpinWritesOneSinkOneSourceAndOneResolvedEventUnderOneKey()
        {
            List<PlayerEventBase> captured = new List<PlayerEventBase>();
            PlayerModel           player   = NewPlayer(captured);
            captured.Clear();

            Spin(player, ThirtyGemsSector, Noon);

            PlayerEventWheelSpinResolved[]  resolved = Spins(captured).ToArray();
            PlayerEventEconomyTransaction[] rows     = Transactions(captured).ToArray();

            Assert.That(resolved.Length, Is.EqualTo(1));
            Assert.That(resolved[0].Sector, Is.EqualTo(ThirtyGemsSector + 1), "the event carries the drawn position, one-based");
            Assert.That(resolved[0].RewardCurrency, Is.EqualTo(CurrencyType.Gems));
            Assert.That(resolved[0].RewardAmount, Is.EqualTo(30));
            Assert.That(resolved[0].Tier, Is.EqualTo(WheelPrizeTier.Premium));
            Assert.That(resolved[0].SpinOrdinal, Is.EqualTo(1));
            Assert.That(resolved[0].SpinTokensAfter, Is.Zero);

            Assert.That(rows.Length, Is.EqualTo(2), "a spin is a token out and a prize in, never one netted row");
            Assert.That(rows[0].Flow, Is.EqualTo(CurrencyFlow.Sink));
            Assert.That(rows[0].Currency, Is.EqualTo(CurrencyType.SpinTokens));
            Assert.That(rows[0].Reason, Is.EqualTo(EconomyReason.WheelSpinCost),
                "the sink is the price of the spin, and an operator filtering the taxonomy for it finds nothing if it is labelled as the prize");
            Assert.That(rows[1].Flow, Is.EqualTo(CurrencyFlow.Source));
            Assert.That(rows[1].Reason, Is.EqualTo(EconomyReason.WheelPrize));
            Assert.That(rows[1].Currency, Is.EqualTo(CurrencyType.Gems));
            Assert.That(rows[1].BalanceBefore, Is.EqualTo(100));
            Assert.That(rows[1].BalanceAfter, Is.EqualTo(130));

            Assert.That(rows.Select(row => row.Correlation).Distinct().Single(), Is.EqualTo(resolved[0].Correlation),
                "the spin and the balances it moved cannot be put back together");
            Assert.That(player.SpinWheel.LastResolvedSpin.Correlation, Is.EqualTo(resolved[0].Correlation),
                "the receipt cannot be joined to the settlement that produced it");
        }

        /// <summary>
        /// A spin-again result writes a sink row and a source row that cancel out, and the balance ends where it
        /// began. Combining them into one row would hide the spin from the economy log (<c>docs/economy.md</c>).
        /// </summary>
        [Test]
        public void AReplacementTokenEmitsBothRowsAndLeavesTheBalanceWhereItWas()
        {
            List<PlayerEventBase> captured = new List<PlayerEventBase>();
            PlayerModel           player   = NewPlayer(captured);
            captured.Clear();

            Spin(player, OneTokenSector, Noon);

            Assert.That(player.Wallet.SpinTokens, Is.EqualTo(1), "the balance should be back where it started");

            PlayerEventEconomyTransaction[] rows = Transactions(captured).ToArray();
            Assert.That(rows.Select(row => row.Flow), Is.EqualTo(new[] { CurrencyFlow.Sink, CurrencyFlow.Source }));
            Assert.That(rows.Select(row => row.Currency), Is.EqualTo(new[] { CurrencyType.SpinTokens, CurrencyType.SpinTokens }));
            Assert.That(rows[0].BalanceBefore, Is.EqualTo(1));
            Assert.That(rows[0].BalanceAfter, Is.Zero);
            Assert.That(rows[1].BalanceBefore, Is.Zero);
            Assert.That(rows[1].BalanceAfter, Is.EqualTo(1));

            Assert.That(Spins(captured).Single().SpinTokensAfter, Is.EqualTo(1), "spin again has to be offerable from the event alone");
        }

        /// <summary>
        /// A blank result spends the token and grants nothing. It writes one sink row and no source row, an empty
        /// receipt, and a spin event with no reward currency. The wallet settles it as a plain spend, so the
        /// blank needs no special case.
        /// </summary>
        [Test]
        public void ABlankSectorSpendsTheTokenAndPaysNothing()
        {
            List<PlayerEventBase> captured = new List<PlayerEventBase>();
            PlayerModel           player   = NewPlayer(captured);
            captured.Clear();

            Assert.That(Spin(player, NothingSector, Noon), Is.EqualTo(MetaActionResult.Success));

            Assert.That(player.Wallet.SpinTokens, Is.Zero, "the blank pays the token back");
            Assert.That(player.Wallet.Coins, Is.EqualTo(3_000), "the blank paid coins");
            Assert.That(player.Wallet.Gems, Is.EqualTo(100), "the blank paid gems");

            PlayerEventWheelSpinResolved[]  resolved = Spins(captured).ToArray();
            PlayerEventEconomyTransaction[] rows     = Transactions(captured).ToArray();

            Assert.That(resolved.Length, Is.EqualTo(1));
            Assert.That(resolved[0].Tier, Is.EqualTo(WheelPrizeTier.Nothing));
            Assert.That(resolved[0].RewardCurrency, Is.EqualTo(CurrencyType.None), "the blank's event names a reward currency");
            Assert.That(resolved[0].RewardAmount, Is.Zero);
            Assert.That(resolved[0].SpinTokensAfter, Is.Zero);

            Assert.That(rows.Select(row => row.Flow), Is.EqualTo(new[] { CurrencyFlow.Sink }),
                "the blank settled as more than the token sink alone");
            Assert.That(rows[0].Currency, Is.EqualTo(CurrencyType.SpinTokens));
            Assert.That(rows[0].Reason, Is.EqualTo(EconomyReason.WheelSpinCost));

            SpinReceipt receipt = player.SpinWheel.PendingReceipt;
            Assert.That(receipt.SectorIndex, Is.EqualTo(NothingSector));
            Assert.That(receipt.Reward.Amounts, Is.Empty, "the receipt invented a reward");

            // The blank result is acknowledged like any other result.
            Assert.That(Acknowledge(player), Is.EqualTo(MetaActionResult.Success));
            Assert.That(player.SpinWheel.HasPendingReceipt, Is.False);
        }

        // ---- The interrupted spin ----

        [Test]
        public void AResolvedResultIsPendingUntilItIsAcknowledged()
        {
            List<PlayerEventBase> captured = new List<PlayerEventBase>();
            PlayerModel           player   = NewPlayer(captured);

            Assert.That(player.SpinWheel.HasPendingReceipt, Is.False, "a player who has never spun has nothing waiting");

            Spin(player, HundredCoinsSector, Noon);

            Assert.That(player.SpinWheel.HasPendingReceipt, Is.True);
            Assert.That(player.SpinWheel.PendingReceipt.Ordinal, Is.EqualTo(1));
            Assert.That(player.SpinWheel.LastAcknowledgedOrdinal, Is.Zero);

            Assert.That(Acknowledge(player), Is.EqualTo(MetaActionResult.Success));

            Assert.That(player.SpinWheel.HasPendingReceipt, Is.False);
            Assert.That(player.SpinWheel.PendingReceipt, Is.Null);
            Assert.That(player.SpinWheel.LastAcknowledgedOrdinal, Is.EqualTo(1));
        }

        /// <summary>
        /// Acknowledging only advances <c>LastAcknowledgedOrdinal</c>. It must not change a balance, write a receipt
        /// or log an event, because a second event would count every spin twice in analytics.
        /// </summary>
        [Test]
        public void AcknowledgingTouchesNoBalanceAndEmitsNothing()
        {
            List<PlayerEventBase> captured = new List<PlayerEventBase>();
            PlayerModel           player   = NewPlayer(captured);

            Spin(player, ThousandCoinsSector, Noon);

            PlayerWalletModel before = player.Wallet;
            SpinReceipt       receipt = player.SpinWheel.LastResolvedSpin;
            captured.Clear();

            Acknowledge(player);

            Assert.That(captured, Is.Empty, "acknowledging wrote an event");
            Assert.That(player.Wallet.Coins, Is.EqualTo(before.Coins));
            Assert.That(player.Wallet.Gems, Is.EqualTo(before.Gems));
            Assert.That(player.Wallet.SpinTokens, Is.EqualTo(before.SpinTokens));
            Assert.That(player.SpinWheel.LastResolvedSpin, Is.SameAs(receipt), "the receipt itself changed");
            Assert.That(player.SpinWheel.TotalResolvedSpins, Is.EqualTo(1));
        }

        /// <summary>A second acknowledgement is refused and changes nothing.</summary>
        [Test]
        public void AcknowledgingTwiceIsRefused()
        {
            List<PlayerEventBase> captured = new List<PlayerEventBase>();
            PlayerModel           player   = NewPlayer(captured);

            Spin(player, HundredCoinsSector, Noon);
            Acknowledge(player);

            Assert.That(Acknowledge(player), Is.EqualTo(ActionResults.WheelNothingToAcknowledge));
            Assert.That(player.SpinWheel.LastAcknowledgedOrdinal, Is.EqualTo(1));
        }

        [Test]
        public void AcknowledgingWithNothingPendingIsRefused()
        {
            PlayerModel player = NewPlayer(new List<PlayerEventBase>());

            Assert.That(Acknowledge(player), Is.EqualTo(ActionResults.WheelNothingToAcknowledge));
            Assert.That(player.SpinWheel.LastAcknowledgedOrdinal, Is.Zero);
        }

        /// <summary>
        /// The same action delivered twice. The second finds an unacknowledged result and is refused before the
        /// wallet is changed.
        /// </summary>
        [Test]
        public void ASecondDeliveryOfTheSameSpinSettlesNothing()
        {
            List<PlayerEventBase> captured = new List<PlayerEventBase>();
            PlayerModel           player   = NewPlayer(captured, ConfigWithSpinTokens(5));

            PlayerWheelSpinResolved action = new PlayerWheelSpinResolved(1, ThousandCoinsSector, Noon);
            Assert.That(action.Execute(player, commit: true), Is.EqualTo(MetaActionResult.Success));
            captured.Clear();

            Assert.That(action.Execute(player, commit: true), Is.EqualTo(ActionResults.WheelResultPending));

            Assert.That(player.Wallet.Coins, Is.EqualTo(4_000), "the prize was paid twice");
            Assert.That(player.Wallet.SpinTokens, Is.EqualTo(4), "a second token was spent");
            Assert.That(player.SpinWheel.TotalResolvedSpins, Is.EqualTo(1));
            Assert.That(captured, Is.Empty, "a refused replay wrote to the log");
        }

        /// <summary>A double tap: a second spin arrives before the first result is acknowledged.</summary>
        [Test]
        public void ASecondSpinBeforeTheFirstIsSeenIsRefused()
        {
            List<PlayerEventBase> captured = new List<PlayerEventBase>();
            PlayerModel           player   = NewPlayer(captured, ConfigWithSpinTokens(5));

            Assert.That(Spin(player, HundredCoinsSector, Noon), Is.EqualTo(MetaActionResult.Success));
            Assert.That(Spin(player, ThousandCoinsSector, Noon + MetaDuration.FromSeconds(1)), Is.EqualTo(ActionResults.WheelResultPending));

            Assert.That(player.SpinWheel.LastResolvedSpin.SectorIndex, Is.EqualTo(HundredCoinsSector),
                "an unseen result was overwritten by the spin behind it");
            Assert.That(player.Wallet.SpinTokens, Is.EqualTo(4));
        }

        // ---- Choosing a prize, which is the one thing the wheel must not let a client do ----

        /// <summary>
        /// <b>The spin ordinal on a draw prevents this cheat.</b> A synchronized server action executes when the
        /// <i>client</i> puts it on its timeline, so a modified client can hold a draw back and read its sector
        /// first. Without the ordinal, the client could keep the jackpot draw until after acknowledging a worse
        /// result, and the held draw would then pay on the next spin.
        /// <para>
        /// Here two draws were made for spin 1: the blank and the thousand coins. The blank executes and is
        /// acknowledged, and the held-back draw is refused because its spin is over.
        /// </para>
        /// </summary>
        [Test]
        public void ADrawHeldBackCannotBeCashedOnceItsSpinIsOver()
        {
            List<PlayerEventBase> captured = new List<PlayerEventBase>();
            PlayerModel           player   = NewPlayer(captured, ConfigWithSpinTokens(5));

            Assert.That(Spin(player, NothingSector, Noon, drawnFor: 1), Is.EqualTo(MetaActionResult.Success));
            Assert.That(Acknowledge(player), Is.EqualTo(MetaActionResult.Success));

            captured.Clear();

            Assert.That(Spin(player, ThousandCoinsSector, Noon + MetaDuration.FromSeconds(11), drawnFor: 1),
                Is.EqualTo(ActionResults.WheelStaleOrdinal));

            Assert.That(player.Wallet.Coins, Is.EqualTo(3_000), "a draw made for a finished spin paid its prize");
            Assert.That(player.Wallet.SpinTokens, Is.EqualTo(4), "a draw made for a finished spin took a second token");
            Assert.That(player.SpinWheel.TotalResolvedSpins, Is.EqualTo(1));
            Assert.That(player.SpinWheel.LastResolvedSpin.SectorIndex, Is.EqualTo(NothingSector),
                "the spin the player actually paid for was replaced by the one they held back");
            Assert.That(captured, Is.Empty, "a refused draw wrote to the log");
        }

        /// <summary>
        /// A draw for a spin the player has not reached yet is also refused, so a draw is bound to exactly one
        /// spin and not to any later spin.
        /// </summary>
        [Test]
        public void ADrawMadeForASpinThePlayerIsNotOnYetSettlesNothing()
        {
            List<PlayerEventBase> captured = new List<PlayerEventBase>();
            PlayerModel           player   = NewPlayer(captured, ConfigWithSpinTokens(5));

            captured.Clear();

            Assert.That(Spin(player, ThousandCoinsSector, Noon, drawnFor: 2), Is.EqualTo(ActionResults.WheelStaleOrdinal));

            Assert.That(player.Wallet.Coins, Is.EqualTo(3_000));
            Assert.That(player.Wallet.SpinTokens, Is.EqualTo(5), "the token was spent on a draw for a spin that has not happened");
            Assert.That(player.SpinWheel.TotalResolvedSpins, Is.Zero);
            Assert.That(player.SpinWheel.LastResolvedSpin, Is.Null);
            Assert.That(captured, Is.Empty);
        }

        /// <summary>
        /// After a reconnect, the model has been serialized and restored, and the pending receipt must still be
        /// there. This is how an interrupted spin is recovered.
        /// </summary>
        [Test]
        public void APendingReceiptSurvivesASerializationRoundTrip()
        {
            List<PlayerEventBase> captured = new List<PlayerEventBase>();
            PlayerModel           player   = NewPlayer(captured);

            Spin(player, ThirtyGemsSector, Noon);

            PlayerModel restored = Restore(player, captured);

            Assert.That(restored.SpinWheel.HasPendingReceipt, Is.True);
            Assert.That(restored.SpinWheel.PendingReceipt.SectorIndex, Is.EqualTo(ThirtyGemsSector));
            Assert.That(restored.SpinWheel.PendingReceipt.Reward.AmountOf(CurrencyType.Gems), Is.EqualTo(30));
            Assert.That(restored.SpinWheel.PendingReceipt.Table, Is.EqualTo(TestGameConfig.Wheel));
            Assert.That(restored.Wallet.Gems, Is.EqualTo(130), "the reward was already paid; the reveal was the only thing owed");

            // Another spin is refused on the restored model until the pending receipt is acknowledged.
            Assert.That(Spin(restored, HundredCoinsSector, Noon), Is.EqualTo(ActionResults.WheelResultPending));

            Assert.That(Acknowledge(restored), Is.EqualTo(MetaActionResult.Success));
            Assert.That(restored.SpinWheel.HasPendingReceipt, Is.False);
        }

        /// <summary>
        /// An acknowledged result is not shown again after a reconnect. The receipt is kept as the last result
        /// for operators, but it is no longer pending.
        /// </summary>
        [Test]
        public void AnAcknowledgedResultIsNotPresentedAgainAfterAReconnect()
        {
            List<PlayerEventBase> captured = new List<PlayerEventBase>();
            PlayerModel           player   = NewPlayer(captured);

            Spin(player, HundredCoinsSector, Noon);
            Acknowledge(player);

            PlayerModel restored = Restore(player, captured);

            Assert.That(restored.SpinWheel.HasPendingReceipt, Is.False);
            Assert.That(restored.SpinWheel.LastResolvedSpin, Is.Not.Null, "the last result is still readable for support");
            Assert.That(restored.SpinWheel.LastAcknowledgedOrdinal, Is.EqualTo(1));
        }

        /// <summary>Spin, acknowledge and spin again: the ordinal increases and each spin pays.</summary>
        [Test]
        public void SpinAgainAdvancesTheOrdinalAndPaysAgain()
        {
            List<PlayerEventBase> captured = new List<PlayerEventBase>();
            PlayerModel           player   = NewPlayer(captured, ConfigWithSpinTokens(3));

            Spin(player, HundredCoinsSector, Noon);
            Acknowledge(player);
            Spin(player, ThousandCoinsSector, Noon + MetaDuration.FromSeconds(5));

            Assert.That(player.SpinWheel.TotalResolvedSpins, Is.EqualTo(2));
            Assert.That(player.SpinWheel.LastResolvedSpin.Ordinal, Is.EqualTo(2));
            Assert.That(player.SpinWheel.LastAcknowledgedOrdinal, Is.EqualTo(1));
            Assert.That(player.Wallet.Coins, Is.EqualTo(4_100));
            Assert.That(player.Wallet.SpinTokens, Is.EqualTo(1));

            Assert.That(Spins(captured).Select(e => e.SpinOrdinal), Is.EqualTo(new[] { 1, 2 }));
        }

        // ---- Refusals, none of which spend anything ----

        [Test]
        public void APlayerWithNoTokenCannotSpinAndPaysNothing()
        {
            List<PlayerEventBase> captured = new List<PlayerEventBase>();
            PlayerModel           player   = NewPlayer(captured, TestGameConfig.Build(
                startingWallet: new RewardBundle(CurrencyAmount.Coins(3_000), CurrencyAmount.Gems(100))));

            captured.Clear();

            Assert.That(Spin(player, ThousandCoinsSector, Noon), Is.EqualTo(ActionResults.InsufficientFunds));

            Assert.That(player.Wallet.Coins, Is.EqualTo(3_000));
            Assert.That(player.SpinWheel.TotalResolvedSpins, Is.Zero);
            Assert.That(player.SpinWheel.LastResolvedSpin, Is.Null);
            Assert.That(Spins(captured), Is.Empty);
        }

        /// <summary>
        /// The wallet caps are checked against <b>every sector</b> before drawing. If any sector could not be
        /// paid, the spin is refused and the player keeps the token, so no draw ever needs a reroll.
        /// </summary>
        [Test]
        public void ATableWithAnyUnpayableSectorRefusesTheSpinAndKeepsTheToken()
        {
            List<PlayerEventBase> captured = new List<PlayerEventBase>();

            // The gem balance is at the cap, so only the gem sector cannot be paid.
            PlayerModel player = NewPlayer(captured, TestGameConfig.Build(
                startingWallet: new RewardBundle(
                    CurrencyAmount.Coins(3_000),
                    CurrencyAmount.Gems(TestGameConfig.MaxGems),
                    CurrencyAmount.SpinTokens(1))));

            captured.Clear();

            Assert.That(Spin(player, HundredCoinsSector, Noon), Is.EqualTo(ActionResults.WheelWalletFull),
                "a coin sector was paid while a gem sector on the same wheel could not be");

            Assert.That(player.Wallet.SpinTokens, Is.EqualTo(1), "the token was spent on a spin that never happened");
            Assert.That(player.Wallet.Coins, Is.EqualTo(3_000));
            Assert.That(captured, Is.Empty);
        }

        [Test]
        public void AMissingTableRefusesTheSpinRatherThanGuessingAtIt()
        {
            SharedGameConfig config = TestGameConfig.Build();
            TestGameConfig.SetEntry(config, "WheelTables",
                Metaplay.Core.Config.GameConfigLibrary<WheelTableId, WheelTableInfo>.CreateEmpty());

            List<PlayerEventBase> captured = new List<PlayerEventBase>();
            PlayerModel           player   = NewPlayer(captured, config);
            captured.Clear();

            Assert.That(Spin(player, HundredCoinsSector, Noon), Is.EqualTo(ActionResults.WheelUnavailable));
            Assert.That(player.Wallet.SpinTokens, Is.EqualTo(1));
            Assert.That(captured, Is.Empty);
        }

        [Test]
        public void ASectorOutsideTheTableIsRefused()
        {
            List<PlayerEventBase> captured = new List<PlayerEventBase>();
            PlayerModel           player   = NewPlayer(captured);

            Assert.That(Spin(player, WheelTableInfo.NumSectors, Noon), Is.EqualTo(ActionResults.WheelUnavailable));
            Assert.That(Spin(player, -1, Noon), Is.EqualTo(ActionResults.WheelUnavailable));
            Assert.That(player.Wallet.SpinTokens, Is.EqualTo(1));
        }

        /// <summary>
        /// The receipt stores its own table id and reward, so a config published between the spin and the reveal
        /// does not change what the player is shown.
        /// </summary>
        [Test]
        public void AReceiptKeepsWhatItPaidThroughALaterPublish()
        {
            List<PlayerEventBase> captured = new List<PlayerEventBase>();
            PlayerModel           player   = NewPlayer(captured);

            Spin(player, ThousandCoinsSector, Noon);
            SpinReceipt receipt = player.SpinWheel.LastResolvedSpin;

            // Publish a config with different sectors in the wheel table.
            List<WheelSectorInfo> retuned = new List<WheelSectorInfo>();
            for (int position = 1; position <= WheelTableInfo.NumSectors; position++)
                retuned.Add(TestGameConfig.Sector(position, CurrencyAmount.Coins(300), WheelPrizeTier.Common));

            player.SetGameConfig(TestGameConfig.Build(wheel: new WheelTableInfo(TestGameConfig.Wheel, retuned)));

            Assert.That(player.SpinWheel.LastResolvedSpin, Is.SameAs(receipt));
            Assert.That(player.SpinWheel.PendingReceipt.Reward.AmountOf(CurrencyType.Coins), Is.EqualTo(1_000),
                "the receipt started reading the new table instead of what it paid");
        }
    }

    /// <summary>
    /// Tests for <see cref="SpinWheelPolicy.OutlookFor"/>. The screen and the server both use it, so an enabled Spin
    /// button always matches what the server accepts.
    /// </summary>
    [TestFixture]
    public class SpinWheelOutlookTests
    {
        static readonly MetaTime Noon = SpinWheelActionTests.Noon;

        static PlayerModel Player(RewardBundle wallet = null) => TestPlayers.New(Noon, TestGameConfig.Build(startingWallet: wallet));

        static SpinWheelOutlook OutlookFor(PlayerModel player, int? expectedOrdinal = null) =>
            SpinWheelPolicy.OutlookFor(player.SpinWheel, player.Wallet, player.GameConfig, expectedOrdinal);

        [Test]
        public void AFreshPlayerHasExactlyOneSpinAndNothingWaiting()
        {
            SpinWheelOutlook outlook = OutlookFor(Player());

            Assert.That(outlook.CanSpin, Is.True);
            Assert.That(outlook.SpinsAvailable, Is.EqualTo(1));
            Assert.That(outlook.NextOrdinal, Is.EqualTo(1));
            Assert.That(outlook.HasPendingReceipt, Is.False);
            Assert.That(outlook.Table.Id, Is.EqualTo(TestGameConfig.Wheel));
        }

        [Test]
        public void TheTokenBalanceIsTheWholeOfAvailability()
        {
            Assert.That(OutlookFor(Player(new RewardBundle(CurrencyAmount.Coins(10), CurrencyAmount.SpinTokens(4)))).SpinsAvailable, Is.EqualTo(4));
            Assert.That(OutlookFor(Player(new RewardBundle(CurrencyAmount.Coins(10)))).SpinsAvailable, Is.Zero);
            Assert.That(OutlookFor(Player(new RewardBundle(CurrencyAmount.Coins(10)))).Refusal, Is.EqualTo(SpinRefusal.NoTokens));
        }

        /// <summary>
        /// A request with an ordinal other than the player's next spin, such as a retransmission or a stale tab,
        /// is refused. The ordinal is checked <i>after</i> the pending receipt, so a replay of the spin that just
        /// resolved gets <see cref="SpinRefusal.ResultPending"/> rather than <see cref="SpinRefusal.StaleOrdinal"/>.
        /// </summary>
        [Test]
        public void AStaleOrdinalIsRefusedAndAPendingReceiptAnswersFirst()
        {
            PlayerModel player = Player(new RewardBundle(CurrencyAmount.Coins(10), CurrencyAmount.SpinTokens(3)));

            Assert.That(OutlookFor(player, expectedOrdinal: 1).CanSpin, Is.True);
            Assert.That(OutlookFor(player, expectedOrdinal: 2).Refusal, Is.EqualTo(SpinRefusal.StaleOrdinal));
            Assert.That(OutlookFor(player).CanSpin, Is.True, "a caller with no ordinal is asking about the wheel, not about a request");
            Assert.That(OutlookFor(player, expectedOrdinal: 0).Refusal, Is.EqualTo(SpinRefusal.StaleOrdinal),
                "zero is not an ordinal, so a request naming it must not skip the guard the ordinal exists to be");

            new PlayerWheelSpinResolved(1, 0, Noon).Execute(player, commit: true);

            Assert.That(OutlookFor(player, expectedOrdinal: 1).Refusal, Is.EqualTo(SpinRefusal.ResultPending));
            Assert.That(OutlookFor(player, expectedOrdinal: 99).Refusal, Is.EqualTo(SpinRefusal.ResultPending));
            Assert.That(OutlookFor(player).PendingReceipt.Ordinal, Is.EqualTo(1));
        }

        [Test]
        public void EveryPrizeMustFitBeforeAnythingIsDrawn()
        {
            WheelTableInfo table = TestGameConfig.WheelTable();
            WalletCaps     caps  = new WalletCaps(TestGameConfig.MaxCoins, TestGameConfig.MaxGems, TestGameConfig.MaxSpinTokens);

            PlayerModel roomy = Player();
            PlayerModel full  = Player(new RewardBundle(CurrencyAmount.Coins(TestGameConfig.MaxCoins), CurrencyAmount.SpinTokens(1)));

            Assert.That(SpinWheelPolicy.EveryPrizeFits(roomy.Wallet, caps, table), Is.True);
            Assert.That(SpinWheelPolicy.EveryPrizeFits(full.Wallet, caps, table), Is.False);
            Assert.That(SpinWheelPolicy.OverflowingCurrency(full.Wallet, caps, table), Is.EqualTo(CurrencyType.Coins),
                "the refusal cannot say which balance to spend down");
            Assert.That(SpinWheelPolicy.OverflowingCurrency(roomy.Wallet, caps, table), Is.EqualTo(CurrencyType.None));

            Assert.That(OutlookFor(full).Refusal, Is.EqualTo(SpinRefusal.WalletFull));
        }

        [Test]
        public void AWheelWithNoPublishedTableIsUnavailableRatherThanEmpty()
        {
            PlayerModel player = Player();
            SharedGameConfig config = TestGameConfig.Build();
            TestGameConfig.SetEntry(config, "WheelTables",
                Metaplay.Core.Config.GameConfigLibrary<WheelTableId, WheelTableInfo>.CreateEmpty());
            player.SetGameConfig(config);

            SpinWheelOutlook outlook = OutlookFor(player);

            Assert.That(outlook.Refusal, Is.EqualTo(SpinRefusal.ConfigUnavailable));
            Assert.That(outlook.Table, Is.Null);
        }

        /// <summary>
        /// A spin costs one token, and the spend and the grant are one wallet transaction tagged with the wheel's
        /// feature, reasons and content id.
        /// </summary>
        [Test]
        public void OneSpinCostsExactlyOneTokenAndBuysOneSector()
        {
            WheelTableInfo    table       = TestGameConfig.WheelTable();
            WalletTransaction transaction = SpinWheelPolicy.SpinTransaction(table, table.Sectors[5]);

            Assert.That(SpinWheelPolicy.SpinCost.Currency, Is.EqualTo(CurrencyType.SpinTokens));
            Assert.That(SpinWheelPolicy.SpinCost.Amount, Is.EqualTo(1));

            Assert.That(transaction.Feature, Is.EqualTo(EconomyFeature.SpinWheel));
            Assert.That(transaction.Reason, Is.EqualTo(EconomyReason.WheelPrize));
            Assert.That(transaction.SpendReason, Is.EqualTo(EconomyReason.WheelSpinCost),
                "the wheel's design asks for a wheel_spin_cost sink beside the wheel_prize source");
            Assert.That(transaction.ContentId, Is.EqualTo(EconomyContentId.FromString(TestGameConfig.Wheel.Value)));
            Assert.That(transaction.Spends.Single().Currency, Is.EqualTo(CurrencyType.SpinTokens));
            Assert.That(transaction.Spends.Single().Amount, Is.EqualTo(1));
            Assert.That(transaction.Grants.Single().Amount, Is.EqualTo(1_000));
            Assert.That(transaction.IsWellFormed, Is.True);
        }
    }
}
