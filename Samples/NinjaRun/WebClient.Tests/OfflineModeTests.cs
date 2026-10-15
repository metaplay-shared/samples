using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;
using System;
using System.Text.RegularExpressions;

namespace WebClient.Tests;

/// <summary>
/// E2E tests for offline mode, selected with <c>?env=offline</c>: the session runs against the in-process
/// offline server, against the built-in game config archive. Unlike <see cref="HomePageTests"/> these need
/// only the WebClient server running.
/// <para>
/// Offline state persists in IndexedDB, so these rely on each test getting a fresh browser context (which
/// Playwright's <c>PageTest</c> provides) to start from a clean player.
/// </para>
/// </summary>
[Parallelizable(ParallelScope.Self)]
[TestFixture]
public class OfflineModeTests : GamePageTest
{
    // The WebClient server to drive (see WebClient/Properties/launchSettings.json). Set WEBCLIENT_BASE_URL
    // to point a run at another instance, which is what a second checkout on an offset port needs.
    /// <summary>Offline practice on the map named in the published config.</summary>
    private static readonly string OfflineUrl =
        (Environment.GetEnvironmentVariable("WEBCLIENT_BASE_URL") ?? "http://localhost:5290").TrimEnd('/') + "/?env=offline";

    /// <summary>Long enough for a practice run with no input at all to reach the finish.</summary>
    private const int RunTimeoutMs = 90_000;

    [Test]
    public async Task OfflineMode_MenuTransition_CoversBothViewChanges()
    {
        await Page.GotoAsync(OfflineUrl);

        ILocator startRun = Page.GetByRole(AriaRole.Button, new() { Name = "Start run" });
        ILocator portraitArea = Page.GetByTestId("portrait-game-area");
        ILocator transition = Page.GetByTestId("screen-transition");
        await Expect(startRun).ToBeVisibleAsync(new() { Timeout = 30000 });

        string transitionPosition = await transition.EvaluateAsync<string>(
            "element => getComputedStyle(element).position");
        Assert.That(transitionPosition, Is.EqualTo("absolute"),
            "the race transition escaped the portrait game area");

        LocatorBoundingBoxResult? portraitBounds = await portraitArea.BoundingBoxAsync();
        LocatorBoundingBoxResult? transitionBounds = await transition.BoundingBoxAsync();
        Assert.That(portraitBounds, Is.Not.Null);
        Assert.That(transitionBounds, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(transitionBounds!.X, Is.EqualTo(portraitBounds!.X).Within(1));
            Assert.That(transitionBounds.Y, Is.EqualTo(portraitBounds.Y).Within(1));
            Assert.That(transitionBounds.Width, Is.EqualTo(portraitBounds.Width).Within(1));
            Assert.That(transitionBounds.Height, Is.EqualTo(portraitBounds.Height).Within(1));
        });

        await startRun.ClickAsync();
        await Expect(transition).ToHaveAttributeAsync("data-transition-state", "covering");

        await WaitForTransitionStateAsync("covered");
        await Expect(Page.GetByTestId("time-trial-canvas")).ToBeVisibleAsync(new() { Timeout = 15000 });
        await Expect(Page.GetByTestId("time-trial-canvas")).ToHaveAttributeAsync("data-opponent-count", "0");

        bool inputDuringReveal = await Page.EvaluateAsync<bool>(
            "async () => (await import('./js/timeTrialHost.js')).isInputEnabled()");
        Assert.That(inputDuringReveal, Is.False, "gameplay input was enabled before the race reveal finished");

        await Expect(transition).ToHaveAttributeAsync("data-transition-state", "revealing");

        await Expect(transition).ToHaveAttributeAsync("data-transition-state", "hidden");

        bool inputAfterReveal = await Page.EvaluateAsync<bool>(
            "async () => (await import('./js/timeTrialHost.js')).isInputEnabled()");
        Assert.That(inputAfterReveal, Is.True, "gameplay input was not handed over after the reveal");

