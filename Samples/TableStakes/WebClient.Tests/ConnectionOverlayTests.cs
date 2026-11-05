using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace WebClient.Tests;

/// <summary>
/// E2E tests for the connection overlay (WebClientBase's ConnectionOverlay, mounted once in App.razor) over this
/// game's layouts, and the connection loop behind it. Requires a running game server and web client: a hand-started
/// pair, or the one run_e2e.py starts.
/// </summary>
[TestFixture]
public class ConnectionOverlayTests : PlaywrightPageTest
{
    private static readonly string HomeUrl = ClientUrl();

    private static ILocator Dialog(IPage page, string name) => page.GetByRole(AriaRole.Alertdialog, new() { Name = name });

    /// <summary>A local port that nothing listens on.</summary>
    private static int UnusedPort()
    {
        TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    /// <summary>Open Home on <paramref name="page"/> and wait until its session is live.</summary>
    private async Task OpenHomeAsync(IPage page)
    {
        await page.GotoAsync(HomeUrl);
        await Expect(page.GetByTestId("session")).ToHaveAttributeAsync("data-state", "live", new() { Timeout = BootTimeoutMs });
    }

    /// <summary>
    /// A second tab in the same browser shares the first one's credentials, so its session takes the first one's.
    /// The first tab must not reconnect on its own, or the two would take the session from each other for as long as
    /// both are open; it offers the player the choice instead.
    /// </summary>
    [Test]
    public async Task SecondTab_SupersedesTheFirst_WhichWaitsForThePlayerToPlayHere()
    {
        int firstTabConnections = 0;
        Page.WebSocket += (_, webSocket) =>
        {
            if (new Uri(webSocket.Url).Port.ToString() == E2EStack.WebSocketPort)
                Interlocked.Increment(ref firstTabConnections);
        };
        await OpenHomeAsync(Page);

        // Same browser context, so the same stored credentials: the same player.
        IPage second = Watch(await Context.NewPageAsync());
        await OpenHomeAsync(second);

        // The first tab says the game is playing elsewhere, with its one choice focused.
        ILocator firstDialog = Dialog(Page, "Playing elsewhere");
        await Expect(firstDialog).ToBeVisibleAsync(new() { Timeout = 15000 });
        await Expect(firstDialog).ToHaveAccessibleDescriptionAsync(new Regex("playing somewhere else"));
        ILocator playHere = firstDialog.GetByRole(AriaRole.Button, new() { Name = "Play here" });
        await Page.BringToFrontAsync();
        await Expect(playHere).ToBeFocusedAsync();

        // Had the first tab reconnected on its own, it would have opened another connection to the game server, and
        // the second would have lost its session in turn.
        await Page.WaitForTimeoutAsync(5000);
        Assert.That(firstTabConnections, Is.EqualTo(1), "The first tab reconnected on its own");
        await Expect(firstDialog).ToBeVisibleAsync();
        await Expect(second.GetByRole(AriaRole.Alertdialog)).ToHaveCountAsync(0);

        // Playing here takes the session back, and now the second tab says the game is playing elsewhere.
        await playHere.ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Alertdialog)).ToHaveCountAsync(0, new() { Timeout = 20000 });
        await Expect(Dialog(second, "Playing elsewhere")).ToBeVisibleAsync(new() { Timeout = 15000 });
    }

    /// <summary>
    /// A first connection that fails shows a dialog the player can retry from, over the screen's fixture data. Before
    /// the first session there is no game to show a "Reconnecting…" dialog over, so the retries show none.
    /// </summary>
    [Test]
    public async Task FirstConnectionRefused_ShowsADialog()
    {
        // No game server on that port, so the connection is refused. The asset CDN is the stack's own.
        await Page.GotoAsync($"{ClientOrigin}/?wsPort={UnusedPort()}&cdnPort={E2EStack.CdnPort}");
        await Expect(Page.GetByTestId("primary-nav")).ToBeVisibleAsync(new() { Timeout = BootTimeoutMs });

        ILocator dialog = Dialog(Page, "Disconnected");
        ILocator reconnecting = Dialog(Page, "Reconnecting…");
        System.Diagnostics.Stopwatch elapsed = System.Diagnostics.Stopwatch.StartNew();
        while (!await dialog.IsVisibleAsync())
        {
            Assert.That(await reconnecting.CountAsync(), Is.EqualTo(0), "a reconnecting dialog showed before the first session");
            Assert.That(elapsed.ElapsedMilliseconds, Is.LessThan(60000), "the client did not stop retrying and ask the player");
            await Task.Delay(100);
        }

        await Expect(dialog.GetByRole(AriaRole.Button, new() { Name = "Reconnect" })).ToBeVisibleAsync();
        await Expect(Page.GetByTestId("session")).ToHaveAttributeAsync("data-state", "fixture");
    }
}
