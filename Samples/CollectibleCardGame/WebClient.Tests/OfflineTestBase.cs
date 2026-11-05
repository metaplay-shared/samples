namespace WebClient.Tests;

/// <summary>
/// Entry points for the offline-mode suites. Offline mode runs the whole player loop against the in-process
/// offline server and the built-in config archive, so none of these need a game server — which is what makes
/// it the right harness for the out-of-match loop.
/// <para>
/// Offline state persists in browser storage. Each test gets a fresh browser context from <c>PageTest</c>,
/// which is what makes "a new player" the starting state of every test here.
/// </para>
/// </summary>
public abstract class OfflineTestBase : MetaScreenTestBase
{
    protected override string? StartQuery => "offline";

    /// <summary>
    /// A page in offline mode. The client reads the query once, at startup, so a test that reloads after in-app
    /// navigation does it with <see cref="MetaScreenTestBase.ReloadOnStackAsync"/>.
    /// </summary>
    protected static string Offline(string path = "/") => ClientUrl(path, "offline");

    [SetUp]
    public void FailOnGameServerConnections() => ExpectNoGameServerConnections();

    /// <summary> Go to a screen and wait until the player model is there to draw. </summary>
    protected async Task GotoOfflineAsync(string path, string readyTestId)
    {
        await Page.GotoAsync(Offline(path));
        await Expect(Page.GetByTestId(readyTestId).First).ToBeVisibleAsync(new() { Timeout = BootTimeout });
    }
}
