using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;
using System.Text.Json;

namespace WebClient.Tests;

/// <summary>
/// E2E tests for the runner's animations in the run renderer, driven through <c>?env=offline</c> so they need
/// only the web client. The renderer is created directly over a small course and fed snapshots, and the canvas'
/// data attributes say which frame the runner was drawn in and how many high-landing rolls and climb frames
/// there have been.
/// </summary>
[Parallelizable(ParallelScope.Self)]
[TestFixture]
public class RunnerAnimationOfflineModeTests : GamePageTest
{
    private static readonly string BaseUrl =
        (Environment.GetEnvironmentVariable("WEBCLIENT_BASE_URL") ?? "http://localhost:5290").TrimEnd('/');

    private static readonly string OfflineUrl = BaseUrl + "/?env=offline";

    /// <summary>
    /// A shelf at 6 m, a 2 m pit down to the floor at 0 m, and the ground after it at 2 m, whose far wall is the
    /// catchable face out of the pit. The course carries runner rules, so a landing rolls when its drop is more
    /// than the renderer's <c>RUNNER_TUNING.highLandingDrop</c>.
    /// </summary>
    private const string SetUpRenderer = @"
        const { createTimeTrialRenderer } = await import('./js/timeTrialRenderer.js');
        const canvas = document.createElement('canvas');
        canvas.style.width = '360px';
        canvas.style.height = '640px';
        document.body.appendChild(canvas);
        const course = {
            id: 'runner-animation-test', startX: 0, finishX: 60,
            ground: [{ startX: -30, endX: 20, topY: 6 }, { startX: 20, endX: 22, topY: 0 }, { startX: 22, endX: 90, topY: 2 }],
            pits: [{ startX: 20, endX: 22, floorY: 0, depth: 2 }], hazards: [],
            encounters: [{ index: 0, id: 'pit-wall-1', kind: 'ledge-grab', x: 22, bottomY: 0, topY: 2, minimumGrabY: 0, landingX: 23 }],
            droopingBridges: [], climbs: [], decorations: [],
            playerBody: { leftExtent: 0.8, rightExtent: 0.85, height: 1.7 },
            runnerRules: { maxLaunchSpeed: 16, gravity: 30, ledgeGrabClearanceHeight: 0.3 },
        };
        const renderer = createTimeTrialRenderer(canvas, course, { assetBaseUrl: 'art/' });
        const wait = ms => new Promise(resolve => setTimeout(resolve, ms));
        let step = 0;
        const draw = s => renderer.render(Object.assign(
            { step: step++, vx: 8, vy: 0, mode: 'Running', encounterPhase: '', encounterProgress: 0, activeEncounterIndex: -1 }, s));
        for (let attempt = 0; attempt < 200 && canvas.dataset.runnerSheetReady !== 'true'; attempt++) {
            draw({ x: 0, y: 6 });
            await wait(50);
        }
        const count = name => Number(canvas.dataset[name]);
    ";

    private const string TearDownRenderer = @"
        renderer.dispose();
        canvas.remove();
    ";

