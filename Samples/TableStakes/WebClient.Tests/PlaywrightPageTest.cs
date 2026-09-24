using Game.Logic;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace WebClient.Tests;

/// <summary>
/// Base class for every browser fixture in this suite. It holds where the client and the server are served, the
/// boot timeout, and the helpers that take a fixture from the menu to a seat at a table and through a game, and
/// read what the game or a reward changed.
/// <para>
/// Before its first test, every fixture checks that the client on the port was built from this working tree (see
/// <see cref="ServedClientBuildCheck"/>). The default ports are those of a hand-started <c>dotnet run</c> pair. The harness
/// (<c>tools/run-e2e.sh</c>) gives each worktree's stack free ports in environment variables, and every page URL
/// is built by <see cref="ClientUrl"/>, so the ports are read in one place.
/// </para>
/// </summary>
public abstract class PlaywrightPageTest : PageTest
{
    /// <summary>
    /// Origin of the web client, with no path and no query. Defaults to the port in
    /// <c>WebClient/Properties/launchSettings.json</c>. Overridden by <c>TABLESTAKES_E2E_CLIENT_URL</c>.
    /// </summary>
    protected static readonly string ClientOrigin =
        E2EStackPorts.ReadEnvironmentVariable("TABLESTAKES_E2E_CLIENT_URL") ?? "http://localhost:5290";

    /// <summary>
    /// The game server's public web host. It serves the unauthenticated test endpoints, which exist only when the
    /// server was started with <c>--TestRoutes:Enabled=true</c>. Overridden by
    /// <c>TABLESTAKES_E2E_SERVER_PUBLIC_URL</c>.
    /// </summary>
    protected static readonly string ServerPublicUrl =
        E2EStackPorts.ReadEnvironmentVariable("TABLESTAKES_E2E_SERVER_PUBLIC_URL") ?? "http://localhost:5560";

    /// <summary>
    /// The Admin API that the LiveOps Dashboard reads. A test that asserts what an operator sees, such as a
    /// player's event log, reads it from here. Overridden by <c>TABLESTAKES_E2E_SERVER_ADMIN_URL</c>.
    /// </summary>
    protected static readonly string ServerAdminUrl =
        E2EStackPorts.ReadEnvironmentVariable("TABLESTAKES_E2E_SERVER_ADMIN_URL") ?? "http://localhost:5550";

    /// <summary>
    /// The matchmaking fill wait of the server under test, in milliseconds. A fixture that has to wait out the fill
    /// wait sizes its timeout from this value, so the test asserts the same thing under any server pacing.
    /// <para>
    /// The harness shortens the fill wait and passes its value in <c>TABLESTAKES_E2E_FILL_WAIT_MS</c>. When the
    /// variable is not set, the server runs the shipped default. <c>Backend/Server.Tests</c> asserts that default,
    /// so changing it on the server fails there instead of silently changing what these fixtures measure.
    /// </para>
    /// </summary>
    protected static readonly int FillWaitMs =
        int.TryParse(E2EStackPorts.ReadEnvironmentVariable("TABLESTAKES_E2E_FILL_WAIT_MS"), out int exportedFillWaitMs)
            ? exportedFillWaitMs
            : 5000;

    /// <summary>
    /// Build a page URL on the client under test from the origin, the route and the test's query. When the harness
    /// runs the game server on non-default ports, the URL also carries the two parameters that tell the client
    /// where to connect.
    /// <para>
    /// The client reads the connection parameters only at boot, so every URL a test navigates to must carry them.
    /// In-app navigation drops them without harm because it does not reboot the client, but a reload would boot
    /// without them. Use <see cref="ReloadOnStackAsync"/> instead of Playwright's reload for that reason.
    /// </para>
    /// </summary>
    protected static string ClientUrl(string route = "", string? query = null)
    {
        string fullQuery = string.Join('&', new[] { query, E2EStackPorts.EndpointQuery }.Where(part => !string.IsNullOrEmpty(part)));
        return fullQuery.Length == 0 ? ClientOrigin + route : $"{ClientOrigin}{route}?{fullQuery}";
    }

