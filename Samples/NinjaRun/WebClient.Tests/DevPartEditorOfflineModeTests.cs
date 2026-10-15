using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WebClient.Tests;

/// <summary>
/// E2E tests for the developer part editor at <c>/dev/parts</c>, driven through <c>?env=offline</c>, which runs
/// developer-only actions and so needs only the web client. The editor drafts one part, draws it composed alone
/// under an overlay canvas that edits it with the mouse, checks it by the part's own rules and writes it out as
/// a <c>CourseParts.json</c> part object.
/// </summary>
[Parallelizable(ParallelScope.Self)]
[TestFixture]
public class DevPartEditorOfflineModeTests : GamePageTest
{
    private static readonly string BaseUrl =
        (Environment.GetEnvironmentVariable("WEBCLIENT_BASE_URL") ?? "http://localhost:5290").TrimEnd('/');

    private static readonly string OfflineUrl = BaseUrl + "/?env=offline";

    /// <summary>rr-lowland's first stretch of ground ends here, in meters.</summary>
    private const int LowlandFirstEnd = 37;

    [Test]
    public async Task TheMenu_LinksToThePartEditor()
    {
        await Page.GotoAsync(OfflineUrl);

        ILocator link = Page.GetByTestId("open-dev-parts");
        await Expect(link).ToBeVisibleAsync(new() { Timeout = 30000 });
        await link.ClickAsync();

        await Expect(Page).ToHaveURLAsync(new Regex("/dev/parts"));
        await Expect(Page.GetByTestId("dev-parts")).ToBeVisibleAsync(new() { Timeout = 15000 });
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Part editor" })).ToBeVisibleAsync();
        await Expect(Page.GetByTestId("dev-parts-unavailable")).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("dev-parts-overlay")).ToBeVisibleAsync(new() { Timeout = 15000 });

