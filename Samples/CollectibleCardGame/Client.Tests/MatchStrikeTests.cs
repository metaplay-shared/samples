using Microsoft.Playwright;
using System.Globalization;

namespace Game.Client.Tests;

/// <summary>
/// <b>An idle turn costs a strike, and two cost the seat.</b> The player presses nothing at all after the
/// mulligan, and the board tells them what each lapse cost.
/// <para>
/// This suite <em>owns</em> its game-server process, and for one reason: it needs a turn deadline of a few
/// seconds, and it is the only fixture that does. The end-to-end profile's deadline is five minutes
/// deliberately — `MatchTests.TheDeadlineRingIsWithheldUntilTheDeadlineCloses` reads that number, and a
/// fixture overtaken by a deadline fails for a reason that has nothing to do with what it asserts — so
/// shortening it in `Options.e2e.yaml` would break the suite that shares the server. Bringing its own server
/// is how one fixture gets one timing without spending everybody else's.
/// </para>
/// <para>
/// Run it the way <see cref="MatchAbandonTests"/> is run: with only the web client dev server up, and nothing
/// else on the machine.
/// <code>
/// dotnet run --project Client/Client.csproj
/// dotnet test Client.Tests/Client.Tests.csproj --filter "FullyQualifiedName~MatchStrikeTests"
/// </code>
/// </para>
/// </summary>
[TestFixture]
[NonParallelizable]
public class MatchStrikeTests : MatchTestBase
{
    /// <summary>
    /// Five seconds, armed at eight: <c>Match:PresentationAllowance</c> is added to every positive decision
    /// clock. Short enough that two lapses and the auto-played turn between them fit inside one fixture, and
    /// long enough that the board is settled and on turn well before the first one fires.
    /// </summary>
    const string ShortTurnDeadline = "Match:TurnDeadline=00:00:05";

    GameServerProcess? _server;

    [TearDown]
    public async Task StopServer()
    {
        if (_server != null)
            await _server.DisposeAsync();

        _server = null;
    }

    /// <summary>
    /// One lapse is being slow; two is being gone. What the fixture actually proves is the pair of facts a
    /// player can see: after the first lapse the turn has moved on without them and the seat is still theirs,
    /// and after the second a bot has it under their own name.
    /// <para>
    /// The reserve is not in the way, and that is a property rather than luck: a turn deadline is extended out
    /// of the bank only by a seat that has already acted this turn, and every turn opens with nobody having acted.
    /// A player who does nothing therefore takes the strike immediately rather than four extensions later.
    /// </para>
    /// <para>
    /// Taking the seat back goes through the notice's own button, which reloads the board: arriving asks for
    /// the seat, and the table hands it over at the next turn boundary. A press from the covered seat would
    /// ask the same way, but at the suite's zero think delay a covered turn is over before a press could land
    /// in it; the ask-then-boundary rule itself is pinned in `MatchSeatPolicyTests`.
    /// </para>
    /// </summary>
    [Test]
    public async Task AnIdleTurnCostsAStrikeAndTwoCostTheSeat()
    {
        _server = await GameServerProcess.StartAsync(ShortTurnDeadline);

        string ownName = await StartPracticeFromHomeAsync();
        await Expect(Page.GetByTestId("mulligan-overlay")).ToBeVisibleAsync(new() { Timeout = MatchTimeout });
        await ResolveMulliganAsync();

        Assert.That(await WaitForOwnTurnAsync(GameTimeout / 6), Is.True, "the first own turn never arrived");
        int turnBefore = await BoardTurnAsync();

        // And from here the player does nothing whatsoever. Every assertion below is about what the table and
        // the board do to a seat that is present and silent.
        ILocator notice = Page.GetByTestId("covered-seat-notice");
        await Expect(notice).ToBeVisibleAsync(new() { Timeout = MatchTimeout });
        await Expect(notice).ToHaveAttributeAsync("data-stage", "struck");
        await Expect(notice).ToContainTextAsync("One more and a bot takes the seat");

        // The turn was played out rather than the table stalling on a lapsed stamp: half a turn is not a state
        // the game is left in. This waits rather than reads, because `data-turn` is the **presented** position
        // and a whole auto-played turn's worth of events is still draining through the board's own queue at
        // the moment the notice goes up.
        await Expect(TurnIndicator).Not.ToHaveAttributeAsync(
            "data-turn", turnBefore.ToString(CultureInfo.InvariantCulture), new() { Timeout = MatchTimeout });

        int turnAfter = await BoardTurnAsync();
        Assert.That(turnAfter, Is.GreaterThan(turnBefore),
            "the lapsed turn should have been played out and ended");

        // One lapse is being slow, so the seat is still this player's and their plaque says nothing.
        await Expect(Page.GetByTestId("den-mine").GetByTestId("den-bot-mark")).ToHaveCountAsync(0);

        // The second lapse. The notice is transient and the cover is not, so this is a wait on the standing
        // one rather than on the same element changing hands in a single frame.
        //
        // Only the sentence's stable half is asserted. The cover lands at the end of the struck seat's own
        // turn, so the standing line is the off-turn wording at that instant — but with both seats now
        // bot-driven at the suite's zero think delay the table changes hands within a few hundred
        // milliseconds, so which of the two wordings is up is a race. Both are pinned deterministically in
        // BoardChromeTests, off the `covered` and `covered-waiting` scenes.
        await Expect(notice).ToHaveAttributeAsync("data-stage", "covered", new() { Timeout = MatchTimeout });
        await Expect(notice).ToContainTextAsync("A bot is playing your seat");

        ILocator ownDen = Page.GetByTestId("den-mine");
        await Expect(ownDen.GetByTestId("den-bot-mark")).ToBeVisibleAsync(new() { Timeout = MatchTimeout });

        // The cover is not a disguise: the identity is kept, so the plaque carries this account's own name
        // beside the mark. Compared against the name Home showed before the match rather than merely against
        // the opponent's, which any other bot-roster name would also have satisfied.
        await Expect(ownDen.GetByTestId("den-name")).ToHaveTextAsync(ownName);

        // The seat is handed back at the next turn boundary rather than at once. With both seats bot-driven at
        // the suite's zero think delay that boundary is moments away, so the "back from the next turn" line
        // may already be gone by the time the board is up; what is asserted is where the table ends.
        await Page.GetByTestId("reclaim-seat").ClickAsync();
        await Expect(ownDen.GetByTestId("den-bot-mark")).ToBeHiddenAsync(new() { Timeout = MatchTimeout });
        await Expect(notice).ToBeHiddenAsync(new() { Timeout = MatchTimeout });
        await Expect(ownDen.GetByTestId("den-name")).ToHaveTextAsync(ownName);

        TestContext.Out.WriteLine($"struck out on turn {turnAfter}; the seat is covered under the name {ownName}");
    }

