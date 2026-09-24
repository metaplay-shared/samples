using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;
using System.Text.RegularExpressions;

namespace Game.Client.Tests;

/// <summary>
/// Browser-level checks for the deterministic match-board preview. The page uses the same <c>BoardView</c>
/// as a live match but builds a legal seeded table in offline mode, so these interaction contracts need no
/// game server and do not race a bot.
/// </summary>
[Parallelizable(ParallelScope.Self)]
[TestFixture]
public sealed class BoardInteractionTests : OfflineTestBase
{
    [TestCase(true, "none")]
    [TestCase(true, "den")]
    [TestCase(true, "critter")]
    [TestCase(false, "none")]
    [TestCase(false, "den")]
    public async Task TricksRevealTheirFaceRegardlessOfPlayerOrTarget(bool opponent, string targetKind)
    {
        await OpenSceneAsync("inspect");
        bool revealed = await Page.EvaluateAsync<bool>("""
            async ({opponent, targetKind}) => {
                const hand = document.querySelector('[data-testid="hand-card"]');
                const instance = opponent ? '987654' : hand.dataset.instance;
                const template = document.createElement('div');
                template.className = 'trick-flight-destination';
                template.dataset.testid = 'trick-flight-destination';
                template.dataset.instance = instance;
                template.append(hand.querySelector('.card-face').cloneNode(true));
                template.querySelector('.card-face').classList.remove('is-dimmed', 'is-playable', 'is-selected');
                document.querySelector('[data-testid="match-board"]').append(template);
                const target = targetKind === 'den' ? document.querySelector('[data-testid="den-mine"]').dataset.seat
                    : targetKind === 'critter' ? document.querySelector('[data-testid="board-critter"]').dataset.instance : 0;
                stickyPawsBoardMotion.run(990, 'cardplay', instance, targetKind, target, 600, false, opponent);
                const moving = document.querySelector('[data-testid="board-motion-card"]');
                if (!moving) return false;
                const face = [...moving.querySelectorAll('.card-face')].at(-1);
                await Promise.all(face.getAnimations().map(animation => animation.finished));
                const flight = moving.getAnimations()[0];
                flight.pause();
                flight.currentTime = 300;
                const visible = face && Number(getComputedStyle(face).opacity) > .9
                    && getComputedStyle(face).visibility !== 'hidden'
                    && Number(getComputedStyle(moving).opacity) > .99;
                flight.play();
                await flight.finished;
                await new Promise(requestAnimationFrame);
                const removed = !moving.isConnected;
                stickyPawsBoardMotion.reset();
                template.remove();
                return visible && removed;
            }
            """, new { opponent, targetKind });
        Assert.That(revealed, Is.True, "a trick needs a visible face even without a battlefield destination");
    }

    [Test]
    public async Task BusyHandDoesNotAdvertiseFuturePlayableCards()
    {
        await Page.GotoAsync($"{BaseUrl}/dev/board?scene=inspect&animation=opponent-trick&env=offline");
        await Expect(Page.GetByTestId("match-board")).ToBeVisibleAsync(new() { Timeout = BootTimeout });
        await Expect(Page.GetByTestId("hand-card").First).ToBeVisibleAsync();
        await Expect(Page.Locator("[data-testid='hand-card'][data-playable='true']")).ToHaveCountAsync(0);
    }

    [Test]
    public async Task RapidOpponentTricksHaveIndependentFlightsAndDoNotRestoreConsumedBacks()
    {
        await OpenSceneAsync("inspect");
        bool independent = await Page.EvaluateAsync<bool>("""
            async () => {
                const board = document.querySelector('[data-testid="match-board"]');
                const face = document.querySelector('[data-testid="hand-card"] .card-face');
                for (const instance of [987654, 987655]) {
                    const template = document.createElement('div');
                    template.className = 'trick-flight-destination';
                    template.dataset.testid = 'trick-flight-destination';
                    template.dataset.instance = instance;
                    template.append(face.cloneNode(true));
                    board.append(template);
                    stickyPawsBoardMotion.run(instance, 'cardplay', instance, 'none', 0, 400, false, true);
                }
                const moving = [...document.querySelectorAll('[data-testid="board-motion-card"]')];
                const hiddenBacks = () => [...document.querySelectorAll('.enemy-card')]
                    .filter(back => getComputedStyle(back).visibility === 'hidden').length;
                const separate = moving.length === 2 && hiddenBacks() === 2;
                await Promise.all(moving.map(card => card.getAnimations()[0].finished));
                await new Promise(requestAnimationFrame);
                const consumed = hiddenBacks() === 2 && moving.every(card => !card.isConnected);
                stickyPawsBoardMotion.reset();
                return separate && consumed && hiddenBacks() === 0;
            }
            """);
        Assert.That(independent, Is.True);
    }

    [TestCase(ReducedMotion.Reduce)]
    [TestCase(ReducedMotion.NoPreference)]
    public async Task LastTrickStaysReadableAfterTheFlight(ReducedMotion motion)
    {
        await Page.EmulateMediaAsync(new() { ReducedMotion = motion });
        await OpenSceneAsync("inspect");
        await Expect(Page.GetByTestId("last-trick")).ToBeVisibleAsync();
        await Expect(Page.GetByTestId("last-trick").Locator("strong")).Not.ToBeEmptyAsync();
        await Expect(Page.GetByTestId("last-trick").Locator("p")).Not.ToBeEmptyAsync();
        await Expect(Page.GetByTestId("event-feed").GetByTestId("last-trick")).ToBeVisibleAsync();
        await Expect(Page.GetByTestId("last-trick")).ToHaveCountAsync(0, new() { Timeout = 8000 });
        await Expect(Page.GetByTestId("event-line").First).ToBeVisibleAsync();
        await Page.GetByTestId("event-feed-expand").ClickAsync();
        await Expect(Page.GetByTestId("last-trick")).ToHaveCountAsync(0);
    }

