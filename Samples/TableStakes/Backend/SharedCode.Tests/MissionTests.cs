using Metaplay.Core;
using Metaplay.Core.Analytics;
using Metaplay.Core.Model;
using Metaplay.Core.Player;
using Metaplay.Core.Schedule;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.Logic.Tests
{
    /// <summary>
    /// Tests for missions, the daily and weekly goals that advance from ordinary play (<c>docs/missions.md</c>).
    /// <para>
    /// Missions consume the match-completion fact, so most tests deliver a result through
    /// <see cref="PlayerRecordMatchResult"/>. That covers the once-per-match guard, the dispatch to consumers and
    /// the isolation of a failing consumer. The tests assert what the player would see.
    /// </para>
    /// </summary>
    [TestFixture]
    public class MissionTests
    {
        #region Fixture

        /// <summary>The UTC time on the given date, at noon unless an hour is given.</summary>
        static MetaTime At(int year, int month, int day, int hour = 12, int minute = 0) =>
            MetaTime.FromDateTime(new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Utc));

        // 1 September 2026 is a Tuesday. Its week begins on Monday 31 August.
        static readonly MetaTime TuesdayNoon    = At(2026, 9, 1);
        static readonly MetaTime MondayMidnight = At(2026, 8, 31, 0, 0);

        static PlayerModel NewPlayer(List<PlayerEventBase> captured = null, MetaDuration? utcOffset = null, SharedGameConfig config = null)
        {
            PlayerModel player = TestPlayers.New(TuesdayNoon, config, captured);

            if (utcOffset.HasValue)
                player.UpdateTimeZone(new PlayerTimeZoneInfo(utcOffset.Value), isFirstLogin: false);

            player.ResetTime(TuesdayNoon);
            return player;
        }

        /// <summary>Delivers one match result through <see cref="TestPlayers.DryRunThenCommit"/>, stamped now unless <paramref name="at"/> is given.</summary>
        static MetaActionResult Deliver(PlayerModel player, int matchIndex, bool win = true, MetaTime? at = null) =>
            TestPlayers.DryRunThenCommit(player, TestPlayers.MatchResult(matchIndex, at ?? player.CurrentTime, position: win ? 0 : 2));

        static MetaActionResult Claim(PlayerModel player, MissionInstanceId instanceId) =>
            TestPlayers.DryRunThenCommit(player, new PlayerClaimMissionReward(instanceId));

        static MissionRollover Rollover(PlayerModel player, MetaTime? at = null) =>
            player.Missions.RolloverAt(player.GameConfig, new PlayerLocalTime(at ?? player.CurrentTime, player.TimeZoneInfo.CurrentUtcOffset));

        static MissionInstanceId Instance(PlayerModel player, MissionCadence cadence, MissionId mission, MetaTime? at = null) =>
            Rollover(player, at).ActivationOf(cadence).InstanceOf(mission);

        static int ProgressOf(PlayerModel player, MissionCadence cadence, MissionId mission, MetaTime? at = null) =>
            Rollover(player, at).ActivationOf(cadence).Find(mission).Count;

        #endregion

        #region What a player starts with

        [Test]
        public void AFreshPlayerHasThreeDailyAndTwoWeeklyMissionsAtZero()
        {
            // The mission sets are derived from the calendar and the config, so they exist from the first read
            // without any action from the player or an operator.
            PlayerModel     player   = NewPlayer();
            MissionRollover rollover = Rollover(player);

            Assert.That(rollover.Daily.Missions.Select(mission => mission.Id),
                Is.EqualTo(new[] { TestGameConfig.DailyPlay1, TestGameConfig.DailyPlay3, TestGameConfig.DailyWin1 }));
            Assert.That(rollover.Weekly.Missions.Select(mission => mission.Id),
                Is.EqualTo(new[] { TestGameConfig.WeeklyPlay10, TestGameConfig.WeeklyWin3 }));

            Assert.That(rollover.Daily.Missions.Select(mission => mission.Count), Is.All.Zero);
            Assert.That(rollover.Weekly.Missions.Select(mission => mission.Count), Is.All.Zero);
            Assert.That(rollover.DailyLateClaim, Is.Null);
            Assert.That(rollover.WeeklyLateClaim, Is.Null);
        }

        [Test]
        public void ADayRunsFromLocalMidnightToLocalMidnight()
        {
            PlayerModel player = NewPlayer(utcOffset: MetaDuration.FromHours(-5));

            MissionActivation daily = Rollover(player).Daily;

            Assert.That(daily.StartsAt, Is.EqualTo(At(2026, 9, 1, 5, 0)), "midnight in a UTC-5 calendar is 05:00 UTC");
            Assert.That(daily.EndsAt, Is.EqualTo(At(2026, 9, 2, 5, 0)));
        }

        /// <summary>
        /// The daily reward and missions both reset at the start of the player's day, but compute it differently:
        /// the daily reward from a schedule in the config, missions from a calendar defined in code. This test
        /// checks that the two agree.
        /// <para>
        /// They agree because <c>DailyResetScheduleRules</c> rejects any schedule that is not in local time, one
        /// day long, one day apart and starting at local midnight. Any two such schedules split local time into
        /// the same days. This test fails if that validation is relaxed.
        /// </para>
        /// </summary>
        [Test]
        public void TheDailyRewardAndMissionsResetOnTheSameBoundary()
        {
            MetaRecurringCalendarSchedule published = TestGameConfig.DailyReset();

            foreach (int offsetHours in new[] { -11, -5, 0, 3, 8, 13 })
            {
                MetaDuration offset = MetaDuration.FromHours(offsetHours);

                foreach (MetaTime instant in new[] { At(2026, 9, 1, 0, 0), At(2026, 9, 1, 12, 0), At(2026, 9, 1, 23, 59), At(2027, 2, 28, 6, 30) })
                {
                    PlayerLocalTime      at        = new PlayerLocalTime(instant, offset);
                    PlayerCalendarWindow missions  = PlayerCalendar.WindowAt(PlayerCalendar.Daily, at);
                    DailyActivation      dailyReward = DailyRewardCalendar.ActivationAt(published, at);

                    Assert.That(dailyReward.Exists, Is.True, $"{instant} at {offsetHours}h");
                    Assert.That(missions.StartsAt, Is.EqualTo(dailyReward.StartsAt), $"day start disagrees at {instant}, offset {offsetHours}h");
                    Assert.That(missions.EndsAt, Is.EqualTo(dailyReward.EndsAt), $"day end disagrees at {instant}, offset {offsetHours}h");
                }
            }
        }

        [Test]
        public void AWeekBeginsAtMidnightOnMondayInThePlayersOwnCalendar()
        {
            PlayerModel player = NewPlayer();

            Assert.That(Rollover(player).Weekly.StartsAt, Is.EqualTo(MondayMidnight));
            Assert.That(Rollover(player).Weekly.EndsAt, Is.EqualTo(At(2026, 9, 7, 0, 0)), "and ends on the next Monday");
        }

        #endregion

        #region A finished game

        [Test]
        public void OneWinningMatchAdvancesEveryApplicableMissionTogether()
        {
            // Finishing one mission never costs progress on another. One win advances every play and win
            // mission in both cadences.
            PlayerModel player = NewPlayer();

            Deliver(player, matchIndex: 1, win: true);

            Assert.That(ProgressOf(player, MissionCadence.Daily, TestGameConfig.DailyPlay1), Is.EqualTo(1));
            Assert.That(ProgressOf(player, MissionCadence.Daily, TestGameConfig.DailyPlay3), Is.EqualTo(1));
            Assert.That(ProgressOf(player, MissionCadence.Daily, TestGameConfig.DailyWin1), Is.EqualTo(1));
            Assert.That(ProgressOf(player, MissionCadence.Weekly, TestGameConfig.WeeklyPlay10), Is.EqualTo(1));
            Assert.That(ProgressOf(player, MissionCadence.Weekly, TestGameConfig.WeeklyWin3), Is.EqualTo(1));

            Assert.That(Rollover(player).Daily.Find(TestGameConfig.DailyPlay1).IsComplete, Is.True);
            Assert.That(Rollover(player).Daily.Find(TestGameConfig.DailyWin1).IsComplete, Is.True);
            Assert.That(Rollover(player).Daily.Find(TestGameConfig.DailyPlay3).IsComplete, Is.False);
        }

        [Test]
        public void ALossAdvancesTheCompletionMissionsAndNotTheWinMissions()
        {
            PlayerModel player = NewPlayer();

            Deliver(player, matchIndex: 1, win: false);

            Assert.That(ProgressOf(player, MissionCadence.Daily, TestGameConfig.DailyPlay1), Is.EqualTo(1));
            Assert.That(ProgressOf(player, MissionCadence.Daily, TestGameConfig.DailyWin1), Is.Zero);
            Assert.That(ProgressOf(player, MissionCadence.Weekly, TestGameConfig.WeeklyWin3), Is.Zero);
        }

        [Test]
        public void ProgressStopsAtItsTarget()
        {
            PlayerModel player = NewPlayer();

            for (int index = 1; index <= 14; index++)
                Deliver(player, matchIndex: index, win: true);

            Assert.That(ProgressOf(player, MissionCadence.Daily, TestGameConfig.DailyPlay3), Is.EqualTo(3));
            Assert.That(ProgressOf(player, MissionCadence.Weekly, TestGameConfig.WeeklyPlay10), Is.EqualTo(10));
            Assert.That(ProgressOf(player, MissionCadence.Weekly, TestGameConfig.WeeklyWin3), Is.EqualTo(3));
            Assert.That(player.Record.GamesPlayed, Is.EqualTo(14), "the record counts every game whatever the missions do");
        }

        [Test]
        public void ALateRedeliveryCannotRollTheCalendarBackwards()
        {
            // A table whose entry has aged off the match history resends its result days later with its original
            // completion time, so the day it names has already ended. If rollover treated any window other than
            // the stored one as a new window, it would create a zeroed activation for that old day. Instance ids
            // come from the activation start, so the old day would get back the ids already claimed, unclaimed.
            //
            // That would pay one reward twice and wipe the unclaimed rewards of the old day. Client and server
            // would agree, so no desync would report it.
            PlayerModel player = NewPlayer();

            // Day one: win, which finishes two missions, and claim one of them.
            MetaTime dayOne = At(2026, 9, 1);
            Deliver(player, matchIndex: 1, win: true, at: dayOne);

            MissionInstanceId dayOneWin  = Instance(player, MissionCadence.Daily, TestGameConfig.DailyWin1, dayOne);
            MissionInstanceId dayOnePlay = Instance(player, MissionCadence.Daily, TestGameConfig.DailyPlay1, dayOne);

            player.ResetTime(dayOne);
            Assert.That(Claim(player, dayOneWin), Is.EqualTo(MetaActionResult.Success));
            Assert.That(player.Wallet.Coins, Is.EqualTo(3_150));

            // Day two: play enough matches to age match 1 off the history, which also makes progress on day two.
            MetaTime dayTwo = At(2026, 9, 2);
            for (int index = 0; index < MatchHistoryRules.HistoryCapacity; index++)
                Deliver(player, matchIndex: 100 + index, win: false, at: dayTwo);

            Assert.That(player.HasRecordedMatch(MatchTestDeals.MatchId(1)), Is.False, "the entry has to age off for this to be the case under test");
            Assert.That(ProgressOf(player, MissionCadence.Daily, TestGameConfig.DailyPlay3, dayTwo), Is.EqualTo(3));

            // Deliver match 1 again, still timestamped on day one.
            Assert.That(Deliver(player, matchIndex: 1, win: true, at: dayOne), Is.EqualTo(MetaActionResult.Success),
                "the record has to accept it for this to be the case under test");

            player.ResetTime(dayTwo);

            // The daily activation is still day two.
            Assert.That(Rollover(player, dayTwo).Daily.StartsAt, Is.EqualTo(At(2026, 9, 2, 0, 0)));

            // Day two's progress and completed missions are unchanged.
            Assert.That(ProgressOf(player, MissionCadence.Daily, TestGameConfig.DailyPlay3, dayTwo), Is.EqualTo(3));
            Assert.That(Rollover(player, dayTwo).Daily.Find(TestGameConfig.DailyPlay1).IsComplete, Is.True);
            Assert.That(Rollover(player, dayTwo).Daily.Find(TestGameConfig.DailyPlay3).IsComplete, Is.True);

            // Day one's claimed mission cannot be claimed again.
            Assert.That(Claim(player, dayOneWin), Is.EqualTo(ActionResults.NoSuchMission));
            Assert.That(player.Wallet.Coins, Is.EqualTo(3_150), "the reward was paid a second time");
            Assert.That(player.Missions.ClaimCount, Is.EqualTo(1));

            // Day one's unclaimed mission is still complete in the late-claim activation and can be claimed.
            Assert.That(Rollover(player, dayTwo).DailyLateClaim, Is.Not.Null);
            Assert.That(Rollover(player, dayTwo).DailyLateClaim.Find(TestGameConfig.DailyPlay1).IsComplete, Is.True);
            Assert.That(Claim(player, dayOnePlay), Is.EqualTo(MetaActionResult.Success));
            Assert.That(player.Wallet.Coins, Is.EqualTo(3_250));
        }

        [Test]
        public void AGameFromAWindowThePlayerNoLongerHoldsCountsIntoNone()
        {
            // Rollover never goes back to an older window, so a game from a closed window counts into no daily
            // activation. Counting it into today would reward today's missions for an older game.
            PlayerModel player = NewPlayer();

            MetaTime dayOne = At(2026, 9, 1);
            MetaTime dayTwo = At(2026, 9, 2);

            Deliver(player, matchIndex: 1, win: false, at: dayTwo);
            Assert.That(ProgressOf(player, MissionCadence.Daily, TestGameConfig.DailyPlay3, dayTwo), Is.EqualTo(1));

            Deliver(player, matchIndex: 2, win: true, at: dayOne);

            Assert.That(ProgressOf(player, MissionCadence.Daily, TestGameConfig.DailyPlay3, dayTwo), Is.EqualTo(1),
                "a game from a closed day counted into today");
            Assert.That(ProgressOf(player, MissionCadence.Daily, TestGameConfig.DailyWin1, dayTwo), Is.Zero);

            // Both games fall in the current weekly window, so the weekly set counts both.
            Assert.That(ProgressOf(player, MissionCadence.Weekly, TestGameConfig.WeeklyPlay10, dayTwo), Is.EqualTo(2),
                "a cadence whose window still covers the game must still count it");
        }

        #endregion

        #region What reaches the analytics log

        static IReadOnlyList<PlayerEventMissionCompleted> CompletionRows(List<PlayerEventBase> captured) =>
            captured.OfType<PlayerEventMissionCompleted>().ToList();

        [Test]
        public void CrossingATargetIsARowAndAnIntermediateIncrementIsNot()
        {
            // Progress is saved on every game but only a completion writes an analytics event. Analytics needs
            // which missions players finish, not each intermediate count.
            List<PlayerEventBase> captured = new List<PlayerEventBase>();
            PlayerModel           player   = NewPlayer(captured);

            Deliver(player, matchIndex: 1, win: false);
            Assert.That(CompletionRows(captured).Select(row => row.Mission), Is.EqualTo(new[] { TestGameConfig.DailyPlay1 }),
                "only the mission that crossed its target should have written a row");

            captured.Clear();
            Deliver(player, matchIndex: 2, win: false);
            Assert.That(CompletionRows(captured), Is.Empty, "2 of 3 is not an event");

            captured.Clear();
            Deliver(player, matchIndex: 3, win: false);
            Assert.That(CompletionRows(captured).Select(row => row.Mission), Is.EqualTo(new[] { TestGameConfig.DailyPlay3 }));
        }

        [Test]
        public void OneMatchThatFinishesTwoMissionsWritesTwoRows()
        {
            List<PlayerEventBase> captured = new List<PlayerEventBase>();
            PlayerModel           player   = NewPlayer(captured);

            Deliver(player, matchIndex: 1, win: true);

            Assert.That(CompletionRows(captured).Select(row => row.Mission),
                Is.EquivalentTo(new[] { TestGameConfig.DailyPlay1, TestGameConfig.DailyWin1 }));
        }

        [Test]
        public void ACompletionRowExplainsItselfWithoutTheConfigArchive()
        {
            List<PlayerEventBase> captured = new List<PlayerEventBase>();
            PlayerModel           player   = NewPlayer(captured);

            Deliver(player, matchIndex: 1, win: true);

            PlayerEventMissionCompleted row = CompletionRows(captured).Single(entry => entry.Mission == TestGameConfig.DailyWin1);

            Assert.That(row.Instance, Is.EqualTo(Instance(player, MissionCadence.Daily, TestGameConfig.DailyWin1)));
            Assert.That(row.SetId, Is.EqualTo(TestGameConfig.DailySet));
            Assert.That(row.Cadence, Is.EqualTo(MissionCadence.Daily));
            Assert.That(row.Objective, Is.EqualTo(MissionObjective.MatchesWon));
            Assert.That(row.Target, Is.EqualTo(1));
            Assert.That(row.ProgressBefore, Is.Zero);
            Assert.That(row.ProgressAfter, Is.EqualTo(1));
        }

        #endregion

        #region Resets, late claims and the calendar

        [Test]
        public void AMatchOnTheNextLocalDayStartsAFreshDailySetAndUnfinishedProgressExpires()
        {
            PlayerModel player = NewPlayer();

            Deliver(player, matchIndex: 1, win: false);
            Deliver(player, matchIndex: 2, win: false);
            Assert.That(ProgressOf(player, MissionCadence.Daily, TestGameConfig.DailyPlay3), Is.EqualTo(2));

            Deliver(player, matchIndex: 3, win: false, at: At(2026, 9, 2));

            Assert.That(Rollover(player, At(2026, 9, 2)).Daily.StartsAt, Is.EqualTo(At(2026, 9, 2, 0, 0)));
            Assert.That(ProgressOf(player, MissionCadence.Daily, TestGameConfig.DailyPlay3, At(2026, 9, 2)), Is.EqualTo(1),
                "yesterday's two matches must not carry into today");
        }

        [Test]
        public void TheWeeklySetSurvivesADailyReset()
        {
            PlayerModel player = NewPlayer();

            Deliver(player, matchIndex: 1, win: false);
            Deliver(player, matchIndex: 2, win: false, at: At(2026, 9, 2));

            Assert.That(ProgressOf(player, MissionCadence.Weekly, TestGameConfig.WeeklyPlay10, At(2026, 9, 2)), Is.EqualTo(2));
            Assert.That(Rollover(player, At(2026, 9, 2)).Weekly.StartsAt, Is.EqualTo(MondayMidnight));
        }

        [Test]
        public void AFinishedMissionStaysClaimableForTwentyFourHoursAfterItsDayEnds()
        {
            PlayerModel player = NewPlayer();
            Deliver(player, matchIndex: 1, win: true);

            MissionInstanceId yesterday = Instance(player, MissionCadence.Daily, TestGameConfig.DailyWin1);

            // The next day's first game rolls the daily set over. The late-claim activation keeps only the finished,
            // unclaimed missions.
            Deliver(player, matchIndex: 2, win: false, at: At(2026, 9, 2, 1, 0));

            MissionActivation lateClaim = Rollover(player, At(2026, 9, 2, 1, 0)).DailyLateClaim;
            Assert.That(lateClaim, Is.Not.Null);
            Assert.That(lateClaim.Missions.Select(mission => mission.Id),
                Is.EquivalentTo(new[] { TestGameConfig.DailyPlay1, TestGameConfig.DailyWin1 }),
                "only the finished and unclaimed missions carry over");

            player.ResetTime(At(2026, 9, 2, 23, 0));
            Assert.That(Claim(player, yesterday), Is.EqualTo(MetaActionResult.Success));
        }

        [Test]
        public void AFinishedMissionIsGoneOnceTheLateClaimWindowLapses()
        {
            PlayerModel player = NewPlayer();
            Deliver(player, matchIndex: 1, win: true);

            MissionInstanceId yesterday = Instance(player, MissionCadence.Daily, TestGameConfig.DailyWin1);

            // The mission expires exactly at the claim deadline, which is the end of its day plus the late-claim window.
            player.ResetTime(At(2026, 9, 3, 0, 0) - MetaDuration.FromMinutes(1));
            Assert.That(player.Missions.ResolveClaim(player.GameConfig, yesterday, player.CurrentTime, out MissionClaim _),
                Is.EqualTo(MetaActionResult.Success), "one minute before the deadline it is still claimable");

            player.ResetTime(At(2026, 9, 3, 0, 0));
            Assert.That(Claim(player, yesterday), Is.EqualTo(ActionResults.MissionExpired));
            Assert.That(player.Wallet.Coins, Is.EqualTo(3_000));
        }

        [Test]
        public void TheLateClaimWindowNeverOutlastsTheWindowItFollows()
        {
            // Only one previous activation is kept. If the late-claim window were longer than the following window, the
            // snapshot would be dropped while still claimable and an earned reward would silently disappear. So
            // the late-claim window is capped at the length of the window it follows.
            MetaTime start = At(2026, 9, 1, 0, 0);

            MissionActivation day = new MissionActivation(
                MissionCadence.Daily,
                new PlayerCalendarWindow(start, start + MetaDuration.FromDays(1)),
                TestGameConfig.DailySet,
                new[] { TestGameConfig.DailyPlay1 });

            Assert.That(day.ClaimDeadline, Is.EqualTo(start + MetaDuration.FromDays(2)),
                "a 24-hour day and a 24-hour late-claim window meet exactly at the following day's end");

            MissionActivation halfDay = new MissionActivation(
                MissionCadence.Daily,
                new PlayerCalendarWindow(start, start + MetaDuration.FromHours(12)),
                TestGameConfig.DailySet,
                new[] { TestGameConfig.DailyPlay1 });

            Assert.That(halfDay.ClaimDeadline, Is.EqualTo(start + MetaDuration.FromHours(24)),
                "a shorter published window shortens the late-claim window rather than being quietly lossy");

            MissionActivation week = new MissionActivation(
                MissionCadence.Weekly,
                new PlayerCalendarWindow(start, start + MetaDuration.FromDays(7)),
                TestGameConfig.WeeklySet,
                new[] { TestGameConfig.WeeklyPlay10 });

            Assert.That(week.ClaimDeadline, Is.EqualTo(start + MetaDuration.FromDays(7) + PlayerMissionState.LateClaimWindow),
                "a window longer than the late-claim window is not shortened by the cap");
        }

        [Test]
        public void OnlyOnePreviousActivationIsKept()
        {
            PlayerModel player = NewPlayer();

            Deliver(player, matchIndex: 1, win: true);
            Deliver(player, matchIndex: 2, win: true, at: At(2026, 9, 2));
            Deliver(player, matchIndex: 3, win: true, at: At(2026, 9, 3));

            MissionActivation lateClaim = Rollover(player, At(2026, 9, 3)).DailyLateClaim;

            Assert.That(lateClaim, Is.Not.Null);
            Assert.That(lateClaim.StartsAt, Is.EqualTo(At(2026, 9, 2, 0, 0)), "the late-claim snapshot is the immediately previous day and no older one");
            Assert.That(player.Missions.StoredDailyLateClaim.StartsAt, Is.EqualTo(At(2026, 9, 2, 0, 0)));
        }

        /// <summary>
        /// The server rolls over as soon as a match result arrives. A claim the client issued just before the new
        /// day can execute on the server after that rollover. The server must still be able to pay from the
        /// replaced snapshot, or the client pays and the server does not.
        /// </summary>
        [Test]
        public void AClaimIssuedJustBeforeARolloverPaysOnTheServerToo()
        {
            MetaTime lateOnDayTwo = At(2026, 9, 2, 23, 59);
            MissionInstanceId dayOneWin = MissionInstanceId.Create(MissionCadence.Daily, At(2026, 9, 1, 0, 0), TestGameConfig.DailyWin1);

            PlayerModel client = NewPlayer();
            PlayerModel server = NewPlayer();
            foreach (PlayerModel player in new[] { client, server })
            {
                Deliver(player, matchIndex: 1, win: true);
                Deliver(player, matchIndex: 2, win: true, at: At(2026, 9, 2));
                player.ResetTime(lateOnDayTwo);
            }

            // Only the server has received the game that finished just after midnight.
            Deliver(server, matchIndex: 3, win: true, at: At(2026, 9, 3, 0, 1));

            Assert.That(Claim(client, dayOneWin), Is.EqualTo(MetaActionResult.Success));
            Assert.That(Claim(server, dayOneWin), Is.EqualTo(MetaActionResult.Success),
                "the server dropped the snapshot the client claimed from, so the wallets differ");
            Assert.That(server.Wallet.Coins, Is.EqualTo(client.Wallet.Coins));
        }

        [Test]
        public void ARetiredSnapshotIsDroppedAtTheNextRollover()
        {
            PlayerModel player = NewPlayer();

            Deliver(player, matchIndex: 1, win: true);
            Deliver(player, matchIndex: 2, win: true, at: At(2026, 9, 2));
            Deliver(player, matchIndex: 3, win: true, at: At(2026, 9, 3));

            Assert.That(player.Missions.StoredDailyRetired.StartsAt, Is.EqualTo(At(2026, 9, 1, 0, 0)));

            Deliver(player, matchIndex: 4, win: true, at: At(2026, 9, 4));

            Assert.That(player.Missions.StoredDailyRetired.StartsAt, Is.EqualTo(At(2026, 9, 2, 0, 0)),
                "only the snapshot one rollover behind the late-claim one is kept");
        }

        [Test]
        public void AYearAwayCostsOneRolloverAndCarriesNothingBack()
        {
            // Rollover compares the stored window with the current calendar window and does not step through the
            // days in between, so returning after a year costs the same as returning after an hour.
            PlayerModel player = NewPlayer();
            Deliver(player, matchIndex: 1, win: true);

            MetaTime        muchLater = At(2027, 9, 1);
            MissionRollover rollover  = Rollover(player, muchLater);

            Assert.That(rollover.Daily.StartsAt, Is.EqualTo(At(2027, 9, 1, 0, 0)));
            Assert.That(rollover.Daily.Missions.Select(mission => mission.Count), Is.All.Zero);

            // Rollover keeps at most one late-claim snapshot and does not read the clock, so the year-old snapshot is
            // still stored. It is not claimable: the claim action and the screen check expiry against the model's
            // current time.
            Assert.That(rollover.DailyLateClaim, Is.Not.Null);
            Assert.That(rollover.DailyLateClaim.StartsAt, Is.EqualTo(At(2026, 9, 1, 0, 0)), "and it is the one immediately behind, not an older one");

            player.ResetTime(muchLater);
            Assert.That(Claim(player, MissionInstanceId.Create(MissionCadence.Daily, At(2026, 9, 1, 0, 0), TestGameConfig.DailyWin1)),
                Is.EqualTo(ActionResults.MissionExpired));
            Assert.That(player.Wallet.Coins, Is.EqualTo(3_000));
        }

        [Test]
        public void TwoPlayersInDifferentCalendarsCrossTheDayBoundaryAtDifferentInstants()
        {
            // This is why the match-completion fact carries the completion time in the player's own calendar: the
            // same two games fall on one day for one player and on two days for the other.
            PlayerModel utc     = NewPlayer();
            PlayerModel western = NewPlayer(utcOffset: MetaDuration.FromHours(-5));

            MetaTime morning = At(2026, 9, 1, 12, 0);
            MetaTime night   = At(2026, 9, 2, 2, 0);

            foreach (PlayerModel player in new[] { utc, western })
            {
                Deliver(player, matchIndex: 1, win: false, at: morning);
                Deliver(player, matchIndex: 2, win: false, at: night);
            }

            Assert.That(ProgressOf(utc, MissionCadence.Daily, TestGameConfig.DailyPlay3, night), Is.EqualTo(1),
                "02:00 UTC is already the next day in a UTC calendar");
            Assert.That(ProgressOf(western, MissionCadence.Daily, TestGameConfig.DailyPlay3, night), Is.EqualTo(2),
                "02:00 UTC is still the evening before in a UTC-5 calendar");
        }

        #endregion

        #region Claiming

        [Test]
        public void AClaimPaysTheExactConfiguredRewardAndMarksTheMissionClaimed()
        {
            PlayerModel player = NewPlayer();
            Deliver(player, matchIndex: 1, win: true);

            Assert.That(Claim(player, Instance(player, MissionCadence.Daily, TestGameConfig.DailyWin1)), Is.EqualTo(MetaActionResult.Success));

            Assert.That(player.Wallet.Coins, Is.EqualTo(3_150));
            Assert.That(Rollover(player).Daily.Find(TestGameConfig.DailyWin1).IsClaimed, Is.True);
            Assert.That(Rollover(player).Daily.Find(TestGameConfig.DailyPlay1).IsClaimed, Is.False, "one claim pays one mission");
            Assert.That(player.Missions.ClaimCount, Is.EqualTo(1));
        }

        [Test]
        public void AReplayedClaimPaysOnce()
        {
            // Duplicate taps, replayed actions and retries after a reconnect all execute the same action again.
            // The commit that pays the reward also marks the mission claimed, and the next claim checks that mark
            // first.
            PlayerModel player = NewPlayer();
            Deliver(player, matchIndex: 1, win: true);

            MissionInstanceId instance = Instance(player, MissionCadence.Daily, TestGameConfig.DailyWin1);

            Assert.That(Claim(player, instance), Is.EqualTo(MetaActionResult.Success));
            Assert.That(Claim(player, instance), Is.EqualTo(ActionResults.MissionAlreadyClaimed));
            Assert.That(Claim(player, instance), Is.EqualTo(ActionResults.MissionAlreadyClaimed));

            Assert.That(player.Wallet.Coins, Is.EqualTo(3_150));
            Assert.That(player.Missions.ClaimCount, Is.EqualTo(1));
        }

        [Test]
        public void AnUnfinishedMissionCannotBeClaimed()
        {
            PlayerModel player = NewPlayer();
            Deliver(player, matchIndex: 1, win: true);

            Assert.That(Claim(player, Instance(player, MissionCadence.Daily, TestGameConfig.DailyPlay3)), Is.EqualTo(ActionResults.MissionNotComplete));
            Assert.That(player.Wallet.Coins, Is.EqualTo(3_000));
            Assert.That(player.Missions.ClaimCount, Is.Zero);
        }

        [Test]
        public void AnInstanceThePlayerDoesNotHoldIsRefused()
        {
            PlayerModel player = NewPlayer();

            Assert.That(Claim(player, MissionInstanceId.FromString("Daily:0:nothing")), Is.EqualTo(ActionResults.NoSuchMission));
            Assert.That(Claim(player, null), Is.EqualTo(ActionResults.NoSuchMission));
            Assert.That(player.Wallet.Coins, Is.EqualTo(3_000));
        }

        [Test]
        public void YesterdaysCardCannotClaimTodaysMission()
        {
            // The instance id includes its activation, so a stale screen holding yesterday's id cannot claim the
            // same mission again today.
            PlayerModel player = NewPlayer();
            Deliver(player, matchIndex: 1, win: true);

            MissionInstanceId yesterday = Instance(player, MissionCadence.Daily, TestGameConfig.DailyWin1);
            Assert.That(Claim(player, yesterday), Is.EqualTo(MetaActionResult.Success));

            Deliver(player, matchIndex: 2, win: true, at: At(2026, 9, 2));
            player.ResetTime(At(2026, 9, 2));

            MissionInstanceId today = Instance(player, MissionCadence.Daily, TestGameConfig.DailyWin1);
            Assert.That(today, Is.Not.EqualTo(yesterday), "the same definition on a new day is a new instance");

            // Rollover does not carry a claimed mission into the late-claim activation, so yesterday's instance no
            // longer exists.
            Assert.That(Claim(player, yesterday), Is.EqualTo(ActionResults.NoSuchMission));
            Assert.That(Claim(player, today), Is.EqualTo(MetaActionResult.Success));
            Assert.That(player.Missions.ClaimCount, Is.EqualTo(2));
        }

        [Test]
        public void AWalletThatRefusesLeavesTheMissionReadyAndUnclaimed()
        {
            // The mission is marked claimed only after the wallet accepts the grant, so a refused grant leaves the
            // reward for the player to collect later.
            PlayerModel player = NewPlayer(config: TestGameConfig.Build(new RewardBundle(CurrencyAmount.Coins(TestGameConfig.MaxCoins))));
            Deliver(player, matchIndex: 1, win: true);

            MissionInstanceId instance = Instance(player, MissionCadence.Daily, TestGameConfig.DailyWin1);

            Assert.That(Claim(player, instance), Is.EqualTo(ActionResults.WalletCapExceeded));
            Assert.That(player.Wallet.Coins, Is.EqualTo(TestGameConfig.MaxCoins));
            Assert.That(Rollover(player).Daily.Find(TestGameConfig.DailyWin1).IsClaimed, Is.False);
            Assert.That(player.Missions.ClaimCount, Is.Zero);
        }

        [Test]
        public void AClaimWritesItsOwnRowAndTheWalletRowsUnderOneCorrelationKey()
        {
            List<PlayerEventBase> captured = new List<PlayerEventBase>();
            PlayerModel           player   = NewPlayer(captured);

            Deliver(player, matchIndex: 1, win: true);
            captured.Clear();

            Claim(player, Instance(player, MissionCadence.Daily, TestGameConfig.DailyWin1));

            PlayerEventMissionRewardClaimed claimed = captured.OfType<PlayerEventMissionRewardClaimed>().Single();
            PlayerEventEconomyTransaction   money   = captured.OfType<PlayerEventEconomyTransaction>().Single();

            Assert.That(claimed.Mission, Is.EqualTo(TestGameConfig.DailyWin1));
            Assert.That(claimed.SetId, Is.EqualTo(TestGameConfig.DailySet));
            Assert.That(claimed.Cadence, Is.EqualTo(MissionCadence.Daily));
            Assert.That(claimed.ClaimOrdinal, Is.EqualTo(1));
            Assert.That(claimed.InGrace, Is.False);

            Assert.That(money.Currency, Is.EqualTo(CurrencyType.Coins));
            Assert.That(money.Flow, Is.EqualTo(CurrencyFlow.Source));
            Assert.That(money.Amount, Is.EqualTo(150));
            Assert.That(money.Reason, Is.EqualTo(EconomyReason.MissionReward));
            Assert.That(money.Feature, Is.EqualTo(EconomyFeature.Missions));
            Assert.That(money.ContentId, Is.EqualTo(EconomyContentId.FromString(TestGameConfig.DailyWin1.Value)));

            Assert.That(money.Correlation, Is.EqualTo(claimed.Correlation), "the row and the money it moved must join");
            Assert.That(claimed.Correlation.IsSet, Is.True);
        }

        [Test]
        public void AClaimInsideTheLateClaimWindowSaysSo()
        {
            List<PlayerEventBase> captured = new List<PlayerEventBase>();
            PlayerModel           player   = NewPlayer(captured);

            Deliver(player, matchIndex: 1, win: true);
            MissionInstanceId yesterday = Instance(player, MissionCadence.Daily, TestGameConfig.DailyWin1);

            captured.Clear();
            player.ResetTime(At(2026, 9, 2, 10, 0));
            Assert.That(Claim(player, yesterday), Is.EqualTo(MetaActionResult.Success));

            Assert.That(captured.OfType<PlayerEventMissionRewardClaimed>().Single().InGrace, Is.True);
        }

        #endregion

        #region What the sets are worth

        [Test]
        public void SevenPerfectDaysAndOneWeekPayExactlyTwoThousandEightHundredCoinsAndTwoSpinTokens()
        {
            // Checks the economy's totals for a full week. Three matches a day with one win finishes every daily
            // mission, and seven such days finish every weekly mission.
            PlayerModel player = NewPlayer();
            int         match  = 0;

            for (int day = 0; day < 7; day++)
            {
                MetaTime noon = MondayMidnight + MetaDuration.FromHours(12) + MetaDuration.FromDays(day);
                player.ResetTime(noon);

                Deliver(player, matchIndex: ++match, win: true, at: noon);
                Deliver(player, matchIndex: ++match, win: false, at: noon);
                Deliver(player, matchIndex: ++match, win: false, at: noon);

                foreach (MissionId mission in new[] { TestGameConfig.DailyPlay1, TestGameConfig.DailyPlay3, TestGameConfig.DailyWin1 })
                    Assert.That(Claim(player, Instance(player, MissionCadence.Daily, mission)), Is.EqualTo(MetaActionResult.Success), $"day {day}, {mission}");
            }

            foreach (MissionId mission in new[] { TestGameConfig.WeeklyPlay10, TestGameConfig.WeeklyWin3 })
                Assert.That(Claim(player, Instance(player, MissionCadence.Weekly, mission)), Is.EqualTo(MetaActionResult.Success), mission.Value);

            Assert.That(player.Wallet.Coins - 3_000, Is.EqualTo(2_800));
            Assert.That(player.Wallet.SpinTokens - 1, Is.EqualTo(2));
            Assert.That(player.Wallet.Gems, Is.EqualTo(100), "missions pay no gems");
            Assert.That(player.Missions.ClaimCount, Is.EqualTo(23));
        }

        #endregion

        #region Missions cannot break the game

        [Test]
        public void AMissingMissionSetLeavesTheGameOnTheRecordAndTheMissionsUntouched()
        {
            // A broken optional feature must never invalidate a finished match. This matters because the mission
            // update runs inside the request that acknowledges the table's result.
            PlayerModel player = NewPlayer(config: TestGameConfig.BuildWithoutMissions());

            Assert.That(Deliver(player, matchIndex: 1, win: true), Is.EqualTo(MetaActionResult.Success));

            Assert.That(player.Record.GamesPlayed, Is.EqualTo(1));
            Assert.That(player.MatchHistory, Has.Count.EqualTo(1));
            Assert.That(player.Missions.StoredDaily, Is.Null, "nothing was written, rather than half of something");
            Assert.That(player.Missions.StoredWeekly, Is.Null);
        }

        [Test]
        public void AMissionSetThatComesBackStartsCountingAgain()
        {
            SharedGameConfig config = TestGameConfig.BuildWithoutMissions();
            PlayerModel      player = NewPlayer(config: config);

            Deliver(player, matchIndex: 1, win: true);

            TestGameConfig.SetMissions(config, TestGameConfig.Missions(), TestGameConfig.MissionSets());
            Deliver(player, matchIndex: 2, win: true);

            Assert.That(ProgressOf(player, MissionCadence.Daily, TestGameConfig.DailyPlay3), Is.EqualTo(1),
                "the game played while the config was missing is not replayed, and the next one counts");
        }

        [Test]
        public void PublishingANewSetDoesNotChangeAnActivationAlreadyUnderWay()
        {
            MissionId          replacement = MissionId.FromString("daily.win2");
            MissionSetId       version2    = MissionSetId.FromString("missions.daily.v2");
            List<MissionInfo>  missions    = TestGameConfig.Missions();
            missions.Add(new MissionInfo(replacement, MissionCadence.Daily, MissionObjective.MatchesWon, 2, new RewardBundle(CurrencyAmount.Coins(150))));

            List<MissionSetInfo> sets = TestGameConfig.MissionSets();
            sets.Add(new MissionSetInfo(version2, MissionCadence.Daily,
                new List<MissionId> { TestGameConfig.DailyPlay1, TestGameConfig.DailyPlay3, replacement }));

            SharedGameConfig config = new SharedGameConfig();
            TestGameConfig.SetEntry(config, "Global", TestGameConfig.GlobalWith(TestGameConfig.StartingWallet()));
            TestGameConfig.SetMissions(config, missions, sets);

            PlayerModel player = NewPlayer(config: config);
            Deliver(player, matchIndex: 1, win: true);

            // Publish a config that makes version2 the active daily set while the player is part-way through a
            // day on the old set.
            TestGameConfig.SetEntry(config, "Global", TestGameConfig.GlobalWith(TestGameConfig.StartingWallet(), version2, TestGameConfig.WeeklySet));

            Assert.That(Rollover(player).Daily.SetId, Is.EqualTo(TestGameConfig.DailySet), "today's set is the one the day began with");
            Assert.That(Rollover(player).Daily.Find(TestGameConfig.DailyWin1), Is.Not.Null);
            Assert.That(Rollover(player).Daily.Find(TestGameConfig.DailyWin1).IsComplete, Is.True, "and the progress made against it stands");

            // The next day uses the new set.
            MissionRollover tomorrow = Rollover(player, At(2026, 9, 2));
            Assert.That(tomorrow.Daily.SetId, Is.EqualTo(version2));
            Assert.That(tomorrow.Daily.Find(replacement), Is.Not.Null);
            Assert.That(tomorrow.Daily.Find(TestGameConfig.DailyWin1), Is.Null);

            // The previous day's finished, unclaimed reward is still claimable after the change.
            Assert.That(tomorrow.DailyLateClaim, Is.Not.Null);
            player.ResetTime(At(2026, 9, 2));
            Assert.That(Claim(player, MissionInstanceId.Create(MissionCadence.Daily, At(2026, 9, 1, 0, 0), TestGameConfig.DailyWin1)),
                Is.EqualTo(MetaActionResult.Success));
        }

        #endregion

        #region The instance id

        [Test]
        public void AnInstanceIdIsStableWithinItsActivationAndDifferentAcrossThem()
        {
            PlayerModel player = NewPlayer();

            MissionInstanceId first  = Instance(player, MissionCadence.Daily, TestGameConfig.DailyWin1);
            Deliver(player, matchIndex: 1, win: true);
            MissionInstanceId second = Instance(player, MissionCadence.Daily, TestGameConfig.DailyWin1);

            Assert.That(second, Is.EqualTo(first), "progress does not change which instance this is");
            Assert.That(Instance(player, MissionCadence.Daily, TestGameConfig.DailyWin1, At(2026, 9, 2)), Is.Not.EqualTo(first));
            Assert.That(Instance(player, MissionCadence.Weekly, TestGameConfig.WeeklyWin3), Is.Not.EqualTo(first));
        }

        #endregion
    }
}
