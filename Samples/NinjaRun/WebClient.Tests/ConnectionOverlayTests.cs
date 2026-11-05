using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace WebClient.Tests;

/// <summary>
/// E2E tests for the connection overlay (WebClientBase's ConnectionOverlay, mounted once in App.razor), the
/// connection loop behind it, and what a run does while its session is lost. Requires a running game server and
/// web client: a hand-started pair, or the one run_e2e.py starts.
/// </summary>
[TestFixture]
public class ConnectionOverlayTests : NinjaRunPageTest
{
    private static readonly string BaseUrl = ClientUrl();

    private static ILocator StartRunButton(IPage page) => page.GetByRole(AriaRole.Button, new() { Name = "Start run" });

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
        await Page.GotoAsync(BaseUrl);
        await Expect(StartRunButton(Page)).ToBeEnabledAsync(new() { Timeout = 20000 });

        // Same browser context, so the same stored credentials: the same player.
        IPage second = Watch(await Context.NewPageAsync());
        await second.GotoAsync(BaseUrl);
        await Expect(StartRunButton(second)).ToBeEnabledAsync(new() { Timeout = 20000 });

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

        // The second tab is the live one: the server answers its request for a run with a ticket.
        await StartRunButton(second).ClickAsync();
        await Expect(second.GetByTestId("time-trial-canvas")).ToBeVisibleAsync(new() { Timeout = 15000 });

