using System;
using System.Linq;
using WebClient.Meta;
using WebClient.Meta.Fixtures;

namespace WebClient.Tests;

/// <summary>
/// Invariants of the fixture scenarios that the Events hub draws. A hub state that no scenario produces cannot be
/// seen or reviewed in the fixture pages.
/// </summary>
[TestFixture]
public class EventsHubFixtureTests
{
    /// <summary>
    /// The approved Events design leads with the featured First-Week card. The hub sorts actionable features first,
    /// so the <c>first-week</c> scenario must make the first-week day <see cref="ActivityState.Actionable"/>.
    /// </summary>
    [Test]
    public void TheFirstWeekScenarioLeadsItsHubWithTheFirstWeekEvent()
    {
        MetaSnapshot snapshot = MetaFixtures.Build("first-week", TimeSpan.Zero);

        Assert.That(snapshot.FirstWeek.State, Is.EqualTo(ActivityState.Actionable),
            "a day with goals open and minutes left is a day the player can still change");

        IFeatureView lead = FeatureOrdering.Sort(snapshot.EventsFeatures).First();

        Assert.That(lead.Feature, Is.EqualTo(MetaFeature.FirstWeekEvent),
            "the approved featured card is not reachable in any scenario");
    }

    /// <summary>
    /// The <c>fresh</c> scenario is a new player with empty balances and no progress, so the pages can be checked
    /// for text that assumes past activity.
    /// </summary>
    [Test]
    public void TheFreshScenarioIsAPlayerWhoHasDoneNothingYet()
    {
        MetaSnapshot s = MetaFixtures.Build("fresh", TimeSpan.Zero);

        Assert.That(s.Wallet.Coins, Is.Zero);
        Assert.That(s.Wallet.Gems, Is.Zero);
        Assert.That(s.Wallet.SpinTokens, Is.Zero);

        Assert.That(s.DailyReward.StreakDays, Is.Zero, "a new player has no streak");
        Assert.That(s.FirstWeek.CurrentDay, Is.EqualTo(1));
        Assert.That(s.FirstWeek.GoalsComplete, Is.Zero,
            "a new player cannot already have finished today's goals");
        Assert.That(s.FirstWeek.TodaysGoals.Any(g => g.IsClaimed), Is.False,
            "and cannot already have collected one");

        Assert.That(s.Missions.DailyMissionsTotal, Is.Zero);
        Assert.That(s.SpinWheel.SpinsAvailable, Is.Zero);
    }

    /// <summary>
    /// Having a reward to claim is separate from the activity state. In the <c>missions</c> scenario a mission reward
    /// is claimable while the mission list as a whole is <see cref="ActivityState.InProgress"/>.
    /// </summary>
    [Test]
    public void AMissionListWithARewardWaitingIsReadyWithoutBeingActionable()
    {
        MetaSnapshot s = MetaFixtures.Build("missions", TimeSpan.Zero);

        Assert.That(s.Missions.ClaimableCount, Is.GreaterThan(0), "the scenario has a reward waiting");
        Assert.That(s.Missions.State, Is.EqualTo(ActivityState.InProgress),
            "and the list as a whole is still running");
    }
}
