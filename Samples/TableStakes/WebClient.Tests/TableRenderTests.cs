using AngleSharp.Dom;
using Bunit;
using Bunit.TestDoubles;
using Game.Logic;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using WebClient.Components.Pages;
using WebClient.Services;

namespace WebClient.Tests;

/// <summary>
/// bUnit render tests for what <c>Table.razor</c> draws from a <see cref="MatchModel"/> in a single frame: which
/// seats and cards are shown, where, and with what labels. Each test takes a named table from
/// <see cref="TestTables"/>, whereas a browser test would have to reload until a fresh deal reached the state.
/// <para>
/// Behavior that depends on time or CSS is tested in <c>TablePageTests</c> and <c>TableRobustnessTests</c>. bUnit
/// has no layout, so these tests prove an element is in the markup but not that it is visible. The browser tests
/// interact with the same elements, so a CSS rule that hid one would fail there.
/// </para>
/// <para>
/// The fixture derives from bUnit's context rather than <see cref="BunitPageTest"/>, because the table reads no
/// <see cref="MetaStateService"/> fixture scenario. The table reaches the page through
/// <see cref="NoSessionClientService.ShowTable"/>.
/// </para>
/// </summary>
[TestFixture]
public class TableRenderTests : Bunit.TestContext
{
    /// <summary>The CSS position name for <paramref name="seat"/> as seen by <see cref="TestTables.ViewerSeat"/>.</summary>
    private static string ScreenPositionOfSeat(int seat) =>
        ExpectedSeatPositions.ScreenPositionOfSeat(seat, TestTables.ViewerSeat);

    /// <summary>Maps each suit name the board writes in <c>data-suit</c> to the glyph drawn for it.</summary>
    private static readonly Dictionary<string, string> GlyphOfSuit = new Dictionary<string, string>
    {
        ["Clubs"]    = "♣",
        ["Diamonds"] = "♦",
        ["Hearts"]   = "♥",
        ["Spades"]   = "♠",
    };

    /// <summary>
    /// Registers the services, navigates to the table route with <paramref name="match"/> on screen, and renders the
    /// <c>Table</c> page. Call it once per test: bUnit builds its service provider on the first resolution, and
    /// registering a service after that throws.
    /// <para>
    /// The turn beat and the trump intro are timed against the wall clock, which does not advance during a bUnit
    /// render, so a test sees only the frame for the model it provided. Test those animations in the browser suite.
    /// The order of navigation and <see cref="NoSessionClientService.ShowTable"/> does not matter, because
    /// <c>Table.razor</c> reads the table from the client service on every render.
    /// </para>
    /// </summary>
    private IRenderedComponent<Table> RenderTable(MatchModel match)
    {
        Services.AddNoSessionClient(new NoSessionClientService()).ShowTable(match);

        FakeNavigationManager nav = (FakeNavigationManager)Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo("http://localhost/table");

        return RenderComponent<Table>();
    }

    [Test]
    public void Table_DeliversTheSeatsHandAndThePublicBoard()
    {
        IRenderedComponent<Table> cut = RenderTable(TestTables.OpeningDeal);

        // The public board comes from the model, so it renders as soon as the model is set.
        Assert.That(cut.Find("[data-testid=match-phase]").TextContent, Is.EqualTo("Playing"));
        Assert.That(GlyphOfSuit, Contains.Key(cut.Find("[data-testid=trump-suit]").GetAttribute("data-suit") ?? ""));
        Assert.That(cut.Find("[data-testid=seat-0]").TextContent, Does.Contain("You"));

        // The viewer's hand is delivered separately, stamped with the play index at delivery. The opening deal
        // delivers it before any card is played, so the index is zero.
        Assert.That(cut.Find("[data-testid=own-seat]").TextContent, Is.EqualTo("0"));
        Assert.That(cut.Find("[data-testid=own-hand-count]").TextContent, Is.EqualTo("5"));
        Assert.That(cut.Find("[data-testid=own-hand-index]").TextContent, Is.EqualTo("0"));

        // Every seat starts with no tricks. The other seats' card counts are drawn as card backs, which
        // Table_DrawsEveryOtherSeatsHand_AsBacksBesideTheirPlaque covers.
        for (int seat = 0; seat < 4; seat++)
            Assert.That(cut.Find($"[data-testid=seat-{seat}-tricks]").TextContent, Is.EqualTo("0"));
    }