    /// <summary>
    /// Reload the current route as a player's refresh would, connected to this test's stack.
    /// <para>
    /// In-app navigation drops the connection parameters from the query, so Playwright's <c>ReloadAsync</c> would
    /// boot a client connected to whatever holds the default ports. This method puts the stack's parameters back and
    /// keeps the rest of the query, which on an offline page selects the scenario and the timings.
    /// </para>
    /// </summary>
    protected static Task ReloadOnStackAsync(IPage page)
    {
        Uri current = new Uri(page.Url);

        // ClientUrl adds the stack's two parameters again, so remove them here to avoid duplicates.
        string carried = string.Join('&', current.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(pair => !pair.StartsWith("wsPort=", StringComparison.Ordinal)
                        && !pair.StartsWith("cdnPort=", StringComparison.Ordinal)));

        return page.GotoAsync(ClientUrl(current.AbsolutePath, carried));
    }

    /// <summary>
    /// The route of <paramref name="url"/>, without the origin and the query. Tests assert on the route because
    /// the origin depends on the harness and the query depends on how the page was reached.
    /// </summary>
    protected static string RouteOf(string url) => new Uri(url).AbsolutePath;

    /// <summary>
    /// The table page in offline mode: the real multiplayer entity hosted in the browser, with no game server.
    /// Offline fixtures append their timing knobs to this URL.
    /// </summary>
    protected static readonly string OfflineTableUrl = ClientUrl("/table", "env=offline");

    /// <summary>
    /// Fail the fixture before its first test if the client under test was not built from this working tree.
    /// </summary>
    /// <remarks>
    /// Static because the suite creates a fixture instance per test case (see <c>Parallelism.cs</c>), and NUnit
    /// requires one-time setup methods to be static in that mode.
    /// </remarks>
    [OneTimeSetUp]
    public static Task VerifyClientUnderTestIsThisBuild() => ServedClientBuildCheck.VerifyIsThisBuildAsync(ClientOrigin);

    /// <summary>Boot timeout. A cold WASM boot under parallel test load can take tens of seconds.</summary>
    protected const int BootTimeoutMs = 60000;

    /// <summary>
    /// The client's console errors and warnings, page exceptions and failed requests during this test.
    /// </summary>
    private ClientHealth.Log? _clientLog;

    /// <summary>
    /// The text of every console message and page error during this test, for
    /// <see cref="AssertTheTimelinesNeverDiverged"/> and <see cref="AssertTheSessionRanConsistencyChecks"/>.
    /// </summary>
    private readonly List<string> _consoleText = new List<string>();

    /// <summary>Start collecting the client's output before the test navigates anywhere.</summary>
    [SetUp]
    public void ListenToTheClient()
    {
        _clientLog = ClientHealth.Log.AttachTo(Page);
        Page.Console += (_, message) => AddConsoleText(message.Text);
        Page.PageError += (_, error) => AddConsoleText(error);
    }

    private void AddConsoleText(string text)
    {
        lock (_consoleText)
            _consoleText.Add(text);
    }

    private string[] ConsoleText()
    {
        lock (_consoleText)
            return _consoleText.ToArray();
    }

    /// <summary>
    /// Fail if the SDK's client-side journal checkers logged an illegal state modification or a checksum mismatch.
    /// <para>
    /// A reward grant, a spin or a claim changes checksummed state, which is allowed only in an action that the
    /// server runs at the same timeline position as the client. Otherwise the checker logs an error once, turns
    /// itself off for the rest of the session, and the client and server timelines diverge. The checker reports
    /// only to the console, so this method reads it.
    /// </para>
    /// </summary>
    protected void AssertTheTimelinesNeverDiverged()
    {
        // Match the SDK's error messages exactly. Normal flush logging contains "Checksums=", and the session start
        // log mentions consistency checks, so a looser match reports errors in a healthy session.
        string[] complaints = ConsoleText()
            .Where(line => line.Contains("Illegal state modification", StringComparison.Ordinal)
                        || line.Contains("Checksum mismatch", StringComparison.Ordinal)
                        || line.Contains("does not have FollowerUnsynchronized", StringComparison.Ordinal))
            .ToArray();

        Assert.That(complaints, Is.Empty, "the client's journal checker complained: " + string.Join(" | ", complaints));
    }

