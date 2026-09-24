using Microsoft.Playwright;

namespace Game.Client.Tests;

/// <summary> Board peripherals keep their controls and readable values through the real Blazor shell. </summary>
[TestFixture]
public sealed class BoardChromeTests : OfflineTestBase
{
    [Test]
    public async Task ResizingKeepsChromeAndOpponentAtTheTopAndHandAtTheBottom()
    {
        await OpenBoardAsync();
        foreach (int height in new[] { 720, 900, 1024, 720 })
        {
            await Page.SetViewportSizeAsync(1280, height);
            LocatorBoundingBoxResult frame = (await Page.GetByTestId("match-board").BoundingBoxAsync())!;
            LocatorBoundingBoxResult chrome = (await Page.GetByTestId("board-connection").BoundingBoxAsync())!;
            LocatorBoundingBoxResult opponent = (await Page.Locator(".battlefield-row-enemy").BoundingBoxAsync())!;
            Assert.That(frame.Y, Is.EqualTo(0).Within(1));
            Assert.That(frame.Height, Is.EqualTo(height).Within(1));
            Assert.That(chrome.Y, Is.LessThan(height * 0.04));
            Assert.That(opponent.Y, Is.EqualTo(height * 0.1835).Within(2));
            Assert.That(await Page.Locator(".hand").EvaluateAsync<bool>(
                "e => e.getBoundingClientRect().bottom >= window.innerHeight"), Is.True);
        }
    }

    async Task OpenBoardAsync()
    {
        await Page.SetViewportSizeAsync(1280, 720);
        await Page.GotoAsync($"{BaseUrl}/dev/board?scene=midturn&env=offline");
        await Expect(Page.GetByTestId("match-board")).ToBeVisibleAsync(new() { Timeout = BootTimeout });
    }

    [Test]
    public async Task BoardOwnsItsChromeAndTucksResetBehindDeveloperTools()
    {
        await OpenBoardAsync();
        await Expect(Page.GetByTestId("board-connection")).ToContainTextAsync("Preview ·");
        await Expect(Page.GetByTestId("leave-match")).ToHaveCountAsync(1);
        await Expect(Page.GetByTestId("app-header")).ToHaveCountAsync(0);
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Reset account" })).ToBeHiddenAsync();
        await Page.GetByLabel("Developer tools", new() { Exact = true }).ClickAsync();
        await Expect(Page.GetByTestId("board-player-id")).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Reset account" })).ToBeVisibleAsync();

        // A client-side route change swaps the board's chrome for the metagame top bar without a reload.
        await Page.EvaluateAsync("""
            () => {
                const link = document.createElement('a');
                link.href = '/?env=offline';
                document.body.append(link);
                link.click();
                link.remove();
            }
            """);
        await Expect(Page.GetByTestId("app-header")).ToBeVisibleAsync();
        await Expect(Page.GetByTestId("board-connection")).ToHaveCountAsync(0);
    }

    [Test]
    public async Task AcornCountSitsBesideItsPlaqueAndLogWrapsItsEntries()
    {
        await OpenBoardAsync();
        ILocator mana = Page.GetByTestId("mana-row");
        int maxMana = int.Parse((await mana.GetAttributeAsync("data-max-mana"))!);
        await Expect(Page.GetByTestId("mana-acorn")).ToHaveCountAsync(maxMana);
        LocatorBoundingBoxResult acorns = (await mana.Locator(".acorns").BoundingBoxAsync())!;
        LocatorBoundingBoxResult readout = (await Page.GetByTestId("mana-readout").BoundingBoxAsync())!;
        Assert.That(readout.X, Is.GreaterThanOrEqualTo(acorns.X + acorns.Width), "the readout is inline with the acorns");
        await Expect(Page.GetByTestId("event-feed").GetByText("Latest", new() { Exact = true })).ToBeVisibleAsync();
        await Expect(Page.GetByTestId("event-line").First).ToHaveCSSAsync("white-space", "normal");

        ILocator expand = Page.GetByTestId("event-feed-expand");
        await expand.ClickAsync();
        await Expect(expand).ToHaveAttributeAsync("aria-expanded", "true");
        Assert.That(await Page.GetByTestId("event-line").CountAsync(), Is.GreaterThan(3));
        await expand.ClickAsync();
        await Expect(Page.GetByTestId("event-line")).ToHaveCountAsync(3);
    }

