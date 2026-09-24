using Metaplay.Core;
using Metaplay.Core.Analytics;
using Metaplay.Core.Config;
using Metaplay.Core.InAppPurchase;
using Metaplay.Core.Model;
using Metaplay.Core.Player;
using NUnit.Framework;
using System.Collections.Generic;

namespace Game.Logic.Tests
{
    /// <summary>
    /// Tests for the player segments and the player properties they read (<c>docs/offers.md</c>, "Player segments").
    /// <para>
    /// Membership is checked by evaluating the segment from the config, because a test that reimplemented the
    /// condition would still pass when the config says something else. <c>WalletTests</c> tests the balance
    /// properties.
    /// </para>
    /// </summary>
    [TestFixture]
    public class PlayerSegmentTests
    {
        static readonly MetaTime      CreatedAt = MetaTime.FromDateTime(new System.DateTime(2026, 9, 1, 12, 0, 0, System.DateTimeKind.Utc));
        static readonly InAppProductId Bundle = InAppProductId.FromString("gem_booster_pack");

        static DemoInAppProductInfo Product() =>
            new DemoInAppProductInfo(
                Bundle,
                "Gem Booster Pack",
                "Power up your game.",
                referencePriceUsd: 4.99,
                contents: new RewardBundle(CurrencyAmount.Coins(10), CurrencyAmount.Gems(10)));

        /// <summary>
        /// A player created at <see cref="CreatedAt"/>, starting with <paramref name="startingWallet"/> when given,
        /// with the demo in-app product in the config so <see cref="Purchase"/> can grant it.
        /// </summary>
        static PlayerModel NewPlayer(RewardBundle startingWallet = null)
        {
            SharedGameConfig config = TestGameConfig.Build(startingWallet);
            TestGameConfig.SetEntry(config, "InAppProducts",
                GameConfigLibrary<InAppProductId, DemoInAppProductInfo>.CreateSolo(new[] { Product() }));

            return TestPlayers.New(CreatedAt, config);
        }

        /// <summary>Move the model's clock to <paramref name="age"/> after the account was created.</summary>
        static void AgeTo(PlayerModel player, MetaDuration age) => player.ResetTime(CreatedAt + age);

        static void Purchase(PlayerModel player, string transactionId = "demo_txn_1") =>
            player.OnClaimedInAppProduct(
                DemoPurchaseReceipt.CreatePurchaseEvent(Product(), transactionId),
                player.GameConfig.InAppProducts[Bundle],
                out ResolvedPurchaseContentBase _);

        static void FinishGames(PlayerModel player, int played, int won = 0)
        {
            for (int index = 0; index < played; index++)
            {
                player.Record.Add(new MatchHistoryEntry(
                    EntityId.Create(EntityKindGame.Match, (ulong)index), CreatedAt, tricksWon: 1,
                    position: index < won ? 0 : 2, isWin: index < won, humanOpponents: 0, finishedByPlayer: true));
            }

            // The server copies the new counts to the segments with a PlayerTargetingFactsSynced action after
            // recording the games. Do the same here.
            new PlayerTargetingFactsSynced(PlayerTargetingFacts.Of(player)).Execute(player, commit: true);
        }

        static bool IsIn(PlayerModel player, PlayerSegmentId segmentId) =>
            player.GameConfig.PlayerSegments[segmentId].MatchesPlayer(player);

        #region Account age

        /// <summary>
        /// The age counts whole elapsed days and increases only when a full day has passed. This makes a range
        /// of 0 to 6 days cover exactly the first seven days. A creation time later than the current time gives
        /// an age of zero, not a negative number. Otherwise an imported account or a clock that moved backwards
        /// would fall outside every non-negative range instead of into the newest cohort.
        /// </summary>
        [TestCase(0,   0,  0)]
        [TestCase(1,   -1, 0)]
        [TestCase(1,   0,  1)]
        [TestCase(7,   -1, 6)]
        [TestCase(7,   0,  7)]
        [TestCase(-30, 0,  0)]
        public void TheAgeCountsWholeElapsedDaysAndIsNeverNegative(int days, int extraMilliseconds, int expectedAgeDays)
        {
            PlayerModel player = NewPlayer();

            AgeTo(player, MetaDuration.FromDays(days) + MetaDuration.FromMilliseconds(extraMilliseconds));

            Assert.That(new PlayerPropertyAccountAgeDays().GetTypedValueForPlayer(player), Is.EqualTo(expectedAgeDays));
        }

