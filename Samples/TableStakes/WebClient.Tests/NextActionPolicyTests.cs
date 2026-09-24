using WebClient.Meta;
using WebClient.Meta.Fixtures;

namespace WebClient.Tests;

/// <summary>
/// Tests for <see cref="NextActionPolicy"/>, the priority list that chooses Home's Next-up card.
/// <para>
/// Each rung is tested from a state where only that rung qualifies, unless the test is about ordering.
/// </para>
/// </summary>
[TestFixture]
public class NextActionPolicyTests
{
    /// <summary>
    /// A snapshot where no rung above Play qualifies: all data loaded, nothing claimable, no deadline close,
    /// and no progress part-way. Each test adds the state it needs with a <c>with</c> expression. The snapshot is
    /// an immutable record, so the tests share one instance.
    /// </summary>
    private static readonly MetaSnapshot NothingPending = BuildNothingPending();

    private static MetaSnapshot BuildNothingPending()
    {
        MetaSnapshot s = MetaFixtures.Build("fresh", TimeSpan.Zero);

        return s with
        {
            DailyReward = s.DailyReward with
            {
                State = ActivityState.Completed, HasClaimableReward = false,
                UntilNextAvailable = TimeSpan.FromHours(9),
            },
            FirstWeek = s.FirstWeek with
            {
                State = ActivityState.Completed, TodaysGoals = Array.Empty<GoalView>(),
                UntilDayExpires = TimeSpan.FromHours(20), HasClaimableDayReward = false,
            },
            Missions = s.Missions with
            {
                State = ActivityState.Ready, DailyMissions = Array.Empty<GoalView>(),
                UntilDailyReset = TimeSpan.FromHours(20),
            },
            SpinWheel   = s.SpinWheel   with { State = ActivityState.Ready, SpinsAvailable = 0 },
            WeeklyEvent = s.WeeklyEvent with { State = ActivityState.Ready, Points = 0, UntilEnd = TimeSpan.FromDays(5) },
            Tournament  = s.Tournament  with { State = ActivityState.Ready, UntilStart = TimeSpan.FromDays(5), UntilEnd = null },
        };
    }

    private static GoalView Goal(string title, int progress, int target, bool claimed = false) =>
        new GoalView(title, progress, target, RewardView.Of(RewardViewItem.Of(CurrencyKind.Coins, 100)), claimed);

    private static FirstWeekDayView Day(int day, FirstWeekDayState state) =>
        new FirstWeekDayView(day, $"Day {day}", 1, 1, RewardView.Of(RewardViewItem.Of(CurrencyKind.Coins, 250)), state);

    #region The rungs, one at a time

    [Test]
    public void AFirstWeekDayAboutToTakeItsGoalsAwayOutranksEverything()
    {
        MetaSnapshot snapshot = NothingPending with
        {
            // Lower rungs also qualify, so this checks the ordering.
            DailyReward = NothingPending.DailyReward with { State = ActivityState.Actionable, HasClaimableReward = true },
            SpinWheel   = NothingPending.SpinWheel   with { State = ActivityState.Actionable, SpinsAvailable = 3 },
            FirstWeek   = NothingPending.FirstWeek with
            {
                State           = ActivityState.InProgress,
                TodaysGoals     = new[] { Goal("Score 500 points", 410, 500) },
                UntilDayExpires = TimeSpan.FromMinutes(47),
            },
        };

        NextActionChoice choice = NextActionPolicy.Choose(snapshot);

        Assert.That(choice.Kind, Is.EqualTo(NextActionKind.FirstWeekGoal));
        Assert.That(choice.Feature, Is.EqualTo(MetaFeature.FirstWeekEvent));
        Assert.That(choice.Route, Is.EqualTo(MetaRoutes.FirstWeekEvent));

        // With today's goal unfinished, the card counts down to the end of the day, when the goals expire.
        Assert.That(choice.UntilExpiry, Is.EqualTo(TimeSpan.FromMinutes(47)));
    }

