using Microsoft.Playwright;
using System.Text.RegularExpressions;

namespace Game.Client.Tests;

/// <summary>Owns an isolated server: verifies the public snapshot against real queue, match and database state.</summary>
[TestFixture, NonParallelizable]
public class CommunityTests : MatchTestBase
{
    [Test]
    public async Task ActivityAndLeaderboardFollowRealPlayAndSurviveRestart()
    {
        string databaseDirectory = Path.Combine(Path.GetTempPath(), $"stickypaws-community-{Guid.NewGuid():N}");
        Directory.CreateDirectory(databaseDirectory);
        await using GameServerProcess server = await GameServerProcess.StartAsync(
            $"Database:SqliteDirectory={databaseDirectory}",
            "Matchmaking:FillWait=00:00:30");
        await using IBrowserContext observerContext = await Browser.NewContextAsync();
        IPage observer = await observerContext.NewPageAsync();
        await PrepareNamedAccountAsync(observer, "Ladder observer");
        await PrepareNamedAccountAsync(Page, "Ladder player");
        await Expect(observer.GetByTestId("players-online")).ToHaveTextAsync(new Regex("^[2-9][0-9]*$"), new() { Timeout = 90000 });
        await Expect(observer.GetByTestId("players-queued")).ToHaveTextAsync("0");
        await Expect(observer.GetByTestId("players-in-match")).ToHaveTextAsync("0");

        await TapRankedAsync(Page);
        await Expect(observer.GetByTestId("players-queued")).ToHaveTextAsync("1", new() { Timeout = 20000 });
        await Page.GetByTestId("searching-cancel").ClickAsync();
        await Expect(observer.GetByTestId("players-queued")).ToHaveTextAsync("0", new() { Timeout = 20000 });
        await TapRankedAsync(Page);
        await Expect(Page.GetByTestId("match-board")).ToBeVisibleAsync(new() { Timeout = 60000 });
        await ResolveMulliganAsync();
        await Expect(observer.GetByTestId("players-in-match")).ToHaveTextAsync("1", new() { Timeout = 25000 });
        await Expect(observer.GetByTestId("players-queued")).ToHaveTextAsync("0");

        for (int turn = 0; turn < 60 && !await IsGameOverAsync(); turn++)
        {
            if (!await WaitForOwnTurnAsync(30000) || await IsGameOverAsync()) break;
            await PlayOneTurnAsync();
        }
        await Expect(MatchResult).ToBeVisibleAsync(new() { Timeout = GameTimeout });
        await MatchResult.GetByTestId("leave-match").ClickAsync();
        await Expect(Page.GetByTestId("leaderboard-own")).ToBeVisibleAsync(new() { Timeout = 30000 });
        await Expect(observer.GetByTestId("global-leaderboard")).ToContainTextAsync("Ladder player", new() { Timeout = 30000 });
        await Expect(observer.GetByTestId("players-in-match")).ToHaveTextAsync("0", new() { Timeout = 20000 });
        string position = (await Page.GetByTestId("leaderboard-own").GetAttributeAsync("data-position"))!;

        // Keep the ranked player offline after restart: the observer must read the persisted ladder.
        await Page.CloseAsync();
        await server.RestartAsync();
        await observer.ReloadAsync();
        await Expect(observer.GetByTestId("global-leaderboard")).ToContainTextAsync("Ladder player", new() { Timeout = 60000 });
        await Expect(observer.GetByTestId("leaderboard-row").Filter(new() { HasText = "Ladder player" }).Locator("td").First)
            .ToHaveTextAsync(position);
    }
}
