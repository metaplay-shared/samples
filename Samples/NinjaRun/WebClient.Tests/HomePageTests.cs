using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;
using System.Globalization;
using System.Text.RegularExpressions;

namespace WebClient.Tests;

/// <summary>
/// E2E tests for the web client's menu and the time trial played from it. These drive the real client
/// against a real game server, so the accepted result at the end is one the server computed by running the
/// submitted recording itself.
/// <para>
/// Requires both the WebClient dev server and <c>metaplay dev server</c> to be running.
/// </para>
/// </summary>
[Parallelizable(ParallelScope.Self)]
[TestFixture]
public class HomePageTests : GamePageTest
{
    // The WebClient server to drive (see WebClient/Properties/launchSettings.json). Set WEBCLIENT_BASE_URL
    // to point a run at another instance, which is what a second checkout on an offset port needs.
    private static readonly string BaseUrl =
        (Environment.GetEnvironmentVariable("WEBCLIENT_BASE_URL") ?? "http://localhost:5290").TrimEnd('/');

    /// <summary>
    /// Long enough for a run with no input at all to finish a composed map (three parts plus the two climbs
    /// between them, falling into and climbing out of every pit on the way) and for the server to validate it.
    /// </summary>
    private const int RunTimeoutMs = 150_000;

    /// <summary>
    /// A run on screen is being stepped: the runner moves along the course. There is no running clock on
    /// screen to read, so the runner's own position is what is watched.
    /// </summary>
    [Test]
    public async Task StartingARun_ShowsTheCourseAndAdvancesTheRunner()
    {
        await GotoMenuAsync();
        await StartRunButton.ClickAsync();

        await Expect(Page.GetByTestId("time-trial-canvas")).ToBeVisibleAsync(new() { Timeout = 15000 });
        await Expect(Page.GetByTestId("screen-transition")).ToHaveAttributeAsync("data-transition-state", "hidden");

        double before = await ReadRunnerXAsync();
        await Page.WaitForTimeoutAsync(1500);
        double after = await ReadRunnerXAsync();

        Assert.That(after, Is.GreaterThan(before), "the runner did not advance");
    }

    [Test]
    public async Task StartingARun_RendersAndAdvancesTheServerProvidedGhosts()
    {
        // Whichever map the server draws: the lineup comes from that map's own pool.
        await GotoMenuAsync();
        await StartRunButton.ClickAsync();

        ILocator canvas = Page.GetByTestId("time-trial-canvas");
        await Expect(canvas).ToBeVisibleAsync(new() { Timeout = 15000 });

        // The canvas is in the document before the host has loaded its renderer and drawn a frame, and the
        // lineup is read off the drawn frame, so the first one is waited for.
        await WaitForSnapshotAsync("snapshot => true");

        // A fresh map fields its scripted runners, so the lineup is more than one opponent from the start.
        int ghostCount = await ReadGhostCountAsync();
        Assert.That(ghostCount, Is.GreaterThan(1), "the run was given only one opponent");

        // The opponents hold still through the run-in, so they are watched from the start line on.
        await WaitForSnapshotAsync("snapshot => snapshot.runIn === false && snapshot.step > 0");
        double before = await ReadGhostXAsync();
        string beforeText = before.ToString("R", CultureInfo.InvariantCulture);
        try
        {
            await WaitForSnapshotAsync($"snapshot => (snapshot.ghosts?.[0]?.x ?? -1) > {beforeText}");
        }
        catch (PlaywrightException)
        {
            Assert.Fail("the server-provided ghosts did not advance through their recordings");
        }

        Assert.That(await ReadGhostCountAsync(), Is.EqualTo(ghostCount), "the lineup changed size mid-run");
    }

    [Test]
    public async Task StartingAndLeavingARun_UsesTheWholeScreenCover()
    {
        await GotoMenuAsync();

        ILocator transition = Page.GetByTestId("screen-transition");
        await Expect(transition).ToHaveAttributeAsync("data-transition-state", "hidden");

        await StartRunButton.ClickAsync();

        // The outgoing menu remains in place while the foreground first crosses it. The view is only
        // replaced after the same layer reaches its fully covered state.
        await Expect(transition).ToHaveAttributeAsync("data-transition-state", "covering");
        await Expect(Page.GetByTestId("main-menu")).ToBeVisibleAsync();
        await WaitForTransitionStateAsync("covered");

        ILocator canvas = Page.GetByTestId("time-trial-canvas");
        await Expect(canvas).ToBeVisibleAsync(new() { Timeout = 15000 });
        await Expect(transition).ToHaveAttributeAsync("data-transition-state", "hidden");

        await Page.GetByTestId("exit-run").ClickAsync();
        await Expect(transition).ToHaveAttributeAsync("data-transition-state", "covering");
        await Expect(canvas).ToBeVisibleAsync();
        await WaitForTransitionStateAsync("covered");

        await Expect(Page.GetByTestId("main-menu")).ToBeVisibleAsync();
        await Expect(transition).ToHaveAttributeAsync("data-transition-state", "hidden");
        await Expect(StartRunButton).ToBeEnabledAsync();
    }

    /// <summary>
    /// Two runs in a row are two different maps: the server draws from the registry's active set and never
    /// hands back the map the player was just on, so pressing Start again is a new course rather than a retry.
    /// </summary>
    [Test]
    public async Task TwoConsecutiveRuns_AreOnDifferentMaps()
    {
        await GotoMenuAsync();

        string first = await StartRunAndReadMapIdAsync();
        await LeaveRunAsync();

        string second = await StartRunAndReadMapIdAsync();
        await LeaveRunAsync();

        Assert.That(first, Is.Not.Empty, "the run view did not say which map it was on");
        Assert.That(second, Is.Not.EqualTo(first), $"two consecutive runs were both on '{first}'");
    }

    [Test]
    public async Task ARunWithNoInput_IsAcceptedByTheServer()
    {
        await GotoMenuAsync();
        await StartRunButton.ClickAsync();
        await Expect(Page.GetByTestId("time-trial-canvas")).ToBeVisibleAsync(new() { Timeout = 15000 });

        // Nothing is pressed: every map finishes on its own, falling into the pits and stumbling through the
        // spikes. The score screen marks its gates provisional until the server has run the recording itself;
        // the gates losing that mark is the run being counted.
        await Expect(Page.GetByTestId("gate-symbols")).ToHaveAttributeAsync("data-provisional", "false", new() { Timeout = RunTimeoutMs });
        await Expect(Page.GetByTestId("placements-provisional")).Not.ToBeVisibleAsync();

        // The time on the player's own row of the field is the one the server computed from the recording.
        double finishTime = await ReadRunTimeAsync(Page.GetByTestId("player-finish-time"));
        Assert.That(finishTime, Is.GreaterThan(1.0), "the accepted run has no plausible finish time");

        // The score screen has no controls of its own. The player is fresh and so has no name, and a nameless
        // player's run has no standing to turn to: a press anywhere on the field goes back to the menu.
        await Expect(Page.GetByTestId("mute-toggle")).Not.ToBeVisibleAsync();
        await Expect(Page.GetByTestId("exit-run")).Not.ToBeVisibleAsync();
        await Expect(Page.GetByTestId("run-result")).ToHaveAttributeAsync("data-result-panel", "field");
        await Expect(Page.GetByTestId("standing-panel")).ToHaveCountAsync(0);
        await Page.GetByTestId("run-result").ClickAsync();
        await Expect(Page.GetByTestId("main-menu")).ToBeVisibleAsync(new() { Timeout = 15000 });
    }