        await Page.GetByTestId("exit-run").ClickAsync();
        await WaitForTransitionStateAsync("covered");
        await Expect(Page.GetByTestId("main-menu")).ToBeVisibleAsync();
        await Expect(transition).ToHaveAttributeAsync("data-transition-state", "hidden");
    }

    /// <summary>
    /// The view is swapped while the transition reports itself covered, so for every frame of that state the
    /// opaque body must reach past all four edges of the portrait area, or a strip of the outgoing menu shows
    /// down one side while the swap happens.
    /// </summary>
    [Test]
    public async Task OfflineMode_WhileCovered_TheOpaqueBodyReachesPastEveryEdge()
    {
        await Page.GotoAsync(OfflineUrl);

        ILocator startRun = Page.GetByRole(AriaRole.Button, new() { Name = "Start run" });
        await Expect(startRun).ToBeVisibleAsync(new() { Timeout = 30000 });

        double[] overhangs = await CaptureCoveredOverhangAsync(startRun);
        AssertFullyCovered(overhangs, "entering a race");

        await Expect(Page.GetByTestId("time-trial-canvas")).ToBeVisibleAsync(new() { Timeout = 15000 });
        await Expect(Page.GetByTestId("screen-transition")).ToHaveAttributeAsync("data-transition-state", "hidden");

        overhangs = await CaptureCoveredOverhangAsync(Page.GetByTestId("exit-run"));
        AssertFullyCovered(overhangs, "leaving a race");

        await Expect(Page.GetByTestId("main-menu")).ToBeVisibleAsync();
    }

    /// <summary>
    /// Click <paramref name="trigger"/> and sample, once per frame for as long as the transition reports
    /// itself covered, how far the area sticks out past the opaque body on its worst edge. Sampling happens
    /// in the page because the covered state is too short to poll across the wire.
    /// </summary>
    private async Task<double[]> CaptureCoveredOverhangAsync(ILocator trigger)
    {
        await trigger.EvaluateAsync(
            """
            button => {
                const area = document.querySelector('[data-testid="portrait-game-area"]');
                const transition = document.querySelector('[data-testid="screen-transition"]');
                const body = document.querySelector('[data-testid="screen-transition-body"]');
                window.__coverSamples = [];
                let sawCovered = false;
                window.__coverSampler = window.setInterval(() => {
                    if (transition.dataset.transitionState !== 'covered') {
                        if (sawCovered)
                            window.clearInterval(window.__coverSampler);
                        return;
                    }

                    sawCovered = true;
                    const areaBox = area.getBoundingClientRect();
                    const bodyBox = body.getBoundingClientRect();
                    window.__coverSamples.push(Math.max(
                        bodyBox.left - areaBox.left,
                        areaBox.right - bodyBox.right,
                        bodyBox.top - areaBox.top,
                        areaBox.bottom - bodyBox.bottom));
                }, 16);

                button.dispatchEvent(new MouseEvent('click', { bubbles: true }));
            }
            """);

        ILocator transition = Page.GetByTestId("screen-transition");
        await WaitForTransitionStateAsync("covered");
        await Expect(transition).ToHaveAttributeAsync("data-transition-state", "hidden", new() { Timeout = 15000 });

        return await Page.EvaluateAsync<double[]>(
            """
            () => {
                window.clearInterval(window.__coverSampler);
                return window.__coverSamples;
            }
            """);
    }

    private static void AssertFullyCovered(double[] overhangs, string description)
    {
        Assert.That(overhangs.Length, Is.GreaterThan(3), $"{description} did not sample the covered state");
        foreach (double overhang in overhangs)
        {
            Assert.That(overhang, Is.LessThanOrEqualTo(0.0),
                $"{description} left {overhang:0.#}px of the portrait area outside the cover");
        }
    }

    [Test]
    public async Task OfflineMode_StartsMuted_UntilThePlayerAsksForSound()
    {
        await Page.GotoAsync(OfflineUrl);

        // The button is named for what pressing it does, so "Unmute" is the muted state.
        ILocator muteToggle = Page.GetByTestId("mute-toggle");
        await Expect(muteToggle).ToBeVisibleAsync(new() { Timeout = 30000 });
        await Expect(muteToggle).ToHaveAttributeAsync("aria-label", "Unmute");

        bool mutedOnArrival = await Page.EvaluateAsync<bool>(
            "async () => (await import('./js/timeTrialAudio.js')).isMuted()");
        Assert.That(mutedOnArrival, Is.True, "a first visit did not start silent");

        await muteToggle.ClickAsync();
        await Expect(muteToggle).ToHaveAttributeAsync("aria-label", "Mute");

        bool mutedAfterToggle = await Page.EvaluateAsync<bool>(
            "async () => (await import('./js/timeTrialAudio.js')).isMuted()");
        Assert.That(mutedAfterToggle, Is.False, "the switch did not turn sound on");
    }

    /// <summary>
    /// One switch in one corner: the button the menu shows and the button over the stage occupy the same
    /// box, so opening a run never makes the player hunt for it.
    /// </summary>
    [Test]
    public async Task OfflineMode_MuteSwitch_KeepsItsCornerFromMenuIntoTheRun()
    {
        await Page.GotoAsync(OfflineUrl);

        ILocator muteToggle  = Page.GetByTestId("mute-toggle");
        ILocator startRun    = Page.GetByRole(AriaRole.Button, new() { Name = "Start run" });
        ILocator portraitArea = Page.GetByTestId("portrait-game-area");
        await Expect(startRun).ToBeVisibleAsync(new() { Timeout = 30000 });

        LocatorBoundingBoxResult? areaBox = await portraitArea.BoundingBoxAsync();
        LocatorBoundingBoxResult? menuBox = await muteToggle.BoundingBoxAsync();
        Assert.That(areaBox, Is.Not.Null);
        Assert.That(menuBox, Is.Not.Null);

        // Top right of the portrait area, not merely somewhere consistent.
        Assert.Multiple(() =>
        {
            Assert.That(menuBox!.X, Is.GreaterThan(areaBox!.X + areaBox.Width / 2),
                "the menu switch is not on the right of the portrait area");
            Assert.That(menuBox.Y, Is.LessThan(areaBox!.Y + areaBox.Height / 2),
                "the menu switch is not at the top of the portrait area");
        });

        await startRun.ClickAsync();
        await Expect(Page.GetByTestId("time-trial-canvas")).ToBeVisibleAsync(new() { Timeout = 15000 });
        await Expect(Page.GetByTestId("screen-transition")).ToHaveAttributeAsync("data-transition-state", "hidden");

        LocatorBoundingBoxResult? runBox = await muteToggle.BoundingBoxAsync();
        Assert.That(runBox, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(runBox!.X, Is.EqualTo(menuBox!.X).Within(0.5),
                "the switch moved horizontally when the run opened");
            Assert.That(runBox.Y, Is.EqualTo(menuBox!.Y).Within(0.5),
                "the switch moved vertically when the run opened");
            Assert.That(runBox.Width, Is.EqualTo(menuBox!.Width).Within(0.5));
            Assert.That(runBox.Height, Is.EqualTo(menuBox!.Height).Within(0.5));
        });
    }

    [Test]
    public async Task OfflineMode_RepeatedTransitionClicks_NeverReverseTheCurtain()
    {
        await Page.GotoAsync(OfflineUrl);

        ILocator startRun = Page.GetByRole(AriaRole.Button, new() { Name = "Start run" });
        ILocator transition = Page.GetByTestId("screen-transition");
        await Expect(startRun).ToBeVisibleAsync(new() { Timeout = 30000 });

        double[] startMotion = await CaptureRepeatedClickMotionAsync(startRun, transition);
        AssertMotionOnlyTravelsLeft(startMotion, "repeated Start clicks");
        await Expect(Page.GetByTestId("time-trial-canvas")).ToBeVisibleAsync(new() { Timeout = 15000 });

        ILocator exitRun = Page.GetByTestId("exit-run");
        double[] exitMotion = await CaptureRepeatedClickMotionAsync(exitRun, transition);
        AssertMotionOnlyTravelsLeft(exitMotion, "repeated Exit clicks");
        await Expect(Page.GetByTestId("main-menu")).ToBeVisibleAsync();
    }

    private async Task<double[]> CaptureRepeatedClickMotionAsync(ILocator trigger, ILocator transition)
    {
        await trigger.EvaluateAsync(
            """
            button => {
                const transition = document.querySelector('[data-testid="screen-transition"]');
                const train = document.querySelector('[data-testid="screen-transition-train"]');
                window.__transitionMotionSamples = [];
                let sawMotion = false;
                window.__transitionMotionSampler = window.setInterval(() => {
                    const state = transition.dataset.transitionState;
                    if (state !== 'hidden') {
                        sawMotion = true;
                        window.__transitionMotionSamples.push(train.getBoundingClientRect().x);
                    } else if (sawMotion) {
                        window.clearInterval(window.__transitionMotionSampler);
                    }
                }, 16);

                for (let click = 0; click < 4; click++)
                    button.dispatchEvent(new MouseEvent('click', { bubbles: true }));
            }
            """);

        await Expect(transition).ToHaveAttributeAsync("data-transition-state", "covering");
        await Expect(transition).ToHaveAttributeAsync("data-transition-state", "hidden", new() { Timeout = 15000 });
        return await Page.EvaluateAsync<double[]>(
            """
            () => {
                window.clearInterval(window.__transitionMotionSampler);
                return window.__transitionMotionSamples;
            }
            """);
    }

    private static void AssertMotionOnlyTravelsLeft(double[] samples, string triggerDescription)
    {
        Assert.That(samples.Length, Is.GreaterThan(20), $"{triggerDescription} did not capture the transition motion");
        for (int sampleIndex = 1; sampleIndex < samples.Length; sampleIndex++)
        {
            Assert.That(samples[sampleIndex], Is.LessThanOrEqualTo(samples[sampleIndex - 1] + 1.0),
                $"{triggerDescription} restarted or reversed the transition between samples {sampleIndex - 1} and {sampleIndex}");
        }
    }

    /// <summary>
    /// The menu is the title, the medallion and the run button. A fresh player has no Name Tag, no Skill and
    /// no Orbs, so the medallion carries no name plaque, an empty slot and an empty ring, and its label names
    /// no one. Until the first run is finished the medallion opens nothing and carries no charm; after it,
    /// the medallion opens and the charm reads zero, since practice pays nothing.
    /// </summary>
    [Test]
    public async Task OfflineMode_Menu_ShowsTheTitleTheMedallionAndTheRunButton()
    {
        await Page.GotoAsync(OfflineUrl);

        ILocator startRun = Page.GetByRole(AriaRole.Button, new() { Name = "Start run" });
        await Expect(startRun).ToBeVisibleAsync(new() { Timeout = 30000 });
        await Expect(startRun).ToBeEnabledAsync();

        await Expect(Page.GetByRole(AriaRole.Img, new() { Name = "Ninja Time Trial" })).ToBeVisibleAsync();

        ILocator medallion = Page.GetByRole(AriaRole.Button, new() { NameRegex = new Regex("^Customize ninja") });
        await Expect(medallion).ToBeVisibleAsync();
        await Expect(medallion).ToBeDisabledAsync();
        await Expect(medallion).ToHaveAttributeAsync("aria-label", "Customize ninja, after your first run.");

        await Expect(Page.GetByTestId("name-plaque")).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("player-name")).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("equipped-skill")).ToHaveAttributeAsync("data-skill-id", "");
        await Expect(Page.GetByTestId("equipped-skill").Locator("svg")).ToHaveCountAsync(0);

        ILocator balance = Page.GetByTestId("orb-balance");
        await Expect(balance).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("orbit-pearl")).ToHaveCountAsync(0);

        // The medallion sits between the title and the run button without covering either.
        LocatorBoundingBoxResult? medallionBox = await medallion.BoundingBoxAsync();
        LocatorBoundingBoxResult? startBox = await startRun.BoundingBoxAsync();
        LocatorBoundingBoxResult? titleBox = await Page.GetByRole(AriaRole.Img, new() { Name = "Ninja Time Trial" }).BoundingBoxAsync();
        Assert.That(medallionBox, Is.Not.Null);
        Assert.That(startBox, Is.Not.Null);
        Assert.That(titleBox, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(medallionBox!.Y, Is.GreaterThanOrEqualTo(titleBox!.Y + titleBox.Height - 1),
                "the medallion overlaps the title");
            Assert.That(medallionBox.Y + medallionBox.Height, Is.LessThanOrEqualTo(startBox!.Y),
                "the medallion overlaps the run button");
        });

        await CompleteAPracticeRunAsync();
        await Expect(medallion).ToBeEnabledAsync();
        await Expect(medallion).ToHaveAttributeAsync("aria-label", "Customize ninja. Equipped: none. 0 orbs.");
        await Expect(balance).ToHaveTextAsync("0");
        await Expect(balance).ToHaveAttributeAsync("data-orbs-in-flight", "0");
    }

    /// <summary>
    /// Pressing the medallion shrinks it in place: the press scale never replaces the transform that centres
    /// it, so it does not leap sideways while held or on release.
    /// </summary>
    [Test]
    public async Task OfflineMode_PressingTheMedallion_KeepsItCentred()
    {
        await Page.GotoAsync(OfflineUrl);
        await CompleteAPracticeRunAsync();

        ILocator medallion = Page.GetByTestId("open-skills");
        await Expect(medallion).ToBeEnabledAsync(new() { Timeout = 30000 });

        LocatorBoundingBoxResult? resting = await medallion.BoundingBoxAsync();
        Assert.That(resting, Is.Not.Null);
        float restingCentreX = resting!.X + resting.Width / 2;

        // Buttons spring between their resting and pressed scale, so each box is measured once the scale has
        // settled rather than in the frame the press or release lands in.
        await Page.Mouse.MoveAsync(restingCentreX, resting.Y + resting.Height / 2);
        await Page.Mouse.DownAsync();
        await WaitForMedallionScaleAsync(pressed: true);
        LocatorBoundingBoxResult? pressed = await medallion.BoundingBoxAsync();

        await Page.Mouse.UpAsync();
        await WaitForMedallionScaleAsync(pressed: false);
        LocatorBoundingBoxResult? released = await medallion.BoundingBoxAsync();

        Assert.That(pressed, Is.Not.Null);
        Assert.That(released, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(pressed!.Width, Is.LessThan(resting.Width), "the press did not shrink the medallion");
            Assert.That(pressed.X + pressed.Width / 2, Is.EqualTo(restingCentreX).Within(1),
                "the medallion moved sideways while pressed");
            Assert.That(released!.X + released.Width / 2, Is.EqualTo(restingCentreX).Within(1),
                "the medallion moved sideways on release");
        });
    }

    /// <summary>
    /// The medallion opens the customize view through the same curtain a run is entered through: the menu
    /// stays up while the curtain covers it, the customize view is swapped in behind the cover, and Back
    /// returns the same way. A fresh offline player has no Orbs and nothing bought, so the whole tree is shown
    /// with only the root open to buy, and the name field only shows the anonymous name.
    /// </summary>
    [Test]
    public async Task OfflineMode_TheMedallion_OpensTheCustomizeViewThroughTheCurtain()
    {
        await Page.GotoAsync(OfflineUrl);
        await CompleteAPracticeRunAsync();

        ILocator medallion = Page.GetByTestId("open-skills");
        ILocator transition = Page.GetByTestId("screen-transition");
        ILocator menu = Page.GetByTestId("main-menu");
        ILocator customize = Page.GetByTestId("customize-view");
        await Expect(medallion).ToBeEnabledAsync(new() { Timeout = 30000 });

        await medallion.ClickAsync();
        await Expect(transition).ToHaveAttributeAsync("data-transition-state", "covering");
        await Expect(menu).ToBeVisibleAsync();
        await WaitForTransitionStateAsync("covered");
        await Expect(transition).ToHaveAttributeAsync("data-transition-state", "hidden");
        await Expect(customize).ToBeVisibleAsync();
        await Expect(menu).ToHaveCountAsync(0);

        (int skillCount, string rootId) = ReadSkillSheet();
        ILocator nodes = Page.GetByTestId("skill-node");
        await Expect(nodes).ToHaveCountAsync(skillCount);
        for (int ndx = 0; ndx < skillCount; ndx++)
        {
            ILocator node = nodes.Nth(ndx);
            await Expect(node).ToBeVisibleAsync();
            string id = await node.GetAttributeAsync("data-skill-id") ?? "";
            string state = await node.GetAttributeAsync("data-skill-state") ?? "";
            if (id == rootId)
                Assert.That(state, Is.AnyOf("available", "unaffordable"), "the root is not open to buy");
            else
                Assert.That(state, Is.EqualTo("locked"), $"{id} is open before anything has been bought");
        }

        await Expect(Page.GetByTestId("customize-orb-balance")).ToHaveTextAsync("0");
        await Expect(Page.GetByTestId("customize-equipped")).ToHaveAttributeAsync("data-skill-id", "");

        ILocator name = Page.GetByTestId("ninja-name");
        await Expect(name).ToHaveValueAsync("Masked Ninja");
        await Expect(name).Not.ToBeEditableAsync();
        await Expect(Page.GetByTestId("name-locked")).ToBeVisibleAsync();

        // The card opens on the root, which an empty balance cannot buy.
        ILocator action = Page.GetByTestId("skill-action");
        await Expect(Page.Locator($"[data-testid='skill-node'][data-skill-id='{rootId}']")).ToHaveClassAsync(new Regex(@"\bselected\b"));
        await Expect(action).ToBeDisabledAsync();

        // Tapping a locked node selects it, and the card says it is locked: a drawn cross, named in words.
        ILocator locked = Page.Locator("[data-testid='skill-node'][data-skill-state='locked']").First;
        await locked.ClickAsync();
        await Expect(locked).ToHaveClassAsync(new Regex(@"\bselected\b"));
        await Expect(action.GetByTestId("skill-action-locked")).ToHaveCountAsync(1);
        await Expect(action).ToHaveTextAsync("");
        await Expect(action).ToHaveAttributeAsync("aria-label", new Regex(" is locked$"));
        await Expect(action).ToBeDisabledAsync();

        await Page.GetByRole(AriaRole.Button, new() { Name = "Back to main screen" }).ClickAsync();
        await Expect(transition).ToHaveAttributeAsync("data-transition-state", "covering");
        await Expect(customize).ToBeVisibleAsync();
        await WaitForTransitionStateAsync("covered");
        await Expect(transition).ToHaveAttributeAsync("data-transition-state", "hidden");
        await Expect(menu).ToBeVisibleAsync();
        await Expect(customize).ToHaveCountAsync(0);
        await Expect(medallion).ToBeEnabledAsync();
    }

    /// <summary>
    /// The detail card lists a Skill's trade one effect per line: a benefit and its stat word, then each
    /// penalty and its stat word, with the Skill's name under them. The Name Tag moves no stat, so its card
    /// asks for a name instead and lists nothing.
    /// </summary>
    [Test]
    public async Task OfflineMode_TheSkillCard_ListsEachEffectAsABenefitOrAPenalty()
    {
        await Page.GotoAsync(OfflineUrl);
        await CompleteAPracticeRunAsync();

        ILocator medallion = Page.GetByTestId("open-skills");
        ILocator customize = Page.GetByTestId("customize-view");
        await Expect(medallion).ToBeEnabledAsync(new() { Timeout = 30000 });
        await medallion.ClickAsync();
        await WaitForTransitionStateAsync("covered");
        await Expect(Page.GetByTestId("screen-transition")).ToHaveAttributeAsync("data-transition-state", "hidden");
        await Expect(customize).ToBeVisibleAsync();

        ILocator card = Page.GetByTestId("skill-detail");
        ILocator effects = card.GetByTestId("skill-effect");
        ILocator benefits = card.Locator("[data-testid='skill-effect'][data-direction='benefit']");
        ILocator penalties = card.Locator("[data-testid='skill-effect'][data-direction='penalty']");
        ILocator detailName = card.Locator(".skill-detail-name");

        // A tier-3 node: one benefit, one penalty.
        await Page.Locator("[data-testid='skill-node'][data-skill-id='swift-step']").ClickAsync();
        await Expect(effects).ToHaveCountAsync(2);
        await Expect(benefits).ToHaveCountAsync(1);
        await Expect(penalties).ToHaveCountAsync(1);
        await Expect(benefits).ToHaveTextAsync(new Regex(@"^\+\d+(\.\d)?% Speed$"));
        await Expect(penalties).ToHaveTextAsync(new Regex(@"^−\d+(\.\d)?% Recovery$"));
        await Expect(detailName).ToHaveTextAsync("Running Shoes");

        // A tier-4 node: one benefit, two penalties. The three lines fill the card, so the name gives way.
        await Page.Locator("[data-testid='skill-node'][data-skill-id='wind-stride']").ClickAsync();
        await Expect(effects).ToHaveCountAsync(3);
        await Expect(benefits).ToHaveCountAsync(1);
        await Expect(penalties).ToHaveCountAsync(2);
        await Expect(detailName).ToHaveCountAsync(0);

        // The root moves nothing.
        await Page.Locator("[data-testid='skill-node'][data-skill-id='name-tag']").ClickAsync();
        await Expect(card.GetByTestId("skill-detail-effect")).ToHaveTextAsync("Name your ninja");
        await Expect(effects).ToHaveCountAsync(0);
    }

    /// <summary>
    /// A practice run is raced against nobody, so no segment is placed and nothing is called out as the
    /// runner reaches each climb or the finish line.
    /// </summary>
    [Test]
    public async Task OfflineMode_APracticeRun_NeverCallsOutASegmentRank()
    {
        await Page.GotoAsync(OfflineUrl);

        ILocator startRun = Page.GetByRole(AriaRole.Button, new() { Name = "Start run" });
        await Expect(startRun).ToBeVisibleAsync(new() { Timeout = 30000 });
        await startRun.ClickAsync();
        await Expect(Page.GetByTestId("time-trial-canvas")).ToBeVisibleAsync(new() { Timeout = 15000 });

        await Page.EvaluateAsync(
            """
            () => {
                window.__sawSegmentRank = false;
                window.__segmentRankSampler = window.setInterval(() => {
                    if (document.querySelector('[data-testid="segment-rank"]'))
                        window.__sawSegmentRank = true;
                }, 16);
            }
            """);

        await Expect(Page.GetByTestId("run-status")).ToContainTextAsync("Practice", new() { Timeout = RunTimeoutMs });

        bool sawSegmentRank = await Page.EvaluateAsync<bool>(
            "() => { window.clearInterval(window.__segmentRankSampler); return window.__sawSegmentRank; }");
        Assert.That(sawSegmentRank, Is.False, "a practice run called out a segment rank");
    }

    /// <summary>
    /// Crossing the finish line does not stop the runner: it keeps running on past the line, under no input,
    /// for a beat before the score screen takes the run's place, and is never drawn standing still there.
    /// </summary>
    [Test]
    public async Task OfflineMode_AfterTheFinishLine_TheRunnerRunsOnBeforeTheScoreScreen()
    {
        await Page.GotoAsync(OfflineUrl);

        ILocator startRun = Page.GetByRole(AriaRole.Button, new() { Name = "Start run" });
        await Expect(startRun).ToBeVisibleAsync(new() { Timeout = 30000 });
        await startRun.ClickAsync();
        await Expect(Page.GetByTestId("time-trial-canvas")).ToBeVisibleAsync(new() { Timeout = 15000 });

        // The run-out is drawn frame by frame, so the page samples the canvas on every animation frame.
        await Page.EvaluateAsync(
            """
            () => {
                window.__runOut = [];
                window.__runOutSampling = true;
                const sample = () => {
                    if (!window.__runOutSampling)
                        return;
                    const canvas = document.querySelector('[data-testid="time-trial-canvas"]');
                    const result = document.querySelector('[data-testid="run-result"]');
                    if (canvas && !result && canvas.dataset.finished === 'true') {
                        const x = Number(canvas.dataset.runnerX);
                        const last = window.__runOut[window.__runOut.length - 1];
                        if (!last || last.x !== x)
                            window.__runOut.push({ x, vx: Number(canvas.dataset.runnerVx), mode: canvas.dataset.runnerMode ?? '' });
                    }
                    window.requestAnimationFrame(sample);
                };
                window.requestAnimationFrame(sample);
            }
            """);

        await Expect(Page.GetByTestId("run-result")).ToBeVisibleAsync(new() { Timeout = RunTimeoutMs });

        string sampled = await Page.EvaluateAsync<string>(
            "() => { window.__runOutSampling = false; return JSON.stringify(window.__runOut); }");
        RunOutSample[] frames = System.Text.Json.JsonSerializer.Deserialize<RunOutSample[]>(sampled)!;

        Assert.That(frames.Length, Is.GreaterThan(1), "no run-out was drawn after the finish line");
        Assert.That(frames[^1].x - frames[0].x, Is.GreaterThan(2.0),
            $"the runner covered only {frames[^1].x - frames[0].x:0.00} m past the finish line before the score screen");
        foreach (RunOutSample frame in frames)
        {
            Assert.That(frame.vx, Is.GreaterThan(0), $"the runner stood still past the finish line at x = {frame.x:0.00}");
            Assert.That(frame.mode, Is.Not.EqualTo("Finished"), $"the runner was drawn finished rather than running at x = {frame.x:0.00}");
        }
    }

    private sealed record RunOutSample(double x, double vx, string mode);

    /// <summary>
    /// A run opens on the run-in: the runner is already running, from behind the start line, with the button
    /// locked and the clock on step 0. Reaching the start torii starts the clock and hands the button over, and
    /// a press made during the run-in does nothing.
    /// </summary>
    [Test]
    public async Task OfflineMode_TheRunOpensWithARunInToTheStartTorii()
    {
        await Page.GotoAsync(OfflineUrl);

        ILocator startRun = Page.GetByRole(AriaRole.Button, new() { Name = "Start run" });
        await Expect(startRun).ToBeVisibleAsync(new() { Timeout = 30000 });
        await startRun.ClickAsync();
        ILocator canvas = Page.GetByTestId("time-trial-canvas");
        await Expect(canvas).ToBeVisibleAsync(new() { Timeout = 15000 });

        await WaitForSnapshotAsync("snapshot => snapshot.runIn === true && snapshot.x < 0");
        await Expect(canvas).ToHaveAttributeAsync("data-run-in", "true");
        await Expect(canvas).ToHaveAttributeAsync("data-input-locked", "true");
        bool runningIn = await Page.EvaluateAsync<bool>(
            """
            () => {
                const snapshot = window.__timeTrialHost.getLastSnapshot();
                return snapshot.runIn === true && snapshot.step === 0 && snapshot.elapsedMs === 0
                    && snapshot.inputLocked === true && snapshot.mode === 'Running' && snapshot.vx > 0;
            }
            """);
        Assert.That(runningIn, Is.True, "the run did not open running in to the start line on step 0, with the button locked");

        // A press once the curtain has gone, while the run-in still has the button, does nothing.
        await Expect(Page.GetByTestId("screen-transition")).ToHaveAttributeAsync("data-transition-state", "hidden");
        bool pressedDuringRunIn = await Page.EvaluateAsync<bool>(
            """
            () => {
                if (window.__timeTrialHost.getLastSnapshot().runIn !== true)
                    return false;
                window.__timeTrialHost.diagnosticPress();
                return true;
            }
            """);

        await WaitForSnapshotAsync("snapshot => snapshot.runIn === false && snapshot.step > 0");
        await Expect(canvas).ToHaveAttributeAsync("data-run-in", "false");
        bool started = await Page.EvaluateAsync<bool>(
            """
            () => {
                const snapshot = window.__timeTrialHost.getLastSnapshot();
                return snapshot.x >= 0 && snapshot.elapsedMs > 0 && snapshot.inputLocked === false;
            }
            """);
        Assert.That(started, Is.True, "the clock did not start at the start torii with the button handed over");

        if (pressedDuringRunIn)
        {
            string mode = await Page.EvaluateAsync<string>("() => window.__timeTrialHost.getLastSnapshot().mode");
            Assert.That(mode, Is.EqualTo("Running"), "a press made during the run-in reached the run");
            await Page.EvaluateAsync("() => window.__timeTrialHost.diagnosticRelease()");
        }
    }

    /// <summary>
    /// How many Skills the published sheet holds, and the id of its root, the one with no prerequisite. Read
    /// from <c>GameConfigSource/Skills.csv</c>, which the built-in archive offline mode plays on is built from.
    /// </summary>
    private static (int Count, string RootId) ReadSkillSheet()
    {
        DirectoryInfo? directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "GameConfigSource", "Skills.csv")))
            directory = directory.Parent;
        Assert.That(directory, Is.Not.Null, "GameConfigSource/Skills.csv was not found above the test directory");

        string[] lines = File.ReadAllLines(Path.Combine(directory!.FullName, "GameConfigSource", "Skills.csv"));
        string[] header = lines[0].Split(',');
        int prerequisiteColumn = Array.IndexOf(header, "Prerequisite");

        int count = 0;
        string rootId = "";
        for (int ndx = 1; ndx < lines.Length; ndx++)
        {
            string line = lines[ndx].Trim();
            if (line.Length == 0 || line.StartsWith("\"//", StringComparison.Ordinal) || line.StartsWith("//", StringComparison.Ordinal))
                continue;

            string[] cells = line.Split(',');
            count++;
            if (cells[prerequisiteColumn].Trim().Length == 0)
                rootId = cells[0].Trim();
        }

        return (count, rootId);
    }

    /// <summary>
    /// Play one practice run to the finish without pressing anything and dismiss its score screen: what a
    /// fresh offline player has to do before the medallion opens.
    /// </summary>
    /// <summary>
    /// Wait until the medallion's transform has settled: no transition running, and the scale below its resting
    /// one while pressed or back on it once released.
    /// </summary>
    private Task WaitForMedallionScaleAsync(bool pressed)
    {
        return Page.WaitForFunctionAsync(
            @"pressed => {
                const medallion = document.querySelector('[data-testid=open-skills]');
                if (medallion.getAnimations().length > 0)
                    return false;
                const scale = new DOMMatrixReadOnly(getComputedStyle(medallion).transform).a;
                const resting = parseFloat(getComputedStyle(medallion).getPropertyValue('--menu-shrine-scale')) || 1;
                return pressed ? scale < resting : scale === resting;
            }",
            pressed,
            new() { Timeout = 5000 });
    }

    private async Task CompleteAPracticeRunAsync()
    {
        ILocator startRun = Page.GetByRole(AriaRole.Button, new() { Name = "Start run" });
        await Expect(startRun).ToBeEnabledAsync(new() { Timeout = 30000 });
        await startRun.ClickAsync();
        await Expect(Page.GetByTestId("time-trial-canvas")).ToBeVisibleAsync(new() { Timeout = 15000 });

        await Expect(Page.GetByTestId("run-status")).ToContainTextAsync("Practice", new() { Timeout = RunTimeoutMs });
        await Expect(Page.GetByTestId("screen-transition")).ToHaveAttributeAsync("data-transition-state", "hidden");

        await Page.GetByTestId("run-result").ClickAsync();
        await Expect(Page.GetByTestId("main-menu")).ToBeVisibleAsync(new() { Timeout = 15000 });
        await Expect(Page.GetByTestId("screen-transition")).ToHaveAttributeAsync("data-transition-state", "hidden");
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

    /// <summary>
    /// Practice pays nothing, so dismissing its score screen flies no Orbs and leaves the balance where it
    /// was.
    /// </summary>
    [Test]
    public async Task OfflineMode_DismissingAPracticeScoreScreen_FliesNoOrbs()
    {
        await Page.GotoAsync(OfflineUrl);

        ILocator startRun = Page.GetByRole(AriaRole.Button, new() { Name = "Start run" });
        await Expect(startRun).ToBeVisibleAsync(new() { Timeout = 30000 });
        await startRun.ClickAsync();

        await Expect(Page.GetByTestId("run-status")).ToContainTextAsync("Practice", new() { Timeout = RunTimeoutMs });
        await Expect(Page.GetByTestId("screen-transition")).ToHaveAttributeAsync("data-transition-state", "hidden");

        await Page.EvaluateAsync(
            """
            () => {
                window.__sawFlyingOrb = false;
                window.__flyingOrbSampler = window.setInterval(() => {
                    if (document.querySelector('[data-testid="flying-orb"]'))
                        window.__sawFlyingOrb = true;
                }, 16);
            }
            """);

        await Page.GetByTestId("run-result").ClickAsync();
        await Expect(Page.GetByTestId("main-menu")).ToBeVisibleAsync(new() { Timeout = 15000 });
        await Expect(Page.GetByTestId("screen-transition")).ToHaveAttributeAsync("data-transition-state", "hidden");

        bool sawFlyingOrb = await Page.EvaluateAsync<bool>(
            "() => { window.clearInterval(window.__flyingOrbSampler); return window.__sawFlyingOrb; }");
        Assert.That(sawFlyingOrb, Is.False, "a practice run flew Orbs");

        ILocator balance = Page.GetByTestId("orb-balance");
        await Expect(balance).ToHaveTextAsync("0");
        await Expect(balance).ToHaveAttributeAsync("data-orbs-in-flight", "0");
    }

    [Test]
    public async Task OfflineMode_PracticeMapHangsTheWaterfallBridgeInItsFirstPart()
    {
        await Page.GotoAsync(OfflineUrl);

        ILocator startRun = Page.GetByRole(AriaRole.Button, new() { Name = "Start run" });
        await Expect(startRun).ToBeVisibleAsync(new() { Timeout = 30000 });
        await startRun.ClickAsync();

        // The practice map opens on the rr-crossriver part, whose bridge (38–52 m, the map's only one) the
        // composer hangs with the tuned deck and the waterfall under it.
        ILocator canvas = Page.GetByTestId("time-trial-canvas");
        await Expect(canvas).ToBeVisibleAsync(new() { Timeout = 15000 });
        await Expect(canvas).ToHaveAttributeAsync("data-drooping-bridge-count", "1");
        await Expect(canvas).ToHaveAttributeAsync("data-drooping-bridge-span", "14.000");
        await Expect(canvas).ToHaveAttributeAsync("data-bridge-waterfall-present", "true");
        await Expect(canvas).ToHaveAttributeAsync("data-bridge-visible-count", "1", new() { Timeout = 20000 });
    }

    [Test]
    public async Task OfflineMode_DroopingBridgeKeepsItsSagWithReducedMotion()
    {
        await Page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.Reduce });
        await Page.GotoAsync(OfflineUrl);
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Start run" })).ToBeVisibleAsync(new() { Timeout = 30000 });

        await Page.GetByRole(AriaRole.Button, new() { Name = "Start run" }).ClickAsync();
        ILocator canvas = Page.GetByTestId("time-trial-canvas");
        await Expect(canvas).ToBeVisibleAsync(new() { Timeout = 15000 });
        await Expect(canvas).ToHaveAttributeAsync("data-bridge-reduced-motion", "true");
        await Expect(canvas).ToHaveAttributeAsync("data-bridge-waterfall-present", "true");
        await Expect(canvas).ToHaveAttributeAsync("data-bridge-waterfall-reduced-motion", "true");
        await Expect(canvas).ToHaveAttributeAsync("data-bridge-waterfall-asset-ready", "true");
        await Expect(canvas).ToHaveAttributeAsync("data-bridge-waterfall-frame-index", "0");
        await Expect(canvas).ToHaveAttributeAsync("data-bridge-waterfall-frame-blend", "0.000");
        await Expect(canvas).ToHaveAttributeAsync("data-bridge-state", "loaded", new() { Timeout = 20000 });
        await Expect(canvas).ToHaveAttributeAsync(
            "data-bridge-center-deformation",
            new Regex(@"^(?!0\.000$)\d+\.\d{3}$"),
            new() { Timeout = 5000 });
        await AssertBridgeAnchorsStayPinnedAsync(canvas);
    }

    private static async Task AssertBridgeAnchorsStayPinnedAsync(ILocator canvas)
    {
        for (int attempt = 0; attempt < 50; attempt++)
        {
            bool anchored = await canvas.EvaluateAsync<bool>(
                """
                element => {
                    const endpoint = Number(element.dataset.bridgeEndpointDeformation);
                    const anchorZone = Number(element.dataset.bridgeAnchorZoneDeformation);
                    const centre = Number(element.dataset.bridgeCenterDeformation);
                    return endpoint === 0 && centre > 0 && anchorZone < centre * 0.34;
                }
                """);
            if (anchored)
                return;
            await Task.Delay(100);
        }

        Assert.Fail("bridge endpoints moved, or the bank-side logs were not substantially stiffer than the centre");
    }
}
