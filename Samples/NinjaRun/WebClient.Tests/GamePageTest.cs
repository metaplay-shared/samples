using Microsoft.Playwright.NUnit;

namespace WebClient.Tests;

/// <summary>
/// The base of every browser test. Before any page of the test's context loads, it sets
/// <c>globalThis.hsrDiagnostics</c>, which is what makes the run's frame loop and renderer
/// (<c>wwwroot/js/timeTrialHost.js</c>, <c>timeTrialRenderer.js</c> and its modules under <c>js/renderer/</c>) publish their state on the canvas as
/// data attributes. A page a player opens has no flag, so its frames write none of them.
/// </summary>
public abstract class GamePageTest : PageTest
{
    [SetUp]
    public async Task EnableDiagnosticsAsync()
    {
        await Context.AddInitScriptAsync("globalThis.hsrDiagnostics = true;");
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
