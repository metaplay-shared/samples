using Bunit;
using WebClient.Components.Pages.Meta;

namespace WebClient.Tests;

/// <summary>
/// bUnit render tests for the Compete page: the page draws the fixture view it is given. Behavior that needs a
/// live session, such as joining, claiming and the standings board, is tested in <c>LiveServerTournamentTests</c>.
/// </summary>
[TestFixture]
public class CompeteRenderTests : BunitPageTest
{
    /// <summary>
    /// A tournament in the error or loading state draws its state block instead of a blank area, and draws no
    /// join button.
    /// </summary>
    [TestCase("error",   "state-error")]
    [TestCase("loading", "state-loading")]
    public void AnUnhealthyTournamentDrawsItsOwnStateBlock(string scenario, string block)
    {
        Setup("/compete", scenario);
        IRenderedComponent<CompetePage> page = RenderComponent<CompetePage>();

        Assert.That(page.FindAll($"[data-testid=\"{block}\"]"), Has.Count.EqualTo(1));

        // Only the error state has a retry button. The loading skeleton has none.
        int retries = page.FindAll("[data-testid=\"state-retry\"]").Count;
        Assert.That(retries, Is.EqualTo(scenario == "error" ? 1 : 0));

        Assert.That(page.FindAll("[data-testid=\"tournament-join\"]"), Is.Empty);
    }

    /// <summary>
    /// The Compete page shows only the tournament, with no tab bar and no League tab.
    /// </summary>
    [Test]
    public void CompeteIsTheTournamentAndNothingElse()
    {
        Setup("/compete");
        IRenderedComponent<CompetePage> page = RenderComponent<CompetePage>();

        Assert.That(page.FindAll("[data-testid=\"compete-tabs\"]"), Is.Empty);
        Assert.That(page.FindAll("[data-testid=\"tab-league\"]"), Is.Empty);
        Assert.That(page.Find("[data-testid=\"page-title\"]").TextContent, Is.EqualTo("Compete"));
    }

    /// <summary>
    /// The rules and the milestone rewards are visible before the player joins. The milestone rows must sit inside
    /// the <c>.m-goal-list</c> wrapper, which provides their spacing and separators.
    /// </summary>
    [Test]
    public void TheRulesAndTheRewardsAreOnTheScreen()
    {
        Setup("/compete");
        IRenderedComponent<CompetePage> page = RenderComponent<CompetePage>();

        Assert.That(page.Find("[data-testid=\"tournament-rules\"]").TextContent, Does.Contain("Free to enter"));

        Assert.That(page.FindAll("[data-testid=\"tournament-milestones\"]"), Has.Count.EqualTo(1));
        Assert.That(page.FindAll("[data-testid=\"tournament-milestones\"] .m-goal-list [data-testid=\"tournament-milestone\"]"),
            Has.Count.EqualTo(4));
    }

    /// <summary>
    /// Every standings row, including the header row, carries <c>role="row"</c>. The stylesheet lays each
    /// <c>tr</c> out as a grid, and changing <c>display</c> removes the implicit table semantics, so the roles are
    /// written explicitly.
    /// </summary>
    [Test]
    public void TheStandingsBoardCarriesRowRolesForTheHeaderAndEveryStanding()
    {
        Setup("/compete");
        IRenderedComponent<CompetePage> page = RenderComponent<CompetePage>();

        Assert.That(page.FindAll("[data-testid=\"standings\"] [role=\"row\"]"), Has.Count.EqualTo(6));
    }
}
