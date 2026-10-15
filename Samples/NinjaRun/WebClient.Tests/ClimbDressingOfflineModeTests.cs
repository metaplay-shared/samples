using Microsoft.Playwright.NUnit;
using System.Text.Json;

namespace WebClient.Tests;

/// <summary>
/// E2E tests for what dresses a climb's hands-off seconds, driven through <c>?env=offline</c> so they need only
/// the web client: the renderer plants the kekkai fence along the slope and strings it, reaching the bottom
/// torii starts the bamboo shadow's sweep across the view, the sweep is over before the gate, and a new run
/// starts with nothing sweeping. The renderer is driven directly with a course carrying one climb, the way the
/// snapshots of a run drive it.
/// </summary>
[Parallelizable(ParallelScope.Self)]
[TestFixture]
public class ClimbDressingOfflineModeTests : GamePageTest
{
    private static readonly string BaseUrl =
        (Environment.GetEnvironmentVariable("WEBCLIENT_BASE_URL") ?? "http://localhost:5290").TrimEnd('/');

    private static readonly string OfflineUrl = BaseUrl + "/?env=offline";

    [Test]
    public async Task ReachingTheBottomTorii_SweepsTheShadowAcrossTheSlope()
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
                id: 'climb-dressing-test', startX: 0, finishX: 80,
                ground: [{ startX: -30, endX: 20, topY: 0 }, { startX: 44, endX: 100, topY: 5 }],
                pits: [], hazards: [], encounters: [], droopingBridges: [], decorations: [],
                climbs: [{ id: 'climb-1', bottomX: 20, bottomY: 0, topX: 44, topY: 5, gateX: 52 }],
                playerBody: { leftExtent: 0.8, rightExtent: 0.85, height: 1.7 },
            };
            const renderer = createTimeTrialRenderer(canvas, course, { assetBaseUrl: 'art/' });
            const wait = ms => new Promise(resolve => setTimeout(resolve, ms));
            let step = 0;
            const at = (x, climbing) => renderer.render({
                step: step++, x, y: climbing ? (x - 20) * 5 / 24 : 0, vx: 8, vy: 0,
                mode: climbing ? 'Climbing' : 'Running', inputLocked: climbing === true,
                nextClimbIndex: climbing ? 1 : 0, nextGateIndex: 0, elapsedMs: step * 16,
            });
            const read = () => ({
                sweeps: Number(canvas.dataset.climbShadowSweepCount),
                playing: canvas.dataset.climbShadowPlaying,
                stakePassBys: Number(canvas.dataset.climbStakePassByCount),
                decorationPassBys: Number(canvas.dataset.decorationPassByCount),
            });

            const result = {
                decorations: Number(canvas.dataset.decorationCount),
            };
            for (let x = 0; x < 19.5; x += 0.5) at(x, false);
            result.approach = read();

            // The bottom torii is reached: the sweep starts as the segment closes.
            at(20.3, true);
            result.atTorii = read();

            // Up the slope for a little over a second of the renderer's own clock, past the first stakes.
            let x = 20.3;
            for (let frame = 0; frame < 40; frame++) {
                await wait(30);
                x += 0.24;
                at(x, true);
            }
            result.onSlope = read();

            // The sweep is over well before the gate.
            for (let frame = 0; frame < 75; frame++) {
                await wait(30);
                x = Math.min(43.5, x + 0.24);
                at(x, true);
            }
            result.sweptPast = read();

            // A new run starts with nothing sweeping; the lifetime count stands.
            step = 0;
            at(0, false);
            result.restarted = read();
            renderer.dispose();
            canvas.remove();
            return JSON.stringify(result);
        }");

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        Assert.That(root.GetProperty("decorations").GetInt32(), Is.EqualTo(0), "the fence counted as the course's own decorations");

        JsonElement approach = root.GetProperty("approach");
        Assert.That(approach.GetProperty("sweeps").GetInt32(), Is.EqualTo(0), "the shadow swept before the bottom torii");

        JsonElement atTorii = root.GetProperty("atTorii");
        Assert.That(atTorii.GetProperty("sweeps").GetInt32(), Is.EqualTo(1), "reaching the bottom torii did not start the sweep");
        Assert.That(atTorii.GetProperty("playing").GetString(), Is.EqualTo("true"), "the sweep was not playing as the climb took the runner");

        JsonElement onSlope = root.GetProperty("onSlope");
        Assert.That(onSlope.GetProperty("playing").GetString(), Is.EqualTo("true"), "the sweep was over a second into the slope");
        Assert.That(onSlope.GetProperty("stakePassBys").GetInt32(), Is.GreaterThan(0), "running past the stakes set none of them swinging");
        Assert.That(onSlope.GetProperty("decorationPassBys").GetInt32(), Is.EqualTo(0), "a stake's pass-by counted as a decoration's");

        JsonElement sweptPast = root.GetProperty("sweptPast");
        Assert.That(sweptPast.GetProperty("playing").GetString(), Is.EqualTo("false"), "the shadow was still sweeping near the top of the slope");
        Assert.That(sweptPast.GetProperty("sweeps").GetInt32(), Is.EqualTo(1), "the sweep started again on the same climb");

        JsonElement restarted = root.GetProperty("restarted");
        Assert.That(restarted.GetProperty("playing").GetString(), Is.EqualTo("false"), "a new run started with the shadow sweeping");
        Assert.That(restarted.GetProperty("sweeps").GetInt32(), Is.EqualTo(1), "a new run reset the lifetime count of sweeps");
    }
}
