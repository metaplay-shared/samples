using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;
using System.Text.RegularExpressions;

namespace WebClient.Tests;

/// <summary>
/// E2E tests for offline mode, selected with <c>?env=offline</c>: the session runs against the in-process
/// offline server with the built-in game config archive. These tests need only the WebClient server, not a
/// game server.
/// <para>
/// Offline state is stored in the browser's <c>localStorage</c>. Each test starts from a new player because
/// Playwright's <c>PageTest</c> gives each test a new browser context.
/// </para>
/// </summary>
[TestFixture]
public class OfflineModeTests : PlaywrightPageTest
{
    private static readonly string OfflineUrl = ClientUrl("/", "env=offline");

    /// <summary>
    /// The session starts against the in-process server, which seats the player at a table when the session
    /// starts, so opening the menu redirects to the table. A reload while seated on the live server follows the
    /// same rule.
    /// </summary>
    [Test]
    public async Task OfflineMode_StartsSession_WithNoGameServer()
    {
        await OpenTableAsync(OfflineUrl);
        Assert.That(RouteOf(Page.Url), Is.EqualTo("/table"), "the menu did not send the seated player to the table");

        // own-hand-count is the deal size. own-hand-index is -1 until the host delivers a hand, and a play index
        // after that, so a non-negative number shows the hand came from the host. Neither value changes during
        // the test: the URL sets no moveDeadlineMs, so the offline host sets no move deadline, and the test taps
        // no card.
        await Expect(Page.GetByTestId("own-hand-count")).ToHaveTextAsync("5");
        await Expect(Page.GetByTestId("own-hand-index")).ToHaveTextAsync(new Regex(@"^\d+$"));

        // The name is generated from word lists in the built-in config archive. A name matching the
        // adjective-noun-number pattern shows the offline client loaded that archive, instead of falling back to
        // "Guest 1234" (docs/player.md, "Generated names").
        string seat = await Page.GetByTestId("own-seat").InnerTextAsync();
        await Expect(Page.GetByTestId($"seat-{seat}-name")).ToHaveTextAsync(new Regex(@"^[A-Z][a-z]+[A-Z][a-z]+\d{2}$"));

        // The in-process server reported no connection problem.
        await Expect(Page.GetByTestId("connection-modal")).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("connection-pill")).ToHaveCountAsync(0);
    }

    [Test]
    public async Task OfflineMode_APinnedSeedDealsTheSameTableTwice()
    {
        // `matchSeed` makes a game reproducible, for example to record the same game twice. It must fix the whole
        // table: the deal, the opponents drawn from the bot roster, and their strengths.
        //
        // The option exists only in offline mode. On a server table the deal seed must be unguessable, because a
        // known seed reveals all four hands (docs/match.md, "The deal seed"). In offline mode the browser
        // already computes every seat's cards to play the bots.
        string pinned = ClientUrl("/table", "env=offline&matchSeed=4242");

        (List<string> firstHand, List<string> firstBots) = await ReadTable(pinned);

        // Bot names come from a reserved roster that player names may not use, drawn without replacement, so no
        // table seats two bots with the same name (docs/bots.md, "Names"). Backend/SharedCode.Tests tests that
        // the draw depends only on the seed. This test checks that the table was seated from the roster.
        Assert.That(firstBots, Is.Unique, "two seats at one table showed the same computer player");
        Assert.That(firstBots, Has.None.Matches<string>(name => name.StartsWith("Bot ")),
            "the bot names are still the numbered placeholders rather than the reserved roster");

        (List<string> againHand, List<string> againBots) = await ReadTable(pinned);

        Assert.That(againHand, Is.EqualTo(firstHand), "the same seed dealt a different hand");
        Assert.That(againBots, Is.EqualTo(firstBots), "the same seed drew different computer players");

        // Control: a different seed deals a different hand, so the matching hands above come from the seed and
        // not from some other determinism in the host.
        (List<string> otherHand, _) = await ReadTable(ClientUrl("/table", "env=offline&matchSeed=99"));
        Assert.That(otherHand, Is.Not.EqualTo(firstHand), "a different seed dealt the same hand");
    }

    /// <summary>
    /// Loads <paramref name="url"/> and returns the viewer's dealt hand and the names of the bots in seats 1 to 3.
    /// </summary>
    private async Task<(List<string> Hand, List<string> Bots)> ReadTable(string url)
    {
        // Each page load starts a new session, and the offline host deals a new table for it.
        await OpenTableAsync(url);
        await Expect(Page.GetByTestId("own-hand-count")).ToHaveTextAsync("5");

        List<string> hand  = new List<string>();
        ILocator     cards = Page.Locator(".ts-handcard");
        int          count = await cards.CountAsync();
        for (int ndx = 0; ndx < count; ndx++)
            hand.Add(await cards.Nth(ndx).GetAttributeAsync("data-card") ?? "");

        List<string> bots = new List<string>();
        for (int seat = 1; seat < 4; seat++)
        {
            await Expect(Page.GetByTestId($"seat-{seat}")).ToHaveAttributeAsync("data-occupancy", "Bot");
            bots.Add(await Page.GetByTestId($"seat-{seat}-name").InnerTextAsync());
        }

        return (hand, bots);
    }
}