    /// <summary>
    /// Fail unless the session logged that it runs with consistency checks on. Without them an empty result from
    /// <see cref="AssertTheTimelinesNeverDiverged"/> proves nothing.
    /// </summary>
    protected void AssertTheSessionRanConsistencyChecks() =>
        Assert.That(ConsoleText().Any(line => line.Contains("ClientConsistencyChecks=True", StringComparison.Ordinal)), Is.True,
            "the session did not run with consistency checks on, so an empty checker log proves nothing");

    /// <summary>
    /// Fail a passing test if Blazor's error bar is showing, and print the client's output when the test fails.
    /// <para>
    /// The error bar means some code threw and the app stopped rendering. A test whose assertions ran before the
    /// throw would pass, and a later test would fail with an unrelated locator timeout. The output is printed for
    /// every failure because a boot starved of CPU also ends in the error bar, and the output shows that cause.
    /// </para>
    /// </summary>
    [TearDown]
    public async Task FailIfTheClientDied()
    {
        bool failed = TestContext.CurrentContext.Result.Outcome.Status == NUnit.Framework.Interfaces.TestStatus.Failed;
        bool died   = await ClientHealth.ErrorBarRevealedAsync(Page);

        if (failed || died)
        {
            IReadOnlyList<string> clientLogEntries = _clientLog?.Entries ?? Array.Empty<string>();
            if (clientLogEntries.Count > 0)
            {
                TestContext.Out.WriteLine("[client] what the client said during this test:");
                foreach (string entry in clientLogEntries)
                    TestContext.Out.WriteLine("[client]   " + entry);
            }
        }

        if (died && !failed)
        {
            Assert.Fail(
                "the test passed but the client had thrown: Blazor's \"An unhandled error has occurred\" bar is " +
                "showing, so the app stopped rendering at some point during this test. What the client said is in " +
                "the [client] block above.");
        }
    }

    /// <summary>
    /// Wait until the shell shows the player's own state instead of the fixture it renders while the session
    /// connects.
    /// <para>
    /// Every meta screen renders fixture data before a session exists. A value read then is the fixture's, and a
    /// control tapped then sends no request, so the test times out waiting for a result. <c>MetaLayout.razor</c>
    /// sets <c>data-state="live"</c> on the <c>session</c> marker once the session is live.
    /// </para>
    /// </summary>
    protected Task WaitForLiveSessionAsync() =>
        Expect(Page.GetByTestId("session")).ToHaveAttributeAsync("data-state", "live", new() { Timeout = BootTimeoutMs });

    /// <summary>
    /// Wait until Home's identity row shows a server-generated name instead of the fixture's placeholder. A
    /// placeholder name passed to the Admin API finds no player. Generated names (AdjectiveNounNN, or the
    /// Guest NNNN fallback) are at least eight characters long and the placeholder is shorter, which is how the
    /// wait tells them apart.
    /// <para>
    /// The identity row exists only on Home, so the page must be on Home for this wait and for
    /// <see cref="ReadPlayerNameAsync"/>.
    /// </para>
    /// </summary>
    protected Task WaitForRealPlayerNameAsync() =>
        Page.WaitForFunctionAsync(
            "() => { const n = (document.querySelector('[data-testid=\"home-profile\"] [data-testid=\"avatar-name\"]')?.textContent || '').trim();" +
            " return n.length >= 8 && n !== 'Avery'; }",
            null, new() { Timeout = BootTimeoutMs });

