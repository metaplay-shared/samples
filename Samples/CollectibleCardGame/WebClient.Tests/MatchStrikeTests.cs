using Microsoft.Playwright;
using System.Globalization;

namespace WebClient.Tests;

/// <summary>
/// <b>An idle turn costs a strike, and two cost the seat.</b> One player presses nothing at all after the
/// mulligan, and the board tells them what each lapse cost.
/// <para>
/// <b>Nothing here waits for a clock.</b> The idle seat's turns are lapsed on demand, through the board's
/// developer tool <em>Run out the clock</em> (<c>DebugLapseDeadlineIntent</c>), which runs the same lapse the
/// turn deadline's own timer runs. It is pressed on the <em>other</em> player's board: an intent from the idle
/// seat would count as it being present and clear its strikes, which is the very thing under test. So each
/// case seats two accounts, and the other one is a developer.
/// </para>
/// <para>
/// The second player is also what keeps the table still. A bot opponent at the suite's zero think delay plays
/// its turns the moment they open, and once the idle seat is covered, two bots play the rest of the game out in
/// about a second. The second player ends its turns only when the fixture says so, and the end-to-end profile's
/// five-minute turn deadline never runs out under it.
/// </para>
/// <para>
/// The deadline ring that counts a turn down is the board's presentation and is pinned off the scenes, in
/// <see cref="BoardChromeTests"/>; the strike and cover rules are `MatchSeatPolicyTests`'.
/// </para>
/// </summary>
[TestFixture]
[NonParallelizable]
public class MatchStrikeTests : MatchTestBase
{
    /// <summary>
    /// Long enough for two Ranked taps to be paired and both boards to come up, as in
    /// <see cref="MatchmakingTests"/>.
    /// </summary>
    const int QueueTimeout = 45000;

