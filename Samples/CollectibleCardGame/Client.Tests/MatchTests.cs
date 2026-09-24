using Microsoft.Playwright;
using System.Text.RegularExpressions;

namespace Game.Client.Tests;

/// <summary>
/// The live match's proof, played through the real client against a real server: a person starts a practice
/// match from Home and plays a whole game against a server-side bot over the network.
/// <para>
/// Non-parallelizable, and not only for tidiness: these fixtures drive one server's match actors, and two of
/// them racing is not isolation a browser context can provide.
/// </para>
/// </summary>
[TestFixture]
[NonParallelizable]
public class MatchTests : MatchTestBase
{
    [Test]
    public async Task TouchDragCanCancelPlayAndAttack()
    {
        await Page.SetViewportSizeAsync(932, 430);
        await StartPracticeFromHomeAsync();
        await ResolveMulliganAsync();
        await WaitForOwnTurnAsync(MatchTimeout);
        ILocator card = await FirstPlayableCardAsync();
        string instance = (await card.GetAttributeAsync("data-instance"))!;
        card = Page.Locator($".handcard[data-instance='{instance}']");
        ICDPSession touch = await Page.Context.NewCDPSessionAsync(Page);

        async Task Send(string type, double x = 0, double y = 0) => await touch.SendAsync(
            "Input.dispatchTouchEvent", new Dictionary<string, object>
            {
                ["type"] = type,
                ["touchPoints"] = type == "touchEnd" ? Array.Empty<object>() : new object[] { new { x, y, id = 1 } },
            });
        async Task Begin()
        {
            await Expect(Page.Locator(".board-frame")).Not.ToHaveClassAsync(new Regex("is-touch-dragging"));
            await card.EvaluateAsync("async card => await Promise.all(card.getAnimations().map(a => a.finished))");
            // The fan overlaps: find a point on this card's actual exposed hit surface.
            double[] point = await card.EvaluateAsync<double[]>("""
                card => {
                    const r = card.getBoundingClientRect();
                    for (let y = Math.max(1, r.top + 5); y < Math.min(innerHeight - 1, r.bottom); y += 4)
                        for (let x = Math.max(1, r.left + 5); x < Math.min(innerWidth - 1, r.right); x += 4)
                            if (document.elementFromPoint(x, y)?.closest('.handcard') === card) return [x, y];
                    throw Error('No exposed card surface');
                }
                """);
            await Send("touchStart", point[0], point[1]);
            await Send("touchMove", 450, 220);
            await Expect(card).ToHaveAttributeAsync("data-selected", "true");
            await Expect(Page.Locator(".handcard-peek:visible, .critter-peek:visible")).ToHaveCountAsync(0);
        }

        // A mobile browser may blur/leave the card before its compatibility click. It must still only inspect.
        await card.EvaluateAsync("""
            card => {
                card.dispatchEvent(new PointerEvent('pointerdown', { bubbles: true, pointerType: 'touch' }));
                card.dispatchEvent(new FocusEvent('blur', { bubbles: true }));
                card.dispatchEvent(new MouseEvent('mouseleave', { bubbles: true }));
                card.click();
                card.click();
            }
            """);
        await Expect(card).ToHaveAttributeAsync("data-playable", "true");
        await Expect(card).ToHaveAttributeAsync("data-selected", "false");
        await Begin();
        await Send("touchMove", 1, 1);
        await Send("touchEnd");
        await Expect(card).ToHaveAttributeAsync("data-selected", "false");
        await Expect(card).ToHaveAttributeAsync("data-playable", "true");
        await Begin();
        ILocator targets = Page.Locator(".critter[data-targetable='true'], .den[data-targetable='true']");
        if (await targets.CountAsync() > 0)
        {
            LocatorBoundingBoxResult box = (await targets.First.BoundingBoxAsync())!;
            await Send("touchMove", box.X + box.Width / 2, box.Y + box.Height / 2);
            await Expect(Page.GetByTestId("targeting-arrow")).ToHaveAttributeAsync("data-snapped", "true");
        }
        else
            await Send("touchMove", 450, 220);
        await Send("touchEnd");
        await Expect(card).ToHaveCountAsync(0, new() { Timeout = MatchTimeout });
        // Build an ordinary board through the existing driver, then attack using only touch input.
        for (int turn = 0; turn < 8 && await SelectableCritters.CountAsync() == 0; turn++)
        {
            await PlayOneTurnAsync();
            await WaitForOwnTurnAsync(MatchTimeout);
        }
        await Expect(SelectableCritters.First).ToBeVisibleAsync();
        string attackerId = (await SelectableCritters.First.GetAttributeAsync("data-instance"))!;
        ILocator attacker = Page.Locator($"[data-testid='board-critter'][data-instance='{attackerId}']");
        LocatorBoundingBoxResult attackerBox = (await attacker.BoundingBoxAsync())!;
        await Send("touchStart", attackerBox.X + attackerBox.Width / 2, attackerBox.Y + attackerBox.Height / 2);
        await Send("touchMove", 450, 220);
        await Expect(attacker).ToHaveAttributeAsync("data-selected", "true");
        LocatorBoundingBoxResult targetBox = (await targets.First.BoundingBoxAsync())!;
        await Send("touchMove", targetBox.X + targetBox.Width / 2, targetBox.Y + targetBox.Height / 2);
        await Expect(Page.GetByTestId("targeting-arrow")).ToHaveAttributeAsync("data-snapped", "true");
        await Expect(targets.First.Locator(".delta-preview")).ToBeVisibleAsync();
        if (await targets.First.EvaluateAsync<bool>("target => target.classList.contains('critter')"))
            await Expect(Page.GetByTestId("counter-damage-preview")).ToBeVisibleAsync();
        await Send("touchEnd");
        await Expect(Page.Locator($"[data-testid='board-critter'][data-instance='{attackerId}'][data-selectable='true']"))
            .ToHaveCountAsync(0, new() { Timeout = MatchTimeout });
        Assert.That(ConsoleFailures(), Is.EqualTo("(none)"));
        await Page.GetByRole(AriaRole.Button, new() { Name = "Leave", Exact = true }).ClickAsync();
    }

