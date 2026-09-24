namespace Game.Client.Tests;

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
    /// <summary>
    /// The environment override is read from the query string at startup, and in-app links carry it along —
    /// so a test may navigate and reload freely once it has entered through one of these.
    /// </summary>
    protected static string Offline(string path = "/") => $"{BaseUrl}{path}?env=offline";

    /// <summary> Go to a screen and wait until the player model is there to draw. </summary>
    protected async Task GotoOfflineAsync(string path, string readyTestId)
    {
        await Page.GotoAsync(Offline(path));
        await Expect(Page.GetByTestId(readyTestId).First).ToBeVisibleAsync(new() { Timeout = BootTimeout });
    }
}