    /// <summary>
    /// Falling into a pit costs time and nothing else: the runner lands on its floor, runs the bottom and
    /// mantles out at the far wall. A run that presses nothing takes every pit that way, so it is still a run
    /// the server accepts, and it comes home behind the field, whose scripted runners jump the same pits.
    /// </summary>
    [Test]
    public async Task ARunThatFallsIntoEveryPit_FinishesSlowerThanTheField()
    {
        await GotoMenuAsync();
        await StartRunButton.ClickAsync();

        ILocator canvas = Page.GetByTestId("time-trial-canvas");
        await Expect(canvas).ToBeVisibleAsync(new() { Timeout = 15000 });

        // The renderer writes the pit count onto the canvas once its module has the course, a moment after
        // the canvas itself is in the document.
        await Expect(canvas).ToHaveAttributeAsync("data-pit-count", new System.Text.RegularExpressions.Regex("^[0-9]+$"));
        int pitCount = int.Parse(
            await canvas.GetAttributeAsync("data-pit-count") ?? "0", CultureInfo.InvariantCulture);
        Assert.That(pitCount, Is.GreaterThan(0), "the map the server drew has no pit to fall into");

        // Each climb out is counted in the page: a mantle lasts well under a second, and the run is over long
        // before a wire round trip could catch every one of them.
        await Page.EvaluateAsync(
            """
            async () => {
                const host = await import('/js/timeTrialHost.js');
                window.__mantleCount = 0;
                let rising = false;
                window.__mantleSampler = window.setInterval(() => {
                    const snapshot = host.getLastSnapshot();
                    const isRising = snapshot !== null && snapshot.encounterPhase === 'ledge-rise';
                    if (isRising && !rising)
                        window.__mantleCount++;
                    rising = isRising;
                }, 16);
            }
            """);

        await Expect(Page.GetByTestId("gate-symbols")).ToHaveAttributeAsync("data-provisional", "false", new() { Timeout = RunTimeoutMs });
        await Expect(Page.GetByTestId("placements-provisional")).Not.ToBeVisibleAsync();

        int mantles = await Page.EvaluateAsync<int>(
            "() => { window.clearInterval(window.__mantleSampler); return window.__mantleCount; }");
        Assert.That(mantles, Is.GreaterThanOrEqualTo(pitCount),
            $"a run with no input climbed out {mantles} times on a map with {pitCount} pits");

        double playerTime = await ReadRunTimeAsync(Page.GetByTestId("player-finish-time"));
        ILocator opponentTimes = Page.GetByTestId("opponent-finish-time");
        int opponentCount = await opponentTimes.CountAsync();
        Assert.That(opponentCount, Is.GreaterThan(0), "the score screen listed no opponent to compare against");

        double fastestOpponentTime = double.MaxValue;
        for (int ndx = 0; ndx < opponentCount; ndx++)
            fastestOpponentTime = Math.Min(fastestOpponentTime, await ReadRunTimeAsync(opponentTimes.Nth(ndx)));

        Assert.That(playerTime, Is.GreaterThan(fastestOpponentTime),
            $"falling into all {pitCount} pits finished in {playerTime:0.000} s, "
            + $"which is no slower than the quickest of the {opponentCount} opponents at {fastestOpponentTime:0.000} s");
    }

    /// <summary>
    /// A named player's accepted run turns from the field to its standing among everyone's best on the course
    /// on the first press, with the run's own bar marked, and goes back to the menu on the second.
    /// </summary>
    [Test]
    public async Task ANamedPlayersAcceptedRun_TurnsToItsStandingBeforeTheMenu()
    {
        await GotoMenuAsync();

        // A nameless player's run comes back with no standing, so the first run is spent on a name.
        await CompleteARunAsync();
        await CompleteTheNameTagTutorialAsync();

        await StartRunButton.ClickAsync();
        await Expect(Page.GetByTestId("time-trial-canvas")).ToBeVisibleAsync(new() { Timeout = 15000 });
        await Expect(Page.GetByTestId("gate-symbols")).ToHaveAttributeAsync("data-provisional", "false", new() { Timeout = RunTimeoutMs });
        await Expect(Page.GetByTestId("screen-transition")).ToHaveAttributeAsync("data-transition-state", "hidden");

        ILocator result    = Page.GetByTestId("run-result");
        ILocator histogram = Page.GetByTestId("standing-histogram");
        await Expect(result).ToHaveAttributeAsync("data-result-panel", "field");
        await Expect(Page.GetByTestId("result-field")).ToBeVisibleAsync();
        await Expect(histogram).Not.ToBeVisibleAsync();

        await result.ClickAsync();
        await Expect(result).ToHaveAttributeAsync("data-result-panel", "standing");
        await Expect(histogram).ToBeVisibleAsync();
        await Expect(Page.Locator("[data-testid='standing-bar'][data-player='true']")).ToHaveCountAsync(1);
        await Expect(Page.Locator("[data-testid='standing-bar'][data-open-end='true']")).ToHaveCountAsync(2);
        await Expect(Page.GetByTestId("standing-marker")).ToBeVisibleAsync();

        // The gates stay put through the slide: the Orbs burst off their reward marks on the way out.
        await Expect(Page.GetByTestId("gate-symbols")).ToBeVisibleAsync();

        // A press while the panels are still trading places does nothing, so the second waits the slide out.
        await Expect(result).ToHaveAttributeAsync("data-result-sliding", "false");
        await result.ClickAsync();
        await Expect(Page.GetByTestId("main-menu")).ToBeVisibleAsync(new() { Timeout = 15000 });
        await Expect(Page.GetByTestId("screen-transition")).ToHaveAttributeAsync("data-transition-state", "hidden");
    }

