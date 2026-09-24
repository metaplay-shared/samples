using Microsoft.Playwright;
using NUnit.Framework;

namespace WebClient.Tests;

/// <summary>
/// Tests the automatic reload after a deploy (<c>WebClient/wwwroot/client-update.js</c>). The page's build ID comes
/// from the <c>updateBuildId</c> URL parameter and the deployed build ID from a routed <c>build-info.json</c>, so
/// these tests run in offline mode against the web client alone.
/// <para>
/// A reload shows as a second main-frame document request. The first request is the test's own navigation.
/// </para>
/// </summary>
[TestFixture]
public class ClientUpdatePageTests : PlaywrightPageTest
{
    private const string ThisBuild = "build-a";
    private const string NewBuild  = "build-b";

    /// <summary>The check interval the tests set, short so that they do not wait for the production interval.</summary>
    private const int UpdateCheckMs = 250;

    /// <summary>The idle time the tests set, short so that they do not wait for the production idle time.</summary>
    private const int UpdateIdleMs = 500;

    /// <summary>
    /// How often <c>client-update.js</c> retries a pending reload (<c>SAFE_MOMENT_POLL_MS</c>). It has no knob, so
    /// the value is copied here. Change it here when the script changes.
    /// </summary>
    private const int SafeMomentPollMs = 250;

    private static readonly string FastUpdateIntervals = $"updateCheckMs={UpdateCheckMs}&updateIdleMs={UpdateIdleMs}";

    private static readonly string UpdateKnobs = $"env=offline&updateBuildId={ThisBuild}&{FastUpdateIntervals}";

    /// <summary>
    /// How long the table test watches for a reload that must not happen, from the page's first check. Once the
    /// check has seen the new build the script stops checking, so there is no later request to wait for. Away from
    /// the table the reload would come at the first retry after the idle time, and the watch spans the idle time
    /// and four retries.
    /// </summary>
    private const int PendingReloadWatchMs = UpdateIdleMs + 4 * SafeMomentPollMs;

    /// <summary>
    /// How many checks a page that keeps its build must make before the test accepts that it does not reload. Each
    /// check that sees a new build stops the checks and asks for a reload, so three checks prove two intervals of
    /// the page seeing its own build.
    /// </summary>
    private const int ChecksBeforeNoReload = 3;

    /// <summary>
    /// How long a page with the dev placeholder is watched for a check that must not happen. Four check intervals,
    /// so a script that did not stop at the placeholder would have checked several times.
    /// </summary>
    private const int NoCheckWatchMs = 4 * UpdateCheckMs;

    private const int ReloadTimeoutMs = 20000;

    private int _buildInfoRequests;
    private int _documentRequests;
    private readonly TaskCompletionSource _reloadRequested = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// Completed when the reloaded document has finished loading. <see cref="_reloadRequested"/> completes earlier, on
    /// the reload's request, and a navigation started while that request is in flight gets cancelled by it.
    /// </summary>
    private readonly TaskCompletionSource _reloadFinished = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private int _documentLoads;

    /// <summary>
    /// The build ID the routed <c>build-info.json</c> serves. The request handler in
    /// <see cref="WatchDocumentRequestsAsync"/> switches it to <see cref="ThisBuild"/> on the first reload. The
    /// reloaded page has the same URL parameters, so without the switch it would reload again every idle interval.
    /// </summary>
    private volatile string _deployedBuild = ThisBuild;

    [Test]
    public async Task AwayFromTheTable_ANewBuildReloadsThePage()
    {
        await WatchDocumentRequestsAsync(deployedBuild: NewBuild);

        await Page.GotoAsync(ClientUrl("/events", UpdateKnobs), new() { WaitUntil = WaitUntilState.Commit });

        await ExpectReloadAsync("the page on /events did not reload after build-info.json named a new build");
    }

    [Test]
    public async Task AtTheTable_ANewBuildWaitsUntilThePlayerLeaves()
    {
        await WatchDocumentRequestsAsync(deployedBuild: NewBuild);

        await OpenTableAsync(ClientUrl("/table", UpdateKnobs));

        await WaitForBuildInfoRequestsAsync(1, "the page never fetched build-info.json, so nothing here tests waiting");
        await Page.WaitForTimeoutAsync(PendingReloadWatchMs);
        Assert.That(_documentRequests, Is.EqualTo(1), "the page reloaded while the player was seated at a table");

        await Page.GetByTestId("leave").ClickAsync();

        await ExpectReloadAsync("the page did not reload after the player left the table");
    }

