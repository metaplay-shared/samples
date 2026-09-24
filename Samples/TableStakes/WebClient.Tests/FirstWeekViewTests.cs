using Game.Logic;
using Metaplay.Core;
using WebClient.Meta;

namespace WebClient.Tests;

/// <summary>
/// Tests for <see cref="FirstWeekViewBuilder"/>, the client's view of the first-week event: day titles, reward
/// conversion, and the hub card's headline and summary. The active day, its goal and the owed rewards come from
/// the player model.
/// <para>
/// Building a <c>SharedGameConfig</c> needs Metaplay's type registry, and this project does not initialize
/// Metaplay core because its Playwright fixtures share the process. The config-driven part of the mapping is
/// covered by <c>FirstWeekTests</c> in shared code and by the live-server E2E tests.
/// </para>
/// </summary>
[TestFixture]
public class FirstWeekViewTests
{
    [TestCase(1, "Complete 1 match")]
    [TestCase(2, "Complete 2 matches")]
    public void ADayIsNamedAfterHowManyMatchesItAsksFor(int target, string expected)
    {
        Assert.That(FirstWeekViewBuilder.TitleOf(target), Is.EqualTo(expected));
    }

    [Test]
    public void ARewardCrossesFromConfigWithItsCurrenciesInOrder()
    {
        RewardView capstone = FirstWeekViewBuilder.RewardOf(new Game.Logic.RewardBundle(
            CurrencyAmount.Coins(1500), CurrencyAmount.Gems(100), CurrencyAmount.SpinTokens(2)));

        Assert.That(capstone.Items.Select(item => item.Currency),
            Is.EqualTo(new CurrencyKind?[] { CurrencyKind.Coins, CurrencyKind.Gems, CurrencyKind.SpinTokens }));
        Assert.That(capstone.Items.Select(item => item.Amount), Is.EqualTo(new long[] { 1500, 100, 2 }));
        Assert.That(capstone.Items, Is.All.Matches<RewardViewItem>(item => item.TravelsToWallet),
            "every first-week reward is currency, so every one of them flies to a balance");
    }

    [Test]
    public void ARewardWithNothingInItCrossesAsEmptyRatherThanAsNull()
    {
        Assert.That(FirstWeekViewBuilder.RewardOf(null!).IsEmpty, Is.True);
        Assert.That(FirstWeekViewBuilder.RewardOf(new Game.Logic.RewardBundle()).IsEmpty, Is.True);
    }

    [Test]
    public void NoStateAndNoConfigAreBothUnavailable()
    {
        // A player with no first-week state, or a config with no first-week entries, gets the Unavailable view
        // instead of a failed render.
        Assert.That(FirstWeekViewBuilder.From(null!, null!, MetaTime.Epoch).State, Is.EqualTo(ActivityState.Unavailable));
        Assert.That(FirstWeekViewBuilder.From(new PlayerFirstWeekState(), null!, MetaTime.Epoch).State, Is.EqualTo(ActivityState.Unavailable));
        Assert.That(FirstWeekViewBuilder.Unavailable.Days, Is.Empty);
        Assert.That(FirstWeekViewBuilder.Unavailable.TodaysGoals, Is.Empty);
    }

    /// <summary>
    /// When a reward is ready to claim, the hub summary shows the reward count instead of today's goal. A ready
    /// reward stays claimable after its day ends, so the hub keeps pointing at it.
    /// </summary>
    [Test]
    public void TheHubLineLeadsWithARewardThatIsStillOwed()
    {
        FirstWeekView view = Week(day: 5, states: new[]
        {
            FirstWeekDayState.RewardReady, FirstWeekDayState.Missed, FirstWeekDayState.Claimed,
            FirstWeekDayState.Missed, FirstWeekDayState.InProgress, FirstWeekDayState.Future, FirstWeekDayState.Future,
        });

        Assert.That(view.ClaimableDays, Is.EqualTo(1));
        Assert.That(view.Headline, Is.EqualTo("Day 5 of 7"));
        Assert.That(view.Summary, Is.EqualTo("1 reward ready"));
    }

    /// <summary>With no reward ready, the hub summary shows today's goal and its progress.</summary>
    [Test]
    public void TheHubLineNamesTodaysGoalWhileNothingIsWaiting()
    {
        FirstWeekView view = Week(day: 3, states: new[]
        {
            FirstWeekDayState.Claimed, FirstWeekDayState.Missed, FirstWeekDayState.InProgress,
            FirstWeekDayState.Future, FirstWeekDayState.Future, FirstWeekDayState.Future, FirstWeekDayState.Future,
        });

        Assert.That(view.ClaimableDays, Is.Zero);
        Assert.That(view.Summary, Is.EqualTo("Complete 2 matches · 0/2"));
    }