    /// <summary>
    /// An earned first-week reward has <b>no</b> countdown when today has no open goal, because the reward does
    /// not expire with the day. When today's goal is still open, the card counts down to the end of the day.
    /// </summary>
    [Test]
    public void AFirstWeekRewardWaitingCarriesNoCountdownUnlessTodaysGoalIsStillOpen()
    {
        MetaSnapshot nothingLeftToday = NothingPending with
        {
            FirstWeek = NothingPending.FirstWeek with
            {
                State           = ActivityState.Actionable,
                TodaysGoals     = Array.Empty<GoalView>(),
                UntilDayExpires = TimeSpan.FromMinutes(47),
                Week            = new[] { Day(1, FirstWeekDayState.RewardReady) },
            },
        };

        NextActionChoice earned = NextActionPolicy.Choose(nothingLeftToday);

        Assert.That(earned.Kind, Is.EqualTo(NextActionKind.FirstWeekGoal));
        Assert.That(earned.UntilExpiry, Is.Null, "an earned reward outlives the day, so the day's clock is not its clock");

        MetaSnapshot goalStillOpen = NothingPending with
        {
            FirstWeek = NothingPending.FirstWeek with
            {
                State           = ActivityState.Actionable,
                TodaysGoals     = new[] { Goal("Score 500 points", 410, 500) },
                UntilDayExpires = TimeSpan.FromMinutes(47),
                Week            = new[] { Day(1, FirstWeekDayState.RewardReady), Day(2, FirstWeekDayState.InProgress) },
            },
        };

        NextActionChoice owed = NextActionPolicy.Choose(goalStillOpen);

        Assert.That(owed.Kind, Is.EqualTo(NextActionKind.FirstWeekGoal));
        Assert.That(owed.UntilExpiry, Is.EqualTo(TimeSpan.FromMinutes(47)),
            "the reward keeps, but today's goal still lapses with the day");
    }

    /// <summary>A finished first-week goal nobody has collected is as urgent as one about to lapse.</summary>
    [Test]
    public void AnUncollectedFirstWeekGoalAlsoTakesTheFirstRung()
    {
        MetaSnapshot snapshot = NothingPending with
        {
            FirstWeek = NothingPending.FirstWeek with
            {
                State           = ActivityState.InProgress,
                TodaysGoals     = new[] { Goal("Win 1 game", 1, 1, claimed: false) },
                UntilDayExpires = TimeSpan.FromHours(20),
            },
        };

        Assert.That(NextActionPolicy.Choose(snapshot).Kind, Is.EqualTo(NextActionKind.FirstWeekGoal));
    }

    /// <summary>
    /// A first-week day with hours left and nothing finished does <b>not</b> take the first rung. Otherwise the
    /// same card would lead Home for the whole first week.
    /// </summary>
    [Test]
    public void AFirstWeekDayWithHoursLeftIsNotYetUrgent()
    {
        MetaSnapshot snapshot = NothingPending with
        {
            FirstWeek = NothingPending.FirstWeek with
            {
                State           = ActivityState.InProgress,
                TodaysGoals     = new[] { Goal("Score 500 points", 0, 500) },
                UntilDayExpires = TimeSpan.FromHours(20),
            },
        };

        Assert.That(NextActionPolicy.Choose(snapshot).Kind, Is.Not.EqualTo(NextActionKind.FirstWeekGoal));
    }

    [Test]
    public void TodaysLoginRewardTakesTheSecondRung()
    {
        MetaSnapshot snapshot = NothingPending with
        {
            DailyReward = NothingPending.DailyReward with { State = ActivityState.Actionable, HasClaimableReward = true },
            SpinWheel   = NothingPending.SpinWheel   with { State = ActivityState.Actionable, SpinsAvailable = 3 },
        };

        NextActionChoice choice = NextActionPolicy.Choose(snapshot);

        Assert.That(choice.Kind, Is.EqualTo(NextActionKind.DailyReward));
        Assert.That(choice.Route, Is.EqualTo(MetaRoutes.DailyReward));
    }