    [Test]
    public void Table_DrawsEveryOtherSeatsHand_AsBacksBesideTheirPlaque()
    {
        IRenderedComponent<Table> cut = RenderTable(TestTables.OpeningDeal);

        // A table first seen before any card is played runs the deal animation. A table joined mid-game does not.
        Assert.That(cut.Find("[data-testid=table]").GetAttribute("data-dealing"), Is.EqualTo("true"));

        // Each other seat shows a full hand of card backs. The client knows only the public card count for
        // those seats, not the cards.
        for (int seat = 1; seat < 4; seat++)
        {
            IElement hand = cut.Find($"[data-testid=seat-{seat}-hand]");
            Assert.That(hand.GetAttribute("data-cards"), Is.EqualTo("5"));
            Assert.That(hand.QuerySelectorAll(".ts-card--back"), Has.Length.EqualTo(5));
            Assert.That(hand.QuerySelectorAll(".ts-card--face"), Has.Length.EqualTo(0));
        }

        // The viewer's seat has no card backs. The viewer's hand is drawn face up in own-hand.
        Assert.That(cut.FindAll("[data-testid=seat-0-hand]"), Is.Empty);
        Assert.That(cut.Find("[data-testid=own-hand]").QuerySelectorAll(".ts-card--face"), Has.Length.EqualTo(5));
    }

    [Test]
    public void Table_DrawsFourSeatPlaques_RotatedSoTheViewerSitsAtSouth()
    {
        IRenderedComponent<Table> cut = RenderTable(TestTables.OpeningDeal);

        // Each seat's plaque is at its rotated screen position. Every client draws its own seat at South, so
        // different clients show different seats at North.
        for (int seat = 0; seat < 4; seat++)
        {
            IElement plaque = cut.Find($"[data-testid=seat-{seat}]");
            Assert.That(plaque.GetAttribute("data-position"), Is.EqualTo(ScreenPositionOfSeat(seat)));
        }

        // Exactly one seat is on turn. The plaque does not show which seats are bots, but it carries the
        // occupancy in data-occupancy, which is how the table tracks a seat a bot has taken over
        // (docs/match.md, "When players stop playing").
        Assert.That(cut.FindAll("[data-on-turn='true']"), Has.Count.EqualTo(1));
        for (int seat = 1; seat < 4; seat++)
            Assert.That(cut.Find($"[data-testid=seat-{seat}]").GetAttribute("data-occupancy"), Is.EqualTo("Bot"));
    }

    /// <summary>
    /// Each plaque shows its own seat's identity. The name is checked because cosmetics come from the same identity
    /// object, so a page that passed one identity to every plaque would fail here.
    /// <para>
    /// <see cref="TestTables"/> has no game config, so no seat wears a cosmetic. <c>CosmeticsPolicyTests</c> covers
    /// mapping cosmetic ids to style tokens, and <see cref="SeatPlaqueRenderTests"/> covers how a plaque draws them.
    /// </para>
    /// </summary>
    [Test]
    public void Table_GivesEachPlaqueItsOwnSeatsIdentity()
    {
        IRenderedComponent<Table> cut = RenderTable(TestTables.OpeningDeal);

        for (int seat = 0; seat < 4; seat++)
        {
            string expected = TestTables.OpeningDeal.GetSeat(seat).DisplayName ?? "";
            Assert.That(cut.Find($"[data-testid=seat-{seat}-name]").TextContent, Is.EqualTo(expected),
                $"seat {seat} was drawn under somebody else's name");

            // The portrait is the shell's m-avatar component, showing its defaults because the seat has no cosmetics.
            Assert.That(cut.Find($"[data-testid=seat-{seat}-avatar]").ClassList, Does.Contain("m-avatar"));
        }
    }

    /// <summary>
    /// Checks the trump disc's markup: the suit, the glyph, the accessible name, and that the trump intro is not
    /// rendered after the opening. The check that the settled disc is fully opaque needs CSS, so it is in
    /// <c>TablePageTests</c>.
    /// </summary>
    [Test]
    public void Table_ShowsTheTrumpSuitAtTheCentre_AndAnnouncesIt()
    {
        IRenderedComponent<Table> cut = RenderTable(TestTables.ViewerOnTurn);

        // The trump is a suit only. The rank of the turned-up card does not matter to the game, so it is not
        // shown.
        IElement trump = cut.Find("[data-testid=trump-suit]");
        string   suit  = trump.GetAttribute("data-suit") ?? "";
        Assert.That(GlyphOfSuit, Contains.Key(suit));
        Assert.That(cut.FindAll("[data-testid=trump-intro]"), Is.Empty);

        // The glyph and the aria-label name the same suit. The disc is the only place the trump is shown, so
        // without the label a screen reader user could not learn the trump.
        Assert.That(trump.TextContent, Is.EqualTo(GlyphOfSuit[suit]));
        Assert.That(trump.GetAttribute("aria-label"), Is.EqualTo($"Trump suit: {suit}"));
    }

