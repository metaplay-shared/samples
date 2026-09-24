using Microsoft.Playwright;
using NUnit.Framework;
using System;
using System.IO;

namespace Game.Client.Tests;

/// <summary>
/// Captures the board in each of its named states, at 1280 x 720, as PNGs on disk.
/// <para>
/// <b>Marked <c>[Explicit]</c>, so only a filter that names it runs it; <c>run-e2e.sh</c> does.</b> It asserts little:
/// it is the tool the match screen's visual work is done with, because judging a board against its art direction
/// means looking at the board. What it does assert is that each scene drew a board at all — a capture of a
/// page that failed to render is worse than no capture, because it looks like a design regression.
/// </para>
/// <para>
/// It needs the <b>web client only</b>: the scenes are played out in the browser from a seeded deal through
/// the shared rules, so there is no server and no match. That is what makes it cheap enough to run on every
/// pass, and deterministic enough that two runs differ only where the implementation did.
/// </para>
/// <code>
/// dotnet run --project Client/Client.csproj                  # terminal 1
/// dotnet test Client.Tests/Client.Tests.csproj \
///     --settings Client.Tests/playwright.runsettings \
///     --filter "FullyQualifiedName~BoardScreenshots"
/// </code>
/// <para>
/// <c>STICKYPAWS_SHOT_DIR</c> chooses where the PNGs go, and <c>STICKYPAWS_WEB_BASE</c> — which every fixture
/// here reads, through <see cref="MetaScreenTestBase.BaseUrl"/> — which web client to drive.
/// </para>
/// </summary>
[TestFixture]
[Explicit("Captures board screenshots for visual work; it is a tool rather than a test.")]
[NonParallelizable]
public class BoardScreenshots : MatchTestBase
{
    /// <summary> The board is drawn at one aspect ratio and scaled to fit, so a capture fixes the window. </summary>
    const int ShotWidth  = 1280;
    const int ShotHeight = 720;

    /// <summary> Where the PNGs land. One directory per pass, so a pass can be compared with the last. </summary>
    static string ShotDirectory => Environment.GetEnvironmentVariable("STICKYPAWS_SHOT_DIR") is { Length: > 0 } value
        ? value
        : Path.Combine(Path.GetTempPath(), "stickypaws-ui-shots");

    [Test]
    public async Task Mulligan() => await CaptureAsync("mulligan");

    [Test]
    public async Task MidTurn() => await CaptureAsync("midturn");

    [Test]
    public async Task Inspect() => await CaptureAsync("inspect");

    [Test]
    public async Task Heal() => await CaptureAsync("heal");

    [Test]
    public async Task Sneaky() => await CaptureAsync("sneaky");

    [Test]
    public async Task Peek() => await CaptureAsync("peek");

    [Test]
    public async Task End() => await CaptureAsync("end");

    [Test]
    public async Task Struck() => await CaptureAsync("struck");

    [Test]
    public async Task Covered() => await CaptureAsync("covered");

    [Test]
    public async Task CoveredWaiting() => await CaptureAsync("covered-waiting");

    [Test]
    public async Task CoveredReclaiming() => await CaptureAsync("covered-reclaiming");

    [Test]
    public async Task Heist() => await CaptureAsync("heist");

    [Test]
    public async Task HeistTaken() => await CaptureAsync("heist-taken");

    /// <summary>
    /// The raised summary face and parchment tooltip, as the board opens them beside a hovered hand card.
    /// Captured from the mid-turn scene because that is where a hand exists to hover.
    /// </summary>
    [Test]
    public async Task CardFacePeek()
    {
        await OpenAsync("inspect");

        ILocator card = Page.GetByTestId("hand-card").First;
        await card.HoverAsync();
        await Expect(card.GetByTestId("card-tooltip")).ToBeVisibleAsync(new() { Timeout = MatchTimeout });
        await Page.WaitForTimeoutAsync(250);

        string path = Path.Combine(ShotDirectory, "card-peek.png");
        await Page.ScreenshotAsync(new PageScreenshotOptions { Path = path, FullPage = true });
        TestContext.Out.WriteLine($"card-peek → {path}");
    }

    [Test]
    public async Task MobileLandscape()
    {
        await OpenAsync("inspect", width: 844, height: 390);

        string path = Path.Combine(ShotDirectory, "mobile-landscape.png");
        await Page.ScreenshotAsync(new PageScreenshotOptions { Path = path, FullPage = true });
        TestContext.Out.WriteLine($"mobile-landscape → {path}");
    }

