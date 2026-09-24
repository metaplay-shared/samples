using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

namespace WebClient.Tests;

/// <summary>
/// Measures three Core Web Vitals with the browser's own APIs. Only CLS is asserted, because it measures geometry:
/// a loaded host can delay a shift but not change its size. LCP and INP thresholds are field 75th-percentile
/// figures that one unthrottled, parallel run on <c>localhost</c> cannot sample, so they are only printed.
/// <para>
/// CLS excludes shifts within <see cref="InputShiftWindowMs"/> of a discrete input, so the tests do not see the
/// reflow of a sheet as it opens. They do see a later shift, such as content landing after its frame or an image
/// sized after it loads. <see cref="HubNavigationAndASheetStayWithinGoodCls"/> runs under <c>?env=offline</c>
/// (<c>docs/web-client.md</c>, "Offline mode"). A reward claim needs the live server.
/// </para>
/// </summary>
[TestFixture]
public class CoreWebVitalsPageTests : PlaywrightPageTest
{
    /// <summary>
    /// Collects the vitals into <c>window.__vitals</c>. Installed with <c>AddInitScriptAsync</c>, which runs it on
    /// every document before the page's own scripts. <c>buffered: true</c> on each observer also delivers the
    /// entries recorded before the observer was created, so the collection starts from the first paint.
    /// </summary>
    private const string CollectorScript = @"
        window.__vitals = { cls: 0, longestInteractionMs: 0, lcpMs: 0 };
        try {
          // Layout Instability. hadRecentInput is true for 500ms after any discrete input, and those entries
          // are dropped rather than summed — that exclusion is the metric's definition, not a choice made
          // here, and the class summary says what it costs this gate.
          new PerformanceObserver((list) => {
            for (const entry of list.getEntries())
              if (!entry.hadRecentInput)
                window.__vitals.cls += entry.value;
          }).observe({ type: 'layout-shift', buffered: true });
        } catch (e) {}
        try {
          // The Event Timing API is what INP is built on: a click, a key press or a pointerdown/up that
          // produced visible feedback, timed from the input to the next paint. durationThreshold: 16 is the
          // API's own floor (sub-frame interactions are not reported at all), so this sees everything the
          // real metric would.
          new PerformanceObserver((list) => {
            for (const entry of list.getEntries())
              if (entry.duration > window.__vitals.longestInteractionMs)
                window.__vitals.longestInteractionMs = entry.duration;
          }).observe({ type: 'event', buffered: true, durationThreshold: 16 });
        } catch (e) {}
        try {
          // Largest Contentful Paint. The browser reports a run of candidates as bigger elements paint and
          // stops at the first interaction, so the last entry is the one the metric means.
          new PerformanceObserver((list) => {
            for (const entry of list.getEntries())
              if (entry.startTime > window.__vitals.lcpMs)
                window.__vitals.lcpMs = entry.startTime;
          }).observe({ type: 'largest-contentful-paint', buffered: true });
        } catch (e) {}
    ";

    /// <summary>
    /// Zeroes CLS and the longest interaction, so the next reading covers only the phase that follows and not
    /// the boot.
    /// <para>
    /// LCP is not reset. It belongs to the page load, and the browser reports no new candidates after the first
    /// interaction, so resetting it would lose the only value.
    /// </para>
    /// </summary>
    private Task ResetVitalsAsync() => Page.EvaluateAsync("window.__vitals.cls = 0; window.__vitals.longestInteractionMs = 0;");

    /// <summary>
    /// How long after a discrete input the Layout Instability API sets <c>hadRecentInput</c> on a shift. The value
    /// is defined by the metric, not chosen by this test.
    /// </summary>
    private const int InputShiftWindowMs = 500;

    /// <summary>
    /// The quiet period each phase ends with. It is twice <see cref="InputShiftWindowMs"/>, so a shift that
    /// arrives after the input window is counted before the reading.
    /// </summary>
    private const int QuietMs = InputShiftWindowMs * 2;

    /// <summary>Waits <see cref="QuietMs"/> at the end of a phase, so late shifts are counted.</summary>
    private Task WaitForLateShiftsAsync() => Page.WaitForTimeoutAsync(QuietMs);

    /// <summary>
    /// A class with settable properties, not a positional record, because Playwright's JSON converter creates the
    /// result with <c>Activator.CreateInstance</c> and then sets properties. It cannot call a positional
    /// constructor.
    /// </summary>
    private sealed class VitalsSnapshot
    {
        public double Cls { get; set; }
        public double LongestInteractionMs { get; set; }
        public double LcpMs { get; set; }
    }

    private Task<VitalsSnapshot> ReadVitalsAsync() => Page.EvaluateAsync<VitalsSnapshot>(
        "() => ({ cls: window.__vitals.cls, longestInteractionMs: window.__vitals.longestInteractionMs, lcpMs: window.__vitals.lcpMs })");

