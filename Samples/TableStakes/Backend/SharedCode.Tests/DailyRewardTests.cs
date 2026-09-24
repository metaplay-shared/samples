using Metaplay.Core;
using Metaplay.Core.Analytics;
using Metaplay.Core.Config;
using Metaplay.Core.Model;
using Metaplay.Core.Player;
using Metaplay.Core.Schedule;
using Metaplay.Core.Serialization;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.Logic.Tests
{
    /// <summary>
    /// Tests <see cref="DailyRewardCalendar"/>, which maps a player-local time to the daily reward's activation.
    /// Each test passes an explicit time instead of waiting for a day boundary (<c>docs/testing.md</c>,
    /// "Forcing timers").
    /// </summary>
    [TestFixture]
    public class DailyRewardCalendarTests
    {
        static readonly MetaRecurringCalendarSchedule Schedule = TestGameConfig.DailyReset();

        static PlayerLocalTime At(int year, int month, int day, int hour, int minute) =>
            new PlayerLocalTime(MetaTime.FromDateTime(new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Utc)), MetaDuration.Zero);

        static int IndexAt(PlayerLocalTime now) => DailyRewardCalendar.ActivationAt(Schedule, now).Index;

        [Test]
        public void EveryMomentOfOneLocalDayIsTheSameActivation()
        {
            int morning  = IndexAt(At(2026, 3, 10, 0, 0));
            int midday   = IndexAt(At(2026, 3, 10, 12, 0));
            int lastTick = IndexAt(At(2026, 3, 10, 23, 59));

            Assert.That(midday, Is.EqualTo(morning));
            Assert.That(lastTick, Is.EqualTo(morning));
        }

        [Test]
        public void LocalMidnightMovesTheActivationOnByExactlyOne()
        {
            Assert.That(IndexAt(At(2026, 3, 11, 0, 0)) - IndexAt(At(2026, 3, 10, 23, 59)), Is.EqualTo(1));
        }

        /// <summary>
        /// The same instant falls on different days for players in different time zones. The schedule uses
        /// <see cref="MetaScheduleTimeMode.Local"/> so that the reset happens at each player's own midnight.
        /// </summary>
        [Test]
        public void TwoTimeZonesAtOneInstantAreOnDifferentLocalDays()
        {
            MetaTime instant = MetaTime.FromDateTime(new DateTime(2026, 3, 10, 23, 30, 0, DateTimeKind.Utc));

            int aucklandDay = DailyRewardCalendar.ActivationAt(Schedule, new PlayerLocalTime(instant, MetaDuration.FromHours(13))).Index;
            int hawaiiDay   = DailyRewardCalendar.ActivationAt(Schedule, new PlayerLocalTime(instant, MetaDuration.FromHours(-10))).Index;

            Assert.That(aucklandDay - hawaiiDay, Is.EqualTo(1), "Auckland is already on the 11th while Hawaii is still on the 10th");
        }

        /// <summary>
        /// A daylight-saving change moves the player's UTC offset by an hour. The activation is identified by its
        /// local date, so the change does not move the player to another day or change their streak.
        /// </summary>
        [Test]
        public void AnHourOfDaylightSavingDoesNotChangeWhichDayItIs()
        {
            MetaTime middayLocal = MetaTime.FromDateTime(new DateTime(2026, 3, 29, 11, 0, 0, DateTimeKind.Utc));

            int beforeTheChange = DailyRewardCalendar.ActivationAt(Schedule, new PlayerLocalTime(middayLocal, MetaDuration.FromHours(1))).Index;
            int afterTheChange  = DailyRewardCalendar.ActivationAt(Schedule, new PlayerLocalTime(middayLocal, MetaDuration.FromHours(2))).Index;

            Assert.That(afterTheChange, Is.EqualTo(beforeTheChange));
        }

        /// <summary>
        /// The activation ends at the next local midnight. The countdown is computed from the activation's end
        /// time rather than stored, so it stays correct across a reconnect.
        /// </summary>
        [Test]
        public void TheNextRewardUnlocksAtTheNextLocalMidnight()
        {
            PlayerLocalTime now        = At(2026, 3, 10, 14, 0);
            DailyActivation activation = DailyRewardCalendar.ActivationAt(Schedule, now);

            Assert.That(activation.Exists, Is.True);
            Assert.That(activation.EndsAt - now.Time, Is.EqualTo(MetaDuration.FromHours(10)));
            Assert.That(activation.StartsAt, Is.EqualTo(MetaTime.FromDateTime(new DateTime(2026, 3, 10, 0, 0, 0, DateTimeKind.Utc))));
        }

        /// <summary>
        /// The activation index counts days from 1970-01-01, not from the schedule's published start date.
        /// <para>
        /// Stored claims depend on this. The start date is a <c>[MetaMember]</c> of the published config. If the
        /// index counted from it, publishing a later start date would put every new index below the stored ones,
        /// and <see cref="DailyRewardPolicy.TransitionFor"/> would report "already claimed" to every player who
        /// has claimed before. The config build cannot detect that, because it does not see the previously
        /// published archive.
        /// </para>
        /// </summary>
        [Test]
        public void TheDayNumberIsAnchoredToTheEpochAndNotToThePublishedSchedule()
        {
            PlayerLocalTime noon = At(2026, 3, 10, 12, 0);

            // 2026-03-10 is 20,522 days after 1970-01-01. The literal pins the epoch anchor.
            Assert.That(IndexAt(noon), Is.EqualTo(20_522));

            // Schedules with different start dates give the same index for the same moment.
            MetaRecurringCalendarSchedule later = Anchored(2026, 1, 1);
            MetaRecurringCalendarSchedule older = Anchored(2015, 6, 30);

            Assert.That(DailyRewardCalendar.ActivationAt(later, noon).Index, Is.EqualTo(IndexAt(noon)),
                "moving the published anchor forward renumbered the day, which would strand every stored claim");
            Assert.That(DailyRewardCalendar.ActivationAt(older, noon).Index, Is.EqualTo(IndexAt(noon)),
                "moving the published anchor back renumbered the day, which would break every live streak once");
        }

        /// <summary>Builds the daily reset schedule with the given start date.</summary>
        static MetaRecurringCalendarSchedule Anchored(int year, int month, int day) =>
            new MetaRecurringCalendarSchedule(
                timeMode:   MetaScheduleTimeMode.Local,
                start:      new MetaCalendarDateTime(year, month, day, 0, 0, 0),
                duration:   DailyResetScheduleRules.OneDay,
                endingSoon: new MetaCalendarPeriod(),
                preview:    new MetaCalendarPeriod(),
                review:     new MetaCalendarPeriod(),
                recurrence: DailyResetScheduleRules.OneDay,
                numRepeats: null);

        /// <summary>A null schedule, which the config build refuses, produces no activation.</summary>
        [Test]
        public void AMissingScheduleProducesNoActivation()
        {
            Assert.That(DailyRewardCalendar.ActivationAt(null, At(2026, 3, 10, 12, 0)).Exists, Is.False);
        }

        [Test]
        public void AMomentBeforeTheScheduleBeginsHasNoActivation()
        {
            // The epoch numbers the days, but the schedule's start date still decides whether an activation exists.
            Assert.That(DailyRewardCalendar.ActivationAt(Schedule, At(2019, 6, 1, 12, 0)).Exists, Is.False);
        }
    }

    /// <summary>
    /// Tests the streak, the skip day and the step cycle in <see cref="DailyRewardPolicy"/>.
    /// <para>
    /// The policy takes a state and an activation index, so each rule is tested with plain integers and no
    /// calendar.
    /// </para>
    /// </summary>
    [TestFixture]
    public class DailyRewardStreakTests
    {
        static readonly DailyRewardTableInfo Table = TestGameConfig.DailyRewardTable();

        /// <summary>Returns a state whose last claim was <paramref name="step"/> on <paramref name="activation"/>.</summary>
        static DailyRewardState StateAfterClaim(int activation, int step, int streak, bool skipDayUsed)
        {
            DailyRewardState state = new DailyRewardState();

            state.Apply(
                new DailyRewardClaim(DailyStreakTransition.Continued, activation, step, Table.StepAt(step).Id, Table.StepAt(step).Reward, streak - 1, streak, skipDayUsed),
                MetaTime.Epoch,
                TestGameConfig.DailyTable);

            return state;
        }

        static DailyRewardClaim Claim(DailyRewardState state, int activation) =>
            DailyRewardPolicy.ClaimFor(state, Table, activation, out DailyRewardRefusal _);

        [Test]
        public void APlayerWhoHasNeverClaimedStartsAtStepOne()
        {
            DailyRewardClaim claim = Claim(new DailyRewardState(), activation: 20_000);

            Assert.That(claim.Transition, Is.EqualTo(DailyStreakTransition.First));
            Assert.That(claim.Step, Is.EqualTo(1));
            Assert.That(claim.StreakAfter, Is.EqualTo(1));
            Assert.That(claim.Reward.AmountOf(CurrencyType.Coins), Is.EqualTo(150));
        }

        [Test]
        public void TheNextDayAdvancesTheStepAndTheStreak()
        {
            DailyRewardClaim claim = Claim(StateAfterClaim(activation: 100, step: 3, streak: 3, skipDayUsed: false), activation: 101);

            Assert.That(claim.Transition, Is.EqualTo(DailyStreakTransition.Continued));
            Assert.That(claim.Step, Is.EqualTo(4));
            Assert.That(claim.StreakAfter, Is.EqualTo(4));
        }

        /// <summary>
        /// The last claimed day cannot be claimed again. A clock that goes backwards, for example after a device
        /// time change, produces an activation before the last claimed one, which is refused too, because paying
        /// it would grant a day that was already paid.
        /// </summary>
        [TestCase(100)]
        [TestCase(97)]
        public void TheLastClaimedDayOrAnEarlierOneIsNotAClaim(int activation)
        {
            DailyRewardClaim claim = DailyRewardPolicy.ClaimFor(
                StateAfterClaim(activation: 100, step: 3, streak: 3, skipDayUsed: false), Table, activation, out DailyRewardRefusal refusal);

            Assert.That(claim.Exists, Is.False);
            Assert.That(refusal, Is.EqualTo(DailyRewardRefusal.AlreadyClaimed));
        }

        [Test]
        public void OneMissedDayIsBridgedByTheSkipDay()
        {
            DailyRewardClaim claim = Claim(StateAfterClaim(activation: 100, step: 3, streak: 3, skipDayUsed: false), activation: 102);

            Assert.That(claim.Transition, Is.EqualTo(DailyStreakTransition.Bridged));
            Assert.That(claim.UsesSkipDay, Is.True);
            Assert.That(claim.Step, Is.EqualTo(4), "the skip day bridges the gap; it does not pay the missed day");
            Assert.That(claim.StreakAfter, Is.EqualTo(4));
            Assert.That(claim.SkipDayUsedAfter, Is.True);
        }

        /// <summary>
        /// A second missed day in the same cycle resets the streak, and so do two missed days in a row even with
        /// the skip day still in hand.
        /// </summary>
        [TestCase(4, true, 102)]
        [TestCase(3, false, 103)]
        public void AGapTheSkipDayCannotBridgeResetsTheStreak(int step, bool skipDayUsed, int activation)
        {
            DailyRewardClaim claim = Claim(StateAfterClaim(activation: 100, step: step, streak: step, skipDayUsed: skipDayUsed), activation);

            Assert.That(claim.Transition, Is.EqualTo(DailyStreakTransition.Reset));
            Assert.That(claim.Step, Is.EqualTo(1));
            Assert.That(claim.StreakAfter, Is.EqualTo(1));
            Assert.That(claim.SkipDayUsedAfter, Is.False, "a reset starts a new cycle, which has its own skip day");
        }

        [Test]
        public void CompletingTheCycleRestoresTheSkipDayAndWrapsTheStep()
        {
            DailyRewardClaim seventh = Claim(StateAfterClaim(activation: 100, step: 6, streak: 6, skipDayUsed: true), activation: 101);

            Assert.That(seventh.Step, Is.EqualTo(7));
            Assert.That(seventh.CompletesCycle, Is.True);
            Assert.That(seventh.SkipDayUsedAfter, Is.False, "finishing a cycle hands the next one a fresh skip day");

            DailyRewardClaim eighth = Claim(StateAfterClaim(activation: 101, step: 7, streak: 7, skipDayUsed: false), activation: 102);

            Assert.That(eighth.Step, Is.EqualTo(1), "the cycle repeats");
            Assert.That(eighth.StreakAfter, Is.EqualTo(8), "the login streak does not");
        }

        /// <summary>
        /// A seventh claim that bridges a missed day uses the skip day and, because it completes the cycle, also
        /// restores it. The claim ends with the skip day available.
        /// </summary>
        [Test]
        public void ABridgedSeventhClaimStillRestoresTheSkipDay()
        {
            DailyRewardClaim claim = Claim(StateAfterClaim(activation: 100, step: 6, streak: 6, skipDayUsed: false), activation: 102);

            Assert.That(claim.UsesSkipDay, Is.True);
            Assert.That(claim.Step, Is.EqualTo(7));
            Assert.That(claim.SkipDayUsedAfter, Is.False);
        }

        /// <summary>Consecutive claims walk every step of the test table once, in order.</summary>
        [Test]
        public void SevenConsecutiveClaimsWalkThePublishedCycle()
        {
            DailyRewardState state = new DailyRewardState();

            List<int> coins  = new List<int>();
            int       tokens = 0;

            for (int day = 0; day < DailyRewardTableInfo.NumSteps; day++)
            {
                DailyRewardClaim claim = Claim(state, activation: 200 + day);

                Assert.That(claim.Step, Is.EqualTo(day + 1));
                Assert.That(claim.StreakAfter, Is.EqualTo(day + 1));

                coins.Add(claim.Reward.AmountOf(CurrencyType.Coins));
                tokens += claim.Reward.AmountOf(CurrencyType.SpinTokens);

                state.Apply(claim, MetaTime.Epoch, TestGameConfig.DailyTable);
            }

            // The step values of TestGameConfig.DailyRewardTable.
            Assert.That(coins, Is.EqualTo(new[] { 150, 170, 190, 210, 240, 270, 330 }));
            Assert.That(tokens, Is.EqualTo(1));
        }

        [Test]
        public void AMissingTableIsRefusedRatherThanGuessedAt()
        {
            DailyRewardClaim claim = DailyRewardPolicy.ClaimFor(new DailyRewardState(), null, activation: 100, out DailyRewardRefusal refusal);

            Assert.That(claim.Exists, Is.False);
            Assert.That(refusal, Is.EqualTo(DailyRewardRefusal.ConfigUnavailable));
        }
    }

    /// <summary>
    /// Tests <see cref="PlayerDailyRewardClaimed"/> on a player model: the grant, the state it writes, the events
    /// it emits, and the refusal of a second claim for the same day.
    /// <para>
    /// The second claim is tested in three forms: the same action run twice, two separate actions for one day,
    /// and a claim repeated after the model is serialized and restored.
    /// </para>
    /// </summary>
    [TestFixture]
    public class DailyRewardClaimTests
    {
        static readonly MetaTime Noon = MetaTime.FromDateTime(new DateTime(2026, 3, 10, 12, 0, 0, DateTimeKind.Utc));

        static PlayerModel NewPlayer(List<PlayerEventBase> captured, SharedGameConfig config = null) => TestPlayers.New(Noon, config, captured);

        static PlayerModel NewPlayer() => NewPlayer(new List<PlayerEventBase>());

        static DailyRewardOutlook OutlookAt(PlayerModel player, MetaTime at) =>
            DailyRewardPolicy.OutlookAt(player.DailyReward, player.GameConfig.Global, DailyRewardPolicy.ActiveTable(player.GameConfig), new PlayerLocalTime(at, MetaDuration.Zero));

        static MetaActionResult Claim(PlayerModel player, int activation, MetaTime at) =>
            TestPlayers.DryRunThenCommit(player, new PlayerDailyRewardClaimed(activation, at));

        /// <summary>Returns the activation index of <paramref name="at"/> in the test daily reset, for a player on UTC.</summary>
        static int Activation(MetaTime at) =>
            DailyRewardCalendar.ActivationAt(TestGameConfig.DailyReset(), new PlayerLocalTime(at, MetaDuration.Zero)).Index;

        static IEnumerable<PlayerEventDailyRewardClaimed> Claims(IEnumerable<PlayerEventBase> events) =>
            events.OfType<PlayerEventDailyRewardClaimed>();

        static IEnumerable<PlayerEventEconomyTransaction> Transactions(IEnumerable<PlayerEventBase> events) =>
            events.OfType<PlayerEventEconomyTransaction>();

        [Test]
        public void AFreshPlayerCanClaimStepOneAndTheCoinsLand()
        {
            PlayerModel player = NewPlayer();

            Assert.That(Claim(player, activation: 20_000, at: Noon), Is.EqualTo(MetaActionResult.Success));

            Assert.That(player.Wallet.Coins, Is.EqualTo(3_150), "the starting 3,000 plus step one's 150");
            Assert.That(player.DailyReward.StreakDays, Is.EqualTo(1));
            Assert.That(player.DailyReward.LastClaimedStep, Is.EqualTo(1));
            Assert.That(player.DailyReward.ClaimCount, Is.EqualTo(1));
            Assert.That(player.DailyReward.LastClaimedAt, Is.EqualTo(Noon));
            Assert.That(player.DailyReward.LastClaimedTable, Is.EqualTo(TestGameConfig.DailyTable));
        }

        /// <summary>
        /// A run with <c>commit: false</c> emits no events and leaves the serialized model unchanged. A change to
        /// any member, including a lazily filled field or a cached value, would desync the client and the server.
        /// </summary>
        [Test]
        public void ADryRunMovesNothingAndEmitsNothing()
        {
            List<PlayerEventBase> captured = new List<PlayerEventBase>();
            PlayerModel           player   = NewPlayer(captured);

            byte[] before = TestPlayers.Snapshot(player);
            captured.Clear();

            Assert.That(new PlayerDailyRewardClaimed(20_000, Noon).Execute(player, commit: false), Is.EqualTo(MetaActionResult.Success));

            Assert.That(TestPlayers.Snapshot(player), Is.EqualTo(before), "a dry run moved player state");
            Assert.That(captured, Is.Empty, "a dry run wrote an event, so every replay would double-count it");

            Assert.That(new PlayerDailyRewardClaimed(20_000, Noon).Execute(player, commit: true), Is.EqualTo(MetaActionResult.Success));
            Assert.That(TestPlayers.Snapshot(player), Is.Not.EqualTo(before));
        }

        /// <summary>Running the same action instance a second time is refused and changes nothing.</summary>
        [Test]
        public void AReplayedActionGrantsNothingASecondTime()
        {
            List<PlayerEventBase> captured = new List<PlayerEventBase>();
            PlayerModel           player   = NewPlayer(captured);

            PlayerDailyRewardClaimed action = new PlayerDailyRewardClaimed(20_000, Noon);
            Assert.That(action.Execute(player, commit: true), Is.EqualTo(MetaActionResult.Success));

            byte[] afterFirst = TestPlayers.Snapshot(player);
            captured.Clear();

            Assert.That(action.Execute(player, commit: false), Is.EqualTo(ActionResults.DailyRewardAlreadyClaimed));
            Assert.That(action.Execute(player, commit: true), Is.EqualTo(ActionResults.DailyRewardAlreadyClaimed));

            Assert.That(TestPlayers.Snapshot(player), Is.EqualTo(afterFirst), "the replay moved state");
            Assert.That(captured, Is.Empty, "the replay wrote an event");
            Assert.That(player.Wallet.Coins, Is.EqualTo(3_150));
        }

        /// <summary>Two separate claim actions for the same day grant the reward once.</summary>
        [Test]
        public void ADoubleTapGrantsExactlyOnce()
        {
            List<PlayerEventBase> captured = new List<PlayerEventBase>();
            PlayerModel           player   = NewPlayer(captured);

            // Two separate action instances, one second apart, as two taps would send.
            Assert.That(new PlayerDailyRewardClaimed(20_000, Noon).Execute(player, commit: true), Is.EqualTo(MetaActionResult.Success));
            Assert.That(new PlayerDailyRewardClaimed(20_000, Noon + MetaDuration.FromSeconds(1)).Execute(player, commit: true),
                Is.EqualTo(ActionResults.DailyRewardAlreadyClaimed));

            Assert.That(player.Wallet.Coins, Is.EqualTo(3_150));
            Assert.That(player.DailyReward.ClaimCount, Is.EqualTo(1));
            Assert.That(Claims(captured).Count(), Is.EqualTo(1));
        }

        /// <summary>
        /// A claim repeated after the model is serialized and restored, as happens across a reconnect, is refused.
        /// The claimed activation is stored in the model, so it survives the round trip.
        /// </summary>
        [Test]
        public void AClaimSurvivesAReconnectAndStillRefusesTheSameDay()
        {
            List<PlayerEventBase> captured = new List<PlayerEventBase>();
            SharedGameConfig      config   = TestGameConfig.Build();
            PlayerModel           player   = NewPlayer(captured, config);

            Assert.That(Claim(player, activation: 20_000, at: Noon), Is.EqualTo(MetaActionResult.Success));

            byte[]      bytes    = TestPlayers.Snapshot(player);
            PlayerModel restored = MetaSerialization.DeserializeTagged<PlayerModel>(bytes, MetaSerializationFlags.IncludeAll, resolver: config, logicVersion: null);
            restored.SetGameConfig(config);
            restored.AnalyticsEventHandler = new AnalyticsEventHandler<IPlayerModelBase, PlayerEventBase>((context, payload) => captured.Add(payload));

            Assert.That(restored.Wallet.Coins, Is.EqualTo(3_150), "the reward survived the reconnect");
            Assert.That(restored.DailyReward.StreakDays, Is.EqualTo(1));

            captured.Clear();
            Assert.That(new PlayerDailyRewardClaimed(20_000, Noon).Execute(restored, commit: true), Is.EqualTo(ActionResults.DailyRewardAlreadyClaimed));

            Assert.That(restored.Wallet.Coins, Is.EqualTo(3_150));
            Assert.That(captured, Is.Empty);
        }

        /// <summary>One full cycle of claims grants the test table's total, and the next claim starts the cycle again.</summary>
        [Test]
        public void SevenDaysGrantFifteenHundredAndSixtyCoinsAndOneToken()
        {
            PlayerModel player = NewPlayer();

            for (int day = 0; day < DailyRewardTableInfo.NumSteps; day++)
                Assert.That(Claim(player, 20_000 + day, Noon + MetaDuration.FromDays(day)), Is.EqualTo(MetaActionResult.Success));

            Assert.That(player.Wallet.Coins - 3_000, Is.EqualTo(1_560));
            Assert.That(player.Wallet.SpinTokens - 1, Is.EqualTo(1));
            Assert.That(player.Wallet.Gems, Is.EqualTo(100), "the daily reward grants no gems");
            Assert.That(player.DailyReward.StreakDays, Is.EqualTo(7));
            Assert.That(player.DailyReward.LastClaimedStep, Is.EqualTo(7));

            // The eighth day starts the cycle again while the streak keeps counting.
            Assert.That(Claim(player, 20_007, Noon + MetaDuration.FromDays(7)), Is.EqualTo(MetaActionResult.Success));
            Assert.That(player.DailyReward.LastClaimedStep, Is.EqualTo(1));
            Assert.That(player.DailyReward.StreakDays, Is.EqualTo(8));
            Assert.That(player.Wallet.Coins - 3_000, Is.EqualTo(1_710));
        }

        /// <summary>
        /// A grant that would exceed the wallet cap leaves the activation unclaimed and the streak unchanged, so
        /// the player can still claim the day after the cap is raised.
        /// </summary>
        [Test]
        public void AWalletRefusalConsumesNothingAndAdvancesNothing()
        {
            List<PlayerEventBase> captured = new List<PlayerEventBase>();

            // A starting wallet at the coin cap, so step one's coins cannot be granted.
            SharedGameConfig config = TestGameConfig.Build(
                new RewardBundle(CurrencyAmount.Coins(TestGameConfig.MaxCoins), CurrencyAmount.Gems(100), CurrencyAmount.SpinTokens(1)));

            PlayerModel player = NewPlayer(captured, config);
            captured.Clear();

            Assert.That(Claim(player, activation: 20_000, at: Noon), Is.EqualTo(ActionResults.WalletCapExceeded));

            Assert.That(player.DailyReward.HasClaimed, Is.False, "a refused grant consumed the activation");
            Assert.That(player.DailyReward.StreakDays, Is.Zero);
            Assert.That(player.Wallet.Coins, Is.EqualTo(TestGameConfig.MaxCoins));
            Assert.That(Claims(captured), Is.Empty, "a refused claim emitted a success event");
            Assert.That(Transactions(captured), Is.Empty, "a refused claim moved a balance");
        }

        /// <summary>
        /// A claim emits one <see cref="PlayerEventDailyRewardClaimed"/> and one currency row per currency, all with
        /// the same correlation id, so an analyst can join the claim to the currencies it granted.
        /// </summary>
        [Test]
        public void OneClaimEmitsOneEventCorrelatedWithItsCurrencyRows()
        {
            List<PlayerEventBase> captured = new List<PlayerEventBase>();
            PlayerModel           player   = NewPlayer(captured);

            for (int day = 0; day < DailyRewardTableInfo.NumSteps - 1; day++)
                Claim(player, 20_000 + day, Noon + MetaDuration.FromDays(day));

            captured.Clear();
            Claim(player, 20_006, Noon + MetaDuration.FromDays(6));

            PlayerEventDailyRewardClaimed claimed = Claims(captured).Single();

            Assert.That(claimed.Step, Is.EqualTo(7));
            Assert.That(claimed.StreakBefore, Is.EqualTo(6));
            Assert.That(claimed.StreakAfter, Is.EqualTo(7));
            Assert.That(claimed.CompletedCycle, Is.True);
            Assert.That(claimed.UsedGrace, Is.False);
            Assert.That(claimed.ResetStreak, Is.False);
            Assert.That(claimed.ClaimOrdinal, Is.EqualTo(7));
            Assert.That(claimed.Table, Is.EqualTo(TestGameConfig.DailyTable));

            List<PlayerEventEconomyTransaction> rows = Transactions(captured).ToList();

            Assert.That(rows.Select(row => row.Currency), Is.EquivalentTo(new[] { CurrencyType.Coins, CurrencyType.SpinTokens }));
            Assert.That(rows.All(row => row.Flow == CurrencyFlow.Source), Is.True);
            Assert.That(rows.All(row => row.Reason == EconomyReason.DailyReward), Is.True);
            Assert.That(rows.All(row => row.Feature == EconomyFeature.DailyReward), Is.True);
            Assert.That(rows.Single(row => row.Currency == CurrencyType.Coins).Amount, Is.EqualTo(330));

            Assert.That(rows.Select(row => row.Correlation).Append(claimed.Correlation).Distinct().Count(), Is.EqualTo(1),
                "the claim and its currency rows are one cause and share one key");
        }

        /// <summary>
        /// A config publish between two claims changes what the next claim pays. It does not change the balance
        /// already granted or the streak.
        /// </summary>
        [Test]
        public void AConfigPublishChangesFutureClaimsOnly()
        {
            PlayerModel player = NewPlayer();

            Claim(player, 20_000, Noon);
            Assert.That(player.Wallet.Coins, Is.EqualTo(3_150));

            // A table with the same id and step count but different coin values.
            List<DailyRewardStepInfo> retuned = new List<DailyRewardStepInfo>();
            int[] coins = { 150, 500, 500, 500, 500, 500, 500 };
            for (int step = 1; step <= coins.Length; step++)
            {
                RewardBundle reward = step == DailyRewardTableInfo.NumSteps
                    ? new RewardBundle(CurrencyAmount.Coins(coins[step - 1]), CurrencyAmount.SpinTokens(1))
                    : new RewardBundle(CurrencyAmount.Coins(coins[step - 1]));
                retuned.Add(new DailyRewardStepInfo(DailyRewardStepId.FromString($"daily.step{step}"), step, reward));
            }

            player.SetGameConfig(TestGameConfig.Build(dailyRewards: new DailyRewardTableInfo(TestGameConfig.DailyTable, retuned)));

            Assert.That(player.Wallet.Coins, Is.EqualTo(3_150), "publishing rewrote a balance already granted");
            Assert.That(player.DailyReward.StreakDays, Is.EqualTo(1), "publishing moved the streak");

            Claim(player, 20_001, Noon + MetaDuration.FromDays(1));

            Assert.That(player.DailyReward.LastClaimedStep, Is.EqualTo(2));
            Assert.That(player.Wallet.Coins, Is.EqualTo(3_650), "the second step paid the newly published 500");
        }

        /// <summary>
        /// <see cref="DailyRewardPolicy.OutlookAt"/>, which the screen reads, reports the day as claimable before the
        /// claim and as claimed after it, with a countdown to the next activation.
        /// </summary>
        [Test]
        public void TheOutlookSaysClaimableThenClaimedWithTheNextRewardsTime()
        {
            PlayerModel player = NewPlayer();

            DailyRewardOutlook before = OutlookAt(player, Noon);

            Assert.That(before.IsClaimable, Is.True);
            Assert.That(before.Claim.Step, Is.EqualTo(1));
            Assert.That(before.Today.Reward.AmountOf(CurrencyType.Coins), Is.EqualTo(150));
            Assert.That(before.Tomorrow.Reward.AmountOf(CurrencyType.Coins), Is.EqualTo(170));
            Assert.That(before.SkipDayAvailable, Is.True);
            Assert.That(before.StepsClaimedInCycle, Is.Zero);

            Claim(player, before.Activation.Index, Noon);

            DailyRewardOutlook after = OutlookAt(player, Noon);

            Assert.That(after.IsClaimable, Is.False);
            Assert.That(after.Refusal, Is.EqualTo(DailyRewardRefusal.AlreadyClaimed));
            Assert.That(after.Today.Reward.AmountOf(CurrencyType.Coins), Is.EqualTo(150), "the claimed state still shows what today paid");
            Assert.That(after.Tomorrow.Reward.AmountOf(CurrencyType.Coins), Is.EqualTo(170));
            Assert.That(after.StepsClaimedInCycle, Is.EqualTo(1));
            Assert.That(after.UntilNextAvailable(Noon), Is.EqualTo(MetaDuration.FromHours(12)));
        }

        /// <summary>
        /// Before the claim, the outlook reports that the next claim resets the streak, and still shows the current
        /// streak, so the screen can warn the player.
        /// </summary>
        [Test]
        public void TheOutlookWarnsThatTheNextClaimWillResetTheStreak()
        {
            PlayerModel player = NewPlayer();

            // Claim with the activation indexes the schedule gives for these times, so the gap below is measured in
            // the same calendar the outlook reads.
            int today = Activation(Noon);
            Claim(player, today, Noon);
            Claim(player, today + 1, Noon + MetaDuration.FromDays(1));
            Claim(player, today + 2, Noon + MetaDuration.FromDays(2));

            // Three days after the last claim, two activations are missed, which the skip day cannot bridge.
            DailyRewardOutlook outlook = OutlookAt(player, Noon + MetaDuration.FromDays(5));

            Assert.That(outlook.IsClaimable, Is.True);
            Assert.That(outlook.Claim.ResetsStreak, Is.True);
            Assert.That(outlook.StreakDays, Is.EqualTo(3), "the streak that is about to end is still what the screen shows");
            Assert.That(outlook.Claim.StreakAfter, Is.EqualTo(1));
            Assert.That(outlook.Today.Reward.AmountOf(CurrencyType.Coins), Is.EqualTo(150));
        }

        /// <summary>
        /// <see cref="PlayerModel.ApplyDailyRewardClaim"/> throws when the activation is already claimed, even if
        /// the caller skipped <see cref="PlayerModel.CanClaimDailyReward"/>. It throws instead of doing nothing,
        /// because a caller that already granted the currency would leave a grant with no claim recorded.
        /// </summary>
        [Test]
        public void TheModelRefusesASecondWriteForOneActivation()
        {
            PlayerModel player = NewPlayer();

            Claim(player, 20_000, Noon);

            Assert.That(player.CanClaimDailyReward(20_000), Is.False);
            Assert.That(player.CanClaimDailyReward(20_001), Is.True);

            DailyRewardTableInfo table = DailyRewardPolicy.ActiveTable(player.GameConfig);
            DailyRewardClaim     claim = DailyRewardPolicy.ClaimFor(new DailyRewardState(), table, 20_000, out DailyRewardRefusal _);

            Assert.Throws<InvalidOperationException>(() => player.ApplyDailyRewardClaim(claim, Noon, table.Id));
        }
    }
}