    /// <summary>
    /// Return the player's server-generated name from Home's identity row, for looking the player up in the Admin
    /// API. The page must be on Home. See <see cref="WaitForRealPlayerNameAsync"/>.
    /// </summary>
    protected async Task<string> ReadPlayerNameAsync()
    {
        await WaitForRealPlayerNameAsync();
        return (await Page.GetByTestId("home-profile").GetByTestId("avatar-name").InnerTextAsync()).Trim();
    }

    /// <summary>
    /// The HUD balance of <paramref name="currency"/> (<c>coins</c>, <c>gems</c> or <c>spintokens</c>), read from
    /// the chip's <c>data-amount</c> attribute. The displayed digits are not used, because they animate towards the
    /// new value while a reward arrives (<c>BalanceChip.razor</c>).
    /// </summary>
    protected async Task<int> ReadBalanceAsync(string currency)
    {
        ILocator chip = Page.GetByTestId($"balance-{currency}");
        await Expect(chip).ToBeVisibleAsync(new() { Timeout = BootTimeoutMs });

        return int.Parse(await chip.GetAttributeAsync("data-amount") ?? "");
    }

    /// <summary>
    /// Assert the HUD's three balances as the exact text the chips show. A substring match would pass a wrong
    /// balance that contains the expected one, because "3,000" is a substring of "13,000". <paramref name="timeout"/>
    /// applies to the coin balance, which is read first, so a caller waiting for a grant to arrive passes it.
    /// </summary>
    protected async Task AssertWalletAsync(string coins, string gems, string spinTokens, float? timeout = null)
    {
        LocatorAssertionsToHaveTextOptions? options = timeout == null ? null : new() { Timeout = timeout };

        await Expect(Page.GetByTestId("balance-coins").Locator(".m-balance__amount")).ToHaveTextAsync(coins, options);
        await Expect(Page.GetByTestId("balance-gems").Locator(".m-balance__amount")).ToHaveTextAsync(gems);
        await Expect(Page.GetByTestId("balance-spintokens").Locator(".m-balance__amount")).ToHaveTextAsync(spinTokens);
    }

    /// <summary>The Shop, on this test's stack.</summary>
    protected static readonly string ShopUrl = ClientUrl("/shop");

    /// <summary>The seasonal tournament's Compete screen, on this test's stack.</summary>
    protected static readonly string CompeteUrl = ClientUrl("/compete");

    /// <summary>
    /// Open Compete with a page load and wait until the session state has loaded, not only until the screen has
    /// drawn.
    /// <para>
    /// Every meta screen renders fixture data until the session state replaces it, so waiting for the card, the
    /// standings or the HUD name would pass on the fixture. The wait is for "Open to enter", which a fresh
    /// player's real card shows and the fixture's card does not.
    /// </para>
    /// </summary>
    protected async Task OpenCompeteAsync()
    {
        await Page.GotoAsync(CompeteUrl);
        await Expect(Page.GetByTestId("tournament-summary")).ToContainTextAsync("Open to enter", new() { Timeout = BootTimeoutMs });
    }

    /// <summary>
    /// Open Profile through the Home tab and the identity row's button, and wait until it shows the player's own
    /// record.
    /// <para>
    /// It navigates by taps rather than a page load, because a page load restarts the client and the session. The
    /// session wait is required: until the session connects, Profile shows the fixture's record, which has games
    /// played.
    /// </para>
    /// </summary>
    protected async Task OpenProfileFromHomeAsync()
    {
        await Page.GetByTestId("nav-home").ClickAsync();
        await Page.GetByTestId("home-profile-action").ClickAsync();
        await WaitForLiveSessionAsync();
        await Expect(Page.GetByTestId("record-played")).ToBeVisibleAsync(new() { Timeout = BootTimeoutMs });
    }

    /// <summary>
    /// Read the games played, games won and tricks won from the record on Profile. The page must be on Profile (see
    /// <see cref="OpenProfileFromHomeAsync"/>).
    /// </summary>
    protected async Task<(int Played, int Won, int Tricks)> ReadRecordAsync() => (
        ParseRecordNumber(await Page.GetByTestId("record-played").InnerTextAsync()),
        ParseRecordNumber(await Page.GetByTestId("record-won").InnerTextAsync()),
        ParseRecordNumber(await Page.GetByTestId("record-tricks").InnerTextAsync()));

