using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;
using System.Net.Http;
using System.Text.Json;

namespace WebClient.Tests;

/// <summary>
/// E2E tests against a <b>live game server</b> for a table whose player stops playing: a move deadline that
/// expires on the match actor's timer. They need both the WebClient server and <c>metaplay dev server</c>. A player
/// who disconnects mid-game and comes back is tested in
/// <see cref="LiveServerTableTests.LiveTable_SurvivesAReload_AndReSeatsThePlayerAtTheSameTable"/>. The deadline
/// test calls the server's test-only force-expire endpoint and checks the number of timers it expired instead of
/// sleeping (<c>docs/testing.md</c>, "Forcing timers"). Every test joins the queue only through
/// <see cref="PlayAndBeSeatedAsync"/>, which holds <see cref="LiveServerLocks.QueueAsync"/> from the tap to the
/// seated assertion.
/// </summary>
[TestFixture]
public class LiveServerRobustnessTests : PlaywrightPageTest
{
    /// <summary>Tap Play, wait until the hand is dealt, and return the match id.</summary>
    private async Task<string> PlayAndBeDealtInAsync()
    {
        await PlayAndBeSeatedAsync();
        await Expect(Page.GetByTestId("own-hand-count")).ToHaveTextAsync("5", new() { Timeout = TurnTimeoutMs });

        return await Page.GetByTestId("match-id").InnerTextAsync();
    }

    /// <summary>
    /// Expire every pending deadline of the match <paramref name="matchId"/> and return how many were expired.
    /// Zero means the match had no pending deadline, so the test called this before its setup finished.
    /// </summary>
    private static async Task<int> ForceExpireAsync(string matchId)
    {
        using HttpClient http = new HttpClient();
        using HttpResponseMessage response = await http.PostAsync($"{ServerPublicUrl}/test/match/{matchId}/expire", content: null);
        response.EnsureSuccessStatusCode();

        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("expired").GetInt32();
    }

    [Test]
    public async Task LiveTable_AForcedDeadlineLapseAutoPlaysTheSeatAndTheGameCarriesOn()
    {
        string matchId = await PlayAndBeDealtInAsync();

        // Wait until it is this player's turn with a move deadline set. Before that, the pending timers are the
        // bots' think delays, and forcing them would expire those instead.
        string ownSeat = await Page.GetByTestId("own-seat").InnerTextAsync();
        await Expect(Page.GetByTestId("seat-on-turn")).ToHaveTextAsync(ownSeat, new() { Timeout = TurnTimeoutMs });
        await Expect(Page.GetByTestId("deadline-in-force")).ToHaveTextAsync("true", new() { Timeout = TurnTimeoutMs });

        int handBefore = int.Parse(await Page.GetByTestId("own-hand-count").InnerTextAsync());

        int expired = await ForceExpireAsync(matchId);
        Assert.That(expired, Is.GreaterThanOrEqualTo(1), "the table was waiting on nothing, so nothing was forced");

        // The test taps no card. The match actor's timer fired, a bot played a card for the seat, and the game
        // continued.
        await Expect(Page.GetByTestId("own-hand-count")).ToHaveTextAsync((handBefore - 1).ToString(), new() { Timeout = TurnTimeoutMs });
        await Expect(Page.GetByTestId("match-phase")).ToHaveTextAsync("Playing");
    }
}
