namespace WebClient.Tests;

/// <summary>
/// The base of the fixtures that run the client in offline mode (<c>?offline</c>), which never connects to a game
/// server. Their URLs also carry the harness's game server ports, so a client that ignored <c>?offline</c> would
/// connect to that server and pass every other check; this fails the test if the page connected to it.
/// </summary>
public abstract class OfflinePageTest : NinjaRunPageTest
{
    private int _gameServerConnections;

    [SetUp]
    public void CountGameServerConnections()
    {
        Page.WebSocket += (_, webSocket) =>
        {
            if (new Uri(webSocket.Url).Port.ToString() == E2EStack.WebSocketPort)
                Interlocked.Increment(ref _gameServerConnections);
        };
    }

    [TearDown]
    public void NoGameServerConnections() =>
        Assert.That(_gameServerConnections, Is.Zero, "The client connected to the game server in offline mode");
}
