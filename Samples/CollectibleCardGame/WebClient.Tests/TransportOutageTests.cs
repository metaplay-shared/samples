using Microsoft.Playwright;
using System.Text.RegularExpressions;

namespace WebClient.Tests;

/// <summary>
/// <b>A transport outage is not a table going away.</b> The board must stay on screen through one, and no
/// notice may appear.
/// <para>
/// This is the false positive of <see cref="MatchAbandonTests"/>' abandon path. It cannot be observed through
/// a page reload, which destroys the Blazor app and cannot see an in-page detach at all: the detach and the
/// re-attach are not one continuation, so the board's navigation is keyed on the server's own verdict. A
/// laptop sleep, a backgrounded tab or a network switch that outlives the session must not bounce a player
/// out of a live match.
/// </para>
/// <para>
/// This suite <em>owns</em> its game server for one option: <c>Session:SessionLingerDuration</c> is zero, so
/// the server forgets a session the moment its connection closes, and the handshake tells the client so,
/// which then does not try to resume either. A lost transport therefore always ends the session at once, rather
/// than after a resume window has been waited out, and the player reconnects into a new one. The match is
/// untouched by that: the seat's <c>Match:DisconnectGrace</c> (30 s) and the table's <c>Match:ActorLinger</c>
/// (150 s) are far longer than the second or two the new session takes.
/// </para>
/// </summary>
[TestFixture]
[NonParallelizable]
public class TransportOutageTests : MatchTestBase
{
    const string NoSessionLinger = "Session:SessionLingerDuration=00:00:00";

    static GameServerProcess? _server;

    protected override bool LosesConnectionsOnPurpose => true;

    [OneTimeSetUp]
    public static async Task StartServer() => _server = await GameServerProcess.StartAsync(NoSessionLinger);

    [OneTimeTearDown]
    public static async Task StopServer()
    {
        if (_server != null)
            await _server.DisposeAsync();

        _server = null;
    }

    /// <summary>
    /// The page closes the session's WebSocket itself, which the server sees as the connection going away.
    /// The server stays up throughout — that is the whole point — and the table with it.
    /// </summary>
    [Test]
    public async Task ATransportOutageDoesNotAbandonTheMatch()
    {
        await Context.AddInitScriptAsync(TrackSocketsScript);

        await StartPracticeFromHomeAsync();
        await Expect(Page.GetByTestId("mulligan-overlay")).ToBeVisibleAsync(new() { Timeout = MatchTimeout });
        await ResolveMulliganAsync();
        await WaitForOwnTurnAsync(GameTimeout / 6);

        await Expect(Page.GetByTestId("match-board")).ToBeVisibleAsync();
        int turnBefore = await BoardTurnAsync();
        List<string> sessionsBefore = SessionTokens();
        Assert.That(sessionsBefore, Is.Not.Empty, "the client logged no session start");

        // Drop the transport mid-match. This detaches the match sub-entity in the middle of a live game, and the
        // session cannot be resumed, so the player reconnects to get it back in a new one.
        int closed = await Page.EvaluateAsync<int>(CloseSocketsScript);
        Assert.That(closed, Is.GreaterThan(0), "the page had no open WebSocket to close");

        await WaitOutTheSessionGapAsync();

        // A new session, not the old one resumed: the client logs a session start for each session it creates,
        // and a resume creates none.
        List<string> sessionsAfter = SessionTokens();
        Assert.That(sessionsAfter, Has.Count.GreaterThan(sessionsBefore.Count), "the session was resumed rather than started anew");
        Assert.That(sessionsAfter[^1], Is.Not.EqualTo(sessionsBefore[^1]));

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

    /// <summary> The token of each session the client has created, in order, off its own log. </summary>
    List<string> SessionTokens()
    {
        List<string> tokens = new List<string>();
        foreach (string line in ConsoleLog)
        {
            Match match = SessionCreated.Match(line);
            if (match.Success)
                tokens.Add(match.Groups[1].Value);
        }

        return tokens;
    }

    static readonly Regex SessionCreated = new Regex(@"Session created, token SessionToken\((\w+)\)");

    /// <summary> Keeps a handle on every WebSocket the page opens, so the test can close the one the session uses. </summary>
    const string TrackSocketsScript = """
        (() => {
            const Native = window.WebSocket;
            window.__e2eSockets = [];
            window.WebSocket = class extends Native {
                constructor(...args) { super(...args); window.__e2eSockets.push(this); }
            };
        })();
        """;

    /// <summary> Closes every open WebSocket the page holds, and returns how many it closed. </summary>
    const string CloseSocketsScript = """
        () => {
            const open = window.__e2eSockets.filter(socket => socket.readyState === WebSocket.OPEN);
            open.forEach(socket => socket.close());
            return open.length;
        }
        """;
}
