using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;
using System.Text.RegularExpressions;

namespace WebClient.Tests;

/// <summary>
/// E2E tests for the meta shell that need a browser, such as focus order, CSS, layout at several viewports and
/// Profile's name and record. They run against a live game server because the name and the record are server
/// state. What the shell renders from the fixture, and which route each control opens, is asserted without a
/// WASM boot in render tests such as <see cref="ShellRenderTests"/>.
/// <para>
/// This fixture can run in parallel with any other. No test taps PLAY, so none uses the matchmaking queue (see
/// <see cref="LiveServerMatchmakingTests"/>), and each test gets a new browser context and so a new guest account,
/// which keeps the per-player rename cooldown separate.
/// </para>
/// </summary>
[TestFixture]
public class ShellPageTests : PlaywrightPageTest
{
    /// <summary>
    /// One Tab stop read from the browser (<see
    /// cref="Home_TabOrderStartsAtTheProfileButtonAndEndsAtTheNavigationInVisualOrder"/>).
    /// A class with settable properties, not a record, because Playwright's JSON converter creates the result with
    /// <c>Activator.CreateInstance</c> and then sets properties, so it cannot use a positional constructor.
    /// </summary>
    private sealed class TabStop
    {
        public string TestId { get; set; } = "";
        public bool Visible { get; set; }
        public bool HasRing { get; set; }
    }

    private static readonly string HomeUrl    = ClientUrl();
    private static readonly string ProfileUrl = ClientUrl("/profile");

    // -----------------------------------------------------------------------------------------------------
    // Viewports for layout assertions
    // -----------------------------------------------------------------------------------------------------
    //
    // Layout tests loop over widths inside one test case instead of using a [TestCase] per width. A resize
    // costs a reflow, while a test case costs a new browser context and a cold WASM boot. Each assertion names
    // the width it failed at.
    //
    // Set the first width before navigating, so the first paint also happens at a design width. A layout can be
    // correct after a resize and wrong on first paint.

    /// <summary>Every viewport the shell is designed for: phones, a tablet and desktops.</summary>
    private static readonly (int Width, int Height)[] DesignViewports =
        { (360, 640), (390, 844), (430, 932), (600, 960), (1024, 768), (1440, 900) };

    /// <summary>The phone viewports, where cards have the least horizontal room.</summary>
    private static readonly (int Width, int Height)[] PhoneViewports =
        { (360, 640), (390, 844), (430, 932) };

    /// <summary>The desktop viewports, where the app is drawn inside the phone frame.</summary>
    private static readonly (int Width, int Height)[] DesktopViewports =
        { (1440, 900), (1024, 768) };

    /// <summary>Open Home and wait until the shell has drawn its persistent chrome.</summary>
    private async Task OpenHomeAsync()
    {
        await Page.GotoAsync(HomeUrl);
        await Expect(Page.GetByTestId("primary-nav")).ToBeVisibleAsync(new() { Timeout = BootTimeoutMs });
    }

    /// <summary>
    /// Open Profile and wait until it shows the live session's state.
    /// <para>
    /// The method waits for the session marker (<see cref="PlaywrightPageTest.WaitForLiveSessionAsync"/>)
    /// because Profile shows fixture values, including a name, before the player model arrives. A rename typed
    /// before then is not sent. It then waits for the name, which the screen's controls read from.
    /// </para>
    /// </summary>
    private async Task OpenProfileAsync()
    {
        await Page.GotoAsync(ProfileUrl);
        await WaitForLiveSessionAsync();
        await Expect(Page.GetByTestId("player-name-text")).Not.ToBeEmptyAsync(new() { Timeout = BootTimeoutMs });
    }

    /// <summary>
    /// How far the content of the shell's scroll surface (<c>main</c>) exceeds its height, in pixels. Returns -1
    /// when there is no <c>main</c> element.
    /// </summary>
    private async Task<int> ScrollOverflowAsync() => await Page.EvaluateAsync<int>(
        "() => { const m = document.querySelector('main'); return m ? m.scrollHeight - m.clientHeight : -1; }");

    /// <summary>
    /// The boot screen, which <c>wwwroot/index.html</c> shows while the WebAssembly runtime downloads and starts.
    /// <para>
    /// The test blocks the framework's requests so Blazor never mounts and never replaces <c>#app</c>. Otherwise
    /// the boot screen could disappear before the assertions, depending on the browser cache.
    /// <c>bootDelayMs=0</c> removes the boot screen's reveal delay (docs/testing.md, "Forcing timers").
    /// </para>
    /// </summary>
    [Test]
    public async Task BootScreen_ShowsBrandingWhileLoadingAndIsGoneOnceTheAppMounts()
    {
        await Page.RouteAsync("**/_framework/**", route => route.AbortAsync());

        await Page.GotoAsync(ClientUrl("", "bootDelayMs=0"));

        ILocator boot = Page.GetByTestId("boot");
        await Expect(boot).ToBeVisibleAsync();

        // With the reveal delay at zero, only the short fade-in animation remains to wait for.
        await Page.WaitForFunctionAsync(
            "() => parseFloat(getComputedStyle(document.getElementById('ts-boot')).opacity) >= 0.99");

        await Expect(boot.GetByText("Table Stakes")).ToBeVisibleAsync();
        await Expect(Page.GetByTestId("boot-phase")).ToHaveTextAsync("Loading");

        // No download has completed, so the progress bar is indeterminate, which a progressbar expresses by
        // having no aria-valuenow.
        ILocator track = Page.Locator("#ts-boot-track");
        await Expect(track).ToHaveAttributeAsync("role", "progressbar");
        await Expect(track).ToHaveAttributeAsync("aria-label", "Loading Table Stakes");
        Assert.That(await track.GetAttributeAsync("aria-valuenow"), Is.Null);

        // Unblock the requests. The client boots and its first render removes the boot screen.
        await Page.UnrouteAsync("**/_framework/**");
        await OpenHomeAsync();
        await Expect(boot).ToHaveCountAsync(0);
    }