    /// <summary>
    /// One lapse is being slow; two is being gone. What the fixture actually proves is the pair of facts a
    /// player can see: after the first lapse the turn has moved on without them and the seat is still theirs,
    /// and after the second a bot has it under their own name — until they take it back.
    /// <para>
    /// The cover lands at the end of the idle seat's own turn, which hands the table to the other player, who
    /// holds it. Taking the seat back goes through the notice's own button, which reloads the board: arriving
    /// asks for the seat, and the table hands it over at the next turn boundary — the one the other player makes
    /// by ending the turn it held. The covering bot never takes a turn.
    /// </para>
    /// </summary>
    [Test]
    public async Task AnIdleTurnCostsAStrikeAndTwoCostTheSeat()
    {
        (IPage idle, IPage other, string idleName) = await SeatAnIdlePlayerAndADeveloperAsync();

        await EndTurnIfOnTurnAsync(other);
        Assert.That(await WaitForOwnTurnOnAsync(idle, MatchTimeout), Is.True, "the first own turn never arrived");
        int turnBefore = await TurnOnAsync(idle);

        // From here the idle player does nothing whatsoever. Every assertion below is about what the table and
        // the board do to a seat that is present and silent.
        await RunOutTheClockAsync(other);

        ILocator notice = idle.GetByTestId("covered-seat-notice");
        await Expect(notice).ToHaveAttributeAsync("data-stage", "struck", new() { Timeout = MatchTimeout });
        await Expect(notice).ToContainTextAsync("One more and a bot takes the seat");

        // The turn was played out rather than the table stalling on a lapsed stamp: half a turn is not a state
        // the game is left in. This waits rather than reads, because `data-turn` is the **presented** position
        // and a whole auto-played turn's worth of events is still draining through the board's own queue at
        // the moment the notice goes up.
        await Expect(TurnIndicatorOn(idle)).Not.ToHaveAttributeAsync(
            "data-turn", turnBefore.ToString(CultureInfo.InvariantCulture), new() { Timeout = MatchTimeout });

        int turnAfter = await TurnOnAsync(idle);
        Assert.That(turnAfter, Is.GreaterThan(turnBefore), "the lapsed turn should have been played out and ended");

        // One lapse is being slow, so the seat is still this player's and their plaque says nothing.
        ILocator ownDen = idle.GetByTestId("den-mine");
        await Expect(ownDen.GetByTestId("den-bot-mark")).ToHaveCountAsync(0);

        // The second lapse. The cover lands at the end of the idle seat's turn, so the table is then on the other
        // player's turn, which it holds. The struck notice is transient and the cover is not, so this is a wait
        // on the standing one rather than on the same element changing hands in a single frame.
        await EndTurnWhenOnTurnAsync(other);
        Assert.That(await WaitForOwnTurnOnAsync(idle, MatchTimeout), Is.True, "the second own turn never arrived");
        await RunOutTheClockAsync(other);

        await Expect(notice).ToHaveAttributeAsync("data-stage", "covered", new() { Timeout = MatchTimeout });
        await Expect(notice).ToHaveAttributeAsync("data-on-turn", "false");
        await Expect(notice).ToContainTextAsync("A bot is playing your seat.");
        await Expect(ownDen.GetByTestId("den-bot-mark")).ToBeVisibleAsync();

        // The cover is not a disguise: the identity is kept, so the plaque carries this account's own name
        // beside the mark, rather than a bot-roster name.
        await Expect(ownDen.GetByTestId("den-name")).ToHaveTextAsync(idleName);

        // Asking for the seat back. Nothing has moved since the cover, so the board comes back still covered,
        // and says when the seat will be its owner's again. The reload goes to the app's root, which a CDN serves,
        // rather than to /match, which it does not, and Home turns to the board.
        Task<IRequest> reload = idle.WaitForRequestAsync(request => request.IsNavigationRequest, new() { Timeout = MatchTimeout });
        await idle.GetByTestId("reclaim-seat").ClickAsync();
        Assert.That(new Uri((await reload).Url).AbsolutePath, Is.EqualTo("/"));
        await Expect(idle.GetByTestId("match-board")).ToBeVisibleAsync(new() { Timeout = MatchTimeout });
        await Expect(notice).ToHaveAttributeAsync("data-stage", "reclaiming", new() { Timeout = MatchTimeout });
        await Expect(notice).ToContainTextAsync("The bot hands your seat over when the next turn starts.");
        await Expect(ownDen.GetByTestId("den-bot-mark")).ToBeVisibleAsync();

        // The other player ends the held turn, and the turn boundary hands the seat back before the bot is armed
        // for it: the next turn is the owner's own, to play.
        await EndTurnWhenOnTurnAsync(other);
        Assert.That(await WaitForOwnTurnOnAsync(idle, MatchTimeout), Is.True, "the seat's owner never got a turn back");
        await Expect(ownDen.GetByTestId("den-bot-mark")).ToHaveCountAsync(0);
        await Expect(notice).ToBeHiddenAsync();
        await Expect(ownDen.GetByTestId("den-name")).ToHaveTextAsync(idleName);

        TestContext.Out.WriteLine($"struck out on turn {turnAfter}; the seat was covered under {idleName} and handed back");
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
        (IPage idle, IPage other, string idleName) = await SeatAnIdlePlayerAndADeveloperAsync();

        await EndTurnIfOnTurnAsync(other);
        Assert.That(await WaitForOwnTurnOnAsync(idle, MatchTimeout), Is.True, "the first own turn never arrived");

        // One lapse, and nothing else.
        await RunOutTheClockAsync(other);
        ILocator notice = idle.GetByTestId("covered-seat-notice");
        await Expect(notice).ToHaveAttributeAsync("data-stage", "struck", new() { Timeout = MatchTimeout });

        // Now the flag, and then one press the server will refuse. The reload comes back on the same table
        // through the account's pointer, with the strike still against the seat.
        await ReloadBoardWithOnAsync(idle, "stale=1");
        await EndTurnWhenOnTurnAsync(other);
        Assert.That(await WaitForOwnTurnOnAsync(idle, MatchTimeout), Is.True, "the seat lost its turn across the reload");

        await EndTurnOn(idle).ClickAsync();

        // The press itself is refused, the turn having passed. Waited for by COUNT rather than by visibility,
        // because that refusal deliberately renders no words — the player is not told off for losing a race.
        await Expect(idle.Locator("[data-testid='refusal-notice'][data-reason='NotYourTurn']"))
            .ToHaveCountAsync(1, new() { Timeout = MatchTimeout });

        // The next own turn lapses too — and this is a FIRST strike, not a second. The struck notice returning
        // is what says so: at two the seat would be covered and the standing notice would have replaced it.
        await EndTurnWhenOnTurnAsync(other);
        Assert.That(await WaitForOwnTurnOnAsync(idle, MatchTimeout), Is.True, "the next own turn never arrived");
        await RunOutTheClockAsync(other);
        await Expect(notice).ToHaveAttributeAsync("data-stage", "struck", new() { Timeout = MatchTimeout });

        ILocator ownDen = idle.GetByTestId("den-mine");
        await Expect(ownDen.GetByTestId("den-bot-mark")).ToHaveCountAsync(0);
        await Expect(ownDen.GetByTestId("den-name")).ToHaveTextAsync(idleName);

        TestContext.Out.WriteLine("two lapses with one refused press between them, and the seat is still its owner's");
    }

