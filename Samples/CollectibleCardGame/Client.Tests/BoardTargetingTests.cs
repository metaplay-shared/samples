using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

namespace Game.Client.Tests;

/// <summary> Targeting geometry and cleanup run against the shipped browser controller, without a server. </summary>
[TestFixture]
public sealed class BoardTargetingTests : PageTest
{
    async Task SetUpArrowAsync()
    {
        await Page.SetContentAsync("""
            <div class="board-frame" style="position:relative;width:800px;height:500px">
              <button class="critter" data-selected="true" style="position:absolute;left:350px;top:350px;width:80px;height:100px">Source</button>
              <button class="den" data-targetable="true" style="position:absolute;left:350px;top:30px;width:100px;height:100px">Target</button>
              <svg class="targeting-arrow" data-kind="Attack" hidden aria-hidden="true">
                <path class="targeting-arrow-shadow"/><path class="targeting-arrow-body"/>
                <path class="targeting-arrow-thread"/><path class="targeting-arrow-head"/>
                <circle class="targeting-arrow-origin" r="5"/>
                <g class="targeting-counter" data-testid="counter-damage-preview" hidden>
                  <rect x="-26" y="-14" width="52" height="28" rx="9"/>
                  <text class="targeting-counter-value" text-anchor="middle" dominant-baseline="central"/>
                  <g class="targeting-counter-mark"><path class="targeting-counter-shield"/>
                    <path class="targeting-counter-lethal"/></g>
                </g>
              </svg>
            </div>
            """);
        string root = Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "../../../../Client/wwwroot"));
        await Page.AddStyleTagAsync(new() { Content = await File.ReadAllTextAsync(Path.Combine(root, "board-targeting.css")) });
        await Page.AddScriptTagAsync(new() { Content = await File.ReadAllTextAsync(Path.Combine(root, "board-targeting.js")) });
        await Page.EvaluateAsync("""
            stickyPawsTargeting.update(document.querySelector('svg'), 'Attack', {
                invokeMethodAsync: () => { window.targetingCancelled = true; return Promise.resolve(); }
            })
            """);
    }

    async Task SetUpTouchBoardAsync(bool fromHand = false, int delayMs = 0)
    {
        await SetUpArrowAsync();
        await Page.EvaluateAsync("""
            ({fromHand, delayMs}) => {
                window.dragCalls = [];
                window.touchTrace = [];
                for (const name of ['pointerdown', 'pointermove', 'pointerup', 'pointercancel', 'lostpointercapture'])
                    window.addEventListener(name, e => touchTrace.push([name, e.clientX, e.clientY, e.pointerId]), true);
                window.sourceClicks = 0;
                const source = document.querySelector('[data-selected="true"]');
                source.dataset.instance = '7';
                source.dataset.selectable = 'true';
                source.dataset.playable = 'true';
                source.dataset.selected = 'false';
                if (fromHand) {
                    source.className = 'handcard';
                    const zone = document.createElement('div'); zone.className = 'touch-play-zone';
                    document.querySelector('.board-frame').append(zone);
                    const hand = document.createElement('div');
                    hand.className = 'hand';
                    hand.style = 'position:absolute;top:350px;width:100%;height:150px';
                    document.querySelector('.board-frame').append(hand);
                    source.style.top = '0';
                    hand.append(source);
                }
                source.addEventListener('click', () => window.sourceClicks++);
                document.querySelector('.den').dataset.seat = '1';
                stickyPawsTargeting.dispose(document.querySelector('svg'));
                stickyPawsTargeting.update(document.querySelector('svg'), 'None', {
                    async invokeMethodAsync(method, ...args) {
                        window.dragCalls.push([method, ...args]);
                        if (method === 'BeginDrag') {
                            await new Promise(r => setTimeout(r, delayMs));
                            source.dataset.selected = 'true';
                            stickyPawsTargeting.update(document.querySelector('svg'), 'Attack');
                            return true;
                        }
                        source.dataset.selected = 'false';
                        stickyPawsTargeting.update(document.querySelector('svg'), 'None');
                    }
                });
            }
            """, new { fromHand, delayMs });
    }

    async Task<ICDPSession> SetUpTouchAsync(bool fromHand = false, int delayMs = 0)
    {
        await SetUpTouchBoardAsync(fromHand, delayMs);
        return await Page.Context.NewCDPSessionAsync(Page);
    }

    [Test]
    public async Task NativeTouchContactKeepsAimingThroughPointerCancellation()
    {
        await SetUpTouchBoardAsync();
        await Page.EvaluateAsync("""
            () => {
                const source = document.querySelector('[data-instance="7"]');
                window.contact = (type, x, y) => {
                    const touch = { identifier: 12, target: source, clientX: x, clientY: y };
                    const event = new Event(type, { bubbles: true, cancelable: true });
                    Object.defineProperties(event, {
                        touches: { value: type === 'touchend' ? [] : [touch] },
                        changedTouches: { value: [touch] }
                    });
                    source.dispatchEvent(event);
                };
                contact('touchstart', 395, 400);
                contact('touchmove', 395, 80);
                source.dispatchEvent(new PointerEvent('pointercancel', { bubbles: true, pointerType: 'touch' }));
                source.dispatchEvent(new PointerEvent('lostpointercapture', { bubbles: true, pointerType: 'touch' }));
            }
            """);
        await Expect(Page.Locator("svg")).ToHaveAttributeAsync("data-snapped", "true");
        Assert.That(await Page.EvaluateAsync<int>("dragCalls.length"), Is.EqualTo(1));
        await Page.EvaluateAsync("contact('touchend', 395, 80)");
        await Page.WaitForFunctionAsync("dragCalls.length === 2");
        Assert.That(await Page.EvaluateAsync<string>("JSON.stringify(dragCalls[1])"),
            Is.EqualTo("[\"EndDrag\",\"den\",1]"));
    }

    static Task TouchAsync(ICDPSession session, string type, int x = 390, int y = 400)
        => session.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object>
        {
            ["type"] = type,
            ["touchPoints"] = type is "touchEnd" or "touchCancel" ? Array.Empty<object>()
                : new object[] { new { x, y, id = 1 } },
        });

    [Test]
    public async Task TouchDragTargetsOnReleaseAndSuppressesCompatibilityClick()
    {
        ICDPSession touch = await SetUpTouchAsync();
        await TouchAsync(touch, "touchStart");
        await TouchAsync(touch, "touchMove", 395, 250);
        await TouchAsync(touch, "touchMove", 395, 80);
        await Expect(Page.Locator("svg")).ToHaveAttributeAsync("data-snapped", "true");
        Assert.That(await Page.EvaluateAsync<int>("dragCalls.length"), Is.EqualTo(1));
        await TouchAsync(touch, "touchEnd");
        await Page.WaitForFunctionAsync("dragCalls.length === 2");
        Assert.That(await Page.EvaluateAsync<string>("JSON.stringify(dragCalls)"),
            Is.EqualTo("[[\"BeginDrag\",false,7],[\"EndDrag\",\"den\",1]]"), await Page.EvaluateAsync<string>("JSON.stringify(touchTrace)"));
        Assert.That(await Page.EvaluateAsync<int>("sourceClicks"), Is.Zero);
        await Expect(Page.Locator("svg")).ToBeHiddenAsync();
    }

    [TestCase("touchCancel")]
    [TestCase("touchEnd")]
    public async Task CancelOrInvalidDropNeverChoosesATarget(string endType)
    {
        ICDPSession touch = await SetUpTouchAsync();
        await TouchAsync(touch, "touchStart");
        await TouchAsync(touch, "touchMove", 50, 240);
        await TouchAsync(touch, endType);
        await Page.WaitForFunctionAsync("dragCalls.length === 2");
        Assert.That(await Page.EvaluateAsync<string>("JSON.stringify(dragCalls[1])"),
            Is.EqualTo("[\"EndDrag\",\"\",-1]"));
        await Expect(Page.Locator(".board-frame")).Not.ToHaveClassAsync(new System.Text.RegularExpressions.Regex("is-touch-dragging"));
    }

    [Test]
    public async Task TapDoesNotBeginDragAndQuickFlickWaitsForSelection()
    {
        ICDPSession touch = await SetUpTouchAsync(delayMs: 120);
        await TouchAsync(touch, "touchStart");
        await TouchAsync(touch, "touchEnd");
        Assert.That(await Page.EvaluateAsync<int>("dragCalls.length"), Is.Zero);
        await TouchAsync(touch, "touchStart");
        await TouchAsync(touch, "touchMove", 395, 80);
        await TouchAsync(touch, "touchEnd");
        await Page.WaitForFunctionAsync("dragCalls.length === 2");
        Assert.That(await Page.EvaluateAsync<string>("JSON.stringify(dragCalls[1])"),
            Is.EqualTo("[\"EndDrag\",\"den\",1]"));
    }

    [Test]
    public async Task HandCardCanDropOntoTheMarkedBattlefieldArea()
    {
        ICDPSession touch = await SetUpTouchAsync(fromHand: true);
        await TouchAsync(touch, "touchStart");
        await TouchAsync(touch, "touchMove", 200, 240);
        await TouchAsync(touch, "touchEnd");
        await Page.WaitForFunctionAsync("dragCalls.length === 2");
        Assert.That(await Page.EvaluateAsync<string>("JSON.stringify(dragCalls)"),
            Is.EqualTo("[[\"BeginDrag\",true,7],[\"EndDrag\",\"board\",-1]]"));
    }

    [Test]
    public async Task PointerSnapsToLegalTargetAndCancelRemovesArrow()
    {
        await SetUpArrowAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Target", Exact = true }).HoverAsync();
        await Expect(Page.Locator("svg")).ToHaveAttributeAsync("data-snapped", "true");
        await Expect(Page.Locator("svg")).ToBeVisibleAsync();
        Assert.That(await Page.Locator("svg").EvaluateAsync<string>("svg => getComputedStyle(svg).pointerEvents"), Is.EqualTo("none"));
        await Page.EvaluateAsync("stickyPawsTargeting.update(document.querySelector('svg'), 'None')");
        await Expect(Page.Locator("svg")).ToBeHiddenAsync();
    }

    [Test]
    public async Task CounterDamageAppearsAtSourceOnlyWhileACombatTargetIsSnapped()
    {
        await SetUpArrowAsync();
        await Page.Locator(".den").EvaluateAsync("""
            target => {
                target.className = 'critter';
                const preview = document.createElement('span');
                preview.className = 'delta-preview';
                preview.dataset.counterAmount = '3';
                preview.dataset.counterBlocked = 'false';
                preview.dataset.counterLethal = 'true';
                target.append(preview);
            }
            """);
        ILocator counter = Page.GetByTestId("counter-damage-preview");
        await Expect(counter).ToBeHiddenAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Target", Exact = true }).HoverAsync();
        await Expect(counter).ToBeVisibleAsync();
        await Expect(counter.Locator("text")).ToHaveTextAsync("−3");
        await Expect(counter).ToHaveAttributeAsync("data-lethal", "true");
        double anchorError = await counter.EvaluateAsync<double>("""
            counter => {
                const source = document.querySelector('[data-selected="true"]').getBoundingClientRect();
                const rect = counter.querySelector('rect').getBoundingClientRect();
                return Math.max(Math.abs(rect.x + rect.width / 2 - source.x - source.width / 2),
                    Math.abs(rect.y + rect.height / 2 - source.y - source.height * .12));
            }
            """);
        Assert.That(anchorError, Is.LessThan(1));
        await Page.Mouse.MoveAsync(30, 200);
        await Expect(counter).ToBeHiddenAsync();

        await Page.Keyboard.PressAsync("Tab");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Target", Exact = true }).FocusAsync();
        await Expect(counter).ToBeVisibleAsync();
        await Page.Locator(".delta-preview").EvaluateAsync("""
            preview => {
                preview.dataset.counterAmount = '0';
                preview.dataset.counterBlocked = 'true';
                preview.dataset.counterLethal = 'false';
            }
            """);
        await Expect(counter.Locator("text")).ToHaveTextAsync("0");
        await Expect(counter).ToHaveAttributeAsync("data-blocked", "true");
        await Page.EvaluateAsync("stickyPawsTargeting.update(document.querySelector('svg'), 'Heal')");
        await Expect(counter).ToBeHiddenAsync();
        await Page.EvaluateAsync("stickyPawsTargeting.update(document.querySelector('svg'), 'Attack')");
        await Expect(counter).ToBeVisibleAsync();
        await Page.Keyboard.PressAsync("Escape");
        await Expect(counter).ToBeHiddenAsync();
    }

    [Test]
    public async Task KeyboardFocusTargetsWithoutPointerAndDisposalStopsTracking()
    {
        await SetUpArrowAsync();
        await Page.Keyboard.PressAsync("Tab");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Target", Exact = true }).FocusAsync();
        await Expect(Page.Locator("svg")).ToHaveAttributeAsync("data-snapped", "true");
        await Page.EvaluateAsync("stickyPawsTargeting.dispose(document.querySelector('svg'))");
        await Page.Mouse.MoveAsync(400, 80);
        await Expect(Page.Locator("svg")).ToBeHiddenAsync();
    }

    [Test]
    public async Task EscapeCancelsWhileKeyboardFocusIsOnTarget()
    {
        await SetUpArrowAsync();
        await Page.Keyboard.PressAsync("Tab");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Target", Exact = true }).FocusAsync();
        await Expect(Page.Locator("svg")).ToHaveAttributeAsync("data-snapped", "true");
        await Page.Keyboard.PressAsync("Escape");
        await Expect(Page.Locator("svg")).ToBeHiddenAsync();
        Assert.That(await Page.EvaluateAsync<bool>("window.targetingCancelled"), Is.True);
    }

    [Test]
    public async Task ArrowUsesStaticGeometryWithReducedMotionAndTracksResize()
    {
        await Page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.Reduce });
        await SetUpArrowAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Target", Exact = true }).HoverAsync();
        await Expect(Page.Locator("svg")).ToBeVisibleAsync();
        Assert.That(await Page.Locator("svg").EvaluateAsync<int>("svg => svg.getAnimations({ subtree: true }).length"), Is.Zero);
        await Page.Locator(".board-frame").EvaluateAsync("board => board.style.width = '900px'");
        await Expect(Page.Locator("svg")).ToHaveAttributeAsync("viewBox", "0 0 900 500");
    }
}
