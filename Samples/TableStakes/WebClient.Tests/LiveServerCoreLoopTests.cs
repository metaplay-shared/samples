using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

namespace WebClient.Tests;

/// <summary>
/// The core loop end to end against a live game server: Home, matchmaking, a game, the results, back to Home,
/// and a second full game, with the Profile record checked before and after. The parts are tested separately in
/// <see cref="ShellPageTests"/>, <see cref="LiveServerMatchmakingTests"/> and <see cref="LiveServerTableTests"/>.
/// This loop catches failures between them: a result that never reaches the profile, a match reference that is
/// not cleared so the second PLAY is refused, and a game recorded twice. Every PLAY tap runs inside
/// <see cref="LiveServerLocks.QueueAsync"/> until the client is seated.
/// </summary>
[TestFixture]
public class LiveServerCoreLoopTests : PlaywrightPageTest
{
    /// <summary>
    /// Open Home and wait until its PLAY button is visible. Uses the tab rather than a page load, because a page
    /// load restarts the client and the session.
    /// </summary>
    private async Task OpenHomeAsync()
    {
        await Page.GetByTestId("nav-home").ClickAsync();
        await Expect(Page.GetByTestId("play")).ToBeVisibleAsync(new() { Timeout = BootTimeoutMs });
    }

    /// <summary>
    /// The viewer's row in the results overlay's standings. The profile records the result from the standings,
    /// so the test reads its expected numbers from there.
    /// </summary>
    private async Task<ILocator> OwnStandingRowAsync()
    {
        int ownSeat = int.Parse(await Page.GetByTestId("own-seat").InnerTextAsync());
        return Page.GetByTestId("standings").Locator($"[data-seat='{ownSeat}']");
    }

    /// <summary>How many tricks the viewer's seat won, and whether the seat is first in the standings.</summary>
    private async Task<(int Tricks, bool Won)> ReadOwnResultAsync()
    {
        ILocator row = await OwnStandingRowAsync();
        await Expect(row).ToBeVisibleAsync();

        string tricksText = await row.Locator(".ts-standings__tricks").InnerTextAsync();
        int    tricks     = int.Parse(tricksText.Split(' ')[0]);

        int ownSeat   = int.Parse(await Page.GetByTestId("own-seat").InnerTextAsync());
        string winner = await Page.GetByTestId("standing-0").GetAttributeAsync("data-seat") ?? "-1";

        return (tricks, winner == ownSeat.ToString());
    }

    /// <summary>
    /// The results overlay names the winner, shows every seat in final order, and states the rule that put the
    /// winner first. The only way out is back to the menu.
    /// </summary>
    private async Task AssertTheResultsNameTheWinnerAndTheRuleAsync()
    {
        await Expect(Page.GetByTestId("results-winner")).Not.ToBeEmptyAsync();

        for (int rank = 0; rank < 4; rank++)
            await Expect(Page.GetByTestId($"standing-{rank}")).ToBeVisibleAsync();

        // The first-place row states the rule that put it first. A played game always has a winner of at least
        // one trick, so the rule is either MoreTricks or MoreRecentTrick.
        string separation = await Page.GetByTestId("standing-0").GetAttributeAsync("data-separation") ?? string.Empty;
        Assert.That(separation, Is.AnyOf("MoreTricks", "MoreRecentTrick"),
            $"the winner's standing carried '{separation}', which is not a reason a played game can produce");

        // The reason sentence appears only for a tie broken by the more recent trick, which the trick counts do
        // not show. The client reads the reason from the standings instead of recomputing it, so the sentence
        // and the list agree (docs/player.md, "Results screen").
        await Expect(Page.GetByTestId("results-reason")).ToHaveCountAsync(separation == "MoreRecentTrick" ? 1 : 0);

        // There is no Play Again button.
        await Expect(Page.GetByTestId("results-leave")).ToBeVisibleAsync();
        await Expect(Page.GetByTestId("play-again")).ToHaveCountAsync(0);
    }

    [Test]
    public async Task TheWholeLoopRunsTwice_AndTheRecordMoves()
    {
        await Page.GotoAsync(ClientUrl("/"));
        await WaitForLiveSessionAsync();

        // ---- The record, before anything has been played ----
        await OpenProfileFromHomeAsync();
        (int Played, int Won, int Tricks) before = await ReadRecordAsync();
        Assert.That(before.Played, Is.Zero, "a fresh browser context started with games already on the record");

        // ---- First game ----
        await OpenHomeAsync();
        await TapPlayAndBeSeatedAsync(Page.GetByTestId("play"), "the loop's first game");

        // The second game must be at a different table. match-id and match-phase render in the same block,
        // so a phase of Playing guarantees that match-id is not empty. An empty value would make the later
        // Not.ToHaveTextAsync check pass trivially.
        string firstMatchId = await Page.GetByTestId("match-id").InnerTextAsync();

        await PlayWholeHandAsync();
        await AssertTheResultsNameTheWinnerAndTheRuleAsync();
        (int Tricks, bool Won) first = await ReadOwnResultAsync();

        // ---- Back into the shell, and play a second game from the hero ----
        // Leaving the results returns to Home. The table route shows neither the hero PLAY nor the navigation
        // bar, so seeing them shows that the client left the table. Matchmaking refuses a player whose model still
        // references a match, so a successful second PLAY also shows that the first result was delivered and the
        // reference cleared.
        await LeaveTheResultsAsync();
        await Expect(Page.GetByTestId("play-hero")).ToBeVisibleAsync(new() { Timeout = TurnTimeoutMs });
        Assert.That(RouteOf(Page.Url), Is.EqualTo("/"), "leaving the results went somewhere other than Home");

        await OpenHomeAsync();
        await TapPlayAndBeSeatedAsync(Page.GetByTestId("play"), "the loop's second game");

        // A different table. The match id changes once, when the second match arrives, and then stays fixed,
        // so the retrying assertion is stable.
        await Expect(Page.GetByTestId("match-id"))
            .Not.ToHaveTextAsync(firstMatchId, new() { Timeout = TurnTimeoutMs });

        // ---- Second game ----
        await PlayWholeHandAsync();
        (int Tricks, bool Won) second = await ReadOwnResultAsync();

        // ---- Leave, back to Profile, and the record has moved by exactly two games ----
        await LeaveTheResultsAsync();
        await OpenProfileFromHomeAsync();

        // The result reaches the player model shortly after the match ends, so wait for the count. The other
        // numbers are read after the count has settled.
        await Expect(Page.GetByTestId("record-played"))
            .ToHaveTextAsync((before.Played + 2).ToString(), new() { Timeout = TurnTimeoutMs });

        (int Played, int Won, int Tricks) after = await ReadRecordAsync();

        Assert.That(after.Played, Is.EqualTo(before.Played + 2),
            "two completed games did not put exactly two games on the record — a lost result, or one counted twice");
        Assert.That(after.Tricks, Is.EqualTo(before.Tricks + first.Tricks + second.Tricks),
            "the tricks on the record are not the tricks the two tables' standings gave this seat");
        Assert.That(after.Won, Is.EqualTo(before.Won + (first.Won ? 1 : 0) + (second.Won ? 1 : 0)),
            "a win was counted by a rule other than first place in the table's own standings");
    }
}
