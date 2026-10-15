using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;
using System.Globalization;
using System.Text.RegularExpressions;

namespace WebClient.Tests;

/// <summary>
/// E2E tests for the developer course tools at <c>/dev/courses</c>, driven through <c>?env=offline</c>: the
/// offline server runs developer-only actions, so the tools are there and need only the web client. The page
/// lists every course, part and legal map in the session's config, draws the selected one with the run
/// renderer, and opens practice or a race on it through the ordinary run screen.
/// </summary>
[Parallelizable(ParallelScope.Self)]
[TestFixture]
public class DeveloperCourseToolsOfflineModeTests : GamePageTest
{
    private static readonly string OfflineUrl =
        (Environment.GetEnvironmentVariable("WEBCLIENT_BASE_URL") ?? "http://localhost:5290").TrimEnd('/') + "/?env=offline";

    /// <summary>Long enough for a composed map run with no input to reach the finish.</summary>
    private const int RunTimeoutMs = 150_000;

    [Test]
    public async Task TheMenu_LinksToTheCourseTools()
    {
        await OpenCourseToolsAsync();

        await Expect(Page).ToHaveURLAsync(new Regex("/dev/courses"));
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Course tools" })).ToBeVisibleAsync();
        await Expect(Page.GetByTestId("dev-courses-unavailable")).ToHaveCountAsync(0);

        await Page.GetByTestId("dev-back").ClickAsync();
        await Expect(Page.GetByTestId("main-menu")).ToBeVisibleAsync(new() { Timeout = 15000 });
    }

    /// <summary>
    /// Every part and every legal map, which the filters narrow to the maps holding a part, or holding it in
    /// one position.
    /// </summary>
    [Test]
    public async Task ThePage_ListsThePartsAndTheMaps()
    {
        await OpenCourseToolsAsync();

        ILocator parts = Page.GetByTestId("dev-part");
        Assert.That(await parts.CountAsync(), Is.GreaterThan(2), "fewer parts than a map is made of");
        await Expect(Page.Locator("[data-testid='dev-part'][data-part-id='rr-crossriver']")).ToHaveCountAsync(1);

        ILocator count = Page.GetByTestId("dev-map-count");
        int allMaps = await ReadCountAsync(count);
        Assert.That(allMaps, Is.GreaterThan(0), "no legal map was listed");
        await Expect(Page.GetByTestId("dev-map")).ToHaveCountAsync(allMaps);

        await Page.GetByTestId("dev-map-filter-part").SelectOptionAsync("rr-crossriver");
        await Expect(count).Not.ToHaveAttributeAsync("data-count", allMaps.ToString(CultureInfo.InvariantCulture));
        int withCrossing = await ReadCountAsync(count);
        Assert.That(withCrossing, Is.GreaterThan(0).And.LessThan(allMaps));
        foreach (string mapId in await MapIdsAsync())
            Assert.That(mapId.Split('+'), Does.Contain("rr-crossriver"), $"{mapId} was listed under a part it does not hold");

        await Page.GetByTestId("dev-map-filter-position").SelectOptionAsync(new SelectOptionValue { Label = "First" });
        await Expect(count).Not.ToHaveAttributeAsync("data-count", withCrossing.ToString(CultureInfo.InvariantCulture));
        foreach (string mapId in await MapIdsAsync())
            Assert.That(mapId, Does.StartWith("rr-crossriver+"), $"{mapId} does not open on the part it was filtered for");
    }

    /// <summary>
    /// A part is drawn on its own by the run renderer (the river crossing with its drooping bridge) and timed
    /// by the scripted runners, but it is not a course a run can be opened on.
    /// </summary>
    [Test]
    public async Task SelectingTheRiverCrossing_DrawsItsBridge()
    {
        await OpenCourseToolsAsync();

        await Page.Locator("[data-testid='dev-part'][data-part-id='rr-crossriver']").ClickAsync();

        ILocator canvas = Page.GetByTestId("dev-preview-canvas");
        await Expect(canvas).ToBeVisibleAsync();
        await Expect(canvas).ToHaveAttributeAsync("data-drooping-bridge-count", "1", new() { Timeout = 15000 });
        await Expect(canvas).ToHaveAttributeAsync("data-bridge-waterfall-present", "true");

        // Scrolling the preview moves the renderer's camera along the course.
        await Page.GetByTestId("dev-preview-scrub").FillAsync("20");
        await Expect(canvas).ToHaveAttributeAsync("data-preview-x", "20.00", new() { Timeout = 5000 });

        ILocator times = Page.GetByTestId("dev-runner-time");
        await Expect(times).ToHaveCountAsync(4, new() { Timeout = 30000 });
        foreach (string runner in new[] { "no-input", "cautious", "competent", "sharp" })
        {
            string finish = await Page.Locator($"[data-testid='dev-runner-time'][data-runner='{runner}']").GetAttributeAsync("data-finish-ms") ?? "";
            Assert.That(int.TryParse(finish, NumberStyles.Integer, CultureInfo.InvariantCulture, out int ms) && ms > 0, Is.True, $"{runner} has no finish time on the part");
        }

        await Expect(Page.GetByTestId("dev-not-playable")).ToBeVisibleAsync();
        await Expect(Page.GetByTestId("dev-practice")).ToHaveCountAsync(0);
    }

