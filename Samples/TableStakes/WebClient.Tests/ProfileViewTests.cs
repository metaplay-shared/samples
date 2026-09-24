using WebClient.Meta;
using WebClient.Meta.Fixtures;

namespace WebClient.Tests;

/// <summary>
/// The Profile screen's view models: the win rate text, including before the first game, and how
/// <see cref="IdentityView"/> describes the player's competition standing.
/// </summary>
[TestFixture]
public class ProfileViewTests
{
    private static ProfileView WithRecord(int played, int won) =>
        MetaFixtures.Build(MetaFixtures.Default, System.TimeSpan.Zero).Profile with { GamesPlayed = played, GamesWon = won };

    [Test]
    public void BeforeTheFirstGameTheWinRateIsADashRatherThanZero()
    {
        // 0% would mean the player has played and lost every game. A player with no games shows a dash instead
        // (docs/player.md).
        ProfileView fresh = WithRecord(played: 0, won: 0);

        Assert.That(fresh.WinRatePercent, Is.Null);
        Assert.That(fresh.WinRateText, Is.EqualTo("—"));
    }

    /// <summary>After a game the win rate is a whole percent, rounded rather than truncated (2 of 3 is 67%).</summary>
    [TestCase(1, 0, "0%")]
    [TestCase(1, 1, "100%")]
    [TestCase(4, 1, "25%")]
    [TestCase(3, 2, "67%")]
    public void AfterAGameTheWinRateIsAWholePercent(int played, int won, string expected)
    {
        Assert.That(WithRecord(played: played, won: won).WinRateText, Is.EqualTo(expected));
    }

    #region A standing is read from the session, never printed as a constant

    private static IdentityView Standing(string competition, int rank) =>
        MetaFixtures.Build(MetaFixtures.Default, System.TimeSpan.Zero).Identity
            with { CompetitionName = competition, CompetitionRank = rank };

    /// <summary>
    /// A player in a competition is shown it and their place in it. A player in no competition is told so rather than
    /// shown a rank. An entered competition with no place yet names itself and claims no rank. A leaderboard row has a
    /// rank but no competition name, because the leaderboard itself is the competition, so both the line and the
    /// badge show the rank.
    /// </summary>
    [TestCase("Seasonal Tournament", 15, "Seasonal Tournament \u00b7 #15", "#15")]
    [TestCase("",                    0,  "Not in a competition",               "Unranked")]
    [TestCase("Seasonal Tournament", 0,  "Seasonal Tournament",                "Unranked")]
    [TestCase("",                    4,  "#4",                                 "#4")]
    public void TheStandingIsDescribedFromTheCompetitionAndThePlace(string competition, int rank, string expectedLine, string expectedBadge)
    {
        IdentityView identity = Standing(competition, rank);

        Assert.That(identity.StandingLine, Is.EqualTo(expectedLine));
        Assert.That(identity.StandingBadge, Is.EqualTo(expectedBadge));
    }

    #endregion
}
