using Microsoft.Playwright.NUnit;
using System.Text.Json;

namespace WebClient.Tests;

/// <summary>
/// E2E tests for the field arriving and leaving through the torii at the course's two ends, driven through
/// <c>?env=offline</c> so they need only the web client. The renderer is driven directly with the snapshots a
/// run would hand it: the field hidden through the run-in and drawn from step 0, then one opponent hidden at
/// its own finish-line crossing. The start torii plays one warp-in as the field arrives, the finish line plays
/// a poof for the opponent that crosses it, and nothing plays for a run-in that has not ended.
/// </summary>
[Parallelizable(ParallelScope.Self)]
[TestFixture]
public class GhostToriiEffectsOfflineModeTests : GamePageTest
{
    private static readonly string BaseUrl =
        (Environment.GetEnvironmentVariable("WEBCLIENT_BASE_URL") ?? "http://localhost:5290").TrimEnd('/');

    private static readonly string OfflineUrl = BaseUrl + "/?env=offline";

    [Test]
    public async Task TheFieldWarpsInAtTheStartLine_AndPoofsAtTheFinishLine()
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
                id: 'ghost-torii-test', startX: 0, finishX: 40,
                ground: [{ startX: -30, endX: 100, topY: 0 }], pits: [], hazards: [], encounters: [],
                droopingBridges: [], climbs: [], decorations: [],
                playerBody: { leftExtent: 0.8, rightExtent: 0.85, height: 1.7 },
            };
            const renderer = createTimeTrialRenderer(canvas, course, { assetBaseUrl: 'art/' });
            let step = 0;
            const ghost = (x, hideReason) => ({ x, y: 0, vx: 8, vy: 0, mode: 'Running', hidden: hideReason !== '', hideReason });
            const frame = (x, runIn, ghosts) => renderer.render({
                step: runIn ? 0 : step++, x, y: 0, vx: 8, vy: 0, mode: 'Running', runIn, inputLocked: runIn,
                nextClimbIndex: 0, nextGateIndex: 0, elapsedMs: step * 16, ghosts,
            });
            const read = () => ({
                poofs: Number(canvas.dataset.ghostPoofCount),
                warpIns: Number(canvas.dataset.ghostWarpInCount),
            });
            const result = {};

            // The run-in: the player alone, the field hidden. Nothing plays.
            for (let x = -12; x < 0; x += 1) frame(x, true, [ghost(0, 'runIn'), ghost(0, 'runIn')]);
            result.runIn = read();

            // Step 0: the field is drawn under the start torii for the first time.
            frame(0, false, [ghost(0, ''), ghost(0, '')]);
            result.start = read();

            // Out on the course together: nothing more plays.
            for (let x = 0.5; x < 38; x += 0.5) frame(x, false, [ghost(x + 1, ''), ghost(x - 1, '')]);
            result.course = read();

            // The quicker opponent crosses the finish line and is hidden there; the other is still running.
            frame(38.5, false, [ghost(40.2, 'finish'), ghost(37.5, '')]);
            result.firstHome = read();

            // The slower one crosses during the player's run-out.
            for (let x = 39; x < 46; x += 0.5) frame(x, false, [ghost(40.2, 'finish'), ghost(x - 1, '')]);
            frame(46, false, [ghost(40.2, 'finish'), ghost(45.5, 'finish')]);
            result.secondHome = read();

            // A new run: nothing playing, the lifetime counts standing, and the field arrives again on step 0.
            step = 0;
            frame(-12, true, [ghost(0, 'runIn'), ghost(0, 'runIn')]);
            result.restarted = read();
            frame(0, false, [ghost(0, ''), ghost(0, '')]);
            result.secondStart = read();
            renderer.dispose();
            canvas.remove();
            return JSON.stringify(result);
        }");

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        Assert.That(root.GetProperty("runIn").GetProperty("warpIns").GetInt32(), Is.EqualTo(0), "the field warped in before the start line");
        Assert.That(root.GetProperty("runIn").GetProperty("poofs").GetInt32(), Is.EqualTo(0), "something poofed during the run-in");
        Assert.That(root.GetProperty("start").GetProperty("warpIns").GetInt32(), Is.EqualTo(1), "the field did not warp in under the start torii on step 0");
        Assert.That(root.GetProperty("course").GetProperty("warpIns").GetInt32(), Is.EqualTo(1), "a warp-in played out on the course");
        Assert.That(root.GetProperty("course").GetProperty("poofs").GetInt32(), Is.EqualTo(0), "an opponent poofed out on the course");
        Assert.That(root.GetProperty("firstHome").GetProperty("poofs").GetInt32(), Is.EqualTo(1), "the opponent that crossed the finish line did not poof");
        Assert.That(root.GetProperty("secondHome").GetProperty("poofs").GetInt32(), Is.EqualTo(2), "the second opponent to cross did not poof");
        Assert.That(root.GetProperty("restarted").GetProperty("poofs").GetInt32(), Is.EqualTo(2), "a new run reset the lifetime count of poofs");
        Assert.That(root.GetProperty("restarted").GetProperty("warpIns").GetInt32(), Is.EqualTo(1), "a new run's run-in played a warp-in");
        Assert.That(root.GetProperty("secondStart").GetProperty("warpIns").GetInt32(), Is.EqualTo(2), "the field did not warp in again on the new run's step 0");
    }
}