    /// <summary>
    /// After the week ends, the headline says the week is complete and the summary counts claimed and missed days,
    /// instead of showing a day that no longer exists.
    /// </summary>
    [Test]
    public void AFinishedWeekReportsWhatItCameTo()
    {
        FirstWeekView view = Week(day: 7, hasEnded: true, states: new[]
        {
            FirstWeekDayState.Claimed, FirstWeekDayState.Claimed, FirstWeekDayState.Missed,
            FirstWeekDayState.Claimed, FirstWeekDayState.Missed, FirstWeekDayState.Claimed, FirstWeekDayState.Claimed,
        });

        Assert.That(view.Headline, Is.EqualTo("First week complete"));
        Assert.That(view.Summary, Is.EqualTo("5 claimed · 2 missed"));
    }

    /// <summary>
    /// A reward earned before the week ended can still be claimed after it, so the summary shows the ready count
    /// instead of the claimed and missed totals.
    /// </summary>
    [Test]
    public void AFinishedWeekStillLeadsWithAnEarnedRewardNobodyCollected()
    {
        FirstWeekView view = Week(day: 7, hasEnded: true, states: new[]
        {
            FirstWeekDayState.RewardReady, FirstWeekDayState.Missed, FirstWeekDayState.Missed,
            FirstWeekDayState.Missed, FirstWeekDayState.Missed, FirstWeekDayState.Missed, FirstWeekDayState.RewardReady,
        });

        Assert.That(view.ClaimableDays, Is.EqualTo(2));
        Assert.That(view.Summary, Is.EqualTo("2 rewards ready"));
    }

    /// <summary>Only a day in the RewardReady state is claimable.</summary>
    [Test]
    public void OnlyAReadyDayIsClaimable()
    {
        FirstWeekView view = Week(day: 4, states: new[]
        {
            FirstWeekDayState.Claimed, FirstWeekDayState.Missed, FirstWeekDayState.RewardReady,
            FirstWeekDayState.InProgress, FirstWeekDayState.Future, FirstWeekDayState.Future, FirstWeekDayState.Future,
        });

        Assert.That(view.Days.Where(d => d.IsClaimable).Select(d => d.Day), Is.EqualTo(new[] { 3 }));
        Assert.That(view.ClaimedDays, Is.EqualTo(1));
        Assert.That(view.MissedDays, Is.EqualTo(1));
    }

    /// <summary>
    /// A day counts as ending soon only in its last hour. A threshold scaled to the day's length would make every
    /// day urgent from its start, and the card would stay on Home for the whole week.
    /// </summary>
    [Test]
    public void ADayIsOnlyEndingSoonInItsLastHour()
    {
        FirstWeekView view = Week(day: 1, states: Enumerable.Repeat(FirstWeekDayState.Future, 7).ToArray());

        Assert.That(view.EndingSoonWithin, Is.EqualTo(TimeSpan.FromHours(1)));
        Assert.That(Countdown.PhaseOf(TimeSpan.FromHours(20), view.EndingSoonWithin), Is.Not.EqualTo(CountdownPhase.EndingSoon));
        Assert.That(Countdown.PhaseOf(TimeSpan.FromMinutes(40), view.EndingSoonWithin), Is.EqualTo(CountdownPhase.EndingSoon));
    }

    /// <summary>The derived properties of a view with no days return zero or empty values instead of throwing.</summary>
    [Test]
    public void AViewWithNoDaysStillAnswers()
    {
        FirstWeekView view = FirstWeekViewBuilder.Unavailable;

        Assert.That(view.ClaimableDays, Is.Zero);
        Assert.That(view.ClaimedDays, Is.Zero);
        Assert.That(view.MissedDays, Is.Zero);
        Assert.That(view.Progress, Is.Null);
        Assert.That(view.GoalLine, Is.Empty);
    }

    /// <summary>
    /// The instruction asks the player to finish today's goal only while today's goal is unfinished. The
    /// instruction is drawn directly above the goal's progress, so it must agree with it. A ready reward from an
    /// earlier day does not change the instruction.
    /// </summary>
    [Test]
    public void TheDayStopsAskingForAGoalItAlreadyHas()
    {
        FirstWeekDayState[] unfinished =
        {
            FirstWeekDayState.Claimed, FirstWeekDayState.InProgress, FirstWeekDayState.Future,
            FirstWeekDayState.Future, FirstWeekDayState.Future, FirstWeekDayState.Future, FirstWeekDayState.Future,
        };

        Assert.That(Week(day: 2, states: unfinished).Instruction,
            Is.EqualTo("Complete today's goal before the day runs out."));

        FirstWeekDayState[] earned = (FirstWeekDayState[])unfinished.Clone();
        earned[1] = FirstWeekDayState.RewardReady;

        Assert.That(Week(day: 2, states: earned).Instruction, Is.EqualTo("Your reward is waiting below."));

        FirstWeekDayState[] collected = (FirstWeekDayState[])unfinished.Clone();
        collected[1] = FirstWeekDayState.Claimed;

        Assert.That(Week(day: 2, states: collected).Instruction,
            Is.EqualTo("Today's goal is done. The next one opens when the day turns."));

        // Day one's reward is ready and today's goal is unfinished, so the instruction still asks for the goal.
        FirstWeekDayState[] owedFromEarlierDay = (FirstWeekDayState[])unfinished.Clone();
        owedFromEarlierDay[0] = FirstWeekDayState.RewardReady;

        FirstWeekView mixed = Week(day: 2, states: owedFromEarlierDay);

        Assert.That(mixed.ClaimableDays, Is.EqualTo(1));
        Assert.That(mixed.Instruction, Is.EqualTo("Complete today's goal before the day runs out."));
    }