    /// <summary>On the viewer's turn, the turn banner is shown.</summary>
    [Test]
    public void Table_AnnouncesTheViewersTurn()
    {
        IRenderedComponent<Table> cut = RenderTable(TestTables.ViewerOnTurn);

        Assert.That(cut.Find("[data-testid=presented-seat-on-turn]").TextContent, Is.EqualTo("0"));
        Assert.That(cut.Find("[data-testid=turn-banner]").TextContent, Does.Contain("Your turn"));
    }

    /// <summary>
    /// On another seat's turn, the turn banner is not rendered. The banner is shown only while the viewer must play
    /// a card.
    /// </summary>
    [Test]
    public void Table_ShowsNoTurnBannerWhenItIsNotTheViewersTurn()
    {
        IRenderedComponent<Table> cut = RenderTable(TestTables.MidTrick);

        Assert.That(cut.Find("[data-testid=presented-seat-on-turn]").TextContent, Is.Not.EqualTo("0"));
        Assert.That(cut.FindAll("[data-testid=turn-banner]"), Is.Empty);
    }

    /// <summary>
    /// The viewer's hand is drawn but takes no input while another seat is on turn. With its fixed deal seed,
    /// <see cref="TestTables.OpeningDeal"/> starts on a seat other than the viewer's, and the first assertion fails if
    /// that changes.
    /// </summary>
    [Test]
    public void Table_HandTakesNoInputWhenItIsNotTheViewersTurn()
    {
        MatchModel opening = TestTables.OpeningDeal;
        Assert.That(opening.Board.SeatOnTurn, Is.Not.EqualTo(TestTables.ViewerSeat),
            "the opening deal for this fixed seed no longer opens on another seat");

        IRenderedComponent<Table> cut = RenderTable(opening);

        Assert.That(cut.Find("[data-testid=own-hand]").QuerySelectorAll("button"), Has.Length.EqualTo(5));
        Assert.That(cut.FindAll("[data-testid=waiting-card]"), Has.Count.EqualTo(5));
        Assert.That(cut.FindAll("[data-testid=legal-card]"), Is.Empty);

        // Clicking an inert card changes nothing: no card is played and the hand does not lock.
        cut.Find("[data-testid=own-hand] button").Click();
        Assert.That(cut.Find("[data-testid=play-index]").TextContent, Is.EqualTo("0"));
        Assert.That(cut.Find("[data-testid=own-hand-count]").TextContent, Is.EqualTo("5"));
        Assert.That(cut.Find("[data-testid=hand-locked]").TextContent, Is.EqualTo("false"));
    }

    /// <summary>
    /// Each card in the current trick is drawn at its seat's rotated screen position. With its fixed deal seed,
    /// <see cref="TestTables.ViewerOnTurn"/> has at least one bot card in the trick before the viewer's turn, and the
    /// first assertion fails if that changes.
    /// </summary>
    [Test]
    public void Table_AnchorsEachPlayedCardToTheSeatThatPlayedIt()
    {
        MatchModel onTurn = TestTables.ViewerOnTurn;
        int        cardsInTrick = onTurn.Board.Plays.Count;
        Assert.That(cardsInTrick, Is.GreaterThan(0),
            "the opening deal for this fixed seed no longer leaves a bot's card on the felt before the viewer's turn");

        IRenderedComponent<Table> cut = RenderTable(onTurn);

        IReadOnlyList<IElement> trickCards = cut.FindAll("[data-testid=trick-card]");
        Assert.That(trickCards, Has.Count.EqualTo(cardsInTrick));

        // Each card's data-position must match the rotated position of the seat that played it.
        foreach (IElement card in trickCards)
        {
            string seat = card.GetAttribute("data-seat") ?? "-1";
            Assert.That(seat, Is.Not.EqualTo("0"), "the viewer has not played yet, so no centre card is theirs");
            Assert.That(card.GetAttribute("data-position"), Is.EqualTo(ScreenPositionOfSeat(int.Parse(seat))));
        }

        // Cards played by other seats are drawn as flipped cards with a back and a face, because they leave the
        // hand face down and turn over. The viewer's own played card has no flip, and none of these is the viewer's.
        Assert.That(cut.FindAll("[data-testid=flipped-card]"), Has.Count.EqualTo(cardsInTrick));
        Assert.That(cut.FindAll("[data-testid=played-card]"), Is.Empty);
        Assert.That(cut.FindAll(".ts-slot__back"), Has.Count.EqualTo(cardsInTrick));
        Assert.That(cut.FindAll(".ts-slot__face"), Has.Count.EqualTo(cardsInTrick));
    }

