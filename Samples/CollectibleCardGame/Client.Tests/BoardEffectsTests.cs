using Microsoft.Playwright;

namespace Game.Client.Tests;

[TestFixture]
public sealed class BoardEffectsTests : OfflineTestBase
{
    [Test]
    public async Task OnlyTheAimedTargetShowsItsDeltaAndArrowIsAboveCards()
    {
        await Page.GotoAsync($"{BaseUrl}/dev/board?scene=heal&env=offline");
        await Expect(Page.GetByTestId("match-board")).ToBeVisibleAsync(new() { Timeout = BootTimeout });
        await Page.Mouse.MoveAsync(5, 5);
        await Expect(Page.Locator(".delta-preview:visible")).ToHaveCountAsync(0);
        await Page.GetByTestId("den-mine").HoverAsync();
        await Expect(Page.Locator(".delta-preview:visible")).ToHaveCountAsync(1);
        await Expect(Page.GetByTestId("den-mine").GetByTestId("heal-preview")).ToBeVisibleAsync();
        // The number rides the badge as an attribute, not only as copy, and the preview scene fills the
        // amount maps the live board fills — so a fixture can read the amount rather than parse the wording.
        await Expect(Page.GetByTestId("den-mine").GetByTestId("heal-preview"))
            .ToHaveAttributeAsync("data-amount", new System.Text.RegularExpressions.Regex(@"^\d+$"));
        await Expect(Page.GetByTestId("targeting-arrow")).ToHaveCSSAsync("z-index", "200");
        await Page.Keyboard.PressAsync("Tab");
        await Page.Locator(".critter[data-targetable='true']").First.FocusAsync();
        await Expect(Page.Locator(".delta-preview:visible")).ToHaveCountAsync(1);
        await Expect(Page.GetByTestId("den-mine").GetByTestId("heal-preview")).ToBeHiddenAsync();
        await Page.Keyboard.PressAsync("Escape");
        await Expect(Page.Locator(".delta-preview:visible")).ToHaveCountAsync(0);
    }

