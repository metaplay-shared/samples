using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace WebClient.Tests;

/// <summary>
/// The connection overlay (WebClientBase's ConnectionOverlay, mounted once in App.razor) and the connection loop
/// behind it, over the game's own screens. Requires a running game server and web client: a hand-started pair, or
/// the one run_e2e.py starts.
/// </summary>
[TestFixture]
public class ConnectionOverlayTests : MetaScreenTestBase
{
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

    /// <summary>Rename the player on <paramref name="page"/>'s Home, an action whose result the page shows.</summary>
    private async Task RenameAsync(IPage page, string name)
    {
        await page.GetByTestId("display-name-edit").ClickAsync();
        await page.GetByTestId("display-name-input").FillAsync(name);
        await page.GetByTestId("save-name").ClickAsync();
        await Expect(page.GetByTestId("display-name")).ToHaveTextAsync(name);
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
        await Page.GotoAsync(ClientUrl());
        await Expect(Page.GetByTestId("display-name")).ToBeVisibleAsync(new() { Timeout = BootTimeout });

        // Same browser context, so the same stored credentials: the same player.
        IPage second = Watch(await Context.NewPageAsync());
        await second.GotoAsync(ClientUrl());
        await Expect(second.GetByTestId("display-name")).ToBeVisibleAsync(new() { Timeout = BootTimeout });

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

        // The second tab is the live one: its actions still reach the model.
        await second.BringToFrontAsync();
        await RenameAsync(second, "Second tab");

        // Playing here takes the session back, and now the second tab says the game is playing elsewhere.
        await Page.BringToFrontAsync();
        await playHere.ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Alertdialog)).ToHaveCountAsync(0, new() { Timeout = 20000 });
        await Expect(Dialog(second, "Playing elsewhere")).ToBeVisibleAsync(new() { Timeout = 15000 });

        // The first tab is live again, on the player the second tab renamed.
        await Expect(Page.GetByTestId("display-name")).ToHaveTextAsync("Second tab", new() { Timeout = BootTimeout });
        await RenameAsync(Page, "First tab");
    }

    /// <summary>
    /// A first connection that fails shows the startup splash while it tries, then a dialog the player can retry from.
    /// Before the first session there is no game to show a "Reconnecting…" dialog over, so the retries stay behind the
    /// splash.
    /// </summary>
    [Test]
    public async Task FirstConnectionRefused_ShowsTheSplashThenADialog()
    {
        // No game server on that port, so the connection is refused. The asset CDN is the stack's own.
        await Page.GotoAsync($"{ClientOrigin}/?wsPort={UnusedPort()}&cdnPort={E2EStack.CdnPort}");
        ILocator splash = Page.GetByTestId("startup-splash");
        await Expect(splash).ToBeVisibleAsync(new() { Timeout = 20000 });

        ILocator dialog = Dialog(Page, "Disconnected");
        ILocator reconnecting = Dialog(Page, "Reconnecting…");
        System.Diagnostics.Stopwatch elapsed = System.Diagnostics.Stopwatch.StartNew();
        while (!await dialog.IsVisibleAsync())
        {
            Assert.That(await reconnecting.CountAsync(), Is.EqualTo(0), "a reconnecting dialog showed before the first session");
            Assert.That(await splash.IsVisibleAsync(), Is.True, "the startup splash went away before the first session");
            Assert.That(elapsed.ElapsedMilliseconds, Is.LessThan(60000), "the client did not stop retrying and ask the player");
            await Task.Delay(100);
        }

        await Expect(dialog.GetByRole(AriaRole.Button, new() { Name = "Reconnect" })).ToBeVisibleAsync();
    }

    /// <summary>
    /// Reset starts a new player and reloads the client at its start page, with the query the page was loaded with, so
    /// a client the harness pointed at its own game server stays on it.
    /// </summary>
    [Test]
    public async Task Reset_StartsANewPlayer_OnTheSameServer()
    {
        await Page.GotoAsync(ClientUrl());
        await Expect(Page.GetByTestId("display-name")).ToBeVisibleAsync(new() { Timeout = BootTimeout });
        ILocator playerId = Page.Locator(".session-menu p");
        string firstPlayer = (await playerId.TextContentAsync())!.Trim();

        // An in-app link leaves another route in the address bar, without the query.
        await Page.GetByTestId("nav-collection").ClickAsync();
        await Expect(Page.GetByTestId("collection-grid")).ToBeVisibleAsync(new() { Timeout = BootTimeout });
        Assert.That(new Uri(Page.Url).Query, Is.Empty);

        await Page.Locator(".session-menu summary").ClickAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Reset" }).ClickAsync();

        await Expect(Page.GetByTestId("display-name")).ToBeVisibleAsync(new() { Timeout = BootTimeout });
        await Expect(playerId).Not.ToHaveTextAsync(firstPlayer, new() { Timeout = 30000 });
        await Expect(playerId).ToHaveTextAsync(new Regex("^Player:"));
        Assert.That(Page.Url, Is.EqualTo(ClientUrl()));
    }
}
