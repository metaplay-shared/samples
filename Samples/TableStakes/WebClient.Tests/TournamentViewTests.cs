using Game.Logic;
using WebClient.Meta;
using WebClient.Meta.Fixtures;

namespace WebClient.Tests;

/// <summary>
/// Tests for <see cref="TournamentView"/> and the Compete routes in the client.
/// <para>
/// The tournament rules (bots, match cap, tiebreaks, claims) are shared code and are tested in
/// <c>TournamentTests</c>. This fixture tests only what the view derives and how Compete routes.
/// </para>
/// </summary>
[TestFixture]
public class TournamentViewTests
{
    private static TournamentView Cup => MetaFixtures.Build(MetaFixtures.Default, TimeSpan.Zero).Tournament;

    #region Seasons are numbered the way a player counts them

    /// <summary>
    /// The SDK numbers seasons from zero. The UI shows season numbers counting from one, because "Season 0" reads
    /// as a season that has not started.
    /// </summary>
    [TestCase(0,  1)]
    [TestCase(11, 12)]
    public void TheFirstSeasonIsSeasonOne(int seasonIndex, int expected)
    {
        Assert.That(TournamentSeasons.NumberOf(seasonIndex), Is.EqualTo(expected));
    }

    #endregion

    #region Compete has one feature

    [Test]
    public void CompeteIsTheTournamentAndNothingElse()
    {
        MetaSnapshot snapshot = MetaFixtures.Build(MetaFixtures.Default, TimeSpan.Zero);

        Assert.That(snapshot.CompeteFeatures, Has.Count.EqualTo(1));
        Assert.That(snapshot.CompeteFeatures[0].Feature, Is.EqualTo(MetaFeature.Tournament));
    }

    [Test]
    public void NoFeatureRoutesToALeagueScreen()
    {
        foreach (MetaFeature feature in Enum.GetValues<MetaFeature>())
            Assert.That(MetaRoutes.For(feature), Is.Not.EqualTo("/compete/league"));
    }

    /// <summary>
    /// <c>/compete/league</c> is not a route, so it maps to <see cref="ShellScreen.Unknown"/>, which is not
    /// reported.
    /// </summary>
    [Test]
    public void TheLeagueRouteReportsNoScreen()
    {
        Assert.That(ShellScreens.Of("/compete/league"), Is.EqualTo(ShellScreen.Unknown));
    }

    /// <summary>
    /// <see cref="ShellScreen.League"/> is no longer used but keeps its value. Analytics rows already store that
    /// value, and reusing it for another screen would make those rows ambiguous.
    /// </summary>
    [Test]
    public void TheRetiredLeagueScreenKeepsItsNumber()
    {
        Assert.That((int)ShellScreen.League, Is.EqualTo(10));
        Assert.That(ShellScreens.Of(MetaRoutes.Compete), Is.EqualTo(ShellScreen.Compete));
        Assert.That(ShellScreens.Of(MetaRoutes.Tournament), Is.EqualTo(ShellScreen.Tournament));
    }

    #endregion

    #region What a place earns

    /// <summary>
    /// <c>RewardFor</c> returns the first band whose maximum rank is at or above the rank. A rank past the last
    /// band, or rank 0 (no rank), earns nothing.
    /// </summary>
    [Test]
    public void RewardForPicksTheFirstBandTheRankFitsIn()
    {
        RewardView first = RewardView.Of(
            RewardViewItem.Of(CurrencyKind.Coins, 1_500),
            RewardViewItem.Of(CurrencyKind.Gems, 50),
            RewardViewItem.Item("Tournament Champion", "frame"));
        RewardView second = RewardView.Of(
            RewardViewItem.Of(CurrencyKind.Coins, 1_000),
            RewardViewItem.Of(CurrencyKind.SpinTokens, 1));
        RewardView third = RewardView.Of(RewardViewItem.Of(CurrencyKind.Coins, 500));

        TournamentView view = Cup with
        {
            PlacementBands = new[]
            {
                new PlacementBandView(1, first),
                new PlacementBandView(3, second),
                new PlacementBandView(5, third),
            },
        };

        Assert.That(view.RewardFor(1), Is.EqualTo(first));
        Assert.That(view.RewardFor(2), Is.EqualTo(second));
        Assert.That(view.RewardFor(3), Is.EqualTo(second));
        Assert.That(view.RewardFor(5), Is.EqualTo(third));
        Assert.That(view.RewardFor(6), Is.EqualTo(RewardView.Empty), "past the last band earns nothing");
        Assert.That(view.RewardFor(0), Is.EqualTo(RewardView.Empty), "no rank earns nothing");
    }

