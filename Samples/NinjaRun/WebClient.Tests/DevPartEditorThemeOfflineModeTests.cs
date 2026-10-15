using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;
using System.Text.RegularExpressions;

namespace WebClient.Tests;

/// <summary>
/// E2E tests for the part editor's theme: the altitude band a draft is drawn in. A part alone has no climb to
/// carry it up the mountain, so the editor lets the author pick the band the preview and the draft's runs
/// stand in; a map's parts take theirs from their position and never read it.
/// </summary>
[Parallelizable(ParallelScope.Self)]
[TestFixture]
public class DevPartEditorThemeOfflineModeTests : GamePageTest
{
    private static readonly string BaseUrl =
        (Environment.GetEnvironmentVariable("WEBCLIENT_BASE_URL") ?? "http://localhost:5290").TrimEnd('/');

    /// <summary>The bands every course climbs through, lowest first, as the renderer names them on the canvas.</summary>
    private const string EveryBand = "foothills mid-ridge summit";

    /// <summary>
    /// The preview opens in the foothills, like any course, and the theme moves it to the chosen band and every
    /// band above it, so the crest stages the canvas may draw start at the theme.
    /// </summary>
    [Test]
    public async Task TheTheme_PicksTheBandTheDraftIsPreviewedIn()
    {
        await OpenOnLowlandAsync();

        ILocator preview = Page.GetByTestId("dev-parts-preview");
        ILocator theme   = Page.GetByTestId("dev-editor-theme");
        await Expect(theme).ToHaveValueAsync("foothills");
        await Expect(preview).ToHaveAttributeAsync("data-terrain-crest-stages", EveryBand, new() { Timeout = 15000 });

        await theme.SelectOptionAsync("summit");
        await Expect(preview).ToHaveAttributeAsync("data-terrain-crest-stages", "summit", new() { Timeout = 15000 });

        await theme.SelectOptionAsync("mid-ridge");
        await Expect(preview).ToHaveAttributeAsync("data-terrain-crest-stages", "mid-ridge summit", new() { Timeout = 15000 });
    }

    /// <summary>
    /// The theme is the draft's, so a practice run on the draft is drawn in it too, and it is still set when
    /// the run comes back to the editor. The theme is not part of the part: the JSON pane never carries it.
    /// </summary>
    [Test]
    public async Task TheTheme_FollowsTheDraftOntoTheRunScreenAndBack()
    {
        await OpenOnLowlandAsync();

        ILocator idField = Page.GetByTestId("dev-editor-id");
        await idField.FillAsync("lowland-theme-draft");
        await idField.PressAsync("Tab");
        await Expect(Page.GetByTestId("dev-editor-code")).ToHaveValueAsync(new Regex("\"lowland-theme-draft\""));
        await Expect(Page.GetByTestId("dev-parts-practice")).ToBeEnabledAsync();

        await Page.GetByTestId("dev-editor-theme").SelectOptionAsync("summit");
        await Expect(Page.GetByTestId("dev-parts-preview")).ToHaveAttributeAsync("data-terrain-crest-stages", "summit", new() { Timeout = 15000 });
        await Expect(Page.GetByTestId("dev-editor-code")).Not.ToHaveValueAsync(new Regex("\"summit\"|\"altitudeBand\"|\"theme\""));

        await Page.GetByTestId("dev-parts-practice").ClickAsync();

        ILocator canvas = Page.GetByTestId("time-trial-canvas");
        await Expect(canvas).ToBeVisibleAsync(new() { Timeout = 15000 });
        await Expect(canvas).ToHaveAttributeAsync("data-course-id", "lowland-theme-draft");
        await Expect(canvas).ToHaveAttributeAsync("data-terrain-crest-stages", "summit");
        await Expect(Page.GetByTestId("screen-transition")).ToHaveAttributeAsync("data-transition-state", "hidden");

        await Page.GetByTestId("exit-run").ClickAsync();
        await Expect(Page.GetByTestId("dev-parts")).ToBeVisibleAsync(new() { Timeout = 15000 });
        await Expect(Page.GetByTestId("dev-editor-theme")).ToHaveValueAsync("summit", new() { Timeout = 15000 });
        await Expect(Page.GetByTestId("dev-parts-preview")).ToHaveAttributeAsync("data-terrain-crest-stages", "summit", new() { Timeout = 15000 });
    }

    /// <summary>Open the editor offline on a fresh draft of the lowland part.</summary>
    private async Task OpenOnLowlandAsync()
    {
        await Page.GotoAsync(BaseUrl + "/dev/parts?env=offline&source=rr-lowland");

        await Expect(Page.GetByTestId("dev-parts")).ToBeVisibleAsync(new() { Timeout = 30000 });
        await Expect(Page.GetByTestId("dev-editor-code")).ToHaveValueAsync(new Regex("\"rr-lowland\""), new() { Timeout = 30000 });
        await Expect(Page.GetByTestId("dev-parts-overlay")).ToHaveCountAsync(1);
    }
}