    [Test]
    public void AFinishedButUncollectedMissionTakesTheThirdRung()
    {
        MetaSnapshot snapshot = NothingPending with
        {
            Missions = NothingPending.Missions with
            {
                State         = ActivityState.InProgress,
                DailyMissions = new[] { Goal("Win 2 games", 2, 2, claimed: false) },
            },
            SpinWheel = NothingPending.SpinWheel with { State = ActivityState.Actionable, SpinsAvailable = 3 },
        };

        Assert.That(NextActionPolicy.Choose(snapshot).Kind, Is.EqualTo(NextActionKind.MissionReward));
    }

    /// <summary>A finished mission that was already collected is not an opportunity.</summary>
    [Test]
    public void ACollectedMissionIsNotAnOpportunity()
    {
        MetaSnapshot snapshot = NothingPending with
        {
            Missions = NothingPending.Missions with
            {
                State         = ActivityState.Completed,
                DailyMissions = new[] { Goal("Win 2 games", 2, 2, claimed: true) },
            },
        };

        Assert.That(NextActionPolicy.Choose(snapshot).Kind, Is.Not.EqualTo(NextActionKind.MissionReward));
    }

    [Test]
    public void ASpinInHandTakesTheFourthRung()
    {
        MetaSnapshot snapshot = NothingPending with
        {
            SpinWheel = NothingPending.SpinWheel with { State = ActivityState.Actionable, SpinsAvailable = 1 },
        };

        Assert.That(NextActionPolicy.Choose(snapshot).Kind, Is.EqualTo(NextActionKind.Spin));
    }

    /// <summary>
    /// A claimable weekly event reward takes its own rung. A crossed target is progress at 1.0, which the
    /// nearest-progress rung excludes, so without this rung the player would be sent to Play.
    /// </summary>
    [Test]
    public void AWeeklyEventRewardWaitingTakesTheFifthRung()
    {
        MetaSnapshot snapshot = NothingPending with
        {
            WeeklyEvent = NothingPending.WeeklyEvent with
            {
                State = ActivityState.Actionable, Points = 1_000, TargetPoints = 1_000,
                HasClaimableReward = true, UntilEnd = TimeSpan.FromHours(2),
            },
        };

        NextActionChoice choice = NextActionPolicy.Choose(snapshot);

        Assert.That(choice.Kind, Is.EqualTo(NextActionKind.WeeklyEventReward));
        Assert.That(choice.Feature, Is.EqualTo(MetaFeature.WeeklyEvent));
        Assert.That(choice.Route, Is.EqualTo(MetaRoutes.WeeklyEvent));
    }

    /// <summary>
    /// A week whose scoring has closed with the target missed still has a countdown (the late-claim window),
    /// but the player can do nothing on that screen, so it must not take the ending-soon rung.
    /// </summary>
    [Test]
    public void AClosedWeekWithNothingOwedIsNotSomethingToHurryFor()
    {
        MetaSnapshot snapshot = NothingPending with
        {
            WeeklyEvent = NothingPending.WeeklyEvent with
            {
                State = ActivityState.Completed, Points = 400, TargetPoints = 1_000,
                IsScoring = false, UntilEnd = TimeSpan.FromHours(2),
            },
        };

        Assert.That(NextActionPolicy.Choose(snapshot).Kind, Is.EqualTo(NextActionKind.PlayInvitation),
            "a closed week took Home's card, on either the ending-soon rung or the nearest-progress one");
    }

    /// <summary>
    /// A week still scoring whose reward was already collected must not take the ending-soon rung, because the
    /// player has nothing left to gain.
    /// </summary>
    [Test]
    public void AWeekWhoseRewardIsAlreadyCollectedIsNotSomethingToHurryFor()
    {
        MetaSnapshot snapshot = NothingPending with
        {
            WeeklyEvent = NothingPending.WeeklyEvent with
            {
                State = ActivityState.InProgress, Points = 1_000, TargetPoints = 1_000,
                RewardClaimed = true, UntilEnd = TimeSpan.FromHours(2),
            },
        };

        Assert.That(NextActionPolicy.Choose(snapshot).Kind, Is.EqualTo(NextActionKind.PlayInvitation));
    }

