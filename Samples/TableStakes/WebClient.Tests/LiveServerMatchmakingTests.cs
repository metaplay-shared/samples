using System.Diagnostics;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

namespace WebClient.Tests;

/// <summary>
/// E2E tests for matchmaking against a <b>live game server</b>. They need both the WebClient server and
/// <c>metaplay dev server</c> running. A lone player is seated with three bots, and two browsers that tap Play
/// inside one fill wait share <i>one</i> table, which rules out a matchmaker that gives every player their own table.
/// The checks hold for any non-zero fill wait, so the E2E harness shortens it. <c>Backend/Server.Tests</c> checks
/// the shipped length.
/// Every tap runs inside <see cref="LiveServerLocks.QueueAsync"/> until the seating assertion. Two-browser cases
/// hold one lock across both taps, and <see cref="Cancelling_TakesThePlayerOutOfTheQueue"/> holds it until its watch
/// for a table ends, because a tap from another fixture in that window would join or form a table.
/// </summary>
[TestFixture]
public class LiveServerMatchmakingTests : PlaywrightPageTest
{
    // The seating wait covers the server's fill wait plus the round trips around it.
    private const int SeatingTimeoutMs = 30000;

    /// <summary>
    /// How long after the first tap both players must be seated. It bounds the fill wait, the table formation and
    /// both attaches. It does not prove the two players shared one fill wait, because two consecutive waits also
    /// fit inside it. The identical seat names prove that. This bound catches a search that runs until one of the
    /// server's much longer backstop timeouts instead of ending after the fill wait.
    /// </summary>
    private const int SeatedTogetherWithinMs = 20000;

    /// <summary>
    /// How long a cancelled search is watched for a table that must not arrive. A locator cannot wait for an
    /// absence, so this is a fixed sleep. It must be longer than the fill wait, or the test passes whether or not
    /// the cancel worked.
    /// <para>
    /// It is derived from <see cref="PlaywrightPageTest.FillWaitMs"/>, the fill wait of the server under test,
    /// because the harness shortens the wait and a server started by hand runs the shipped default. The extra
    /// 80% leaves room for the table formation and the attach after the wait.
    /// </para>
    /// </summary>
    private static readonly int PastTheFillWaitMs = FillWaitMs * 9 / 5;

    /// <summary>
    /// Boot a page to Home and wait until its session is behind the screen, so its PLAY answers. Returns the PLAY
    /// button.
    /// </summary>
    private async Task<ILocator> OpenHomeAsync(IPage page)
    {
        await page.GotoAsync(ClientUrl("/"));
        await Expect(page.GetByTestId("session")).ToHaveAttributeAsync("data-state", "live", new() { Timeout = BootTimeoutMs });

        ILocator play = page.GetByTestId("play");
        await Expect(play).ToBeVisibleAsync(new() { Timeout = BootTimeoutMs });
        return play;
    }

    /// <summary>The four seat names on one page's table, in seat order.</summary>
    private static async Task<string[]> ReadSeatNamesAsync(IPage page)
    {
        string[] names = new string[4];
        for (int seat = 0; seat < 4; seat++)
            names[seat] = await page.GetByTestId($"seat-{seat}-name").InnerTextAsync();
        return names;
    }

    [Test]
    public async Task OnePlayerAlone_IsSeatedWithThreeBots()
    {
        ILocator play = await OpenHomeAsync(Page);

        await using (await LiveServerLocks.QueueAsync("one player alone"))
        {
            await play.ClickAsync();

            // The searching dialog opens over the screen the player tapped Play on. The Play button and the
            // shell's navigation stay visible behind it, and the route does not change.
            await Expect(Page.GetByTestId("searching")).ToBeVisibleAsync();
            await Expect(Page.GetByTestId("searching-text")).ToHaveTextAsync("Finding you a table");
            await Expect(play).ToBeVisibleAsync();
            await Expect(Page.GetByTestId("primary-nav")).ToBeVisibleAsync();
            Assert.That(RouteOf(Page.Url), Is.EqualTo("/"), "the page turned before there was a table to show");

            // The countdown shows the deadline the server sends with the queue status, measured against the
            // client's estimate of the server clock.
            await Expect(Page.GetByTestId("searching-countdown")).ToBeVisibleAsync();

            // Nobody else joins the queue during the fill wait, so bots take the three empty seats.
            await Expect(Page.GetByTestId("match-phase")).ToHaveTextAsync("Playing", new() { Timeout = SeatingTimeoutMs });
            await Expect(Page.GetByTestId("searching")).ToHaveCountAsync(0);
        }

        await ExpectOnlyTheOwnSeatIsHumanAsync(Page);
    }