        #endregion

        #region Validated purchases

        [Test]
        public void ThePurchaseCountStartsAtZeroAndCountsValidatedGrants()
        {
            PlayerModel player = NewPlayer();

            Assert.That(new PlayerPropertyValidatedPurchases().GetTypedValueForPlayer(player), Is.EqualTo(0));

            Purchase(player);

            Assert.That(new PlayerPropertyValidatedPurchases().GetTypedValueForPlayer(player), Is.EqualTo(1));
        }

        /// <summary>
        /// Buying a once-only bundle again grants nothing and does not increase the count. The property counts
        /// granted purchases, not attempts.
        /// </summary>
        [Test]
        public void ARefusedRepeatDoesNotCount()
        {
            PlayerModel player = NewPlayer();

            Purchase(player);
            Purchase(player, "demo_txn_2");

            Assert.That(new PlayerPropertyValidatedPurchases().GetTypedValueForPlayer(player), Is.EqualTo(1));
        }

        #endregion

        #region The profile counters the segments read

        [Test]
        public void TheProfileCountersAreReadFromTheRecord()
        {
            PlayerModel player = NewPlayer();

            FinishGames(player, played: 4, won: 3);

            Assert.That(new PlayerPropertyGamesPlayed().GetTypedValueForPlayer(player), Is.EqualTo(4));
            Assert.That(new PlayerPropertyGamesWon().GetTypedValueForPlayer(player), Is.EqualTo(3));
        }

        /// <summary>
        /// The match record is <c>[NoChecksum]</c> and the client and server update it at different ticks. If a
        /// segment read the record directly, a client action could write checksummed offer state differently on
        /// the client and the server. Segments read the counts only after <c>PlayerTargetingFactsSynced</c>.
        /// </summary>
        [Test]
        public void ARecordedGameReachesSegmentsOnlyWhenSettled()
        {
            PlayerModel player = NewPlayer();
            FinishGames(player, played: 4);

            player.Record.Add(new MatchHistoryEntry(
                EntityId.Create(EntityKindGame.Match, 100), CreatedAt, tricksWon: 1,
                position: 0, isWin: true, humanOpponents: 0, finishedByPlayer: true));

            Assert.That(new PlayerPropertyGamesPlayed().GetTypedValueForPlayer(player), Is.EqualTo(4));
            Assert.That(new PlayerPropertyGamesWon().GetTypedValueForPlayer(player), Is.EqualTo(0));

            new PlayerTargetingFactsSynced(PlayerTargetingFacts.Of(player)).Execute(player, commit: true);

            Assert.That(new PlayerPropertyGamesPlayed().GetTypedValueForPlayer(player), Is.EqualTo(5));
            Assert.That(new PlayerPropertyGamesWon().GetTypedValueForPlayer(player), Is.EqualTo(1));
        }

        [Test]
        public void ARenameReachesSegmentsOnlyWhenSettled()
        {
            PlayerModel player = NewPlayer();

            player.ApplyRename("Velvet Ace", CreatedAt);

            Assert.That(new PlayerPropertyHasCustomizedName().GetTypedValueForPlayer(player), Is.False);

            new PlayerTargetingFactsSynced(PlayerTargetingFacts.Of(player)).Execute(player, commit: true);

            Assert.That(new PlayerPropertyHasCustomizedName().GetTypedValueForPlayer(player), Is.True);
        }