    /// <summary>A number from Profile's record, which may be shown with digit grouping.</summary>
    private static int ParseRecordNumber(string text) =>
        int.Parse(text, NumberStyles.Integer | NumberStyles.AllowThousands, CultureInfo.InvariantCulture);

    /// <summary>The tile for day <paramref name="day"/> in the first-week event's full list of days.</summary>
    protected ILocator FirstWeekDay(int day) =>
        Page.GetByTestId("first-week-day").Filter(new LocatorFilterOptions { HasText = $"Day {day} ·" });

    /// <summary>
    /// Wait until the game server has written the named event to this player's event log. The player is looked up
    /// by name through the Admin API, as the LiveOps Dashboard does.
    /// <para>
    /// The client predicts an action and renders the result before the server commits it, so the committed event is
    /// the only reliable sign that the server has the action. A test that reloads or navigates away before this wait
    /// returns can discard the unflushed action. The player lookup is <see cref="FindLivePlayerIdAsync"/>.
    /// </para>
    /// </summary>
    protected static async Task WaitForServerToHaveEventAsync(string playerName, string eventName, int timeoutMs = 15000)
    {
        Stopwatch waited = Stopwatch.StartNew();

        string? playerId = await WaitForLivePlayerIdAsync(playerName, timeoutMs);
        if (playerId == null)
            Assert.Fail($"the Admin API found no player named {playerName} within {timeoutMs} ms");

        int  remainingMs = Math.Max(0, timeoutMs - (int)waited.ElapsedMilliseconds);
        bool committed   = await PollAsync(() => PlayerLogHoldsAsync(playerId!, eventName), held => held, remainingMs);

        if (!committed)
            Assert.Fail($"the server did not commit {eventName} for {playerName} within {timeoutMs} ms");
    }

    /// <summary>
    /// One HTTP client for every Admin API read in the suite. <see cref="HttpClient"/> is safe to share between
    /// concurrent requests, and one instance reuses its connections instead of opening new ones on every poll.
    /// </summary>
    protected static HttpClient AdminHttp => SharedAdminHttp.Client;

    /// <summary>
    /// Holds <see cref="AdminHttp"/> outside the fixture type, because the client lives as long as the test process.
    /// A <c>[OneTimeTearDown]</c> on this base class runs once per fixture, so disposing the client there would
    /// dispose it under every fixture that runs later.
    /// </summary>
    private static class SharedAdminHttp
    {
        public static readonly HttpClient Client = new HttpClient();
    }

    /// <summary>
    /// Call <paramref name="read"/> every <paramref name="intervalMs"/> until <paramref name="isDone"/> accepts its
    /// result or <paramref name="timeoutMs"/> has passed, and return the last result. It does not fail on a
    /// timeout, so the caller asserts on the result with a message that says what was last seen.
    /// <para>
    /// Use it for state outside the page, such as the Admin API or a test route. A page element is waited for with
    /// Playwright's retrying assertions instead.
    /// </para>
    /// </summary>
    protected static async Task<T> PollAsync<T>(Func<Task<T>> read, Func<T, bool> isDone, int timeoutMs, int intervalMs = 250)
    {
        Stopwatch waited = Stopwatch.StartNew();

        T value = await read();
        while (!isDone(value) && waited.ElapsedMilliseconds < timeoutMs)
        {
            await Task.Delay(intervalMs);
            value = await read();
        }

        return value;
    }

    /// <summary>
    /// Poll <see cref="FindLivePlayerIdAsync"/> until it finds the player named <paramref name="playerName"/>, and
    /// return the id, or null if no player by that name appeared within <paramref name="timeoutMs"/>. A caller
    /// that then polls the player's state looks the player up only once.
    /// </summary>
    protected static Task<string?> WaitForLivePlayerIdAsync(string playerName, int timeoutMs) =>
        PollAsync(() => FindLivePlayerIdAsync(playerName), id => id != null, timeoutMs);

