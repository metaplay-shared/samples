using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;
using System;
using System.Text.RegularExpressions;

namespace WebClient.Tests;

/// <summary>
/// E2E tests for the wait on a run's art behind the menu transition, driven through <c>?env=offline</c> so they
/// need only the web client. While the curtain is covered, the run's renderer loads every sheet the course
/// draws from, and the reveal starts only once each has arrived and been decoded or failed to load. The canvas'
/// <c>data-art-ready</c> attribute says when that is, and the curtain carries a loading notice while it waits.
/// The art responses are held back or refused with a Playwright route to make the wait observable.
/// </summary>
[Parallelizable(ParallelScope.Self)]
[TestFixture]
public class RunArtLoadingOfflineModeTests : GamePageTest
{
    private static readonly string BaseUrl =
        (Environment.GetEnvironmentVariable("WEBCLIENT_BASE_URL") ?? "http://localhost:5290").TrimEnd('/');

    private static readonly string OfflineUrl = BaseUrl + "/?env=offline";

    /// <summary>How long each held-back art response is delayed, which is long enough to see the wait.</summary>
    private const int ArtDelayMs = 1500;

    /// <summary>
    /// With every art response held back, the curtain stays covered and shows the loading notice with a
    /// settled / total count while the canvas reports its art not ready. The reveal starts only once the art
    /// is ready and the runner sheet decoded, and the notice is gone by then.
    /// </summary>
    [Test]
    public async Task TheReveal_WaitsForTheCourseArt_AndTheCurtainSaysSo()
    {
        await Page.GotoAsync(OfflineUrl);

        ILocator startRun = Page.GetByRole(AriaRole.Button, new() { Name = "Start run" });
        await Expect(startRun).ToBeVisibleAsync(new() { Timeout = 30000 });

        // Registered once the menu is up, so only the run's art is held back and not the menu's own. The
        // served sheets carry a content hash in their names, so they are matched by extension alone.
        await Page.RouteAsync("**/art/*.webp", async route =>
        {
            await Task.Delay(ArtDelayMs);
            await route.ContinueAsync();
        });

        await startRun.ClickAsync();
        await WaitForTransitionStateAsync("covered");

        ILocator canvas = Page.GetByTestId("time-trial-canvas");
        ILocator loading = Page.GetByTestId("screen-transition").GetByTestId("run-loading");
        ILocator loadingCount = Page.GetByTestId("run-loading-count");

        // The notice fades in only after a delay, but it is in the DOM for the whole wait, which is what is
        // asserted here.
        await Expect(loading).ToBeVisibleAsync();
        await Expect(loadingCount).ToHaveTextAsync(new Regex(@"^\d+ / \d+$"));
        await Expect(canvas).ToHaveAttributeAsync("data-art-ready", "false");

        await WaitForTransitionStateAsync("revealing");

        await Expect(canvas).ToHaveAttributeAsync("data-art-ready", "true");
        await Expect(canvas).ToHaveAttributeAsync("data-runner-sheet-ready", "true");
        int noticesLeft = await Page.GetByTestId("run-loading").CountAsync();
        Assert.That(noticesLeft, Is.EqualTo(0), "the loading notice was still on the curtain when the reveal started");

        await WaitForTransitionStateAsync("hidden");
    }

    /// <summary>
    /// A sheet whose request fails settles at once and is not waited for: the art is reported ready and the
    /// curtain reveals the run.
    /// </summary>
    [Test]
    public async Task ASheetThatNeverArrives_DoesNotHoldTheRun()
    {
        await Page.GotoAsync(OfflineUrl);

        ILocator startRun = Page.GetByRole(AriaRole.Button, new() { Name = "Start run" });
        await Expect(startRun).ToBeVisibleAsync(new() { Timeout = 30000 });

        await Page.RouteAsync("**/art/course-hazards-and-pits.*.webp", route => route.AbortAsync());

        await startRun.ClickAsync();

        ILocator canvas = Page.GetByTestId("time-trial-canvas");
        await Expect(canvas).ToBeVisibleAsync(new() { Timeout = 15000 });
        await Expect(canvas).ToHaveAttributeAsync("data-art-ready", "true", new() { Timeout = 15000 });
        await Expect(Page.GetByTestId("screen-transition"))
            .ToHaveAttributeAsync("data-transition-state", "hidden", new() { Timeout = 15000 });
    }

    /// <summary>
    /// Wait for the screen transition to report <paramref name="state"/>. The fully covered beat can be shorter
    /// than an assertion's retry interval, so this polls in the page on every animation frame.
    /// </summary>
    private Task WaitForTransitionStateAsync(string state)
        => Page.WaitForFunctionAsync(
            "state => document.querySelector('[data-testid=\"screen-transition\"]')?.dataset.transitionState === state",
            state,
            new() { Timeout = 15000 });
}
