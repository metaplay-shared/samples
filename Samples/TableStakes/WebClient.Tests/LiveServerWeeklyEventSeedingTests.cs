using Microsoft.Playwright;
using WebClient.Meta;
using Microsoft.Playwright.NUnit;
using System.Globalization;
using System.Text.Json;

namespace WebClient.Tests;

/// <summary>
/// E2E tests for weekly event seeding against a <b>live game server</b>: with no weekly event created by hand, a
/// new player finds one already running (<c>docs/weekly-event.md</c>). The seeder is a singleton service entity that
/// imports a package of weeks into the LiveOps timeline, which only a live server has. <c>WeeklyEventSeedingTests</c>
/// and <c>WeeklyEventSeedPackageTests</c> cover the calendar and the package. Idempotency is tested by asking the
/// server to run the seeder's own pass again.
/// This fixture must read the seeded weeks before <see cref="LiveServerWeeklyEventTests"/> creates a week, which
/// concludes the seeded weeks it overlaps. NUnit honours <c>[Order]</c> between the two only because both are
/// <c>[NonParallelizable]</c>, since it does not order work under parallel execution. Keep both attributes.
/// </summary>
[NonParallelizable]
[Order(1)]
[TestFixture]
public class LiveServerWeeklyEventSeedingTests : PlaywrightPageTest
{
    /// <summary>
    /// The number of weeks the server's seeding options keep on the timeline. It is copied here because this
    /// project cannot reference the server assembly, so a change to the server option must be made here too.
    /// </summary>
    private const int HorizonWeeks = 8;

    /// <summary>The themes of the shipped weekly event templates. Every seeded week uses one of them.</summary>
    private static readonly string[] SeededThemes = { "Trickster's Week", "High Suit Season" };

    private sealed record SeedPass(bool Ran, int Created, int Kept, DateTime SeededThrough, string[] Problems, int PassesRun);

    /// <summary>Runs one seeding pass, the same pass the server runs at startup, and returns its result.</summary>
    private static Task<SeedPass> RunSeedPassAsync() => AskSeederAsync(run: true);

    /// <summary>Returns the result of the last pass without running a new one.</summary>
    private static Task<SeedPass> ReadLastPassAsync() => AskSeederAsync(run: false);

    /// <summary>
    /// Polls until the seeding pass that the server runs at startup has completed.
    /// <para>
    /// <c>/isReady</c> answers before that pass completes, so the test must poll. Running a pass instead would
    /// hide whether the server seeds the weeks on its own.
    /// </para>
    /// </summary>
    private static async Task<SeedPass> WaitForTheServersOwnFirstPassAsync()
    {
        SeedPass pass = await PollAsync(ReadLastPassAsync, last => last.PassesRun > 0, timeoutMs: 60_000, intervalMs: 500);

        Assert.That(pass.PassesRun, Is.GreaterThan(0),
            $"the server ran no seeding pass of its own within a minute of being ready: {string.Join("; ", pass.Problems)}");

        return pass;
    }

    private static async Task<SeedPass> AskSeederAsync(bool run)
    {
        using HttpClient http = new HttpClient();
        using HttpResponseMessage response = run
            ? await http.PostAsync($"{ServerPublicUrl}/test/weeklyevent/seed", content: null)
            : await http.GetAsync($"{ServerPublicUrl}/test/weeklyevent/seed");

        response.EnsureSuccessStatusCode();

        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        JsonElement root = body.RootElement;

        List<string> problems = new List<string>();
        if (root.TryGetProperty("problems", out JsonElement listed) && listed.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement problem in listed.EnumerateArray())
                problems.Add(problem.GetString() ?? "");
        }