    /// <summary>
    /// The same week with the target not yet reached does take the ending-soon rung.
    /// </summary>
    [Test]
    public void AWeekStillWinnableAndRunningOutIsStillSomethingToHurryFor()
    {
        MetaSnapshot snapshot = NothingPending with
        {
            WeeklyEvent = NothingPending.WeeklyEvent with
            {
                State = ActivityState.InProgress, Points = 400, TargetPoints = 1_000,
                UntilEnd = TimeSpan.FromHours(2),
            },
        };

        NextActionChoice choice = NextActionPolicy.Choose(snapshot);

        Assert.That(choice.Kind, Is.EqualTo(NextActionKind.EndingSoon));
        Assert.That(choice.Feature, Is.EqualTo(MetaFeature.WeeklyEvent));
    }

    /// <summary>
    /// An available spin outranks a claimable weekly event reward, because the weekly reward stays claimable for days.
    /// </summary>
    [Test]
    public void ASpinOutranksAWeeklyEventReward()
    {
        MetaSnapshot snapshot = NothingPending with
        {
            SpinWheel   = NothingPending.SpinWheel   with { State = ActivityState.Actionable, SpinsAvailable = 1 },
            WeeklyEvent = NothingPending.WeeklyEvent with { State = ActivityState.Actionable, HasClaimableReward = true },
        };

        Assert.That(NextActionPolicy.Choose(snapshot).Kind, Is.EqualTo(NextActionKind.Spin));
    }

    /// <summary>A claimable weekly event reward outranks the ending-soon rung.</summary>
    [Test]
    public void AWeeklyEventRewardOutranksAnythingMerelyEndingSoon()
    {
        MetaSnapshot snapshot = NothingPending with
        {
            WeeklyEvent = NothingPending.WeeklyEvent with { State = ActivityState.Actionable, HasClaimableReward = true },
            Tournament  = NothingPending.Tournament  with { State = ActivityState.InProgress, UntilStart = null, UntilEnd = TimeSpan.FromHours(2) },
        };

        Assert.That(NextActionPolicy.Choose(snapshot).Kind, Is.EqualTo(NextActionKind.WeeklyEventReward));
    }

    [Test]
    public void ATimedActivityRunningOutTakesTheSixthRung()
    {
        MetaSnapshot snapshot = NothingPending with
        {
            WeeklyEvent = NothingPending.WeeklyEvent with
            {
                State = ActivityState.InProgress, Points = 620, UntilEnd = TimeSpan.FromHours(3),
            },
        };

        NextActionChoice choice = NextActionPolicy.Choose(snapshot);

        Assert.That(choice.Kind, Is.EqualTo(NextActionKind.EndingSoon));
        Assert.That(choice.Feature, Is.EqualTo(MetaFeature.WeeklyEvent));
        Assert.That(choice.UntilExpiry, Is.EqualTo(TimeSpan.FromHours(3)));
    }

    [Test]
    public void TheNearestThingToFinishedTakesTheSeventhRung()
    {
        MetaSnapshot snapshot = NothingPending with
        {
            WeeklyEvent = NothingPending.WeeklyEvent with { State = ActivityState.InProgress, Points = 620 },
        };

        NextActionChoice choice = NextActionPolicy.Choose(snapshot);

        Assert.That(choice.Kind, Is.EqualTo(NextActionKind.NearestProgress));
        Assert.That(choice.Progress, Is.EqualTo(0.62).Within(0.001));
    }

    /// <summary>
    /// When nothing else qualifies, the choice is Play, so the policy always returns a card. Play has no route
    /// because the card's button enters matchmaking from the current screen.
    /// </summary>
    [Test]
    public void WithNothingPendingTheAnswerIsTheGame()
    {
        NextActionChoice choice = NextActionPolicy.Choose(NothingPending);

        Assert.That(choice.Kind, Is.EqualTo(NextActionKind.PlayInvitation));
        Assert.That(choice.Feature, Is.EqualTo(MetaFeature.Play));
        Assert.That(choice.Route, Is.Null);
    }

    #endregion

    #region Tie-breaking and exclusions