    /// <summary>
    /// The id of the player named exactly <paramref name="playerName"/>, or null when the Admin API does not find
    /// one. Among exact matches, the player with the most recent login is taken, because that is the one this test
    /// has a session for.
    /// </summary>
    protected static async Task<string?> FindLivePlayerIdAsync(string playerName)
    {
        string json = await AdminHttp.GetStringAsync($"{ServerAdminUrl}/api/players?query={Uri.EscapeDataString(playerName)}&count=10");

        using JsonDocument found = JsonDocument.Parse(json);

        return found.RootElement.EnumerateArray()
            .Where(player => player.TryGetProperty("name", out JsonElement name) && name.GetString() == playerName)
            .OrderByDescending(player => player.GetProperty("lastLoginAt").GetDateTime())
            .Select(player => player.GetProperty("id").GetString())
            .FirstOrDefault();
    }

    /// <summary>
    /// The payload of every entry in the player's event log, oldest first, read through the Admin API as the
    /// LiveOps Dashboard reads it. Fails the test if the server reports that the scan desynced, because the entries
    /// are then not the player's log.
    /// </summary>
    protected static async Task<IReadOnlyList<JsonElement>> ReadEventLogPayloadsAsync(string playerId)
    {
        string json = await AdminHttp.GetStringAsync(
            $"{ServerAdminUrl}/api/players/{playerId}/eventLog?startCursor=$oldest&numEntries=200&scanDirection=TowardsNewer");

        using JsonDocument log = JsonDocument.Parse(json);

        if (log.RootElement.GetProperty("failedWithDesync").GetBoolean())
            Assert.Fail("the event log scan desynced, so what came back is not the player's log");

        // Clone each payload, because the elements of a JsonDocument are invalid once the document is disposed.
        return log.RootElement.GetProperty("entries").EnumerateArray()
            .Select(entry => entry.GetProperty("payload").Clone())
            .ToArray();
    }

    /// <summary>Whether the player's event log holds an event of the given type name, without the namespace.</summary>
    private static async Task<bool> PlayerLogHoldsAsync(string playerId, string eventName)
    {
        // "$type" holds the full type name, such as "Game.Logic.PlayerEventCosmeticPurchased". Compare the part
        // after the namespace.
        return (await ReadEventLogPayloadsAsync(playerId))
            .Any(payload => payload.GetProperty("$type").GetString()!.Split('.').Last() == eventName);
    }

    /// <summary>
    /// Timeout for one turn against the live server. A turn is three bot think delays plus three resolve pauses.
    /// The value is sized for a hand-started server with shipped timings, which is slower than the harness.
    /// </summary>
    protected const int TurnTimeoutMs = 30000;

    /// <summary>
    /// The server's shipped <c>Match:DisconnectGrace</c> (<c>MatchTimings.Default</c>), in milliseconds. A fixture
    /// that drops the session and reconnects measures its gap against this value, because a cold WASM boot can take
    /// longer and after the grace period a bot takes the seat. <c>Backend/Server.Tests</c> asserts the shipped
    /// value, so a shorter server grace cannot make these fixtures silently test the after-grace path.
    /// </summary>
    protected const int DisconnectGraceMs = 20000;

    /// <summary>
    /// The CSS screen position of seat <paramref name="seat"/> for a viewer in seat <paramref name="ownSeat"/>, from
    /// <see cref="ExpectedSeatPositions"/>. The matchmaker picks the viewer's seat index, so a fixture computes the
    /// position instead of assuming it.
    /// </summary>
    protected static string ScreenPositionOfSeat(int seat, int ownSeat) =>
        ExpectedSeatPositions.ScreenPositionOfSeat(seat, ownSeat);