    #region The deadline

    /// <summary>
    /// <c>deadline-in-force</c> reads true as soon as a move deadline is set, even when the deadline is outside the
    /// ring's closing window and no ring is drawn. The hand still takes input.
    /// </summary>
    [Test]
    public void ADeadlineInForceIsStampedOnTheBoardAndDrawnAsARing()
    {
        IRenderedComponent<Table> cut = RenderTable(TestTables.BuildMoveDeadlineFarOut());

        Assert.That(cut.Find("[data-testid=deadline-in-force]").TextContent, Is.EqualTo("true"));

        // The deadline is outside the ring's closing window, so no ring is drawn. By design, most of a turn shows
        // no timer.
        Assert.That(cut.FindAll("[data-testid=deadline-ring]"), Is.Empty);
        Assert.That(cut.Find("[data-testid=hand-locked]").TextContent, Is.EqualTo("false"));
    }

    /// <summary>Inside the closing window, one deadline ring is drawn, on the viewer's plaque.</summary>
    [Test]
    public void ARingIsDrawnOnceTheDeadlineIsCloseEnoughToShow()
    {
        IRenderedComponent<Table> cut = RenderTable(TestTables.BuildMoveDeadlineClosing());

        Assert.That(cut.FindAll("[data-testid=deadline-ring]"), Has.Count.EqualTo(1));
        Assert.That(cut.Find("[data-testid=seat-0]").QuerySelectorAll("[data-testid=deadline-ring]"), Has.Length.EqualTo(1));
    }

    #endregion

    #region Leaving

    /// <summary>
    /// Leave is a single button with no confirmation prompt. <c>TableRobustnessTests</c> covers what the tap does.
    /// <para>
    /// This table has a move deadline, so <c>Table.razor</c>'s repaint timer re-renders it on a background thread
    /// during the test. <c>WaitForState</c> re-checks the condition after every render, so the test does not read
    /// the DOM while a concurrent render is changing it.
    /// </para>
    /// </summary>
    [Test]
    public void LeaveIsOfferedAsOneButton_AndAsksNothingFirst()
    {
        IRenderedComponent<Table> cut = RenderTable(TestTables.ViewerOnTurn);

        cut.WaitForState(() => cut.FindAll("[data-testid=leave]").Count == 1);
        Assert.That(cut.FindAll("[data-testid=leave-prompt]"), Is.Empty,
            "leaving must not ask again before it acts");
        Assert.That(cut.FindAll("[data-testid=leave-confirm]"), Is.Empty,
            "leaving must not ask again before it acts");
        Assert.That(cut.FindAll("[data-testid=leave-cancel]"), Is.Empty,
            "leaving must not ask again before it acts");
        Assert.That(cut.Find("[data-testid=match-phase]").TextContent, Is.EqualTo("Playing"));
    }

    #endregion

    #region Abandonment

    /// <summary>
    /// An abandoned table shows the abandoned panel instead of the results overlay, because no game was played. The
    /// panel's only control is its leave button.
    /// </summary>
    [Test]
    public void ATableNobodyEverJoinedShowsTheAbandonedPanel()
    {
        IRenderedComponent<Table> cut = RenderTable(TestTables.Abandoned);

        Assert.That(cut.Find("[data-testid=abandoned-panel]"), Is.Not.Null);
        Assert.That(cut.Find("[data-testid=plays-count]").TextContent, Is.EqualTo("0"));
        Assert.That(cut.FindAll("[data-testid=results-overlay]"), Is.Empty);
        Assert.That(cut.Find("[data-testid=abandoned-leave]"), Is.Not.Null);
    }

    #endregion
}
