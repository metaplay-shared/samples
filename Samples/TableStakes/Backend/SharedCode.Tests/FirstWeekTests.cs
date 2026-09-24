using Metaplay.Core;
using Metaplay.Core.Analytics;
using Metaplay.Core.Model;
using Metaplay.Core.Player;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.Logic.Tests
{
    /// <summary>
    /// Tests the first-week event (<c>docs/first-week-event.md</c>): seven 24-hour days counted from the
    /// player's own start time, a match-completion goal for each day, and a reward claim that does not expire.
    /// <para>
    /// The event consumes the match-completion fact, so most tests deliver results through
    /// <see cref="PlayerRecordMatchResult"/> and run the production recording and dispatch code. The tests
    /// assert what the player would see.
    /// </para>
    /// </summary>
    [TestFixture]
    public class FirstWeekTests
    {
        #region Fixture

        static MetaTime At(int year, int month, int day, int hour = 12, int minute = 0) =>
            MetaTime.FromDateTime(new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Utc));

        /// <summary>The creation time of every player in this fixture, which is also their first-week start time.</summary>
        static readonly MetaTime CreatedAt = At(2026, 9, 1, 23, 55);

        static readonly MetaDuration OneDay = FirstWeekScheduleInfo.DayLength;

        static PlayerModel NewPlayer(List<PlayerEventBase> captured = null, SharedGameConfig config = null, MetaTime? createdAt = null) =>
            TestPlayers.New(createdAt ?? CreatedAt, config, captured);

        /// <summary>Delivers one table result through <see cref="TestPlayers.DryRunThenCommit"/>. A loss is position 2.</summary>
        static MetaActionResult Deliver(PlayerModel player, int matchIndex, MetaTime at, bool win = true) =>
            TestPlayers.DryRunThenCommit(player, TestPlayers.MatchResult(matchIndex, at, position: win ? 0 : 2));

        static MetaActionResult Claim(PlayerModel player, FirstWeekDayId dayId) =>
            TestPlayers.DryRunThenCommit(player, new PlayerClaimFirstWeekReward(dayId));

        /// <summary>
        /// Runs the player model's schema migrations from <paramref name="fromVersion"/> to the current version,
        /// through the same <see cref="SchemaMigrationRegistry"/> that the player actor uses.
        /// </summary>
        static void Migrate(PlayerModel player, int fromVersion) =>
            SchemaMigrationRegistry.Instance.GetSchemaMigrator<PlayerModel>().RunMigrations(player, fromVersion);

        /// <summary>The time the migration tests run the v1-to-v2 migration, long after the account was created.</summary>
        static readonly MetaTime MigratedAt = CreatedAt + MetaDuration.FromDays(40);

        /// <summary>
        /// Returns a v1 player, created with no first-week state, after the v1-to-v2 migration ran at
        /// <see cref="MigratedAt"/>. The SDK sets the game config and the time before it runs migrations.
        /// </summary>
        static PlayerModel MigratedPlayer()
        {
            PlayerModel player = NewPlayer(config: ConfigWithoutSchedules());
            Assert.That(player.FirstWeek.HasStarted, Is.False, "a v1 player has no start time and no pinned schedule");

            player.SetGameConfig(TestGameConfig.Build());
            player.ResetTime(MigratedAt);
            Migrate(player, fromVersion: 1);
            return player;
        }

        static FirstWeekOutlook Outlook(PlayerModel player, MetaTime at) =>
            player.FirstWeek.OutlookAt(player.GameConfig, at);

        static FirstWeekDayStatus Day(PlayerModel player, int day, MetaTime at) =>
            Outlook(player, at).Days[day - 1];

        /// <summary>The time 12 hours into the player's day <paramref name="day"/> (1-based).</summary>
        static MetaTime Inside(int day) => CreatedAt + OneDay * (day - 1) + MetaDuration.FromHours(12);

        /// <summary>The fixture schedule's daily match goals, repeated here so tests assert against fixed numbers.</summary>
        static readonly int[] DailyMatchGoals = { 1, 1, 2, 2, 2, 3, 1 };

        /// <summary>Delivers enough results to meet day <paramref name="day"/>'s goal, with match ids <c>day * 100 + n</c>.</summary>
        static void FinishDay(PlayerModel player, int day, MetaTime? at = null)
        {
            MetaTime finishedAt = at ?? Inside(day);
            for (int index = 0; index < DailyMatchGoals[day - 1]; index++)
                Deliver(player, day * 100 + index, finishedAt);
        }

        #endregion

        #region What a player starts with

        [Test]
        public void AFreshPlayerIsPinnedToTheActiveScheduleAndStartsOnDayOne()
        {
            // The event starts at account creation with no player or operator action, and the player is pinned
            // to the schedule that the game config names as active at that time.
            PlayerModel player = NewPlayer();

            Assert.That(player.FirstWeek.HasStarted, Is.True);
            Assert.That(player.FirstWeek.StartedAt, Is.EqualTo(CreatedAt));
            Assert.That(player.FirstWeek.ScheduleId, Is.EqualTo(TestGameConfig.FirstWeekSchedule));
            Assert.That(player.FirstWeek.DayIndexAt(CreatedAt), Is.Zero);
        }

        /// <summary>
        /// Checks that every day's goal and exact reward is visible from the first session. Future days are shown
        /// as previews, not hidden.
        /// </summary>
        [Test]
        public void AllSevenDaysAndTheirExactRewardsAreVisibleFromTheFirstSession()
        {
            PlayerModel      player  = NewPlayer();
            FirstWeekOutlook outlook = Outlook(player, CreatedAt);

            Assert.That(outlook.IsResolved, Is.True);
            Assert.That(outlook.Days, Has.Count.EqualTo(FirstWeekScheduleInfo.NumDays));
            Assert.That(outlook.Days.Select(day => day.Target), Is.EqualTo(DailyMatchGoals));

            Assert.That(outlook.Days.Select(day => day.Info.Reward.AmountOf(CurrencyType.Coins)),
                Is.EqualTo(new[] { 250, 300, 350, 400, 450, 500, 1500 }));

            // Day one is active and every later day is a preview. No day is missed and no reward is claimable.
            Assert.That(outlook.Days[0].IsActive, Is.True);
            Assert.That(outlook.Days.Skip(1).Select(day => day.IsFuture), Is.All.True);
            Assert.That(outlook.Days.Any(day => day.IsMissed), Is.False);
            Assert.That(outlook.ClaimableCount, Is.Zero);
        }

        #endregion

        #region The day arithmetic

        /// <summary>
        /// Checks that a day is 24 hours counted from the player's start time, not a calendar day. The fixture
        /// player is created shortly before midnight, where a calendar day would make day one very short.
        /// </summary>
        [Test]
        public void ADayIsTwentyFourElapsedHoursFromTheEpochAndNotACalendarDay()
        {
            PlayerModel player = NewPlayer();

            Assert.That(player.FirstWeek.DayIndexAt(CreatedAt), Is.Zero);
            Assert.That(player.FirstWeek.DayIndexAt(CreatedAt + MetaDuration.FromMinutes(10)), Is.Zero,
                "ten minutes past local midnight is still the player's own day one");
            Assert.That(player.FirstWeek.DayIndexAt(CreatedAt + OneDay - MetaDuration.FromMilliseconds(1)), Is.Zero);
            Assert.That(player.FirstWeek.DayIndexAt(CreatedAt + OneDay), Is.EqualTo(1));
        }

        [TestCase(0, 0)]
        [TestCase(1, 1)]
        [TestCase(6, 6)]
        [TestCase(7, 7)]
        [TestCase(365, 7)]
        public void TheDayIndexIsAnalyticalAndSaturatesWhenTheRunEnds(int elapsedDays, int expectedIndex)
        {
            PlayerModel player = NewPlayer();
            Assert.That(player.FirstWeek.DayIndexAt(CreatedAt + OneDay * elapsedDays), Is.EqualTo(expectedIndex));
        }

        /// <summary>
        /// Checks that the day index and the outlook for a player away for a year come from a direct
        /// calculation, without advancing through each elapsed day.
        /// </summary>
        [Test]
        public void APlayerAwayForAYearCostsWhatAPlayerAwayForAnHourCosts()
        {
            PlayerModel player = NewPlayer();

            FirstWeekOutlook outlook = Outlook(player, CreatedAt + MetaDuration.FromDays(365));

            Assert.That(outlook.HasEnded, Is.True);
            Assert.That(outlook.MissedCount, Is.EqualTo(FirstWeekScheduleInfo.NumDays), "every day of an unplayed run is missed");
        }

        /// <summary>Checks that a time before the player's start time belongs to no day (index -1).</summary>
        [Test]
        public void AStampBeforeTheEpochBelongsToNoDay()
        {
            PlayerModel player = NewPlayer();
            Assert.That(player.FirstWeek.DayIndexAt(CreatedAt - MetaDuration.FromHours(1)), Is.EqualTo(-1));
        }

        #endregion

        #region Progress

        [Test]
        public void OneFinishedGameAdvancesTheActiveDayAndOnlyThatDay()
        {
            PlayerModel player = NewPlayer();

            Deliver(player, 1, Inside(1));

            Assert.That(Day(player, 1, Inside(1)).Count, Is.EqualTo(1));
            Assert.That(Day(player, 1, Inside(1)).IsComplete, Is.True, "day one asks for one match");
            Assert.That(Day(player, 2, Inside(1)).Count, Is.Zero);
        }

        /// <summary>Checks that a loss counts toward the goal the same as a win, because the goal is completed matches.</summary>
        [Test]
        public void ALossCountsTheSameAsAWin()
        {
            PlayerModel player = NewPlayer();

            Deliver(player, 1, Inside(3), win: false);
            Deliver(player, 2, Inside(3), win: false);

            Assert.That(Day(player, 3, Inside(3)).IsComplete, Is.True, "day three asks for two completed matches");
        }

        /// <summary>Checks that matches after the day's goal is met change neither the count nor the completion time.</summary>
        [Test]
        public void MatchesPastTheTargetChangeNothing()
        {
            PlayerModel player = NewPlayer();

            Deliver(player, 1, Inside(1));
            MetaTime completedAt = Day(player, 1, Inside(1)).Progress.CompletedAt;

            Deliver(player, 2, Inside(1) + MetaDuration.FromHours(1));

            Assert.That(Day(player, 1, Inside(1)).Count, Is.EqualTo(1));
            Assert.That(Day(player, 1, Inside(1)).Progress.CompletedAt, Is.EqualTo(completedAt),
                "the completion stamp is the crossing, and a later game must not move it");
        }

        /// <summary>Checks that progress does not carry over: the day after an unfinished day starts at zero.</summary>
        [Test]
        public void ProgressDoesNotCarryToTheNextDay()
        {
            PlayerModel player = NewPlayer();

            Deliver(player, 1, Inside(3));               // day three asks for two
            Assert.That(Day(player, 3, Inside(3)).Count, Is.EqualTo(1));

            Deliver(player, 2, Inside(4));

            Assert.That(Day(player, 3, Inside(4)).Count, Is.EqualTo(1), "day three keeps what it had and expires with it");
            Assert.That(Day(player, 3, Inside(4)).IsMissed, Is.True);
            Assert.That(Day(player, 4, Inside(4)).Count, Is.EqualTo(1), "day four started at zero");
        }

        /// <summary>
        /// Checks that a match counts toward the day that contains its completion time, not the day in which
        /// the result is delivered.
        /// </summary>
        [Test]
        public void AMatchAcrossABoundaryIsAssignedByItsCompletionStamp()
        {
            PlayerModel player = NewPlayer();

            // The table finished a minute before day two started. The outlook is read during day two.
            MetaTime justBefore = CreatedAt + OneDay - MetaDuration.FromMinutes(1);
            Deliver(player, 1, justBefore);

            Assert.That(Day(player, 1, Inside(2)).IsComplete, Is.True, "the stamp fell inside day one");
            Assert.That(Day(player, 2, Inside(2)).Count, Is.Zero);
        }

        [Test]
        public void AMatchFinishedAfterTheSeventhWindowCountsNowhere()
        {
            PlayerModel player = NewPlayer();

            Deliver(player, 1, CreatedAt + OneDay * 7 + MetaDuration.FromHours(1));

            Assert.That(Outlook(player, CreatedAt + OneDay * 7).Days.Any(day => day.Count > 0), Is.False);
            Assert.That(player.Record.GamesPlayed, Is.EqualTo(1), "the game is still on the record");
        }

        #endregion

        #region The seam's two guards

        /// <summary>
        /// Checks the monotone rule of <see cref="IMatchCompletionObserver"/>: a fact whose completion time is in a
        /// day earlier than the latest day that counted a match counts toward no day. Crediting the current day
        /// would pay for an old game, and crediting the old day would reopen a closed day.
        /// </summary>
        [Test]
        public void AFactCarryingAStampFromAClosedDayCountsNowhere()
        {
            PlayerModel player = NewPlayer();

            Deliver(player, 1, Inside(5));
            Assert.That(Day(player, 5, Inside(5)).Count, Is.EqualTo(1));

            // A result with a completion time in day two, delivered during day five.
            Deliver(player, 99, Inside(2));

            Assert.That(Day(player, 2, Inside(5)).Count, Is.Zero, "the closed day was not revived");
            Assert.That(Day(player, 5, Inside(5)).Count, Is.EqualTo(1), "and today was not credited for it either");
        }

        /// <summary>
        /// Checks that a fact with an old completion time cannot reopen or un-claim a day whose reward was
        /// already paid, which would let the reward be paid twice.
        /// </summary>
        [Test]
        public void AStaleStampCannotReopenADayWhoseRewardWasAlreadyPaid()
        {
            PlayerModel player = NewPlayer();

            FinishDay(player, 1);
            Assert.That(Claim(player, TestGameConfig.FirstWeekDay(1)), Is.EqualTo(MetaActionResult.Success));

            player.ResetTime(Inside(4));
            Deliver(player, 1, Inside(4));                        // counts toward day four
            Deliver(player, 42, Inside(1));                       // completion time in day one

            Assert.That(Day(player, 1, Inside(4)).IsClaimed, Is.True);
            Assert.That(Day(player, 1, Inside(4)).Count, Is.EqualTo(1));
            Assert.That(player.FirstWeek.ClaimCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Checks the deduplication rule of <see cref="IMatchCompletionObserver"/>: the same match delivered twice
        /// within one day counts once. The set of counted match ids covers one day, and no ids are added after
        /// the day's goal is met, so its size is bounded by the day's goal.
        /// </summary>
        [Test]
        public void TheSameTableDeliveredTwiceInsideOneDayCountsOnce()
        {
            PlayerModel player = NewPlayer();

            // Day three asks for two, so counting the repeat would complete the day.
            Deliver(player, 1, Inside(3));

            Redeliver(player, 1, Inside(3));

            Assert.That(Day(player, 3, Inside(3)).Count, Is.EqualTo(1));
            Assert.That(Day(player, 3, Inside(3)).IsComplete, Is.False);

            Deliver(player, 2, Inside(3));
            Assert.That(Day(player, 3, Inside(3)).IsComplete, Is.True);
        }

        /// <summary>Checks that the set of counted match ids is cleared when a new day starts counting.</summary>
        [Test]
        public void TheDedupeSetIsScopedToOneDay()
        {
            PlayerModel player = NewPlayer();

            Deliver(player, 1, Inside(3));
            Assert.That(player.FirstWeek.CountedMatches, Has.Count.EqualTo(1));

            Deliver(player, 2, Inside(4));
            Assert.That(player.FirstWeek.CountedMatches, Has.Count.EqualTo(1), "the previous day's ids went with the day");
            Assert.That(player.FirstWeek.CountedMatches, Does.Contain(MatchTestDeals.MatchId(2)));
        }

        #endregion

        #region A fact that arrives after its own window closed

        /// <summary>
        /// Checks that a game finished in day three but recorded during day five counts toward day three, which
        /// changes day three from missed to reward ready. A day must be missed only because the player did not
        /// play it, not because the result was recorded late.
        /// </summary>
        [Test]
        public void AGameFinishedInsideAClosedDayIsStillCreditedToThatDay()
        {
            PlayerModel player = NewPlayer();

            // Day three asks for two. One result is recorded during day three, so the day ends one match short.
            Deliver(player, 1, Inside(3));
            Assert.That(Day(player, 3, Inside(4)).IsMissed, Is.True);

            // The second game finished two minutes before day three ended and is recorded during day five.
            MetaTime lateStamp = CreatedAt + OneDay * 3 - MetaDuration.FromMinutes(2);
            player.ResetTime(Inside(5));
            Deliver(player, 2, lateStamp);

            Assert.That(Day(player, 3, Inside(5)).IsMissed, Is.False);
            Assert.That(Day(player, 3, Inside(5)).IsRewardReady, Is.True);
            Assert.That(Claim(player, TestGameConfig.FirstWeekDay(3)), Is.EqualTo(MetaActionResult.Success));
        }

        /// <summary>
        /// Checks a late fact when no match has counted after day one: two results with completion times in day
        /// three, recorded during day five, complete day three and change no other day.
        /// </summary>
        [Test]
        public void ALateFactRollsForwardIntoTheClosedDayItsStampBelongsTo()
        {
            PlayerModel player = NewPlayer();

            FinishDay(player, 1);

            player.ResetTime(Inside(5));
            Deliver(player, 301, Inside(3));
            Deliver(player, 302, Inside(3));

            Assert.That(Day(player, 3, Inside(5)).IsRewardReady, Is.True);
            Assert.That(Day(player, 2, Inside(5)).IsMissed, Is.True, "day two saw no game and stays missed");
            Assert.That(Day(player, 4, Inside(5)).IsMissed, Is.True);
            Assert.That(Day(player, 5, Inside(5)).Count, Is.Zero, "today was not credited for a game played on day three");
        }

        /// <summary>
        /// Checks the limit on late facts: once a match has counted toward a later day, a fact with an earlier
        /// completion time counts nowhere. A missed day can only be recovered while no later day has counted a
        /// match.
        /// </summary>
        [Test]
        public void ADayCannotComeBackOnceALaterDayHasCountedAMatch()
        {
            PlayerModel player = NewPlayer();

            Deliver(player, 1, Inside(3));    // day three, one match short
            Deliver(player, 2, Inside(5));    // counts toward day five

            Deliver(player, 3, Inside(3));    // completion time in day three, after day five counted

            Assert.That(Day(player, 3, Inside(6)).IsMissed, Is.True);
            Assert.That(Day(player, 3, Inside(6)).Count, Is.EqualTo(1));
        }

        #endregion

        #region Expiry, and what it does not take

        /// <summary>
        /// Checks that a missed day does not reset the event: later days, including the last, can still be
        /// completed and claimed.
        /// </summary>
        [Test]
        public void AMissedDayDoesNotBlockTheDaysAfterItOrTheCapstone()
        {
            PlayerModel player = NewPlayer();

            FinishDay(player, 1);
            // Days two to six go by unplayed.
            FinishDay(player, 7);

            FirstWeekOutlook outlook = Outlook(player, Inside(7));

            Assert.That(outlook.MissedCount, Is.EqualTo(5), "days two to six elapsed unfinished");
            Assert.That(outlook.Days[6].IsRewardReady, Is.True, "the capstone is still earnable");
            Assert.That(Claim(player, TestGameConfig.FirstWeekDay(7)), Is.EqualTo(MetaActionResult.Success));
        }

        /// <summary>Checks that a completed day never becomes missed while its reward is unclaimed.</summary>
        [Test]
        public void ACompletedDayNeverBecomesMissed()
        {
            PlayerModel player = NewPlayer();

            FinishDay(player, 1);

            Assert.That(Day(player, 1, Inside(7)).IsMissed, Is.False);
            Assert.That(Day(player, 1, Inside(7)).IsRewardReady, Is.True);
        }

        #endregion

        #region Claiming

        /// <summary>
        /// Checks that a completed day's reward stays claimable after the event has ended.
        /// </summary>
        [Test]
        public void ARewardEarnedOnDayOneIsStillClaimableAfterTheRunHasEnded()
        {
            PlayerModel player = NewPlayer();

            FinishDay(player, 1);

            player.ResetTime(CreatedAt + MetaDuration.FromDays(30));

            Assert.That(Outlook(player, player.CurrentTime).HasEnded, Is.True);
            Assert.That(Claim(player, TestGameConfig.FirstWeekDay(1)), Is.EqualTo(MetaActionResult.Success));
            Assert.That(player.Wallet.Coins, Is.EqualTo(TestGameConfig.StartingWallet().AmountOf(CurrencyType.Coins) + 250));
        }

        [Test]
        public void ClaimingPaysTheConfiguredBundleAndMarksTheDayClaimed()
        {
            PlayerModel player = NewPlayer();
            int         before = player.Wallet.Coins;

            FinishDay(player, 1);
            Assert.That(Claim(player, TestGameConfig.FirstWeekDay(1)), Is.EqualTo(MetaActionResult.Success));

            Assert.That(player.Wallet.Coins, Is.EqualTo(before + 250));
            Assert.That(Day(player, 1, Inside(1)).IsClaimed, Is.True);
            Assert.That(player.FirstWeek.ClaimCount, Is.EqualTo(1));
            Assert.That(player.FirstWeek.LastClaimedDay, Is.EqualTo(TestGameConfig.FirstWeekDay(1)));
        }

        /// <summary>Checks that one claim of the last day pays its coins, gems and spin tokens together.</summary>
        [Test]
        public void TheCapstonePaysCoinsGemsAndSpinTokensTogether()
        {
            PlayerModel player = NewPlayer();
            RewardBundle starting = TestGameConfig.StartingWallet();

            player.ResetTime(Inside(7));
            FinishDay(player, 7);
            Assert.That(Claim(player, TestGameConfig.FirstWeekDay(7)), Is.EqualTo(MetaActionResult.Success));

            Assert.That(player.Wallet.Coins, Is.EqualTo(starting.AmountOf(CurrencyType.Coins) + 1500));
            Assert.That(player.Wallet.Gems, Is.EqualTo(starting.AmountOf(CurrencyType.Gems) + 100));
            Assert.That(player.Wallet.SpinTokens, Is.EqualTo(starting.AmountOf(CurrencyType.SpinTokens) + 2));
        }

        /// <summary>
        /// Checks that a second claim of the same day is refused and pays nothing. A duplicate tap, a replayed
        /// action and a retry after a reconnect all reach this case.
        /// </summary>
        [Test]
        public void ClaimingTwicePaysOnce()
        {
            List<PlayerEventBase> captured = new List<PlayerEventBase>();
            PlayerModel           player   = NewPlayer(captured);

            FinishDay(player, 1);
            Claim(player, TestGameConfig.FirstWeekDay(1));
            int after = player.Wallet.Coins;
            captured.Clear();

            Assert.That(Claim(player, TestGameConfig.FirstWeekDay(1)), Is.EqualTo(ActionResults.FirstWeekAlreadyClaimed));
            Assert.That(player.Wallet.Coins, Is.EqualTo(after));
            Assert.That(player.FirstWeek.ClaimCount, Is.EqualTo(1));
            Assert.That(captured, Is.Empty, "a refused claim reports nothing");
        }

        [Test]
        public void AnUnfinishedDayCannotBeClaimed()
        {
            PlayerModel player = NewPlayer();
            int         before = player.Wallet.Coins;

            player.ResetTime(Inside(3));
            Deliver(player, 1, Inside(3));                       // day three asks for two

            Assert.That(Claim(player, TestGameConfig.FirstWeekDay(3)), Is.EqualTo(ActionResults.FirstWeekDayNotComplete));
            Assert.That(Claim(player, TestGameConfig.FirstWeekDay(4)), Is.EqualTo(ActionResults.FirstWeekDayNotComplete));
            Assert.That(player.Wallet.Coins, Is.EqualTo(before));
        }

        /// <summary>
        /// Checks that a claim refused by the wallet cap leaves the day ready and unclaimed. The claim marks the
        /// day claimed only after the wallet accepts the reward, so a player at a currency cap can spend and
        /// claim later.
        /// </summary>
        [Test]
        public void AWalletThatRefusesLeavesTheDayReadyAndUnclaimed()
        {
            List<PlayerEventBase> captured = new List<PlayerEventBase>();
            PlayerModel player = NewPlayer(captured,
                TestGameConfig.Build(new RewardBundle(CurrencyAmount.Coins(TestGameConfig.MaxCoins))));

            FinishDay(player, 1);
            captured.Clear();

            Assert.That(Claim(player, TestGameConfig.FirstWeekDay(1)), Is.EqualTo(ActionResults.WalletCapExceeded));

            Assert.That(player.Wallet.Coins, Is.EqualTo(TestGameConfig.MaxCoins));
            Assert.That(Day(player, 1, Inside(1)).IsClaimed, Is.False);
            Assert.That(Day(player, 1, Inside(1)).IsRewardReady, Is.True, "the reward is still there to come back for");
            Assert.That(player.FirstWeek.ClaimCount, Is.Zero);
            Assert.That(captured, Is.Empty, "a refused claim pays nothing and reports nothing");
        }

        [Test]
        public void AClaimForADayTheScheduleDoesNotHoldIsRefused()
        {
            PlayerModel player = NewPlayer();

            Assert.That(Claim(player, FirstWeekDayId.FromString("nowhere")), Is.EqualTo(ActionResults.NoSuchFirstWeekDay));
            Assert.That(Claim(player, null), Is.EqualTo(ActionResults.NoSuchFirstWeekDay));
        }

        /// <summary>
        /// Checks that a claim against a schedule missing from the game config is refused, not paid from the
        /// active schedule. Publishing the schedule again makes the reward claimable, while paying from another
        /// schedule would pay a reward the player was never shown.
        /// </summary>
        [Test]
        public void AClaimAgainstAWithdrawnScheduleIsRefusedRatherThanPaidFromTheActiveOne()
        {
            PlayerModel player = NewPlayer();
            FinishDay(player, 1);

            player.SetGameConfig(TestGameConfig.Build(firstWeek: OtherSchedule()));

            Assert.That(Claim(player, TestGameConfig.FirstWeekDay(1)), Is.EqualTo(ActionResults.FirstWeekConfigMissing));
            Assert.That(player.Wallet.Coins, Is.EqualTo(TestGameConfig.StartingWallet().AmountOf(CurrencyType.Coins)));
        }

        #endregion

        #region The pinned schedule

        /// <summary>
        /// Checks that a player keeps the schedule they were pinned to. Publishing a new active schedule affects
        /// only players created afterwards.
        /// </summary>
        [Test]
        public void PublishingANewScheduleDoesNotChangeAnActivePlayersGoalsOrRewards()
        {
            PlayerModel player = NewPlayer();
            FinishDay(player, 1);

            // A new config with a second schedule that has different goals and rewards, marked as active.
            player.SetGameConfig(ConfigWithBothSchedules(active: OtherScheduleId));

            Assert.That(player.FirstWeek.ScheduleId, Is.EqualTo(TestGameConfig.FirstWeekSchedule));
            Assert.That(Day(player, 1, Inside(1)).Target, Is.EqualTo(1), "still the goal this player was shown");
            Assert.That(Day(player, 2, Inside(1)).Info.Reward.AmountOf(CurrencyType.Coins), Is.EqualTo(300));

            int before = player.Wallet.Coins;
            Assert.That(Claim(player, TestGameConfig.FirstWeekDay(1)), Is.EqualTo(MetaActionResult.Success));
            Assert.That(player.Wallet.Coins, Is.EqualTo(before + 250), "paid the promise, not the retune");
        }

        /// <summary>Checks that a player created after the active schedule changes is pinned to the new schedule.</summary>
        [Test]
        public void APlayerCreatedAfterTheRetuneGetsTheNewSchedule()
        {
            PlayerModel player = NewPlayer(config: ConfigWithBothSchedules(active: OtherScheduleId));

            Assert.That(player.FirstWeek.ScheduleId, Is.EqualTo(OtherScheduleId));
            Assert.That(Day(player, 1, Inside(1)).Info.Reward.AmountOf(CurrencyType.Coins), Is.EqualTo(500));
        }

        #endregion

        #region The migration

        /// <summary>
        /// Checks that the schema v1-to-v2 migration starts the event for an account with no first-week state:
        /// the start time is the migration time, and the schedule is the one active in the game config then.
        /// <para>
        /// The migration can read both because the SDK assigns the model's game config and current time before
        /// it runs migrations, in <c>PlayerActorBase.AssignBasicRuntimePropertiesToModel</c>.
        /// </para>
        /// </summary>
        [Test]
        public void TheMigrationStampsAnEpochAndPinsAScheduleFromTheSameMoment()
        {
            PlayerModel player = MigratedPlayer();

            Assert.That(player.FirstWeek.StartedAt, Is.EqualTo(MigratedAt), "the epoch is the migration, not the account");
            Assert.That(player.FirstWeek.ScheduleId, Is.EqualTo(TestGameConfig.FirstWeekSchedule));
            Assert.That(player.FirstWeek.DayIndexAt(MigratedAt), Is.Zero, "day one opens on a full 24-hour window");
            Assert.That(player.FirstWeek.EndsAt, Is.EqualTo(MigratedAt + FirstWeekScheduleInfo.EventLength));
        }

        /// <summary>
        /// Checks that a game completed before a migrated player's start time counts toward no day. Every game a
        /// migrated player played before the migration has such a completion time, so any re-delivered result
        /// reaches this case.
        /// </summary>
        [Test]
        public void AGameAMigratedPlayerFinishedBeforeTheMigrationCountsNowhere()
        {
            PlayerModel player = MigratedPlayer();

            Deliver(player, 1, CreatedAt + MetaDuration.FromDays(3));

            Assert.That(player.FirstWeek.DayIndexAt(CreatedAt + MetaDuration.FromDays(3)), Is.EqualTo(-1));
            Assert.That(Day(player, 1, MigratedAt).Count, Is.Zero, "day one was not credited for a game played before it opened");
            Assert.That(player.FirstWeek.StartedAt, Is.EqualTo(MigratedAt), "and the epoch did not move");
        }

        /// <summary>Checks that a migrated player's day one starts at the migration and counts games normally.</summary>
        [Test]
        public void AMigratedPlayerPlaysTheirFirstDayFromTheMigration()
        {
            PlayerModel player   = MigratedPlayer();
            MetaTime    playedAt = MigratedAt + MetaDuration.FromHours(3);
            Deliver(player, 1, playedAt);

            Assert.That(Day(player, 1, playedAt).IsRewardReady, Is.True);
            Assert.That(Claim(player, TestGameConfig.FirstWeekDay(1)), Is.EqualTo(MetaActionResult.Success));
        }

        /// <summary>
        /// Checks that a migration run with no schedule in the game config sets neither the start time nor the
        /// schedule, and that the player's next finished game then starts the event.
        /// </summary>
        [Test]
        public void AMigrationWithNoScheduleToPinLeavesTheSeamToStartThePlayer()
        {
            PlayerModel player = NewPlayer(config: ConfigWithoutSchedules());

            player.ResetTime(CreatedAt + MetaDuration.FromDays(40));
            Migrate(player, fromVersion: 1);
            Assert.That(player.FirstWeek.HasStarted, Is.False);

            player.SetGameConfig(TestGameConfig.Build());
            MetaTime playedAt = CreatedAt + MetaDuration.FromDays(50);
            Deliver(player, 1, playedAt);

            Assert.That(player.FirstWeek.StartedAt, Is.EqualTo(playedAt));
            Assert.That(player.FirstWeek.ScheduleId, Is.EqualTo(TestGameConfig.FirstWeekSchedule));
        }

        #endregion

        #region Never breaking the game

        /// <summary>
        /// Checks that a missing schedule does not invalidate a committed match. The result is recorded and
        /// the first-week event makes no progress.
        /// </summary>
        [Test]
        public void AMissingScheduleLeavesTheMatchOnTheRecord()
        {
            PlayerModel player = NewPlayer(config: ConfigWithoutSchedules());

            Assert.That(player.FirstWeek.HasStarted, Is.False, "there was no schedule to pin, so no promise was made");
            Assert.That(Deliver(player, 1, Inside(1)), Is.EqualTo(MetaActionResult.Success));
            Assert.That(player.Record.GamesPlayed, Is.EqualTo(1));
            Assert.That(Outlook(player, Inside(1)).IsResolved, Is.False);
        }

        /// <summary>
        /// Checks that an account with no start time starts the event on the next game it finishes, not at its
        /// creation time, and that the game counts toward day one.
        /// </summary>
        [Test]
        public void AnAccountWithNoEpochJoinsOnTheGameItNextFinishes()
        {
            // A player created with no schedule in the game config has no start time.
            PlayerModel player = NewPlayer(config: ConfigWithoutSchedules());
            Assert.That(player.FirstWeek.HasStarted, Is.False);

            player.SetGameConfig(TestGameConfig.Build());

            MetaTime playedAt = CreatedAt + MetaDuration.FromDays(3);
            Deliver(player, 1, playedAt);

            Assert.That(player.FirstWeek.StartedAt, Is.EqualTo(playedAt), "the epoch is the game, not the account");
            Assert.That(player.FirstWeek.ScheduleId, Is.EqualTo(TestGameConfig.FirstWeekSchedule));
            Assert.That(Day(player, 1, playedAt).IsComplete, Is.True);
        }

        #endregion

        #region Analytics

        [Test]
        public void CompletingADayEmitsExactlyOneRow()
        {
            List<PlayerEventBase> captured = new List<PlayerEventBase>();
            PlayerModel           player   = NewPlayer(captured);

            player.ResetTime(Inside(3));
            Deliver(player, 1, Inside(3));
            Assert.That(captured.OfType<PlayerEventFirstWeekDayCompleted>(), Is.Empty, "an intermediate increment is not a row");

            Deliver(player, 2, Inside(3));

            PlayerEventFirstWeekDayCompleted row = captured.OfType<PlayerEventFirstWeekDayCompleted>().Single();
            Assert.That(row.Schedule, Is.EqualTo(TestGameConfig.FirstWeekSchedule));
            Assert.That(row.DayId, Is.EqualTo(TestGameConfig.FirstWeekDay(3)));
            Assert.That(row.Day, Is.EqualTo(3));
            Assert.That(row.DayIndex, Is.EqualTo(2));
            Assert.That(row.Target, Is.EqualTo(2));
            Assert.That(row.ProgressBefore, Is.EqualTo(1));
            Assert.That(row.ProgressAfter, Is.EqualTo(2));
            Assert.That(row.IsFinalDay, Is.False);
        }

        /// <summary>
        /// Checks that a claim writes one claim event and one currency event per currency, all with the same
        /// correlation id, so an analyst can join the claim to the currency it paid.
        /// </summary>
        [Test]
        public void ClaimingEmitsOneRowCorrelatedWithEveryCurrencyRow()
        {
            List<PlayerEventBase> captured = new List<PlayerEventBase>();
            PlayerModel           player   = NewPlayer(captured);

            player.ResetTime(Inside(7));
            FinishDay(player, 7);
            captured.Clear();

            Claim(player, TestGameConfig.FirstWeekDay(7));

            PlayerEventFirstWeekRewardClaimed row = captured.OfType<PlayerEventFirstWeekRewardClaimed>().Single();
            Assert.That(row.DayId, Is.EqualTo(TestGameConfig.FirstWeekDay(7)));
            Assert.That(row.Day, Is.EqualTo(7));
            Assert.That(row.IsFinalDay, Is.True);
            Assert.That(row.ClaimOrdinal, Is.EqualTo(1));
            Assert.That(row.ClaimedDays, Is.EqualTo(1));
            Assert.That(row.MissedDays, Is.EqualTo(6), "days one to six elapsed unfinished");

            List<PlayerEventEconomyTransaction> currency = captured.OfType<PlayerEventEconomyTransaction>().ToList();
            Assert.That(currency, Has.Count.EqualTo(3), "coins, gems and spin tokens");
            Assert.That(currency.Select(r => r.Correlation), Is.All.EqualTo(row.Correlation));
            Assert.That(currency.Select(r => r.Feature), Is.All.EqualTo(EconomyFeature.FirstWeekEvent));
            Assert.That(currency.Select(r => r.Reason), Is.All.EqualTo(EconomyReason.FirstWeekReward));
            Assert.That(currency.Select(r => r.ContentId),
                Is.All.EqualTo(EconomyContentId.FromString(TestGameConfig.FirstWeekDay(7).Value)));
        }

        #endregion

        #region A whole perfect week

        /// <summary>
        /// Checks a full week in which every day is completed and claimed, and the final wallet totals.
        /// </summary>
        [Test]
        public void APerfectWeekPlayedAndClaimedEndsOnTheBudgetedTotals()
        {
            PlayerModel  player   = NewPlayer();
            RewardBundle starting = TestGameConfig.StartingWallet();

            for (int day = 1; day <= FirstWeekScheduleInfo.NumDays; day++)
            {
                player.ResetTime(Inside(day));
                FinishDay(player, day);
                Assert.That(Claim(player, TestGameConfig.FirstWeekDay(day)), Is.EqualTo(MetaActionResult.Success), $"day {day}");
            }

            Assert.That(player.Record.GamesPlayed, Is.EqualTo(12));
            Assert.That(player.FirstWeek.ClaimCount, Is.EqualTo(7));
            Assert.That(Outlook(player, Inside(7)).MissedCount, Is.Zero);

            Assert.That(player.Wallet.Coins, Is.EqualTo(starting.AmountOf(CurrencyType.Coins) + 3_750));
            Assert.That(player.Wallet.Gems, Is.EqualTo(starting.AmountOf(CurrencyType.Gems) + 100));
            Assert.That(player.Wallet.SpinTokens, Is.EqualTo(starting.AmountOf(CurrencyType.SpinTokens) + 2));
        }

        #endregion

        #region Fixture helpers

        static readonly FirstWeekScheduleId OtherScheduleId = FirstWeekScheduleId.FromString("week.v2");

        /// <summary>A second schedule whose goals and rewards differ from the fixture schedule's.</summary>
        static FirstWeekScheduleInfo OtherSchedule()
        {
            List<FirstWeekDayInfo> days = new List<FirstWeekDayInfo>();
            for (int day = 1; day <= FirstWeekScheduleInfo.NumDays; day++)
            {
                RewardBundle reward = day == FirstWeekScheduleInfo.NumDays
                    ? new RewardBundle(CurrencyAmount.Coins(1500), CurrencyAmount.Gems(100), CurrencyAmount.SpinTokens(2))
                    : new RewardBundle(CurrencyAmount.Coins(500));

                days.Add(new FirstWeekDayInfo(
                    FirstWeekDayId.FromString($"week.v2.day{day}"), day,
                    day == FirstWeekScheduleInfo.NumDays ? 1 : 3, reward));
            }

            return new FirstWeekScheduleInfo(OtherScheduleId, days);
        }

        /// <summary>A config holding both schedules, with <paramref name="active"/> as the active schedule.</summary>
        static SharedGameConfig ConfigWithBothSchedules(FirstWeekScheduleId active)
        {
            SharedGameConfig config = TestGameConfig.Build();

            TestGameConfig.SetEntry(config, "FirstWeekSchedules",
                Metaplay.Core.Config.GameConfigLibrary<FirstWeekScheduleId, FirstWeekScheduleInfo>.CreateSolo(
                    new List<FirstWeekScheduleInfo> { TestGameConfig.FirstWeekScheduleTable(), OtherSchedule() }));

            TestGameConfig.SetEntry(config, "Global", new GlobalConfig(
                startingWallet:        TestGameConfig.StartingWallet(),
                maxCoins:              TestGameConfig.MaxCoins,
                maxGems:               TestGameConfig.MaxGems,
                maxSpinTokens:         TestGameConfig.MaxSpinTokens,
                dailyResetSchedule:    TestGameConfig.DailyReset(),
                dailyRewardTable:      TestGameConfig.DailyTable,
                firstWeekSchedule:     active,
                wheelTable:            WheelTableId.FromString("wheel"),
                dailyMissionSet:       TestGameConfig.DailySet,
                weeklyMissionSet:      TestGameConfig.WeeklySet,
                tournamentRewardTable: TournamentRewardTableId.FromString("tournament")));

            return config;
        }

        /// <summary>A config with no first-week schedules.</summary>
        static SharedGameConfig ConfigWithoutSchedules()
        {
            SharedGameConfig config = TestGameConfig.Build();
            TestGameConfig.SetEntry(config, "FirstWeekSchedules",
                Metaplay.Core.Config.GameConfigLibrary<FirstWeekScheduleId, FirstWeekScheduleInfo>.CreateSolo(new List<FirstWeekScheduleInfo>()));
            return config;
        }

        /// <summary>
        /// Calls <see cref="IMatchCompletionObserver.OnMatchCompleted"/> on the first-week state directly, as a
        /// re-delivered result would.
        /// <para>
        /// A repeated fact reaches a consumer only after its match has aged off the bounded match history, and a
        /// player who played that many games in one day met the day's goal long before. The deduplication set
        /// therefore cannot be reached through <see cref="PlayerRecordMatchResult"/>, so this helper calls the
        /// observer directly.
        /// </para>
        /// </summary>
        static void Redeliver(PlayerModel player, int matchIndex, MetaTime at)
        {
            MatchCompletionContext context = new MatchCompletionContext(
                player, new MatchCompletion(MatchTestDeals.MatchId(matchIndex), at, position: 0, tricksWon: 3));

            ((IMatchCompletionObserver)player.FirstWeek).OnMatchCompleted(context);
        }

        #endregion
    }
}