    // ---------------------------------------------------------------- the two seats

    /// <summary>
    /// Two accounts at one Ranked table, past the mulligan: the idle one the cases are about, and a developer
    /// whose board runs the idle seat's clock out. The developer status is set before the queue, so the board
    /// draws the tool from its first frame.
    /// </summary>
    async Task<(IPage Idle, IPage Other, string IdleName)> SeatAnIdlePlayerAndADeveloperAsync()
    {
        IPage idle  = await NewPageAsync();
        IPage other = await NewPageAsync();

        const string idleName = "Dormouse";
        await PrepareNamedAccountAsync(idle, idleName);
        await PrepareNamedAccountAsync(other, "Kestrel");
        await MakeDeveloperAsync(other);

        await TapRankedAsync(idle);
        await TapRankedAsync(other);

        await Expect(idle.GetByTestId("match-board")).ToBeVisibleAsync(new() { Timeout = QueueTimeout });
        await Expect(other.GetByTestId("match-board")).ToBeVisibleAsync(new() { Timeout = QueueTimeout });

        await ResolveBothMulligansAsync(idle, other);
        return (idle, other, idleName);
    }

    /// <summary>
    /// Mark the account on this page as a developer through the admin API, the way <see cref="ProgressionTests"/>
    /// does, and reload Home so the client has the status.
    /// </summary>
    async Task MakeDeveloperAsync(IPage page)
    {
        string playerId = (await page.Locator(".session-menu p").TextContentAsync())!.Trim();
        string adminUrl = Environment.GetEnvironmentVariable("E2E_SERVER_ADMIN_URL") is { Length: > 0 } url ? url.TrimEnd('/') : "http://127.0.0.1:5550";

        using HttpClient admin = new HttpClient();
        (await admin.PostAsync($"{adminUrl}/api/players/{playerId}/developerStatus?newStatus=true", new StringContent(""))).EnsureSuccessStatusCode();

        await page.ReloadAsync();
        await Expect(page.GetByTestId("display-name")).ToBeVisibleAsync(new() { Timeout = BootTimeout });
    }

    /// <summary> Lapse the deadline in force from this developer's board, opening its developer tools if needed. </summary>
    async Task RunOutTheClockAsync(IPage page)
    {
        ILocator button = page.GetByTestId("debug-lapse-deadline");
        if (!await button.IsVisibleAsync())
            await page.GetByLabel("Developer tools", new() { Exact = true }).ClickAsync();

        await Expect(button).ToBeVisibleAsync();
        await button.ClickAsync();
    }

    /// <summary> End this seat's turn if the table is on it now; the table is waiting on one of the two seats. </summary>
    async Task EndTurnIfOnTurnAsync(IPage page)
    {
        await WaitUntilCaughtUpOnAsync(page);
        if (await EndTurnOn(page).IsEnabledAsync())
            await EndTurnOn(page).ClickAsync();
    }

    /// <summary> Wait for this seat's turn and end it at once. </summary>
    async Task EndTurnWhenOnTurnAsync(IPage page)
    {
        await Expect(EndTurnOn(page)).ToBeEnabledAsync(new() { Timeout = MatchTimeout });
        await EndTurnOn(page).ClickAsync();
    }

    /// <summary> The turn the board on this page is presenting. </summary>
    static async Task<int> TurnOnAsync(IPage page)
        => int.Parse(await TurnIndicatorOn(page).GetAttributeAsync("data-turn") ?? "0");
}
