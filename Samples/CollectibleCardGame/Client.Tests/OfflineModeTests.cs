using Microsoft.Playwright;

namespace Game.Client.Tests;

/// <summary>
/// E2E tests for offline mode, selected with <c>?env=offline</c>: the session runs against the in-process
/// offline server, against the built-in game config archive. Unlike <see cref="HomePageTests"/> these need
/// only the Client dev server running — deliberately no game server, which is the point of the mode.
/// <para>
/// Offline mode covers the player loop only. Matches are never hosted offline (<c>Docs/match.md</c>,
/// "Two layers, deliberately separated"), so nothing beyond this fixture will ever run against it.
/// </para>
/// <para>
/// Offline state persists in browser localStorage, so these rely on each test getting a fresh browser context
/// (which Playwright's <c>PageTest</c> provides) to start from a clean player.
/// </para>
/// </summary>
[Parallelizable(ParallelScope.Self)]
[TestFixture]
public class OfflineModeTests : OfflineTestBase
{
    /// <summary>
    /// The offline entry point, from the base rather than a const of its own — so this fixture honours
    /// <c>STICKYPAWS_WEB_BASE</c> like every other one and a second working copy can run it.
    /// </summary>
    static string OfflineUrl => Offline();

    [Test]
    public async Task OfflineMode_StartsSession_WithNoGameServer()
    {
        await Page.GotoAsync(OfflineUrl);

        // The header reports a started session the same way it does online. Exact match so it doesn't also
        // match the hidden Blazor "Browser Disconnected" reconnect banner.
        ILocator connected = Page.GetByText("Connected", new() { Exact = true });
        await Expect(connected).ToBeVisibleAsync(new() { Timeout = 30000 });
    }

    /// <summary>
    /// A disabled control must not animate. The Ranked button is `btn-hero` and
    /// `Disabled` before the queue existed, and the hero glow used to breathe around its grey body.
    /// </summary>
    [Test]
    public async Task OfflineMode_TheDisabledRankedButton_DoesNotPulse()
    {
        await GotoOfflineAsync("/", "display-name");

        ILocator ranked = Page.GetByTestId("queue-ranked");
        await Expect(ranked).ToBeDisabledAsync();

        string animations = await ranked.EvaluateAsync<string>("el => getComputedStyle(el).animationName");
        Assert.That(animations, Does.Not.Contain("mp-pulse-glow"), $"a disabled hero button is animating: {animations}");
    }

    [Test]
    public async Task OfflineMode_RunsThePlayerActionLoop()
    {
        await GotoOfflineAsync("/", "display-name");
        await OpenChangeNameAsync();

        // The offline server executes the action and ticks the model exactly as a real server would, so the
        // same round trip is observable with no backend running at all.
        await Page.GetByTestId("display-name-input").FillAsync("Whiskers");
        await Page.GetByTestId("save-name").ClickAsync();

        await Expect(Page.GetByTestId("display-name")).ToHaveTextAsync("Whiskers");
    }
}