    /// <summary>When two activities are ending soon, the one with the nearer deadline wins.</summary>
    [Test]
    public void AmongThingsEndingSoonTheNearestDeadlineWins()
    {
        MetaSnapshot snapshot = NothingPending with
        {
            WeeklyEvent = NothingPending.WeeklyEvent with { State = ActivityState.InProgress, UntilEnd = TimeSpan.FromHours(9) },
            Tournament  = NothingPending.Tournament  with { State = ActivityState.InProgress, UntilStart = null, UntilEnd = TimeSpan.FromHours(3) },
        };

        Assert.That(NextActionPolicy.Choose(snapshot).Feature, Is.EqualTo(MetaFeature.Tournament));
    }

    /// <summary>
    /// Equal deadlines are resolved by the stable feature order. Otherwise the Next-up card could switch between
    /// the two activities on every render.
    /// </summary>
    [Test]
    public void ADeadHeatFallsBackToTheStableFeatureOrder()
    {
        MetaSnapshot snapshot = NothingPending with
        {
            WeeklyEvent = NothingPending.WeeklyEvent with { State = ActivityState.InProgress, UntilEnd = TimeSpan.FromHours(3) },
            Tournament  = NothingPending.Tournament  with { State = ActivityState.InProgress, UntilStart = null, UntilEnd = TimeSpan.FromHours(3) },
        };

        NextActionChoice first = NextActionPolicy.Choose(snapshot);

        Assert.That(first.Feature, Is.EqualTo(MetaFeature.WeeklyEvent),
            "the weekly event is declared before the tournament, so it wins a tie");
        Assert.That(NextActionPolicy.Choose(snapshot), Is.EqualTo(first),
            "the same state must produce the same answer every time it is asked");
    }

    /// <summary>Completed progress must not win the nearest-progress rung.</summary>
    [Test]
    public void FinishedProgressIsNotTheNearestProgress()
    {
        MetaSnapshot snapshot = NothingPending with
        {
            WeeklyEvent = NothingPending.WeeklyEvent with
            {
                State = ActivityState.InProgress, Points = 1_000, TargetPoints = 1_000,
            },
        };

        Assert.That(NextActionPolicy.Choose(snapshot).Kind, Is.EqualTo(NextActionKind.PlayInvitation));
    }

    /// <summary>
    /// A feature whose data has not loaded must not win a rung on its placeholder default values.
    /// </summary>
    [TestCase(ActivityState.Loading)]
    [TestCase(ActivityState.Offline)]
    [TestCase(ActivityState.Error)]
    public void AnUnhealthySurfaceIsNeverTheNextAction(ActivityState unhealthy)
    {
        MetaSnapshot snapshot = NothingPending with
        {
            DailyReward = NothingPending.DailyReward with { State = unhealthy, HasClaimableReward = true },
            SpinWheel   = NothingPending.SpinWheel   with { State = unhealthy, SpinsAvailable = 5 },
        };

        Assert.That(NextActionPolicy.Choose(snapshot).Kind, Is.EqualTo(NextActionKind.PlayInvitation));
    }

    /// <summary>An expired activity does not win, whatever remaining time it reports.</summary>
    [Test]
    public void AnExpiredActivityIsNeverTheNextAction()
    {
        MetaSnapshot snapshot = NothingPending with
        {
            FirstWeek = NothingPending.FirstWeek with
            {
                State           = ActivityState.Expired,
                TodaysGoals     = new[] { Goal("Score 500 points", 410, 500) },
                UntilDayExpires = TimeSpan.Zero,
            },
        };

        Assert.That(NextActionPolicy.Choose(snapshot).Kind, Is.Not.EqualTo(NextActionKind.FirstWeekGoal));
    }

    /// <summary>Every fixture scenario produces a Next-up card.</summary>
    [Test]
    public void EveryFixtureProducesANextAction()
    {
        foreach (string scenario in MetaFixtures.Scenarios.Keys)
        {
            MetaSnapshot snapshot = MetaFixtures.Build(scenario, TimeSpan.Zero);
            Assert.That(NextActionPolicy.Choose(snapshot), Is.Not.Null, $"{scenario} produced no next action");
        }
    }

    #endregion
}