    /// <summary>
    /// The pre-match reveal states the wager and <b>gets out of the way</b>. That it takes no pointer events
    /// is the whole reason it can be drawn above both the mulligan's scrim and the raised hand: the mulligan
    /// deadline is already running when the client boots, so a panel that intercepted a press would be
    /// spending the clock the decision underneath it needs.
    /// </summary>
    [Test]
    public async Task ThePreMatchRevealStatesTheWagerWithoutTakingAPress()
    {
        await Page.SetViewportSizeAsync(1280, 720);
        await Page.GotoAsync($"{BaseUrl}/dev/board?scene=reveal&env=offline");
        await Expect(Page.GetByTestId("match-board")).ToBeVisibleAsync(new() { Timeout = BootTimeout });

        ILocator reveal = Page.GetByTestId("pre-match-reveal");
        await Expect(reveal).ToBeVisibleAsync();

        // Everything game-design.md's list names, and nothing else: the Weather, both names, both Power Scores,
        // both lock sets and the tier those imply.
        await Expect(Page.GetByTestId("reveal-weather-name")).Not.ToBeEmptyAsync();
        await Expect(Page.GetByTestId("reveal-name")).ToHaveCountAsync(2);
        await Expect(Page.GetByTestId("reveal-power-score")).ToHaveCountAsync(2);
        await Expect(Page.GetByTestId("reveal-locks")).ToHaveCountAsync(2);
        await Expect(Page.GetByTestId("reveal-tier")).Not.ToBeEmptyAsync();

        // Two humans, so no computer-player mark and a tier that names a favourite.
        await Expect(Page.GetByTestId("reveal-bot-label")).ToHaveCountAsync(0);
        await Expect(reveal).ToHaveAttributeAsync("data-tier", "Favourite");
        await Expect(reveal).ToHaveAttributeAsync("data-ranked", "true");

        // The padlocks are open information and both sets froze at enqueue, so each seat's count is stated
        // rather than implied.
        await Expect(Page.Locator("[data-testid='reveal-locks'][data-seat='0']")).ToHaveAttributeAsync("data-count", "2");
        await Expect(Page.Locator("[data-testid='reveal-locks'][data-seat='1']")).ToHaveAttributeAsync("data-count", "1");

        await Expect(reveal).ToHaveCSSAsync("pointer-events", "none");

        // And the mulligan underneath it is still the mulligan: the panel moved none of its test ids and
        // intercepts none of its presses.
        await Expect(Page.GetByTestId("mulligan-overlay")).ToBeVisibleAsync();
        await Expect(Page.GetByTestId("mulligan-confirm")).ToBeEnabledAsync();
    }

    [TestCase(1440, 900)]
    [TestCase(2560, 1080)]
    [TestCase(844, 390)]
    [TestCase(640, 360)]
    public async Task OpeningSummaryStaysCenteredAndClearOfTheHand(int width, int height)
    {
        await Page.SetViewportSizeAsync(width, height);
        await Page.GotoAsync($"{BaseUrl}/dev/board?scene=reveal&env=offline");
        ILocator reveal = Page.GetByTestId("pre-match-reveal");
        await Expect(reveal).ToBeVisibleAsync(new() { Timeout = BootTimeout });
        // Measure the settled entrance pose: its animation must not replace the centering transform.
        await reveal.EvaluateAsync("async element => await Promise.all(element.getAnimations().map(a => a.finished))");
        LocatorBoundingBoxResult summary = (await reveal.BoundingBoxAsync())!;
        LocatorBoundingBoxResult prompt = (await Page.Locator(".board-overlay-mulligan .overlay-panel").BoundingBoxAsync())!;
        LocatorBoundingBoxResult backdrop = (await Page.Locator(".board-backdrop").BoundingBoxAsync())!;
        Assert.Multiple(() =>
        {
            Assert.That(summary.X + summary.Width / 2, Is.EqualTo(width / 2.0).Within(1));
            Assert.That(summary.Y + summary.Height, Is.LessThan(prompt.Y), "the wager must not cover the opening-hand prompt");
            Assert.That(backdrop.X, Is.EqualTo(0).Within(1));
            Assert.That(backdrop.Y, Is.EqualTo(0).Within(1));
            Assert.That(backdrop.Width, Is.EqualTo(width).Within(1));
            Assert.That(backdrop.Height, Is.EqualTo(height).Within(1));
        });
        await Expect(Page.GetByTestId("reveal-weather-description")).Not.ToBeEmptyAsync();
        await Expect(Page.GetByTestId("mulligan-weather")).ToBeHiddenAsync();
        await Expect(Page.Locator(".board-overlay-mulligan h3")).ToHaveCSSAsync("font-family", "\"Lilita One\", system-ui");
        await Expect(Page.GetByTestId("mulligan-confirm")).ToBeEnabledAsync();
        foreach (ILocator card in await Page.GetByTestId("hand-card").AllAsync())
        {
            LocatorBoundingBoxResult box = (await card.BoundingBoxAsync())!;
            Assert.That(prompt.Y + prompt.Height, Is.LessThan(box.Y), "the opening controls stay above the cards");
        }
    }