    /// <summary>The composer says why three parts make no map, in the composer's own words.</summary>
    [Test]
    public async Task TheComposer_SaysWhyATripletIsNotAMap()
    {
        await OpenCourseToolsAsync();

        await Page.GetByTestId("dev-compose-first").SelectOptionAsync("rr-crossriver");
        await Page.GetByTestId("dev-compose-middle").SelectOptionAsync("rr-crossriver");

        await Expect(Page.GetByTestId("dev-compose-reason")).ToContainTextAsync("used twice");
        await Expect(Page.GetByTestId("dev-compose-show")).ToHaveCountAsync(0);
    }

    /// <summary>
    /// Practice on a chosen map opens the ordinary run screen on that map, alone, and ends as practice; leaving
    /// the score screen goes back to the tools.
    /// </summary>
    [Test]
    public async Task PracticeOnAChosenMap_RunsThatMapAndEndsAsPractice()
    {
        await OpenCourseToolsAsync();
        string mapId = await SelectAMapAsync();

        await Page.GetByTestId("dev-practice").ClickAsync();

        ILocator canvas = Page.GetByTestId("time-trial-canvas");
        await Expect(canvas).ToBeVisibleAsync(new() { Timeout = 15000 });
        await Expect(canvas).ToHaveAttributeAsync("data-course-id", mapId);
        await Expect(canvas).ToHaveAttributeAsync("data-practice", "true");
        await Expect(canvas).ToHaveAttributeAsync("data-opponent-count", "0");

        await Expect(Page.GetByTestId("run-status")).ToContainTextAsync("Practice", new() { Timeout = RunTimeoutMs });
        await Expect(Page.GetByTestId("gate-symbols")).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("screen-transition")).ToHaveAttributeAsync("data-transition-state", "hidden");

        await Page.GetByTestId("run-result").ClickAsync();
        await Expect(Page.GetByTestId("dev-courses")).ToBeVisibleAsync(new() { Timeout = 15000 });
        await Expect(Page.GetByTestId("dev-selected")).ToHaveAttributeAsync("data-selected-id", mapId);
    }

    /// <summary>
    /// A race on a chosen map offline is run against that map's three scripted runners, driven in the browser
    /// because there is no pool to ask, and is practice all the same.
    /// </summary>
    [Test]
    public async Task RaceOnAChosenMap_FieldsThreeOpponents()
    {
        await OpenCourseToolsAsync();
        string mapId = await SelectAMapAsync();

        await Page.GetByTestId("dev-race").ClickAsync();

        ILocator canvas = Page.GetByTestId("time-trial-canvas");
        await Expect(canvas).ToBeVisibleAsync(new() { Timeout = 15000 });
        await Expect(canvas).ToHaveAttributeAsync("data-course-id", mapId);
        await Expect(canvas).ToHaveAttributeAsync("data-practice", "true");
        await Expect(canvas).ToHaveAttributeAsync("data-opponent-count", "3");
        await Expect(Page.GetByTestId("screen-transition")).ToHaveAttributeAsync("data-transition-state", "hidden");

        await Page.GetByTestId("exit-run").ClickAsync();
        await Expect(Page.GetByTestId("dev-courses")).ToBeVisibleAsync(new() { Timeout = 15000 });
    }

    /// <summary>Open the client offline and follow the menu's link into the course tools.</summary>
    private async Task OpenCourseToolsAsync()
    {
        await Page.GotoAsync(OfflineUrl);

        ILocator link = Page.GetByTestId("open-dev-courses");
        await Expect(link).ToBeVisibleAsync(new() { Timeout = 30000 });
        await link.ClickAsync();

        await Expect(Page.GetByTestId("dev-courses")).ToBeVisibleAsync(new() { Timeout = 15000 });
        await Expect(Page.GetByTestId("dev-map").First).ToBeVisibleAsync(new() { Timeout = 15000 });
    }

    /// <summary>Select the first map that opens on the river crossing, and return its id.</summary>
    private async Task<string> SelectAMapAsync()
    {
        await Page.GetByTestId("dev-map-filter-part").SelectOptionAsync("rr-crossriver");
        await Page.GetByTestId("dev-map-filter-position").SelectOptionAsync(new SelectOptionValue { Label = "First" });

        ILocator map = Page.GetByTestId("dev-map").First;
        await Expect(map).ToHaveAttributeAsync("data-map-id", new Regex("^rr-crossriver\\+"));
        string mapId = await map.GetAttributeAsync("data-map-id") ?? "";

        await map.ClickAsync();
        await Expect(Page.GetByTestId("dev-selected")).ToHaveAttributeAsync("data-selected-id", mapId);
        await Expect(Page.GetByTestId("dev-practice")).ToBeVisibleAsync();
        return mapId;
    }

    private async Task<string[]> MapIdsAsync()
        => await Page.GetByTestId("dev-map").EvaluateAllAsync<string[]>("maps => maps.map(map => map.dataset.mapId)");

    private static async Task<int> ReadCountAsync(ILocator count)
        => int.Parse(await count.GetAttributeAsync("data-count") ?? "0", CultureInfo.InvariantCulture);
}