    /// <summary>
    /// <see cref="FirstWeekView.UntilDeadline"/> is set only while today's goal is unfinished, because the end of the
    /// day can only cost the player an unfinished goal. An earned reward stays claimable, so meeting today's goal
    /// clears the urgency. A ready reward from an earlier day does not change it.
    /// </summary>
    [Test]
    public void TheDayClockRunsOnlyWhileTodaysGoalIsUnfinished()
    {
        FirstWeekView unfinished = Week(day: 2, states: new[]
        {
            FirstWeekDayState.Claimed, FirstWeekDayState.InProgress, FirstWeekDayState.Future,
            FirstWeekDayState.Future, FirstWeekDayState.Future, FirstWeekDayState.Future, FirstWeekDayState.Future,
        });

        Assert.That(unfinished.UntilDayExpires, Is.EqualTo(TimeSpan.FromHours(6)));
        Assert.That(unfinished.UntilDeadline, Is.EqualTo(TimeSpan.FromHours(6)));

        FirstWeekView earnedToday = Week(day: 2, states: new[]
        {
            FirstWeekDayState.Claimed, FirstWeekDayState.RewardReady, FirstWeekDayState.Future,
            FirstWeekDayState.Future, FirstWeekDayState.Future, FirstWeekDayState.Future, FirstWeekDayState.Future,
        });

        Assert.That(earnedToday.UntilDayExpires, Is.EqualTo(TimeSpan.FromHours(6)),
            "the day still ends when it ends");
        Assert.That(earnedToday.UntilDeadline, Is.Null, "a met goal has nothing left for the window to take away");

        FirstWeekView owedFromEarlierDay = Week(day: 2, states: new[]
        {
            FirstWeekDayState.RewardReady, FirstWeekDayState.InProgress, FirstWeekDayState.Future,
            FirstWeekDayState.Future, FirstWeekDayState.Future, FirstWeekDayState.Future, FirstWeekDayState.Future,
        });

        Assert.That(owedFromEarlierDay.ClaimableDays, Is.EqualTo(1));
        Assert.That(owedFromEarlierDay.UntilDeadline, Is.EqualTo(TimeSpan.FromHours(6)),
            "an older reward keeps, but today's goal can still lapse");
    }

    /// <summary>Builds a view with the given active day and day states directly, without the config that
    /// <see cref="FirstWeekViewBuilder.From"/> reads.</summary>
    private static FirstWeekView Week(int day, FirstWeekDayState[] states, bool hasEnded = false)
    {
        int[] goals = { 1, 1, 2, 2, 2, 3, 1 };

        List<FirstWeekDayView> week = new List<FirstWeekDayView>();
        for (int index = 1; index <= states.Length; index++)
        {
            week.Add(new FirstWeekDayView(
                Day:      index,
                Title:    FirstWeekViewBuilder.TitleOf(goals[index - 1]),
                Progress: states[index - 1] is FirstWeekDayState.Claimed or FirstWeekDayState.RewardReady ? goals[index - 1] : 0,
                Target:   goals[index - 1],
                Reward:   RewardView.Of(RewardViewItem.Of(CurrencyKind.Coins, 250)),
                State:    states[index - 1],
                Id:       $"firstweek.v1.day{index}"));
        }

        FirstWeekDayView active = week[day - 1];

        return FirstWeekViewBuilder.Unavailable with
        {
            State           = ActivityState.InProgress,
            CurrentDay      = day,
            TotalDays       = states.Length,
            TodaysGoals     = hasEnded
                                ? Array.Empty<GoalView>()
                                : new[] { new GoalView(active.Title, active.Progress, active.Target, active.Reward, active.State == FirstWeekDayState.Claimed, active.Id) },
            UntilDayExpires = hasEnded ? null : TimeSpan.FromHours(6),
            Week            = week,
            HasEnded        = hasEnded,
        };
    }
}
