using System.Globalization;
using System.Reflection;

namespace WebClient.Tests;

/// <summary>
/// The base of this game's browser fixtures: <see cref="GamePageTest"/> with the run's own test hooks. Before any
/// page of the test's context loads, it sets <c>globalThis.hsrDiagnostics</c>, which is what makes the run's frame
/// loop and renderer (<c>wwwroot/js/timeTrialHost.js</c>, <c>timeTrialRenderer.js</c> and its modules under
/// <c>js/renderer/</c>) publish their state on the canvas as data attributes. A page a player opens has no flag, so
/// its frames write none of them.
/// <para>
/// A test or fixture marked <see cref="RunTimeScaleAttribute"/> also plays its runs faster than real time (see
/// <see cref="ApplyRunTimeScaleAsync"/>).
/// </para>
/// </summary>
public abstract class NinjaRunPageTest : GamePageTest
{
    [SetUp]
    public async Task EnableDiagnosticsAsync()
    {
        await Context.AddInitScriptAsync("globalThis.hsrDiagnostics = true;");
    }

    /// <summary>
    /// Play the test's runs at the speed its <see cref="RunTimeScaleAttribute"/> names, the test's own over its
    /// fixture's, before any page of the context loads. The scale is the renderer's slow-motion knob,
    /// <c>hsrRunnerTuning.timeScale</c> (<c>wwwroot/js/renderer/runnerAnimation.js</c>), which the run's host
    /// multiplies its clock by; the client caps it at 4. The run steps through the same fixed simulation steps
    /// at any scale, only more of them per frame: 4 at 60 frames a second, inside the 6 a frame may catch up on
    /// (<c>TimeTrial.MaxCatchUpSteps</c>), and a slower frame drops its surplus wall clock, which only slows the
    /// run down again. Its recording and the time the server computes from it are therefore the ones a
    /// real-time run would have, and the server, which replays the recording, holds a run to no wall clock
    /// but its ticket's lifetime. What runs on the page's own timers rather than the run's clock (the curtain,
    /// the callouts, the run-out's wait before the score screen) keeps real time.
    /// </summary>
    [SetUp]
    public async Task ApplyRunTimeScaleAsync()
    {
        MethodInfo? method = GetType().GetMethod(TestContext.CurrentContext.Test.MethodName ?? "");
        RunTimeScaleAttribute? scale = method?.GetCustomAttribute<RunTimeScaleAttribute>()
            ?? GetType().GetCustomAttribute<RunTimeScaleAttribute>();
        if (scale == null || scale.Scale == 1)
            return;

        // runnerAnimation.js puts its tuning object on globalThis when the renderer is first imported, at the
        // first run. The accessor takes that assignment, sets the scale on the object and leaves it in place
        // as a plain property, so the console's knob works as usual.
        await Context.AddInitScriptAsync(string.Create(CultureInfo.InvariantCulture,
            $$"""
            Object.defineProperty(globalThis, 'hsrRunnerTuning', {
                configurable: true,
                get: () => undefined,
                set: tuning => {
                    tuning.timeScale = {{scale.Scale}};
                    Object.defineProperty(globalThis, 'hsrRunnerTuning', { value: tuning, writable: true, configurable: true, enumerable: true });
                },
            });
            """));
    }

    /// <summary>
    /// Drop the run in progress back to real time the moment the runner crosses the finish line, for a test
    /// that watches the run-out past it. The run-out is drawn for a fixed number of simulation steps at most,
    /// and the score screen's curtain waits a fixed time on the page's clock, so a scaled run-out would be
    /// over before the curtain started. A page-side frame loop watches for the crossing, which no poll across
    /// the wire could catch in time, so at most one frame of the run-out is scaled.
    /// </summary>
    protected async Task ReturnToRealTimeAtTheFinishLineAsync()
    {
        await Page.EvaluateAsync(
            """
            () => {
                const watch = () => {
                    const canvas = document.querySelector('[data-testid="time-trial-canvas"]');
                    if (canvas && canvas.dataset.finished === 'true' && globalThis.hsrRunnerTuning) {
                        globalThis.hsrRunnerTuning.timeScale = 1;
                        return;
                    }
                    window.requestAnimationFrame(watch);
                };
                window.requestAnimationFrame(watch);
            }
            """);
    }

    /// <summary>
    /// Wait until the host's most recent frame satisfies <paramref name="predicate"/>, a JavaScript arrow
    /// function of one snapshot.
    /// </summary>
    protected async Task WaitForSnapshotAsync(string predicate)
    {
        // The poller does not await what the polled function returns, and a promise is truthy, so the host
        // module is pulled onto window first to keep the polled function synchronous.
        await Page.EvaluateAsync(
            "async () => { window.__timeTrialHost = window.__timeTrialHost ?? await import('/js/timeTrialHost.js'); }");

        await Page.WaitForFunctionAsync(
            $"() => {{ const snapshot = window.__timeTrialHost.getLastSnapshot(); return snapshot != null && ({predicate})(snapshot); }}",
            null,
            new() { Timeout = 15000 });
    }
}

/// <summary>
/// How fast the runs of a browser test play, as a multiple of real time, applied by
/// <see cref="NinjaRunPageTest.ApplyRunTimeScaleAsync"/>. On a fixture it sets every test's speed; on a test it
/// overrides the fixture's, and <c>[RunTimeScale(1)]</c> keeps a test in real time. A test that plays a run to
/// the finish line runs faster; a test that times something on the run's clock against the page's own (the
/// run-in against the curtain, the run-out against the score screen) or watches a run only briefly keeps
/// real time.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true)]
public sealed class RunTimeScaleAttribute : Attribute
{
    /// <summary>The client's own cap on <c>hsrRunnerTuning.timeScale</c>.</summary>
    public const int Fastest = 4;

    public int Scale { get; }

    public RunTimeScaleAttribute(int scale) { Scale = scale; }
}