        /// <summary>
        /// The schema migration settles the targeting facts of an account saved at schema version 4, so the
        /// account still matches its segments.
        /// </summary>
        [Test]
        public void TheMigrationSettlesAnExistingAccountsFacts()
        {
            PlayerModel player = NewPlayer();
            for (int index = 0; index < TestGameConfig.EngagedMinMatchesPlayed; index++)
            {
                player.Record.Add(new MatchHistoryEntry(
                    EntityId.Create(EntityKindGame.Match, (ulong)index), CreatedAt, tricksWon: 1,
                    position: 2, isWin: false, humanOpponents: 0, finishedByPlayer: true));
            }
            AgeTo(player, MetaDuration.FromDays(30));

            SchemaMigrationRegistry.Instance.GetSchemaMigrator<PlayerModel>().RunMigrations(player, fromVersion: 4);

            Assert.That(IsIn(player, PlayerSegmentIds.EngagedNonPurchaser), Is.True);
        }

        #endregion

        #region First week, no purchase

        [Test]
        public void AFreshAccountIsInTheFirstWeekCohort()
        {
            Assert.That(IsIn(NewPlayer(), PlayerSegmentIds.FirstWeekNonPurchaser), Is.True);
        }

        [Test]
        public void TheFirstWeekCohortEndsAfterTheSeventhDay()
        {
            PlayerModel player = NewPlayer();

            AgeTo(player, MetaDuration.FromDays(TestGameConfig.FirstWeekSegmentMaxAccountAgeDays));
            Assert.That(IsIn(player, PlayerSegmentIds.FirstWeekNonPurchaser), Is.True, "the last day of the first week is inside the cohort");

            AgeTo(player, MetaDuration.FromDays(TestGameConfig.FirstWeekSegmentMaxAccountAgeDays + 1) - MetaDuration.FromMilliseconds(1));
            Assert.That(IsIn(player, PlayerSegmentIds.FirstWeekNonPurchaser), Is.True, "the cohort ended before the seventh day was over");

            AgeTo(player, MetaDuration.FromDays(TestGameConfig.FirstWeekSegmentMaxAccountAgeDays + 1));
            Assert.That(IsIn(player, PlayerSegmentIds.FirstWeekNonPurchaser), Is.False, "the cohort did not end when the first week did");
        }

        [Test]
        public void APurchaseLeavesTheFirstWeekCohortImmediately()
        {
            PlayerModel player = NewPlayer();

            Purchase(player);

            Assert.That(IsIn(player, PlayerSegmentIds.FirstWeekNonPurchaser), Is.False);
        }

        #endregion

        #region Gem funded

        [TestCase(-1, false)]
        [TestCase(0,  true)]
        [TestCase(1,  true)]
        public void TheGemCohortStartsExactlyAtItsThreshold(int gemsAboveThreshold, bool expectedFunded)
        {
            PlayerModel player = NewPlayer(new RewardBundle(CurrencyAmount.Gems(TestGameConfig.GemFundedMinGems + gemsAboveThreshold)));

            Assert.That(IsIn(player, PlayerSegmentIds.GemFunded), Is.EqualTo(expectedFunded));
        }

        /// <summary>
        /// The gem cohort reads the current wallet balance, so spending below the threshold leaves the cohort.
        /// </summary>
        [Test]
        public void SpendingBelowTheThresholdLeavesTheGemCohort()
        {
            PlayerModel player = NewPlayer(new RewardBundle(CurrencyAmount.Gems(TestGameConfig.GemFundedMinGems)));

            Assert.That(IsIn(player, PlayerSegmentIds.GemFunded), Is.True);

            player.ApplyWallet(
                WalletTransaction.Spend(EconomyFeature.Cosmetics, EconomyReason.CosmeticPurchase, CurrencyAmount.Gems(1), EconomyContentId.FromString("frame.emerald")),
                commit: true,
                AnalyticsCorrelationId.None);

            Assert.That(IsIn(player, PlayerSegmentIds.GemFunded), Is.False);
        }