    [Test]
    public async Task TwoBrowsers_TappingPlayTogether_AreSeatedAtTheSameTable()
    {
        // Two browser contexts are two accounts: credentials live in the page's own storage, so an isolated
        // context logs in as a guest of its own rather than force-terminating the other's session.
        IBrowserContext second = await Browser.NewContextAsync();
        try
        {
            IPage pageA = Page;
            IPage pageB = await second.NewPageAsync();

            // Both pages show the Play button before either taps, so the two taps land inside one fill wait
            // instead of a WASM boot apart. The two boots are independent, so they run at the same time.
            await Task.WhenAll(OpenHomeAsync(pageA), OpenHomeAsync(pageB));

            Stopwatch sinceTapped = Stopwatch.StartNew();

            // One lock covers both taps and both seat assertions, because the two pages must share a table with
            // each other and not with a player from another fixture.
            await using (await LiveServerLocks.QueueAsync("two browsers tapping play together"))
            {
                await pageA.GetByTestId("play").ClickAsync();
                await pageB.GetByTestId("play").ClickAsync();

                await Expect(pageA.GetByTestId("match-phase")).ToHaveTextAsync("Playing", new() { Timeout = SeatingTimeoutMs });
                await Expect(pageB.GetByTestId("match-phase")).ToHaveTextAsync("Playing", new() { Timeout = SeatingTimeoutMs });
            }
            sinceTapped.Stop();

            Assert.That(sinceTapped.ElapsedMilliseconds, Is.LessThan(SeatedTogetherWithinMs),
                "both players should be seated by the one fill wait their taps fell inside");

            // Neither client shows the table id, so identical seat names in identical seat order prove both
            // browsers are at the same table.
            string[] namesA = await ReadSeatNamesAsync(pageA);
            string[] namesB = await ReadSeatNamesAsync(pageB);
            Assert.That(namesB, Is.EqualTo(namesA), "the two browsers are looking at different tables");

            // Each client draws its own player at South, so the seat indexes must differ to prove the players
            // sit in different seats of one table.
            int seatA = int.Parse(await pageA.GetByTestId("own-seat").InnerTextAsync());
            int seatB = int.Parse(await pageB.GetByTestId("own-seat").InnerTextAsync());
            Assert.That(seatB, Is.Not.EqualTo(seatA));

            // Both browsers must show exactly two humans and two bots. Checking only the two human seats would
            // also pass on a table of four humans, which a queue that pooled players from other tests produces.
            foreach (IPage page in new[] { pageA, pageB })
            {
                for (int seat = 0; seat < 4; seat++)
                {
                    ILocator plaque = page.GetByTestId($"seat-{seat}");
                    bool     isHuman = seat == seatA || seat == seatB;
                    await Expect(plaque).ToHaveAttributeAsync("data-occupancy", isHuman ? "Human" : "Bot");
                }
            }
        }
        finally
        {
            await second.CloseAsync();
        }
    }

    [Test]
    public async Task Cancelling_TakesThePlayerOutOfTheQueue()
    {
        ILocator play = await OpenHomeAsync(Page);

        // The lock is held until the watch below ends. A tap from another fixture during the watch would form a
        // table and fail the test even though the cancel worked.
        await using (await LiveServerLocks.QueueAsync("cancelling takes the player out of the queue"))
        {
            await play.ClickAsync();

            ILocator cancel = Page.GetByTestId("cancel-search");
            await Expect(cancel).ToBeVisibleAsync();
            await cancel.ClickAsync();

            // The dialog closes when the server answers the cancel, not on the tap. The server refuses a cancel
            // that arrives after the seat is committed, and closing early would show the menu to a player who
            // is about to be seated.
            await Expect(Page.GetByTestId("play")).ToBeVisibleAsync();
            await Expect(Page.GetByTestId("searching")).ToHaveCountAsync(0);

            // No table arrives after the fill wait has passed, which proves the server removed the queue entry
            // and the client did not only hide the dialog.
            TestContext.Out.WriteLine(
                $"Watching for a table that must never arrive for {PastTheFillWaitMs} ms (the server's fill wait is {FillWaitMs} ms)");
            await Page.WaitForTimeoutAsync(PastTheFillWaitMs);
            await Expect(Page.GetByTestId("match-phase")).ToHaveCountAsync(0);
            await Expect(Page.GetByTestId("play")).ToBeVisibleAsync();
        }
    }

