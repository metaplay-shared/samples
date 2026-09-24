using System;
using System.Collections.Generic;
using System.Linq;
using WebClient.Meta;
using WebClient.Meta.Fixtures;

namespace WebClient.Tests;

/// <summary>
/// Tests for the progress ladders built by <see cref="StepRailBuilder.ForCycle"/> (daily reward cycle) and
/// <see cref="StepRailBuilder.ForFirstWeekDays"/> (first-week event), using fixture views. Each step's state is computed from
/// the view model alone, so it can be tested without rendering a component.
/// </summary>
[TestFixture]
public class StepRailBuilderTests
{
    /// <summary>The default fixture's daily reward view, with <paramref name="claimed"/> steps claimed in the cycle.</summary>
    private static DailyRewardView Cycle(int claimed) =>
        MetaFixtures.Build(MetaFixtures.Default, TimeSpan.Zero).DailyReward with { StepsClaimedInCycle = claimed };

    /// <summary>
    /// Claimed steps are Done, the next step (today's reward) is Current, later steps are Future, and the last step
    /// is Prize. No step is Locked, because the daily reward cycle has no requirement other than logging in.
    /// </summary>
    [Test]
    public void TheCycleLadderMarksTheClaimedStepsDoneAndTheNextOneCurrent()
    {
        IReadOnlyList<RailStep> ladder = StepRailBuilder.ForCycle(Cycle(claimed: 3));

        Assert.That(ladder, Has.Count.EqualTo(7), "the cycle is seven steps long");
        Assert.That(ladder.Select(s => s.State), Is.EqualTo(new[]
        {
            RailStepState.Done, RailStepState.Done, RailStepState.Done,
            RailStepState.Current,
            RailStepState.Future, RailStepState.Future,
            RailStepState.Prize,
        }));

        Assert.That(ladder.Any(s => s.State == RailStepState.Locked), Is.False,
            "a login streak has no locked rungs");
    }

    /// <summary>With nothing claimed in the cycle, the first step is Current and no step is Done.</summary>
    [Test]
    public void AtTheStartOfACycleNoStepIsDone()
    {
        IReadOnlyList<RailStep> ladder = StepRailBuilder.ForCycle(Cycle(claimed: 0));

        Assert.That(ladder.Any(s => s.State == RailStepState.Done), Is.False);
        Assert.That(ladder[0].State, Is.EqualTo(RailStepState.Current));
    }

    /// <summary>
    /// When every step but the last is claimed, the last step is Current instead of Prize, and it is the only
    /// Current step.
    /// </summary>
    [Test]
    public void TheFinalStepBecomesCurrentWhenItIsTheOneBeingClaimed()
    {
        IReadOnlyList<RailStep> ladder = StepRailBuilder.ForCycle(Cycle(claimed: 6));

        Assert.That(ladder[6].State, Is.EqualTo(RailStepState.Current));
        Assert.That(ladder.Count(s => s.State == RailStepState.Current), Is.EqualTo(1));
    }

    /// <summary>When every step is claimed, every step is Done.</summary>
    [Test]
    public void AFinishedCycleMarksEveryStepDone()
    {
        Assert.That(StepRailBuilder.ForCycle(Cycle(claimed: 7)).Select(s => s.State), Is.All.EqualTo(RailStepState.Done));
    }

    /// <summary>
    /// Every step carries its reward value. The on-screen row of values is hidden from assistive technology,
    /// because it is laid out to line up with the ladder, not to be read in order. Screen readers get each value
    /// from its step.
    /// </summary>
    [Test]
    public void EveryCycleRungCarriesItsValue()
    {
        foreach (RailStep step in StepRailBuilder.ForCycle(Cycle(claimed: 3)))
            Assert.That(step.Caption, Is.Not.Null.And.Not.Empty, $"{step.Label} has no value to announce");
    }