    [Test]
    public async Task TheSameBuildDoesNotReloadThePage()
    {
        await WatchDocumentRequestsAsync(deployedBuild: ThisBuild);

        await Page.GotoAsync(ClientUrl("/events", UpdateKnobs));
        await WaitForBuildInfoRequestsAsync(ChecksBeforeNoReload, "the page did not keep checking build-info.json");

        Assert.That(_documentRequests, Is.EqualTo(1), "the page reloaded although build-info.json named its own build");
    }

    /// <summary>
    /// A page with the <c>dev</c> build ID placeholder never checks for updates. The dev server serves the page with
    /// the placeholder, which is why the other tests set the page's build ID with <c>updateBuildId</c>.
    /// </summary>
    [Test]
    public async Task APageWithTheDevPlaceholderNeverChecks()
    {
        await WatchDocumentRequestsAsync(deployedBuild: NewBuild);

        await Page.GotoAsync(ClientUrl("/events", $"env=offline&{FastUpdateIntervals}"));
        await Page.WaitForTimeoutAsync(NoCheckWatchMs);

        Assert.That(_buildInfoRequests, Is.EqualTo(0), "a page with the dev placeholder fetched build-info.json");
        Assert.That(_documentRequests, Is.EqualTo(1), "a page with the dev placeholder reloaded");
    }

    /// <summary>Serves <paramref name="deployedBuild"/> as build-info.json and counts main-frame document requests.</summary>
    private async Task WatchDocumentRequestsAsync(string deployedBuild)
    {
        _deployedBuild = deployedBuild;

        Page.Request += (_, request) =>
        {
            if (!request.IsNavigationRequest || request.Frame != Page.MainFrame)
                return;

            if (Interlocked.Increment(ref _documentRequests) > 1)
            {
                // Serve the page's own build from now on. The reloaded page has not run its script yet, so its
                // first check sees its own build and does not reload again.
                _deployedBuild = ThisBuild;
                _reloadRequested.TrySetResult();
            }
        };

        Page.Load += (_, _) =>
        {
            if (Interlocked.Increment(ref _documentLoads) > 1)
                _reloadFinished.TrySetResult();
        };

        await Page.RouteAsync("**/build-info.json", async route =>
        {
            Interlocked.Increment(ref _buildInfoRequests);
            await route.FulfillAsync(new()
            {
                Status      = 200,
                ContentType = "application/json",
                Body        = $"{{\"buildId\":\"{_deployedBuild}\"}}",
            });
        });
    }

    /// <summary>
    /// Wait until the page has fetched build-info.json at least <paramref name="atLeast"/> times, and fail with
    /// <paramref name="failure"/> if it has not within <see cref="ReloadTimeoutMs"/>.
    /// </summary>
    private async Task WaitForBuildInfoRequestsAsync(int atLeast, string failure)
    {
        int seen = await PollAsync(() => Task.FromResult(Volatile.Read(ref _buildInfoRequests)), count => count >= atLeast,
            ReloadTimeoutMs, intervalMs: 50);

        Assert.That(seen, Is.GreaterThanOrEqualTo(atLeast), $"{failure} ({seen} requests)");
    }

    private async Task ExpectReloadAsync(string failure)
    {
        try
        {
            await _reloadRequested.Task.WaitAsync(TimeSpan.FromMilliseconds(ReloadTimeoutMs));
        }
        catch (TimeoutException)
        {
            Assert.Fail(failure);
        }

        // The reload has happened, so the test has passed. Wait for the reloaded document to finish loading before
        // leaving the page, because the in-flight reload would cancel the navigation and fail the test. No second
        // reload follows, because build-info.json already names the page's own build.
        await _reloadFinished.Task.WaitAsync(TimeSpan.FromMilliseconds(ReloadTimeoutMs));
        await Page.GotoAsync("about:blank");
    }
}
