using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

namespace WebClient.Tests;

/// <summary>
/// The seasonal tournament, through the real client against a live game server (<c>docs/seasonal-tournament.md</c>).
/// <c>TournamentTests</c> covers the rules, bots and tiebreaks without a browser. These tests cover what needs a
/// real league manager, group and client: a player who has just entered sees a <b>full group, not a single row</b>,
/// and the standings are <b>unchanged after a reload</b>, which shows that bot results are not rerolled.
/// Every test that joins holds <see cref="LiveServerLocks.LeagueAsync"/> from the join through its last read of the
/// standings, because all tests share one league group. <see cref="ClaimingAMilestone_MovesTheWalletOnTheClient"/>
/// also takes <see cref="LiveServerLocks.QueueAsync"/>, nested inside League.
/// </summary>
[TestFixture]
public class LiveServerTournamentTests : PlaywrightPageTest
{
    /// <summary>
    /// Like <see cref="OpenCompeteAsync"/>, but opens Compete through the shell navigation instead of a page load.
    /// Use it in a test that already has a live session, so the test does not pay for another WASM boot and
    /// session handshake.
    /// </summary>
    private async Task OpenCompeteOnStackAsync()
    {
        await Page.GetByTestId("nav-compete").ClickAsync();
        await Expect(Page.GetByTestId("tournament-summary")).ToContainTextAsync("Open to enter", new() { Timeout = BootTimeoutMs });
    }

    /// <summary>
    /// Enter the season and wait until the group has attached.
    /// <para>
    /// The join is retried because the league manager opens its first season on a timer that starts after the
    /// server reports ready. On a freshly started server, the first join can be refused because no season is
    /// running yet. Joining is idempotent: the league answers a player who is already in it with their current
    /// group, so retrying is safe.
    /// </para>
    /// </summary>
    private async Task JoinAsync()
    {
        ILocator rows = Page.Locator("[data-testid=standings] tbody tr");

        // Each attempt waits up to AttemptMs for the full standings and returns as soon as they appear. On a
        // running server the first attempt succeeds. The number of attempts and the wait per attempt are sized to
        // outlast the wait for the first season on a freshly started server.
        const int AttemptMs = 5000;
        for (int attempt = 0; attempt < 12; attempt++)
        {
            ILocator join = Page.GetByTestId("tournament-join");
            if (await join.CountAsync() > 0)
                await join.ClickAsync();

            try
            {
                await Expect(rows).ToHaveCountAsync(20, new() { Timeout = AttemptMs });
                return;
            }
            catch (PlaywrightException)
            {
                // The join was refused or has not attached yet. Tap Join again if it is still shown.
            }
        }

        await Expect(rows).ToHaveCountAsync(20, new() { Timeout = BootTimeoutMs });
    }

    /// <summary>The inner text of every standings row, so two snapshots of the standings can be compared.</summary>
    private async Task<IReadOnlyList<string>> BoardAsync() =>
        await Page.Locator("[data-testid=standings] tbody tr").AllInnerTextsAsync();

    /// <summary>
    /// A player who has just entered sees a full group, not a list with only their own row. Bot rows are labelled
    /// as bots, as they are at the card table.
    /// </summary>
    [Test]
    public async Task Joining_OpensOntoAFullGroup()
    {
        await OpenCompeteAsync();

        await using (await LiveServerLocks.LeagueAsync("joining opens onto a full group"))
        {
            await JoinAsync();

            await Expect(Page.GetByTestId("standings-self")).ToHaveCountAsync(1);

            // Bots fill the rows that no human holds. Every run of the suite adds a human to the same group, so
            // the test checks only that at least one bot row exists, not a specific number.
            Assert.That(await Page.GetByTestId("standings-bot").CountAsync(), Is.GreaterThan(0));

            // The first row shows the first-place prize from the shipped config. The test checks the exact
            // amount, so a row mapped to the wrong prize band fails.
            ILocator firstRow = Page.Locator("[data-testid=standings] tbody tr").First;
            await Expect(firstRow.Locator("[data-testid=standings-prize]")).ToContainTextAsync("wins", new() { Timeout = BootTimeoutMs });
            await Expect(firstRow.GetByTestId("standings-prize")).ToContainTextAsync("1,500");

            // The prizes are shown on the standings rows, so there is no separate placement block.
            await Expect(Page.GetByTestId("tournament-placement")).ToHaveCountAsync(0);
        }
    }

