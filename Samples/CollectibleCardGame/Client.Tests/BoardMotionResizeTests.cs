using Microsoft.Playwright.NUnit;

namespace Game.Client.Tests;

/// <summary> Resized sockets retain a single, smoothly resizing visual owner through motion handoff. </summary>
[TestFixture]
public sealed class BoardMotionResizeTests : PageTest
{
    [TestCase("cardplay")]
    [TestCase("attack")]
    public async Task ResizedDestinationMatchesFlightBeforeOwnershipChanges(string kind)
    {
        await Page.SetContentAsync("""
            <style>
              .board-frame { --card-size:100px; position:relative; width:800px; height:600px; }
              .card { position:absolute; width:var(--card-size); height:calc(var(--card-size) * 1.4); }
              .source { left:calc(200px - var(--card-size)/2); top:calc(400px - var(--card-size)*.7); }
              .socket { position:absolute; left:calc(450px - var(--card-size)/2); top:calc(200px - var(--card-size)*.7);
                width:var(--card-size); height:calc(var(--card-size)*1.4); }
              .card-face { width:100%; height:100%; background:coral; }
              .board-motion-card { position:fixed!important; transform-origin:center; pointer-events:none; }
              .board-motion-source-hidden { visibility:hidden!important; }
            </style>
            <div class="board-frame" data-testid="match-board">
              <div class="card source" data-testid="hand-card" data-instance="42"><div class="card-face"></div></div>
              <div class="socket" data-testid="critter-entry-slot" data-entry-instance="42">
                <div class="card" data-testid="board-critter" data-instance="42"><div class="card-face"></div></div>
              </div>
            </div>
            """);
        string script = Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory,
            "../../../../Client/wwwroot/board-motion.js"));
        await Page.AddScriptTagAsync(new() { Content = await File.ReadAllTextAsync(script) });
        await Page.EvaluateAsync("""
            kind => {
                if (kind === 'attack') {
                    document.querySelector('.source').dataset.testid = 'board-critter';
                    document.querySelector('.socket .card').dataset.instance = '99';
                }
                const originalAnimate = Element.prototype.animate;
                window.handoffErrors = [];
                window.handoffCount = 0;
                Element.prototype.animate = function (...args) {
                    const animation = originalAnimate.apply(this, args);
                    animation.finished.then(() => {
                        if (animation.id !== 'sticky-paws-handoff-align') return;
                        const destination = kind === 'attack' ? document.querySelector('.source')
                            : document.querySelector('.socket .card');
                        const from = this.getBoundingClientRect(), to = destination.getBoundingClientRect();
                        window.handoffCount++;
                        window.handoffErrors.push(Math.max(Math.abs(from.width - to.width), Math.abs(from.height - to.height),
                            Math.abs(from.x - to.x), Math.abs(from.y - to.y)));
                    }).catch(() => {});
                    return animation;
                };
                stickyPawsBoardMotion.run(1, kind, 42, 'critter', 99, 240, false, false);
                // A resize changes only the destination: the body-owned flight retains its pixel dimensions.
                document.querySelector('.board-frame').style.setProperty('--card-size', '64px');
                document.querySelector('.socket').dataset.testid = 'settled-socket';
            }
            """, kind);

        await Expect(Page.Locator(".board-motion-card")).ToHaveCountAsync(0);
        Assert.That(await Page.EvaluateAsync<int>("window.handoffCount"), Is.GreaterThan(0),
            "dimension-only changes must receive an animated alignment rather than an immediate swap");
        double[] errors = await Page.EvaluateAsync<double[]>("window.handoffErrors");
        Assert.That(errors, Is.All.LessThan(1), "the final clone must match all four destination bounds");
        await Expect(Page.Locator(kind == "attack" ? ".source" : ".socket .card")).ToBeVisibleAsync();
    }
}
