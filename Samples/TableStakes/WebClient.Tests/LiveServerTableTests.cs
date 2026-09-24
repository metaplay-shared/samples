using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace WebClient.Tests;

/// <summary>
/// E2E tests for the table against a <b>live game server</b>: the match actor runs in the server process and the
/// table is persisted in the database. Unlike <see cref="TablePageTests"/>, which uses the in-process offline
/// server, these need both the WebClient server and <c>metaplay dev server</c> running.
/// <see cref="LiveServerMatchmakingTests"/> covers matchmaking.
/// Server timings are runtime options, so the tests run at the pace the server was started with. The E2E harness
/// shortens them. No test waits for a fixed duration, and every timeout allows for the slower shipped timings.
/// Every test joins the queue only through <see cref="PlayAndBeSeatedAsync"/>, which holds
/// <see cref="LiveServerLocks.QueueAsync"/> from the tap to the seated assertion.
/// </summary>
[TestFixture]
public class LiveServerTableTests : PlaywrightPageTest
{
    [Test]
    public async Task LiveTable_SeatsThePlayerAndDeliversTheirHand()
    {
        await PlayAndBeSeatedAsync();

        // The trump suit arrived with the replicated table state. It is drawn at the centre of the table with an
        // accessible name.
        ILocator trump = Page.GetByTestId("trump-suit");
        await Expect(trump).ToHaveAttributeAsync("data-suit", new Regex("^(Clubs|Diamonds|Hearts|Spades)$"));
        await Expect(Page.GetByRole(AriaRole.Img, new() { Name = $"Trump suit: {await trump.GetAttributeAsync("data-suit")}" })).ToBeVisibleAsync();

        int ownSeat = int.Parse(await Page.GetByTestId("own-seat").InnerTextAsync());
        for (int seat = 0; seat < 4; seat++)
        {
            ILocator plaque = Page.GetByTestId($"seat-{seat}");
            await Expect(plaque).ToBeVisibleAsync();
            await Expect(plaque).ToHaveAttributeAsync("data-position", ScreenPositionOfSeat(seat, ownSeat));
        }

        // The player's hand arrives on the private channel at subscribe time, together with the play index it was
        // read at. The client never receives other seats' cards.
        await Expect(Page.GetByTestId("own-hand-count")).ToHaveTextAsync("5", new() { Timeout = TurnTimeoutMs });
        await Expect(Page.GetByTestId("own-hand-index")).ToHaveTextAsync("0");

        // A player alone in the queue gets bots in the other three seats.
        await ExpectOnlyTheOwnSeatIsHumanAsync(Page);
    }

    [Test]
    public async Task LiveTable_PlaysAFullFiveTrickGame_OverTheNetwork()
    {
        await PlayAndBeSeatedAsync();

        ILocator handCount = Page.GetByTestId("own-hand-count");
        await Expect(handCount).ToHaveTextAsync("5", new() { Timeout = TurnTimeoutMs });

        // Measure the game's wall-clock length: the server's bot delays and resolve pauses, the network and the
        // browser, with the human playing as fast as possible. `Backend/SharedCode.Tests/MatchPacingTests` measures
        // the pacing alone. This measurement also includes the network and the browser (docs/match.md, "Runtime
        // options").
        Stopwatch gameClock = Stopwatch.StartNew();

        // One turn per trick. Waiting for a playable card also waits out the bots and the resolve pauses, which
        // timers on the match actor drive.
        for (int cardsLeft = 5; cardsLeft > 0; cardsLeft--)
        {
            await Expect(handCount).ToHaveTextAsync(cardsLeft.ToString(), new() { Timeout = TurnTimeoutMs });

            ILocator card = Page.GetByTestId("legal-card").First;
            await Expect(card).ToBeVisibleAsync(new() { Timeout = TurnTimeoutMs });
            await card.ClickAsync();

            // The card leaves the hand only when the server confirms the play. A refused card stays in the hand,
            // so this wait times out on the trick where the refusal happened. The refusal banner cannot replace
            // this check, because it clears when the table moves to the next play index.
            await Expect(handCount).ToHaveTextAsync((cardsLeft - 1).ToString(), new() { Timeout = TurnTimeoutMs });
            await Expect(Page.GetByTestId("hand-locked")).ToHaveTextAsync("false", new() { Timeout = TurnTimeoutMs });
        }

        await Expect(Page.GetByTestId("match-phase")).ToHaveTextAsync("Ended", new() { Timeout = TurnTimeoutMs });

        gameClock.Stop();
        TestContext.Out.WriteLine($"Live game length, deal to Ended, playing as fast as the client can: {gameClock.Elapsed.TotalSeconds:F1} s");

        // The limit is the budgeted length of a bot-filled game with a human thinking between cards. This test
        // taps as fast as possible, and the E2E harness also shortens the pacing, so the limit catches a stall or
        // a lost round trip, not a pacing regression. The line above logs the measured length.
        Assert.That(gameClock.Elapsed.TotalSeconds, Is.LessThan(45.0), "the table's own pacing plus the network must fit inside the budgeted game length");

        await Expect(Page.GetByTestId("plays-count")).ToHaveTextAsync("20");
        await Expect(Page.GetByTestId("tricks-resolved")).ToHaveTextAsync("5");

        // The results overlay is shown over the finished table.
        await Expect(Page.GetByTestId("results-overlay")).ToBeVisibleAsync(new() { Timeout = TurnTimeoutMs });
    }