    /// <summary>
    /// The Orbs an accepted run paid burst off the score screen's reward marks when it is dismissed, float over
    /// the curtain while the views swap, and are drawn into the medallion's orbit once the menu is uncovered.
    /// The charm counts each one only once it has landed, and the ring gains a pearl per Orb.
    /// <para>
    /// The player is fresh, so this is also their first run: until it is accepted the medallion opens nothing
    /// and carries no charm, and once it is the medallion opens and the charm appears.
    /// </para>
    /// </summary>
    [Test]
    public async Task AnAcceptedRun_FliesItsOrbsIntoTheMedallion()
    {
        await GotoMenuAsync();

        ILocator medallion = Page.GetByTestId("open-skills");
        ILocator balance   = Page.GetByTestId("orb-balance");
        await Expect(medallion).ToBeDisabledAsync();
        await Expect(medallion).ToHaveAttributeAsync("aria-label", "Customize ninja, after your first run.");
        await Expect(balance).ToHaveCountAsync(0);
        const int before = 0;

        await StartRunButton.ClickAsync();
        await Expect(Page.GetByTestId("time-trial-canvas")).ToBeVisibleAsync(new() { Timeout = 15000 });
        await Expect(Page.GetByTestId("gate-symbols")).ToHaveAttributeAsync("data-provisional", "false", new() { Timeout = RunTimeoutMs });

        int earned = 0;
        foreach (string reward in await Page.GetByTestId("gate-reward").AllInnerTextsAsync())
            earned += int.Parse(reward.Trim().TrimStart('+'), CultureInfo.InvariantCulture);

        // The curtain and the flight are both too quick to poll across the wire, so the page samples them.
        await Page.EvaluateAsync(
            """
            () => {
                const transition = document.querySelector('[data-testid="screen-transition"]');
                window.__orbFlight = { orbsOverCurtain: 0, inFlightOnMenu: 0, sawOrbs: 0, movedOverCurtain: false };
                const firstSeen = new Map();
                window.__orbFlightSampler = window.setInterval(() => {
                    const state = transition.dataset.transitionState;
                    const orbs = document.querySelectorAll('[data-testid="flying-orb"]').length;
                    const balance = document.querySelector('[data-testid="orb-balance"]');
                    window.__orbFlight.sawOrbs = Math.max(window.__orbFlight.sawOrbs, orbs);
                    if (state === 'covering' || state === 'covered') {
                        window.__orbFlight.orbsOverCurtain = Math.max(window.__orbFlight.orbsOverCurtain, orbs);
                        for (const orb of document.querySelectorAll('[data-testid="flying-orb"]')) {
                            const box = orb.getBoundingClientRect();
                            const first = firstSeen.get(orb.dataset.orbId);
                            if (!first)
                                firstSeen.set(orb.dataset.orbId, { x: box.x, y: box.y });
                            else if (Math.hypot(box.x - first.x, box.y - first.y) > 2)
                                window.__orbFlight.movedOverCurtain = true;
                        }
                    }
                    if (balance && state !== 'hidden')
                        window.__orbFlight.inFlightOnMenu = Math.max(window.__orbFlight.inFlightOnMenu, Number(balance.dataset.orbsInFlight));
                }, 16);
            }
            """);

        await Page.GetByTestId("run-result").ClickAsync();
        await Expect(Page.GetByTestId("main-menu")).ToBeVisibleAsync(new() { Timeout = 15000 });
        await Expect(Page.GetByTestId("screen-transition")).ToHaveAttributeAsync("data-transition-state", "hidden");

        await Expect(balance).ToHaveTextAsync((before + earned).ToString(CultureInfo.InvariantCulture), new() { Timeout = 15000 });
        await Expect(balance).ToHaveAttributeAsync("data-orbs-in-flight", "0");
        await Expect(Page.GetByTestId("flying-orb")).ToHaveCountAsync(0);
        await Expect(medallion).ToBeEnabledAsync();
        await Expect(medallion).ToHaveAttributeAsync("aria-label", new Regex(@"^Customize ninja\. Equipped: "));

        int orbsOverCurtain = await Page.EvaluateAsync<int>("() => window.__orbFlight.orbsOverCurtain");
        int inFlightOnMenu  = await Page.EvaluateAsync<int>("() => window.__orbFlight.inFlightOnMenu");
        bool movedOverCurtain = await Page.EvaluateAsync<bool>("() => window.__orbFlight.movedOverCurtain");
        int sawOrbs         = await Page.EvaluateAsync<int>(
            "() => { window.clearInterval(window.__orbFlightSampler); return window.__orbFlight.sawOrbs; }");

        if (earned > 0)
        {
            Assert.Multiple(() =>
            {
                Assert.That(sawOrbs, Is.EqualTo(earned), "the flight did not carry one Orb per Orb the gates paid");
                Assert.That(orbsOverCurtain, Is.EqualTo(earned), "the Orbs did not hold over the curtain while the views swapped");
                Assert.That(inFlightOnMenu, Is.GreaterThan(0), "the medallion counted the Orbs before they had flown in");
                Assert.That(movedOverCurtain, Is.True, "the Orbs did not burst and drift while the curtain covered the swap");
            });
        }
        else
        {
            Assert.That(sawOrbs, Is.EqualTo(0), "a run that paid nothing flew Orbs");
        }

        int settled = before + earned;
        await Expect(Page.GetByTestId("orbit-pearl")).ToHaveCountAsync(Math.Min(settled, 40));

        // Orbs that cover the Name Tag start its tutorial: the hand is at the medallion once they have landed,
        // and the run button is shut until the player is named.
        if (earned > 0)
        {
            await Expect(Page.GetByTestId("tutorial-pointer")).ToHaveAttributeAsync("data-tutorial-step", "open-medallion");
            await Expect(StartRunButton).ToBeDisabledAsync();
        }
    }

    /// <summary>
    /// The customize view sits on a history entry of its own, so the browser's back button returns to the
    /// menu through the same curtain as the view's Back button, and stays on the page. It does so in the
    /// middle of the Name Tag tutorial too, which leaves the player where the tutorial started: the hand at
    /// the medallion.
    /// </summary>
    [Test]
    public async Task BrowserBack_ClosesTheCustomizeView()
    {
        await GotoMenuAsync();
        await CompleteARunAsync();

        ILocator balance = Page.GetByTestId("orb-balance");
        await Expect(balance).ToHaveAttributeAsync("data-orbs-in-flight", "0", new() { Timeout = 15000 });
        bool inTutorial = (await balance.InnerTextAsync()).Trim() != "0";

        ILocator transition = Page.GetByTestId("screen-transition");
        await Page.GetByTestId("open-skills").ClickAsync();
        await Expect(Page.GetByTestId("customize-view")).ToBeVisibleAsync(new() { Timeout = 15000 });
        await Expect(transition).ToHaveAttributeAsync("data-transition-state", "hidden");

        await Page.GoBackAsync();
        await Expect(Page.GetByTestId("main-menu")).ToBeVisibleAsync(new() { Timeout = 15000 });
        await Expect(Page.GetByTestId("customize-view")).ToHaveCountAsync(0);
        await Expect(transition).ToHaveAttributeAsync("data-transition-state", "hidden");
        Assert.That(Page.Url.TrimEnd('/'), Is.EqualTo(BaseUrl), "browser back left the game page");

        if (inTutorial)
        {
            await Expect(Page.GetByTestId("tutorial-pointer")).ToHaveAttributeAsync("data-tutorial-step", "open-medallion");
            await Expect(StartRunButton).ToBeDisabledAsync();
        }
    }