    /// <summary>
    /// The end-to-end proof. Home → Practice → mulligan → play the whole game to a result, driving every move
    /// off the rendered highlights rather than off any knowledge of the rules.
    /// <para>
    /// It is also where the board's legal set is checked for staleness, because it is the fixture that plays
    /// enough positions to meet an answered peek: every action <c>PlayOneTurnAsync</c>
    /// takes is followed by <c>AssertAffordableCardsAreOfferedAsync</c>, and the number of cards that check
    /// actually asked about is reported at the end.
    /// </para>
    /// </summary>
    [Test]
    public async Task PlayingAFullGameAgainstABot()
    {
        await StartPracticeFromHomeAsync();

        // A board appears, with everything a table is: two Dens, the Weather in force, the stakes agreed
        // before the first card was dealt.
        await Expect(Page.GetByTestId("weather-banner")).ToBeVisibleAsync(new() { Timeout = MatchTimeout });
        await Expect(Page.GetByTestId("stakes-chip")).ToHaveAttributeAsync("data-tier", "Practice");
        await Expect(DenHp(0)).ToBeVisibleAsync();
        await Expect(DenHp(1)).ToBeVisibleAsync();

        // The opening hand arrived — as private state, on the subscribe, with no request of its own.
        await Expect(HandCards.First).ToBeVisibleAsync(new() { Timeout = MatchTimeout });
        int dealtHand = await HandCards.CountAsync();
        Assert.That(dealtHand, Is.GreaterThan(0), "the seat's own hand arrives with the subscribe");

        // The mulligan resolves, and the hand comes back as a correction rather than as a request answered.
        await Expect(Page.GetByTestId("mulligan-overlay")).ToBeVisibleAsync(new() { Timeout = MatchTimeout });
        await ResolveMulliganAsync();
        await Expect(HandCards.First).ToBeVisibleAsync(new() { Timeout = MatchTimeout });

        // Play the game out. Every turn is: wait for the table to come to this seat, take what the highlights
        // offer, end the turn.
        int turnsPlayed = 0;
        for (int turn = 0; turn < 60 && !await IsGameOverAsync(); turn++)
        {
            if (!await WaitForOwnTurnAsync(GameTimeout / 6))
                break;

            if (await IsGameOverAsync())
                break;

            await PlayOneTurnAsync();
            turnsPlayed++;
        }

        // Only a board that is neither finished nor mid-beat is worth reporting: that is the shape a genuine stall
        // takes, and a game that ended is the loop's ordinary exit.
        if (!await IsGameOverAsync() && await TurnIndicator.GetAttributeAsync("data-state") != "over")
        {
            TestContext.Out.WriteLine($"stalled after {turnsPlayed} turns");
            TestContext.Out.WriteLine($"board: turn={await TurnIndicator.GetAttributeAsync("data-turn")} state={await TurnIndicator.GetAttributeAsync("data-state")} endTurn={(await EndTurn.CountAsync() > 0 && await EndTurn.IsEnabledAsync())} hand={await HandCards.CountAsync()} playable={await PlayableHandCards.CountAsync()} selectable={await SelectableCritters.CountAsync()}");
            TestContext.Out.WriteLine($"peek={await Page.GetByTestId("peek-overlay").IsVisibleAsync()} mulligan={await Page.GetByTestId("mulligan-overlay").IsVisibleAsync()}");
            TestContext.Out.WriteLine($"console failures:\n{ConsoleFailures()}");
        }

        // A Den reached zero, and the result panel appeared only after the finish had been shown.
        await Expect(MatchResult).ToBeVisibleAsync(new() { Timeout = GameTimeout });
        await Expect(Page.GetByTestId("match-result-headline")).Not.ToBeEmptyAsync();
        await Expect(Page.GetByTestId("match-result-stakes")).ToContainTextAsync("nothing was at stake");

        Assert.That(turnsPlayed, Is.GreaterThan(0), "the client took at least one turn of its own");

        // How many affordable cards the staleness check actually asked about. It asserts nothing when the hand
        // holds nothing the mana can pay for, so the count is what separates a check that ran from one that
        // had nothing to say.
        TestContext.Out.WriteLine($"affordable cards checked across {turnsPlayed} turns: {AffordableCardsChecked}");

        int denZero = int.Parse((await DenHp(0).InnerTextAsync()).Trim().TrimStart('❤', '️').Trim());
        int denOne  = int.Parse((await DenHp(1).InnerTextAsync()).Trim().TrimStart('❤', '️').Trim());
        Assert.That(Math.Min(denZero, denOne), Is.LessThanOrEqualTo(0), "the game ended on a Den at zero");

        // Play again after a practice match is another practice match, not a ranked search.
        await Expect(Page.GetByTestId("play-again")).ToBeEnabledAsync(new() { Timeout = MatchTimeout });
        await Page.GetByTestId("play-again").ClickAsync();
        await Expect(MatchResult).ToHaveCountAsync(0, new() { Timeout = MatchTimeout });
        await Expect(Page.GetByTestId("searching-dialog")).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("stakes-chip")).ToHaveAttributeAsync("data-tier", "Practice");
    }

    /// <summary>
    /// The listener-binding trap's only end-to-end cover. Pressing Practice during a live session is the
    /// mid-session attach path: the client buffers the new channel while it settles, and the activation
    /// flushes that buffer before any activation hook runs. If the listeners moved to an activation hook, the
    /// buffered hand correction would be dispatched to nobody and this would fail with an empty hand.
    /// </summary>
    [Test]
    public async Task MidSessionAttachDeliversTheHand()
    {
        await StartPracticeFromHomeAsync();

        await Expect(HandCards.First).ToBeVisibleAsync(new() { Timeout = MatchTimeout });
        Assert.That(await HandCards.CountAsync(), Is.GreaterThan(0), "the hand is populated on the first board frame");

        // And the cards are real cards rather than placeholders: the config resolved and the payload carried
        // catalogue identities the client could look up.
        await Expect(HandCards.First).ToHaveAttributeAsync("data-card-id", new Regex("^[A-Za-z]+$"));
        await Expect(Page.GetByTestId("hand-card-cost").First).Not.ToBeEmptyAsync();
    }

    /// <summary>
    /// The mulligan's other half: putting a card back. It is played the way it reads — tap the card in the
    /// hand, press "Replace 1" — which is only possible because the overlay leaves the fan uncovered and
    /// live. Every fixture before this one answered the mulligan with "Keep this hand", so the replace path
    /// had no end-to-end cover at all and the overlay could and did sit on top of the cards it asks for.
    /// </summary>
    [Test]
    public async Task MarkingACardInTheMulliganReplacesIt()
    {
        await StartPracticeFromHomeAsync();
        await Expect(Page.GetByTestId("mulligan-overlay")).ToBeVisibleAsync(new() { Timeout = MatchTimeout });
        await Expect(HandCards.First).ToBeVisibleAsync(new() { Timeout = MatchTimeout });

        // The hand is in its mulligan state: the cards it may put back are bright and take input. Nothing is
        // playable during the mulligan, so a fan drawn by legality alone is the dimmed, inert thing an
        // unplayable hand is — and then the overlay is asking for a tap the board is not offering.
        await Expect(MarkableHandCards.First).ToBeVisibleAsync(new() { Timeout = MatchTimeout });
        await Expect(PlayableHandCards).ToHaveCountAsync(0);

        // And what it offers is what the table would accept — which, at the deal, is the whole hand. The
        // second seat's compensation card is granted when the mulligan resolves rather than dealt into the
        // opening hand, so it cannot be in the fan the mulligan is asking about, on either seat's
        // board. Both halves are deal-independent, which the guarded version of this assertion was not.
        await Expect(Page.Locator("[data-testid='hand-card'][data-card-id='TheAcorn']")).ToHaveCountAsync(0);
        await Expect(MarkableHandCards).ToHaveCountAsync(await HandCards.CountAsync());

        string[] before = await HandInstancesAsync();
        Assert.That(before.Length, Is.GreaterThan(1), "an opening hand is dealt before the mulligan is asked");

        string replaced = await ReplaceOneCardInTheMulliganAsync();

        // Which card arrives, and where it sits in the fan, cannot be asserted: the reshuffle may redraw the
        // card just returned (pinned by MulliganTests.ARedrawnCardMayBeOneJustReturned), and the fan keeps a
        // card's slot across a hand change. What a browser can catch is a hand that got smaller, so the size is
        // sampled until the hand changes.
        int      smallest = before.Length;
        string[] after    = await WaitForTheHandToChangeAsync(before, hand => smallest = Math.Min(smallest, hand.Length));

        Assert.That(smallest, Is.GreaterThanOrEqualTo(before.Length),
            $"the fan dipped to {smallest} cards from {before.Length}: cards went back and fewer came in");

        // The hand did come back different. This is the one assertion that fails when the resolution does
        // nothing at all — and the one the reshuffle can satisfy the long way round, since a redrawn card that is the
        // one just returned leaves the fan identical until the turn's own draw lands on it.
        Assert.That(after, Is.Not.EqualTo(before),
            $"the hand did not change across a mulligan that put {replaced} back");

        // What the resolution guarantees on every deal is arithmetic, not identity: one card out and one card
        // back in, so the fan never shrinks. **Two other cards can arrive on top of that and neither is the
        // mulligan's**: the second seat's compensation card is granted when the mulligan resolves rather than
        // dealt into the opening fan, and the first turn's own draw may already have landed. So the
        // hand is its old size or up to two larger — never smaller, which is what a redraw that did not
        // happen looks like.
        Assert.That(after.Length, Is.InRange(before.Length, before.Length + 2),
            $"one card out and one back in, plus at most The Acorn and the turn's own draw: "
            + $"{before.Length} → {after.Length}");

        // And at most three instances are new, by the same arithmetic: the redraw when it is not the card
        // just returned (it may be), The Acorn, and the turn's draw. A resolution that redrew more than
        // it was asked for shows up here whatever the shuffle did.
        HashSet<string> was     = new HashSet<string>(before);
        int             arrived = after.Count(instance => !was.Contains(instance));
        Assert.That(arrived, Is.InRange(0, 3),
            $"a one-card mulligan, The Acorn and one turn draw can bring in at most three cards, not {arrived}");

        // The mark itself is gone from the fan: it was answered rather than left standing.
        await Expect(Page.Locator("[data-testid='hand-card'][data-marked='true']"))
            .ToHaveCountAsync(0, new() { Timeout = MatchTimeout });

        // And the board went on to be a board: the mulligan is answered, the game is playing.
        await Expect(TurnIndicator).Not.ToHaveAttributeAsync("data-state", "mulligan", new() { Timeout = MatchTimeout });
    }

    /// <summary>
    /// Once this seat has answered, nothing in the mulligan takes input any more — even though the scrim is
    /// still up, because the table has not left the phase.
    /// <para>
    /// <b>How the window is made observable.</b> Under the E2E profile the bot answers in 0 ms, so "this seat
    /// has answered and the table has not" would normally last less than a frame. A long <c>?beat=</c> opens
    /// it: the resolution's own events — two mulligan-resolved, the compensation card, the turn start and its
    /// draw — are queued as beats, and the <em>presented</em> phase stays <c>Mulligan</c> until they run,
    /// while the authoritative one has already moved. That is precisely the disagreement this asserts, and it
    /// is the same mechanism a slow frame or a human opponent produces for real.
    /// </para>
    /// </summary>
    [Test]
    public async Task AnAnsweredMulliganStopsTakingInputWhileTheTableIsStillInIt()
    {
        await StartPracticeFromHomeAsync("beat=3000");
        await Expect(Page.GetByTestId("mulligan-overlay")).ToBeVisibleAsync(new() { Timeout = MatchTimeout });

        ILocator confirm = Page.GetByTestId("mulligan-confirm");
        await Expect(confirm).ToBeEnabledAsync(new() { Timeout = MatchTimeout });
        await Expect(MarkableHandCards.First).ToBeVisibleAsync(new() { Timeout = MatchTimeout });

        await confirm.ClickAsync();

        // The scrim is still there, because the table is still in the mulligan on the presented clock.
        await Expect(Page.GetByTestId("mulligan-overlay")).ToBeVisibleAsync(new() { Timeout = MatchTimeout });
        await Expect(Page.GetByTestId("mulligan-waiting")).ToBeVisibleAsync(new() { Timeout = MatchTimeout });

        // And nothing on it acts: no confirm at all rather than an enabled control that does nothing, and a
        // hand that is dim and offers no mark rather than a lit one that refuses every tap.
        await Expect(confirm).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("mulligan-selected-count")).ToHaveCountAsync(0);
        await Expect(MarkableHandCards).ToHaveCountAsync(0);
        await Expect(Page.Locator("[data-testid='hand-card'] .card-face.is-dimmed").First)
            .ToBeVisibleAsync(new() { Timeout = MatchTimeout });

        // The board does open, once the queued beats have run.
        await Expect(Page.GetByTestId("mulligan-overlay")).Not.ToBeVisibleAsync(new() { Timeout = GameTimeout });
    }

    [Test]
    public async Task ATimedOutMulliganClearsItsLocalMarks()
    {
        await StartPracticeFromHomeAsync();
        await Expect(Page.GetByTestId("mulligan-overlay")).ToBeVisibleAsync(new() { Timeout = MatchTimeout });
        await Expect(MarkableHandCards.First).ToBeVisibleAsync(new() { Timeout = MatchTimeout });

        await MarkableHandCards.First.ClickAsync();
        await Expect(Page.Locator("[data-testid='hand-card'][data-marked='true']"))
            .ToHaveCountAsync(1, new() { Timeout = MatchTimeout });

        await Expect(Page.GetByTestId("mulligan-overlay"))
            .Not.ToBeVisibleAsync(new() { Timeout = GameTimeout });
        await Expect(Page.Locator("[data-testid='hand-card'][data-marked='true']"))
            .ToHaveCountAsync(0, new() { Timeout = MatchTimeout });
    }

    /// <summary>
    /// The hand, once it differs from the one passed in. The mulligan's replacements reach the client as
    /// addressed timeline operations after the resolving action, so the first read after a confirm is
    /// routinely the hand from before it.
    /// </summary>
    async Task<string[]> WaitForTheHandToChangeAsync(string[] before, Action<string[]>? observe = null)
    {
        string[] hand = await HandInstancesAsync();
        observe?.Invoke(hand);

        for (int waited = 0; waited < MatchTimeout && SameOrder(hand, before); waited += 200)
        {
            await Page.WaitForTimeoutAsync(200);
            hand = await HandInstancesAsync();
            observe?.Invoke(hand);
        }

        return hand;
    }

    static bool SameOrder(string[] left, string[] right)
    {
        if (left.Length != right.Length)
            return false;

        for (int ndx = 0; ndx < left.Length; ndx++)
        {
            if (left[ndx] != right[ndx])
                return false;
        }

        return true;
    }

    /// <summary>
    /// Every turn's opening draw is a hand the server rewrote without this seat playing anything, delivered
    /// as a correction. This observes the path rather than inferring it: the hand count goes up across a turn
    /// boundary in which the client played nothing.
    /// </summary>
    [Test]
    public async Task TheStartOfTurnDrawArrivesAsACorrection()
    {
        await StartPracticeFromHomeAsync();
        await Expect(Page.GetByTestId("mulligan-overlay")).ToBeVisibleAsync(new() { Timeout = MatchTimeout });
        await ResolveMulliganAsync();

        await WaitForOwnTurnAsync(GameTimeout / 6);

        // Sampled from a board that has caught up. End turn goes live the moment the model says the seat is
        // on turn, which is ahead of the hand correction being applied — so a count taken straight away is a
        // count from before this turn's own draw, and the next turn's draw then makes the hand grow by two.
        await WaitUntilCaughtUpAsync();

        int before = await HandCards.CountAsync();

        // End the turn without playing anything at all, so the only thing that can change the hand is the
        // draw the server does at the start of this seat's next turn.
        await Expect(EndTurn).ToBeEnabledAsync(new() { Timeout = MatchTimeout });
        await EndTurn.ClickAsync();

        // An assertion, not a skip. Two turns from the opening with this seat playing nothing cannot end a
        // game: the only way out that early is Tuckered Out, which is a deck away. An Assert.Inconclusive here
        // was worse than useless — NUnit counts an inconclusive result in no bucket at all, not even the
        // total, so a run in which this fired reported "8 of 9" and read as a fixture-level abort.
        Assert.That(await WaitForOwnTurnAsync(GameTimeout / 6), Is.True, "the seat never got its next turn");
        Assert.That(await IsGameOverAsync(), Is.False, "a game ended two turns from the opening with nothing played");

        await Expect(HandCards).ToHaveCountAsync(before + 1, new() { Timeout = MatchTimeout });
    }

    /// <summary>
    /// A double press must cost exactly one turn.
    /// <para>
    /// The input locks for the length of the round trip, so the second press has nothing to send; were it
    /// sent, the table would judge it against a turn that has already passed and refuse it. Counting the turns
    /// is what shows the press cost one turn each way, where "no error appeared" would pass whatever happened.
    /// </para>
    /// </summary>
    [Test]
    public async Task ADoublePressCostsExactlyOneTurn()
    {
        await StartPracticeFromHomeAsync();
        await Expect(Page.GetByTestId("mulligan-overlay")).ToBeVisibleAsync(new() { Timeout = MatchTimeout });
        await ResolveMulliganAsync();

        Assert.That(await WaitForOwnTurnAsync(GameTimeout / 6), Is.True, "the seat never got its first turn");

        // End turn is always live on this seat's turn, which is what makes this reachable on every deal.
        await Expect(EndTurn).ToBeEnabledAsync(new() { Timeout = MatchTimeout });

        int turnBefore = await BoardTurnAsync();

        await EndTurn.ClickAsync();
        await EndTurn.ClickAsync(new LocatorClickOptions { Force = true, Timeout = 2000 })
            .ContinueWith(_ => Task.CompletedTask).Unwrap();

        Assert.That(await WaitForOwnTurnAsync(GameTimeout / 6), Is.True, "the board never came back to a state that takes input");

        // Ours, theirs, ours: exactly two turns on.
        int turnAfter = await BoardTurnAsync();
        Assert.That(turnAfter, Is.EqualTo(turnBefore + 2),
            $"one press should cost one turn each way; the board went from turn {turnBefore} to {turnAfter}");

        // And nothing was reported as a bug signal. Stale, NotYourTurn and AlreadyDone all mean something the
        // server did first; only Illegal means the client offered a move its own legality check should have
        // prevented.
        await Expect(Page.Locator("[data-testid='refusal-notice'][data-reason='Illegal']")).Not.ToBeVisibleAsync();
    }

    /// <summary>
    /// A refused card falls back into the hand, says why, and leaves a board that still takes input.
    /// <para>
    /// The refusal is produced for real, with <c>?stale=1</c>, which sends an End turn ahead of the intent, so
    /// the play reaches the table after the turn has passed — the race a turn deadline lapsing under a press
    /// produces — and is refused as not this seat's turn. Nothing a browser can do on its own reaches this: the
    /// input locks, the control renders disabled, and the client re-checks before sending, all of which is
    /// correct and all of which means the refusal path has no other route to a test.
    /// </para>
    /// </summary>
    [Test]
    public async Task ARefusedCardFallsBackIntoTheHandAndSaysWhy()
    {
        await StartPracticeFromHomeAsync();
        await Expect(Page.GetByTestId("mulligan-overlay")).ToBeVisibleAsync(new() { Timeout = MatchTimeout });
        await ResolveMulliganAsync();

        Assert.That(await WaitForOwnTurnAsync(GameTimeout / 6), Is.True, "the seat never got its first turn");

        // Get to a state with something playable in it first, with honest intents. Turning the flag on before
        // this would refuse the End turn presses the helper uses to get here, so the mana would never grow.
        string instance = await (await FirstPlayableCardAsync()).GetAttributeAsync("data-instance") ?? "";

        // Now the flag. The same table comes back with the hand it had, and the next intent it sends will
        // arrive after an End turn.
        await ReloadBoardWithAsync("stale=1");
        Assert.That(await WaitForOwnTurnAsync(GameTimeout / 6), Is.True, "the seat lost its turn across the reload");

        ILocator card = Page.Locator($"[data-testid='hand-card'][data-instance='{instance}']");
        await Expect(card).ToBeVisibleAsync(new() { Timeout = MatchTimeout });
        await card.ClickAsync();

        // A card that asks for a target is only *lifted* by that click: the intent goes when the target is
        // picked. Seven catalogue cards ask, one of them is a cost-1 Wanderer that can always aim at its own
        // Den, and which card is first-playable comes out of the deal — so on the deals that put one there,
        // a single click sent nothing and this test timed out waiting for a refusal that was never asked for.
        // Completing the play is what makes the refusal arrive on every deal rather than on most of them.
        //
        // Waited for by COUNT rather than by visibility: a not-your-turn refusal renders no words on purpose
        // (see below), so its notice is an empty element that Playwright reports as not visible.
        ILocator refusalOrTarget = Page.Locator(
            "[data-testid='refusal-notice'], [data-testid^='den-'][data-targetable='true'], [data-testid='board-critter'][data-targetable='true']");
        await Expect(refusalOrTarget.First).ToHaveCountAsync(1, new() { Timeout = MatchTimeout });

        if (await TargetableDens.CountAsync() > 0)
            await TargetableDens.First.ClickAsync();
        else if (await TargetableCritters.CountAsync() > 0)
            await TargetableCritters.First.ClickAsync();

        // Refused because the turn had passed. NotYourTurn is a "the server got there first" reason, and the
        // board deliberately says nothing for it — the card goes down, the input unlocks, and the player is not
        // told off for losing a race they did not know they were in. So the notice carries the reason for the
        // board's own bookkeeping and renders no words: what is asserted is the reason, and the silence.
        ILocator notice = Page.GetByTestId("refusal-notice");
        await Expect(notice).ToHaveCountAsync(1, new() { Timeout = MatchTimeout });
        await Expect(notice).ToHaveAttributeAsync("data-reason", "NotYourTurn", new() { Timeout = MatchTimeout });
        await Expect(notice).ToHaveTextAsync("", new() { Timeout = MatchTimeout });

        // And not Illegal, which is the board's bug signal: a lost race is an ordinary outcome, and reporting
        // it as a rules violation would train a player to distrust a board that is working.
        await Expect(Page.Locator("[data-testid='refusal-notice'][data-reason='Illegal']")).Not.ToBeVisibleAsync();

        // The card is back in the hand and not lifted, and the board takes input again once the turn comes
        // back round — the lock was released rather than held.
        await Expect(Page.Locator($"[data-testid='hand-card'][data-instance='{instance}']")).ToBeVisibleAsync();
        await Expect(Page.Locator($"[data-testid='hand-card'][data-instance='{instance}'][data-selected='true']"))
            .Not.ToBeVisibleAsync(new() { Timeout = MatchTimeout });
        await Expect(EndTurn).ToBeEnabledAsync(new() { Timeout = MatchTimeout });
    }

    /// <summary>
    /// One entry per player. Pressing Practice twice quickly must seat the account <b>once</b>.
    /// <para>
    /// Asserted on the thing the account owns — the game it ends up having played — rather than on a DOM
    /// count. The board renders exactly one frame whenever the client is attached to a table, whichever table
    /// that is and however many were minted, so counting boards is a tautology: a genuine double-seating,
    /// where the second pointer overwrites the first and one table is orphaned, leaves it completely
    /// untouched. What a second table cannot survive is being played to the end: the account records the
    /// result of the table it is actually at, and if that is a different table from the one the first press
    /// created then the match this test watched is not the match that was recorded.
    /// </para>
    /// <para>
    /// Home now also holds an in-flight lock across a press, so the second of two quick presses is swallowed
    /// on the client and never reaches the server — which is what this fixture exercises. The server's own
    /// refusal of a second entry, and what it costs, is
    /// <c>HomePageTests.ARefusedSecondEntry_KeepsTheSessionAndTheRememberedDeck</c>: it comes back to Home
    /// past the lock and presses deliberately.
    /// </para>
    /// </summary>
    [Test]
    public async Task PressingPracticeTwiceSeatsTheAccountOnce()
    {
        await Page.GotoAsync(BaseUrl);
        await Expect(Page.GetByTestId("display-name")).ToBeVisibleAsync(new() { Timeout = BootTimeout });

        int playedBefore = await PracticeGamesPlayedAsync();

        ILocator practice = Page.GetByTestId("queue-practice");
        await Expect(practice).ToBeVisibleAsync(new() { Timeout = BootTimeout });

        // Two presses, as fast as the browser will deliver them.
        await practice.ClickAsync();
        await practice.ClickAsync(new LocatorClickOptions { Force = true, Timeout = 2000 }).ContinueWith(_ => Task.CompletedTask).Unwrap();

        await Expect(Page.GetByTestId("match-board")).ToBeVisibleAsync(new() { Timeout = MatchTimeout });

        // Off the mulligan first: it is a modal overlay, so Leave is behind it until it is answered.
        await Expect(Page.GetByTestId("mulligan-overlay")).ToBeVisibleAsync(new() { Timeout = MatchTimeout });
        await ResolveMulliganAsync();

        // Play the table out. Leaving does it without playing every turn: a table that loses its humans is
        // played out rather than torn down, which is the design pillar the economy rests on.
        await Page.GetByTestId("leave-match").ClickAsync();
        await Expect(Page.GetByTestId("display-name")).ToBeVisibleAsync(new() { Timeout = GameTimeout });

        // Exactly one more practice game on the record. Two would mean two tables were minted and both
        // delivered their result; the count is the account's own, and it is the only place a second seating
        // is visible at all.
        await Expect(Page.GetByTestId("practice-played"))
            .ToHaveTextAsync((playedBefore + 1).ToString(), new() { Timeout = GameTimeout });
    }

    /// <summary> Practice games on the account's record right now. </summary>
    async Task<int> PracticeGamesPlayedAsync()
    {
        ILocator played = Page.GetByTestId("practice-played");
        await Expect(played).ToBeVisibleAsync(new() { Timeout = BootTimeout });

        return int.TryParse((await played.TextContentAsync() ?? "0").Trim(), out int games) ? games : 0;
    }

    /// <summary>
    /// Leaving is a disconnect that skips grace, and the table plays itself out rather than being torn down.
    /// The design pillar, observed: leaving costs exactly what staying would.
    /// </summary>
    [Test]
    public async Task LeavingPlaysTheMatchOut()
    {
        await StartPracticeFromHomeAsync();
        await Expect(Page.GetByTestId("mulligan-overlay")).ToBeVisibleAsync(new() { Timeout = MatchTimeout });
        await ResolveMulliganAsync();
        await WaitForOwnTurnAsync(GameTimeout / 6);

        // Play a card first, so the played-card list this seat leaves behind is not empty — what is exposed is
        // what a full game exposes, not what a rage-quit chose to show.
        await TakeOneActionAsync();

        // Wait for the board to have caught up rather than for half a second: what has to be true before
        // leaving is that the play's own beats have run and the table has the card on this seat's played
        // list, and `data-trailing` is that condition stated.
        await WaitUntilCaughtUpAsync();

        await Page.GetByTestId("leave-match").ClickAsync();

        // Back on Home, and the account is released — which only happens once the table has played the game
        // out to a real result and both accounts have folded it in.
        await Expect(Page.GetByTestId("display-name")).ToBeVisibleAsync(new() { Timeout = MatchTimeout });
        await Expect(Page.GetByTestId("queue-practice")).ToBeEnabledAsync(new() { Timeout = GameTimeout });

        // The record moved, which is the observable proof that a real result was delivered rather than the
        // table being abandoned.
        await Expect(Page.GetByTestId("record-summary")).ToBeVisibleAsync();
    }

    /// <summary>
    /// A reconnect delivers a whole state with no prior frame, so the board is rendered directly with no
    /// beats replayed — and the seat's hand arrives with the fresh subscribe. This drives it the way a player
    /// would: a full page reload, which tears the session down and starts a new one.
    /// </summary>
    [Test]
    public async Task ReconnectMidMatchRestoresTheBoard()
    {
        await StartPracticeFromHomeAsync();
        await Expect(Page.GetByTestId("mulligan-overlay")).ToBeVisibleAsync(new() { Timeout = MatchTimeout });
        await ResolveMulliganAsync();
        await WaitForOwnTurnAsync(GameTimeout / 6);

        await Expect(HandCards.First).ToBeVisibleAsync(new() { Timeout = MatchTimeout });
        int turnBefore = await BoardTurnAsync();

        // The reload is a session teardown: the SDK's resume fails, the shell starts a new session, and the
        // player actor re-associates from the account's own match pointer. None of that is client code the
        // test drives.
        await Page.ReloadAsync();

        await Expect(Page.GetByTestId("match-board")).ToBeVisibleAsync(new() { Timeout = MatchTimeout });
        await Expect(HandCards.First).ToBeVisibleAsync(new() { Timeout = MatchTimeout });

        // The hand came back with the subscribe. Not necessarily the same cards: a reconnect takes a couple of
        // seconds and the opponent plays through them, so the seat may well have drawn since — which is the
        // correction path doing its job rather than a failure.
        Assert.That(await HandCards.CountAsync(), Is.GreaterThan(0), "the hand came back with the subscribe");
        await Expect(TurnIndicator).ToBeVisibleAsync();

        // And the game continues from where it was rather than from the deal.
        Assert.That(await BoardTurnAsync(), Is.GreaterThanOrEqualTo(turnBefore), "the match resumed rather than restarting from the deal");
        Assert.That(await Page.GetByTestId("event-line").CountAsync(), Is.GreaterThan(0), "the history came back with the board");
    }

    /// <summary>
    /// An attack picks its target by hand, exactly the way a targeted trick does: tap the attacker and its
    /// legal targets light up, tap one and the attack goes out. Tap the attacker again instead and the
    /// selection drops, because a selection is a question rather than a commitment.
    /// <para>
    /// What is asserted is the gesture and its consequence — a target offered, then a board that moved —
    /// never <em>which</em> target, because choosing is the player's job and this fixture does the choosing.
    /// </para>
    /// </summary>
    [Test]
    public async Task AttackingPicksItsTargetByHand()
    {
        await StartPracticeFromHomeAsync();
        await Expect(Page.GetByTestId("mulligan-overlay")).ToBeVisibleAsync(new() { Timeout = MatchTimeout });
        await ResolveMulliganAsync();

        string   instance = await (await FirstReadyAttackerAsync()).GetAttributeAsync("data-instance") ?? "";
        ILocator attacker = Page.Locator($"[data-testid='board-critter'][data-instance='{instance}']");

        ILocator banner  = Page.GetByTestId("targeting-banner");
        ILocator targets = Page.Locator(
            "[data-testid='board-critter'][data-targetable='true'], [data-testid^='den-'][data-targetable='true']");

        // Local input must redraw independently of the once-per-second countdown. Repeated selections
        // prevent a click that happens to coincide with that tick from hiding a missing parent render.
        for (int selection = 0; selection < 3; selection++)
        {
            await attacker.ClickAsync();
            await Expect(attacker).ToHaveAttributeAsync("data-selected", "true", new() { Timeout = 250 });
            await Expect(Page.Locator("[data-testid='targeting-banner'][data-kind='Attack']"))
                .ToBeVisibleAsync(new() { Timeout = 250 });
            await Expect(targets.First).ToBeVisibleAsync(new() { Timeout = 250 });

            await attacker.ClickAsync();
            await Expect(attacker).ToHaveAttributeAsync("data-selected", "false", new() { Timeout = 250 });
            await Expect(banner).Not.ToBeVisibleAsync(new() { Timeout = 250 });
        }

        int enemySeat    = await EnemySeatAsync();
        int turnBefore   = await BoardTurnAsync();
        int denBefore    = await DenHpAsync(enemySeat);
        int enemyBodies  = await EnemyBodiesAsync();
        int hurtBefore   = await EnemyHurtBodiesAsync();
        int bubbleBefore = await EnemyBubbledBodiesAsync();

        // Select again, and this time follow through.
        await attacker.ClickAsync();
        await Expect(targets.First).ToBeVisibleAsync(new() { Timeout = MatchTimeout });
        await targets.First.ClickAsync();

        // The attack was sent and answered: the banner goes with the selection it belonged to.
        await Expect(banner).Not.ToBeVisibleAsync(new() { Timeout = MatchTimeout });

        // And the board moved. Which of the four shows it depends on what was tapped and on what the Weather
        // handed out — the Den takes damage, or a body dies, or a body is hurt, or a Bubble absorbs the whole
        // blow and pops — so the assertion is that something was attacked rather than which thing. **The
        // Bubble disjunct is not optional**: a Bubble absorbs the whole first damage INSTANCE, and
        // Bubble Bath grants one to every critter, so an attack into an intact Bubble legitimately leaves the
        // Den, the body count and the damage marks all where they were. Without this fourth reading the
        // fixture fails on the deals that draw that Weather, which is one of six.
        //
        // Every disjunct is a DELTA against a pre-attack snapshot, including the damage one: the damage mark
        // is a persistent state and not an event, so as an absolute count it was already true whenever an
        // earlier turn had traded into a surviving enemy body. Each is read off a `data-` value the board
        // writes — `data-hurt` off the health readout, `data-bubble` off the face — never off a style class:
        // a class is a look, and one that a card face renamed took a whole disjunct silently with it once.
        int denAfter   = denBefore;
        int bodies     = enemyBodies;
        int hurtNow    = hurtBefore;
        int bubbleNow  = bubbleBefore;

        bool Moved() => denAfter < denBefore || bodies < enemyBodies || hurtNow > hurtBefore
            || bubbleNow < bubbleBefore;

        for (int waited = 0; waited < MatchTimeout && !Moved(); waited += 200)
        {
            await Page.WaitForTimeoutAsync(200);
            denAfter  = await DenHpAsync(enemySeat);
            bodies    = await EnemyBodiesAsync();
            hurtNow   = await EnemyHurtBodiesAsync();
            bubbleNow = await EnemyBubbledBodiesAsync();
        }

        Assert.That(Moved(), Is.True,
            $"nothing was attacked: den {denBefore}→{denAfter}, enemy bodies {enemyBodies}→{bodies}, "
            + $"hurt bodies {hurtBefore}→{hurtNow}, intact bubbles {bubbleBefore}→{bubbleNow}");

        // The attack did not end the turn: it sent an attack and left the seat on turn. (It says nothing
        // about how many intents went out — the turn counter cannot see that, and the double-press lock has
        // its own fixture for it.)
        Assert.That(await BoardTurnAsync(), Is.EqualTo(turnBefore), "the attack ended the turn");

        Assert.That(ConsoleFailures(), Is.EqualTo("(none)"));
    }

    /// <summary>
    /// A critter of this seat's that can attack right now, playing turns until one appears. Which seat is on
    /// turn first and what the deal put on the board are both seeded, so waiting is what makes this fixture
    /// deterministic where "if there is an attacker" would be a coin flip.
    /// <para>
    /// The turns it plays are <b>whole</b> ones, attacks included, which is what keeps it off the deal's
    /// mercy: a seat that puts one body down a turn and never swings leaves the opponent to walk to the Den,
    /// and the game can end before an attacker has ever been ready. A critter played this turn is asleep, so
    /// what this returns is one that survived to the start of a later turn — and it is looked for before the
    /// turn is played rather than after it.
    /// </para>
    /// </summary>
    async Task<ILocator> FirstReadyAttackerAsync(int maxTurns = 12)
    {
        for (int turn = 0; turn < maxTurns; turn++)
        {
            Assert.That(await WaitForOwnTurnAsync(GameTimeout / 6), Is.True, "the seat never got its turn");
            Assert.That(await IsGameOverAsync(), Is.False, "the game ended before an attacker was ready");

            // A ready attacker read off a trailing board is a fact about a position the board has not
            // reached yet.
            await WaitUntilCaughtUpAsync();

            if (await SelectableCritters.CountAsync() > 0)
                return SelectableCritters.First;

            await PlayOneTurnAsync();
        }

        Assert.Fail($"no ready attacker appeared within {maxTurns} turns");
        return SelectableCritters.First;
    }

    /// <summary> How many critters the opponent has in play. </summary>
    Task<int> EnemyBodiesAsync()
        => Page.Locator("[data-testid='critter-row-enemy'] [data-testid='board-critter']").CountAsync();

    /// <summary>
    /// How many of them are carrying damage. Read as a count and compared as a delta, because the mark is a
    /// state rather than an event.
    /// </summary>
    Task<int> EnemyHurtBodiesAsync()
        => Page.Locator("[data-testid='critter-row-enemy'] [data-testid='critter-health'][data-hurt='true']").CountAsync();

    /// <summary>
    /// How many of them still carry an intact Bubble. The fourth thing an attack can change and the only one
    /// that leaves every other reading alone: a Bubble absorbs the whole first damage instance, so the body takes
    /// no damage, does not die and never marks. Read off the face's own `data-bubble`, which is the same
    /// `View.BubbleIntact` the board draws the bubble from.
    /// </summary>
    Task<int> EnemyBubbledBodiesAsync()
        => Page.Locator("[data-testid='critter-row-enemy'] [data-testid='card-face'][data-bubble='true']").CountAsync();

    /// <summary> A seat's Den hit points as a number, off the readout the board draws. </summary>
    async Task<int> DenHpAsync(int seat)
        => int.Parse((await DenHp(seat).InnerTextAsync()).Trim().TrimStart('❤', '️').Trim());

    /// <summary>
    /// Which seat index the opponent holds. Read off the board rather than assumed: which seat a player is
    /// dealt into comes out of the deal seed, so hard-coding 1 would be right about half the time.
    /// </summary>
    async Task<int> EnemySeatAsync()
        => int.Parse(await Page.GetByTestId("den-enemy").GetAttributeAsync("data-seat") ?? "1");

    /// <summary>
    /// The ring reads the server's clock against a stamp in the model. With the local server's lengthened
    /// turn deadline the ring is deliberately absent — most of a turn shows no clock at all, and it appears
    /// only as the deadline closes.
    /// </summary>
    [Test]
    public async Task TheDeadlineRingIsWithheldUntilTheDeadlineCloses()
    {
        await StartPracticeFromHomeAsync();
        await Expect(Page.GetByTestId("mulligan-overlay")).ToBeVisibleAsync(new() { Timeout = MatchTimeout });
        await ResolveMulliganAsync();

        // The first turn after the mulligan, so there is nothing a deal can do to make this unreachable.
        Assert.That(await WaitForOwnTurnAsync(GameTimeout / 6), Is.True, "the seat never got its first turn");
        Assert.That(await IsGameOverAsync(), Is.False, "a game ended on its first turn");

        // A clock that nags for a whole minute is worse than no clock. The local turn deadline is five
        // minutes and the ring's window is twenty seconds, so early in a turn there is no ring — and the
        // board keeps taking input, because nothing is going to take the turn away.
        await Expect(Page.GetByTestId("deadline-ring")).Not.ToBeVisibleAsync();
        await Expect(EndTurn).ToBeEnabledAsync();
    }
}
