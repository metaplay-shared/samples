using Microsoft.Playwright;

namespace Game.Client.Tests;

/// <summary>
/// <b>A match that dies with its node is abandoned by both players.</b> The whole of the ephemeral
/// table's player-visible half, end to end.
/// <para>
/// This suite <em>owns</em> the game-server process so that it can restart it, which is how a match is
/// actually killed. A polite "shut this table down" endpoint would test a different sentence: the claim is
/// about a node going away, and a SIGTERM restart is also exactly the shape of the trade
/// <c>Docs/match.md</c> states — a rolling deploy ends every match in flight. The fixture and the doc
/// make the same claim.
/// </para>
/// <para>
/// It cannot share a machine with a manually started server. Run it on its own, with only the web client dev
/// server up:
/// <code>
/// dotnet run --project Client/Client.csproj
/// dotnet test Client.Tests/Client.Tests.csproj --filter "FullyQualifiedName~MatchAbandonTests"
/// </code>
/// </para>
/// </summary>
[TestFixture]
[NonParallelizable]
public class MatchAbandonTests : MatchTestBase
{
    GameServerProcess? _server;

    [TearDown]
    public async Task StopServer()
    {
        if (_server != null)
            await _server.DisposeAsync();

        _server = null;
    }

    [Test]
    public async Task AMatchThatDiesWithItsNodeIsAbandoned()
    {
        _server = await GameServerProcess.StartAsync();

        await StartPracticeFromHomeAsync();
        await Expect(Page.GetByTestId("mulligan-overlay")).ToBeVisibleAsync(new() { Timeout = MatchTimeout });
        await ResolveMulliganAsync();
        await WaitForOwnTurnAsync(GameTimeout / 6);

        // A real game in progress rather than a deal, so what is lost is a game somebody was playing.
        await PlayUntilPastTheOpeningAsync();

        if (await IsGameOverAsync())
            Assert.Ignore("the game ended before the restart");

        int turnBefore = await BoardTurnAsync();

        // A graceful stop and start: a rolling deploy. Nothing about the match is persisted, so the table
        // does not come back — which is the point.
        await _server.RestartAsync();

        // Wait out the session gap before reading anything, and this is not optional: the board on screen
        // OUTLIVES the session that fed it. The client holds its last committed model, so every assertion
        // below would otherwise be satisfied instantly by a fully-rendered pre-restart board that is pure
        // history. The gap appearing and then clearing is the only thing that separates "the new server is
        // feeding us" from "nothing has happened yet".
        await Expect(SessionGap.First).ToBeVisibleAsync(new() { Timeout = GameTimeout });
        await Expect(SessionGap).ToHaveCountAsync(0, new() { Timeout = GameTimeout });

        // The player is on Home and the notice says why. The pointer clear itself is invisible to a client —
        // CurrentMatch is ServerOnly — so this flag is the only thing that turns "you are back on Home" into
        // an explanation.
        await Expect(Page.GetByTestId("match-gone-notice")).ToBeVisibleAsync(new() { Timeout = GameTimeout });
        await Expect(Page.GetByTestId("match-board")).Not.ToBeVisibleAsync();

        TestContext.Out.WriteLine($"the match was abandoned on turn {turnBefore}");

        // THIS is the assertion that proves the pointer was cleared, and nothing else can:
        // PlayerStartPracticeMatch refuses with AlreadyInMatch while CurrentMatch is set, and CurrentMatch is
        // ServerOnly — so a fresh board is the only observable consequence of the clear.
        await Expect(Page.GetByTestId("queue-practice")).ToBeEnabledAsync(new() { Timeout = GameTimeout });
        await Page.GetByTestId("queue-practice").ClickAsync();
        await Expect(Page.GetByTestId("match-board")).ToBeVisibleAsync(new() { Timeout = MatchTimeout });
        await Expect(HandCards.First).ToBeVisibleAsync(new() { Timeout = MatchTimeout });

        // Leave the fresh table rather than leaving it running into the teardown. The mulligan is answered
        // first because it is a modal overlay and Leave is behind it — the same thing
        // PressingPracticeTwiceSeatsTheAccountOnce has to do, for the same reason.
        await ResolveMulliganAsync();
        await Page.GetByTestId("leave-match").ClickAsync();
        await Expect(Page.GetByTestId("display-name")).ToBeVisibleAsync(new() { Timeout = GameTimeout });

        // And the notice is NOT back. This press of Practice never went through the OK button — which is
        // exactly what a player does — so the flag is cleared by the match starting rather than by the
        // dismissal. Without that it survives on Home as a persisted member and tells them two matches later
        // that their last one ended early.
        await Expect(Page.GetByTestId("queue-practice")).ToBeEnabledAsync(new() { Timeout = GameTimeout });
        await Expect(Page.GetByTestId("match-gone-notice")).Not.ToBeVisibleAsync();
    }

