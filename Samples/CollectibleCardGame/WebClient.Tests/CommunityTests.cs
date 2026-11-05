using Microsoft.Playwright;
using System.Text.RegularExpressions;

namespace WebClient.Tests;

/// <summary>Owns an isolated server: verifies the public snapshot against real queue, match and database state.</summary>
[TestFixture, NonParallelizable]
public class CommunityTests : MatchTestBase
{
    protected override bool LosesConnectionsOnPurpose => true;

    /// <summary>
    /// The server's community limits, off: the 5 s request spacing and the 5 s queue sample exist to bound what
    /// a real population of polling clients costs, and this server has two clients that pace themselves.
    /// </summary>
    static readonly string[] CommunityLimitsOff = { "Community:RequestInterval=00:00:00", "Community:SampleInterval=00:00:00" };

    /// <summary>
    /// Home's refresh, shortened from the shipped ten seconds. Every count below is read off the observer's
    /// panel, so this is the latency of each one of them.
    /// </summary>
    const string FastPoll = "poll=250";

    /// <summary>
    /// How long the solo ranked search waits before it seats a bot. The observer has to see the first search
    /// before it is cancelled, which with the limits off and a quarter-second poll takes well under a second;
    /// three seconds is margin for a loaded machine. The second search is waited out in full, so this is also
    /// what that search costs.
    /// </summary>
    const string FillWait = "Matchmaking:FillWait=00:00:03";

    [Test]
    public async Task ActivityAndLeaderboardFollowRealPlayAndSurviveRestart()
    {
        string databaseDirectory = Path.Combine(Path.GetTempPath(), $"stickypaws-community-{Guid.NewGuid():N}");
        Directory.CreateDirectory(databaseDirectory);
        await using GameServerProcess server = await GameServerProcess.StartAsync(
            CommunityLimitsOff.Append($"Database:SqliteDirectory={databaseDirectory}").Append(FillWait).ToArray());
        IPage observer = await NewPageAsync();
        await Task.WhenAll(
            PrepareNamedAccountAsync(observer, "Ladder observer", FastPoll),
            PrepareNamedAccountAsync(Page, "Ladder player", FastPoll));
        await Expect(observer.GetByTestId("players-queued")).ToHaveTextAsync("0", new() { Timeout = BootTimeout });
        await Expect(observer.GetByTestId("players-in-match")).ToHaveTextAsync("0");

        await TapRankedAsync(Page);
        await Expect(observer.GetByTestId("players-queued")).ToHaveTextAsync("1");
        await Page.GetByTestId("searching-cancel").ClickAsync();
        await Expect(observer.GetByTestId("players-queued")).ToHaveTextAsync("0");
        await TapRankedAsync(Page);
        await Expect(Page.GetByTestId("match-board")).ToBeVisibleAsync(new() { Timeout = MatchTimeout });
        await ResolveMulliganAsync();
        await Expect(observer.GetByTestId("players-in-match")).ToHaveTextAsync("1");
        await Expect(observer.GetByTestId("players-queued")).ToHaveTextAsync("0");

        // Leaving covers the seat, so the table has no human left and plays itself out to a real ranked result,
        // which is what puts the account on the ladder.
        await Page.GetByTestId("leave-match").ClickAsync();
        await Expect(Page.GetByTestId("queue-ranked")).ToBeEnabledAsync(new() { Timeout = GameTimeout });
        await Expect(Page.GetByTestId("leaderboard-own")).ToBeVisibleAsync();
        await Expect(observer.GetByTestId("global-leaderboard")).ToContainTextAsync("Ladder player");
        await Expect(observer.GetByTestId("players-in-match")).ToHaveTextAsync("0");
        string position = (await Page.GetByTestId("leaderboard-own").GetAttributeAsync("data-position"))!;

        // Last of the counts, because the online figure is the SDK's concurrent-player estimate, which each node
        // reports on a fixed ten-second stats tick: checked here, that tick has mostly gone by during the rest.
        await Expect(observer.GetByTestId("players-online")).ToHaveTextAsync(new Regex("^[2-9][0-9]*$"), new() { Timeout = 30000 });

        // Keep the ranked player offline after restart: the observer must read the persisted ladder.
        await Page.CloseAsync();
        await server.RestartAsync();
        await observer.ReloadAsync();
        await Expect(observer.GetByTestId("global-leaderboard")).ToContainTextAsync("Ladder player", new() { Timeout = BootTimeout });
        await Expect(observer.GetByTestId("leaderboard-row").Filter(new() { HasText = "Ladder player" }).Locator("td").First)
            .ToHaveTextAsync(position);
    }
}