    /// <summary>
    /// The stages of losing a seat to the clock and getting it back, and what the board says at each. There is
    /// deliberately no strike total drawn anywhere: the first notice says a lapse cost something and what the
    /// next one costs, the second says a bot has the seat and how to take it back, and the third that it is
    /// the owner's again from the next turn.
    /// <para>
    /// Driven from the scenes, which is what makes every stage inspectable with no server and no match — the
    /// notices are presentation, and this is the cheapest suite that can catch a regression in them.
    /// <see cref="MatchStrikeTests"/> is where the same lines are read off a real table that a real deadline
    /// lapsed on, and `MatchSeatPolicyTests` is where the escalation and the hand-back are decided.
    /// </para>
    /// </summary>
    [Test]
    public async Task ALapsedTurnSaysWhatItCostAndACoveredSeatSaysHowToTakeItBack()
    {
        await Page.SetViewportSizeAsync(1280, 720);
        await Page.GotoAsync($"{BaseUrl}/dev/board?scene=struck&env=offline");
        await Expect(Page.GetByTestId("match-board")).ToBeVisibleAsync(new() { Timeout = BootTimeout });

        ILocator notice = Page.GetByTestId("covered-seat-notice");
        await Expect(notice).ToBeVisibleAsync();
        await Expect(notice).ToHaveAttributeAsync("data-stage", "struck");
        await Expect(notice).ToContainTextAsync("One more and a bot takes the seat");

        // One lapse is being slow: the seat is still its owner's, so their plaque carries no computer mark.
        await Expect(Page.GetByTestId("den-mine").GetByTestId("den-bot-mark")).ToHaveCountAsync(0);

        await Page.GotoAsync($"{BaseUrl}/dev/board?scene=covered&env=offline");
        await Expect(Page.GetByTestId("match-board")).ToBeVisibleAsync(new() { Timeout = BootTimeout });

        notice = Page.GetByTestId("covered-seat-notice");
        await Expect(notice).ToBeVisibleAsync();
        await Expect(notice).ToHaveAttributeAsync("data-stage", "covered");

        // Recovery is available independently of the game controls.
        await Expect(notice).ToHaveAttributeAsync("data-on-turn", "true");
        await Expect(Page.GetByTestId("reclaim-seat")).ToBeEnabledAsync();

        // The cover is not a disguise: the plaque keeps the owner's name and adds the mark beside it. And the
        // notice lets presses through except for its explicit recovery button.
        ILocator ownDen = Page.GetByTestId("den-mine");
        await Expect(ownDen.GetByTestId("den-bot-mark")).ToBeVisibleAsync();
        await Expect(ownDen.GetByTestId("den-name")).Not.ToBeEmptyAsync();
        await Expect(notice).ToHaveCSSAsync("pointer-events", "none");
        await Expect(Page.GetByTestId("reclaim-seat")).ToHaveCSSAsync("pointer-events", "auto");

        // Only one of the two is ever up, and it is the one that helps.
        await Expect(notice).ToHaveCountAsync(1);

        // Even off turn, recovery must be available while ordinary game controls are disabled.
        await Page.GotoAsync($"{BaseUrl}/dev/board?scene=covered-waiting&env=offline");
        await Expect(Page.GetByTestId("match-board")).ToBeVisibleAsync(new() { Timeout = BootTimeout });

        notice = Page.GetByTestId("covered-seat-notice");
        await Expect(notice).ToHaveAttributeAsync("data-stage", "covered");
        await Expect(notice).ToHaveAttributeAsync("data-on-turn", "false");
        await Expect(Page.GetByTestId("reclaim-seat")).ToBeEnabledAsync();
        await Expect(Page.GetByTestId("den-mine").GetByTestId("den-bot-mark")).ToBeVisibleAsync();
        await Expect(Page.GetByTestId("end-turn")).Not.ToBeEnabledAsync();
        await Page.EvaluateAsync("window.reclaimReloadProbe = true");
        await Page.GetByTestId("reclaim-seat").ClickAsync();
        await Page.WaitForFunctionAsync("window.reclaimReloadProbe === undefined");
        await Expect(Page.GetByTestId("match-board")).ToBeVisibleAsync(new() { Timeout = BootTimeout });
        Assert.That(Page.Url, Does.Contain("env=offline"));

        // Once the owner has asked, the seat is theirs from the next turn: the notice says so, the recovery
        // button has done its job, and the bot keeps its mark until the boundary.
        await Page.GotoAsync($"{BaseUrl}/dev/board?scene=covered-reclaiming&env=offline");
        await Expect(Page.GetByTestId("match-board")).ToBeVisibleAsync(new() { Timeout = BootTimeout });

        notice = Page.GetByTestId("covered-seat-notice");
        await Expect(notice).ToHaveAttributeAsync("data-stage", "reclaiming");
        await Expect(notice).ToContainTextAsync("when the next turn starts");
        await Expect(Page.GetByTestId("reclaim-seat")).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("den-mine").GetByTestId("den-bot-mark")).ToBeVisibleAsync();
    }

    /// <summary>
    /// The Heist screen, over a board the game has finished on. Browser-only and server-free: the scene plays
    /// a real game out and stops at the phase, so what is asserted here is the screen the winner meets — the
    /// lineup, the padlock that is visible rather than missing, and the transfer priced on both sides before
    /// anything is committed. The transaction behind it is <c>HeistTests</c>'.
    /// </summary>
    [Test]
    public async Task TheHeistLineupShowsEveryPlayedCardAndPricesAPickOnBothSides()
    {
        await Page.SetViewportSizeAsync(1280, 720);
        await Page.GotoAsync($"{BaseUrl}/dev/board?scene=heist&env=offline");
        await Expect(Page.GetByTestId("match-board")).ToBeVisibleAsync(new() { Timeout = BootTimeout });

        ILocator screen = Page.GetByTestId("heist-screen");
        await Expect(screen).ToBeVisibleAsync();
        await Expect(screen).ToHaveAttributeAsync("data-stage", "Picking");
        await Expect(screen).ToHaveAttributeAsync("data-winner", "true");
        await Expect(Page.GetByTestId("heist-tier")).Not.ToBeEmptyAsync();

        // The lineup is every card the loser played, and the one they locked is IN it: the lineup should be the
        // game the winner just watched, so a card that vanished would read as a bug rather than as foresight.
        int drawn = int.Parse(await Page.GetByTestId("heist-loot").GetAttributeAsync("data-count") ?? "0");
        int pickable = await Page.GetByTestId("heist-pick").CountAsync();
        int inert    = await Page.GetByTestId("heist-loot-card").CountAsync();

        Assert.That(drawn, Is.GreaterThanOrEqualTo(3), "the scene poses a lineup worth drawing");
        Assert.That(pickable, Is.GreaterThan(0));
        Assert.That(pickable + inert, Is.EqualTo(drawn), "every played card is on the lineup, pickable or not");

        // Whose padlock a row carries is stated, and the wording is relative to the seat reading it: the
        // winner is looking at the loser's cards, so their own frozen copy is the one that says "yours".
        ILocator theirPadlock = Page.Locator("[data-testid='heist-loot-card'][data-locked-by='TheLoser']");
        await Expect(theirPadlock.First.GetByTestId("lock-mark")).ToBeVisibleAsync();
        await Expect(theirPadlock.First.GetByTestId("card-badge")).ToHaveTextAsync("LOCKED");

        ILocator ownPadlock = Page.Locator("[data-testid='heist-loot-card'][data-locked-by='TheWinner']");
        Assert.That(await ownPadlock.CountAsync(), Is.GreaterThan(0),
            "the scene freezes one of the loser's played cards on the winner's side too: a lock runs both ways, "
            + "so that card is off the menu rather than a pick that pays nothing");
        await Expect(ownPadlock.First.GetByTestId("card-badge")).ToHaveTextAsync("YOURS, FROZEN");
        await Expect(Page.Locator("[data-testid='heist-pick'][data-locked-by='TheWinner']")).ToHaveCountAsync(0);

        // The lineup is long enough to scroll, and its first card is still reachable. A flex row centred with
        // justify-content overflows to BOTH sides and the left half cannot be scrolled to — the first cards
        // were unclickable, by a pointer as much as by a fixture, and a five-card scene never showed it.
        ILocator scroller = Page.GetByTestId("heist-loot");
        Assert.That(await scroller.EvaluateAsync<bool>("el => el.scrollWidth > el.clientWidth + 1"), Is.True,
            "the scene has to pose a lineup that overflows, or this proves nothing");

        double firstTileOffset = await scroller.EvaluateAsync<double>(
            "el => el.querySelector('[data-card-id]').getBoundingClientRect().left - el.getBoundingClientRect().left");
        Assert.That(firstTileOffset, Is.GreaterThanOrEqualTo(-1.0),
            $"the first card starts {firstTileOffset:0.#} px outside the scroller, where nothing can reach it");

        // Nothing is priced until something is chosen, and the commit is a deliberate press.
        await Expect(Page.GetByTestId("heist-transfer")).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("heist-snatch")).ToBeDisabledAsync();

        await Page.GetByTestId("heist-pick").First.ClickAsync();

        // The transfer, spelled out on both sides, which is the point of the screen. The loser's side is read
        // off the rank the lineup row carries — public the moment the card was played.
        ILocator mine   = Page.Locator("[data-testid='heist-transfer'][data-side='mine']");
        ILocator theirs = Page.Locator("[data-testid='heist-transfer'][data-side='theirs']");
        await Expect(mine).ToBeVisibleAsync();
        await Expect(theirs).ToBeVisibleAsync();
        Assert.That(await theirs.GetAttributeAsync("data-from"), Is.EqualTo("3"));
        Assert.That(await theirs.GetAttributeAsync("data-to"), Is.EqualTo("2"));

        await Expect(Page.GetByTestId("heist-snatch")).ToBeEnabledAsync();

        // The winner's own clock, and the board draws none of its own: by now the engine's phase is Complete.
        await Expect(Page.GetByTestId("deadline-ring")).ToHaveCountAsync(1);

        // And a way off the screen while the pick is still owed. The board chrome's own Leave sits at z-index
        // 35 and every overlay in this game covers it, so a screen that offered none would hold both players
        // here until a clock let them go.
        await Expect(screen.GetByTestId("leave-match")).ToBeVisibleAsync();
        await Expect(screen.GetByTestId("leave-match")).ToContainTextAsync("take the default");
    }

    /// <summary>
    /// The same screen from the losing side, after the pick has landed. <b>The transfer read by both players
    /// is the point of the screen</b>, and this is the half that carries it: the loser is asked for nothing,
    /// can never select a tile, and would otherwise be left looking at a card showing the rank it was frozen
    /// at with no statement anywhere that it moved.
    /// </summary>
    [Test]
    public async Task TheLosersHeistScreenSaysWhatLeftTheirCollection()
    {
        await Page.SetViewportSizeAsync(1280, 720);
        await Page.GotoAsync($"{BaseUrl}/dev/board?scene=heist-taken&env=offline");
        await Expect(Page.GetByTestId("match-board")).ToBeVisibleAsync(new() { Timeout = BootTimeout });

        ILocator screen = Page.GetByTestId("heist-screen");
        await Expect(screen).ToHaveAttributeAsync("data-stage", "Done");
        await Expect(screen).ToHaveAttributeAsync("data-winner", "false");
        await Expect(screen).ToHaveAttributeAsync("data-picks-taken", "1");

        // Their own copy, in words and in numbers, off the rank the lineup row carries and the rule the table
        // applied — not off a live read, which would re-apply the rule to a rank that has already moved.
        ILocator mine = Page.Locator("[data-testid='heist-transfer'][data-side='mine']");
        await Expect(mine).ToBeVisibleAsync();
        await Expect(mine).ToContainTextAsync("Your copy");
        Assert.That(await mine.GetAttributeAsync("data-from"), Is.EqualTo("3"));
        Assert.That(await mine.GetAttributeAsync("data-to"), Is.EqualTo("2"));

        // And nothing about the winner's collection, which is not theirs to see.
        await Expect(Page.Locator("[data-testid='heist-transfer'][data-side='theirs']")).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("heist-yours-now")).ToHaveCountAsync(0);

        // The card that went, marked as gone, and the note that says a clock rather than a player chose it.
        await Expect(Page.Locator("[data-testid='heist-loot-card'][data-taken='true']").First.GetByTestId("card-badge"))
            .ToHaveTextAsync("TAKEN");
        await Expect(Page.GetByTestId("heist-auto-defaulted")).ToBeVisibleAsync();

        // Every relative word is framed for the side reading it: these are the loser's own cards, so their
        // padlock is "yours" and the winner's is "theirs".
        await Expect(Page.Locator("[data-testid='heist-loot-card'][data-locked-by='TheLoser']").First.GetByTestId("card-badge"))
            .ToHaveTextAsync("YOURS, LOCKED");
        await Expect(Page.Locator("[data-testid='heist-loot-card'][data-locked-by='TheWinner']").First.GetByTestId("card-badge"))
            .ToHaveTextAsync("THEIRS, FROZEN");

        // Nothing is asked of them, and there is a way out.
        await Expect(Page.GetByTestId("heist-snatch")).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("play-again")).ToBeVisibleAsync();
    }
}
