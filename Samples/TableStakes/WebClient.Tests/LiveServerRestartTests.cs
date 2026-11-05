namespace WebClient.Tests;

/// <summary>
/// E2E test of a server restart during a game. Tables are persisted, so a graceful restart, as in a deploy, must leave
/// the game where it was: the player comes back to the same table, with the same hand, and plays it to the end
/// (<c>docs/match.md</c>, "Restart and cold wake").
/// <para>
/// The fixture owns its game server (<see cref="GameServerProcess"/>), so the harness runs it in a phase of its own
/// with the shared server stopped. To run it by hand, start only the web client:
/// <code>
/// dotnet run --project WebClient/WebClient.csproj
/// dotnet test WebClient.Tests/WebClient.Tests.csproj --filter "FullyQualifiedName~LiveServerRestartTests"
/// </code>
/// </para>
/// </summary>
[TestFixture]
[NonParallelizable]
public class LiveServerRestartTests : PlaywrightPageTest
{
    static GameServerProcess? _server;

    [OneTimeSetUp]
    public static async Task StartServer() => _server = await GameServerProcess.StartAsync();

    [OneTimeTearDown]
    public static async Task StopServer()
    {
        if (_server != null)
            await _server.DisposeAsync();
        _server = null;
    }

    [Test]
    public async Task AGameInProgressSurvivesAServerRestart()
    {
        await PlayAndBeSeatedAsync();

        // Play one card, so the table has a move to keep, not only the deal.
        await Expect(Page.GetByTestId("own-hand-count")).ToHaveTextAsync("5", new() { Timeout = TurnTimeoutMs });
        await PlayFirstLegalCardAsync();
        await Expect(Page.GetByTestId("own-hand-count")).ToHaveTextAsync("4", new() { Timeout = TurnTimeoutMs });

        string ownSeat   = await Page.GetByTestId("own-seat").InnerTextAsync();
        string trumpSuit = await Page.GetByTestId("trump-suit").GetAttributeAsync("data-suit") ?? "";

        await _server!.RestartAsync();

        // Reload as a returning player does. The board on screen until then is from before the restart, so nothing
        // read from it would show that the restored table answers.
        await ReloadOnStackAsync(Page);

        await Expect(Page.GetByTestId("match-phase")).ToHaveTextAsync("Playing", new() { Timeout = BootTimeoutMs });
        await Expect(Page.GetByTestId("own-seat")).ToHaveTextAsync(ownSeat);
        await Expect(Page.GetByTestId("trump-suit")).ToHaveAttributeAsync("data-suit", trumpSuit);
        await Expect(Page.GetByTestId("seat-" + ownSeat)).ToHaveAttributeAsync("data-occupancy", "Human");

        await PlayWholeHandAsync(cardsInHand: 4);
        AssertTheTimelinesNeverDiverged();
    }
}
