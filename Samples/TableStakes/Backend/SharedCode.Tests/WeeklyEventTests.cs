using Metaplay.Core;
using Metaplay.Core.Analytics;
using Metaplay.Core.LiveOpsEvent;
using Metaplay.Core.Model;
using Metaplay.Core.Player;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.Logic.Tests
{
    /// <summary>
    /// Tests for the weekly event: a shared UTC week, a points target, and a reward the player can claim once
    /// the target is reached (<c>docs/weekly-event.md</c>).
    /// <para>
    /// Most tests deliver results through <see cref="PlayerRecordMatchResult"/>, so they also cover the
    /// once-per-match guard and the dispatch to consumers. The tests add the week to
    /// <c>PlayerModelBase.LiveOpsEvents</c> with an SDK schedule, the way the server's LiveOps event manager does.
    /// </para>
    /// </summary>
    [TestFixture]
    public class WeeklyEventTests
    {
        #region Fixture

        static MetaTime At(int day, int hour = 12) =>
            MetaTime.FromDateTime(new DateTime(2026, 9, day, hour, 0, 0, DateTimeKind.Utc));

        /// <summary>
        /// The fixture week's phase times. Scoring runs from <see cref="WeekOpensAt"/> to <see cref="WeekClosesAt"/>,
        /// and the reward can be claimed until <see cref="WeekConcludesAt"/>.
        /// </summary>
        static readonly MetaTime WeekOpensAt    = At(7);
        static readonly MetaTime WeekClosesAt   = At(14);
        static readonly MetaTime WeekConcludesAt = At(16);
        static readonly MetaTime PreviewOpensAt = At(5);

        static MetaGuid EventId(int index) =>
            MetaGuid.FromTimeAndValue(new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc), (ulong)index);

        /// <summary>Weekly event content with the given target and win bonus.</summary>
        static WeeklyEventContent Content(int target = 10, int winBonus = 5) =>
            new WeeklyEventContent(
                "Trickster's Week", "Every trick counts.", target, winBonus,
                new RewardBundle(CurrencyAmount.Coins(500), CurrencyAmount.Gems(20)));

        /// <summary>
        /// An SDK schedule for one week with every optional phase present, like every seeded week.
        /// </summary>
        static LiveOpsEventScheduleInfo Schedule(
            MetaTime? preview = null, MetaTime? opens = null, MetaTime? endingSoon = null,
            MetaTime? closes = null, MetaTime? concludes = null)
        {
            MetaDictionary<LiveOpsEventPhase, MetaTime> phases = new MetaDictionary<LiveOpsEventPhase, MetaTime>
            {
                { LiveOpsEventPhase.Preview,      preview    ?? PreviewOpensAt },
                { LiveOpsEventPhase.NormalActive, opens      ?? WeekOpensAt },
                { LiveOpsEventPhase.EndingSoon,   endingSoon ?? At(13) },
                { LiveOpsEventPhase.Review,       closes     ?? WeekClosesAt },
                { LiveOpsEventPhase.Concluded,    concludes  ?? WeekConcludesAt },
            };

            return new LiveOpsEventScheduleInfo(phases);
        }

        static PlayerModel NewPlayer(List<PlayerEventBase> captured = null)
        {
            PlayerModel player = TestPlayers.New(At(7), captured: captured);
            player.ResetTime(At(8));
            return player;
        }

        /// <summary>Add a weekly event to the player the way the SDK's add action does. Returns the event id.</summary>
        static MetaGuid GiveEvent(
            PlayerModel              player,
            WeeklyEventContent       content   = null,
            LiveOpsEventScheduleInfo schedule  = null,
            LiveOpsEventPhase        phase     = null,
            int                      index     = 1)
        {
            MetaGuid id = EventId(index);
            PlayerLiveOpsEventInfo info = new PlayerLiveOpsEventInfo(
                id, schedule ?? Schedule(), content ?? Content(), phase ?? LiveOpsEventPhase.NormalActive);

            player.LiveOpsEvents.EventModels.Add(id, info.Content.CreateModel(info));
            return id;
        }

        /// <summary>
        /// Move an event the player holds to a new phase, the way the SDK's synchronized phase action does.
        /// </summary>
        static void MovePhase(PlayerModel player, MetaGuid eventId, LiveOpsEventPhase newPhase)
        {
            PlayerLiveOpsEventModel model = player.LiveOpsEvents.EventModels[eventId];
            LiveOpsEventPhase       old   = model.Phase;

            model.Phase = newPhase;
            model.OnPhaseChanged(player, old, Array.Empty<LiveOpsEventPhase>(), newPhase);

            if (newPhase == LiveOpsEventPhase.Concluded && model.AllowRemove)
                player.LiveOpsEvents.EventModels.Remove(eventId);
        }

        /// <summary>Delivers one match result through <see cref="TestPlayers.DryRunThenCommit"/>. A loss is position 2.</summary>
        static MetaActionResult Deliver(PlayerModel player, int matchIndex, MetaTime at, int tricksWon = 3, bool win = true) =>
            TestPlayers.DryRunThenCommit(player, TestPlayers.MatchResult(matchIndex, at, position: win ? 0 : 2, tricksWon));

        static MetaActionResult Claim(PlayerModel player, MetaGuid eventId) =>
            TestPlayers.DryRunThenCommit(player, new PlayerClaimWeeklyEventReward(eventId));

        static WeeklyEventOutlook Outlook(PlayerModel player, MetaTime? at = null) =>
            player.WeeklyEvent.OutlookAt(player.LiveOpsEvents, at ?? At(8));

        #endregion

        #region Scoring

        /// <summary>A point per trick, plus the week's bonus for winning the table. A loss scores the tricks and no bonus.</summary>
        [TestCase(3, true,  8)]
        [TestCase(2, false, 2)]
        public void APointPerTrickPlusTheWeeksBonusForWinningTheTable(int tricksWon, bool win, int expectedPoints)
        {
            PlayerModel player = NewPlayer();
            GiveEvent(player, Content(target: 100, winBonus: 5));

            Deliver(player, 1, At(8), tricksWon, win);

            Assert.That(Outlook(player).Points, Is.EqualTo(expectedPoints));
        }

        [Test]
        public void PointsAccumulateAcrossGames()
        {
            PlayerModel player = NewPlayer();
            GiveEvent(player, Content(target: 100, winBonus: 5));

            Deliver(player, 1, At(8), tricksWon: 1, win: false);
            Deliver(player, 2, At(9), tricksWon: 2, win: true);

            Assert.That(Outlook(player).Points, Is.EqualTo(1 + 2 + 5));
        }

        /// <summary>
        /// A game that scores no points is not recorded. This keeps the set of counted matches bounded by the
        /// target rather than by the number of games played.
        /// </summary>
        [Test]
        public void AGameThatScoresNothingIsNotRemembered()
        {
            PlayerModel player = NewPlayer();
            GiveEvent(player, Content(target: 100, winBonus: 5));

            Deliver(player, 1, At(8), tricksWon: 0, win: false);

            Assert.That(player.WeeklyEvent.StoredEvents, Is.Empty, "a nil game created an entry with nothing in it");
        }

        #endregion

        #region The window

        /// <summary>
        /// A game finished before the week opened or after scoring closed does not score. The scoring window comes
        /// from the SDK schedule of an event the player holds, not from the completion time.
        /// </summary>
        [TestCase(6)]
        [TestCase(15)]
        public void AStampOutsideTheEnabledWindowScoresNowhere(int day)
        {
            PlayerModel player = NewPlayer();
            GiveEvent(player, Content(target: 100));

            Deliver(player, 1, At(day), tricksWon: 3, win: true);

            Assert.That(Outlook(player).Points, Is.Zero);
            Assert.That(player.WeeklyEvent.StoredEvents, Is.Empty);
        }

        /// <summary>
        /// A game finished inside the scoring window still counts when its result arrives after the window closed.
        /// A delivery delay must not cost the player the points.
        /// </summary>
        [Test]
        public void AGamePlayedInsideTheWindowCountsEvenWhenItArrivesInReview()
        {
            PlayerModel player = NewPlayer();
            MetaGuid    id     = GiveEvent(player, Content(target: 100));
            MovePhase(player, id, LiveOpsEventPhase.Review);

            Deliver(player, 1, At(13), tricksWon: 4, win: true);

            Assert.That(Outlook(player, At(15)).Points, Is.EqualTo(9));
        }

        /// <summary>
        /// Progress is only written for events the player currently holds. A result timed in a week the player no
        /// longer holds creates nothing, so it cannot recreate a removed entry with its claimed flag reset.
        /// </summary>
        [Test]
        public void AFactForAnEventThePlayerNoLongerHoldsCreatesNothing()
        {
            PlayerModel player = NewPlayer();
            MetaGuid    id     = GiveEvent(player, Content(target: 5));

            Deliver(player, 1, At(8), tricksWon: 3, win: true);
            Assert.That(Claim(player, id), Is.EqualTo(MetaActionResult.Success));

            MovePhase(player, id, LiveOpsEventPhase.Concluded);
            Assert.That(player.WeeklyEvent.StoredEvents, Is.Empty, "the entry outlived the event that identified it");

            // A late delivery of a match with a completion time inside the removed week.
            Deliver(player, 2, At(9), tricksWon: 5, win: true);

            Assert.That(player.WeeklyEvent.StoredEvents, Is.Empty, "a stale fact created an entry for an event nobody holds");
            Assert.That(Outlook(player).IsResolved, Is.False);
        }

        [Test]
        public void APlayerHoldingNoWeeklyEventHasNoStatusToDraw()
        {
            PlayerModel player = NewPlayer();

            Assert.That(Outlook(player).IsResolved, Is.False);
            Assert.That(Outlook(player).TargetPoints, Is.Zero);
        }

        #endregion

        #region Deduplication

        /// <summary>
        /// A result whose entry has aged off the bounded match history can be delivered again with its original
        /// completion time. It still counts only once.
        /// </summary>
        [Test]
        public void ARepeatedTableCountsOnce()
        {
            PlayerModel player = NewPlayer();
            GiveEvent(player, Content(target: 100));

            Deliver(player, 1, At(8), tricksWon: 3, win: true);
            int after = Outlook(player).Points;

            // Call the observer directly, because the weekly event's own duplicate check is there. The player
            // model's once-per-match guard would refuse a second delivery through the action.
            ((IMatchCompletionObserver)player.WeeklyEvent).OnMatchCompleted(Context(player, 1, At(8), tricksWon: 3, win: true));

            Assert.That(Outlook(player).Points, Is.EqualTo(after));
            Assert.That(player.WeeklyEvent.StoredEvents[0].CountedMatches, Has.Count.EqualTo(1));
        }

        /// <summary>
        /// Nothing is recorded once the target is met, so the number of counted matches is bounded by the
        /// matches needed to reach the target, which the content checks limit.
        /// </summary>
        [Test]
        public void NothingIsRememberedOnceTheTargetIsMet()
        {
            PlayerModel player = NewPlayer();
            GiveEvent(player, Content(target: 5));

            Deliver(player, 1, At(8), tricksWon: 3, win: true); // 8 points, target crossed
            Deliver(player, 2, At(9), tricksWon: 5, win: true);

            WeeklyEventProgress progress = player.WeeklyEvent.StoredEvents.Single();
            Assert.That(progress.Points, Is.EqualTo(8), "a game after the target moved the total");
            Assert.That(progress.CountedMatches, Has.Count.EqualTo(1));
        }

        #endregion

        #region Crossing the target

        [Test]
        public void CrossingTheTargetMakesTheRewardClaimableAndGrantsNothing()
        {
            PlayerModel player = NewPlayer();
            GiveEvent(player, Content(target: 5));

            int coinsBefore = player.Wallet.Coins;
            Deliver(player, 1, At(8), tricksWon: 3, win: true);

            Assert.That(Outlook(player).IsClaimable, Is.True);
            Assert.That(player.Wallet.Coins, Is.EqualTo(coinsBefore), "the event granted a reward of its own");
        }

        /// <summary>
        /// Reaching the target is reported from the stored flag, not from the point total, so a later match
        /// that would pass the target again reports nothing.
        /// </summary>
        [Test]
        public void TheTargetIsReportedOnlyOnce()
        {
            List<PlayerEventBase> captured = new List<PlayerEventBase>();
            PlayerModel           player   = NewPlayer(captured);
            GiveEvent(player, Content(target: 5));

            Deliver(player, 1, At(8), tricksWon: 3, win: true);
            Deliver(player, 2, At(9), tricksWon: 5, win: true);
            ((IMatchCompletionObserver)player.WeeklyEvent).OnMatchCompleted(Context(player, 3, At(9), tricksWon: 5, win: true));

            Assert.That(captured.OfType<PlayerEventWeeklyEventTargetReached>().Count(), Is.EqualTo(1));

            PlayerEventWeeklyEventTargetReached row = captured.OfType<PlayerEventWeeklyEventTargetReached>().Single();
            Assert.That(row.TargetPoints, Is.EqualTo(5));
            Assert.That(row.PointsAfter, Is.EqualTo(8));
            Assert.That(row.ScoringMatches, Is.EqualTo(1));
        }

        #endregion

        #region Claiming

        [Test]
        public void TheClaimPaysTheWeeksRewardOnce()
        {
            PlayerModel player = NewPlayer();
            MetaGuid    id     = GiveEvent(player, Content(target: 5));

            Deliver(player, 1, At(8), tricksWon: 3, win: true);

            int coins = player.Wallet.Coins;
            int gems  = player.Wallet.Gems;

            Assert.That(Claim(player, id), Is.EqualTo(MetaActionResult.Success));
            Assert.That(player.Wallet.Coins, Is.EqualTo(coins + 500));
            Assert.That(player.Wallet.Gems, Is.EqualTo(gems + 20));

            Assert.That(Claim(player, id), Is.EqualTo(ActionResults.WeeklyEventAlreadyClaimed));
            Assert.That(player.Wallet.Coins, Is.EqualTo(coins + 500), "a second claim paid again");
        }

        [Test]
        public void AClaimBeforeTheTargetIsRefused()
        {
            PlayerModel player = NewPlayer();
            MetaGuid    id     = GiveEvent(player, Content(target: 100));

            Deliver(player, 1, At(8), tricksWon: 1, win: false);

            Assert.That(Claim(player, id), Is.EqualTo(ActionResults.WeeklyEventTargetNotReached));
        }

        [Test]
        public void AClaimForAnEventThePlayerDoesNotHoldIsRefused()
        {
            PlayerModel player = NewPlayer();
            GiveEvent(player, Content(target: 5));

            Assert.That(Claim(player, EventId(99)), Is.EqualTo(ActionResults.NoSuchWeeklyEvent));
        }

        /// <summary>
        /// The reward stays claimable after scoring closes, during the Review phase. That late-claim window is the
        /// purpose of the Review phase.
        /// </summary>
        [Test]
        public void ARewardEarnedInTheWeekIsStillClaimableInReview()
        {
            PlayerModel player = NewPlayer();
            MetaGuid    id     = GiveEvent(player, Content(target: 5));

            Deliver(player, 1, At(8), tricksWon: 3, win: true);
            MovePhase(player, id, LiveOpsEventPhase.Review);

            Assert.That(Outlook(player, At(15)).IsClaimable, Is.True);
            Assert.That(Claim(player, id), Is.EqualTo(MetaActionResult.Success));
        }

        [Test]
        public void TheClaimWritesOneRowUnderTheWalletsOwnCorrelationKey()
        {
            List<PlayerEventBase> captured = new List<PlayerEventBase>();
            PlayerModel           player   = NewPlayer(captured);
            MetaGuid              id       = GiveEvent(player, Content(target: 5));

            Deliver(player, 1, At(8), tricksWon: 3, win: true);
            captured.Clear();

            Assert.That(Claim(player, id), Is.EqualTo(MetaActionResult.Success));

            PlayerEventWeeklyEventRewardClaimed claimed = captured.OfType<PlayerEventWeeklyEventRewardClaimed>().Single();
            Assert.That(claimed.EventId, Is.EqualTo(id));
            Assert.That(claimed.ClaimOrdinal, Is.EqualTo(1));
            Assert.That(claimed.PointsScored, Is.EqualTo(8));

            IEnumerable<AnalyticsCorrelationId> wallet = captured
                .OfType<PlayerEventEconomyTransaction>()
                .Select(row => row.Correlation);

            Assert.That(wallet, Is.Not.Empty);
            Assert.That(wallet, Has.All.EqualTo(claimed.Correlation),
                "the wallet rows and the feature's own row must be joinable");
        }

        #endregion

        #region The week ending

        /// <summary>
        /// The progress and its counted matches are removed with the event, in the phase where the SDK removes the
        /// event model. Otherwise a player would keep one entry per week forever.
        /// </summary>
        [Test]
        public void ConcludingTheWeekDropsItsProgress()
        {
            PlayerModel player = NewPlayer();
            MetaGuid    id     = GiveEvent(player, Content(target: 100));

            Deliver(player, 1, At(8), tricksWon: 3, win: true);
            Assert.That(player.WeeklyEvent.StoredEvents, Has.Count.EqualTo(1));

            MovePhase(player, id, LiveOpsEventPhase.Concluded);

            Assert.That(player.WeeklyEvent.StoredEvents, Is.Empty);
            Assert.That(player.LiveOpsEvents.EventModels, Is.Empty, "the SDK did not remove the concluded event");
        }

        [Test]
        public void AnEarlierPhaseChangeKeepsTheProgress()
        {
            PlayerModel player = NewPlayer();
            MetaGuid    id     = GiveEvent(player, Content(target: 100));

            Deliver(player, 1, At(8), tricksWon: 3, win: true);
            MovePhase(player, id, LiveOpsEventPhase.EndingSoon);
            MovePhase(player, id, LiveOpsEventPhase.Review);

            Assert.That(Outlook(player, At(15)).Points, Is.EqualTo(8));
        }

        #endregion

        #region Two weeks at once

        /// <summary>
        /// Consecutive weeks overlap on purpose: next week's preview runs during this week's review. Progress is
        /// keyed by event id for this reason, and only the running week scores.
        /// </summary>
        [Test]
        public void OnlyTheRunningWeekScoresWhileTwoAreHeld()
        {
            PlayerModel player = NewPlayer();

            MetaGuid running = GiveEvent(player, Content(target: 100), index: 1);
            MetaGuid next    = GiveEvent(player, Content(target: 100),
                schedule: Schedule(preview: At(12), opens: At(14), endingSoon: At(20), closes: At(21), concludes: At(23)),
                phase: LiveOpsEventPhase.Preview, index: 2);

            Deliver(player, 1, At(8), tricksWon: 3, win: true);

            Assert.That(player.WeeklyEvent.Find(running).Points, Is.EqualTo(8));
            Assert.That(player.WeeklyEvent.Find(next), Is.Null, "a game scored into a week that has not opened");
        }

        /// <summary>
        /// The card shows what the player can act on. An unclaimed reward from the week that just ended takes
        /// priority over the running week, because claiming is the only action available now.
        /// </summary>
        [Test]
        public void AnOwedRewardIsTheWeekTheScreenDraws()
        {
            PlayerModel player = NewPlayer();

            MetaGuid ending = GiveEvent(player, Content(target: 5), index: 1);
            Deliver(player, 1, At(8), tricksWon: 3, win: true);
            MovePhase(player, ending, LiveOpsEventPhase.Review);

            GiveEvent(player, Content(target: 100),
                schedule: Schedule(preview: At(12), opens: At(14), endingSoon: At(20), closes: At(21), concludes: At(23)),
                phase: LiveOpsEventPhase.NormalActive, index: 2);

            WeeklyEventOutlook outlook = Outlook(player, At(15));
            Assert.That(outlook.EventId, Is.EqualTo(ending));
            Assert.That(outlook.IsClaimable, Is.True);
        }

        #endregion

        #region The phases a screen reads

        [Test]
        public void APreviewedWeekIsNotScoringAndHasItsTargetAndRewardToShow()
        {
            PlayerModel player = NewPlayer();
            GiveEvent(player, Content(target: 100), phase: LiveOpsEventPhase.Preview);

            WeeklyEventOutlook outlook = Outlook(player, At(6));

            Assert.That(outlook.IsPreview, Is.True);
            Assert.That(outlook.IsScoring, Is.False);
            Assert.That(outlook.TargetPoints, Is.EqualTo(100));
            Assert.That(outlook.Reward, Is.Not.Null);
            Assert.That(outlook.StartsAt, Is.EqualTo(WeekOpensAt));
        }

        [TestCase(3)] // NormalActive
        [TestCase(4)] // EndingSoon
        public void BothActivePhasesScore(int phaseId)
        {
            LiveOpsEventPhase phase = LiveOpsEventPhase.AllValues.Single(p => p.Id == phaseId);

            PlayerModel player = NewPlayer();
            GiveEvent(player, Content(target: 100), phase: phase);

            Deliver(player, 1, At(8), tricksWon: 3, win: true);

            Assert.That(Outlook(player).IsScoring, Is.True);
            Assert.That(Outlook(player).Points, Is.EqualTo(8));
        }

        [Test]
        public void AWeekInReviewDoesNotScoreNewGames()
        {
            PlayerModel player = NewPlayer();
            MetaGuid    id     = GiveEvent(player, Content(target: 100));
            MovePhase(player, id, LiveOpsEventPhase.Review);

            // A game finished after the scoring window closed.
            Deliver(player, 1, At(15), tricksWon: 5, win: true);

            Assert.That(Outlook(player, At(15)).Points, Is.Zero);
            Assert.That(Outlook(player, At(15)).IsScoring, Is.False);
        }

        [Test]
        public void TheScheduleGivesTheWindowAndTheClaimDeadline()
        {
            PlayerModel player = NewPlayer();
            GiveEvent(player, Content(target: 100));

            WeeklyEventOutlook outlook = Outlook(player);

            Assert.That(outlook.HasSchedule, Is.True);
            Assert.That(outlook.StartsAt, Is.EqualTo(WeekOpensAt));
            Assert.That(outlook.EndsAt, Is.EqualTo(WeekClosesAt));
            Assert.That(outlook.ConcludesAt, Is.EqualTo(WeekConcludesAt));
        }

        #endregion

        #region The content's own bands

        [Test]
        public void AWellFormedWeekPassesItsChecks()
        {
            Assert.That(WeeklyEventScoring.ProblemsWith(Content(target: 100, winBonus: 5)), Is.Empty);
        }

        [TestCase(0, 5, "not a target")]
        [TestCase(100_000, 5, "more than")]
        [TestCase(100, 0, "outside the 1-10 band")]
        [TestCase(100, 50, "outside the 1-10 band")]
        public void ABadlyBalancedWeekIsRefused(int target, int winBonus, string reason)
        {
            IEnumerable<string> problems = WeeklyEventScoring.ProblemsWith(Content(target, winBonus));
            Assert.That(string.Join("; ", problems), Does.Contain(reason));
        }

        [Test]
        public void AWeekThatPaysSpinTokensIsRefused()
        {
            WeeklyEventContent content = new WeeklyEventContent(
                "Theme", "Tagline", 100, 5, new RewardBundle(CurrencyAmount.SpinTokens(1)));

            Assert.That(string.Join("; ", WeeklyEventScoring.ProblemsWith(content)), Does.Contain("spin tokens"));
        }

        [Test]
        public void AWeekWithNoThemeOrTaglineOrRewardIsRefused()
        {
            WeeklyEventContent content = new WeeklyEventContent("", "", 100, 5, new RewardBundle());
            string             report  = string.Join("; ", WeeklyEventScoring.ProblemsWith(content));

            Assert.That(report, Does.Contain("theme"));
            Assert.That(report, Does.Contain("tagline"));
            Assert.That(report, Does.Contain("grants nothing"));
        }

        /// <summary>
        /// The target ceiling bounds the set of counted matches. A recorded match scores at least one point and
        /// nothing is recorded once the target is met, so the set holds at most one id per target point.
        /// </summary>
        [Test]
        public void TheTargetCeilingIsTheDeduplicationSetsBound()
        {
            int bonus   = 5;
            int perGame = WeeklyEventScoring.MaxPointsPerMatch(bonus);

            Assert.That(WeeklyEventScoring.ProblemsWith(Content(perGame * WeeklyEventScoring.MaxTargetInPerfectMatches, bonus)), Is.Empty);
            Assert.That(WeeklyEventScoring.ProblemsWith(Content(perGame * WeeklyEventScoring.MaxTargetInPerfectMatches + 1, bonus)), Is.Not.Empty);
        }

        /// <summary>
        /// <see cref="WeeklyEventContent.Validate"/> is the hook the SDK and the test endpoint call. It must run
        /// <see cref="WeeklyEventScoring.ProblemsWith"/> but not the balance checks. Testing only
        /// <c>ProblemsWith</c> would not catch the balance checks being added to <c>Validate</c>, and only the
        /// live end-to-end test would fail.
        /// </summary>
        [Test]
        public void TheCreationHookRunsTheRunnabilityChecksAndNotTheBalanceOnes()
        {
            CollectingLog runnable = new CollectingLog();
            Content(target: 1, winBonus: 5).Validate(runnable, activeGameConfig: null);

            Assert.That(runnable.Errors, Is.Empty,
                "the creation hook refused a week a single game finishes, which is the test endpoint's whole use");

            CollectingLog broken = new CollectingLog();
            new WeeklyEventContent("", "", 0, 99, null).Validate(broken, activeGameConfig: null);

            Assert.That(broken.Errors, Is.Not.Empty, "the creation hook let an unrunnable week through");
        }

        /// <summary>
        /// Collects the errors reported by <see cref="LiveOpsEventContent.Validate"/> so a test can read them.
        /// </summary>
        sealed class CollectingLog : ILiveOpsEventValidationLog
        {
            public List<string> Errors { get; } = new List<string>();

            public void Error(string msg, string memberNameOrNull = null) => Errors.Add(msg);
            public void Warning(string msg, string memberNameOrNull = null) { }
        }

        /// <summary>
        /// A week that one game can finish is <b>runnable</b>, which is what the creation-time checks decide.
        /// Balance is checked separately, on templates, by the config build (<c>GameConfigValidationTests</c>).
        /// Keeping the two apart lets the live test create a week it can finish.
        /// </summary>
        [Test]
        public void ATargetOneGameCouldCrossPassesRunnabilityButFailsBalanceChecks()
        {
            Assert.That(WeeklyEventScoring.ProblemsWith(Content(target: 1, winBonus: 5)), Is.Empty);
            Assert.That(WeeklyEventScoring.BalanceProblemsWith(Content(target: 1, winBonus: 5)), Is.Not.Empty);
        }

        #endregion

        #region Isolation

        /// <summary>
        /// An event model without a schedule scores nothing and does not break recording. The game is still
        /// recorded, as it must be when any optional feature fails.
        /// </summary>
        [Test]
        public void AnUnscheduledEventScoresNothingAndCostsTheRecordNothing()
        {
            PlayerModel player = NewPlayer();
            MetaGuid    id     = EventId(1);
            PlayerLiveOpsEventInfo info = new PlayerLiveOpsEventInfo(
                id, scheduleMaybe: null, Content(target: 100), LiveOpsEventPhase.NormalActive);
            player.LiveOpsEvents.EventModels.Add(id, info.Content.CreateModel(info));

            Assert.That(Deliver(player, 1, At(8), tricksWon: 3, win: true), Is.EqualTo(MetaActionResult.Success));
            Assert.That(player.Record.GamesPlayed, Is.EqualTo(1));
            Assert.That(player.WeeklyEvent.StoredEvents, Is.Empty);
        }

        #endregion

        /// <summary>
        /// A match-completion fact for calling the observer directly. Tests of a <i>re-delivery</i> need this,
        /// because the player model's once-per-match guard refuses a second delivery of a match still in the
        /// history before any consumer sees it.
        /// </summary>
        static MatchCompletionContext Context(PlayerModel player, int matchIndex, MetaTime at, int tricksWon, bool win) =>
            new MatchCompletionContext(player, new MatchCompletion(MatchTestDeals.MatchId(matchIndex), at, win ? 0 : 2, tricksWon));
    }
}