        return new SeedPass(
            root.GetProperty("ran").GetBoolean(),
            root.GetProperty("created").GetInt32(),
            root.GetProperty("kept").GetInt32(),
            DateTime.Parse(root.GetProperty("seededThrough").GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal),
            problems.ToArray(),
            root.GetProperty("passesRun").GetInt32());
    }

    /// <summary>
    /// The server seeds its weeks at startup, and later passes create no weeks.
    /// <para>
    /// Both checks are in one test because a later pass creating nothing proves idempotency only if the startup
    /// pass put the weeks there first.
    /// </para>
    /// </summary>
    [Test]
    public async Task TheServerSeedsItsOwnWeeksAndEveryPassAfterThatCreatesNoneOfThemAgain()
    {
        // ---- The server's own startup pass ----
        SeedPass startup = await WaitForTheServersOwnFirstPassAsync();

        Assert.That(startup.Ran, Is.True, $"the server's own seeding pass did not run: {string.Join("; ", startup.Problems)}");

        // Assert the sum of created and kept weeks, not "all created". A server started by hand, or one running
        // across a week boundary, already holds weeks from an earlier pass. Only the harness gives each run a
        // fresh database.
        Assert.That(startup.Created + startup.Kept, Is.EqualTo(HorizonWeeks),
            "the server's own pass should account for the whole horizon");

        // Both bounds use one clock reading, so a week boundary between two readings cannot break the check.
        DateTime now = DateTime.UtcNow;
        Assert.That(startup.SeededThrough, Is.GreaterThan(now.AddDays(7 * (HorizonWeeks - 1))),
            "weekly events should run at least seven weeks ahead");
        Assert.That(startup.SeededThrough, Is.LessThanOrEqualTo(now.AddDays(7 * HorizonWeeks + 1)),
            "and no further ahead than the horizon plus the part-week that is running");

        // ---- A second pass, as after a redeploy, creates nothing ----
        SeedPass second = await RunSeedPassAsync();

        Assert.That(second.Created, Is.Zero, "a second pass created weeks the first one had already created");
        Assert.That(second.Kept, Is.EqualTo(HorizonWeeks), "the second pass should have found the whole horizon already on the timeline");
        Assert.That(second.SeededThrough, Is.EqualTo(startup.SeededThrough));

        // A third pass also changes nothing.
        SeedPass third = await RunSeedPassAsync();

        Assert.That(third.Created, Is.Zero);
        Assert.That(third.Kept, Is.EqualTo(HorizonWeeks));
        Assert.That(third.SeededThrough, Is.EqualTo(startup.SeededThrough));
    }

    /// <summary>
    /// A new player on an environment where no weekly event was created by hand finds a themed week running.
    /// </summary>
    [Test]
    public async Task APlayerWhoArrivesFindsAThemedWeekAlreadyRunning()
    {
        // /isReady answers before the seeder's first pass, and a player who arrives before it sees the empty state.
        await WaitForTheServersOwnFirstPassAsync();

        await Page.GotoAsync(ClientUrl(MetaRoutes.WeeklyEvent));
        await WaitForLiveSessionAsync();

        await Expect(Page.GetByTestId("page-title")).ToBeVisibleAsync(new() { Timeout = BootTimeoutMs });

        // The seeding plan starts with the current week, so the week is scoring, not in preview. Waiting for this
        // element also makes the Not.ToBeVisible check below meaningful, because that check passes on a page
        // that has not rendered yet.
        await Expect(Page.GetByTestId("weekly-play")).ToBeVisibleAsync(new() { Timeout = BootTimeoutMs });

        // The empty state is for a gap in the schedule. A seeded environment must not show it.
        await Expect(Page.GetByText("No event scheduled")).Not.ToBeVisibleAsync();

        // A shipped template's theme shows the week was created by the seeder. Read TextContent, not InnerText,
        // because the card's stylesheet upper-cases the headline.
        string theme = ((await Page.GetByTestId("weekly-theme").TextContentAsync()) ?? "").Trim();
        Assert.That(SeededThemes, Does.Contain(theme), $"the week on screen is themed \"{theme}\", which is not one of the shipped templates");

        // A new player has zero points toward one of the templates' targets. The regex matches whole numbers,
        // because "100" is also a substring of "1200".
        string points = (await Page.GetByTestId("weekly-points").InnerTextAsync()).Trim();
        Assert.That(points, Does.Match(@"^0 / (100|120)\b"),
            $"a fresh player should be at zero of one of the authored targets, not \"{points}\"");
    }
}