    /// <summary>
    /// What size the board actually draws each piece of art at. The art has to survive these
    /// numbers and nothing else, and computing them from the stylesheet by hand is how art ends up drawn
    /// for the wrong scale — so they are measured off a real board and written beside the screenshots.
    /// </summary>
    [Test]
    public async Task MeasuredArtSizes()
    {
        await OpenAsync("midturn");

        string[] rows = await Page.EvaluateAsync<string[]>(
            """
            (() => {
              const seen = new Map();
              for (const img of document.querySelectorAll('img[src^="art/"]')) {
                const box = img.getBoundingClientRect();
                const key = img.getAttribute('src');
                if (box.width < 1) continue;
                const size = Math.round(box.width) + ' x ' + Math.round(box.height);
                if (!seen.has(key)) seen.set(key, size);
              }
              const faces = document.querySelectorAll('[data-testid="card-face"]');
              const extra = [];
              if (faces.length) {
                const b = faces[0].getBoundingClientRect();
                extra.push('card-face (Summary)|' + Math.round(b.width) + ' x ' + Math.round(b.height));
              }
              const den = document.querySelector('.den');
              if (den) {
                const b = den.getBoundingClientRect();
                extra.push('den box|' + Math.round(b.width) + ' x ' + Math.round(b.height));
              }
              return [...Array.from(seen, ([k, v]) => k + '|' + v), ...extra].sort();
            })()
            """);

        string path = Path.Combine(ShotDirectory, "measured-art-sizes.txt");
        await File.WriteAllLinesAsync(path, rows);

        foreach (string row in rows)
            TestContext.Out.WriteLine(row);
    }

    /// <summary>
    /// Open one scene at the capture size. Reduced motion is on, because a capture taken mid-animation is a
    /// capture of a frame nobody designed; the preview's own corner label identifies the scene.
    /// </summary>
    async Task OpenAsync(string scene, int width = ShotWidth, int height = ShotHeight)
    {
        Directory.CreateDirectory(ShotDirectory);

        await Page.SetViewportSizeAsync(width, height);
        await Page.GotoAsync($"{BaseUrl}/dev/board?scene={scene}&env=offline&motion=reduced");

        // The scene is played out in the browser once the session has brought the content, so this waits for
        // a board rather than for a page.
        await Expect(Page.GetByTestId("match-board")).ToBeVisibleAsync(new() { Timeout = MatchTimeout });
        await Expect(Page.GetByTestId("dev-scene-label")).ToHaveAttributeAsync(
            "data-scene", scene, new() { Timeout = MatchTimeout });
    }

    /// <summary>
    /// Open one scene and save it.
    /// </summary>
    async Task CaptureAsync(string scene)
    {
        await OpenAsync(scene);

        // The consequence tag on a target is a hover reveal, so a capture of it has to hover one — which is
        // also the only way to look at it at all.
        ILocator target = Page.Locator("[data-testid='board-critter'][data-targetable='true']");
        if (await target.CountAsync() > 0)
        {
            await target.First.HoverAsync();
            // The reveal is a CSS transition. Reduced motion collapses it, but the pointer still has to have
            // arrived before the frame is grabbed.
            await Page.WaitForTimeoutAsync(250);
        }

        string path = Path.Combine(ShotDirectory, $"{scene}.png");
        await Page.ScreenshotAsync(new PageScreenshotOptions { Path = path, FullPage = true });

        // Which test ids this board actually carries. The fixtures locate by test id, several of them are
        // passed into the card face as parameters rather than written on an element, and a grep over the
        // source cannot see those — so the capture lists what the DOM has.
        await File.WriteAllLinesAsync(
            Path.Combine(ShotDirectory, $"{scene}.testids.txt"),
            await Page.EvaluateAsync<string[]>(
                "Array.from(new Set(Array.from(document.querySelectorAll('[data-testid]'))"
                + ".map(e => e.getAttribute('data-testid')))).sort()"));

        TestContext.Out.WriteLine($"{scene} → {path}");
        Assert.That(File.Exists(path), Is.True, $"no screenshot was written for '{scene}'");

        // Only the two lines that mean the page itself broke. The offline session logs at debug level and
        // its traffic mentions plenty of words a broader filter would call a failure.
        foreach (string line in ConsoleLog)
        {
            Assert.That(line.Contains("[pageerror]") || line.Contains("frame failure"), Is.False,
                $"the '{scene}' scene broke while rendering: {line}");
        }
    }
}