    /// <summary>
    /// An ordinary landing goes straight back into the run cycle. A landing from higher than a full-charge jump
    /// reaches starts the roll, and a charge on the next frame cuts it short and is drawn at once.
    /// </summary>
    [Test]
    public async Task AHighLanding_Rolls_AndAChargeCutsTheRollShort()
    {
        JsonElement root = await RunAsync(@"
            const result = { ready: canvas.dataset.runnerSheetReady };
            // A 2.5 m hop off the shelf back onto the shelf.
            for (let i = 0; i < 10; i++)
                draw({ x: 2 + i * 0.3, y: 6 + 2.5 * Math.sin(i / 9 * Math.PI), vy: 10 - i * 2.2, mode: 'Airborne' });
            draw({ x: 5, y: 6 });
            result.ordinary = { frame: canvas.dataset.runnerFrame, started: count('runnerRollsStarted') };

            // Off the shelf's end, from 8 m high down onto the pit floor.
            for (let i = 0; i < 10; i++)
                draw({ x: 18 + i * 0.2, y: 8 - i * 0.8, vy: -12, mode: 'Airborne' });
            draw({ x: 20.2, y: 0 });
            result.landed = { frame: canvas.dataset.runnerFrame, started: count('runnerRollsStarted') };
            draw({ x: 20.3, y: 0, vx: 0, mode: 'Charging' });
            result.charged = { frame: canvas.dataset.runnerFrame, interrupted: count('runnerRollsInterrupted') };
            return result;
        ");

        Assert.That(root.GetProperty("ready").GetString(), Is.EqualTo("true"), "the runner sheet never loaded");
        Assert.That(root.GetProperty("ordinary").GetProperty("frame").GetString(), Does.StartWith("run-"), "an ordinary landing did not go straight back into the run");
        Assert.That(root.GetProperty("ordinary").GetProperty("started").GetInt32(), Is.EqualTo(0), "an ordinary landing rolled");
        Assert.That(root.GetProperty("landed").GetProperty("started").GetInt32(), Is.EqualTo(1), "the high landing did not roll");
        Assert.That(root.GetProperty("landed").GetProperty("frame").GetString(), Does.StartWith("roll-"), "the high landing was not drawn rolling");
        Assert.That(root.GetProperty("charged").GetProperty("interrupted").GetInt32(), Is.EqualTo(1), "the charge did not cut the roll short");
        Assert.That(root.GetProperty("charged").GetProperty("frame").GetString(), Is.EqualTo("charge"), "the charge was not drawn at once");
    }

    /// <summary>
    /// Climbing out of the pit (the hang, the rise and the top transition) is drawn in the climb frames, and
    /// the runner runs on from the lip.
    /// </summary>
    [Test]
    public async Task ThePitClimb_IsDrawnInTheClimbFrames()
    {
        JsonElement root = await RunAsync(@"
            const seen = [];
            const before = count('runnerClimbFramesDrawn');
            draw({ x: 20.5, y: 0 });
            for (let i = 0; i < 10; i++) {
                draw({ x: 21.15, y: 0, vx: 0, mode: 'Traversing', encounterPhase: 'ledge-hang', encounterProgress: i / 10, activeEncounterIndex: 0 });
                seen.push(canvas.dataset.runnerFrame);
            }
            for (let i = 0; i <= 20; i++) {
                draw({ x: 21.15, y: 2.3 * i / 20, vx: 0, mode: 'Traversing', encounterPhase: 'ledge-rise', encounterProgress: i / 20, activeEncounterIndex: 0 });
                seen.push(canvas.dataset.runnerFrame);
            }
            for (let i = 1; i <= 10; i++) {
                draw({ x: 21.15 + i * 0.185, y: 2.3 - 0.03 * i, vx: 0, mode: 'Traversing', encounterPhase: 'ledge-top-transition', encounterProgress: i / 10, activeEncounterIndex: 0 });
                seen.push(canvas.dataset.runnerFrame);
            }
            const drawn = count('runnerClimbFramesDrawn') - before;
            draw({ x: 23.2, y: 2, vx: 4 });
            return { seen: seen, drawn: drawn, after: canvas.dataset.runnerFrame };
        ");

        List<string> seen = root.GetProperty("seen").EnumerateArray().Select(frame => frame.GetString() ?? "").ToList();
        Assert.That(root.GetProperty("drawn").GetInt32(), Is.EqualTo(seen.Count), "not every mantle frame was drawn in a climb frame");
        Assert.That(root.GetProperty("after").GetString(), Does.StartWith("run-"), "the runner did not run on from the lip");
    }

    /// <summary>Opens the offline client, runs <paramref name="body"/> against a fresh renderer and parses what it returns.</summary>
    private async Task<JsonElement> RunAsync(string body)
    {
        await Page.GotoAsync(OfflineUrl);
        await Expect(Page.GetByTestId("mute-toggle")).ToBeVisibleAsync(new() { Timeout = 30000 });

        string json = await Page.EvaluateAsync<string>("async () => {" + SetUpRenderer
            + "const run = async () => {" + body + "};"
            + "const value = await run();" + TearDownRenderer
            + "return JSON.stringify(value); }");

        using JsonDocument document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
