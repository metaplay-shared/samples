using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;
using NUnit.Framework.Interfaces;
using System.Diagnostics;

namespace WebClient.Tests;

/// <summary>
/// The base of the browser fixtures: where the client under test is, a check that it is this build, and a check after
/// each test that it did not stop with an error.
/// </summary>
public abstract class GamePageTest : PageTest
{
    /// <summary>
    /// The client under test's origin: the dev server's port from <c>WebClient/Properties/launchSettings.json</c>, or
    /// the harness's from <c>E2E_CLIENT_URL</c>.
    /// </summary>
    protected static readonly string ClientOrigin = E2EStack.Setting("E2E_CLIENT_URL") ?? "http://localhost:5290";

    /// <summary>
    /// A page URL on the client under test, with the query that points it at the harness's game server. The client
    /// reads that query only when it starts, so a test that reloads after in-app navigation goes to a
    /// <see cref="ClientUrl"/> again.
    /// </summary>
    protected static string ClientUrl(string route = "/", string? query = null)
    {
        string full = string.Join('&', new[] { query, E2EStack.EndpointQuery }.Where(part => !string.IsNullOrEmpty(part)));
        return full.Length == 0 ? ClientOrigin + route : $"{ClientOrigin}{route}?{full}";
    }

    /// <summary>
    /// Wait until the number <paramref name="counter"/> shows is at least <paramref name="atLeast"/>, and fail with
    /// the last value seen if it does not get there in time.
    /// </summary>
    protected static async Task ExpectCountToReachAsync(ILocator counter, int atLeast, int timeoutMs)
    {
        Stopwatch elapsed = Stopwatch.StartNew();
        string last = "";
        while (elapsed.ElapsedMilliseconds < timeoutMs)
        {
            last = (await counter.InnerTextAsync()).Trim();
            if (int.TryParse(last, out int value) && value >= atLeast)
                return;
            await Task.Delay(100);
        }

        Assert.Fail($"The count did not reach {atLeast} within {timeoutMs} ms; it last showed '{last}'.");
    }

    /// <summary>Fail the fixture before its first test if the client under test is not this build.</summary>
    [OneTimeSetUp]
    public static Task VerifyClientUnderTestIsThisBuild() => ClientUnderTest.VerifyIsThisBuildAsync(ClientOrigin);

    private readonly List<ClientHealth> _clients = new List<ClientHealth>();

    [SetUp]
    public void WatchThePage() => Watch(Page);

    /// <summary>
    /// Check <paramref name="page"/> after the test, as the fixture's own <c>Page</c> is checked. A test that opens
    /// another page passes it through here.
    /// </summary>
    protected IPage Watch(IPage page)
    {
        lock (_clients)
            _clients.Add(ClientHealth.Watch(page));
        return page;
    }

    /// <summary>
    /// Fail a passing test if Blazor's error bar showed on a page it opened, and print what the pages' clients logged
    /// when the test fails.
    /// </summary>
    [TearDown]
    public async Task CheckTheClients()
    {
        try
        {
            ClientHealth[] clients;
            lock (_clients)
                clients = _clients.ToArray();

            // A test whose assertions ran before the client threw would pass, and a later test would fail with an
            // unrelated timeout instead.
            bool errorBarShown = false;
            foreach (ClientHealth client in clients.Where(client => !client.ErrorBarExpected))
                errorBarShown |= await client.ErrorBarShownAsync();

            // What the clients logged often explains a failure of any kind.
            bool failed = TestContext.CurrentContext.Result.Outcome.Status == TestStatus.Failed;
            if (failed || errorBarShown)
                PrintClientOutput(clients);

            if (errorBarShown && !failed)
            {
                Assert.Fail(
                    "Blazor's error bar showed: the client threw during this test and stopped rendering. What it " +
                    "logged is in the [client] lines above.");
            }
        }
        finally
        {
            await CloseAfterTestAsync();
        }
    }

    /// <summary>
    /// Expect Blazor's error bar on <paramref name="page"/>, for a test of a client that is meant to stop, and leave
    /// that page out of the check after the test.
    /// </summary>
    protected async Task ExpectErrorBarAsync(IPage page)
    {
        await Expect(page.Locator("#blazor-error-ui")).ToBeVisibleAsync();
        lock (_clients)
        {
            foreach (ClientHealth client in _clients.Where(client => client.Page == page))
                client.ErrorBarExpected = true;
        }
    }

    /// <summary>
    /// Close what the test opened besides the fixture's own page, such as a browser of its own. A <c>[TearDown]</c> in
    /// the fixture would run before <see cref="CheckTheClients"/> and close the pages before they are checked.
    /// </summary>
    protected virtual Task CloseAfterTestAsync() => Task.CompletedTask;

    private static void PrintClientOutput(ClientHealth[] clients)
    {
        for (int ndx = 0; ndx < clients.Length; ndx++)
        {
            IReadOnlyList<string> entries = clients[ndx].Entries;
            if (entries.Count == 0)
                continue;

            TestContext.Out.WriteLine($"[client] page {ndx + 1} logged:");
            foreach (string entry in entries)
                TestContext.Out.WriteLine("[client]   " + entry);
        }
    }
}
