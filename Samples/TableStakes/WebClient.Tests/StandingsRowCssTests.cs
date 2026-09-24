namespace WebClient.Tests;

/// <summary>
/// Tests for the shared standings row style in app.css (docs/meta-shell.md, "Styles and design tokens").
/// <c>.stand-row</c> draws the row fill, inset ring and radius, and <c>.stand-row--gilt</c> draws first place.
/// <para>
/// A board rule that declares its own fill, ring or radius silently overrides the shared style. Like
/// <see cref="MedallionCssTests"/>, the tests read the stylesheet and component sources as text, taking each rule as
/// the brace-balanced block at its exact selector (<see cref="CssSource.RuleBlocks"/>).
/// </para>
/// </summary>
[TestFixture]
public class StandingsRowCssTests
{
    /// <summary>The shared row classes that every board in <see cref="Boards"/> carries.</summary>
    private const string RowClass = "stand-row";
    private const string GiltClass = "stand-row--gilt";

    /// <summary>The boards that use the shared row, as (component source, the board's own layout class).</summary>
    private static readonly (string SourcePath, string BoardClass)[] Boards =
    {
        ("WebClient/Components/TableUI/ResultsOverlay.razor", "ts-standings__row"),
        ("WebClient/Components/Meta/Standings.razor",         "m-standings"),
    };

    /// <summary>
    /// Each board's component emits its own layout class, the shared row class and the shared first-place class.
    /// </summary>
    [Test]
    public void EveryBoardWearsTheSharedRowAndTheSharedGilt()
    {
        foreach ((string sourcePath, string boardClass) in Boards)
        {
            string source = File.ReadAllText(CssSource.RepoPath(sourcePath));

            Assert.That(source, Does.Contain(boardClass),
                $"{sourcePath} no longer carries its own '{boardClass}' layout class");
            Assert.That(source, Does.Contain(RowClass),
                $"{sourcePath} no longer carries '{RowClass}' beside '{boardClass}', so its rows draw from " +
                "the board's own rule instead of the one recipe");
            Assert.That(source, Does.Contain(GiltClass),
                $"{sourcePath} no longer carries '{GiltClass}', so its first place is poured apart from the " +
                "overlay's winner");
        }
    }

    /// <summary>
    /// The fill, ring and radius are declared only at <c>.stand-row</c> and the first-place wash only at
    /// <c>.stand-row--gilt</c>, both in app.css. Board rules declare layout only.
    /// </summary>
    [Test]
    public void OnlyStandRowDeclaresTheRowFillRingAndRadius()
    {
        string appCss = CssSource.AppCss();
        string metaCss = CssSource.MetaCss();

        string row = CssSource.RuleBlock(appCss, "." + RowClass);
        Assert.That(row, Is.Not.Empty, "app.css has no .stand-row rule — the recipe is gone");
        Assert.That(row, Does.Contain("background-color:"), "the quiet fill is not the recipe's to draw");
        Assert.That(row, Does.Contain("box-shadow: inset 0 0 0 1px"), "the brass inset ring is not the recipe's to draw");
        Assert.That(row, Does.Contain("border-radius: var(--r-strip)"), "the rows do not take the strip radius");

        string gilt = CssSource.RuleBlock(appCss, "." + GiltClass);
        Assert.That(gilt, Is.Not.Empty, "app.css has no .stand-row--gilt rule — the winner pour is gone");
        Assert.That(gilt, Does.Contain("linear-gradient(180deg, rgba(201, 169, 97, 0.14)"), "the gilt wash is not the one recipe");
        Assert.That(gilt, Does.Contain("box-shadow: inset 0 0 0 1px var(--gilt)"), "the gilt ring is not the one recipe");
        Assert.That(gilt, Does.Contain("color: var(--gilt-light)"), "the winner's ink is not the gilt-light the treatment names");

        Assert.That(CssSource.RuleBlocks(metaCss, "." + RowClass), Is.Empty,
            "meta-shell.css re-declares the row drawing, which is app.css's to declare");

        // Any .stand-row--gilt block in meta-shell.css and the overlay's .ts-standings__row blocks may declare layout,
        // but no fill, ring or radius.
        foreach (string block in CssSource.RuleBlocks(metaCss, "." + GiltClass))
        {
            Assert.That(block, Does.Not.Match(@"(^|[;{])\s*background"), ".m-standings' gilt block re-pours the wash, which is the recipe's to draw");
            Assert.That(block, Does.Not.Match(@"(^|[;{])\s*box-shadow"), ".m-standings' gilt block re-draws the ring, which is the recipe's to draw");
            Assert.That(block, Does.Not.Match(@"(^|[;{])\s*border-radius"), ".m-standings' gilt block re-states the radius, which is the recipe's to draw");
        }

        foreach (string block in CssSource.RuleBlocks(appCss, ".ts-standings__row"))
        {
            Assert.That(block, Does.Not.Match(@"(^|[;{])\s*background"), ".ts-standings__row re-pours the fill, which is the recipe's to draw");
            Assert.That(block, Does.Not.Match(@"(^|[;{])\s*box-shadow"), ".ts-standings__row re-draws the ring, which is the recipe's to draw");
            Assert.That(block, Does.Not.Match(@"(^|[;{])\s*border-radius"), ".ts-standings__row re-states the radius, which is the recipe's to draw");
        }
    }

