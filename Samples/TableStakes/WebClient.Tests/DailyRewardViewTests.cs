using System;
using System.Linq;
using WebClient.Meta;
using WebClient.Meta.Fixtures;

namespace WebClient.Tests;

/// <summary>
/// Tests for how the client presents the daily reward: <see cref="RewardView.Describe"/> and the
/// <see cref="DailyRewardView"/> built from the fixtures. The reward mechanic is shared code and is tested there.
/// </summary>
[TestFixture]
public class DailyRewardViewTests
{
    private static DailyRewardView Daily(string scenario = MetaFixtures.Default) =>
        MetaFixtures.Build(scenario, TimeSpan.Zero).DailyReward;

    /// <summary>
    /// <see cref="RewardView.Describe"/> is the one-line text the reward reveal shows, with each currency named.
    /// Button labels never include it.
    /// </summary>
    [Test]
    public void ARewardDescribesItselfInWordsWithItsCurrencyNamed()
    {
        RewardView bundle = RewardView.Of(
            RewardViewItem.Of(CurrencyKind.Coins, 330),
            RewardViewItem.Of(CurrencyKind.SpinTokens, 1));

        Assert.That(bundle.Describe(), Is.EqualTo("330 Coins + 1 Spin Token"));
    }

    /// <summary>A singular amount is written in the singular, and a large amount is grouped rather than abbreviated.</summary>
    [TestCase(1L,      "1 Coin")]
    [TestCase(12_500L, "12,500 Coins")]
    public void ACoinAmountIsWrittenInFull(long coins, string expected)
    {
        Assert.That(RewardView.Of(RewardViewItem.Of(CurrencyKind.Coins, coins)).Describe(), Is.EqualTo(expected));
    }

    [Test]
    public void AnEmptyBundleDescribesItselfAsNothing()
    {
        Assert.That(RewardView.Empty.Describe(), Is.Empty);
    }

    /// <summary>
    /// Every scenario shows the whole cycle from the first visit, with the spin token on the last step only, so
    /// the player can see what the coming days are worth.
    /// <para>
    /// An <see cref="ActivityState.Unavailable"/> view has an empty cycle, because the cycle is read from the
    /// published config and an unpublished feature has none. The test requires it to be empty, not partial.
    /// </para>
    /// </summary>
    [Test]
    public void EveryScenarioShowsTheWholeCycleWithTheTokenOnTheLastStep()
    {
        foreach (string scenario in MetaFixtures.Scenarios.Keys)
        {
            DailyRewardView view = Daily(scenario);
            if (view.State == ActivityState.Unavailable)
            {
                Assert.That(view.Cycle, Is.Empty, $"{scenario}: an unavailable feature drew part of a cycle");
                continue;
            }

            Assert.That(view.Cycle, Has.Count.EqualTo(7), $"{scenario}: the cycle is not seven steps");
            Assert.That(view.Cycle.Select(s => s.Step), Is.EqualTo(new[] { 1, 2, 3, 4, 5, 6, 7 }), $"{scenario}: steps out of order");
            Assert.That(view.Cycle.Count(s => s.HasSpinToken), Is.EqualTo(1), $"{scenario}: the token is not on exactly one step");
            Assert.That(view.Cycle.Last().HasSpinToken, Is.True, $"{scenario}: the token is not the capstone");
            Assert.That(view.Cycle.All(s => s.Coins > 0), Is.True, $"{scenario}: a step pays no coins");
        }
    }

    /// <summary>
    /// The fixture cycle must match the published economy config, or the fixture pages would show rewards no
    /// player receives.
    /// </summary>
    [Test]
    public void TheFixtureCycleIsTheApprovedEconomyBudget()
    {
        Assert.That(Daily().Cycle.Select(s => s.Coins), Is.EqualTo(new long[] { 150, 170, 190, 210, 240, 270, 330 }));
    }

    /// <summary>
    /// A claimable daily reward has no urgency and no ending-soon threshold. It is claimable every day, so an
    /// urgent countdown would be shown every day.
    /// </summary>
    [Test]
    public void AClaimableRewardIsNotACountdown()
    {
        DailyRewardView view = Daily();

        Assert.That(view.HasClaimableReward, Is.True);
        Assert.That(view.UntilDeadline, Is.Null);
        Assert.That(view.EndingSoonWithin, Is.EqualTo(TimeSpan.Zero));
    }

    /// <summary>The claimed state carries the time until the next reward unlocks and a preview of that reward.</summary>
    [Test]
    public void TheClaimedStateCarriesTheNextRewardsTime()
    {
        DailyRewardView view = Daily("claimed");

        Assert.That(view.HasClaimableReward, Is.False);
        Assert.That(view.UntilNextAvailable, Is.Not.Null.And.GreaterThan(TimeSpan.Zero));
        Assert.That(view.TomorrowReward.IsEmpty, Is.False, "the claimed state previews the next step");
    }
}