    /// <summary>
    /// A run sits on a history entry of its own, so the browser's back button leaves it for the menu through
    /// the same curtain as the exit arrow, and stays on the page. The exit arrow is drawn, not written, and is
    /// named for what it does.
    /// </summary>
    [Test]
    public async Task BrowserBack_ExitsTheRun()
    {
        await GotoMenuAsync();
        await StartRunButton.ClickAsync();

        ILocator transition = Page.GetByTestId("screen-transition");
        await Expect(Page.GetByTestId("time-trial-canvas")).ToBeVisibleAsync(new() { Timeout = 15000 });
        await Expect(transition).ToHaveAttributeAsync("data-transition-state", "hidden");

        ILocator exitRun = Page.GetByTestId("exit-run");
        await Expect(exitRun).ToHaveAttributeAsync("aria-label", "Exit run");
        await Expect(exitRun.Locator("svg use")).ToHaveAttributeAsync("href", "#control-icon-exit-run");

        await Page.GoBackAsync();
        await Expect(Page.GetByTestId("main-menu")).ToBeVisibleAsync(new() { Timeout = 15000 });
        await Expect(Page.GetByTestId("time-trial-canvas")).ToHaveCountAsync(0);
        await Expect(transition).ToHaveAttributeAsync("data-transition-state", "hidden");
        Assert.That(Page.Url.TrimEnd('/'), Is.EqualTo(BaseUrl), "browser back left the game page");
    }

    /// <summary>
    /// The shared top bar is off screen while a run is up, so its blurred chips are not composited over the
    /// canvas, and it is back on the menu once the run is left.
    /// </summary>
    [Test]
    public async Task TopBar_IsHiddenDuringARun()
    {
        await GotoMenuAsync();
        ILocator connected = Page.GetByText("Connected", new() { Exact = true });
        await Expect(connected).ToBeVisibleAsync();

        await StartRunButton.ClickAsync();
        await Expect(Page.GetByTestId("time-trial-canvas")).ToBeVisibleAsync(new() { Timeout = 15000 });
        await Expect(connected).ToBeHiddenAsync();

        await Page.GetByTestId("exit-run").ClickAsync();
        await Expect(Page.GetByTestId("main-menu")).ToBeVisibleAsync(new() { Timeout = 15000 });
        await Expect(connected).ToBeVisibleAsync();
    }