    /// <summary>
    /// Starting a game changes the page once, when the table is ready to draw.
    /// <para>
    /// A predicate runs on every animation frame from the tap until the table appears, and records any frame
    /// that shows none of the menu, the searching dialog and the table. An assertion made after the table
    /// appears cannot see such a frame.
    /// </para>
    /// </summary>
    [Test]
    public async Task StartingAGame_TurnsThePageOnceAndShowsNoScreenInBetween()
    {
        ILocator play = await OpenHomeAsync(Page);

        int  frames;
        bool wasBlank;
        await using (await LiveServerLocks.QueueAsync("starting a game turns the page once"))
        {
            await play.ClickAsync();

            // WaitForFunction polls on every animation frame by default. The predicate runs in the page, so it
            // keeps its state on window.
            await Page.WaitForFunctionAsync(@"() => {
                const state = window.__ftStartup = window.__ftStartup || { blank: false, frames: 0 };
                const menu   = document.querySelector('[data-testid=""play-hero""]');
                const dialog = document.querySelector('[data-testid=""searching""]');
                const table  = document.querySelector('[data-testid=""match-phase""]');
                state.frames++;
                if (!menu && !dialog && !table)
                    state.blank = true;
                return !!table;
            }", null, new PageWaitForFunctionOptions { Timeout = SeatingTimeoutMs });

            frames   = await Page.EvaluateAsync<int>("() => window.__ftStartup.frames");
            wasBlank = await Page.EvaluateAsync<bool>("() => window.__ftStartup.blank");

            // The page shows the running game, not a loading screen.
            await Expect(Page.GetByTestId("match-phase")).ToHaveTextAsync("Playing", new() { Timeout = SeatingTimeoutMs });
        }

        Assert.That(frames, Is.GreaterThan(1), "the sampler never got to look at the wait");
        Assert.That(wasBlank, Is.False, "a frame showed neither the menu, the searching dialog nor the table");

        await Expect(Page.GetByTestId("table-loading")).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("searching")).ToHaveCountAsync(0);
    }

    /// <summary>
    /// Tapping Play in the navigation bar on Compete starts a search without changing the route. The page changes
    /// only when the table arrives.
    /// <para>
    /// <see cref="OnePlayerAlone_IsSeatedWithThreeBots"/> covers Home. The search is hosted by the shell layout
    /// that every screen uses, and this test checks it on a screen without the Home Play button.
    /// </para>
    /// </summary>
    [Test]
    public async Task TappingTheBarsPlayOnAnotherScreen_SearchesInPlaceUntilTheTableArrives()
    {
        await Page.GotoAsync(ClientUrl("/compete"));
        await WaitForLiveSessionAsync();

        await using (await LiveServerLocks.QueueAsync("tapping the bar's play on another screen"))
        {
            await Page.GetByTestId("nav-play").ClickAsync();

            await Expect(Page.GetByTestId("searching")).ToBeVisibleAsync();
            Assert.That(RouteOf(Page.Url), Is.EqualTo("/compete"), "the search opened no screen of its own");

            await Expect(Page.GetByTestId("match-phase")).ToHaveTextAsync("Playing", new() { Timeout = SeatingTimeoutMs });
            await Expect(Page.GetByTestId("searching")).ToHaveCountAsync(0);
        }
    }

    /// <summary>
    /// A player who taps Play and closes the tab stays in the queue, because their session outlives the closed
    /// socket by longer than the fill wait. Table formation must check that each queued player is still connected
    /// and skip the player who left.
    /// <para>
    /// The remaining player must see a bot in the seat the leaver would have taken. A check that asks only whether
    /// a session exists would seat the leaver and fail this test with two human seats.
    /// </para>
    /// </summary>
    [Test]
    public async Task APlayerWhoClosesTheirTabIsNotSeated_AndTheirSeatGoesToABot()
    {
        IBrowserContext leaver = await Browser.NewContextAsync();
        try
        {
            IPage stays = Page;
            IPage goes  = await leaver.NewPageAsync();

            // Both pages boot before either taps, so the two taps land inside one fill wait. The two boots are
            // independent, so they run at the same time.
            await Task.WhenAll(OpenHomeAsync(stays), OpenHomeAsync(goes));

            // One lock covers both taps and the remaining player's seat assertion, so a tap from another fixture
            // cannot take the leaver's seat.
            await using (await LiveServerLocks.QueueAsync("a player who closes their tab is not seated"))
            {
                await stays.GetByTestId("play").ClickAsync();
                await goes.GetByTestId("play").ClickAsync();
                await Expect(goes.GetByTestId("searching")).ToBeVisibleAsync();

                // Close the leaver's tab while the fill wait is still running.
                await leaver.CloseAsync();

                await Expect(stays.GetByTestId("match-phase")).ToHaveTextAsync("Playing", new() { Timeout = SeatingTimeoutMs });
            }

            await ExpectOnlyTheOwnSeatIsHumanAsync(stays);
        }
        finally
        {
            await leaver.CloseAsync();
        }
    }
}
