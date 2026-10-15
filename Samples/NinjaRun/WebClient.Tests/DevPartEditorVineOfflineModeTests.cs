using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WebClient.Tests;

/// <summary>
/// E2E tests for hanging vines in the developer part editor at <c>/dev/parts</c>, driven through
/// <c>?env=offline</c> so they need only the web client. Add hangs a vine on the draft where it first fits the
/// part's rules; it is listed in the side pane, written into the part JSON's <c>vines</c>, drawn by the overlay
/// as an anchor, a rope and a platform handle, drawn by the preview, and played on the run screen when the draft
/// is.
/// </summary>
[Parallelizable(ParallelScope.Self)]
[TestFixture]
public class DevPartEditorVineOfflineModeTests : GamePageTest
{
    private static readonly string BaseUrl =
        (Environment.GetEnvironmentVariable("WEBCLIENT_BASE_URL") ?? "http://localhost:5290").TrimEnd('/');

    /// <summary>The part the editor is opened on: one of the handcrafted parts, which carries no vine of its own.</summary>
    private const string SourcePart = "rr-lowland";

    private static readonly Regex VineJson = new Regex(
        @"""vines"": \[\s*\{ ""anchorX"": -?\d+, ""anchorY"": -?[\d.]+, ""platformStartX"": -?\d+, ""platformEndX"": -?\d+, ""platformTopY"": -?[\d.]+ \}\s*\]");

    /// <summary>
    /// Add hangs one vine on the draft where it breaks no rule: a row in the side pane, the JSON's <c>vines</c>,
    /// three overlay handles and one vine drawn by the preview, and the draft still sound. Remove takes it all
    /// away again.
    /// </summary>
    [Test]
    public async Task AddingAVine_ListsItWritesItAndDrawsItsHandles()
    {
        await OpenOnSourceAsync();

        ILocator overlay = Page.GetByTestId("dev-parts-overlay");
        ILocator preview = Page.GetByTestId("dev-parts-preview");
        ILocator code    = Page.GetByTestId("dev-editor-code");
        ILocator rows    = Page.GetByTestId("dev-editor-vine-row");

        await Expect(code).Not.ToHaveValueAsync(new Regex("\"vines\""));
        await Expect(rows).ToHaveCountAsync(0);
        await Expect(preview).ToHaveAttributeAsync("data-vine-count", "0", new() { Timeout = 15000 });

        await Page.GetByTestId("dev-editor-add-vine").ClickAsync();

        await Expect(rows).ToHaveCountAsync(1);
        await Expect(rows.First).ToHaveAttributeAsync("data-selected", "true");
        await Expect(code).ToHaveValueAsync(VineJson);
        await Expect(Page.GetByTestId("dev-editor-problems").Locator("li")).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("dev-editor-sound")).ToBeVisibleAsync();
        await Expect(preview).ToHaveAttributeAsync("data-vine-count", "1", new() { Timeout = 15000 });
        await Expect(overlay).ToHaveAttributeAsync("data-selected-kind", "vine");
        await Expect(overlay).ToHaveAttributeAsync("data-selected-index", "0");

        // The anchor, rope and platform are each a handle on the overlay, the platform ahead of the anchor.
        List<Handle> handles = await VineHandlesAsync(overlay, 0);
        Assert.That(handles.Select(handle => handle.Part), Is.EquivalentTo(new[] { "anchor", "rope", "platform" }));
        Handle anchor   = handles.Single(handle => handle.Part == "anchor");
        Handle platform = handles.Single(handle => handle.Part == "platform");
        Assert.That(platform.StartX, Is.GreaterThan(anchor.StartX), "the platform is not drawn ahead of the anchor");
        Assert.That(platform.EndX, Is.GreaterThan(platform.StartX), "the platform is drawn with no width");
        Assert.That(platform.Y, Is.GreaterThan(anchor.Y), "the platform is not drawn below the anchor");

        // The side pane's X fields are the JSON's own whole metres. Its height fields are in decimetres.
        ILocator row = rows.First;
        string anchorX       = await row.GetByTestId("dev-editor-vine-anchor-x").InputValueAsync();
        string platformStart = await row.GetByTestId("dev-editor-vine-platform-start").InputValueAsync();
        await Expect(code).ToHaveValueAsync(new Regex($@"""anchorX"": {anchorX}, ""anchorY"": [\d.]+, ""platformStartX"": {platformStart},"));

        await row.GetByTestId("dev-editor-remove-vine").ClickAsync();
        await Expect(rows).ToHaveCountAsync(0);
        await Expect(code).Not.ToHaveValueAsync(new Regex("\"vines\""));
        await Expect(preview).ToHaveAttributeAsync("data-vine-count", "0", new() { Timeout = 15000 });
    }