        // Playing here takes the session back, and now the second tab says the game is playing elsewhere.
        await playHere.ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Alertdialog)).ToHaveCountAsync(0, new() { Timeout = 20000 });
        await Expect(Dialog(second, "Playing elsewhere")).ToBeVisibleAsync(new() { Timeout = 15000 });

        // The first tab is live again.
        await StartRunButton(Page).ClickAsync();
        await Expect(Page.GetByTestId("time-trial-canvas")).ToBeVisibleAsync(new() { Timeout = 15000 });
    }

    /// <summary>
    /// A run whose session is lost stops where it is while the dialog is up: its frames, its clock and its input.
    /// Taking the session back starts a new one, which the run's ticket was not issued in, so the run
    /// is left for the menu.
    /// </summary>
    [Test]
    public async Task ARunWhoseSessionIsLost_StopsAndIsLeftForTheMenuInTheNextSession()
    {
        await Page.GotoAsync(BaseUrl);
        await Expect(StartRunButton(Page)).ToBeEnabledAsync(new() { Timeout = 20000 });
        await StartRunButton(Page).ClickAsync();
        await Expect(Page.GetByTestId("time-trial-canvas")).ToBeVisibleAsync(new() { Timeout = 15000 });
        await WaitForSnapshotAsync("snapshot => snapshot.runIn === false && snapshot.step > 0");

        // A second tab takes the session.
        IPage second = Watch(await Context.NewPageAsync());
        await second.GotoAsync(BaseUrl);
        await Expect(StartRunButton(second)).ToBeEnabledAsync(new() { Timeout = 20000 });
        ILocator dialog = Dialog(Page, "Playing elsewhere");
        await Expect(dialog).ToBeVisibleAsync(new() { Timeout = 15000 });

        // The run stands still behind the dialog, and takes no input: no press, and no Space.
        double stoppedAt = await ReadRunnerXAsync();
        await Page.WaitForTimeoutAsync(2000);
        Assert.That(await ReadRunnerXAsync(), Is.EqualTo(stoppedAt), "the run went on with its session lost");
        Assert.That(await Page.EvaluateAsync<bool>("async () => (await import('/js/timeTrialHost.js')).isInputEnabled()"),
            Is.False, "the run still takes input with its session lost");
        await Expect(Page.GetByTestId("time-trial-canvas")).ToBeVisibleAsync();

        // From here until the menu is up, a frame that sees any other snapshot than the stopped run's last one means the
        // run was started or stepped again. The page watches every frame, which no poll across the wire could.
        await Page.EvaluateAsync(
            """
            async () => {
                const host = await import('/js/timeTrialHost.js');
                const frozen = host.getLastSnapshot();
                window.__runMovedAfterLoss = false;
                const watch = () => {
                    if (host.getLastSnapshot() !== frozen)
                        window.__runMovedAfterLoss = true;
                    if (!document.querySelector('[data-testid="main-menu"]'))
                        window.requestAnimationFrame(watch);
                };
                window.requestAnimationFrame(watch);
            }
            """);

        // Taking the session back leaves the run for the menu.
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Play here" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Alertdialog)).ToHaveCountAsync(0, new() { Timeout = 20000 });
        await Expect(Page.GetByTestId("main-menu")).ToBeVisibleAsync(new() { Timeout = 15000 });
        Assert.That(await Page.EvaluateAsync<bool>("() => window.__runMovedAfterLoss"), Is.False,
            "the stopped run was started again before the menu took its place");
        await Expect(Page.GetByTestId("time-trial-canvas")).ToHaveCountAsync(0);
        await Expect(StartRunButton(Page)).ToBeEnabledAsync(new() { Timeout = 15000 });
    }

    /// <summary>
    /// A session lost while the curtain still covers a run, waiting for its art, leaves the run unseen: the curtain
    /// uncovers the menu under the dialog, never the run.
    /// </summary>
    [Test]
    public async Task ASessionLostWhileTheRunsArtLoads_UncoversTheMenuInsteadOfTheRun()
    {
        await Page.GotoAsync(BaseUrl);
        await Expect(StartRunButton(Page)).ToBeEnabledAsync(new() { Timeout = 20000 });

        // The run's art is held back, so the curtain stays covered over the run while the session is lost. Registered
        // once the menu is up, so only what is loaded from here on is held back. The served sheets carry a content
        // hash in their names, so they are matched by extension alone.
        await Page.RouteAsync("**/art/*.webp", async route =>
        {
            await Task.Delay(15000);
            try
            {
                await route.ContinueAsync();
            }
            catch (PlaywrightException)
            {
                // The test was over before the response was let through.
            }
        });

        // Any frame with the run's canvas in the document and the curtain not covering it is the run uncovered.
        await Page.EvaluateAsync(
            """
            () => {
                window.__runUncovered = false;
                const watch = () => {
                    const state = document.querySelector('[data-testid="screen-transition"]')?.dataset.transitionState;
                    if (document.querySelector('[data-testid="time-trial-canvas"]') && state !== 'covering' && state !== 'covered')
                        window.__runUncovered = true;
                    window.requestAnimationFrame(watch);
                };
                window.requestAnimationFrame(watch);
            }
            """);

        await StartRunButton(Page).ClickAsync();
        await Expect(Page.GetByTestId("screen-transition").GetByTestId("run-loading")).ToBeVisibleAsync(new() { Timeout = 15000 });

        // A second tab takes the session while the art is still on its way.
        IPage second = Watch(await Context.NewPageAsync());
        await second.GotoAsync(BaseUrl);
        await Expect(StartRunButton(second)).ToBeEnabledAsync(new() { Timeout = 20000 });
        ILocator dialog = Dialog(Page, "Playing elsewhere");
        await Expect(dialog).ToBeVisibleAsync(new() { Timeout = 15000 });

        // The menu is back under the dialog, and stays once the session is taken back.
        await Expect(Page.GetByTestId("main-menu")).ToBeVisibleAsync(new() { Timeout = 15000 });
        await Expect(Page.GetByTestId("time-trial-canvas")).ToHaveCountAsync(0);
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Play here" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Alertdialog)).ToHaveCountAsync(0, new() { Timeout = 20000 });
        await Expect(StartRunButton(Page)).ToBeEnabledAsync(new() { Timeout = 15000 });
        Assert.That(await Page.EvaluateAsync<bool>("() => window.__runUncovered"), Is.False,
            "the curtain uncovered the run after its session was lost");
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
        await Page.GotoAsync(BaseUrl);
        await Expect(StartRunButton(Page)).ToBeEnabledAsync(new() { Timeout = 20000 });
        ILocator playerId = Page.GetByText(new Regex("^Player:"));
        string firstPlayer = await playerId.InnerTextAsync();

        // What a link to another page of a larger game leaves in the address bar: another route, without the query.
        await Page.EvaluateAsync("history.replaceState(null, '', 'elsewhere')");

        await Page.GetByRole(AriaRole.Button, new() { Name = "Reset" }).ClickAsync();

        await Expect(playerId).Not.ToHaveTextAsync(firstPlayer, new() { Timeout = 30000 });
        await Expect(StartRunButton(Page)).ToBeEnabledAsync();
        Assert.That(Page.Url, Is.EqualTo(BaseUrl));
    }

    /// <summary>How far the runner has got, in meters along the course, from the run's last frame.</summary>
    private async Task<double> ReadRunnerXAsync()
    {
        return await Page.EvaluateAsync<double>(
            "async () => (await import('/js/timeTrialHost.js')).getLastSnapshot()?.x ?? -1");
    }
}