        await Page.GetByTestId("dev-parts-open-courses").ClickAsync();
        await Expect(Page.GetByTestId("dev-courses")).ToBeVisibleAsync(new() { Timeout = 15000 });
    }

    /// <summary>Every part in the course tools opens the editor on a fresh draft of that part.</summary>
    [Test]
    public async Task TheCourseTools_OpenTheEditorOnAPart()
    {
        await Page.GotoAsync(OfflineUrl);

        ILocator link = Page.GetByTestId("open-dev-courses");
        await Expect(link).ToBeVisibleAsync(new() { Timeout = 30000 });
        await link.ClickAsync();

        ILocator edit = Page.Locator("[data-testid='dev-edit-part'][data-part-id='rr-lowland']");
        await Expect(edit).ToBeVisibleAsync(new() { Timeout = 15000 });
        await edit.ClickAsync();

        await Expect(Page).ToHaveURLAsync(new Regex("/dev/parts\\?source=rr-lowland"));
        await Expect(Page.GetByTestId("dev-editor-code")).ToHaveValueAsync(new Regex("\"rr-lowland\""), new() { Timeout = 15000 });
    }

    /// <summary>
    /// A draft started from a part is checked by the part's own rules and written out as a part object. A
    /// broken rule is reported, and the gaps table follows the ground rows.
    /// </summary>
    [Test]
    public async Task ADraft_IsCheckedAndWrittenAsJson()
    {
        await OpenOnLowlandAsync();

        await Expect(Page.GetByTestId("dev-editor-problems").Locator("li")).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("dev-editor-sound")).ToBeVisibleAsync();

        ILocator code = Page.GetByTestId("dev-editor-code");
        await Expect(code).ToHaveValueAsync(new Regex("\"ground\": \\["));

        // The lowland's 3 m gap between its first two stretches and its 5 m pit floor are both ordinary jumps.
        ILocator gaps = Page.GetByTestId("dev-editor-gap");
        await Expect(gaps).ToHaveCountAsync(2);
        await Expect(gaps.Nth(0)).ToHaveAttributeAsync("data-kind", "jump");
        await Expect(gaps.Nth(0)).ToHaveAttributeAsync("data-width", "3");
        await Expect(gaps.Nth(1)).ToHaveAttributeAsync("data-kind", "jump");
        await Expect(gaps.Nth(1)).ToHaveAttributeAsync("data-width", "5");

        // Ending the first stretch at 2 m leaves less than the entry clearance to arrive on, and widens the first
        // gap to 38 m, past what a full charge clears. The canvas stays.
        ILocator groundRows = Page.GetByTestId("dev-editor-ground-row");
        ILocator firstEnd   = groundRows.First.GetByTestId("dev-editor-ground-end");
        await firstEnd.FillAsync("2");
        await firstEnd.PressAsync("Tab");

        await Expect(Page.GetByTestId("dev-editor-problems")).ToContainTextAsync("flat ground to arrive on");
        await Expect(Page.GetByTestId("dev-editor-sound")).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("dev-parts-overlay")).ToHaveAttributeAsync("data-sound", "false");
        await Expect(Page.GetByTestId("dev-parts-preview")).ToBeVisibleAsync();
        await Expect(code).ToHaveValueAsync(new Regex(@"\{ ""startX"": 0, ""endX"": 2, ""topY"": 0 \}"));
        await Expect(gaps.Nth(0)).ToHaveAttributeAsync("data-kind", "run");
        await Expect(gaps.Nth(0)).ToHaveAttributeAsync("data-width", "38");

        // Removing the stretch between the gap and the pit floor joins them into one gap, from 2 m to 44 m: the
        // floor no longer touches higher ground on both sides, so it opens no pit of its own.
        await groundRows.Nth(1).GetByTestId("dev-editor-remove-ground").ClickAsync();
        await Expect(groundRows).ToHaveCountAsync(5);
        await Expect(gaps).ToHaveCountAsync(1);
        await Expect(gaps.First).ToHaveAttributeAsync("data-width", "42");
    }

    /// <summary>
    /// The developer control plays the ghost effects in the preview: one poof at the part's end torii and one
    /// warp-in under its start torii, each time it is pressed.
    /// </summary>
    [Test]
    public async Task PlayGhostEffects_PlaysThePoofAndTheWarpInInThePreview()
    {
        await OpenOnLowlandAsync();

        ILocator preview = Page.GetByTestId("dev-parts-preview");
        ILocator play    = Page.GetByTestId("dev-parts-play-ghost-effects");
        await Expect(play).ToBeVisibleAsync();
        // The preview is drawing once it has written where its camera stands.
        await Expect(preview).ToHaveAttributeAsync("data-preview-x", new Regex(@"\d"), new() { Timeout = 15000 });
        await Expect(preview).ToHaveAttributeAsync("data-ghost-poof-count", "0");

        await play.ClickAsync();
        await Expect(preview).ToHaveAttributeAsync("data-ghost-poof-count", "1", new() { Timeout = 15000 });
        await Expect(preview).ToHaveAttributeAsync("data-ghost-warp-in-count", "1");

        await play.ClickAsync();
        await Expect(preview).ToHaveAttributeAsync("data-ghost-poof-count", "2");
        await Expect(preview).ToHaveAttributeAsync("data-ghost-warp-in-count", "2");
    }

    /// <summary>
    /// Dragging the first stretch's right edge on the canvas moves its end a whole meter at a time, which the
    /// side pane's row and the JSON follow; Delete then removes the stretch the drag selected.
    /// </summary>
    [Test]
    public async Task DraggingAnEdge_ResizesTheStretch_AndDeleteRemovesIt()
    {
        await OpenOnLowlandAsync();

        ILocator overlay = Page.GetByTestId("dev-parts-overlay");
        await Expect(overlay).ToBeVisibleAsync();
        await Page.GetByTestId("dev-parts-fit").ClickAsync();

        LocatorBoundingBoxResult box = await overlay.BoundingBoxAsync() ?? throw new AssertionException("the overlay has no box");
        Handle first = await StableFirstGroundHandleAsync(overlay, box.Width);

        // The handles are the renderer's own projection, so the stretch's drawn width is its length in pixels.
        double pxPerMetre = (first.EndX - first.StartX) / LowlandFirstEnd;
        Assert.That(pxPerMetre, Is.GreaterThan(1), "the first stretch is drawn with no width");

        // Two meters, so the stretch still ends short of the next one, which starts 3 m on.
        const int stretchMetres = 2;
        double fromX = box.X + first.EndX;
        double fromY = box.Y + first.Y + 6;
        await Page.Mouse.MoveAsync((float)fromX, (float)fromY);
        await Page.Mouse.DownAsync();
        await Page.Mouse.MoveAsync((float)(fromX + stretchMetres * pxPerMetre), (float)fromY, new() { Steps = 8 });
        await Page.Mouse.UpAsync();

        string newEnd = (LowlandFirstEnd + stretchMetres).ToString(CultureInfo.InvariantCulture);
        ILocator groundRows = Page.GetByTestId("dev-editor-ground-row");
        await Expect(groundRows.First.GetByTestId("dev-editor-ground-end")).ToHaveValueAsync(newEnd);
        await Expect(Page.GetByTestId("dev-editor-code")).ToHaveValueAsync(new Regex($@"\{{ ""startX"": 0, ""endX"": {newEnd}, ""topY"": 0 \}}"));
        await Expect(overlay).ToHaveAttributeAsync("data-selected-kind", "ground");
        await Expect(overlay).ToHaveAttributeAsync("data-selected-index", "0");
        await Expect(groundRows.First).ToHaveAttributeAsync("data-selected", "true");

        await Page.Keyboard.PressAsync("Delete");
        await Expect(groundRows).ToHaveCountAsync(5);
        await Expect(Page.GetByTestId("dev-editor-code")).Not.ToHaveValueAsync(new Regex($@"""endX"": {newEnd},"));
    }

    /// <summary>
    /// Practice on the draft plays it composed alone on the ordinary run screen, named by the draft's own id,
    /// one the config does not hold, as practice and against nobody. Leaving the run comes back to the editor
    /// with the draft as it was left.
    /// </summary>
    [Test]
    public async Task PracticeOnTheDraft_PlaysItAloneAndReturnsToTheEditor()
    {
        await OpenOnLowlandAsync();
        await RenameDraftAsync("lowland-draft");

        await Page.GetByTestId("dev-parts-practice").ClickAsync();

        ILocator canvas = Page.GetByTestId("time-trial-canvas");
        await Expect(canvas).ToBeVisibleAsync(new() { Timeout = 15000 });
        await Expect(canvas).ToHaveAttributeAsync("data-course-id", "lowland-draft");
        await Expect(canvas).ToHaveAttributeAsync("data-practice", "true");
        await Expect(canvas).ToHaveAttributeAsync("data-opponent-count", "0");
        await Expect(Page.GetByTestId("screen-transition")).ToHaveAttributeAsync("data-transition-state", "hidden");

        await Page.GetByTestId("exit-run").ClickAsync();
        await Expect(Page.GetByTestId("dev-parts")).ToBeVisibleAsync(new() { Timeout = 15000 });
        await Expect(Page.GetByTestId("dev-editor-id")).ToHaveValueAsync("lowland-draft", new() { Timeout = 15000 });
        await Expect(Page.GetByTestId("dev-editor-code")).ToHaveValueAsync(new Regex("\"lowland-draft\""));
    }

    /// <summary>
    /// A race on the draft fields its three scripted runners, driven in the browser: a draft has no pool in any
    /// mode, so the field is the one the server would fall back to.
    /// </summary>
    [Test]
    public async Task RaceOnTheDraft_FieldsThreeOpponents()
    {
        await OpenOnLowlandAsync();
        await RenameDraftAsync("lowland-draft");

        await Page.GetByTestId("dev-parts-race").ClickAsync();

        ILocator canvas = Page.GetByTestId("time-trial-canvas");
        await Expect(canvas).ToBeVisibleAsync(new() { Timeout = 15000 });
        await Expect(canvas).ToHaveAttributeAsync("data-course-id", "lowland-draft");
        await Expect(canvas).ToHaveAttributeAsync("data-practice", "true");
        await Expect(canvas).ToHaveAttributeAsync("data-opponent-count", "3");
        await Expect(Page.GetByTestId("screen-transition")).ToHaveAttributeAsync("data-transition-state", "hidden");

        await Page.GetByTestId("exit-run").ClickAsync();
        await Expect(Page.GetByTestId("dev-parts")).ToBeVisibleAsync(new() { Timeout = 15000 });
    }

    /// <summary>A draft that breaks a rule composes into nothing, so it cannot be played until it is sound again.</summary>
    [Test]
    public async Task ABrokenDraft_CannotBePlayed()
    {
        await OpenOnLowlandAsync();

        ILocator practice = Page.GetByTestId("dev-parts-practice");
        ILocator race     = Page.GetByTestId("dev-parts-race");
        await Expect(practice).ToBeEnabledAsync();
        await Expect(race).ToBeEnabledAsync();

        // Ending the first stretch at 2 m leaves less than the entry clearance to arrive on.
        ILocator firstEnd = Page.GetByTestId("dev-editor-ground-row").First.GetByTestId("dev-editor-ground-end");
        await firstEnd.FillAsync("2");
        await firstEnd.PressAsync("Tab");

        await Expect(Page.GetByTestId("dev-editor-sound")).ToHaveCountAsync(0);
        await Expect(practice).ToBeDisabledAsync();
        await Expect(race).ToBeDisabledAsync();

        await firstEnd.FillAsync(LowlandFirstEnd.ToString(CultureInfo.InvariantCulture));
        await firstEnd.PressAsync("Tab");

        await Expect(Page.GetByTestId("dev-editor-sound")).ToBeVisibleAsync();
        await Expect(practice).ToBeEnabledAsync();
        await Expect(race).ToBeEnabledAsync();
    }

    /// <summary>
    /// The Pit box flags a stretch of ground as a pit floor, which the JSON writes as <c>"pit": true</c> and the
    /// part's rules check: the first stretch cannot be one, since a pit's lips are in the same part as its floor.
    /// P on the canvas toggles the selected stretch's flag the same way.
    /// </summary>
    [Test]
    public async Task ThePitBox_FlagsAPitFloor_AndItsRulesAreChecked()
    {
        // rr-spikesteps flags no pit floor of its own, so every "pit": true in the JSON is the test's.
        await OpenOnPartAsync("rr-spikesteps");

        ILocator code      = Page.GetByTestId("dev-editor-code");
        ILocator problems  = Page.GetByTestId("dev-editor-problems");
        ILocator firstRow  = Page.GetByTestId("dev-editor-ground-row").First;
        ILocator firstPit  = firstRow.GetByRole(AriaRole.Checkbox, new() { Name = "Pit floor" });
        await Expect(code).Not.ToHaveValueAsync(new Regex("\"pit\": true"));
        await Expect(firstPit).Not.ToBeCheckedAsync();

        await firstPit.CheckAsync();

        await Expect(firstRow).ToHaveAttributeAsync("data-pit", "true");
        await Expect(code).ToHaveValueAsync(new Regex(@"\{ ""startX"": 0, ""endX"": 15, ""topY"": 0, ""pit"": true \}"));
        await Expect(problems).ToContainTextAsync("is the part's first stretch of ground");
        await Expect(Page.GetByTestId("dev-editor-sound")).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("dev-parts-practice")).ToBeDisabledAsync();

        await firstPit.UncheckAsync();

        await Expect(firstRow).ToHaveAttributeAsync("data-pit", "false");
        await Expect(code).Not.ToHaveValueAsync(new Regex("\"pit\": true"));
        await Expect(Page.GetByTestId("dev-editor-sound")).ToBeVisibleAsync();

        // Selecting the stretch from its row and pressing P on the canvas flags it again.
        ILocator overlay = Page.GetByTestId("dev-parts-overlay");
        await firstRow.GetByTestId("dev-editor-ground-pick").ClickAsync();
        await Expect(overlay).ToHaveAttributeAsync("data-selected-kind", "ground");
        await Expect(overlay).ToHaveAttributeAsync("data-selected-index", "0");
        await overlay.FocusAsync();
        await Page.Keyboard.PressAsync("p");

        await Expect(firstPit).ToBeCheckedAsync();
        await Expect(code).ToHaveValueAsync(new Regex("\"pit\": true"));
        await Expect(problems).ToContainTextAsync("is the part's first stretch of ground");
    }

    /// <summary>Give the draft an id of its own, so the run it opens is named by something the config does not hold.</summary>
    private async Task RenameDraftAsync(string id)
    {
        ILocator idField = Page.GetByTestId("dev-editor-id");
        await idField.FillAsync(id);
        await idField.PressAsync("Tab");
        await Expect(Page.GetByTestId("dev-editor-code")).ToHaveValueAsync(new Regex($"\"{Regex.Escape(id)}\""));
        await Expect(Page.GetByTestId("dev-parts-practice")).ToBeEnabledAsync();
    }

    /// <summary>Open the editor offline on a fresh draft of rr-lowland.</summary>
    private Task OpenOnLowlandAsync() => OpenOnPartAsync("rr-lowland");

    /// <summary>Open the editor offline on a fresh draft of the part <paramref name="partId"/>.</summary>
    private async Task OpenOnPartAsync(string partId)
    {
        await Page.GotoAsync(BaseUrl + "/dev/parts?env=offline&source=" + partId);

        await Expect(Page.GetByTestId("dev-parts")).ToBeVisibleAsync(new() { Timeout = 30000 });
        await Expect(Page.GetByTestId("dev-editor-code")).ToHaveValueAsync(new Regex($"\"{Regex.Escape(partId)}\""), new() { Timeout = 30000 });
        await Expect(Page.GetByTestId("dev-parts-overlay")).ToHaveCountAsync(1);
    }

    private sealed record Handle(string Kind, int Index, double StartX, double EndX, double Y);

    /// <summary>
    /// The first stretch of ground's handles as the overlay last wrote them, once they lie on the canvas and
    /// have stopped moving: the overlay rewrites them on the frame after anything moves the camera.
    /// </summary>
    private async Task<Handle> StableFirstGroundHandleAsync(ILocator overlay, double width)
    {
        Handle? previous = null;
        for (int attempt = 0; attempt < 50; attempt++)
        {
            string json = await overlay.GetAttributeAsync("data-handles") ?? "";
            Handle? current = FirstGroundHandle(json);
            if (current != null && current.EndX > 0 && current.EndX < width && current == previous)
                return current;

            previous = current;
            await Page.WaitForTimeoutAsync(100);
        }

        throw new AssertionException("the first stretch's handles never settled on the canvas");
    }

    private static Handle? FirstGroundHandle(string json)
    {
        if (string.IsNullOrEmpty(json))
            return null;

        using JsonDocument document = JsonDocument.Parse(json);
        foreach (JsonElement handle in document.RootElement.EnumerateArray())
        {
            if (handle.GetProperty("kind").GetString() == "ground" && handle.GetProperty("index").GetInt32() == 0)
            {
                return new Handle("ground", 0, handle.GetProperty("startX").GetDouble(),
                    handle.GetProperty("endX").GetDouble(), handle.GetProperty("y").GetDouble());
            }
        }
        return null;
    }
}