        /// <summary>
        /// The gem cohort ignores purchase history. It checks only whether the player can afford the offer, and
        /// bought gems pay for it the same as earned gems.
        /// </summary>
        [Test]
        public void TheGemCohortDoesNotCareWhetherThePlayerHasPurchased()
        {
            PlayerModel player = NewPlayer(new RewardBundle(CurrencyAmount.Gems(TestGameConfig.GemFundedMinGems)));

            Purchase(player);

            Assert.That(IsIn(player, PlayerSegmentIds.GemFunded), Is.True);
        }

        #endregion

        #region Engaged, no purchase

        [Test]
        public void TheEngagedCohortStartsExactlyAtItsThreshold()
        {
            PlayerModel player = NewPlayer();

            FinishGames(player, TestGameConfig.EngagedMinMatchesPlayed - 1);
            Assert.That(IsIn(player, PlayerSegmentIds.EngagedNonPurchaser), Is.False, "one game short of the threshold was counted as engaged");

            FinishGames(player, 1);
            Assert.That(IsIn(player, PlayerSegmentIds.EngagedNonPurchaser), Is.True, "the threshold itself was not counted as engaged");
        }

        [Test]
        public void APurchaseLeavesTheEngagedCohort()
        {
            PlayerModel player = NewPlayer();

            FinishGames(player, TestGameConfig.EngagedMinMatchesPlayed);
            Purchase(player);

            Assert.That(IsIn(player, PlayerSegmentIds.EngagedNonPurchaser), Is.False);
        }

        /// <summary>
        /// Lost games count toward the engaged cohort, which measures games played. The sample has no property
        /// for wins or losses on purpose, so no offer can target a player for losing.
        /// </summary>
        [Test]
        public void LosingEveryGameStillCountsAsEngaged()
        {
            PlayerModel player = NewPlayer();

            FinishGames(player, TestGameConfig.EngagedMinMatchesPlayed, won: 0);

            Assert.That(IsIn(player, PlayerSegmentIds.EngagedNonPurchaser), Is.True);
        }

        #endregion

        #region The cohorts as a set

        /// <summary>
        /// The cohorts can overlap, and the segments do not resolve the overlap. A first-week player who holds
        /// enough gems is in both cohorts, and the offer group priority decides which offer the player sees
        /// (<c>docs/offers.md</c>).
        /// </summary>
        [Test]
        public void TheCohortsOverlapAndDoNotResolveEachOther()
        {
            PlayerModel player = NewPlayer(new RewardBundle(CurrencyAmount.Gems(TestGameConfig.GemFundedMinGems)));

            FinishGames(player, TestGameConfig.EngagedMinMatchesPlayed);

            Assert.That(IsIn(player, PlayerSegmentIds.FirstWeekNonPurchaser), Is.True);
            Assert.That(IsIn(player, PlayerSegmentIds.GemFunded), Is.True);
            Assert.That(IsIn(player, PlayerSegmentIds.EngagedNonPurchaser), Is.True);
        }

        /// <summary>
        /// A player past the first week who has made a purchase and holds fewer gems than the gem threshold is
        /// in no cohort. The fallback offer group covers this player by having no targeting, so no segment
        /// matches everybody.
        /// </summary>
        [Test]
        public void APlayerCanBeInNoCohortAtAll()
        {
            PlayerModel player = NewPlayer();

            Purchase(player);
            AgeTo(player, MetaDuration.FromDays(30));
            FinishGames(player, 50);

            foreach (PlayerSegmentId segmentId in PlayerSegmentIds.All)
                Assert.That(IsIn(player, segmentId), Is.False, $"{segmentId} matched a player who should be in no cohort");
        }

        /// <summary>
        /// The segment ids in <see cref="PlayerSegmentIds.All"/> match the config exactly. An id on only one side
        /// is a targeting rule that never matches and reports no error.
        /// </summary>
        [Test]
        public void TheNamedSegmentsAreExactlyTheOnesInConfig()
        {
            SharedGameConfig config = TestGameConfig.Build();

            Assert.That(config.PlayerSegments.Keys, Is.EquivalentTo(PlayerSegmentIds.All));
        }

        #endregion
    }
}