    /// <summary>
    /// The score screen is still on the run's history entry, so the browser's back button dismisses it the
    /// same way a press on it does.
    /// </summary>
    [Test]
    public async Task BrowserBack_DismissesTheScoreScreen()
    {
        await GotoMenuAsync();
        await StartRunButton.ClickAsync();
        await Expect(Page.GetByTestId("time-trial-canvas")).ToBeVisibleAsync(new() { Timeout = 15000 });

        await Expect(Page.GetByTestId("run-result")).ToBeVisibleAsync(new() { Timeout = RunTimeoutMs });
        await Expect(Page.GetByTestId("screen-transition")).ToHaveAttributeAsync("data-transition-state", "hidden");

        await Page.GoBackAsync();
        await Expect(Page.GetByTestId("main-menu")).ToBeVisibleAsync(new() { Timeout = 15000 });
        await Expect(Page.GetByTestId("run-result")).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("screen-transition")).ToHaveAttributeAsync("data-transition-state", "hidden");
        Assert.That(Page.Url.TrimEnd('/'), Is.EqualTo(BaseUrl), "browser back left the game page");
    }

    /// <summary>Space on the main menu opens a run, the same as the Start run button.</summary>
    [Test]
    public async Task SpaceOnTheMenu_StartsARun()
    {
        await GotoMenuAsync();
        await Expect(Page.GetByTestId("screen-transition")).ToHaveAttributeAsync("data-transition-state", "hidden");

        // The window's keys are handed to the menu once the page has rendered it; the controls module writes
        // the mode it is in onto the document.
        await Expect(Page.Locator("html")).ToHaveAttributeAsync("data-home-key-mode", "menu", new() { Timeout = 15000 });
        await Page.Keyboard.PressAsync("Space");

        await Expect(Page.GetByTestId("time-trial-canvas")).ToBeVisibleAsync(new() { Timeout = 15000 });
    }

    /// <summary>
    /// Space dismisses the score screen without the screen having focus, the same as a press on it.
    /// </summary>
    [Test]
    public async Task SpaceOnTheScoreScreen_ReturnsToTheMenu()
    {
        await GotoMenuAsync();
        await StartRunButton.ClickAsync();
        await Expect(Page.GetByTestId("time-trial-canvas")).ToBeVisibleAsync(new() { Timeout = 15000 });

        await Expect(Page.GetByTestId("run-result")).ToBeVisibleAsync(new() { Timeout = RunTimeoutMs });
        await Expect(Page.GetByTestId("screen-transition")).ToHaveAttributeAsync("data-transition-state", "hidden");
        await Expect(Page.Locator("html")).ToHaveAttributeAsync("data-home-key-mode", "result");
        await Expect(Page.GetByTestId("run-result")).Not.ToBeFocusedAsync();

        await Page.Keyboard.PressAsync("Space");
        await Expect(Page.GetByTestId("main-menu")).ToBeVisibleAsync(new() { Timeout = 15000 });
    }

    /// <summary>
    /// The fullscreen switch sits directly under the mute switch, labelled for what pressing it does, and the
    /// pair stays in the same corner when the run opens. Headless Chromium may refuse fullscreen, so only the
    /// button itself is checked, not the switch.
    /// </summary>
    [Test]
    public async Task TheFullscreenToggle_SitsUnderTheMuteSwitch_InTheMenuAndTheRun()
    {
        await GotoMenuAsync();

        bool fullscreenEnabled = await Page.EvaluateAsync<bool>("() => document.fullscreenEnabled === true");
        if (!fullscreenEnabled)
            Assert.Ignore("this browser cannot go fullscreen, so the button is left out");

        ILocator toggle = Page.GetByTestId("fullscreen-toggle");
        ILocator mute = Page.GetByTestId("mute-toggle");
        await Expect(toggle).ToBeVisibleAsync();
        await Expect(mute).ToBeVisibleAsync();
        await Expect(toggle).ToHaveAttributeAsync("aria-label", "Fullscreen");
        (LocatorBoundingBoxResult menuMute, LocatorBoundingBoxResult menuToggle) = await ReadTogglePairAsync(mute, toggle);

        await StartRunButton.ClickAsync();
        await Expect(Page.GetByTestId("time-trial-canvas")).ToBeVisibleAsync(new() { Timeout = 15000 });
        await WaitForTransitionStateAsync("hidden");
        await Expect(mute).ToBeVisibleAsync();
        await Expect(toggle).ToBeVisibleAsync();
        (LocatorBoundingBoxResult runMute, LocatorBoundingBoxResult runToggle) = await ReadTogglePairAsync(mute, toggle);

        Assert.Multiple(() =>
        {
            Assert.That(runMute.X, Is.EqualTo(menuMute.X).Within(1), "the mute switch moved sideways when the run opened");
            Assert.That(runMute.Y, Is.EqualTo(menuMute.Y).Within(1), "the mute switch moved up or down when the run opened");
            Assert.That(runToggle.X, Is.EqualTo(menuToggle.X).Within(1), "the fullscreen switch moved sideways when the run opened");
            Assert.That(runToggle.Y, Is.EqualTo(menuToggle.Y).Within(1), "the fullscreen switch moved up or down when the run opened");
        });
    }

    /// <summary>
    /// The bounds of the mute and fullscreen switches, after checking the fullscreen switch sits directly under
    /// the mute switch: centred on it and below it without overlapping.
    /// </summary>
    private static async Task<(LocatorBoundingBoxResult Mute, LocatorBoundingBoxResult Toggle)> ReadTogglePairAsync(ILocator mute, ILocator toggle)
    {
        LocatorBoundingBoxResult? muteBox = await mute.BoundingBoxAsync();
        LocatorBoundingBoxResult? toggleBox = await toggle.BoundingBoxAsync();
        Assert.That(muteBox, Is.Not.Null);
        Assert.That(toggleBox, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(toggleBox!.X + toggleBox.Width / 2, Is.EqualTo(muteBox!.X + muteBox.Width / 2).Within(1),
                "the fullscreen switch is not centred under the mute switch");
            Assert.That(toggleBox.Y, Is.GreaterThanOrEqualTo(muteBox.Y + muteBox.Height),
                "the fullscreen switch is not below the mute switch");
        });
        return (muteBox!, toggleBox!);
    }

    /// <summary>
    /// Buying the Name Tag opens an empty name field that holds the player until the game accepts a name, and
    /// the accepted name is what the menu's plaque shows.
    /// </summary>
    [Test]
    public async Task BuyingTheNameTag_LetsThePlayerNameTheirNinja()
    {
        await GotoMenuAsync();
        await StartRunButton.ClickAsync();
        await Expect(Page.GetByTestId("time-trial-canvas")).ToBeVisibleAsync(new() { Timeout = 15000 });
        await Expect(Page.GetByTestId("gate-symbols")).ToHaveAttributeAsync("data-provisional", "false", new() { Timeout = RunTimeoutMs });
        await Page.GetByTestId("run-result").ClickAsync();
        await Expect(Page.GetByTestId("main-menu")).ToBeVisibleAsync(new() { Timeout = 15000 });

        ILocator transition = Page.GetByTestId("screen-transition");
        await Expect(transition).ToHaveAttributeAsync("data-transition-state", "hidden");
        await Page.GetByTestId("open-skills").ClickAsync();
        await Expect(Page.GetByTestId("customize-view")).ToBeVisibleAsync(new() { Timeout = 15000 });
        await Expect(transition).ToHaveAttributeAsync("data-transition-state", "hidden");

        ILocator name = Page.GetByTestId("ninja-name");
        if (await Page.GetByTestId("name-locked").CountAsync() == 0)
            Assert.Ignore("this player already owns the Name Tag");

        int balance = int.Parse((await Page.GetByTestId("customize-orb-balance").InnerTextAsync()).Trim(), CultureInfo.InvariantCulture);
        if (balance < 1)
            Assert.Ignore("the run paid no Orbs, so there is nothing to buy the Name Tag with");

        ILocator back = Page.GetByTestId("customize-back");
        ILocator root = Page.Locator("[data-testid='skill-node'][data-skill-id='name-tag']");
        await Expect(root).ToHaveAttributeAsync("data-skill-state", "available");
        await root.ClickAsync();
        ILocator action = Page.GetByTestId("skill-action");
        await Expect(action).ToBeEnabledAsync();
        await action.ClickAsync();

        await Expect(Page.GetByTestId("name-locked")).ToHaveCountAsync(0);
        await Expect(Page.Locator(".customize-name-note")).ToHaveCountAsync(0);
        await Expect(name).ToBeEditableAsync();
        await Expect(name).ToHaveValueAsync("");
        await Expect(name).ToBeFocusedAsync();
        await Expect(back).ToBeDisabledAsync();

        // An invalid name is refused in place and nothing is sent, so the tutorial still holds the view.
        await name.FillAsync("Masked Ninja");
        await name.PressAsync("Enter");
        await Expect(Page.GetByTestId("name-error")).ToBeVisibleAsync();
        await Expect(Page.GetByTestId("tutorial-pointer")).ToHaveAttributeAsync("data-tutorial-step", "name-ninja");
        await Expect(back).ToBeDisabledAsync();

        await name.FillAsync("  Test Runner  ");
        await name.PressAsync("Enter");
        await Expect(Page.GetByTestId("name-error")).ToHaveCountAsync(0);
        await Expect(name).ToHaveValueAsync("Test Runner");
        await Expect(Page.GetByTestId("tutorial-pointer")).ToHaveCountAsync(0);

        await back.ClickAsync();
        await Expect(Page.GetByTestId("main-menu")).ToBeVisibleAsync(new() { Timeout = 15000 });
        await Expect(Page.GetByTestId("player-name")).ToHaveTextAsync("Test Runner");
        await Expect(Page.GetByTestId("open-skills")).ToHaveAttributeAsync("aria-label", new Regex(@"^Customize ninja\. Name: Test Runner\. "));
    }

    /// <summary>
    /// The first Orbs are forced into a name: a pointing hand shows the one control to press while every other
    /// way on is shut, the tutorial survives a reload, and the menu is free again once a name is set.
    /// </summary>
    [Test]
    public async Task TheNameTagTutorial_ForcesTheFirstOrbsIntoAName()
    {
        await GotoMenuAsync();
        await CompleteARunAsync();

        ILocator balance = Page.GetByTestId("orb-balance");
        await Expect(balance).ToHaveAttributeAsync("data-orbs-in-flight", "0", new() { Timeout = 15000 });
        if ((await balance.InnerTextAsync()).Trim() == "0")
            Assert.Ignore("the run paid no Orbs, so there is nothing to buy the Name Tag with");

        ILocator pointer = Page.GetByTestId("tutorial-pointer");
        await ExpectTheMenuHeldByTheTutorialAsync(pointer);

        // Closing the game changes nothing: the step is read off the model the server keeps.
        await Page.ReloadAsync();
        await Expect(StartRunButton).ToBeVisibleAsync(new() { Timeout = 30000 });
        await ExpectTheMenuHeldByTheTutorialAsync(pointer);

        await Page.GetByTestId("open-skills").ClickAsync();
        await Expect(Page.GetByTestId("customize-view")).ToBeVisibleAsync(new() { Timeout = 15000 });
        await Expect(Page.GetByTestId("screen-transition")).ToHaveAttributeAsync("data-transition-state", "hidden");

        // The Name Tag's node first: nothing is selected for the player, and nothing else can be tapped.
        ILocator back   = Page.GetByTestId("customize-back");
        ILocator action = Page.GetByTestId("skill-action");
        await Expect(pointer).ToHaveAttributeAsync("data-tutorial-step", "select-name-tag");
        await Expect(back).ToBeDisabledAsync();
        await Expect(action).ToHaveCountAsync(0);

        ILocator others = Page.Locator("[data-testid='skill-node']:not([data-skill-id='name-tag'])");
        int otherCount = await others.CountAsync();
        Assert.That(otherCount, Is.GreaterThan(0), "the tree has nothing but the Name Tag");
        for (int ndx = 0; ndx < otherCount; ndx++)
            await Expect(others.Nth(ndx)).ToBeDisabledAsync();

        // Then its Unlock button.
        await Page.Locator("[data-testid='skill-node'][data-skill-id='name-tag']").ClickAsync();
        await Expect(pointer).ToHaveAttributeAsync("data-tutorial-step", "unlock-name-tag");
        await Expect(action).ToBeEnabledAsync();
        await Expect(back).ToBeDisabledAsync();
        await action.ClickAsync();

        // Then the name, which is the last step: once one is set, the tutorial is over.
        ILocator name = Page.GetByTestId("ninja-name");
        await Expect(pointer).ToHaveAttributeAsync("data-tutorial-step", "name-ninja");
        await Expect(name).ToBeFocusedAsync();
        await Expect(back).ToBeDisabledAsync();

        await name.FillAsync("Pointed Runner");
        await name.PressAsync("Enter");
        await Expect(pointer).ToHaveCountAsync(0);
        await Expect(back).ToBeEnabledAsync();

        await back.ClickAsync();
        await Expect(Page.GetByTestId("main-menu")).ToBeVisibleAsync(new() { Timeout = 15000 });
        await Expect(Page.GetByTestId("screen-transition")).ToHaveAttributeAsync("data-transition-state", "hidden");
        await Expect(pointer).ToHaveCountAsync(0);
        await Expect(StartRunButton).ToBeEnabledAsync();
        await Expect(Page.GetByTestId("player-name")).ToHaveTextAsync("Pointed Runner");
    }

    /// <summary>
    /// The menu as the Name Tag tutorial holds it: the hand at the medallion, the run button shut and Space
    /// doing nothing.
    /// </summary>
    private async Task ExpectTheMenuHeldByTheTutorialAsync(ILocator pointer)
    {
        await Expect(pointer).ToHaveAttributeAsync("data-tutorial-step", "open-medallion", new() { Timeout = 15000 });
        await Expect(StartRunButton).ToBeDisabledAsync();
        await Expect(Page.GetByTestId("open-skills")).ToBeEnabledAsync();

        // The controls module starts in the mode that ignores keys and only writes a mode down once it changes,
        // so a menu the tutorial holds from its first render may carry no mode at all. What matters is that it
        // is never handed the menu's.
        await Expect(Page.Locator("html")).Not.ToHaveAttributeAsync("data-home-key-mode", "menu");
        await Page.Locator("body").PressAsync("Space");
        await Page.WaitForTimeoutAsync(1000);
        await Expect(Page.GetByTestId("time-trial-canvas")).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("main-menu")).ToBeVisibleAsync();
    }

    /// <summary>
    /// The slot follows what is bought and chosen: buying a movement Skill puts it straight in the slot, and
    /// Equip on the card of another owned one switches to it.
    /// </summary>
    [Test]
    public async Task TheSlot_FollowsABoughtOrEquippedSkill()
    {
        await GotoMenuAsync();

        // Every movement Skill is bought behind the Name Tag, which the tutorial makes the first purchase.
        await CompleteARunAsync();
        await CompleteTheNameTagTutorialAsync();

        ILocator transition = Page.GetByTestId("screen-transition");
        await Expect(transition).ToHaveAttributeAsync("data-transition-state", "hidden");
        await Page.GetByTestId("open-skills").ClickAsync();
        await Expect(Page.GetByTestId("customize-view")).ToBeVisibleAsync(new() { Timeout = 15000 });
        await Expect(transition).ToHaveAttributeAsync("data-transition-state", "hidden");

        ILocator slot   = Page.GetByTestId("customize-equipped");
        ILocator action = Page.GetByTestId("skill-action");
        const string NameTagId = "name-tag";

        // Buying: the first open movement Skill the balance covers goes straight into the slot.
        ILocator available = Page.Locator($"[data-testid='skill-node'][data-skill-state='available']:not([data-skill-id='{NameTagId}'])");
        if (await available.CountAsync() > 0)
        {
            ILocator bought   = available.First;
            string   boughtId = await bought.GetAttributeAsync("data-skill-id") ?? "";
            await bought.ClickAsync();
            await action.ClickAsync();
            await Expect(bought).ToHaveAttributeAsync("data-skill-state", "equipped");
            await Expect(slot).ToHaveAttributeAsync("data-skill-id", boughtId, new() { Timeout = 2000 });
            await Expect(slot).ToHaveAttributeAsync("data-flashing", "false", new() { Timeout = 2000 });
        }

        // Equipping: any owned movement Skill that is not in the slot is switched in from its card.
        ILocator owned = Page.Locator($"[data-testid='skill-node'][data-skill-state='owned']:not([data-skill-id='{NameTagId}'])");
        if (await owned.CountAsync() == 0)
        {
            // With only the slot's own Skill owned, emptying the slot leaves it to be equipped again.
            ILocator equipped = Page.Locator("[data-testid='skill-node'][data-skill-state='equipped']");
            if (await equipped.CountAsync() == 0)
                Assert.Ignore("this player owns no movement Skill and cannot afford one");

            await equipped.First.ClickAsync();
            await action.ClickAsync();
            await Expect(slot).ToHaveAttributeAsync("data-skill-id", "", new() { Timeout = 2000 });
        }

        ILocator target   = owned.First;
        string   targetId = await target.GetAttributeAsync("data-skill-id") ?? "";
        await target.ClickAsync();
        await Expect(action).ToHaveAttributeAsync("aria-label", new Regex("^Equip "));
        await action.ClickAsync();

        await Expect(slot).ToHaveAttributeAsync("data-skill-id", targetId, new() { Timeout = 2000 });
        await Expect(slot).ToHaveAttributeAsync("data-flashing", "false", new() { Timeout = 2000 });
        await Expect(slot).ToHaveAttributeAsync("aria-label", new Regex("^Equipped: "));
        await Expect(slot.Locator("svg")).ToHaveCountAsync(1);
    }

    /// <summary>
    /// Each segment's rank is called out over the stage as the runner reaches the climb that closes it (the
    /// last one at the finish line) with the run time it closed at under it, and is gone again by the time
    /// the score screen carries the placements. The last segment's time is the run's finish time.
    /// </summary>
    [Test]
    public async Task ARun_CallsOutEachSegmentRankAtItsClimb()
    {
        await GotoMenuAsync();
        await StartRunButton.ClickAsync();
        await Expect(Page.GetByTestId("time-trial-canvas")).ToBeVisibleAsync(new() { Timeout = 15000 });

        // The callout stays up for SegmentRankCallout.razor's ShowMs at a moment the page chooses, so the page samples it.
        await Page.EvaluateAsync(
            """
            () => {
                window.__segmentRanks = [];
                window.__segmentRankSampler = window.setInterval(() => {
                    const rank = document.querySelector('[data-testid="segment-rank"]');
                    const time = rank?.parentElement?.querySelector('[data-testid="segment-time"]');
                    if (rank && !window.__segmentRanks.some(seen => seen.segment === rank.dataset.segment))
                        window.__segmentRanks.push({ segment: rank.dataset.segment, placement: rank.dataset.placement ?? '', text: rank.textContent, time: time?.textContent ?? '' });
                }, 16);
            }
            """);

        await Expect(Page.GetByTestId("run-result")).ToBeVisibleAsync(new() { Timeout = RunTimeoutMs });
        await Expect(Page.GetByTestId("segment-rank")).ToHaveCountAsync(0);

        string sampled = await Page.EvaluateAsync<string>(
            "() => { window.clearInterval(window.__segmentRankSampler); return JSON.stringify(window.__segmentRanks); }");
        SegmentRankSample[] ranks = System.Text.Json.JsonSerializer.Deserialize<SegmentRankSample[]>(sampled)!;
        Assert.That(ranks.Length, Is.GreaterThan(0), "no segment rank was called out during the run");
        foreach (SegmentRankSample rank in ranks)
        {
            Assert.That(rank.placement, Is.AnyOf("1", "2", "3", "4"), $"segment {rank.segment} was called out without a placement");
            Assert.That(rank.text, Is.AnyOf("1ST", "2ND", "3RD", "4TH"), $"segment {rank.segment} was called out as '{rank.text}'");
            Assert.That(rank.time, Does.Match(@"^(\d+:)?\d{2}\.\d{3}$"), $"segment {rank.segment} was called out with the time '{rank.time}'");
        }

        // The score screen has a gate per segment. Every one of them was called out during the run, the
        // one the finish line closes included.
        int segmentCount = await Page.GetByTestId("gate-symbol").CountAsync();
        Assert.That(segmentCount, Is.GreaterThan(0), "the score screen showed no gates");
        string[] calledOut = ranks.Select(rank => rank.segment).ToArray();
        for (int segment = 0; segment < segmentCount; segment++)
        {
            string label = segment.ToString(CultureInfo.InvariantCulture);
            string where = segment == segmentCount - 1 ? "at the finish line" : "at its climb";
            Assert.That(calledOut, Does.Contain(label), $"segment {segment} was not called out {where}");
        }

        string lastLabel = (segmentCount - 1).ToString(CultureInfo.InvariantCulture);
        string finishTime = (await Page.GetByTestId("player-finish-time").InnerTextAsync()).Trim();
        Assert.That(ranks.Single(rank => rank.segment == lastLabel).time, Is.EqualTo(finishTime),
            "the finish line's callout did not carry the run's finish time");
    }

    private sealed record SegmentRankSample(string segment, string placement, string text, string time);

    /// <summary>
    /// The field leaves at the foot of a climb in a poof and comes back at the player's gate under a warp-in.
    /// </summary>
    [Test]
    public async Task ARun_PoofsTheFieldAtAClimbAndWarpsItInAtTheGate()
    {
        await GotoMenuAsync();
        await StartRunButton.ClickAsync();
        await Expect(Page.GetByTestId("time-trial-canvas")).ToBeVisibleAsync(new() { Timeout = 15000 });

        // The canvas counts the effects it has started, which the page samples while the run is on the stage.
        await Page.EvaluateAsync(
            """
            () => {
                window.__gateEffects = { poofs: 0, warpIns: 0 };
                window.__gateEffectSampler = window.setInterval(() => {
                    const canvas = document.querySelector('[data-testid="time-trial-canvas"]');
                    if (!canvas)
                        return;
                    const seen = window.__gateEffects;
                    seen.poofs = Math.max(seen.poofs, Number(canvas.dataset.ghostPoofCount ?? 0));
                    seen.warpIns = Math.max(seen.warpIns, Number(canvas.dataset.ghostWarpInCount ?? 0));
                }, 16);
            }
            """);

        await Expect(Page.GetByTestId("run-result")).ToBeVisibleAsync(new() { Timeout = RunTimeoutMs });

        string sampled = await Page.EvaluateAsync<string>(
            "() => { window.clearInterval(window.__gateEffectSampler); return JSON.stringify(window.__gateEffects); }");
        GateEffectSample effects = System.Text.Json.JsonSerializer.Deserialize<GateEffectSample>(sampled)!;
        Assert.That(effects.poofs, Is.GreaterThan(0), "no opponent left in a poof at the foot of a climb");
        Assert.That(effects.warpIns, Is.GreaterThan(0), "the field never came back under a warp-in at a gate");
    }

    private sealed record GateEffectSample(int poofs, int warpIns);

    /// <summary>
    /// A developer's race on a chosen map is raced against that map's field and counts for nothing: the score
    /// screen says practice and shows no gates, and the Orb balance is where it was.
    /// </summary>
    [Test]
    public async Task ADeveloperRaceOnAChosenMap_IsRacedAndCountsForNothing()
    {
        await GotoMenuAsync();

        ILocator link = Page.GetByTestId("open-dev-courses");
        if (await link.CountAsync() == 0)
            Assert.Ignore("this server does not allow developer actions for a fresh guest");

        // A fresh player has completed no run, so the medallion carries no charm; a race that counts for
        // nothing must leave it that way.
        ILocator balance = Page.GetByTestId("orb-balance");
        string? orbsBefore = await balance.CountAsync() > 0 ? (await balance.InnerTextAsync()).Trim() : null;

        await link.ClickAsync();
        ILocator map = Page.GetByTestId("dev-map").First;
        await Expect(map).ToBeVisibleAsync(new() { Timeout = 15000 });
        string mapId = await map.GetAttributeAsync("data-map-id") ?? "";
        await map.ClickAsync();
        await Page.GetByTestId("dev-race").ClickAsync();

        ILocator canvas = Page.GetByTestId("time-trial-canvas");
        await Expect(canvas).ToBeVisibleAsync(new() { Timeout = 15000 });
        await Expect(canvas).ToHaveAttributeAsync("data-course-id", mapId);
        await Expect(canvas).ToHaveAttributeAsync("data-practice", "true");
        int opponents = int.Parse(await canvas.GetAttributeAsync("data-opponent-count") ?? "0", CultureInfo.InvariantCulture);
        Assert.That(opponents, Is.GreaterThan(0), "the server fielded nobody for the chosen map");

        await Expect(Page.GetByTestId("run-status")).ToContainTextAsync("Practice", new() { Timeout = RunTimeoutMs });
        await Expect(Page.GetByTestId("gate-symbols")).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("result-field-row")).ToHaveCountAsync(opponents + 1);
        await Expect(Page.GetByTestId("player-finish-time")).ToBeVisibleAsync();

        await Page.GetByTestId("run-result").ClickAsync();
        await Expect(Page.GetByTestId("dev-courses")).ToBeVisibleAsync(new() { Timeout = 15000 });
        await Page.GetByTestId("dev-back").ClickAsync();
        await Expect(StartRunButton).ToBeVisibleAsync(new() { Timeout = 15000 });
        if (orbsBefore == null)
        {
            await Expect(balance).ToHaveCountAsync(0);
            await Expect(Page.GetByTestId("open-skills")).ToBeDisabledAsync();
        }
        else
        {
            await Expect(balance).ToHaveTextAsync(orbsBefore);
        }
    }

    /// <summary>
    /// Wait for the screen transition to report <paramref name="state"/>. The fully covered beat is shorter
    /// than an assertion's retry interval, so this polls in the page on every animation frame.
    /// </summary>
    private Task WaitForTransitionStateAsync(string state)
        => Page.WaitForFunctionAsync(
            "state => document.querySelector('[data-testid=\"screen-transition\"]')?.dataset.transitionState === state",
            state,
            new() { Timeout = 15000 });

    private ILocator StartRunButton => Page.GetByRole(AriaRole.Button, new() { Name = "Start run" });

    /// <summary>Open a run and read the map the server put the player on off the run view.</summary>
    private async Task<string> StartRunAndReadMapIdAsync()
    {
        await Expect(StartRunButton).ToBeEnabledAsync(new() { Timeout = 15000 });
        await StartRunButton.ClickAsync();

        ILocator canvas = Page.GetByTestId("time-trial-canvas");
        await Expect(canvas).ToBeVisibleAsync(new() { Timeout = 15000 });
        await Expect(Page.GetByTestId("screen-transition")).ToHaveAttributeAsync("data-transition-state", "hidden");

        return await canvas.GetAttributeAsync("data-course-id") ?? "";
    }

    /// <summary>Walk out of a run without finishing it, back to the menu.</summary>
    private async Task LeaveRunAsync()
    {
        await Page.GetByTestId("exit-run").ClickAsync();
        await Expect(Page.GetByTestId("main-menu")).ToBeVisibleAsync(new() { Timeout = 15000 });
        await Expect(Page.GetByTestId("screen-transition")).ToHaveAttributeAsync("data-transition-state", "hidden");
    }

    /// <summary>
    /// Play one run to the finish without pressing anything, wait for the server to accept it, and dismiss the
    /// score screen: what a fresh player has to do before the medallion opens.
    /// </summary>
    private async Task CompleteARunAsync()
    {
        await Expect(StartRunButton).ToBeEnabledAsync(new() { Timeout = 15000 });
        await StartRunButton.ClickAsync();
        await Expect(Page.GetByTestId("time-trial-canvas")).ToBeVisibleAsync(new() { Timeout = 15000 });
        await Expect(Page.GetByTestId("gate-symbols")).ToHaveAttributeAsync("data-provisional", "false", new() { Timeout = RunTimeoutMs });
        await Expect(Page.GetByTestId("screen-transition")).ToHaveAttributeAsync("data-transition-state", "hidden");

        await Page.GetByTestId("run-result").ClickAsync();
        await Expect(Page.GetByTestId("main-menu")).ToBeVisibleAsync(new() { Timeout = 15000 });
        await Expect(Page.GetByTestId("screen-transition")).ToHaveAttributeAsync("data-transition-state", "hidden");
        await Expect(Page.GetByTestId("open-skills")).ToBeEnabledAsync();
    }

    /// <summary>
    /// Walk the Name Tag tutorial from the menu to its end, back on the menu with the run button open. Ignores
    /// the calling test when the run paid no Orbs, since that starts no tutorial.
    /// </summary>
    private async Task CompleteTheNameTagTutorialAsync(string playerName = "Test Runner")
    {
        ILocator balance = Page.GetByTestId("orb-balance");
        await Expect(balance).ToHaveAttributeAsync("data-orbs-in-flight", "0", new() { Timeout = 15000 });
        if ((await balance.InnerTextAsync()).Trim() == "0")
            Assert.Ignore("the run paid no Orbs, so there is no Name Tag to buy");

        ILocator pointer    = Page.GetByTestId("tutorial-pointer");
        ILocator transition = Page.GetByTestId("screen-transition");
        ILocator back       = Page.GetByTestId("customize-back");
        ILocator action     = Page.GetByTestId("skill-action");
        ILocator name       = Page.GetByTestId("ninja-name");

        await Expect(pointer).ToHaveAttributeAsync("data-tutorial-step", "open-medallion", new() { Timeout = 15000 });
        await Expect(StartRunButton).ToBeDisabledAsync();
        await Page.GetByTestId("open-skills").ClickAsync();
        await Expect(Page.GetByTestId("customize-view")).ToBeVisibleAsync(new() { Timeout = 15000 });
        await Expect(transition).ToHaveAttributeAsync("data-transition-state", "hidden");

        await Expect(pointer).ToHaveAttributeAsync("data-tutorial-step", "select-name-tag");
        await Expect(back).ToBeDisabledAsync();
        await Page.Locator("[data-testid='skill-node'][data-skill-id='name-tag']").ClickAsync();

        await Expect(pointer).ToHaveAttributeAsync("data-tutorial-step", "unlock-name-tag");
        await action.ClickAsync();

        await Expect(pointer).ToHaveAttributeAsync("data-tutorial-step", "name-ninja");
        await Expect(name).ToBeFocusedAsync();
        await name.FillAsync(playerName);
        await name.PressAsync("Enter");

        await Expect(pointer).ToHaveCountAsync(0);
        await Expect(back).ToBeEnabledAsync();
        await back.ClickAsync();

        await Expect(Page.GetByTestId("main-menu")).ToBeVisibleAsync(new() { Timeout = 15000 });
        await Expect(transition).ToHaveAttributeAsync("data-transition-state", "hidden");
        await Expect(StartRunButton).ToBeEnabledAsync();
    }

    /// <summary>Load the client and wait until the menu is up, which needs a session and a player model.</summary>
    private async Task GotoMenuAsync()
    {
        await Page.GotoAsync(BaseUrl);
        await Expect(StartRunButton).ToBeVisibleAsync(new() { Timeout = 30000 });
    }

    /// <summary>Read a run time off the page, in seconds. The page writes them as mm:ss.mmm or ss.mmm.</summary>
    private static async Task<double> ReadRunTimeAsync(ILocator locator)
    {
        string text = (await locator.InnerTextAsync()).Trim();
        string[] parts = text.Split(':');

        double seconds = double.Parse(parts[^1], CultureInfo.InvariantCulture);
        if (parts.Length > 1)
            seconds += int.Parse(parts[0], CultureInfo.InvariantCulture) * 60;

        return seconds;
    }

    /// <summary>How far the player's runner has got, in meters along the course.</summary>
    private async Task<double> ReadRunnerXAsync()
    {
        return await Page.EvaluateAsync<double>(
            "async () => (await import('/js/timeTrialHost.js')).getLastSnapshot()?.x ?? -1");
    }

    /// <summary>How far the first opponent in the lineup has got, in meters along the course.</summary>
    private async Task<double> ReadGhostXAsync()
    {
        return await Page.EvaluateAsync<double>(
            "async () => (await import('/js/timeTrialHost.js')).getLastSnapshot()?.ghosts?.[0]?.x ?? -1");
    }

    /// <summary>How many opponents the last drawn frame carries.</summary>
    private async Task<int> ReadGhostCountAsync()
    {
        return await Page.EvaluateAsync<int>(
            "async () => (await import('/js/timeTrialHost.js')).getLastSnapshot()?.ghosts?.length ?? 0");
    }
}
