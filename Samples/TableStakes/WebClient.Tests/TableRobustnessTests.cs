using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;
using System.Text.RegularExpressions;

namespace WebClient.Tests;

/// <summary>
/// E2E tests for what a table does when its player stops playing: the move deadline, auto-play, covering a
/// seat after repeated lapses, leaving, and the join window.
/// <para>
/// The tests run in offline mode, which hosts the real multiplayer entity and runs the same turn-flow driver as
/// the server actor, so they need no game server. Each timer is set from the query string: short to make it
/// lapse, or far past the end of the run to keep it from lapsing. How the table looks in these states is asserted
/// in <see cref="TableRenderTests"/>.
/// </para>
/// </summary>
[TestFixture]
public class TableRobustnessTests : PlaywrightPageTest
{
    #region The deadline

    [Test]
    public async Task ALapsedDeadlineAutoPlaysTheSeatAndTheGameCarriesOn()
    {
        // When a deadline lapses, the strongest bot profile plays the seat's card and the table moves on. The
        // test never taps a card, so every one of the viewer's turns is auto-played and the game still finishes.
        await OpenTableAsync($"{OfflineTableUrl}&botThinkMs=0&resolvePauseMs=0&moveDeadlineMs=800");

        // The test does not read the viewer's hand after Playing to check the deal. The viewer's seat is the one
        // being auto-played, and MatchHost.TryAutoPlayLapsedSeat calls OnSeatHandChanged on the first lapse, so
        // own-hand-count and own-hand-index can change before the read. The final play count and the empty hand
        // prove the deal size instead: four seats of five cards each reach Ended only after twenty plays.
        await Expect(Page.GetByTestId("match-phase")).ToHaveTextAsync("Ended", new() { Timeout = TurnTimeoutMs });
        await Expect(Page.GetByTestId("plays-count")).ToHaveTextAsync("20");
        await Expect(Page.GetByTestId("own-hand-count")).ToHaveTextAsync("0");
        await Expect(Page.GetByTestId("results-overlay")).ToBeVisibleAsync(new() { Timeout = TurnTimeoutMs });
    }

    [Test]
    public async Task ASeatThatLetsTwoDeadlinesLapseIsCovered_WithItsOwnersNameStillOnThePlaque()
    {
        // A covered seat keeps its owner's name on the plaque, and its occupancy says a bot is playing it.
        //
        // The player never taps a card, so their first two turns both lapse. The reclaim delay is short because
        // this test checks the plaque, not how long the covering bot waits.
        await OpenTableAsync($"{OfflineTableUrl}&botThinkMs=0&resolvePauseMs=0&moveDeadlineMs=700&strikes=2&reclaimDelayMs=150");

        string ownName = await Page.GetByTestId("seat-0-name").InnerTextAsync();

        await Expect(Page.GetByTestId("covered-seats")).ToHaveTextAsync("1", new() { Timeout = TurnTimeoutMs });

        ILocator plaque = Page.GetByTestId("seat-0");
        await Expect(plaque).ToHaveAttributeAsync("data-occupancy", "HumanCoveredByBot");
        await Expect(Page.GetByTestId("seat-0-name")).ToHaveTextAsync(ownName);

        // A covered seat and a seat that was always a bot have different occupancy values.
        await Expect(Page.GetByTestId("seat-1")).ToHaveAttributeAsync("data-occupancy", "Bot");
    }

    #endregion

    #region Leaving

    /// <summary>
    /// The Home URL: the root of the client origin, with or without a query string. The query string is there
    /// because <c>EnvironmentLink</c> keeps the page's <c>?env=</c> override on the link home.
    /// </summary>
    static Regex HomeUrlPattern { get; } = new Regex(@"^https?://[^/]+/(\?.*)?$");

    /// <summary>
    /// The Leave button is placed inside the table's phone-width column, not against the browser window, so on a
    /// desktop viewport it stays beside the board. The game route draws no shell chrome above the column (see
    /// docs/meta-shell.md, "Layouts").
    /// </summary>
    [Test]
    public async Task LeaveSitsInsideTheGamesOwnColumn()
    {
        await Page.SetViewportSizeAsync(1440, 900);
        await OpenTableAsync($"{OfflineTableUrl}&botThinkMs=600000&resolvePauseMs=0");

        // The placement check below measures Leave against the game's column only. That is valid only while the
        // route draws no HUD and no navigation, so assert that first.
        await Expect(Page.GetByRole(AriaRole.Banner)).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("primary-nav")).ToHaveCountAsync(0);

        float[] box = await Page.EvaluateAsync<float[]>(@"() => {
            const leave = document.querySelector('[data-testid=""leave""]').getBoundingClientRect();
            const grid  = document.querySelector('.ts-table__grid').getBoundingClientRect();
            return [leave.left, leave.right, leave.top, grid.left, grid.right, grid.top];
        }");

        (float leaveLeft, float leaveRight, float leaveTop) = (box[0], box[1], box[2]);
        (float gridLeft, float gridRight, float gridTop)    = (box[3], box[4], box[5]);

        Assert.That(leaveLeft,  Is.GreaterThanOrEqualTo(gridLeft),      "Leave hangs off the left of the game's column");
        Assert.That(leaveRight, Is.LessThanOrEqualTo(gridRight + 1.0f), "Leave hangs off the right of the game's column");
        Assert.That(leaveTop,   Is.GreaterThanOrEqualTo(gridTop - 1.0f), "Leave sits above the game's column");
    }

    /// <summary>
    /// One tap on Leave takes the player off the table with no confirmation. The host covers the seat at once,
    /// without the grace period, and empties the player's match slot, and the empty slot routes the page to Home.
    /// No results screen is shown afterwards.
    /// <para>
    /// If the release does not detach the client, the table is still Playing and the seated-player rule
    /// navigates the page back to it. The settle wait in the test catches that.
    /// </para>
    /// </summary>
    [Test]
    public async Task LeavingIsOneTap_AndTakesThePlayerOffTheTableForGood()
    {
        await OpenTableAsync($"{OfflineTableUrl}&botThinkMs=0&resolvePauseMs=0");

        await Page.GetByTestId("leave").ClickAsync();

        await Expect(Page).ToHaveURLAsync(HomeUrlPattern, new() { Timeout = TurnTimeoutMs });

        // The offline host removes the table, because its only watcher was the player who left. Wait, then check
        // that the page is still on Home and shows neither the table nor a result.
        await Page.WaitForTimeoutAsync(1500);
        await Expect(Page).ToHaveURLAsync(HomeUrlPattern);
        await Expect(Page.GetByTestId("table")).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("results-overlay")).ToHaveCountAsync(0);
    }

    #endregion

    #region Abandonment

    [Test]
    public async Task PlayDoesNotBeginUntilTheJoinWindowCloses()
    {
        // Bots start thinking as soon as a table is dealt. Without the join window, they would play the first
        // cards while a cold client is still booting.
        await Page.GotoAsync($"{OfflineTableUrl}&botThinkMs=0&resolvePauseMs=0&joinWindowMs=600000&seatArrives=false");

        await Expect(Page.GetByTestId("match-phase")).ToHaveTextAsync("Playing", new() { Timeout = BootTimeoutMs });
        await Expect(Page.GetByTestId("plays-count")).ToHaveTextAsync("0");

        // The bots have zero think time, so a play count still at zero shows that the open join window holds
        // back the first turn.
        await Page.WaitForTimeoutAsync(1500);
        await Expect(Page.GetByTestId("plays-count")).ToHaveTextAsync("0");
    }

    #endregion
}
