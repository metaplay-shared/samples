using AngleSharp.Dom;
using Bunit;
using Game.Logic;
using WebClient.Components.TableUI;
using WebClient.Meta;

namespace WebClient.Tests;

/// <summary>
/// The results overlay rendered for a won and a lost match. The overlay needs a match but no session, so bUnit can
/// render it fully. Whether the viewer won decides the headline style, the confetti and the place line. The
/// standings and the Leave button are the same for both outcomes. The matches come from <see cref="TestTables"/>,
/// which plays them with the real game rules.
/// </summary>
[TestFixture]
public class ResultsOverlayRenderTests : BunitPageTest
{
    [Test]
    public void AWinIsCelebratedInGoldWithConfetti()
    {
        MatchModel won = TestTables.FinishedViewerWon;
        IRenderedComponent<ResultsOverlay> cut = RenderOverlay(won);

        IElement winner = cut.Find("[data-testid=results-winner]");
        Assert.That(winner.GetAttribute("data-outcome"), Is.EqualTo("won"));
        Assert.That(winner.TextContent, Is.EqualTo("You win!"));

        // The headline uses the letter cascade, with one element per letter.
        Assert.That(cut.FindAll(".ft-results__cascade .cascade__letter").Count, Is.GreaterThan(0));

        IElement confetti = cut.Find("[data-testid=confetti]");
        Assert.That(confetti.GetAttribute("data-pieces"), Is.EqualTo(ConfettiPlan.Count.ToString()));
        Assert.That(cut.FindAll(".ft-confetti__piece").Count, Is.EqualTo(ConfettiPlan.Count));

        // A win has no placement line, because the headline and the gilt first row already show it.
        Assert.That(cut.FindAll("[data-testid=results-place]").Count, Is.EqualTo(0));

        EveryStandingAndTheWayOnAreThere(cut);
        Assert.That(cut.Find(".stand-row--gilt [data-testid=standing-0-tricks]").TextContent,
            Does.Contain($"{won.Board.Standings[0].TricksWon} tricks"),
            "the winner's gilt row counts the tricks the standings sort recorded");
    }

    [Test]
    public void ALossNamesTheWinnerInMutedGiltAndSaysWhereTheViewerFinished()
    {
        MatchModel lost = TestTables.FinishedViewerLost;
        SeatStanding winner = lost.Board.Standings[0];
        SeatStanding viewer = lost.Board.Standings.Single(standing => standing.Seat == TestTables.ViewerSeat);

        IRenderedComponent<ResultsOverlay> cut = RenderOverlay(lost);

        IElement winnerLine = cut.Find("[data-testid=results-winner]");
        Assert.That(winnerLine.GetAttribute("data-outcome"), Is.EqualTo("lost"));
        Assert.That(winnerLine.TextContent, Is.EqualTo($"{lost.GetSeat(winner.Seat).DisplayName} wins!"));
        Assert.That(cut.Find(".ft-results__cascade.cascade--muted"), Is.Not.Null,
            "the loss's headline is the muted cascade, not the gold one");

        Assert.That(cut.Find("[data-testid=results-place]").TextContent.Trim(),
            Is.EqualTo($"You finished {Ordinal(viewer.Position + 1)} with {viewer.TricksWon} {(viewer.TricksWon == 1 ? "trick" : "tricks")}"));

        Assert.That(cut.FindAll("[data-testid=confetti]").Count, Is.EqualTo(0),
            "the confetti is the win's half of the celebration, and a loss draws none of it");

        EveryStandingAndTheWayOnAreThere(cut);
    }

    /// <summary>
    /// Asserts the parts that are the same for both outcomes: every standing row and the Leave button.
    /// </summary>
    private static void EveryStandingAndTheWayOnAreThere(IRenderedComponent<ResultsOverlay> cut)
    {
        for (int position = 0; position < 4; position++)
        {
            Assert.That(cut.FindAll($"[data-testid=standing-{position}]").Count, Is.EqualTo(1), $"standing-{position}");

            // Each standing row uses the shell's avatar component, the same one the seat plaques use, so a
            // player's cosmetics look the same in both places (docs/cosmetics.md).
            Assert.That(cut.Find($"[data-testid=standing-{position}-avatar]").ClassList, Does.Contain("m-avatar"),
                $"standing-{position} has no portrait");
        }
        Assert.That(cut.Find("[data-testid=results-leave]"), Is.Not.Null);
    }

    private IRenderedComponent<ResultsOverlay> RenderOverlay(MatchModel match)
    {
        Setup("/");
        return RenderComponent<ResultsOverlay>(parameters => parameters.Add(p => p.Match, match));
    }

    private static string Ordinal(int place)
    {
        return place switch
        {
            1 => "1st",
            2 => "2nd",
            3 => "3rd",
            _ => $"{place}th",
        };
    }

    /// <summary>
    /// Under reduced motion, app.css hides the confetti layer completely instead of showing it frozen. The rule
    /// lives in the stylesheet, so the test reads the stylesheet, as <see cref="StandingsRowCssTests"/> does.
    /// </summary>
    [Test]
    public void ReducedMotionHidesTheConfettiLayerEntirely()
    {
        string appCss = CssSource.AppCss();

        Assert.That(appCss, Does.Match(@"prefers-reduced-motion:\s*reduce[^{]*\{[^@]*?\.ft-confetti\s*\{[^}]*display:\s*none"),
            "app.css's reduced-motion block does not hide .ft-confetti");
    }
}