    /// <summary>
    /// Open Home, tap PLAY on the hero banner, and wait until the client is seated at a table. The tap enqueues
    /// from Home, the searching dialog opens over it, and the page changes once, to the table. With no other players
    /// queued, bots take the empty seats after the fill wait.
    /// <para>
    /// The tap and the seated assertion are the only steps that use the server's single matchmaking queue, so this
    /// method holds the <see cref="LiveServerLocks.QueueAsync"/> lock around them. Callers do not take the lock.
    /// </para>
    /// </summary>
    protected async Task PlayAndBeSeatedAsync()
    {
        await Page.GotoAsync(ClientUrl("/"));
        await WaitForLiveSessionAsync();

        // Home shows PLAY when the player is not at a table.
        ILocator play = Page.GetByTestId("play");
        await Expect(play).ToBeVisibleAsync(new() { Timeout = BootTimeoutMs });

        await TapPlayAndBeSeatedAsync(play, $"{GetType().Name}.PlayAndBeSeatedAsync");
    }

    /// <summary>
    /// Tap Play in the navigation bar and wait until the client is seated at a table. The searching dialog opens
    /// over the current screen and keeps the session, so the same session plays the game and then reads what it
    /// changed. The tap and the seated assertion run inside <see cref="LiveServerLocks.QueueAsync"/>, named
    /// <paramref name="step"/>.
    /// </summary>
    protected Task PlayFromTheNavigationBarAsync(string step) =>
        TapPlayAndBeSeatedAsync(Page.GetByTestId("nav-play"), step);

    /// <summary>
    /// Tap <paramref name="play"/>, a control that enters the matchmaking queue, and wait until the searching
    /// dialog has opened and the client is seated at a table. The tap and both waits run inside
    /// <see cref="LiveServerLocks.QueueAsync"/>, named <paramref name="step"/>, and nothing else does. A caller
    /// that also needs <see cref="LiveServerLocks.LeagueAsync"/> takes it before calling this method.
    /// </summary>
    protected async Task TapPlayAndBeSeatedAsync(ILocator play, string step)
    {
        await using (await LiveServerLocks.QueueAsync(step))
        {
            await play.ClickAsync();
            await Expect(Page.GetByTestId("searching")).ToBeVisibleAsync(new() { Timeout = BootTimeoutMs });
            await Expect(Page.GetByTestId("match-phase")).ToHaveTextAsync("Playing", new() { Timeout = BootTimeoutMs });
        }
    }

    /// <summary>
    /// Play every card in the viewer's hand, one per trick, and wait until the match has ended and the results
    /// overlay is shown. Waiting for a playable card also waits out the bots' think delays and the pauses between
    /// tricks. <paramref name="playCard"/> plays one legal card, and defaults to the first one.
    /// </summary>
    protected async Task PlayWholeHandAsync(Func<Task>? playCard = null)
    {
        // own-hand-index is -1 until the deal reaches this client, then the play index the hand was dealt at.
        // The loop below counts cards to confirm each tap landed. That works only if nothing else plays the
        // viewer's cards: an expired move deadline auto-plays the seat, and an expired disconnect grace gives
        // the seat to a bot. The E2E harness keeps both timeouts at their shipped values (tools/run-e2e.py,
        // SANCTIONED_PACING_KEYS), and they are long enough for the loop to finish first.
        await Expect(Page.GetByTestId("own-hand-index")).ToHaveTextAsync(new Regex(@"^\d+$"), new() { Timeout = TurnTimeoutMs });

        ILocator   handCount = Page.GetByTestId("own-hand-count");
        Func<Task> play      = playCard ?? PlayFirstLegalCardAsync;

        for (int cardsLeft = 5; cardsLeft > 0; cardsLeft--)
        {
            await Expect(handCount).ToHaveTextAsync(cardsLeft.ToString(), new() { Timeout = TurnTimeoutMs });

            await play();

            await Expect(handCount).ToHaveTextAsync((cardsLeft - 1).ToString(), new() { Timeout = TurnTimeoutMs });
        }

        await Expect(Page.GetByTestId("match-phase")).ToHaveTextAsync("Ended", new() { Timeout = TurnTimeoutMs });
        await Expect(Page.GetByTestId("results-overlay")).ToBeVisibleAsync(new() { Timeout = TurnTimeoutMs });
    }