    /// <summary>
    /// The notice is read once. It is a fact about a match that is over, so leaving it up would make Home
    /// permanently about a game the player has already been told they lost.
    /// </summary>
    [Test]
    public async Task TheNoticeIsDismissedAndDoesNotComeBack()
    {
        _server = await GameServerProcess.StartAsync();

        await StartPracticeFromHomeAsync();
        await Expect(Page.GetByTestId("mulligan-overlay")).ToBeVisibleAsync(new() { Timeout = MatchTimeout });
        await ResolveMulliganAsync();
        await WaitForOwnTurnAsync(GameTimeout / 6);

        await _server.RestartAsync();

        await Expect(SessionGap.First).ToBeVisibleAsync(new() { Timeout = GameTimeout });
        await Expect(SessionGap).ToHaveCountAsync(0, new() { Timeout = GameTimeout });

        await Expect(Page.GetByTestId("match-gone-notice")).ToBeVisibleAsync(new() { Timeout = GameTimeout });
        await Page.GetByTestId("match-gone-dismiss").ClickAsync();
        await Expect(Page.GetByTestId("match-gone-notice")).Not.ToBeVisibleAsync(new() { Timeout = MatchTimeout });

        // A reload starts a fresh session, which runs the handshake again. The pointer is already clear, so
        // there is nothing to notice a second time — a flag that came back here would mean the clear or the
        // dismissal did not stick.
        //
        // The order of the next three lines is the fixture. `Not.ToBeVisibleAsync` passes the instant the
        // element is absent, and after a reload it is absent before the session has even started — so
        // sampling it straight away would pass whether or not the flag was about to arrive. The notice reaches
        // a client through an enqueued server action, which runs "almost instantly but notably NOT
        // synchronously", so this waits for something downstream of the handshake and then gives the flag a
        // bounded window to show up in rather than sampling once.
        await Page.ReloadAsync();
        await Expect(Page.GetByTestId("display-name")).ToBeVisibleAsync(new() { Timeout = BootTimeout });
        await Expect(Page.GetByTestId("queue-practice")).ToBeEnabledAsync(new() { Timeout = BootTimeout });

        ILocator notice = Page.GetByTestId("match-gone-notice");
        for (int waited = 0; waited < 3000; waited += 250)
        {
            Assert.That(await notice.CountAsync(), Is.Zero, "the notice came back after a session that had nothing to notice");
            await Page.WaitForTimeoutAsync(250);
        }
    }

    /// <summary>
    /// <b>A transport outage is not a table going away.</b> The board must stay on screen through one, and no
    /// notice may appear.
    /// <para>
    /// This is the false positive of the abandon path, and it needs a fixture of its own because the reconnect
    /// the match suite already has is a page reload — which destroys the Blazor app and cannot observe an
    /// in-page detach at all. The board used to navigate Home on the detach edge, believing the detach and the
    /// re-attach were one continuation; they are not, so a laptop sleep, a backgrounded tab or a network
    /// switch that outlived the session-resume window bounced a player out of a live match with no
    /// explanation. The navigation is keyed on the server's own verdict now.
    /// </para>
    /// <para>
    /// The server stays up throughout — that is the whole point. Only the browser's transport goes, for longer
    /// than the resume window and well inside the actor's linger, so the table is still there to come back to.
    /// </para>
    /// </summary>
    [Test]
    public async Task ATransportOutageDoesNotAbandonTheMatch()
    {
        _server = await GameServerProcess.StartAsync();

        await StartPracticeFromHomeAsync();
        await Expect(Page.GetByTestId("mulligan-overlay")).ToBeVisibleAsync(new() { Timeout = MatchTimeout });
        await ResolveMulliganAsync();
        await WaitForOwnTurnAsync(GameTimeout / 6);

        await Expect(Page.GetByTestId("match-board")).ToBeVisibleAsync();
        int turnBefore = await BoardTurnAsync();

        // Drop the transport without touching the server, and hold it down long enough that the session
        // cannot be resumed — the shell has to start a new one, which is the case that detaches the
        // sub-entity in the middle of a live match.
        await Context.SetOfflineAsync(true);
        await Expect(SessionGap.First).ToBeVisibleAsync(new() { Timeout = GameTimeout });
        await Page.WaitForTimeoutAsync(15000);
        await Context.SetOfflineAsync(false);

        // The session comes back on its own.
        await Expect(SessionGap).ToHaveCountAsync(0, new() { Timeout = GameTimeout });

        // The board is still there, and the match is the same one: the turn counter cannot have gone
        // backwards, and the history came back with it.
        await Expect(Page.GetByTestId("match-board")).ToBeVisibleAsync(new() { Timeout = GameTimeout });
        await Expect(HandCards.First).ToBeVisibleAsync(new() { Timeout = GameTimeout });
        Assert.That(await BoardTurnAsync(), Is.GreaterThanOrEqualTo(turnBefore),
            "the match restarted from the deal rather than resuming");
        Assert.That(await Page.GetByTestId("event-line").CountAsync(), Is.GreaterThan(0));

        // And nothing told the player their match went away, because it did not.
        Assert.That(await Page.GetByTestId("match-gone-notice").CountAsync(), Is.Zero,
            "a transport outage raised the table-is-gone notice");

        // The board still takes input, which is what says the player is in the game rather than looking at
        // history: this seat's turn comes round and a turn can be played.
        Assert.That(await WaitForOwnTurnAsync(GameTimeout / 6), Is.True, "the board never took input again");
    }

    /// <summary>
    /// Play this seat's turns until the counter is past the opening exchange. The counter runs over both
    /// seats' turns and which seat moves first comes out of the deal seed, so "one turn of ours" is not the
    /// same thing on every deal.
    /// </summary>
    async Task PlayUntilPastTheOpeningAsync()
    {
        for (int turn = 0; turn < 8; turn++)
        {
            if (await IsGameOverAsync())
                return;

            if (await BoardTurnAsync() > 2)
                return;

            if (!await WaitForOwnTurnAsync(GameTimeout / 6))
                return;

            await PlayOneTurnAsync();
        }
    }
}