    /// <summary>The upper bound of web.dev's "good" CLS band, which is also the design's target.</summary>
    private const double GoodCls = 0.1;

    /// <summary>
    /// Asserts that CLS is within <see cref="GoodCls"/> and prints LCP and the longest interaction. The class
    /// summary explains why only CLS is asserted.
    /// </summary>
    private void AssertClsGoodAndRecordTheRest(VitalsSnapshot vitals, string phaseName)
    {
        // A zero LCP means the browser reported no candidate. Printing it as 0ms would read as an instant paint.
        string lcp = vitals.LcpMs > 0 ? $"{vitals.LcpMs:0}ms" : "not reported";

        TestContext.Out.WriteLine($"{phaseName} — CLS: {vitals.Cls:0.0000} (good is <= {GoodCls:0.0}), " +
                                   $"longest interaction (INP proxy, recorded not gated): {vitals.LongestInteractionMs:0}ms, " +
                                   $"LCP for this page load (recorded not gated): {lcp}");

        Assert.That(vitals.Cls, Is.LessThanOrEqualTo(GoodCls), $"{phaseName} shifted layout enough to fail Google's 'good' CLS band");
    }

    /// <summary>Two navigations and opening and closing a sheet stay within <see cref="GoodCls"/>.</summary>
    [Test]
    public async Task HubNavigationAndASheetStayWithinGoodCls()
    {
        await Page.AddInitScriptAsync(CollectorScript);

        await Page.GotoAsync(ClientUrl("/events", "env=offline"));
        await WaitForLiveSessionAsync();
        await WaitForLateShiftsAsync(); // let the boot's shifts finish before the reset
        await ResetVitalsAsync();

        await Page.GetByTestId("feature-spinwheel-action").ClickAsync();
        await Expect(Page.GetByTestId("prize-details")).ToBeVisibleAsync(new() { Timeout = BootTimeoutMs });
        await WaitForLateShiftsAsync();

        await Page.GetByTestId("prize-details").ClickAsync();
        await Expect(Page.GetByTestId("odds-sheet")).ToBeVisibleAsync();
        await WaitForLateShiftsAsync();

        await Page.GetByTestId("sheet-close").ClickAsync();
        await Expect(Page.GetByTestId("odds-sheet")).ToHaveCountAsync(0);
        await WaitForLateShiftsAsync();

        await Page.GetByTestId("page-back").ClickAsync();
        await Expect(Page.GetByTestId("feature-dailyreward")).ToBeVisibleAsync(new() { Timeout = BootTimeoutMs });
        await WaitForLateShiftsAsync();

        AssertClsGoodAndRecordTheRest(await ReadVitalsAsync(), "Hub navigation and a sheet");
    }

    /// <summary>
    /// A daily reward claim, through the reward reveal and the balance animation that follows it, the longest
    /// animated sequence the shell draws. It needs the live server: the reveal leaves its granting stage only when
    /// the server's grant commits on this client's timeline, and offline mode has no handler for the claim request.
    /// It can run in parallel with other fixtures because it never taps PLAY, so it does not enter the matchmaking
    /// queue (see <see cref="LiveServerMatchmakingTests"/>), and each test gets a fresh guest account.
    /// </summary>
    [Test]
    public async Task LiveReward_AClaimedRewardStaysWithinGoodCls()
    {
        await Page.AddInitScriptAsync(CollectorScript);

        await Page.GotoAsync(ClientUrl("/events/daily"));
        await WaitForLiveSessionAsync();
        await Expect(Page.GetByTestId("daily-claim")).ToBeVisibleAsync(new() { Timeout = BootTimeoutMs });
        await WaitForLateShiftsAsync();
        await ResetVitalsAsync();

        await Page.GetByTestId("daily-claim").ClickAsync();

        // The reveal shows its granting placeholder from the tap. The Continue button appears only after the
        // server's grant has committed on this client, so this waits for the server.
        await Expect(Page.GetByTestId("reward-continue")).ToBeVisibleAsync(new() { Timeout = BootTimeoutMs });
        await WaitForLateShiftsAsync();

        await Page.GetByTestId("reward-continue").ClickAsync();
        await Expect(Page.GetByTestId("reward-reveal")).ToHaveCountAsync(0);
        // The balance digits count up to the new balance after the reveal closes (WalletBurstService). Wait until
        // every chip shows its real balance, data-amount, so the shifts of the count-up are all recorded.
        await Page.WaitForFunctionAsync(
            "() => { const chips = document.querySelectorAll('[data-testid^=\"balance-\"][data-amount]');" +
            " return chips.length > 0 && Array.from(chips).every(chip =>" +
            " (chip.querySelector('.m-balance__amount')?.textContent ?? '').replace(/\\D/g, '') === chip.dataset.amount); }",
            null, new() { Timeout = BootTimeoutMs });
        await WaitForLateShiftsAsync();

        AssertClsGoodAndRecordTheRest(await ReadVitalsAsync(), "A claimed reward");
    }
}