    private async Task PlayFirstLegalCardAsync()
    {
        ILocator card = Page.GetByTestId("legal-card").First;
        await Expect(card).ToBeVisibleAsync(new() { Timeout = TurnTimeoutMs });
        await card.ClickAsync();
    }

    /// <summary>
    /// Leave from the results overlay. Leaving clears the player's reference to the finished table and lands on
    /// Home, so the shell navigation appearing shows that the player has left the table.
    /// </summary>
    protected async Task LeaveTheResultsAsync()
    {
        await Page.GetByTestId("results-leave").ClickAsync();
        await Expect(Page.GetByTestId("primary-nav")).ToBeVisibleAsync(new() { Timeout = TurnTimeoutMs });
    }

    /// <summary>
    /// Assert that on <paramref name="page"/>'s table the viewer's own seat is the only human one and bots hold the
    /// other three.
    /// </summary>
    protected async Task ExpectOnlyTheOwnSeatIsHumanAsync(IPage page)
    {
        int ownSeat = int.Parse(await page.GetByTestId("own-seat").InnerTextAsync());
        for (int seat = 0; seat < 4; seat++)
            await Expect(page.GetByTestId($"seat-{seat}")).ToHaveAttributeAsync("data-occupancy", seat == ownSeat ? "Human" : "Bot");
    }

    /// <summary>
    /// Open the table at <paramref name="url"/> and wait until its match is Playing. Offline fixtures pass
    /// <see cref="OfflineTableUrl"/> with their timing knobs.
    /// </summary>
    protected async Task OpenTableAsync(string url)
    {
        await Page.GotoAsync(url);
        await Expect(Page.GetByTestId("match-phase")).ToHaveTextAsync("Playing", new() { Timeout = BootTimeoutMs });
    }

    /// <summary>
    /// Press and hold the pointer on <paramref name="card"/>, and return the point where it was pressed.
    /// <para>
    /// The pointer moves there with a hover, so Playwright's actionability checks run first: the card has stopped
    /// moving and receives the pointer at that point. Coordinates read earlier could be taken before the card has
    /// risen into its playable position.
    /// </para>
    /// </summary>
    protected async Task<(float X, float Y)> GripCardAsync(ILocator card)
    {
        await card.HoverAsync();

        LocatorBoundingBoxResult box = await card.BoundingBoxAsync()
            ?? throw new InvalidOperationException("the card has no box to grip");

        await Page.Mouse.DownAsync();

        return (box.X + box.Width / 2, box.Y + box.Height / 2);
    }

    /// <summary>
    /// Move a gripped card sideways without raising it: far enough not to count as a tap, and not high enough to
    /// count as a play.
    /// <para>
    /// The card moves towards the horizontal center of the table. The release is caught only inside the app's
    /// frame, which on a desktop is a phone-sized column in the middle of the window (<c>#app</c> in
    /// <c>app.css</c>). A pointer that leaves the frame cancels the gesture. The first legal card's position in the
    /// hand depends on the deal, so a fixed offset would leave the frame on some deals. Moving inwards never does.
    /// </para>
    /// </summary>
    protected async Task DragSidewaysAsync(float gripX, float gripY)
    {
        LocatorBoundingBoxResult frame = await Page.GetByTestId("table").BoundingBoxAsync()
            ?? throw new InvalidOperationException("the table has no box");

        float inwards = gripX < frame.X + frame.Width / 2 ? SidewaysDragPx : -SidewaysDragPx;

        await Page.Mouse.MoveAsync(gripX + inwards, gripY, new() { Steps = 8 });
    }

    /// <summary>
    /// Distance of a sideways drag. It must exceed the table's tap threshold and stay well below half the frame's
    /// width.
    /// </summary>
    private const float SidewaysDragPx = 60f;
}