    /// <summary>
    /// Dragging the anchor sideways moves the whole vine, platform and all, a whole metre at a time, which the
    /// side pane's row and the JSON follow. Delete then removes the vine the drag selected.
    /// </summary>
    [Test]
    public async Task DraggingTheAnchor_MovesTheVine_AndDeleteRemovesIt()
    {
        await OpenOnSourceAsync();

        ILocator overlay = Page.GetByTestId("dev-parts-overlay");
        ILocator rows    = Page.GetByTestId("dev-editor-vine-row");
        await Page.GetByTestId("dev-parts-fit").ClickAsync();
        await Page.GetByTestId("dev-editor-add-vine").ClickAsync();
        await Expect(rows).ToHaveCountAsync(1);

        ILocator row = rows.First;
        int anchorX       = int.Parse(await row.GetByTestId("dev-editor-vine-anchor-x").InputValueAsync(), CultureInfo.InvariantCulture);
        int platformStart = int.Parse(await row.GetByTestId("dev-editor-vine-platform-start").InputValueAsync(), CultureInfo.InvariantCulture);
        int platformEnd   = int.Parse(await row.GetByTestId("dev-editor-vine-platform-end").InputValueAsync(), CultureInfo.InvariantCulture);

        LocatorBoundingBoxResult box = await overlay.BoundingBoxAsync() ?? throw new AssertionException("the overlay has no box");
        List<Handle> handles = await VineHandlesAsync(overlay, 0);
        Handle anchor   = handles.Single(handle => handle.Part == "anchor");
        Handle platform = handles.Single(handle => handle.Part == "platform");
        Assert.That(anchor.StartX, Is.GreaterThan(0).And.LessThan(box.Width), "the anchor is off the canvas");
        Assert.That(anchor.Y, Is.GreaterThan(0).And.LessThan(box.Height), "the anchor is off the canvas");

        // The platform handle's width is its length in pixels.
        double pxPerMetre = (platform.EndX - platform.StartX) / (platformEnd - platformStart);
        Assert.That(pxPerMetre, Is.GreaterThan(1), "the platform is drawn with no width");

        const int moveMetres = 2;
        double fromX = box.X + (anchor.StartX + anchor.EndX) / 2;
        double fromY = box.Y + anchor.Y;
        await Page.Mouse.MoveAsync((float)fromX, (float)fromY);
        await Page.Mouse.DownAsync();
        await Page.Mouse.MoveAsync((float)(fromX + moveMetres * pxPerMetre), (float)fromY, new() { Steps = 8 });
        await Page.Mouse.UpAsync();

        string newAnchor        = (anchorX + moveMetres).ToString(CultureInfo.InvariantCulture);
        string newPlatformStart = (platformStart + moveMetres).ToString(CultureInfo.InvariantCulture);
        string newPlatformEnd   = (platformEnd + moveMetres).ToString(CultureInfo.InvariantCulture);
        await Expect(row.GetByTestId("dev-editor-vine-anchor-x")).ToHaveValueAsync(newAnchor);
        await Expect(row.GetByTestId("dev-editor-vine-platform-start")).ToHaveValueAsync(newPlatformStart);
        await Expect(row.GetByTestId("dev-editor-vine-platform-end")).ToHaveValueAsync(newPlatformEnd);
        await Expect(Page.GetByTestId("dev-editor-code")).ToHaveValueAsync(
            new Regex($@"""anchorX"": {newAnchor}, ""anchorY"": [\d.]+, ""platformStartX"": {newPlatformStart}, ""platformEndX"": {newPlatformEnd},"));
        await Expect(overlay).ToHaveAttributeAsync("data-selected-kind", "vine");

        await overlay.FocusAsync();
        await Page.Keyboard.PressAsync("Delete");
        await Expect(rows).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("dev-editor-code")).Not.ToHaveValueAsync(new Regex("\"vines\""));
    }

    /// <summary>The vine Place tool hangs a vine where the canvas is clicked, selected, and Escape puts the tool down.</summary>
    [Test]
    public async Task ThePlaceTool_HangsAVineWhereTheCanvasIsClicked()
    {
        await OpenOnSourceAsync();

        ILocator overlay = Page.GetByTestId("dev-parts-overlay");
        ILocator rows    = Page.GetByTestId("dev-editor-vine-row");
        await Page.GetByTestId("dev-parts-fit").ClickAsync();

        LocatorBoundingBoxResult box = await overlay.BoundingBoxAsync() ?? throw new AssertionException("the overlay has no box");
        Handle first = await StableHandleAsync(overlay, "ground", 0, box.Width);

        ILocator place = Page.GetByTestId("dev-editor-place-vine");
        await place.ClickAsync();
        await Expect(place).ToHaveAttributeAsync("aria-pressed", "true");
        await Expect(overlay).ToHaveAttributeAsync("data-tool", "vine");

        // Well inside the first stretch of ground, above its surface.
        await Page.Mouse.ClickAsync((float)(box.X + (first.StartX + first.EndX) / 2), (float)(box.Y + first.Y - 40));

        await Expect(rows).ToHaveCountAsync(1);
        await Expect(rows.First).ToHaveAttributeAsync("data-selected", "true");
        await Expect(Page.GetByTestId("dev-editor-code")).ToHaveValueAsync(VineJson);
        await Expect(overlay).ToHaveAttributeAsync("data-selected-kind", "vine");
        Assert.That(await VineHandlesAsync(overlay, 0), Has.Count.EqualTo(3));

        await overlay.FocusAsync();
        await Page.Keyboard.PressAsync("Escape");
        await Expect(overlay).ToHaveAttributeAsync("data-tool", "");
        await Expect(place).ToHaveAttributeAsync("aria-pressed", "false");
    }

    /// <summary>
    /// Practice on a draft with a vine plays it on the ordinary run screen composed alone, and the run canvas
    /// draws the vine the draft carries.
    /// </summary>
    [Test]
    public async Task PracticeOnADraftWithAVine_DrawsTheVineOnTheRunCanvas()
    {
        await OpenOnSourceAsync();

        await Page.GetByTestId("dev-editor-add-vine").ClickAsync();
        await Expect(Page.GetByTestId("dev-editor-vine-row")).ToHaveCountAsync(1);
        await Expect(Page.GetByTestId("dev-editor-sound")).ToBeVisibleAsync();

        ILocator idField = Page.GetByTestId("dev-editor-id");
        await idField.FillAsync("vine-draft");
        await idField.PressAsync("Tab");
        await Expect(Page.GetByTestId("dev-editor-code")).ToHaveValueAsync(new Regex("\"vine-draft\""));
        await Expect(Page.GetByTestId("dev-parts-practice")).ToBeEnabledAsync();

        await Page.GetByTestId("dev-parts-practice").ClickAsync();

        ILocator canvas = Page.GetByTestId("time-trial-canvas");
        await Expect(canvas).ToBeVisibleAsync(new() { Timeout = 15000 });
        await Expect(canvas).ToHaveAttributeAsync("data-course-id", "vine-draft");
        await Expect(canvas).ToHaveAttributeAsync("data-practice", "true");
        await Expect(canvas).ToHaveAttributeAsync("data-vine-count", "1", new() { Timeout = 15000 });

        await Page.GetByTestId("exit-run").ClickAsync();
        await Expect(Page.GetByTestId("dev-parts")).ToBeVisibleAsync(new() { Timeout = 15000 });
        await Expect(Page.GetByTestId("dev-editor-vine-row")).ToHaveCountAsync(1, new() { Timeout = 15000 });
    }

    /// <summary>Open the editor offline on a fresh draft of the source part.</summary>
    private async Task OpenOnSourceAsync()
    {
        await Page.GotoAsync(BaseUrl + $"/dev/parts?env=offline&source={SourcePart}");

        await Expect(Page.GetByTestId("dev-parts")).ToBeVisibleAsync(new() { Timeout = 30000 });
        await Expect(Page.GetByTestId("dev-editor-code")).ToHaveValueAsync(new Regex($"\"{SourcePart}\""), new() { Timeout = 30000 });
        await Expect(Page.GetByTestId("dev-parts-overlay")).ToHaveCountAsync(1);
        await Expect(Page.GetByTestId("dev-editor-sound")).ToBeVisibleAsync();
    }

    private sealed record Handle(string Kind, string Part, int Index, double StartX, double EndX, double Y);

    /// <summary>
    /// The vine's handles as the overlay last wrote them, once all three are there and have stopped moving: the
    /// overlay rewrites them on the frame after anything moves the camera or the draft.
    /// </summary>
    private async Task<List<Handle>> VineHandlesAsync(ILocator overlay, int index)
    {
        string? previous = null;
        for (int attempt = 0; attempt < 50; attempt++)
        {
            string json = await overlay.GetAttributeAsync("data-handles") ?? "";
            List<Handle> current = FindHandles(json, "vine", index);
            string signature = string.Join("|", current);
            if (current.Count == 3 && signature == previous)
                return current;

            previous = signature;
            await Page.WaitForTimeoutAsync(100);
        }

        throw new AssertionException($"the vine {index} handles never settled on the overlay");
    }

    /// <summary>One row's handle as the overlay last wrote it, once it lies on the canvas and has stopped moving.</summary>
    private async Task<Handle> StableHandleAsync(ILocator overlay, string kind, int index, double width)
    {
        Handle? previous = null;
        for (int attempt = 0; attempt < 50; attempt++)
        {
            string json = await overlay.GetAttributeAsync("data-handles") ?? "";
            Handle? current = FindHandles(json, kind, index).FirstOrDefault();
            if (current != null && current.EndX > 0 && current.StartX < width && current == previous)
                return current;

            previous = current;
            await Page.WaitForTimeoutAsync(100);
        }

        throw new AssertionException($"the {kind} {index} handles never settled on the canvas");
    }

    private static List<Handle> FindHandles(string json, string kind, int index)
    {
        List<Handle> found = new List<Handle>();
        if (string.IsNullOrEmpty(json))
            return found;

        using JsonDocument document = JsonDocument.Parse(json);
        foreach (JsonElement handle in document.RootElement.EnumerateArray())
        {
            if (handle.GetProperty("kind").GetString() != kind || handle.GetProperty("index").GetInt32() != index)
                continue;

            string part = handle.TryGetProperty("part", out JsonElement partElement) ? partElement.GetString() ?? "" : "";
            found.Add(new Handle(kind, part, index, handle.GetProperty("startX").GetDouble(),
                handle.GetProperty("endX").GetDouble(), handle.GetProperty("y").GetDouble()));
        }
        return found;
    }
}