    /// <summary>The fixture's bands are in ascending rank order, which <c>RewardFor</c> relies on.</summary>
    [Test]
    public void FixturePlacementsAscend()
    {
        Assert.That(Cup.PlacementBands, Is.Not.Empty);

        int previous = 0;
        foreach (PlacementBandView band in Cup.PlacementBands)
        {
            Assert.That(band.MaxRank, Is.GreaterThan(previous), "bands must ascend and never repeat a rank");
            Assert.That(band.Reward.IsEmpty, Is.False, $"band ≤{band.MaxRank} carries nothing");
            previous = band.MaxRank;
        }
    }

    #endregion

    #region What the view derives

    /// <summary>
    /// Progress is scored matches divided by the match cap, and it is null before the player joins.
    /// </summary>
    [Test]
    public void ProgressIsTheCappedRun()
    {
        Assert.That(Cup.Progress, Is.Null, "a player who has not entered has no run to be part-way through");

        TournamentView joined = Cup with { IsInSeason = true, ScoredMatches = 6, MatchCap = 10 };
        Assert.That(joined.Progress, Is.EqualTo(0.6).Within(0.001));
        Assert.That(joined.IsCapReached, Is.False);

        Assert.That((joined with { ScoredMatches = 10 }).IsCapReached, Is.True);
        Assert.That((joined with { ScoredMatches = 10 }).Progress, Is.EqualTo(1.0).Within(0.001));
    }

    /// <summary><c>NextClaimable</c> is the first milestone that is reached and not claimed.</summary>
    [Test]
    public void TheNextClaimIsTheFirstReachedAndUnpaidMilestone()
    {
        TournamentView view = Cup with
        {
            Milestones = new[]
            {
                new TournamentMilestoneView(0, 1, RewardView.Empty, IsReached: true,    IsClaimed: true),
                new TournamentMilestoneView(1, 3, RewardView.Empty, IsReached: true,    IsClaimed: false),
                new TournamentMilestoneView(2, 7, RewardView.Empty, IsReached: true,    IsClaimed: false),
                new TournamentMilestoneView(3, 10, RewardView.Empty, IsReached: false, IsClaimed: false),
            },
        };

        Assert.That(view.NextClaimable?.Index, Is.EqualTo(1));
        Assert.That(view.Milestones[3].IsClaimable, Is.False, "a milestone that is not reached cannot be claimed");
    }

    [Test]
    public void UrgencyIsTheEndWhileRunningAndTheStartBefore()
    {
        Assert.That((Cup with { UntilStart = TimeSpan.FromHours(3), UntilEnd = null }).UntilDeadline, Is.EqualTo(TimeSpan.FromHours(3)));
        Assert.That((Cup with { UntilStart = null, UntilEnd = TimeSpan.FromHours(9) }).UntilDeadline, Is.EqualTo(TimeSpan.FromHours(9)));
    }

    /// <summary>
    /// In every fixture scenario, the tournament has milestones. <see cref="MetaFixturesTests"/> checks the fixture
    /// standings.
    /// </summary>
    [Test]
    public void EveryFixtureTournamentHasMilestones()
    {
        foreach (string scenario in MetaFixtures.Scenarios.Keys)
            Assert.That(MetaFixtures.Build(scenario, TimeSpan.Zero).Tournament.Milestones, Is.Not.Empty, $"{scenario}: no participation track");
    }

    #endregion
}