    /// <summary>
    /// First-week days after today are Locked, not Future, because the event opens one day at a time. Earlier days
    /// are Done, today is Current, and the last day is Prize.
    /// </summary>
    [Test]
    public void TheEventLadderLocksTheDaysAheadAndMarksTheLastOneAsThePrize()
    {
        FirstWeekView view = MetaFixtures.Build("first-week", TimeSpan.Zero).FirstWeek;
        IReadOnlyList<RailStep> days = StepRailBuilder.ForFirstWeekDays(view);

        Assert.That(days, Has.Count.EqualTo(view.TotalDays));
        Assert.That(days[view.CurrentDay - 1].State, Is.EqualTo(RailStepState.Current));
        Assert.That(days.Take(view.CurrentDay - 1).Select(d => d.State),
            Is.All.EqualTo(RailStepState.Done));
        Assert.That(days[^1].State, Is.EqualTo(RailStepState.Prize));
        Assert.That(days.Skip(view.CurrentDay).SkipLast(1).Select(d => d.State),
            Is.All.EqualTo(RailStepState.Locked));
    }

    /// <summary>On the first day of the event, no step is Done.</summary>
    [Test]
    public void OnTheFirstDayNoStepIsMarkedDone()
    {
        FirstWeekView view = MetaFixtures.Build("fresh", TimeSpan.Zero).FirstWeek;

        Assert.That(view.CurrentDay, Is.EqualTo(1));
        Assert.That(StepRailBuilder.ForFirstWeekDays(view).Any(d => d.State == RailStepState.Done), Is.False);
    }

    /// <summary>
    /// A missed day is Missed, not Done, so the ladder agrees with the day tile below it, which shows the day as
    /// missed.
    /// </summary>
    [Test]
    public void AMissedDayIsDrawnAsMissedRatherThanDone()
    {
        FirstWeekView view = MetaFixtures.Build("first-week-reward", TimeSpan.Zero).FirstWeek;
        IReadOnlyList<RailStep> days = StepRailBuilder.ForFirstWeekDays(view);

        int missed = view.Days.Single(d => d.State == FirstWeekDayState.Missed).Day;

        Assert.That(days[missed - 1].State, Is.EqualTo(RailStepState.Missed));
        Assert.That(days.Count(d => d.State == RailStepState.Done), Is.EqualTo(view.CurrentDay - 2),
            "every earlier day except the missed one is behind the player and taken");
    }

    /// <summary>
    /// A day whose goal is met but whose reward is not yet claimed is Done. The reward is already earned, and the
    /// Claim button is on the day tile, not on the ladder.
    /// </summary>
    [Test]
    public void ADayWhoseRewardIsStillOwedIsBehindThePlayer()
    {
        FirstWeekView view = MetaFixtures.Build("first-week-reward", TimeSpan.Zero).FirstWeek;
        int ready = view.Days.Single(d => d.State == FirstWeekDayState.RewardReady).Day;

        Assert.That(StepRailBuilder.ForFirstWeekDays(view)[ready - 1].State, Is.EqualTo(RailStepState.Done));
    }

    /// <summary>
    /// After the week ends, no step is Current or Prize, so the ladder does not suggest the event is still
    /// running.
    /// </summary>
    [Test]
    public void AFinishedWeekHasNoCurrentDay()
    {
        FirstWeekView view = Ended();
        IReadOnlyList<RailStep> days = StepRailBuilder.ForFirstWeekDays(view);

        Assert.That(days.Any(d => d.State == RailStepState.Current), Is.False);
        Assert.That(days.Any(d => d.State == RailStepState.Prize), Is.False);
        Assert.That(days[^1].State, Is.EqualTo(RailStepState.Done), "day seven was played and claimed");
        Assert.That(days.Count(d => d.State == RailStepState.Missed), Is.EqualTo(view.MissedDays));
    }

    /// <summary>A first-week view that has ended on its last day, with some days claimed and some missed.</summary>
    private static FirstWeekView Ended()
    {
        FirstWeekView week = MetaFixtures.Build("first-week-reward", TimeSpan.Zero).FirstWeek;

        FirstWeekDayState[] states =
        {
            FirstWeekDayState.Claimed, FirstWeekDayState.Claimed, FirstWeekDayState.Missed,
            FirstWeekDayState.Claimed, FirstWeekDayState.Missed, FirstWeekDayState.Claimed,
            FirstWeekDayState.Claimed,
        };

        return week with
        {
            CurrentDay = 7,
            HasEnded   = true,
            Week       = week.Days.Select((day, index) => day with { State = states[index] }).ToList(),
        };
    }
}