    /// <summary>
    /// The app boots into Home, connects, and opens no sheet, confirmation or reward reveal by itself.
    /// </summary>
    [Test]
    public async Task Home_LoadsAndConnects()
    {
        await Page.GotoAsync(HomeUrl);
        await Expect(Page).ToHaveTitleAsync("Table Stakes");

        // The name on Home's identity row is the only value on Home that comes from the server. ConnectionOverlay
        // draws nothing while the connection is healthy, and the rest of Home can still be fixture data.
        //
        // The pattern matches a generated AdjectiveNounNN name, so the assertion also checks that a new player
        // is named automatically from the published vocabulary and not with the Guest fallback
        // (docs/player.md, "Generated names").
        await Expect(Page.GetByTestId("home-profile").GetByTestId("avatar-name")).ToHaveTextAsync(
            new Regex("^[A-Z][a-z]+[A-Z][a-z]+[0-9]{2}$"),
            new() { Timeout = BootTimeoutMs });

        // No reconnect pill and no connection error dialog.
        await Expect(Page.GetByTestId("connection-modal")).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("connection-pill")).ToHaveCountAsync(0);

        await Expect(Page.GetByTestId("play-hero")).ToBeVisibleAsync();

        await Expect(Page.GetByTestId("sheet")).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("confirm")).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("reward-reveal")).ToHaveCountAsync(0);
    }

    /// <summary>
    /// The HUD is one row at the token height at every phone width.
    /// <para>
    /// The HUD's contents are asserted in <c>ShellRenderTests</c>
    /// (<c>TheHudIsTheThreeBalancesAndCarriesNoIdentity</c>, <c>HomeCarriesTheIdentityRowAndItsRouteToProfile</c>).
    /// Those tests check that the markup is present, not that it is visible. A balance chip hidden by CSS passes
    /// there and also passes here, because the HUD's height comes from a token and not from its contents. This
    /// test only measures the rendered height.
    /// </para>
    /// </summary>
    [Test]
    public async Task TheHudIsOneRowAtTheTokenHeightAtEveryPhoneWidth()
    {
        await OpenHomeAsync();

        // The limit allows the one-row token height plus rounding, and fails if the HUD wraps to a second row.
        foreach ((int width, int height) in PhoneViewports)
        {
            await Page.SetViewportSizeAsync(width, height);

            float hudHeight = await Page.Locator(".m-hud").EvaluateAsync<float>(
                "el => el.getBoundingClientRect().height");

            Assert.That(hudHeight, Is.LessThanOrEqualTo(64),
                $"the HUD is {hudHeight}px tall at {width}x{height}, not the one-row token height");
        }
    }

    /// <summary>
    /// On Home, the first Tab stop is the identity row's Profile button and the last stops are the navigation
    /// destinations in visual order. Every stop has a non-zero box and a visible focus ring.
    /// <para>
    /// The stops in between are not asserted, because which cards Home shows depends on player state. The first
    /// and last stops do not: the identity row is Home's first card, its button is the first focusable element
    /// (the avatar is not focusable), and the navigation is last in the page.
    /// </para>
    /// </summary>
    [Test]
    public async Task Home_TabOrderStartsAtTheProfileButtonAndEndsAtTheNavigationInVisualOrder()
    {
        await OpenHomeAsync();

        List<string> stops = new();
        for (int i = 0; i < 40 && stops.Count(s => s.StartsWith("nav-", StringComparison.Ordinal)) < 5; i++)
        {
            await Page.Keyboard.PressAsync("Tab");

            TabStop stop = await Page.EvaluateAsync<TabStop>(
                "() => { const e = document.activeElement; " +
                "const box = e.getBoundingClientRect(); " +
                "const style = getComputedStyle(e); " +
                "return { testId: e.getAttribute('data-testid') || e.tagName, " +
                "visible: box.width > 0 && box.height > 0, " +
                "hasRing: style.outlineStyle !== 'none' && parseFloat(style.outlineWidth) > 0 }; }");

            Assert.That(stop.Visible, Is.True, $"tab stop {i + 1} ('{stop.TestId}') has no visible box");
            Assert.That(stop.HasRing, Is.True, $"tab stop {i + 1} ('{stop.TestId}') drew no focus ring");
            stops.Add(stop.TestId);
        }

        Assert.That(stops, Is.Not.Empty);
        Assert.That(stops[0], Is.EqualTo("home-profile-action"), "the first tab stop was not the identity row's Profile button");

        string[] navStops = stops.Where(s => s.StartsWith("nav-", StringComparison.Ordinal)).ToArray();
        Assert.That(navStops, Is.EqualTo(new[] { "nav-home", "nav-events", "nav-play", "nav-compete", "nav-shop" }),
            "the five destinations were not reached in their visual order");

        // The navigation is last in the page, so the nav stops must be the tail of the sequence, with nothing
        // between or after them.
        Assert.That(stops.TakeLast(5), Is.EqualTo(navStops), "something was reachable after the navigation, or between its five destinations");
    }

    /// <summary>The feature routes that must each boot when opened directly.</summary>
    private static readonly string[] FeatureRoutes =
    {
        "/events", "/events/daily", "/events/first-week", "/events/missions", "/events/spin",
        "/events/weekly", "/compete", "/compete/tournament", "/shop", "/profile", "/profile/cosmetics",
    };

    /// <summary>
    /// Wait until the page shows exactly one visible element with test id <paramref name="marker"/>. Returns null
    /// on success and a failure message otherwise. It returns a message instead of throwing so that a test can
    /// collect several failures and report them together.
    /// </summary>
    private async Task<string?> RouteDrewAsync(string route, string marker)
    {
        try
        {
            await Page.GetByTestId(marker).WaitForAsync(
                new() { State = WaitForSelectorState.Visible, Timeout = BootTimeoutMs });
            return null;
        }
        catch (PlaywrightException e)
        {
            // Either no element matched, or more than one matched and strict mode refused. Playwright's first
            // message line says which.
            return $"{route} drew no single visible '{marker}' — {e.Message.Split('\n')[0].Trim()}";
        }
    }

    /// <summary>
    /// Opening a feature route directly boots the app on that route, with a page title and the navigation drawn.
    /// <para>
    /// Each route is a separate test case so that each gets its own browser context and therefore a cold boot at
    /// that URL, which is what a deep link is. The cases also run on parallel workers. The three checks for one
    /// route are collected and reported together.
    /// </para>
    /// </summary>
    [TestCaseSource(nameof(FeatureRoutes))]
    public async Task EveryFeatureRouteBootsOnItsOwnRoute(string route)
    {
        List<string> failures = new List<string>();

        await Page.GotoAsync(ClientUrl(route));

        string? drewTitle = await RouteDrewAsync(route, "page-title");
        if (drewTitle != null)
            failures.Add(drewTitle);

        // The app stays on the deep-linked route instead of redirecting to Home. Loading a URL boots the app,
        // so this also covers a refresh. The title text per page is asserted in FeatureRouteRenderTests.
        string landedOn = RouteOf(Page.Url);
        if (landedOn != route)
            failures.Add($"{route} came back on {landedOn} instead of its own route");

        string? drewNav = await RouteDrewAsync(route, "primary-nav");
        if (drewNav != null)
            failures.Add(drewNav);

        if (failures.Count > 0)
            Assert.Fail(string.Join(Environment.NewLine, failures));
    }

    /// <summary>
    /// The selected navigation tab is derived from the route, so booting on a child route selects its parent's
    /// tab.
    /// </summary>
    [Test]
    public async Task ARefreshOnAChildRouteKeepsItsDestinationSelected()
    {
        await Page.GotoAsync(ClientUrl("/events/spin"));
        await Expect(Page.GetByTestId("page-title")).ToBeVisibleAsync(new() { Timeout = BootTimeoutMs });

        await Expect(Page.GetByTestId("nav-events")).ToHaveAttributeAsync("aria-current", "page");

        // With no browser history, Back goes to the parent hub instead of leaving the app.
        await Page.GetByTestId("page-back").ClickAsync();
        Assert.That(Page.Url, Does.EndWith("/events"));
    }

    /// <summary>The current screen element, whose <c>data-screen-in</c> holds the direction it entered from.</summary>
    private ILocator Screen => Page.Locator("[data-screen-in]");

    /// <summary>
    /// A navigation replaces the screen element, the new screen enters from the direction given by the two routes,
    /// and it starts scrolled to its top.
    /// <para>
    /// The shell has one scroll surface that persists across screens. Without the reset in
    /// <c>wwwroot/screen-scroll.js</c>, a screen opened from a scrolled hub would open at the hub's offset.
    /// The direction for each pair of routes is asserted in <see cref="ScreenTransitionTests"/>.
    /// </para>
    /// </summary>
    [Test]
    public async Task AScreenComesInFromTheDirectionThePlayerWentAndStartsAtItsTop()
    {
        await Page.SetViewportSizeAsync(390, 844);
        await Page.GotoAsync(ClientUrl("/events"));
        await Expect(Page.GetByTestId("primary-nav")).ToBeVisibleAsync(new() { Timeout = BootTimeoutMs });
        await WaitForLiveSessionAsync();

        Assert.That(await ScrollOverflowAsync(), Is.GreaterThan(0),
            "the Events hub fits on this screen, so this case could not have seen an offset carried at all");

        await Page.EvaluateAsync("() => { const m = document.querySelector('.m-shell__main'); m.scrollTop = m.scrollHeight; }");

        // Scroll the card into view before the click, because a click scrolls its target into view itself and
        // could reset the offset on its own. Reading the offset after this step gives the offset at the time
        // of the navigation.
        await Page.GetByTestId("feature-missions-action").ScrollIntoViewIfNeededAsync();
        Assert.That(await ScrollTopAsync(), Is.GreaterThan(0),
            "the Missions card sits at the top of the hub here, so this case could not have seen an offset carried");

        // Open a child of the hub: it enters forward and starts at the top, not at the hub's offset.
        await StashScreenAsync();
        await Page.GetByTestId("feature-missions-action").ClickAsync();
        await Expect(Screen).ToHaveAttributeAsync("data-screen-in", "forward");
        Assert.That(RouteOf(Page.Url), Is.EqualTo("/events/missions"));
        Assert.That(await ScrollTopAsync(), Is.Zero, "the child screen opened at the offset the hub had been scrolled to");

        // After the entrance animation, the screen has no translate left. A translated element becomes the
        // containing block for `position: fixed` descendants, so every sheet on the screen would be positioned
        // against the screen instead of the app. The computed value is checked because an animation fill mode
        // can keep the translate even when the stylesheet looks correct.
        await Expect(Screen).ToHaveCSSAsync("translate", "none");

        Assert.That(await ScreenWasReplacedAsync(), Is.True, "the hub's own screen element was patched into the child");

        // Back enters backward.
        await Page.GetByTestId("page-back").ClickAsync();
        await Expect(Screen).ToHaveAttributeAsync("data-screen-in", "back");
        Assert.That(RouteOf(Page.Url), Is.EqualTo("/events"));

        // Between navigation tabs, direction follows tab order: Shop is after Events, and Home is before both.
        await Page.GetByTestId("nav-shop").ClickAsync();
        await Expect(Screen).ToHaveAttributeAsync("data-screen-in", "forward");
        Assert.That(RouteOf(Page.Url), Is.EqualTo("/shop"));

        await Page.GetByTestId("nav-home").ClickAsync();
        await Expect(Screen).ToHaveAttributeAsync("data-screen-in", "back");
        Assert.That(RouteOf(Page.Url), Is.EqualTo("/"));

        // Two steps in the same direction. The attribute value does not change, so only a new screen element
        // replays the entrance animation.
        await Page.GetByTestId("nav-events").ClickAsync();
        await Expect(Screen).ToHaveAttributeAsync("data-screen-in", "forward");
        await StashScreenAsync();

        await Page.GetByTestId("nav-shop").ClickAsync();
        await Expect(Screen).ToHaveAttributeAsync("data-screen-in", "forward");
        Assert.That(RouteOf(Page.Url), Is.EqualTo("/shop"));
        Assert.That(await ScreenWasReplacedAsync(), Is.True,
            "a second step in the same direction reused the screen element, so nothing played its entrance");
    }

    /// <summary>Store the current screen element in the page, to compare with after a navigation.</summary>
    private Task StashScreenAsync() =>
        Page.EvaluateAsync("() => window.__screen = document.querySelector('[data-screen-in]')");

    /// <summary>
    /// Whether the current screen element is a different node from the stored one. Both the entrance animation
    /// and the scroll reset in <c>wwwroot/screen-scroll.js</c> run only for a new element.
    /// </summary>
    private Task<bool> ScreenWasReplacedAsync() => Page.EvaluateAsync<bool>(
        "() => window.__screen !== document.querySelector('[data-screen-in]')");

    /// <summary>
    /// Profile shows the games played, games won and tricks won (docs/player.md). Before the first game the record
    /// shows 0 played, 0 won and a dash for the win rate. A 0% win rate would imply games played and lost.
    /// </summary>
    [Test]
    public async Task ProfileShowsADashForTheWinRateBeforeTheFirstGame()
    {
        await OpenProfileAsync();

        await Expect(Page.GetByTestId("record-played")).ToHaveTextAsync("0");
        await Expect(Page.GetByTestId("record-won")).ToHaveTextAsync("0");
        await Expect(Page.GetByTestId("record-tricks")).ToBeVisibleAsync();
        await Expect(Page.GetByTestId("record-winrate")).ToHaveTextAsync("—");
    }

    /// <summary>
    /// A rename to a valid name is accepted, and a name of the maximum length is drawn in full on Profile's two
    /// name plates (the hero and the "how others see you" preview) and on Home's identity row.
    /// <para>
    /// Neither failure causes page overflow. A Profile plate that is too narrow wraps the name, which the test
    /// detects as more than one client rect (see <see cref="WebClient.Meta.NamePlate"/>). Home's identity row
    /// truncates with an ellipsis, which the test detects from the scroll width. Home is reached by in-app
    /// navigation so the session keeps the new name.
    /// </para>
    /// </summary>
    [Test]
    public async Task TheLongestPermittedNameIsDrawnWholeOnProfileAndOnHomesIdentityRow()
    {
        await Page.SetViewportSizeAsync(PhoneViewports[0].Width, PhoneViewports[0].Height);
        await OpenProfileAsync();

        // The longest name the server accepts.
        const string LongestName = "MidnightQueen123";
        Assert.That(LongestName, Has.Length.EqualTo(Game.Logic.DisplayNamePolicy.MaxLength));

        await Page.GetByTestId("player-name").ClickAsync();
        await Page.GetByTestId("name-input").FillAsync(LongestName);
        await Page.GetByTestId("name-save").ClickAsync();

        // The client does not apply a rename locally, so the name on screen comes from the server
        // (docs/player.md, "Renaming").
        await Expect(Page.GetByTestId("player-name-text")).ToHaveTextAsync(LongestName, new() { Timeout = BootTimeoutMs });
        await Expect(Page.GetByTestId("name-message")).ToContainTextAsync("Name changed.");

        // Profile's two plates at the two narrowest phone widths.
        foreach ((int width, int height) in new[] { (360, 640), (390, 844) })
        {
            await Page.SetViewportSizeAsync(width, height);

            int heroLines = await Page.GetByTestId("player-name-text")
                .EvaluateAsync<int>("e => e.getClientRects().length");
            int previewLines = await Page.GetByTestId("identity-preview").GetByTestId("avatar-name")
                .EvaluateAsync<int>("e => e.getClientRects().length");

            Assert.Multiple(() =>
            {
                Assert.That(heroLines, Is.EqualTo(1), $"{width}px: the name beside the portrait broke onto a second line");
                Assert.That(previewLines, Is.EqualTo(1), $"{width}px: the name in the preview broke onto a second line");
            });
        }

        // Home's identity row at every phone width, showing the new name.
        await Page.SetViewportSizeAsync(PhoneViewports[0].Width, PhoneViewports[0].Height);
        await Page.GetByTestId("nav-home").ClickAsync();
        await Expect(Page.GetByTestId("home-profile").GetByTestId("avatar-name")).ToHaveTextAsync(LongestName);

        foreach ((int width, int height) in PhoneViewports)
        {
            await Page.SetViewportSizeAsync(width, height);

            int[] measured = await Page.GetByTestId("home-profile").GetByTestId("avatar-name")
                .EvaluateAsync<int[]>("e => [e.scrollWidth - e.clientWidth, e.textContent.trim().length]");

            Assert.That(measured[1], Is.EqualTo(LongestName.Length), $"at {width}px the row is not drawing the name that was set");
            Assert.That(measured[0], Is.LessThanOrEqualTo(0),
                $"the identity row truncates a {measured[1]}-character name at {width}x{height} by {measured[0]}px");
        }
    }

    /// <summary>
    /// Home's identity row is the only route to Profile, which has no navigation tab (docs/meta-shell.md,
    /// "Screens and routes"). Profile's Cosmetics control opens the Cosmetics screen, and Back returns to Profile.
    /// The control looks like a segmented pair but it navigates to a route, so the test asserts navigation. That
    /// the navigation has no Profile tab is asserted in <see cref="ShellRenderTests"/>.
    /// </summary>
    [Test]
    public async Task ProfileReachesCosmeticsAndBackReturns()
    {
        await OpenHomeAsync();

        await Page.GetByTestId("home-profile-action").ClickAsync();
        await Expect(Page.GetByTestId("player-name")).ToBeVisibleAsync(new() { Timeout = BootTimeoutMs });
        Assert.That(RouteOf(Page.Url), Is.EqualTo("/profile"));

        await Page.GetByTestId("open-cosmetics").ClickAsync();

        await Expect(Page.GetByTestId("page-title")).ToHaveTextAsync("Cosmetics");
        await Expect(Page.GetByTestId("cosmetic-slots")).ToBeVisibleAsync();

        await Page.GetByTestId("page-back").ClickAsync();
        await Expect(Page.GetByTestId("page-title")).ToHaveTextAsync("Profile");
    }

    /// <summary>
    /// The "how others see you" preview shows the player's own name and cosmetics, produced by the same projection
    /// the standings use, and not fixture cosmetics (docs/player.md, "Public identity").
    /// </summary>
    [Test]
    public async Task TheHowOthersSeeYouPreviewShowsTheSameNameAndNoBorrowedCosmetics()
    {
        await OpenProfileAsync();

        string name = await Page.GetByTestId("player-name-text").InnerTextAsync();

        await Expect(Page.GetByTestId("identity-preview").GetByTestId("avatar-name")).ToHaveTextAsync(name);

        // A new player wears only the starting cosmetics (docs/cosmetics.md, "The starting three"), so the
        // preview draws the plain frame and plain name, not the fixture's cosmetics. A bought cosmetic shown
        // here is tested in LiveServerCosmeticsTests.
        await Expect(Page.GetByTestId("identity-preview").GetByTestId("avatar")).ToHaveClassAsync(new Regex(@"\bm-frame--frame-plain\b"));
        await Expect(Page.GetByTestId("identity-preview").GetByTestId("avatar-name")).ToHaveClassAsync(new Regex(@"\bm-name--name-plain\b"));

        // The hero name uses the same PlayerName component, so the same rule applies.
        await Expect(Page.GetByTestId("player-name-text")).ToHaveClassAsync(new Regex(@"\bm-name--name-plain\b"));

        // Home's identity row uses the same projection. In-app navigation keeps the session.
        await Page.GetByTestId("nav-home").ClickAsync();
        await Expect(Page.GetByTestId("home-profile").GetByTestId("avatar")).ToHaveClassAsync(new Regex(@"\bm-frame--frame-plain\b"));
        await Expect(Page.GetByTestId("home-profile").GetByTestId("avatar-name")).ToHaveClassAsync(new Regex(@"\bm-name--name-plain\b"));
    }

    /// <summary>
    /// A rename to a bot's name is refused with a reason. This refusal is tested end to end because the reserved
    /// bot names are published game config.
    /// <para>
    /// The rename rules and refusal messages are asserted in <c>Backend/SharedCode.Tests/DisplayNamePolicyTests.cs</c>.
    /// This test covers the round trip: the client sends the name, the server checks it against the game config
    /// it is serving, and the refusal reaches the screen.
    /// </para>
    /// </summary>
    [Test]
    public async Task Renaming_RefusesAComputerPlayersNameAndSaysWhy()
    {
        await OpenProfileAsync();

        string before = await Page.GetByTestId("player-name-text").InnerTextAsync();

        // "Cogwheel" is a reserved bot name, and "Cog Wheel" must be refused as the same name. A player must
        // not be able to pose as a bot or be mistaken for one (docs/bots.md).
        await Page.GetByTestId("player-name").ClickAsync();
        await Page.GetByTestId("name-input").FillAsync("Cog Wheel");
        await Page.GetByTestId("name-save").ClickAsync();

        // The refusal message gives the reason, and the name is unchanged.
        await Expect(Page.GetByTestId("name-message")).ToContainTextAsync("computer player", new() { Timeout = BootTimeoutMs });
        await Expect(Page.GetByTestId("player-name-text")).ToHaveTextAsync(before);
    }

    /// <summary>
    /// On a desktop the whole app, including the meta shell, is drawn inside the portrait phone frame.
    /// <para>
    /// The frame is on <c>#app</c>, so it applies to every route (app.css, "The phone frame"). The test also
    /// measures the shell's box, which must fill the frame and not exceed it.
    /// </para>
    /// </summary>
    [Test]
    public async Task OnADesktopTheAppIsDrawnIntoThePortraitPhoneFrame()
    {
        await Page.SetViewportSizeAsync(DesktopViewports[0].Width, DesktopViewports[0].Height);
        await OpenHomeAsync();

        foreach ((int width, int height) in DesktopViewports)
        {
            await Page.SetViewportSizeAsync(width, height);

            // Measure the frame with clientWidth, which excludes the bezel border, because the app gets only the
            // area inside the bezel.
            int[] box = await Page.EvaluateAsync<int[]>(
                "() => { const a = document.querySelector('#app');" +
                " const s = document.querySelector('.m-shell').getBoundingClientRect();" +
                " return [a.clientWidth, a.clientHeight, Math.round(s.width), Math.round(s.height)]; }");

            Assert.Multiple(() =>
            {
                Assert.That(box[0], Is.EqualTo(390).Within(2), $"at {width}x{height} the frame is not the phone screen it is drawn for");
                Assert.That(box[1], Is.LessThanOrEqualTo(844), $"at {width}x{height} the frame is taller than the phone screen");
                Assert.That(box[2], Is.EqualTo(box[0]).Within(2), $"at {width}x{height} the shell is not filling the frame");
                Assert.That(box[3], Is.EqualTo(box[1]).Within(2), $"at {width}x{height} the shell is not filling the frame");
            });
        }
    }

    /// <summary>
    /// Everything the shell draws stays inside the frame, including the HUD and the navigation bar.
    /// <para>
    /// Both bars are <c>position: fixed</c>. They are positioned against the frame only because <c>#app</c> has
    /// a transform. Without it they would be positioned against the browser window, and no overflow check would
    /// notice.
    /// </para>
    /// </summary>
    [Test]
    public async Task NothingTheShellDrawsEscapesTheFrame()
    {
        await Page.SetViewportSizeAsync(1440, 900);
        await OpenHomeAsync();

        string[] escaped = await Page.EvaluateAsync<string[]>(@"() => {
            const app = document.querySelector('#app').getBoundingClientRect();
            const out = [];
            for (const sel of ['.m-shell', '.m-hud', '[data-testid=""primary-nav""]', '.m-shell__main']) {
                const el = document.querySelector(sel);
                if (!el) { out.push(sel + ' missing'); continue; }
                const r = el.getBoundingClientRect();
                const over = Math.round(Math.max(r.right - app.right, app.left - r.left,
                                                 r.bottom - app.bottom, app.top - r.top));
                if (over > 1) out.push(sel + ' by ' + over + 'px');
            }
            return out;
        }");

        Assert.That(escaped, Is.Empty, $"drawn outside the frame: {string.Join(", ", escaped)}");
    }

    /// <summary>
    /// The navigation is a bottom bar at every width, and the route does not change on resize. The app is a
    /// portrait phone layout at every size, so there is no side-rail navigation.
    /// </summary>
    [Test]
    public async Task TheNavigationIsABottomBarAtEveryWidth()
    {
        await Page.SetViewportSizeAsync(DesignViewports[0].Width, DesignViewports[0].Height);
        await Page.GotoAsync(ClientUrl("/events"));
        await Expect(Page.GetByTestId("primary-nav")).ToBeVisibleAsync(new() { Timeout = BootTimeoutMs });

        // Add a 599px width next to the 600px design viewport, so both sides of a 600px breakpoint are checked.
        foreach ((int width, int height) in DesignViewports.Concat(new[] { (599, 900) }))
        {
            await Page.SetViewportSizeAsync(width, height);

            Assert.That(RouteOf(Page.Url), Is.EqualTo("/events"), "the route did not survive the width");

            // A bottom bar is wider than it is tall. A side rail would be taller than it is wide.
            int[] nav = await Page.EvaluateAsync<int[]>(
                "() => { const r = document.querySelector('[data-testid=\"primary-nav\"]').getBoundingClientRect(); return [Math.round(r.width), Math.round(r.height)]; }");

            Assert.That(nav[0], Is.GreaterThan(nav[1]), $"at {width}px the navigation is not a bottom bar");
        }
    }

    /// <summary>
    /// The shell's screens checked by <see cref="NoRouteScrollsHorizontallyAtAnyDesignViewport"/>, one per test case.
    /// </summary>
    private static readonly string[] LaidOutRoutes =
        { "/", "/events", "/events/first-week", "/compete", "/shop", "/profile" };

    /// <summary>
    /// No shell screen scrolls horizontally at any design viewport. This catches a card that is too wide for the
    /// phone.
    /// <para>
    /// Each route is a separate test case and each width is a loop iteration. A width change is a reflow, while a
    /// route needs a document load and a WASM boot, so separate cases let the boots run on parallel workers. Each
    /// case also gets its own browser context, so every screen is measured after a cold boot. A case reports every
    /// width that overflowed.
    /// </para>
    /// </summary>
    [TestCaseSource(nameof(LaidOutRoutes))]
    public async Task NoRouteScrollsHorizontallyAtAnyDesignViewport(string route)
    {
        List<string> failures = new List<string>();

        // Load the route at the narrowest width, so the first paint happens at the tightest layout.
        await Page.SetViewportSizeAsync(DesignViewports[0].Width, DesignViewports[0].Height);
        await Page.GotoAsync(ClientUrl(route));

        // Fail early if the screen did not draw, because an overflow measured then would not be the layout's.
        string? drewNav = await RouteDrewAsync(route, "primary-nav");
        if (drewNav != null)
            Assert.Fail(drewNav);

        foreach ((int width, int height) in DesignViewports)
        {
            await Page.SetViewportSizeAsync(width, height);

            int overflow = await Page.EvaluateAsync<int>(
                "() => document.documentElement.scrollWidth - document.documentElement.clientWidth");

            if (overflow > 0)
                failures.Add($"{route} scrolls sideways at {width}x{height} by {overflow}px");
        }

        if (failures.Count > 0)
            Assert.Fail(string.Join(Environment.NewLine, failures));
    }

    /// <summary>
    /// Every visible button in the shell on Home is at least 44x44 CSS pixels, measured from the rendered boxes.
    /// </summary>
    [Test]
    public async Task EveryPrimaryControlMeetsItsTouchTargetSize()
    {
        await Page.SetViewportSizeAsync(390, 844);
        await OpenHomeAsync();

        // Compare with a small tolerance and report sizes with two decimals. getBoundingClientRect can return
        // a value slightly under the minimum for a box laid out exactly at the minimum, and a rounded message
        // would report such a failure as "44".
        const double MinimumPx = 44.0;
        const double Tolerance = 0.05;

        string[] measured = await Page.EvaluateAsync<string[]>(@"([minimum, tolerance]) => {
            const bad = [];
            let seen = 0;
            for (const el of document.querySelectorAll('.m-shell button')) {
                const r = el.getBoundingClientRect();
                if (r.width === 0 && r.height === 0) continue;
                seen++;
                if (r.width < minimum - tolerance || r.height < minimum - tolerance)
                    bad.push((el.getAttribute('data-testid') || el.className || el.tagName)
                             + ' ' + r.width.toFixed(2) + 'x' + r.height.toFixed(2));
            }
            return [String(seen)].concat(bad);
        }", new object[] { MinimumPx, Tolerance });

        // The script also returns the number of buttons measured, because Is.Empty would also pass if the
        // selector matched nothing, for example after a CSS class rename.
        int      drawn    = int.Parse(measured[0]);
        string[] tooSmall = measured.Skip(1).ToArray();

        Assert.That(drawn, Is.GreaterThan(0), "no visible control matched '.m-shell button', so nothing was measured");
        Assert.That(tooSmall, Is.Empty, $"controls below the {MinimumPx}px minimum: {string.Join(", ", tooSmall)}");
    }

    /// <summary>
    /// Infinite animations on Home animate only <c>opacity</c> and <c>transform</c>.
    /// <para>
    /// A player can leave Home open indefinitely. The compositor runs <c>opacity</c> and <c>transform</c>
    /// animations without repainting, while other properties, such as <c>box-shadow</c>, are repainted on the
    /// main thread every frame. Finite animations are excluded because they stop.
    /// </para>
    /// </summary>
    [Test]
    public async Task NothingInTheShellLoopsForeverOnAPropertyThatHasToBeRepainted()
    {
        await OpenHomeAsync();

        string[] animated = await Page.EvaluateAsync<string[]>(@"() => {
            const props = new Set();
            for (const anim of document.getAnimations()) {
                const effect = anim.effect;
                if (!effect || !effect.getKeyframes || !effect.getTiming) continue;
                if (effect.getTiming().iterations !== Infinity) continue;
                for (const frame of effect.getKeyframes())
                    for (const key of Object.keys(frame))
                        if (!['offset', 'computedOffset', 'easing', 'composite'].includes(key))
                            props.add(key);
            }
            return [...props];
        }");

        // The hero has infinite animations, so an empty result means the query is broken.
        Assert.That(animated, Is.Not.Empty,
            "Expected the hero's idle animations to be running; found none, so this test proved nothing.");

        Assert.That(animated, Is.SubsetOf(new[] { "opacity", "transform" }),
            $"An idle loop animates a property that forces a repaint every frame: {string.Join(", ", animated)}.");
    }

    /// <summary>
    /// At every design viewport, the Events hub's stacked cards have a gap between them and each card's text
    /// column has a minimum width. Neither fault causes overflow, so the overflow tests miss both.
    /// <para>
    /// The scroll column's gap applies only to its direct children, and the hub is one child, so the hub must
    /// space its own cards. The width threshold is low, so it catches a collapsed column and not a narrow one.
    /// </para>
    /// </summary>
    [Test]
    public async Task TheEventsHubSeparatesItsCardsAndKeepsTheirTextColumnsReadableAtEveryWidth()
    {
        await Page.SetViewportSizeAsync(DesignViewports[0].Width, DesignViewports[0].Height);
        await Page.GotoAsync(ClientUrl("/events"));
        await Expect(Page.GetByTestId("feature-dailyreward")).ToBeVisibleAsync(new() { Timeout = BootTimeoutMs });

        foreach ((int width, int height) in DesignViewports)
        {
            await Page.SetViewportSizeAsync(width, height);

            // The smallest vertical gap between two stacked cards. Cards side by side in a row are skipped.
            double smallest = await Page.EvaluateAsync<double>(@"() => {
                const rects = [...document.querySelectorAll('.m-shell__column .m-card')]
                    .map(c => c.getBoundingClientRect()).filter(r => r.height > 0);
                let smallest = Infinity;
                for (let i = 1; i < rects.length; i++) {
                    const a = rects[i - 1], b = rects[i];
                    if (b.top < a.bottom - 1) continue;
                    const overlap = Math.min(a.right, b.right) - Math.max(a.left, b.left);
                    if (overlap < Math.min(a.width, b.width) * 0.5) continue;
                    smallest = Math.min(smallest, b.top - a.bottom);
                }
                return smallest === Infinity ? -1 : smallest;
            }");

            Assert.That(smallest, Is.GreaterThan(0),
                $"the hub's cards abut at {width}x{height}: smallest gap {smallest}px");

            int narrowest = await Page.EvaluateAsync<int>(
                "() => Math.round(Math.min(...[...document.querySelectorAll('.m-card__grow')]" +
                ".map(e => e.getBoundingClientRect().width)))");

            Assert.That(narrowest, Is.GreaterThanOrEqualTo(90),
                $"a hub card's text column is {narrowest}px wide at {width}x{height}");
        }
    }

    /// <summary>
    /// No element inside a card on Home extends past the card's horizontal padding.
    /// <para>
    /// Cards use <c>overflow: hidden</c> for their rounded corners, so content that is too wide is clipped and
    /// does not widen the page. <see cref="NoRouteScrollsHorizontallyAtAnyDesignViewport"/> cannot detect it.
    /// </para>
    /// </summary>
    [Test]
    public async Task NoCardOnHomeClipsItsOwnContents()
    {
        await Page.SetViewportSizeAsync(PhoneViewports[0].Width, PhoneViewports[0].Height);
        await Page.GotoAsync(ClientUrl());
        await Expect(Page.GetByTestId("next-up")).ToBeVisibleAsync(new() { Timeout = BootTimeoutMs });

        foreach ((int width, int height) in PhoneViewports)
        {
            await Page.SetViewportSizeAsync(width, height);

            string[] clipped = await Page.EvaluateAsync<string[]>(@"() => {
                const out = [];
                for (const card of document.querySelectorAll('.m-card')) {
                    const cs = getComputedStyle(card), cr = card.getBoundingClientRect();
                    const left = cr.left + (parseFloat(cs.paddingLeft) || 0);
                    const right = cr.right - (parseFloat(cs.paddingRight) || 0);
                    for (const d of card.querySelectorAll('*')) {
                        const r = d.getBoundingClientRect();
                        if (r.width === 0 || r.height === 0) continue;
                        const over = Math.round(Math.max(r.right - right, left - r.left));
                        if (over > 1)
                            out.push((d.getAttribute('data-testid') || d.className.toString()).slice(0, 30) + ' by ' + over + 'px');
                    }
                }
                return out;
            }");

            Assert.That(clipped, Is.Empty,
                $"content clipped by its card at {width}x{height}: {string.Join(", ", clipped)}");
        }
    }

    // -----------------------------------------------------------------------------------------------------
    // Scrolling the emulated phone with a mouse
    // -----------------------------------------------------------------------------------------------------

    /// <summary>The center point of the shell's scroll surface, for aiming the mouse.</summary>
    private async Task<(float X, float Y)> ScrollSurfaceCentreAsync()
    {
        LocatorBoundingBoxResult? bounds = await Page.Locator(".m-shell__main").BoundingBoxAsync();
        Assert.That(bounds, Is.Not.Null, "the shell drew no scroll surface");

        return (bounds!.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
    }

    /// <summary>Scroll Home's Spin wheel shortcut into view and return its centre point, for aiming the mouse.</summary>
    private async Task<(float X, float Y)> SpinWheelShortcutCentreAsync()
    {
        ILocator shortcut = Page.GetByTestId("shortcut-spinwheel");
        await shortcut.ScrollIntoViewIfNeededAsync();
        LocatorBoundingBoxResult? box = await shortcut.BoundingBoxAsync();
        Assert.That(box, Is.Not.Null);

        return (box!.X + box.Width / 2, box.Y + box.Height / 2);
    }

    /// <summary>The scroll surface's current <c>scrollTop</c>, rounded to whole pixels.</summary>
    private Task<int> ScrollTopAsync() => Page.EvaluateAsync<int>(
        "() => Math.round(document.querySelector('.m-shell__main').scrollTop)");

    /// <summary>
    /// A mouse drag scrolls the shell by exactly the drag distance, so the content stays under the pointer.
    /// <para>
    /// Drag-to-scroll handles only pointer events with <c>pointerType: "mouse"</c>, which is what Playwright's
    /// mouse sends. Touch input keeps the browser's native scrolling.
    /// </para>
    /// </summary>
    [Test]
    public async Task AMouseDragScrollsTheShell()
    {
        await Page.SetViewportSizeAsync(1440, 900);
        await OpenHomeAsync();

        (float x, float y) = await ScrollSurfaceCentreAsync();
        Assert.That(await ScrollTopAsync(), Is.Zero, "Home did not start at the top");

        // The exact-distance assertion needs a scroll range at least as long as the drag. Otherwise the scroll
        // would stop at the end of the range.
        Assert.That(await Page.EvaluateAsync<int>(
                "() => { const m = document.querySelector('.m-shell__main'); return m.scrollHeight - m.clientHeight; }"),
            Is.GreaterThanOrEqualTo(200),
            "Home has less than 200px to scroll at this size, so a 200px drag could not move it 200px");

        await Page.Mouse.MoveAsync(x, y);
        await Page.Mouse.DownAsync();
        for (int step = 1; step <= 10; step++)
            await Page.Mouse.MoveAsync(x, y - 20 * step);
        await Page.Mouse.UpAsync();

        Assert.That(await ScrollTopAsync(), Is.EqualTo(200),
            "a 200px drag up did not move the surface 200px down");

        // The drag selects no text, and the dragging state class is removed on release.
        Assert.That(await Page.EvaluateAsync<int>("() => window.getSelection().toString().length"), Is.Zero,
            "the drag selected text instead of scrolling");
        Assert.That(await Page.EvaluateAsync<bool>("() => document.body.classList.contains('is-drag-scrolling')"),
            Is.False, "the surface is still in its dragging state after the pointer came up");
    }

    /// <summary>
    /// Only the primary mouse button drags. A right-button drag does not scroll.
    /// </summary>
    [Test]
    public async Task ARightButtonDragDoesNotScroll()
    {
        await Page.SetViewportSizeAsync(1440, 900);
        await OpenHomeAsync();

        (float x, float y) = await ScrollSurfaceCentreAsync();

        await Page.Mouse.MoveAsync(x, y);
        await Page.Mouse.DownAsync(new() { Button = MouseButton.Right });
        for (int step = 1; step <= 10; step++)
            await Page.Mouse.MoveAsync(x, y - 20 * step);
        await Page.Mouse.UpAsync(new() { Button = MouseButton.Right });

        Assert.That(await ScrollTopAsync(), Is.Zero, "a right-button drag scrolled the shell");
    }

    /// <summary>
    /// A click inside the scroll surface still activates the control under it. Every control in the shell is
    /// inside the scroll surface.
    /// <para>
    /// The pointer moves two pixels between press and release, as a real mouse click can. The drag threshold
    /// must be larger than that movement, or clicks would sometimes be treated as drags.
    /// </para>
    /// </summary>
    [Test]
    public async Task AClickInsideTheScrollSurfaceStillReachesItsControl()
    {
        await Page.SetViewportSizeAsync(1440, 900);
        await OpenHomeAsync();

        (float x, float y) = await SpinWheelShortcutCentreAsync();

        await Page.Mouse.MoveAsync(x, y);
        await Page.Mouse.DownAsync();
        await Page.Mouse.MoveAsync(x + 1, y - 2);
        await Page.Mouse.UpAsync();

        await Expect(Page).ToHaveURLAsync(new Regex(@"/events/spin"), new() { Timeout = BootTimeoutMs });
    }

    /// <summary>
    /// A drag that ends over a control does not activate it, and the next click on the same control does.
    /// <para>
    /// Drag-to-scroll suppresses the click that follows a drag, so a scroll does not open a screen. It must
    /// suppress only that one click, or a later real click would be lost.
    /// </para>
    /// </summary>
    [Test]
    public async Task ADragThatEndsOverAControlDoesNotActivateIt()
    {
        await Page.SetViewportSizeAsync(1440, 900);
        await OpenHomeAsync();

        (float x, float y) = await SpinWheelShortcutCentreAsync();

        // Drag down and back, so the pointer is released over the control it was pressed on. Scrolling the
        // shortcut into view leaves Home at the end of its range, so only a downward drag moves the surface.
        // A drag that moves nothing counts as a press (wwwroot/drag-scroll.js).
        int before = await ScrollTopAsync();

        await Page.Mouse.MoveAsync(x, y);
        await Page.Mouse.DownAsync();
        for (int step = 1; step <= 6; step++)
            await Page.Mouse.MoveAsync(x, y + 10 * step);

        Assert.That(await ScrollTopAsync(), Is.LessThan(before),
            "the surface did not move, so this gesture was a press and the assertion below proves nothing");

        for (int step = 6; step >= 0; step--)
            await Page.Mouse.MoveAsync(x, y + 10 * step);
        await Page.Mouse.UpAsync();

        await Page.WaitForTimeoutAsync(500);
        Assert.That(RouteOf(Page.Url), Is.EqualTo("/"), "a drag that ended over a shortcut opened it");

        // Only one click was suppressed, so this click opens the shortcut.
        await Page.GetByTestId("shortcut-spinwheel").ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"/events/spin"), new() { Timeout = BootTimeoutMs });
    }

    /// <summary>
    /// The shell's scroll surface draws no scrollbar, and the mouse wheel still scrolls it.
    /// <para>
    /// The computed <c>scrollbar-width</c> is checked as well as the layout width, because headless Chromium draws
    /// overlay scrollbars that take no width. The wheel is sent after the live session arrives, because that
    /// re-render can reset the scroll, and the scroll is polled because wheel scrolling is animated.
    /// </para>
    /// </summary>
    [Test]
    public async Task TheShellsScrollSurfaceDrawsNoScrollbar()
    {
        await Page.SetViewportSizeAsync(1440, 900);
        await OpenHomeAsync();
        await WaitForLiveSessionAsync();

        int[] measured = await Page.EvaluateAsync<int[]>(
            "() => { const m = document.querySelector('.m-shell__main');" +
            " return [m.offsetWidth - m.clientWidth, m.scrollHeight - m.clientHeight]; }");

        Assert.That(measured[1], Is.GreaterThan(0),
            "Home fits on the screen here, so this test could not have seen a scrollbar either way.");
        Assert.That(measured[0], Is.Zero, $"a scrollbar takes {measured[0]}px out of the scroll surface");

        Assert.That(await Page.EvaluateAsync<string>(
                "() => getComputedStyle(document.querySelector('.m-shell__main')).scrollbarWidth"),
            Is.EqualTo("none"), "the scroll surface still asks for a scrollbar");

        // The surface must still scroll with the wheel.
        (float x, float y) = await ScrollSurfaceCentreAsync();
        await Page.Mouse.MoveAsync(x, y);
        await Page.Mouse.WheelAsync(0, 300);

        try
        {
            await Page.WaitForFunctionAsync(
                "() => document.querySelector('.m-shell__main').scrollTop > 0",
                null, new() { Timeout = 5000 });
        }
        catch (PlaywrightException)
        {
            // Ignore the timeout so that the assertion below reports the failure with its message.
        }

        Assert.That(await ScrollTopAsync(), Is.GreaterThan(0), "the wheel no longer scrolls the shell");
    }

    [Test]
    public async Task VisitingTheTableWithNoTableSendsThePlayerHome()
    {
        // A player who is not at a table and not searching is redirected from the table page to Home.
        await Page.GotoAsync(ClientUrl("/table"));

        await Expect(Page.GetByTestId("play-hero")).ToBeVisibleAsync(new() { Timeout = BootTimeoutMs });
        Assert.That(RouteOf(Page.Url), Is.EqualTo("/"));
    }
}