    [Test]
    public async Task DamageHasACentredPowBurstButHealingAndReducedMotionDoNot()
    {
        await OpenAsync();
        await Page.EvaluateAsync("""
            () => stickyPawsBoardEffects.play(940, [{kind:'damage',targetKind:'den',
                target:Number(document.querySelector('.den-mine').dataset.seat),amount:3}])
            """);
        await Expect(Page.GetByTestId("board-impact-pow")).ToHaveTextAsync("POW!");
        double error = await Page.GetByTestId("board-impact-pow").EvaluateAsync<double>("""
            pow => {
                const target = document.querySelector('.den-mine').getBoundingClientRect();
                return Math.abs(parseFloat(pow.parentElement.style.top) + parseFloat(pow.style.top)
                    - target.top - target.height / 2);
            }
            """);
        Assert.That(error, Is.LessThan(1));
        await Expect(Page.GetByTestId("board-impact-pow")).ToHaveCountAsync(0);
        await Page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.Reduce });
        await Page.EvaluateAsync("() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))");
        await Page.EvaluateAsync("""
            () => stickyPawsBoardEffects.play(941, [{kind:'damage',targetKind:'den',
                target:Number(document.querySelector('.den-mine').dataset.seat),amount:3}])
            """);
        await Expect(Page.GetByTestId("board-impact")).ToHaveCountAsync(1);
        await Expect(Page.GetByTestId("board-impact-pow")).ToHaveCountAsync(0);
    }
    [Test]
    public async Task TouchCanChooseAHealTargetAndCancelFromTheTable()
    {
        await using IBrowserContext context = await Browser.NewContextAsync(new()
        {
            HasTouch = true,
            ViewportSize = new() { Width = 900, Height = 600 },
            DeviceScaleFactor = 2,
        });
        IPage page = await context.NewPageAsync();
        string url = $"{BaseUrl}/dev/board?scene=heal&env=offline";
        await page.GotoAsync(url);
        await Expect(page.GetByTestId("targeting-arrow")).ToBeVisibleAsync(new() { Timeout = BootTimeout });
        await page.GetByTestId("den-mine").TapAsync();
        await Expect(page.GetByTestId("targeting-layer")).ToHaveCountAsync(0);
        await Expect(page.GetByTestId("targeting-arrow")).ToBeHiddenAsync();
        await page.GotoAsync(url);
        await Expect(page.GetByTestId("targeting-layer")).ToBeVisibleAsync(new() { Timeout = BootTimeout });
        await page.GetByTestId("targeting-layer").TapAsync(new() { Position = new() { X = 10, Y = 10 } });
        await Expect(page.GetByTestId("targeting-layer")).ToHaveCountAsync(0);
        await Expect(page.GetByTestId("targeting-arrow")).ToBeHiddenAsync();
    }

    /// <summary>
    /// Each pose reaches the effects layer exactly once, with the amount the pose actually moves — and that
    /// amount is a whole number of stat quanta. Every authored amount in the game is a multiple of the
    /// quantum with a floor of one, so the 2 these poses used to hard-code was a number no resolution could
    /// produce, in the one tool a human uses to judge how a hit reads.
    /// </summary>
    [TestCase("damage", "damage", 10)]
    [TestCase("den-damage", "damage", 15)]
    [TestCase("heal", "heal", 10)]
    public async Task PresentedEventsReachTheEffectsLayerExactlyOnce(string animation, string kind, int amount)
    {
        Assert.That(amount % StatQuantum, Is.Zero, "a posed beat moves a whole number of stat quanta");
        Assert.That(amount, Is.GreaterThanOrEqualTo(StatQuantum), "a posed beat moves at least one quantum");

        await Page.AddInitScriptAsync("""
            window.recordedImpacts = [];
            new MutationObserver(records => {
                for (const record of records) for (const node of record.addedNodes)
                    if (node.nodeType === 1 && node.dataset.testid === 'board-impact')
                        window.recordedImpacts.push({kind:node.dataset.kind,amount:node.dataset.amount});
            }).observe(document, {subtree:true,childList:true});
            """);
        await Page.GotoAsync($"{BaseUrl}/dev/board?scene=inspect&animation={animation}&env=offline");
        await Expect(Page.GetByTestId("match-board")).ToBeVisibleAsync(new() { Timeout = BootTimeout });
        await Page.WaitForFunctionAsync("window.recordedImpacts.length > 0");
        string[] recorded = await Page.EvaluateAsync<string[]>("window.recordedImpacts.map(i => i.kind + ':' + i.amount)");
        Assert.That(recorded, Is.EqualTo(new[] { $"{kind}:{amount}" }));
    }

    /// <summary> `Global.StatQuantum`, which this fixture reaches no game config to read. </summary>
    const int StatQuantum = 5;

    [Test]
    public async Task SelectedHandCardRemainsOpaqueDuringTargeting()
    {
        await Page.GotoAsync($"{BaseUrl}/dev/board?scene=heal&env=offline");
        ILocator selected = Page.Locator(".handcard[data-selected='true'] .card-face");
        await Expect(selected).ToBeVisibleAsync(new() { Timeout = BootTimeout });
        Assert.That(await selected.EvaluateAsync<bool>("e => e.classList.contains('is-dimmed')"), Is.False);
        Assert.That(await selected.EvaluateAsync<string>("e => getComputedStyle(e).opacity"), Is.EqualTo("1"));
        Assert.That(await Page.Locator(".handcard .card-face.is-dimmed").First.EvaluateAsync<string>(
            "e => getComputedStyle(e).opacity"), Is.EqualTo("1"), "inactive cards must not reveal overlapping neighbours");
    }

    [Test]
    public async Task RetaliationFeedbackUsesTheVisibleAttackerNotItsHiddenSocket()
    {
        await OpenAsync();
        double error = await Page.EvaluateAsync<double>("""
            () => {
                const source = document.querySelector('[data-testid="board-critter"]');
                const clone = source.cloneNode(true);
                clone.dataset.testid = 'board-motion-attacker';
                clone.style.cssText = 'position:fixed;left:35px;top:35px;width:100px;height:140px;transform:none';
                document.body.append(clone);
                source.classList.add('board-motion-source-hidden');
                stickyPawsBoardEffects.play(930, [{kind:'damage',targetKind:'critter',target:Number(source.dataset.instance),amount:2}]);
                const impact = document.querySelector('[data-testid="board-impact"]');
                const expected = clone.getBoundingClientRect();
                const error = Math.abs(parseFloat(impact.style.left) - expected.left - expected.width / 2);
                stickyPawsBoardEffects.reset();
                source.classList.remove('board-motion-source-hidden');
                clone.remove();
                return error;
            }
            """);
        Assert.That(error, Is.LessThan(1));
    }

    async Task OpenAsync()
    {
        await Page.GotoAsync($"{BaseUrl}/dev/board?scene=inspect&env=offline");
        await Expect(Page.GetByTestId("match-board")).ToBeVisibleAsync(new() { Timeout = BootTimeout });
    }

    [Test]
    public async Task ConsecutiveImpactsFinishIndependentlyWithoutDuplicatingTheBeat()
    {
        await OpenAsync();
        await Page.EvaluateAsync("""
            () => {
                const target = Number(document.querySelector('.den-mine').dataset.seat);
                const hit = [{kind:'damage',targetKind:'den',target,amount:3}];
                stickyPawsBoardEffects.play(910, hit);
                stickyPawsBoardEffects.play(910, hit);
                stickyPawsBoardEffects.play(911, [{...hit[0],kind:'heal',amount:2}]);
            }
            """);
        await Expect(Page.GetByTestId("board-impact")).ToHaveCountAsync(2);
        await Expect(Page.GetByTestId("board-impact-pow")).ToHaveCountAsync(1);
        await Expect(Page.Locator(".board-impact-particle")).ToHaveCountAsync(18);
        await Expect(Page.GetByTestId("board-impact")).ToHaveCountAsync(0);
    }

    [Test]
    public async Task ReducedMotionShowsReadableValuesWithoutParticlesAndResetCleansUp()
    {
        await Page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.Reduce });
        await OpenAsync();
        await Page.EvaluateAsync("""
            () => stickyPawsBoardEffects.play(912, [{kind:'heal',targetKind:'den',
                target:Number(document.querySelector('.den-mine').dataset.seat),amount:2}])
            """);
        await Expect(Page.Locator(".board-impact-value")).ToHaveTextAsync("+2");
        await Expect(Page.Locator(".board-impact-particle")).ToHaveCountAsync(0);
        await Page.EvaluateAsync("stickyPawsBoardEffects.reset()");
        await Expect(Page.GetByTestId("board-impact")).ToHaveCountAsync(0);
    }

    [Test]
    public async Task RemovingTheBoardRemovesItsRemainingImpacts()
    {
        await OpenAsync();
        await Page.EvaluateAsync("""
            () => {
                stickyPawsBoardEffects.play(913, [{kind:'blocked',targetKind:'den',
                    target:Number(document.querySelector('.den-mine').dataset.seat),amount:0}]);
                document.querySelector('[data-testid="match-board"]').remove();
            }
            """);
        await Expect(Page.GetByTestId("board-impact")).ToHaveCountAsync(0);
    }
}