    [Test]
    public async Task LiveTable_SurvivesAReload_AndReSeatsThePlayerAtTheSameTable()
    {
        await PlayAndBeSeatedAsync();

        // Play one card, so the returning player's hand shows whether the game is the same one.
        await Expect(Page.GetByTestId("own-hand-count")).ToHaveTextAsync("5", new() { Timeout = TurnTimeoutMs });
        string ownSeat = await Page.GetByTestId("own-seat").InnerTextAsync();
        ILocator card = Page.GetByTestId("legal-card").First;
        await Expect(card).ToBeVisibleAsync(new() { Timeout = TurnTimeoutMs });
        await card.ClickAsync();
        await Expect(Page.GetByTestId("own-hand-count")).ToHaveTextAsync("4", new() { Timeout = TurnTimeoutMs });

        // A page load starts a new session. The player actor re-attaches the match it references, so the table
        // returns in the state the server holds, not the state this browser last showed.
        //
        // trumpIntroMs holds the trump intro far longer than the test, so a table that wrongly treats itself as
        // newly dealt would still show the intro when the assertions below run.
        //
        // The time away is measured, because the WASM boot can take longer than the disconnect grace on a loaded
        // machine. After the grace a bot covers the seat, and bots play the table to the end before the assertions
        // below run.
        Stopwatch away = Stopwatch.StartNew();
        await Page.GotoAsync(ClientUrl("/table", "trumpIntroMs=600000"));

        // The player is back in the same seat of the same table. This is the first sign that the client has
        // returned, so the time away is measured up to it.
        await Expect(Page.GetByTestId("own-seat")).ToHaveTextAsync(ownSeat, new() { Timeout = BootTimeoutMs });
        away.Stop();

        Assert.That(away.ElapsedMilliseconds, Is.LessThan(DisconnectGraceMs),
            $"the reload took {away.ElapsedMilliseconds} ms, which is past the {DisconnectGraceMs} ms disconnect grace: "
            + "this run never tested the reload, it tested what happens after the grace lapses");

        await Expect(Page.GetByTestId("match-phase")).ToHaveTextAsync("Playing", new() { Timeout = BootTimeoutMs });
        await Expect(Page.GetByTestId("play-hero")).ToHaveCountAsync(0);

        // A player who returns to a part-played table sees no deal animation and no trump intro. The trump suit
        // is shown at full opacity.
        await Expect(Page.GetByTestId("table")).ToHaveAttributeAsync("data-dealing", "false");
        await Expect(Page.GetByTestId("trump-intro")).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("trump-suit")).ToHaveCSSAsync("opacity", "1");