    /// <summary>
    /// A player buys a frame, and their own standings row shows it. Other players see a player's frame on the
    /// standings rows (<c>docs/cosmetics.md</c>).
    /// <para>
    /// It is in this fixture, not with the other cosmetics tests, because joining a season needs the League lock.
    /// <c>CosmeticsPolicyTests.AStandingsRowWearsTheStyleTokenOfWhatItsSeatOwns</c> tests the row builder without a
    /// server. Fixture rows can already carry a style token, so only a live test catches live rows that lose the
    /// frame between the server state and the row.
    /// </para>
    /// </summary>
    [Test]
    public async Task AFrameBoughtBeforeJoining_IsWornOnTheStandingsRow()
    {
        // Read the player's name from Home's identity row, for the Admin API lookup below. This is the test's only
        // page load. The later screens are opened through in-app navigation, which keeps the session.
        await Page.GotoAsync(ClientUrl());
        string playerName = await ReadPlayerNameAsync();

        // Buy before joining, so the player data the division receives on join already includes the frame.
        // Open Cosmetics from Home's identity row through Profile.
        await Page.GetByTestId("home-profile-action").ClickAsync();
        await Page.GetByTestId("open-cosmetics").ClickAsync();
        await WaitForLiveSessionAsync();

        await Page.GetByTestId("cosmetic-frame.silver").ClickAsync();
        await Page.GetByTestId("cosmetic-frame.silver").GetByTestId("cosmetic-buy").ClickAsync();
        await Page.GetByTestId("confirm-accept").ClickAsync();
        // A fresh player has no other equipped frame, so the only Equipped chip is on the bought tile.
        await Expect(Page.GetByTestId("cosmetic-equipped")).ToBeVisibleAsync();

        // The Equipped chip shows the client's prediction, which renders before the action reaches the server.
        // The server builds the division entry from its own copy of the player, so the purchase must be committed
        // before the join. The purchase event in the server's event log confirms the commit.
        await WaitForServerToHaveEventAsync(playerName, "PlayerEventCosmeticPurchased");

        await using (await LiveServerLocks.LeagueAsync("a frame bought before joining is worn on the standings row"))
        {
            await OpenCompeteOnStackAsync();
            await JoinAsync();

            // Check the standings row's avatar, not the HUD's. The HUD draws from the player's own model and
            // would show the frame even if the standings row did not.
            await Expect(Page.GetByTestId("standings-self").GetByTestId("avatar"))
                .ToHaveClassAsync(new Regex("m-frame--frame-silver"));

            // A bot owns no cosmetics, so its row shows no frame. Bot rows are found by the standings-bot marker,
            // not by position, because standings-row matches every row except the viewer's, including other
            // humans from earlier runs.
            ILocator botRows = Page.GetByTestId("standings-row")
                .Filter(new LocatorFilterOptions { Has = Page.GetByTestId("standings-bot") });

            // Fail if there is no bot row, instead of passing the check below with nothing to check.
            Assert.That(await botRows.CountAsync(), Is.GreaterThan(0), "the group has no computer player's row to read");
            await Expect(botRows.First.GetByTestId("avatar")).Not.ToHaveClassAsync(new Regex("m-frame--"));
        }
    }

    /// <summary>
    /// The standings are unchanged after a reload. The reload builds a new client and fetches the group again, and
    /// the bot rows must come back identical.
    /// </summary>
    [Test]
    public async Task Reloading_ShowsTheSameBoard()
    {
        await OpenCompeteAsync();

        // The lock is held across the reload, because a player joining the group between the two snapshots would
        // change the standings.
        await using (await LiveServerLocks.LeagueAsync("reloading shows the same board"))
        {
            await JoinAsync();

            IReadOnlyList<string> before = await BoardAsync();

            await ReloadOnStackAsync(Page);

            // Wait for the session marker, not the HUD name, because the fixture also supplies a HUD name.
            await WaitForLiveSessionAsync();
            await Expect(Page.Locator("[data-testid=standings] tbody tr")).ToHaveCountAsync(20, new() { Timeout = BootTimeoutMs });

            IReadOnlyList<string> after = await BoardAsync();

            Assert.That(after, Is.EqualTo(before));
        }
    }

    /// <summary>
    /// A full group fits its card at every tested width, with nothing clipped and no horizontal scroll.
    /// <para>
    /// The test uses a live group because the fixture has fewer rows and shorter names than a real group. It checks
    /// each element's overflow, because the card clips overflowing content and the page then shows no overflow.
    /// </para>
    /// </summary>
    [Test]
    public async Task TheGroupFitsItsCard()
    {
        // Join once and check every width against the same group. The narrowest width is set before the page
        // opens, so the standings are first drawn at that width instead of only resized to it.
        await Page.SetViewportSizeAsync(360, 780);
        await OpenCompeteAsync();

        await using (await LiveServerLocks.LeagueAsync("the group fits its card"))
        {
            await JoinAsync();

            foreach (int width in new[] { 360, 390, 600, 1024 })
            {
                await Page.SetViewportSizeAsync(width, 780);

                int pageOverflow = await Page.EvaluateAsync<int>(
                    "() => document.documentElement.scrollWidth - document.documentElement.clientWidth");

                // Find elements whose content is wider than the element. A card with hidden overflow does not
                // widen the page, so the page-level check above misses it.
                string clipped = await Page.EvaluateAsync<string>(@"() => {
                    const out = [];
                    for (const el of document.querySelectorAll('main .m-card, main .m-btn, main .m-rule, main .m-standings')) {
                        if (el.scrollWidth - el.clientWidth > 1)
                            out.push((el.dataset.testid || el.className) + ' ' + el.scrollWidth + '/' + el.clientWidth);
                    }
                    return out.join(', ');
                }");

                Assert.That(pageOverflow, Is.LessThanOrEqualTo(0), $"{width}px: the page scrolls sideways");
                Assert.That(clipped, Is.Empty, $"{width}px: content clipped by its own box: {clipped}");
            }
        }
    }

