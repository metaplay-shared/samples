using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace WebClient.Tests;

/// <summary>
/// E2E tests for how the client finds its game server when it is served from an environment's CDN, as
/// <c>metaplay deploy webclient</c> deploys it: from the endpoints file the deploy writes next to it, and only from
/// that.
/// <para>
/// The browser resolves a made-up environment's hosts, <c>*.example.test</c>, to this machine, so the client under
/// test is served under a cloud host's name rather than <c>localhost</c>. These tests launch that browser
/// themselves. The fixture's own page is not used.
/// </para>
/// </summary>
[TestFixture]
public class CloudHostingTests : GamePageTest
{
    private const string Domain      = "example.test";
    private const string Environment = "lovely-wombats";

    private IBrowser? _browser;

    // Playwright sets BrowserType up for each test, so the browser is launched for each test too.
    [SetUp]
    public async Task LaunchBrowserResolvingTheEnvironmentLocally()
    {
        _browser = await BrowserType.LaunchAsync(new() { Args = [$"--host-resolver-rules=MAP *.{Domain} 127.0.0.1"] });
    }

    // Null when the launch failed, which then is the test's error to report.
    protected override Task CloseAfterTestAsync() => _browser?.CloseAsync() ?? Task.CompletedTask;

    [Test]
    public async Task ConnectsToTheServerTheEndpointsFileNames()
    {
        IPage page = await NewPageAsync();

        // The file a deploy writes, here naming this machine's game server.
        string endpoints =
            $$"""{"format": 1, "serverHost": "localhost", "serverPortForWebSocket": {{E2EStack.WebSocketPort}}, "enableTls": false, "cdnBaseUrl": "http://localhost:{{E2EStack.CdnPort}}/"}""";
        await page.RouteAsync("**/metaplay-endpoints.json", route => route.FulfillAsync(new() { ContentType = "application/json", Body = endpoints }));

        await page.GotoAsync(PageUrlOnHost($"{Environment}-assets.{Domain}"));

        // Exact match so it doesn't also match the hidden Blazor "Browser Disconnected" reconnect banner.
        await Expect(page.GetByText("Connected", new() { Exact = true })).ToBeVisibleAsync(new() { Timeout = 20000 });
    }

    // A CDN answers 404 for a missing file. A deploy to an environment that reports no game server leaves the empty
    // one the client ships with.
    [TestCase(404, "could not be read (HTTP 404)")]
    [TestCase(200, "it is empty")]
    public async Task WithoutAnEndpointsFile_FailsToStartAndSaysWhy(int status, string reason)
    {
        IPage page = await NewPageAsync();

        await page.RouteAsync("**/metaplay-endpoints.json",
            route => route.FulfillAsync(new() { Status = status, Body = "" }));
        bool dialed = false;
        await page.RouteWebSocketAsync(new Regex("."), _ => dialed = true);
        TaskCompletionSource<string> failure = new(TaskCreationOptions.RunContinuationsAsynchronously);
        page.Console += (_, message) =>
        {
            if (message.Text.Contains("No game server for the web client"))
                failure.TrySetResult(message.Text);
        };

        await page.GotoAsync(PageUrlOnHost($"{Environment}-assets.{Domain}"));

        string text = await failure.Task.WaitAsync(TimeSpan.FromSeconds(20));
        Assert.That(text, Does.Contain("metaplay-endpoints.json").And.Contain(reason));
        Assert.That(dialed, Is.False, "the client must not guess a server to connect to");

        // The page is deployed wrong, so the client stops. The reason is for the developer, in the console.
        await ExpectErrorBarAsync(page);
    }

    private async Task<IPage> NewPageAsync() => Watch(await _browser!.NewPageAsync());

    /// <summary>The client under test, served under <paramref name="host"/> on its own port.</summary>
    private static string PageUrlOnHost(string host) => new UriBuilder(ClientOrigin) { Host = host, Path = "/" }.Uri.ToString();
}