    /// <summary>
    /// <b>Any intent from the seat clears the count, refused ones included.</b> The clause that makes the
    /// strike run <em>consecutive</em>: a player who takes one slow turn early and one slow turn late has
    /// taken two first strikes, not a second one.
    /// <para>
    /// The press goes out with <c>?stale=1</c>, so an End turn precedes it and the press itself is
    /// <b>refused</b> as arriving after the turn passed. The actor marks its sender present <em>before</em> it
    /// looks at legality, so both halves clear the count; the refusal is asserted so the case is known to have
    /// taken the refusal path.
    /// </para>
    /// <para>
    /// The reload the flag needs is not what clears the count, and it matters that it is not: arriving leaves
    /// the strikes alone, so a seat with one strike against it comes back with one strike against it. What
    /// clears it is the press.
    /// </para>
    /// </summary>
    [Test]
    public async Task AnIntentBetweenTwoLapsesKeepsTheSeat()
    {
        _server = await GameServerProcess.StartAsync(ShortTurnDeadline);

        string ownName = await StartPracticeFromHomeAsync();
        await Expect(Page.GetByTestId("mulligan-overlay")).ToBeVisibleAsync(new() { Timeout = MatchTimeout });
        await ResolveMulliganAsync();

        Assert.That(await WaitForOwnTurnAsync(GameTimeout / 6), Is.True, "the first own turn never arrived");

        // One lapse, and nothing else.
        ILocator notice = Page.GetByTestId("covered-seat-notice");
        await Expect(notice).ToHaveAttributeAsync("data-stage", "struck", new() { Timeout = MatchTimeout });

        // Now the flag, and then one press the server will refuse. The reload comes back on the same table
        // through the account's pointer, with the strike still against the seat.
        await ReloadBoardWithAsync("stale=1");
        Assert.That(await WaitForOwnTurnAsync(GameTimeout / 6), Is.True, "the seat lost its turn across the reload");

        await Expect(EndTurn).ToBeEnabledAsync(new() { Timeout = MatchTimeout });
        await EndTurn.ClickAsync();

        // The press itself is refused, the turn having passed. Waited for by COUNT rather than by visibility,
        // because that refusal deliberately renders no words — the player is not told off for losing a race.
        await Expect(Page.Locator("[data-testid='refusal-notice'][data-reason='NotYourTurn']"))
            .ToHaveCountAsync(1, new() { Timeout = MatchTimeout });

        // The turn ended, and the next own turn's deadline runs out too — and this is a FIRST strike, not a
        // second. The struck notice returning is what says so: at two the seat would be covered and the
        // standing notice would have replaced it.
        await Expect(notice).ToHaveAttributeAsync("data-stage", "struck", new() { Timeout = GameTimeout / 6 });

        ILocator ownDen = Page.GetByTestId("den-mine");
        await Expect(ownDen.GetByTestId("den-bot-mark")).ToHaveCountAsync(0);
        await Expect(ownDen.GetByTestId("den-name")).ToHaveTextAsync(ownName);

        TestContext.Out.WriteLine("two lapses with one refused press between them, and the seat is still its owner's");
    }
}
