using Game.Logic;
using Metaplay.Core;
using WebClient.Meta;

namespace WebClient.Tests;

/// <summary>
/// <see cref="MissionsViewBuilder"/>: the parts of the missions screen the client decides itself, which are a goal's title,
/// its reward display and the time left. What a mission counts, when it resets and what it pays come from the
/// player model.
/// <para>
/// These tests cannot build a <c>SharedGameConfig</c>, because that needs Metaplay's type registry and this
/// project runs without the Metaplay core initialized. The config-driven part of the mapping is covered by
/// <c>MissionTests</c> in SharedCode.Tests and by the live E2E tests.
/// </para>
/// </summary>
[TestFixture]
public class MissionViewTests
{
    [TestCase("daily.play1", MissionCadence.Daily,  MissionObjective.MatchesCompleted, 1, "Play 1 game")]
    [TestCase("daily.play3", MissionCadence.Daily,  MissionObjective.MatchesCompleted, 3, "Play 3 games")]
    [TestCase("daily.win1",  MissionCadence.Daily,  MissionObjective.MatchesWon,       1, "Win 1 game")]
    [TestCase("weekly.win3", MissionCadence.Weekly, MissionObjective.MatchesWon,       3, "Win 3 games")]
    public void AGoalIsNamedAfterWhatItCountsAndHowMuchOfIt(string id, MissionCadence cadence, MissionObjective objective, int target, string expected)
    {
        MissionInfo mission = new MissionInfo(MissionId.FromString(id), cadence, objective, target, new Game.Logic.RewardBundle(CurrencyAmount.Coins(100)));

        Assert.That(MissionsViewBuilder.TitleOf(mission), Is.EqualTo(expected));
    }

    [Test]
    public void ARewardCrossesFromConfigWithItsCurrenciesInOrder()
    {
        RewardView both = MissionsViewBuilder.RewardOf(
            new Game.Logic.RewardBundle(CurrencyAmount.Coins(150), CurrencyAmount.SpinTokens(1)));

        Assert.That(both.Items.Select(item => item.Currency), Is.EqualTo(new CurrencyKind?[] { CurrencyKind.Coins, CurrencyKind.SpinTokens }));
        Assert.That(both.Items.Select(item => item.Amount), Is.EqualTo(new long[] { 150, 1 }));
        Assert.That(both.Items, Is.All.Matches<RewardViewItem>(item => item.TravelsToWallet),
            "every mission reward is currency, so every one of them flies to a balance");
    }

    [Test]
    public void ARewardWithNothingInItCrossesAsEmptyRatherThanAsNull()
    {
        Assert.That(MissionsViewBuilder.RewardOf(null!).IsEmpty, Is.True);
        Assert.That(MissionsViewBuilder.RewardOf(new Game.Logic.RewardBundle()).IsEmpty, Is.True);
    }

    [TestCase(3,  3)]
    [TestCase(0,  0)]
    [TestCase(-3, 0)]
    public void AClockThatHasRunOutShowsZeroRatherThanNegativeTime(int hoursUntilDeadline, int expectedHours)
    {
        MetaTime now = MetaTime.FromDateTime(new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc));

        Assert.That(MissionsViewBuilder.TimeLeft(now + MetaDuration.FromHours(hoursUntilDeadline), now), Is.EqualTo(TimeSpan.FromHours(expectedHours)));
    }

    [Test]
    public void NoStateAndNoConfigAreBothUnavailable()
    {
        // A player whose saved state predates missions gets the Unavailable view instead of a render failure.
        Assert.That(MissionsViewBuilder.From(null!, null!, MetaTime.Epoch, MetaDuration.Zero).State, Is.EqualTo(ActivityState.Unavailable));
        Assert.That(MissionsViewBuilder.From(new PlayerMissionState(), null!, MetaTime.Epoch, MetaDuration.Zero).State, Is.EqualTo(ActivityState.Unavailable));
        Assert.That(MissionsViewBuilder.Unavailable.DailyMissions, Is.Empty);
        Assert.That(MissionsViewBuilder.Unavailable.WeeklyMissions, Is.Empty);
    }

    /// <summary>
    /// <see cref="MissionsView.ClaimableCount"/> counts completed, unclaimed goals in both the daily list and the late-claim
    /// list (goals whose claim window is about to close), because it drives the Events badge.
    /// <see cref="MissionsView.DailyMissionsTotal"/> and <see cref="MissionsView.DailyMissionsComplete"/> count the
    /// daily list only.
    /// </summary>
    [Test]
    public void AClaimableGoalIsCountedWhereverItSits()
    {
        MissionsView view = new MissionsView(
            ActivityState.InProgress,
            new[] { new GoalView("Win 1 game", 1, 1, RewardView.Empty, IsClaimed: false, Id: "a") },
            TimeSpan.FromHours(2),
            Weekly: new[] { new GoalView("Play 10 games", 4, 10, RewardView.Empty, IsClaimed: false, Id: "b") },
            UntilWeeklyReset: TimeSpan.FromDays(3),
            LateClaim: new[] { new GoalView("Play 3 games", 3, 3, RewardView.Empty, IsClaimed: false, Id: "c") },
            UntilLateClaimEnds: TimeSpan.FromHours(5));

        Assert.That(view.ClaimableCount, Is.EqualTo(2), "a reward about to expire still has to raise the badge");
        Assert.That(view.DailyMissionsTotal, Is.EqualTo(1), "the hub summarises the daily set and nothing else");
        Assert.That(view.DailyMissionsComplete, Is.EqualTo(1));
        Assert.That(view.Nearest?.Title, Is.Null, "an already-finished mission is not the nearest thing to do");
    }

    [Test]
    public void AViewBuiltWithoutAWeeklySetOrALateClaimWindowAnswersWithEmptyLists()
    {
        MissionsView view = new MissionsView(ActivityState.InProgress, Array.Empty<GoalView>(), TimeSpan.FromHours(1));

        Assert.That(view.WeeklyMissions, Is.Empty);
        Assert.That(view.LateClaimMissions, Is.Empty);
        Assert.That(view.ClaimableCount, Is.Zero);
    }
}