    /// <summary>
    /// Play a tournament match, claim the first milestone, and check that the client's coin balance increases.
    /// The claim changes the checksummed wallet, so it must be a synchronized server action. Unit tests of the action
    /// cannot see which path delivers it, so this test checks the balance <b>on the client</b>, which a wrong path
    /// would leave unchanged. The same match also checks through the Admin API that the player's group counts it,
    /// because the board shows points and a lost match adds none.
    /// </summary>
    [Test]
    public async Task ClaimingAMilestone_MovesTheWalletOnTheClient()
    {
        await Page.GotoAsync(ClientUrl());
        string playerName = await ReadPlayerNameAsync();
        await OpenCompeteOnStackAsync();

        // Take the League lock first and the Queue lock inside it, the order every caller of both locks follows
        // (LiveServerLocks).
        await using (await LiveServerLocks.LeagueAsync("claiming a milestone moves the wallet on the client"))
        {
            await JoinAsync();

            int before = await ReadBalanceAsync("coins");

            // One match unlocks the first milestone. Compete's Play button opens the searching dialog over the
            // standings, and the table replaces the page when it arrives.
            await TapPlayAndBeSeatedAsync(Page.GetByTestId("compete-play"), "claiming a milestone's match");
            await PlayWholeHandAsync();
            await WaitForGroupToCountAsync(playerName, scoredMatches: 1);

            await Page.GotoAsync(CompeteUrl);
            ILocator claim = Page.GetByTestId("tournament-milestone-claim").First;
            await Expect(claim).ToBeVisibleAsync(new() { Timeout = BootTimeoutMs });
            await claim.ClickAsync();

            await Expect(Page.GetByTestId("tournament-milestone-claimed").First).ToBeVisibleAsync(new() { Timeout = BootTimeoutMs });
            // The claimed state is shown as the text "Claimed", not only as an icon.
            await Expect(Page.GetByTestId("tournament-milestone-claimed").First).ToHaveTextAsync("Claimed", new() { Timeout = BootTimeoutMs });

            int after = await ReadBalanceAsync("coins");
            Assert.That(after, Is.EqualTo(before + 250), "the first milestone pays 250 coins, and the client's own wallet must show it");
        }
    }

    /// <summary>
    /// Wait until the player's group holds <paramref name="scoredMatches"/> counted matches for the player, as the
    /// Admin API reads the group. The player actor sends the run's totals to the group after each counted match.
    /// </summary>
    private static async Task WaitForGroupToCountAsync(string playerName, int scoredMatches)
    {
        Stopwatch waited = Stopwatch.StartNew();

        string? playerId = await WaitForLivePlayerIdAsync(playerName, BootTimeoutMs);
        if (playerId == null)
            Assert.Fail($"the Admin API found no player named {playerName} within {BootTimeoutMs} ms");

        int    remainingMs = Math.Max(0, BootTimeoutMs - (int)waited.ElapsedMilliseconds);
        string seen        = await PollAsync(() => GroupCountAsync(playerId!), count => count == scoredMatches.ToString(), remainingMs);

        if (seen != scoredMatches.ToString())
            Assert.Fail($"the group did not count {scoredMatches} matches for {playerName}. Last seen: {seen}");
    }

    /// <summary>
    /// The scored matches the player's group holds for the player, or a description of why there is no count yet.
    /// </summary>
    private static async Task<string> GroupCountAsync(string playerId)
    {
        // The game has one league, so the participant endpoint answers with one entry.
        using JsonDocument leagues = JsonDocument.Parse(await AdminHttp.GetStringAsync($"{ServerAdminUrl}/api/leagues/participant/{playerId}"));
        string? divisionId = leagues.RootElement[0].TryGetProperty("divisionId", out JsonElement division) ? division.GetString() : null;
        if (string.IsNullOrEmpty(divisionId))
            return "the player has no group";

        using JsonDocument group = JsonDocument.Parse(await AdminHttp.GetStringAsync($"{ServerAdminUrl}/api/divisions/{divisionId}"));
        foreach (JsonProperty participant in group.RootElement.GetProperty("model").GetProperty("participants").EnumerateObject())
        {
            if (participant.Value.GetProperty("participantId").GetString() == playerId)
                return participant.Value.GetProperty("playerContribution").GetProperty("scoredMatches").GetInt32().ToString();
        }

        return $"the player is not a participant of {divisionId}";
    }
}