    [Test]
    public async Task ReducedMotionStillShowsTheTrickFace()
    {
        await Page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.Reduce });
        await Page.GotoAsync($"{BaseUrl}/dev/board?scene=inspect&animation=opponent-trick&env=offline");
        await Expect(Page.GetByTestId("trick-flight-destination")).ToBeVisibleAsync(new() { Timeout = BootTimeout });
        await Expect(Page.GetByTestId("trick-flight-destination").GetByTestId("card-face")).ToBeVisibleAsync();
        await Expect(Page.GetByTestId("board-motion-card")).ToHaveCountAsync(0);
    }

    async Task OpenSceneAsync(string scene)
    {
        await Page.GotoAsync($"{BaseUrl}/dev/board?scene={scene}&env=offline&motion=reduced");
        await Expect(Page.GetByTestId("match-board")).ToBeVisibleAsync(new() { Timeout = BootTimeout });
        await Page.EvaluateAsync("""
            () => { window.waitForCardHandoff = async (flights, destinations) => {
                const deadline = performance.now() + 2000;
                while (true) {
                    for (let index = 0; index < flights.length; index++) {
                        const ownsFlight = flights[index].isConnected;
                        const ownsDestination = getComputedStyle(destinations[index]).visibility !== 'hidden';
                        if (ownsFlight === ownsDestination)
                            throw new Error(`Card ${index} has ${ownsFlight ? 'duplicate' : 'missing'} owners during handoff`);
                    }
                    if (flights.every(flight => !flight.isConnected)) return;
                    if (performance.now() > deadline) throw new Error('Card handoff never completed');
                    await new Promise(requestAnimationFrame);
                }
            }; }
            """);
    }

    [Test]
    public async Task HandInspectionWaitsForHoverAndDismissesImmediately()
    {
        await OpenSceneAsync("inspect");

        ILocator card = Page.GetByTestId("hand-card").First;
        ILocator tooltip = card.GetByTestId("card-tooltip");

        await card.HoverAsync();
        await Expect(tooltip).ToBeHiddenAsync();
        await Expect(tooltip).ToBeVisibleAsync(new() { Timeout = 1000 });

        await Page.Mouse.MoveAsync(0, 0);
        await Expect(tooltip).ToBeHiddenAsync();
    }

    [Test]
    public async Task MouseHoverRecoversAfterTouchWithoutClicking()
    {
        await OpenSceneAsync("inspect");
        ILocator card = Page.GetByTestId("hand-card").First;
        await card.EvaluateAsync("""
            element => element.dispatchEvent(new PointerEvent('pointerdown', {
                bubbles: true, pointerType: 'touch'
            }))
            """);
        await Page.Mouse.MoveAsync(0, 0);
        await card.HoverAsync();
        await Expect(card.GetByTestId("card-tooltip")).ToBeVisibleAsync(new() { Timeout = 1500 });
    }

    [Test]
    public async Task HandHoverResumesAfterAnAnimationWithoutMovingTheMouse()
    {
        await OpenSceneAsync("play");
        // Keep a card which the replay will not consume under the pointer throughout the busy interval.
        ILocator card = Page.GetByTestId("hand-card").Last;
        string instance = (await card.GetAttributeAsync("data-instance"))!;
        card = Page.Locator($"[data-testid='hand-card'][data-instance='{instance}']");
        await card.HoverAsync();
        await Expect(card.GetByTestId("card-tooltip")).ToBeVisibleAsync();
        await Page.GetByTestId("preview-play").EvaluateAsync("element => element.click()");
        await Expect(Page.GetByTestId("hand")).Not.ToHaveClassAsync(new Regex("hand-inert"));
        await Expect(card.GetByTestId("card-tooltip")).ToBeVisibleAsync(new() { Timeout = 1500 });
    }

    [Test]
    public async Task HandTooltipPrefersTheRightAndFallsBackOnlyAtANarrowEdge()
    {
        await Page.SetViewportSizeAsync(1280, 720);
        await OpenSceneAsync("inspect");

        ILocator preferredCard = Page.GetByTestId("hand-card").First;
        await preferredCard.HoverAsync();
        ILocator preferredTooltip = preferredCard.GetByTestId("card-tooltip");
        await Expect(preferredTooltip).ToBeVisibleAsync(new() { Timeout = 1000 });

        LocatorBoundingBoxResult preferredCardBox = (await preferredCard.BoundingBoxAsync())!;
        LocatorBoundingBoxResult preferredTooltipBox = (await preferredTooltip.BoundingBoxAsync())!;
        Assert.That(preferredTooltipBox.X, Is.GreaterThan(preferredCardBox.X + preferredCardBox.Width),
            "a card with room should open its parchment on the preferred right side");

        await Page.SetViewportSizeAsync(500, 281);
        await OpenSceneAsync("inspect");

        ILocator edgeCard = Page.GetByTestId("hand-card").Last;
        await edgeCard.HoverAsync();
        ILocator edgeTooltip = edgeCard.GetByTestId("card-tooltip");
        await Expect(edgeTooltip).ToBeVisibleAsync(new() { Timeout = 1000 });

        LocatorBoundingBoxResult edgeCardBox = (await edgeCard.BoundingBoxAsync())!;
        LocatorBoundingBoxResult edgeTooltipBox = (await edgeTooltip.BoundingBoxAsync())!;
        Assert.Multiple(() =>
        {
            Assert.That(edgeTooltipBox.X + edgeTooltipBox.Width, Is.LessThan(edgeCardBox.X),
                "the right-edge card should mirror its parchment to the left when the preferred side clips");
            Assert.That(edgeTooltipBox.X, Is.GreaterThanOrEqualTo(0), "the fallback parchment stays in the viewport");
        });
    }

    [Test]
    public async Task TouchTapTogglesInspectionWithoutBlockingTheBoard()
    {
        await OpenSceneAsync("inspect");

        ILocator card = Page.GetByTestId("hand-card").First;
        await card.EvaluateAsync(
            """
            element => {
              element.dispatchEvent(new PointerEvent('pointerdown', {
                bubbles: true,
                pointerType: 'touch',
                isPrimary: true,
              }));
              element.click();
            }
            """);

        await Expect(card).ToHaveClassAsync(new Regex("handcard-peeking"));
        await Expect(card.GetByTestId("card-tooltip")).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Close card details" })).ToHaveCountAsync(0);

        await card.EvaluateAsync("""
            element => {
                element.dispatchEvent(new PointerEvent('pointerdown', { bubbles: true, pointerType: 'touch' }));
                element.click();
            }
            """);
        await Expect(card).Not.ToHaveClassAsync(new Regex("handcard-peeking"));
    }

    [TestCase(932, 300)]
    [TestCase(844, 280)]
    public async Task ShortLandscapeEnlargesInspectionButKeepsDraggingClearOfTheFriendlyRow(int width, int height)
    {
        await Page.SetViewportSizeAsync(width, height);
        await OpenSceneAsync("inspect");
        ILocator card = Page.GetByTestId("hand-card").First;
        await card.EvaluateAsync("""
            card => {
                card.dispatchEvent(new PointerEvent('pointerdown', { bubbles: true, pointerType: 'touch' }));
                card.dispatchEvent(new FocusEvent('blur', { bubbles: true }));
                card.dispatchEvent(new MouseEvent('mouseleave', { bubbles: true }));
                card.click();
            }
            """);
        await Expect(card).ToHaveClassAsync(new Regex("handcard-peeking"));
        await card.EvaluateAsync("async card => await Promise.all(card.getAnimations().map(a => a.finished))");
        double[] inspection = await card.EvaluateAsync<double[]>("""
            card => { const r = card.getBoundingClientRect(); return [r.width / card.offsetWidth, r.top, r.bottom]; }
            """);
        Assert.That(inspection[0], Is.GreaterThan(2), "inspection should make the card substantially larger");
        Assert.That(inspection[1], Is.GreaterThanOrEqualTo(0));
        Assert.That(inspection[2], Is.LessThanOrEqualTo(height));
        LocatorBoundingBoxResult tooltip = (await card.GetByTestId("card-tooltip").BoundingBoxAsync())!;
        Assert.That(tooltip.X, Is.GreaterThanOrEqualTo(0));
        Assert.That(tooltip.Y, Is.GreaterThanOrEqualTo(0));
        Assert.That(tooltip.X + tooltip.Width, Is.LessThanOrEqualTo(width));
        Assert.That(tooltip.Y + tooltip.Height, Is.LessThanOrEqualTo(height));
        await Page.Locator(".board-frame").EvaluateAsync("board => board.classList.add('is-touch-dragging')");
        await card.EvaluateAsync("async card => await Promise.all(card.getAnimations().map(a => a.finished))");
        await Expect(card.Locator(".handcard-peek")).ToBeHiddenAsync();
        double[] geometry = await Page.EvaluateAsync<double[]>("""
            () => {
                const board = document.querySelector('.board-frame').getBoundingClientRect();
                const cards = [...document.querySelectorAll('.handcard')].map(c => c.getBoundingClientRect());
                const row = [...document.querySelectorAll('.battlefield-row-mine .critter, .den-mine')]
                    .map(c => c.getBoundingClientRect());
                const left = document.querySelector('.piles').getBoundingClientRect();
                const right = document.querySelector('.turn-controls').getBoundingClientRect();
                return [board.width, Math.min(...cards.map(c => c.top)) - Math.max(...row.map(c => c.bottom)),
                        left.left, innerWidth - right.right];
            }
            """);
        Assert.That(geometry[0], Is.EqualTo(width).Within(1));
        Assert.That(geometry[2], Is.LessThan(16), "left controls stay by the window edge");
        Assert.That(geometry[3], Is.LessThan(16), "right controls stay by the window edge");
        Assert.That(geometry[1], Is.GreaterThanOrEqualTo(0), "the raised fan must stay below the friendly cards");
    }

    [Test]
    public async Task HealTargetingKeepsTheSourceRaisedAndUsesOneStableDenTarget()
    {
        await OpenSceneAsync("heal");

        await Expect(Page.GetByTestId("targeting-layer")).ToHaveAttributeAsync("data-kind", "Heal");
        await Expect(Page.GetByTestId("targeting-banner")).ToHaveCountAsync(0);

        ILocator targets = Page.Locator("[data-testid='board-critter'][data-targetable='true'] .card-face");
        int targetCount = await targets.CountAsync();
        Assert.That(targetCount, Is.GreaterThan(0));
        for (int ndx = 0; ndx < targetCount; ndx++)
            await Expect(targets.Nth(ndx)).ToHaveClassAsync(new Regex("is-target-heal"));

        ILocator selected = Page.Locator("[data-testid='hand-card'][data-selected='true']");
        string selectedInstance = (await selected.GetAttributeAsync("data-instance"))!;
        ILocator selectedCard = Page.Locator($"[data-testid='hand-card'][data-instance='{selectedInstance}']");
        double selectedScale = await selectedCard.EvaluateAsync<double>(
            "element => Math.hypot(new DOMMatrixReadOnly(getComputedStyle(element).transform).a, "
            + "new DOMMatrixReadOnly(getComputedStyle(element).transform).b)");

        ILocator den = Page.GetByTestId("den-mine");
        await Expect(den).ToHaveAttributeAsync("data-targetable", "true");
        await Expect(den).ToHaveClassAsync(new Regex("den-targetable-heal"));
        LocatorBoundingBoxResult before = (await den.BoundingBoxAsync())!;
        LocatorBoundingBoxResult card = (await Page.GetByTestId("board-critter").First.GetByTestId("card-face").BoundingBoxAsync())!;

        await den.HoverAsync();
        await WaitForSettledWidthAsync(den);
        LocatorBoundingBoxResult hovered = (await den.BoundingBoxAsync())!;

        Assert.Multiple(() =>
        {
            Assert.That(selectedScale, Is.GreaterThan(1.45), "the source card stays enlarged while it is choosing a target");
            Assert.That(before.Height, Is.EqualTo(card.Height).Within(2), "the Den target is one card-height physical object");
            Assert.That(hovered.Width, Is.GreaterThan(before.Width * 1.04), "hover, not eligibility, creates the pop");
            Assert.That(hovered.X + hovered.Width / 2, Is.EqualTo(before.X + before.Width / 2).Within(1),
                "the Den pop stays centred on its socket");
        });

        await den.ClickAsync();
        await Expect(den).ToHaveAttributeAsync("data-targetable", "false");
        await Page.Mouse.MoveAsync(0, 0);
        await WaitForSettledWidthAsync(den);
        LocatorBoundingBoxResult resolved = (await den.BoundingBoxAsync())!;
        Assert.That(resolved.X, Is.EqualTo(before.X).Within(1), "resolving the target must not displace the Den");
        await Expect(selectedCard).ToHaveAttributeAsync("data-selected", "false");
    }

    /// <summary>
    /// Wait until an element's width stops changing, rather than sleeping for as long as its transition is
    /// declared to take. The Den's pop is a 150 ms transition, so a fixed 200 ms wait is a 50 ms margin on a
    /// machine two suites are sharing — which is the throttling AGENTS.md is explicit about.
    /// </summary>
    static async Task WaitForSettledWidthAsync(ILocator locator)
    {
        await locator.EvaluateAsync(
            """
            element => new Promise(resolve => {
                let last = -1;
                let stable = 0;
                const tick = () => {
                    const width = element.getBoundingClientRect().width;
                    stable = Math.abs(width - last) < 0.05 ? stable + 1 : 0;
                    last = width;
                    // Three consecutive equal frames: one is a coincidence between two animation steps.
                    if (stable >= 3) resolve();
                    else requestAnimationFrame(tick);
                };
                requestAnimationFrame(tick);
            })
            """);
    }

    [Test]
    public async Task MulliganMarksStayRaisedAndWeatherKeepsItsMatchAnchor()
    {
        await OpenSceneAsync("mulligan");

        ILocator marked = Page.Locator("[data-testid='hand-card'][data-marked='true']").First;
        double markedScale = await marked.EvaluateAsync<double>(
            "element => Math.hypot(new DOMMatrixReadOnly(getComputedStyle(element).transform).a, "
            + "new DOMMatrixReadOnly(getComputedStyle(element).transform).b)");
        ILocator weather = Page.GetByTestId("mulligan-weather");
        ILocator controls = Page.Locator(".turn-controls");
        LocatorBoundingBoxResult weatherBox = (await weather.BoundingBoxAsync())!;
        LocatorBoundingBoxResult controlsBox = (await controls.BoundingBoxAsync())!;

        Assert.Multiple(() =>
        {
            Assert.That(markedScale, Is.GreaterThan(1.1), "a marked card must keep the selected lift after the pointer leaves");
            Assert.That(weatherBox.X, Is.EqualTo(controlsBox.X).Within(1));
            Assert.That(weatherBox.Y, Is.EqualTo(controlsBox.Y).Within(1),
                "mulligan Weather belongs at the same right-side anchor as match Weather");
        });
    }

    [Test]
    public async Task SelectedAttackerStaysRaisedAndTheHandOwnsTheForeground()
    {
        await OpenSceneAsync("midturn");

        ILocator attacker = Page.Locator("[data-testid='board-critter'][data-selected='true']");
        double attackerScale = await attacker.EvaluateAsync<double>(
            "element => Math.hypot(new DOMMatrixReadOnly(getComputedStyle(element).transform).a, "
            + "new DOMMatrixReadOnly(getComputedStyle(element).transform).b)");
        int handZ = await Page.GetByTestId("hand").EvaluateAsync<int>("element => Number(getComputedStyle(element).zIndex)");
        int liveCritterZ = await Page.Locator(".critter-live").First.EvaluateAsync<int>(
            "element => Number(getComputedStyle(element).zIndex)");

        Assert.Multiple(() =>
        {
            Assert.That(attackerScale, Is.GreaterThan(1.15), "the selected attacker stays lifted while choosing a target");
            Assert.That(handZ, Is.GreaterThan(liveCritterZ), "even an elevated battlefield card stays behind the hand");
        });
    }

    [Test]
    public async Task SneakyUsesAnOpaqueLiveStateSymbol()
    {
        await OpenSceneAsync("sneaky");

        ILocator dynamicSneaky = Page.Locator(
            "[data-testid='board-critter']:not([data-card-id='MoonlitAlleycat']):not([data-card-id='RaccoonRingleader']) "
            + "[data-testid='card-state-sneaky']").First;
        await Expect(dynamicSneaky).ToBeVisibleAsync(new() { Timeout = BootTimeout });

        ILocator face = dynamicSneaky.Locator("xpath=ancestor::*[@data-testid='card-face']");
        string borderStyle = await face.Locator(".card-face-backing").EvaluateAsync<string>(
            "element => getComputedStyle(element).borderStyle");
        string symbolOpacity = await dynamicSneaky.EvaluateAsync<string>("element => getComputedStyle(element).opacity");
        string sneaky = await face.GetAttributeAsync("data-sneaky") ?? "";

        Assert.Multiple(() =>
        {
            Assert.That(sneaky, Is.EqualTo("true"));
            Assert.That(borderStyle, Is.EqualTo("solid"), "Sneaky must not reuse the dashed disabled vocabulary");
            Assert.That(symbolOpacity, Is.EqualTo("1"), "the live Sneaky symbol must remain fully opaque");
        });
    }

    [Test]
    public async Task ActionableTargetsStayAboveTheTapAwaySurface()
    {
        await OpenSceneAsync("midturn");

        ILocator critterTargets = Page.Locator("[data-testid='board-critter'][data-targetable='true']");
        int critterTargetCount = await critterTargets.CountAsync();
        for (int ndx = 0; ndx < critterTargetCount; ndx++)
            await critterTargets.Nth(ndx).ClickAsync(new() { Trial = true });

        ILocator denTargets = Page.Locator("[data-testid^='den-'][data-targetable='true']");
        int denTargetCount = await denTargets.CountAsync();
        for (int ndx = 0; ndx < denTargetCount; ndx++)
            await denTargets.Nth(ndx).ClickAsync(new() { Trial = true });
    }

    [Test]
    public async Task ResolutionPosesRunTheirConcreteCssAnimations()
    {
        (string Name, string Beat, string Selector, string Animation)[] poses =
        {
            ("damage", "damage", ".critter-beat-damage", "critter-impact"),
            ("heal", "heal", ".critter-beat-heal", "critter-heal"),
            ("den-damage", "dendamage", ".den-beat-damage", "den-impact"),
            ("death", "death", ".critter-beat-death", "critter-to-grave"),
        };

        foreach ((string name, string beat, string selector, string expectedAnimation) in poses)
        {
            await Page.GotoAsync($"{BaseUrl}/dev/board?scene=inspect&animation={name}&env=offline");
            await Expect(Page.GetByTestId("match-board")).ToHaveAttributeAsync(
                "data-beat", beat, new() { Timeout = BootTimeout });

            ILocator animated = Page.Locator(selector).First;
            await animated.WaitForAsync(new() { State = WaitForSelectorState.Attached, Timeout = BootTimeout });
            string animationName = await animated.EvaluateAsync<string>(
                "element => getComputedStyle(element).animationName");

            Assert.That(animationName, Does.Contain(expectedAnimation), $"the '{name}' pose has no visible motion");
        }
    }

    [Test]
    public async Task HandCardsShareOneAnchorAndOverlapAlongTheFan()
    {
        await OpenSceneAsync("inspect");
        bool sharedAnchor = await Page.GetByTestId("hand").EvaluateAsync<bool>(
            """
            hand => {
                const cards = [...hand.querySelectorAll('[data-testid="hand-card"]')];
                const first = cards[0];
                return cards.length > 1 && cards.every(card => card.offsetLeft === first.offsetLeft)
                    && Math.abs(cards[1].getBoundingClientRect().x - first.getBoundingClientRect().x)
                        < first.offsetWidth;
            }
            """);
        Assert.That(sharedAnchor, Is.True, "the fan owns spacing; removing a card must not move a flex layout underneath it");
    }

    [Test]
    public async Task CardFlightKeepsItsFaceUntilTheDestinationArrives()
    {
        await OpenSceneAsync("inspect");
        bool continuous = await Page.EvaluateAsync<bool>(
            """
            async () => {
                const source = document.querySelector('[data-testid="hand-card"]');
                const id = source.dataset.instance;
                const slot = document.createElement('div');
                slot.dataset.testid = 'critter-entry-slot';
                slot.dataset.entryInstance = id;
                slot.style.cssText = 'position:fixed;left:400px;top:250px;width:96px;height:140px';
                document.body.append(slot);
                stickyPawsBoardMotion.run(900, 'cardplay', id, 'none', 0, 80, false);
                const flying = document.querySelector('[data-testid="board-motion-card"]');
                await flying.getAnimations()[0].finished;
                await new Promise(requestAnimationFrame);
                const held = flying.isConnected && getComputedStyle(source).visibility === 'hidden';
                source.classList.remove('board-motion-source-hidden');
                await new Promise(requestAnimationFrame);
                const stillHidden = getComputedStyle(source).visibility === 'hidden';
                const destination = source.cloneNode(true);
                destination.dataset.testid = 'board-critter';
                slot.append(destination);
                await new Promise(requestAnimationFrame);
                const previewHidden = flying.isConnected && getComputedStyle(destination).visibility === 'hidden';
                const face = flying.querySelectorAll('.card-face')[1];
                await Promise.all(face.getAnimations().map(animation => animation.finished));
                const settled = destination.cloneNode(true);
                settled.style.cssText = slot.style.cssText;
                settled.style.transform = 'none';
                slot.replaceWith(settled);
                await new Promise(requestAnimationFrame);
                await waitForCardHandoff([flying], [settled]);
                return held && stillHidden && previewHidden && !flying.isConnected
                    && getComputedStyle(settled).visibility !== 'hidden'
                    && getComputedStyle(source).visibility === 'hidden';
            }
            """);
        Assert.That(continuous, Is.True, "the travelling face owns the card across a delayed destination render");
    }

    [Test]
    public async Task CardFlightMorphsBeforeArrivalAndSurvivesTheSettledRender()
    {
        await OpenSceneAsync("inspect");
        string[] failures = await Page.EvaluateAsync<string[]>(
            """
            async () => {
                const source = document.querySelector('[data-testid="hand-card"]');
                const id = source.dataset.instance;
                const slot = document.createElement('div');
                slot.dataset.testid = 'critter-entry-slot';
                slot.dataset.entryInstance = id;
                slot.style.cssText = 'position:fixed;left:400px;top:250px;width:96px;height:140px';
                const entering = source.cloneNode(true);
                entering.dataset.testid = 'board-critter';
                entering.querySelector('.card-face').classList.add('is-sleepy');
                slot.append(entering);
                document.body.append(slot);
                const targetOpacity = Number(getComputedStyle(entering.querySelector('.card-face')).opacity);
                stickyPawsBoardMotion.run(910, 'cardplay', id, 'none', 0, 400, false);
                const moving = document.querySelector('[data-testid="board-motion-card"]');
                const arrivalFace = moving.querySelectorAll('.card-face')[1];
                const initialHidden = getComputedStyle(entering).visibility === 'hidden';
                await Promise.all(arrivalFace.getAnimations().map(animation => animation.finished));
                const morphedBeforeLanding = moving.getAnimations()[0].playState === 'running'
                    && Math.abs(Number(getComputedStyle(arrivalFace).opacity) - targetOpacity) < .01
                    && arrivalFace.classList.contains('is-sleepy');
                const settled = entering.cloneNode(true);
                settled.style.cssText = slot.style.cssText;
                settled.style.transform = 'none';
                slot.replaceWith(settled);
                await new Promise(requestAnimationFrame);
                const replacementHidden = getComputedStyle(settled).visibility === 'hidden';
                await moving.getAnimations()[0].finished;
                await new Promise(requestAnimationFrame);
                await waitForCardHandoff([moving], [settled]);
                return Object.entries({ initialHidden, morphedBeforeLanding, replacementHidden,
                    flightRemoved: !moving.isConnected,
                    settledVisible: getComputedStyle(settled).visibility !== 'hidden',
                    handHidden: getComputedStyle(source).visibility === 'hidden',
                }).filter(([name, passed]) => !passed).map(([name]) => name);
            }
            """);
        Assert.That(failures, Is.Empty, "the confirmed sleep/keyword face must arrive without exposing a duplicate");
    }

    [Test]
    public async Task ConsecutivePlaysKeepIndependentFlightsUntilBothDestinationsSettle()
    {
        await OpenSceneAsync("inspect");
        bool continuous = await Page.EvaluateAsync<bool>(
            """
            async () => {
                const sources = [...document.querySelectorAll('[data-testid="hand-card"]')].slice(0, 2);
                const slots = sources.map((source, index) => {
                    const slot = document.createElement('div');
                    slot.dataset.testid = 'critter-entry-slot';
                    slot.dataset.entryInstance = source.dataset.instance;
                    slot.style.cssText = `position:fixed;left:${400 + index * 120}px;top:250px;width:96px;height:140px`;
                    const entering = source.cloneNode(true);
                    entering.dataset.testid = 'board-critter';
                    slot.append(entering);
                    document.body.append(slot);
                    return slot;
                });
                stickyPawsBoardMotion.run(912, 'cardplay', sources[0].dataset.instance, 'none', 0, 400, false);
                const first = document.querySelector('[data-testid="board-motion-card"]');
                const firstFlight = first.getAnimations()[0];
                stickyPawsBoardMotion.run(913, 'cardplay', sources[1].dataset.instance, 'none', 0, 240, false);
                const flights = [...document.querySelectorAll('[data-testid="board-motion-card"]')];
                const independent = flights.length === 2 && first.isConnected
                    && first.getAnimations()[0] === firstFlight;
                await Promise.all(flights.map(flight => flight.getAnimations()[0].finished));
                const held = flights.every(flight => flight.isConnected);
                const settled = slots.map(slot => {
                    const destination = slot.firstElementChild.cloneNode(true);
                    destination.style.cssText = slot.style.cssText;
                    destination.style.transform = 'none';
                    slot.replaceWith(destination);
                    return destination;
                });
                await new Promise(requestAnimationFrame);
                await waitForCardHandoff(flights, settled);
                const handedOff = flights.every(flight => !flight.isConnected)
                    && settled.every(card => getComputedStyle(card).visibility !== 'hidden')
                    && sources.every(card => getComputedStyle(card).visibility === 'hidden');
                stickyPawsBoardMotion.reset();
                return independent && held && handedOff
                    && sources.every(card => getComputedStyle(card).visibility !== 'hidden');
            }
            """);
        Assert.That(continuous, Is.True, "a subsequent play must never interrupt an existing flight or leave hidden sources behind");
    }

    [Test]
    public async Task OpponentPlayRevealsOneBackAndMatchesTheDimmedDestination()
    {
        await OpenSceneAsync("inspect");
        bool continuous = await Page.EvaluateAsync<bool>(
            """
            async () => {
                const backs = [...document.querySelectorAll('[data-testid="opponent-hand"] .enemy-card')];
                const source = backs.at(-1);
                const slot = document.createElement('div');
                slot.dataset.testid = 'critter-entry-slot';
                slot.dataset.entryInstance = '987654';
                slot.style.cssText = 'position:fixed;left:400px;top:250px;width:96px;height:140px';
                const entering = document.querySelector('[data-testid="board-critter"]').cloneNode(true);
                entering.dataset.instance = '987654';
                entering.querySelector('.card-face').classList.add('is-spent');
                slot.append(entering);
                document.querySelector('.critter-row-enemy').append(slot);
                const targetOpacity = Number(getComputedStyle(entering.querySelector('.card-face')).opacity);
                stickyPawsBoardMotion.run(911, 'cardplay', 987654, 'none', 0, 240, false);
                const moving = document.querySelector('[data-testid="board-motion-card"]');
                const onlyOneHidden = backs.filter(back => getComputedStyle(back).visibility === 'hidden').length === 1;
                const revealed = moving.querySelector('.card-face');
                await Promise.all(revealed.getAnimations().map(animation => animation.finished));
                const opacityMatches = Math.abs(Number(getComputedStyle(revealed).opacity) - targetOpacity) < .01;
                const settled = entering.cloneNode(true);
                settled.style.cssText = slot.style.cssText;
                settled.style.transform = 'none';
                slot.replaceWith(settled);
                await moving.getAnimations()[0].finished;
                await new Promise(requestAnimationFrame);
                await waitForCardHandoff([moving], [settled]);
                return onlyOneHidden && opacityMatches && !moving.isConnected
                    && getComputedStyle(settled).visibility !== 'hidden'
                    && getComputedStyle(source).visibility !== 'hidden';
            }
            """);
        Assert.That(continuous, Is.True, "an opponent play moves one back and blends into the exact settled opacity");
    }

    [Test]
    public async Task AttackReturnsWithoutWaitingForAnotherServerBeat()
    {
        await OpenSceneAsync("inspect");
        bool returned = await Page.EvaluateAsync<bool>(
            """
            async () => {
                const source = document.querySelector('[data-testid="board-critter"][data-seat="0"]');
                const target = document.querySelector('[data-testid="board-critter"][data-seat="1"]');
                stickyPawsBoardMotion.run(901, 'attack', source.dataset.instance,
                    'critter', target.dataset.instance, 80, false);
                const moving = document.querySelector('[data-testid="board-motion-attacker"]');
                await moving.getAnimations()[0].finished;
                await new Promise(requestAnimationFrame);
                const returning = moving.getAnimations().find(a => a.id === 'sticky-paws-attack-return');
                stickyPawsBoardMotion.run(902, 'dendamage', 0, 'den', 1, 80, false);
                const returns = moving.getAnimations().filter(a => a.id === 'sticky-paws-attack-return');
                await returning.finished;
                return returns.length === 1 && !moving.isConnected
                    && getComputedStyle(source).visibility !== 'hidden';
            }
            """);
        Assert.That(returned, Is.True, "a delayed or repeated follow-up must neither freeze nor restart the return");
    }

    [Test]
    public async Task ANewAttackDoesNotCutOffThePreviousReturn()
    {
        await OpenSceneAsync("inspect");
        bool continuous = await Page.EvaluateAsync<bool>("""
            async () => {
                const sources = [...document.querySelectorAll('[data-testid="board-critter"][data-seat="0"]')];
                stickyPawsBoardMotion.run(910, 'attack', sources[0].dataset.instance, 'den', 1, 1000, false);
                const first = document.querySelector('[data-testid="board-motion-attacker"]');
                first.getAnimations()[0].finish();
                await Promise.resolve();
                const returning = first.getAnimations().find(a => a.id === 'sticky-paws-attack-return');
                stickyPawsBoardMotion.run(911, 'attack', sources[1].dataset.instance, 'den', 1, 1000, false);
                const twoOwners = document.querySelectorAll('[data-testid="board-motion-attacker"]').length === 2;
                const retained = first.isConnected && returning.playState !== 'finished';
                returning.finish();
                await Promise.resolve();
                const secondRemains = document.querySelectorAll('[data-testid="board-motion-attacker"]').length === 1;
                stickyPawsBoardMotion.reset();
                return twoOwners && retained && secondRemains;
            }
            """);
        Assert.That(continuous, Is.True);
    }

    [Test]
    public async Task ReusingAnAttackerQueuesItsFlightAndResetCancelsThatQueue()
    {
        await OpenSceneAsync("inspect");
        bool cancelled = await Page.EvaluateAsync<bool>("""
            async () => {
                const source = document.querySelector('[data-testid="board-critter"][data-seat="0"]');
                stickyPawsBoardMotion.run(912, 'attack', source.dataset.instance, 'den', 1, 1000, false);
                stickyPawsBoardMotion.run(913, 'attack', source.dataset.instance, 'den', 1, 1000, false);
                const singleOwner = document.querySelectorAll('[data-testid="board-motion-attacker"]').length === 1;
                stickyPawsBoardMotion.reset();
                await Promise.resolve();
                return singleOwner && !document.querySelector('[data-testid="board-motion-attacker"]')
                    && !source.classList.contains('board-motion-source-hidden');
            }
            """);
        Assert.That(cancelled, Is.True);
    }

    [Test]
    public async Task AReturningCardFollowsItsSocketIfTheRowReflows()
    {
        await OpenSceneAsync("inspect");
        bool continuous = await Page.EvaluateAsync<bool>("""
            async () => {
                const source = document.querySelector('[data-testid="board-critter"][data-seat="0"]');
                stickyPawsBoardMotion.run(914, 'attack', source.dataset.instance, 'den', 1, 1000, false);
                const moving = document.querySelector('[data-testid="board-motion-attacker"]');
                moving.getAnimations()[0].finish();
                await Promise.resolve();
                const returning = moving.getAnimations().find(a => a.id === 'sticky-paws-attack-return');
                source.parentElement.style.translate = '40px 0px';
                returning.finish();
                await Promise.resolve();
                const retained = moving.isConnected && source.classList.contains('board-motion-source-hidden');
                await Promise.all(moving.getAnimations().map(animation => animation.finished));
                await Promise.resolve();
                const handedOff = !moving.isConnected && !source.classList.contains('board-motion-source-hidden');
                stickyPawsBoardMotion.reset();
                return retained && handedOff;
            }
            """);
        Assert.That(continuous, Is.True);
    }

    [Test]
    public async Task FlyingFaceMatchesTheRotatedSourceAtTakeoff()
    {
        await OpenSceneAsync("inspect");
        double error = await Page.EvaluateAsync<double>(
            """
            () => {
                const source = document.querySelector('[data-testid="hand-card"]');
                const target = document.querySelector('[data-testid="board-critter"]');
                const before = source.querySelector('.card-face').getBoundingClientRect();
                stickyPawsBoardMotion.run(903, 'cardplay', source.dataset.instance,
                    'critter', target.dataset.instance, 1000, false);
                const moving = document.querySelector('[data-testid="board-motion-card"]');
                const animation = moving.getAnimations()[0];
                animation.pause();
                animation.currentTime = 0;
                const after = moving.querySelector('.card-face').getBoundingClientRect();
                return Math.max(Math.abs(before.x - after.x), Math.abs(before.y - after.y),
                    Math.abs(before.width - after.width), Math.abs(before.height - after.height));
            }
            """);
        Assert.That(error, Is.LessThan(1), "takeoff must preserve the face's actual size, rotation and position");
    }

    [Test]
    public async Task CardPlayAndAttackUseTheirRealDestinationGeometry()
    {
        await Page.GotoAsync($"{BaseUrl}/dev/board?scene=inspect&animation=play&env=offline");
        ILocator flyingCard = Page.GetByTestId("board-motion-card");
        await flyingCard.WaitForAsync(new() { State = WaitForSelectorState.Attached, Timeout = BootTimeout });
        await Expect(Page.GetByTestId("critter-entry-slot")).ToBeVisibleAsync();
        bool playLandsInSlot = await flyingCard.EvaluateAsync<bool>(
            "element => {"
            + "const slot=document.querySelector('[data-testid=critter-entry-slot]').getBoundingClientRect();"
            + "const frame=element.getAnimations()[0].effect.getKeyframes().at(-1);"
            + "const matrix=new DOMMatrixReadOnly(frame.transform);"
            + "const x=Number.parseFloat(element.style.left)+element.offsetWidth/2+matrix.e;"
            + "const y=Number.parseFloat(element.style.top)+element.offsetHeight/2+matrix.f;"
            + "return Math.hypot(x-(slot.left+slot.width/2),y-(slot.top+slot.height/2))<3;}");
        string playAnimation = await flyingCard.EvaluateAsync<string>("element => element.getAnimations()[0].id");

        await Page.GotoAsync($"{BaseUrl}/dev/board?scene=inspect&animation=attack&env=offline");
        ILocator attacker = Page.GetByTestId("board-motion-attacker");
        await attacker.WaitForAsync(new() { State = WaitForSelectorState.Attached, Timeout = BootTimeout });
        bool attackClosesOnTarget = await attacker.EvaluateAsync<bool>(
            "element => {"
            + "const target=document.querySelector('[data-testid=board-critter][data-seat=\"1\"]').getBoundingClientRect();"
            + "const frame=element.getAnimations()[0].effect.getKeyframes().at(-1);"
            + "const matrix=new DOMMatrixReadOnly(frame.transform);"
            + "const sx=Number.parseFloat(element.style.left)+element.offsetWidth/2;"
            + "const sy=Number.parseFloat(element.style.top)+element.offsetHeight/2;"
            + "const tx=target.left+target.width/2; const ty=target.top+target.height/2;"
            + "return Math.hypot(sx+matrix.e-tx,sy+matrix.f-ty)<Math.hypot(sx-tx,sy-ty)*0.35;}");
        string attackAnimation = await attacker.EvaluateAsync<string>("element => element.getAnimations()[0].id");

        Assert.Multiple(() =>
        {
            Assert.That(playAnimation, Is.EqualTo("sticky-paws-card-play"));
            Assert.That(playLandsInSlot, Is.True, "the flying hand card lands in the slot highlighted before takeoff");
            Assert.That(attackAnimation, Is.EqualTo("sticky-paws-attack-lunge"));
            Assert.That(attackClosesOnTarget, Is.True, "the attacker lunges along the target vector rather than shaking in place");
        });
    }
}