    /// <summary>
    /// Neither stylesheet declares a per-board first-place selector or the leaderboard's 90-degree gold wash.
    /// </summary>
    [Test]
    public void NoBoardDeclaresItsOwnFirstPlaceStyle()
    {
        string css = CssSource.AppCss() + CssSource.MetaCss();

        Assert.That(CssSource.RuleBlocks(css, ".ts-standings__row--winner"), Is.Empty, "the overlay's private winner pour is still declared");
        Assert.That(CssSource.RuleBlocks(css, ".m-standings__first"), Is.Empty, "the leaderboard's private first-place vocabulary is still declared");

        Assert.That(css, Does.Not.Contain("linear-gradient(90deg, rgba(245, 197, 66, 0.16)"),
            "the leaderboard's 90-degree gold wash is still in a stylesheet");
    }

    /// <summary>
    /// The leaderboard keeps its own markers: the self bar, the promotion zone tint, the prize rows and the gold
    /// rank of first place. The self row's <c>box-shadow</c> must repeat the shared ring next to the bar, because
    /// declaring <c>box-shadow</c> replaces the one from <c>.stand-row</c>.
    /// </summary>
    [Test]
    public void TheLeaderboardKeepsItsBarItsZoneAndItsPrizes()
    {
        string metaCss = CssSource.MetaCss();

        string self = CssSource.RuleBlock(metaCss, ".m-standings__self");
        Assert.That(self, Does.Contain("box-shadow: inset 3px 0 0 var(--m-gold)"), "the self row's gold bar is gone");
        Assert.That(self, Does.Contain("inset 0 0 0 1px"), "the self row lost the ring the recipe paints");

        string promoting = CssSource.RuleBlock(metaCss, ".m-standings__promoting");
        Assert.That(promoting, Does.Contain("background-color: rgba(46, 230, 197, 0.07)"), "the promotion zone's tint is gone");

        Assert.That(CssSource.RuleBlock(metaCss, ".m-standings__prize"), Is.Not.Empty, "the prize rows are gone");
        Assert.That(metaCss, Does.Contain(".m-standings tr.stand-row--gilt td:first-child"),
            "first place no longer keeps its gold rank beside the trophy");
    }

    /// <summary>
    /// Each leaderboard <c>tr</c> is a grid of three columns rather than a table row, so its padding keeps the
    /// content off the ring. The Taking part card's milestone rows stack in a goal list with a gap between them.
    /// </summary>
    [Test]
    public void TheStandingsRowIsAGridAndTheMilestoneListHasAGap()
    {
        string metaCss = CssSource.MetaCss();

        string row = CssSource.RuleBlock(metaCss, ".m-standings tr");
        Assert.That(row, Is.Not.Empty, "the standings row has no grid rule of its own");
        Assert.That(row, Does.Contain("display: grid"), "the standings row is not laid out as a grid");
        Assert.That(row, Does.Contain("grid-template-columns"), "the standings row's three columns are not declared");

        string list = CssSource.RuleBlock(metaCss, ".m-goal-list");
        Assert.That(list, Is.Not.Empty, "the milestone list wrapper is not declared");
        Assert.That(list, Does.Contain("gap:"), "the milestone rows stack with no gap between them");
    }
}
