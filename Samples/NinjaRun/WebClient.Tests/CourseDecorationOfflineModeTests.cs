using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WebClient.Tests;

/// <summary>
/// E2E tests for course decorations and the part editor's add/remove space tool, driven through
/// <c>?env=offline</c> so they need only the web client. The editor places a decoration on the ground, writes it
/// into the part JSON and draws it through the run renderer; the renderer plays a kind's pass-by animation once
/// when the player's runner reaches it; and the space tool moves everything past its X.
/// </summary>
[Parallelizable(ParallelScope.Self)]
[TestFixture]
public class CourseDecorationOfflineModeTests : GamePageTest
{
    private static readonly string BaseUrl =
        (Environment.GetEnvironmentVariable("WEBCLIENT_BASE_URL") ?? "http://localhost:5290").TrimEnd('/');

    private static readonly string OfflineUrl = BaseUrl + "/?env=offline";

    /// <summary>rr-smallclimb's first stretch of ground ends here, in meters.</summary>
    private const int SmallClimbFirstEnd = 24;

    /// <summary>
    /// Place stands a lantern on the ground where the canvas is clicked. It is listed in the side pane after the
    /// part's own two decorations, written into the part JSON in the Behind layer, drawn by the preview, moved
    /// into the Front layer with B, and removed with Delete.
    /// </summary>
    [Test]
    public async Task PlacingALantern_WritesItIntoThePartJson()
    {
        await OpenOnSmallClimbAsync();

        ILocator overlay = Page.GetByTestId("dev-parts-overlay");
        ILocator preview = Page.GetByTestId("dev-parts-preview");
        ILocator code    = Page.GetByTestId("dev-editor-code");
        ILocator rows    = Page.GetByTestId("dev-editor-decoration-row");

        await Expect(Page.GetByTestId("dev-editor-decoration-kind")).ToHaveValueAsync("stone-lantern", new() { Timeout = 15000 });
        // rr-smallclimb carries a lantern in the Behind layer and a clump of reeds in the Front layer.
        await Expect(rows).ToHaveCountAsync(2);
        await Expect(preview).ToHaveAttributeAsync("data-decoration-count", "2", new() { Timeout = 15000 });
        await Page.GetByTestId("dev-parts-fit").ClickAsync();

        LocatorBoundingBoxResult box = await overlay.BoundingBoxAsync() ?? throw new AssertionException("the overlay has no box");
        Handle first = await StableHandleAsync(overlay, "ground", 0, box.Width);
        double pxPerMetre = (first.EndX - first.StartX) / SmallClimbFirstEnd;

        ILocator place = Page.GetByTestId("dev-editor-place-decoration");
        await place.ClickAsync();
        await Expect(place).ToHaveAttributeAsync("aria-pressed", "true");
        await Expect(overlay).ToHaveAttributeAsync("data-tool", "decoration");

        // Eleven metres along the first stretch, on its surface, short of the spike bed at 13 m.
        await Page.Mouse.ClickAsync((float)(box.X + first.StartX + 11 * pxPerMetre), (float)(box.Y + first.Y + 2));

        await Expect(rows).ToHaveCountAsync(3);
        ILocator placed = rows.Nth(2);
        await Expect(placed).ToHaveAttributeAsync("data-kind", "stone-lantern");
        await Expect(placed).ToHaveAttributeAsync("data-layer", "Behind");
        await Expect(code).ToHaveValueAsync(new Regex(@"\{ ""kind"": ""stone-lantern"", ""x"": 1[01](\.\d)?, ""layer"": ""Behind"" \}\s*\]"));
        await Expect(preview).ToHaveAttributeAsync("data-decoration-count", "3", new() { Timeout = 15000 });
        await Expect(preview).ToHaveAttributeAsync("data-decoration-behind-count", "2");
        await Expect(Page.GetByTestId("dev-editor-sound")).ToBeVisibleAsync();

        // Put the tool down, then B moves the selected lantern into the Front layer.
        await place.ClickAsync();
        await Expect(overlay).ToHaveAttributeAsync("data-tool", "");
        await Expect(overlay).ToHaveAttributeAsync("data-selected-kind", "decoration");
        await overlay.FocusAsync();
        await Page.Keyboard.PressAsync("b");
        await Expect(code).ToHaveValueAsync(new Regex(@"\{ ""kind"": ""stone-lantern"", ""x"": 1[01](\.\d)?, ""layer"": ""Front"" \}"));
        await Expect(placed).ToHaveAttributeAsync("data-layer", "Front");
        await Expect(preview).ToHaveAttributeAsync("data-decoration-front-count", "2", new() { Timeout = 15000 });

        // Delete takes the placed lantern and leaves the part's own two decorations.
        await Page.Keyboard.PressAsync("Delete");
        await Expect(rows).ToHaveCountAsync(2);
        await Expect(code).Not.ToHaveValueAsync(new Regex(@"""kind"": ""stone-lantern"", ""x"": 1[01](\.\d)?,"));
        await Expect(preview).ToHaveAttributeAsync("data-decoration-count", "2", new() { Timeout = 15000 });
    }

    /// <summary>
    /// The space tool opens metres at an X and moves everything past it along, takes them back out again, and
    /// refuses a removal that would cut a spike bed, saying so.
    /// </summary>
    [Test]
    public async Task AddAndRemoveSpace_MoveEverythingPastX()
    {
        await OpenOnSmallClimbAsync();

        ILocator code   = Page.GetByTestId("dev-editor-code");
        ILocator atX    = Page.GetByTestId("dev-editor-space-x");
        ILocator amount = Page.GetByTestId("dev-editor-space-amount");

        await atX.FillAsync("10");
        await atX.PressAsync("Tab");
        await amount.FillAsync("3");
        await amount.PressAsync("Tab");

        await Page.GetByTestId("dev-editor-space-add").ClickAsync();
        ILocator firstEnd = Page.GetByTestId("dev-editor-ground-row").First.GetByTestId("dev-editor-ground-end");
        await Expect(firstEnd).ToHaveValueAsync((SmallClimbFirstEnd + 3).ToString(CultureInfo.InvariantCulture));
        await Expect(code).ToHaveValueAsync(new Regex(@"\{ ""startX"": 16, ""endX"": 19 \}"), new() { Timeout = 15000 });
        await Expect(Page.GetByTestId("dev-editor-space-status")).ToContainTextAsync("Added 3 m at 10 m");

        await Page.GetByTestId("dev-editor-space-remove").ClickAsync();
        await Expect(firstEnd).ToHaveValueAsync(SmallClimbFirstEnd.ToString(CultureInfo.InvariantCulture));
        await Expect(code).ToHaveValueAsync(new Regex(@"\{ ""startX"": 13, ""endX"": 16 \}"));

        // The part's first spike bed stands from 13 m to 16 m, so the 3 m from 12 m cut into it.
        await atX.FillAsync("12");
        await atX.PressAsync("Tab");
        await Page.GetByTestId("dev-editor-space-remove").ClickAsync();
        await Expect(Page.GetByTestId("dev-editor-space-status")).ToContainTextAsync("would cut the spike bed");
        await Expect(code).ToHaveValueAsync(new Regex(@"\{ ""startX"": 13, ""endX"": 16 \}"));
    }

    /// <summary>
    /// The reed flowers are placed in the Front layer, and "Play pass-by" plays every clump's bend once in the
    /// preview (the placed one and the part's own) each time it is pressed; a lantern, which has no pass-by
    /// animation, adds nothing to the count.
    /// </summary>
    [Test]
    public async Task PlacedReeds_PlayTheirPassByInThePreview()
    {
        await OpenOnSmallClimbAsync();

        ILocator overlay = Page.GetByTestId("dev-parts-overlay");
        ILocator preview = Page.GetByTestId("dev-parts-preview");
        ILocator rows    = Page.GetByTestId("dev-editor-decoration-row");
        ILocator kind    = Page.GetByTestId("dev-editor-decoration-kind");

        await Expect(kind).ToHaveValueAsync("stone-lantern", new() { Timeout = 15000 });
        await Page.GetByTestId("dev-parts-fit").ClickAsync();
        LocatorBoundingBoxResult box = await overlay.BoundingBoxAsync() ?? throw new AssertionException("the overlay has no box");
        Handle first = await StableHandleAsync(overlay, "ground", 0, box.Width);
        double pxPerMetre = (first.EndX - first.StartX) / SmallClimbFirstEnd;

        // rr-smallclimb carries a lantern and a clump of reeds of its own, listed first.
        await Expect(rows).ToHaveCountAsync(2);
        await kind.SelectOptionAsync("reed-flowers");
        await Page.GetByTestId("dev-editor-place-decoration").ClickAsync();
        await Page.Mouse.ClickAsync((float)(box.X + first.StartX + 8 * pxPerMetre), (float)(box.Y + first.Y + 2));
        await Expect(rows).ToHaveCountAsync(3);
        await Expect(rows.Nth(2)).ToHaveAttributeAsync("data-kind", "reed-flowers");
        await Expect(rows.Nth(2)).ToHaveAttributeAsync("data-layer", "Front");
        await Expect(Page.GetByTestId("dev-editor-code")).ToHaveValueAsync(new Regex(@"""kind"": ""reed-flowers"", ""x"": [\d.]+, ""layer"": ""Front"""));

        await kind.SelectOptionAsync("stone-lantern");
        await Page.Mouse.ClickAsync((float)(box.X + first.StartX + 14 * pxPerMetre), (float)(box.Y + first.Y + 2));
        await Expect(rows).ToHaveCountAsync(4);
        await Expect(rows.Nth(3)).ToHaveAttributeAsync("data-kind", "stone-lantern");
        await Expect(rows.Nth(3)).ToHaveAttributeAsync("data-layer", "Behind");
        await Expect(preview).ToHaveAttributeAsync("data-decoration-count", "4", new() { Timeout = 15000 });
        await Expect(preview).ToHaveAttributeAsync("data-decoration-pass-by-count", "0");

        // Two clumps of reeds, and the two lanterns add nothing.
        ILocator play = Page.GetByTestId("dev-parts-play-pass-by");
        await play.ClickAsync();
        await Expect(preview).ToHaveAttributeAsync("data-decoration-pass-by-count", "2", new() { Timeout = 15000 });
        await Expect(preview).ToHaveAttributeAsync("data-decoration-pass-by-playing", "0", new() { Timeout = 15000 });
        await play.ClickAsync();
        await Expect(preview).ToHaveAttributeAsync("data-decoration-pass-by-count", "4", new() { Timeout = 15000 });
    }

    /// <summary>
    /// The reed flowers' bend plays once when the player's runner reaches them, runs out on the renderer's own
    /// clock back to the rest frame, and plays again on a new run. The renderer is driven directly, with no part
    /// carrying them.
    /// </summary>
    [Test]
    public async Task APassByAnimation_PlaysOnceAsTheRunnerReachesIt()
    {
        await Page.GotoAsync(OfflineUrl);
        await Expect(Page.GetByTestId("mute-toggle")).ToBeVisibleAsync(new() { Timeout = 30000 });

        string json = await Page.EvaluateAsync<string>(@"async () => {
            const { createTimeTrialRenderer } = await import('./js/timeTrialRenderer.js');
            const canvas = document.createElement('canvas');
            canvas.style.width = '360px';
            canvas.style.height = '640px';
            document.body.appendChild(canvas);
            const course = {
                id: 'decoration-pass-by-test', startX: 0, finishX: 40,
                ground: [{ startX: -30, endX: 60, topY: 0 }], pits: [], hazards: [], encounters: [],
                droopingBridges: [], climbs: [],
                decorations: [
                    { id: 'test-1', kind: 'reed-flowers', x: 10, y: 0, layer: 'front', flip: false },
                    { id: 'test-2', kind: 'stone-lantern', x: 12, y: 0, layer: 'behind', flip: true },
                ],
                playerBody: { leftExtent: 0.8, rightExtent: 0.85, height: 1.7 },
            };
            const renderer = createTimeTrialRenderer(canvas, course, { assetBaseUrl: 'art/' });
            const wait = ms => new Promise(resolve => setTimeout(resolve, ms));
            let step = 0;
            const at = x => renderer.render({ step: step++, x, y: 0, vx: 10, vy: 0, mode: 'Running', elapsedMs: step * 16 });
            const read = () => ({
                count: Number(canvas.dataset.decorationPassByCount),
                playing: Number(canvas.dataset.decorationPassByPlaying),
            });

            const result = { decorations: Number(canvas.dataset.decorationCount), front: Number(canvas.dataset.decorationFrontCount) };
            for (let x = 0; x < 9; x += 0.5) at(x);
            result.before = read();
            at(9.6);
            result.atTrigger = read();
            for (let x = 9.6; x < 20; x += 0.5) at(x);
            result.after = read();
            for (let frame = 0; frame < 40; frame++) { await wait(30); at(20); }
            result.ranOut = read();
            step = 0;
            at(0);
            for (let x = 0; x < 20; x += 0.5) at(x);
            result.secondRun = read();
            renderer.dispose();
            canvas.remove();
            return JSON.stringify(result);
        }");

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        Assert.That(root.GetProperty("decorations").GetInt32(), Is.EqualTo(2), "both decorations are known kinds");
        Assert.That(root.GetProperty("front").GetInt32(), Is.EqualTo(1), "the reeds are the one decoration in front");
        Assert.That(root.GetProperty("before").GetProperty("count").GetInt32(), Is.EqualTo(0), "it played before the runner reached it");
        Assert.That(root.GetProperty("atTrigger").GetProperty("count").GetInt32(), Is.EqualTo(1), "reaching it did not set it off");
        Assert.That(root.GetProperty("atTrigger").GetProperty("playing").GetInt32(), Is.EqualTo(1), "it was set off but is not playing");
        Assert.That(root.GetProperty("after").GetProperty("count").GetInt32(), Is.EqualTo(1), "running on past it set it off again");
        Assert.That(root.GetProperty("ranOut").GetProperty("playing").GetInt32(), Is.EqualTo(0), "it never returned to its idle frame");
        Assert.That(root.GetProperty("secondRun").GetProperty("count").GetInt32(), Is.EqualTo(2), "a new run did not set it off again");
    }

    /// <summary>Open the editor offline on a fresh draft of rr-smallclimb.</summary>
    private async Task OpenOnSmallClimbAsync()
    {
        await Page.GotoAsync(BaseUrl + "/dev/parts?env=offline&source=rr-smallclimb");

        await Expect(Page.GetByTestId("dev-parts")).ToBeVisibleAsync(new() { Timeout = 30000 });
        await Expect(Page.GetByTestId("dev-editor-code")).ToHaveValueAsync(new Regex("\"rr-smallclimb\""), new() { Timeout = 30000 });
        await Expect(Page.GetByTestId("dev-parts-overlay")).ToHaveCountAsync(1);
    }

    private sealed record Handle(string Kind, int Index, double StartX, double EndX, double Y);

    /// <summary>
    /// One row's handles as the overlay last wrote them, once they lie on the canvas and have stopped moving:
    /// the overlay rewrites them on the frame after anything moves the camera.
    /// </summary>
    private async Task<Handle> StableHandleAsync(ILocator overlay, string kind, int index, double width)
    {
        Handle? previous = null;
        for (int attempt = 0; attempt < 50; attempt++)
        {
            string json = await overlay.GetAttributeAsync("data-handles") ?? "";
            Handle? current = FindHandle(json, kind, index);
            if (current != null && current.EndX > 0 && current.StartX < width && current == previous)
                return current;

            previous = current;
            await Page.WaitForTimeoutAsync(100);
        }

        throw new AssertionException($"the {kind} {index} handles never settled on the canvas");
    }

    private static Handle? FindHandle(string json, string kind, int index)
    {
        if (string.IsNullOrEmpty(json))
            return null;

        using JsonDocument document = JsonDocument.Parse(json);
        foreach (JsonElement handle in document.RootElement.EnumerateArray())
        {
            if (handle.GetProperty("kind").GetString() == kind && handle.GetProperty("index").GetInt32() == index)
            {
                return new Handle(kind, index, handle.GetProperty("startX").GetDouble(),
                    handle.GetProperty("endX").GetDouble(), handle.GetProperty("y").GetDouble());
            }
        }
        return null;
    }
}