        // The new session receives the remaining hand on the private channel at subscribe time. Occupancy reads
        // Human both when the grace held the seat and when the returning player reclaimed it from a covering bot,
        // so the hand count is the check. If the grace expired, bots would play the table to the end, and no cards
        // would remain.
        await Expect(Page.GetByTestId("own-hand-count")).ToHaveTextAsync("4", new() { Timeout = TurnTimeoutMs });
        await Expect(Page.GetByTestId("own-occupancy")).ToHaveTextAsync("Human");
    }

    /// <summary>
    /// Leaving takes one tap with no confirmation. The match actor gives the seat to a bot and releases the
    /// player, whose client shows Home. No results overlay appears later, even after the abandoned table finishes.
    /// The test waits for <c>PlayerEventMatchSeatLost</c> and <c>PlayerEventMatchFinished</c> in the player's event
    /// log, so the final assertions run after the point where a results overlay could appear.
    /// </summary>
    [Test]
    public async Task LiveTable_LeavingIsOneTap_AndNothingFollowsThePlayerHome()
    {
        await PlayAndBeSeatedAsync();

        // Read the player's name from the seat plaque before leaving. The event-log waits look the player up
        // by name, and the table page closes on leave.
        string ownSeat    = await Page.GetByTestId("own-seat").InnerTextAsync();
        string playerName = (await Page.GetByTestId($"seat-{ownSeat}-name").InnerTextAsync()).Trim();

        await Page.GetByTestId("leave").ClickAsync();

        // The release clears the player's match reference, which routes the client to Home.
        await Expect(Page).ToHaveURLAsync(new Regex(@"/$"), new() { Timeout = TurnTimeoutMs });

        await WaitForServerToHaveEventAsync(playerName, "PlayerEventMatchSeatLost");
        await WaitForServerToHaveEventAsync(playerName, "PlayerEventMatchFinished", 90_000);

        // The client stays on Home. It did not return to the table, and the table's results overlay did not
        // appear.
        await Expect(Page).ToHaveURLAsync(new Regex(@"/$"));
        await Expect(Page.GetByTestId("table")).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("results-overlay")).ToHaveCountAsync(0);
    }

    /// <summary>
    /// A cosmetic bought in the shop is shown on the player's seat at the table, and the opponents show cosmetics
    /// from the same catalogue (<c>docs/cosmetics.md</c>).
    /// <para>
    /// Only a live server covers the whole path. The purchase is a player action. The server builds the seat from
    /// the cosmetics the player actor reports when it commits to the seat. The seat roster then reaches the client
    /// over the entity protocol, and the table draws it. <c>SharedCode.Tests</c>, <c>Server.Tests</c> and
    /// <c>SeatPlaqueRenderTests</c> test each step separately.
    /// </para>
    /// </summary>
    [Test]
    public async Task LiveTable_DrawsTheCosmeticsThePlayerBought_AndDressesTheOpponentsToo()
    {
        const string SilverFrame      = "frame.silver";
        const string SilverFrameClass = "m-frame--frame-silver";

        // Buy the silver frame, and wait for the server to record the purchase before leaving the page. The client
        // sends a predicted action on the next flush, so a navigation before the flush would discard the purchase.
        await Page.GotoAsync(ClientUrl("/"));
        await WaitForLiveSessionAsync();
        string playerName = await ReadPlayerNameAsync();

        await Page.GotoAsync(ClientUrl("/profile/cosmetics"));
        await WaitForLiveSessionAsync();
        await Page.GetByTestId($"cosmetic-{SilverFrame}").ClickAsync();
        await Page.GetByTestId($"cosmetic-{SilverFrame}").GetByTestId("cosmetic-buy").ClickAsync();
        await Expect(Page.GetByTestId("confirm")).ToBeVisibleAsync();
        await Page.GetByTestId("confirm-accept").ClickAsync();
        await Expect(Page.GetByTestId("cosmetic-equipped")).ToBeVisibleAsync();

        await WaitForServerToHaveEventAsync(playerName, "PlayerEventCosmeticPurchased");

        await PlayAndBeSeatedAsync();

        // The buyer's own plaque shows the frame. The plaque is drawn from the seat roster that the server built,
        // not from this client's prediction.
        int ownSeat = int.Parse(await Page.GetByTestId("own-seat").InnerTextAsync());
        await Expect(Page.GetByTestId($"seat-{ownSeat}-avatar")).ToHaveClassAsync(new Regex(SilverFrameClass));

        // Each bot gets an avatar from the catalogue but never a frame. The frame the shop does not sell is the
        // season prize, and a bot wearing it would contradict "earned by winning a season".
        for (int seat = 0; seat < 4; seat++)
        {
            if (seat == ownSeat)
                continue;

            ILocator avatar = Page.GetByTestId($"seat-{seat}-avatar");
            await Expect(avatar.Locator("svg[data-token^='avatar-']")).ToBeVisibleAsync();
            await Expect(avatar).Not.ToHaveClassAsync(new Regex("m-frame--"));
        }
    }
}
