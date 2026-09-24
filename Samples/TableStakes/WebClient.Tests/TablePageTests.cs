using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

namespace WebClient.Tests;

/// <summary>
/// E2E tests for the table page and the match protocol, run in offline mode. Offline mode hosts the real
/// multiplayer entity in the browser, so these tests also cover the replicated timeline, the private channel for
/// the seat's hand, and directed messages. The tests need only the web client, not the game server.
/// <para>
/// What a single frame of the table shows is asserted in <see cref="TableRenderTests"/>. This fixture covers what
/// needs a browser or the wall clock: a full game, drag gestures, computed CSS, reduced motion and animation
/// timing. The auto-advance timings in the query string are set to zero to finish a game, or to far longer than
/// the test to hold the table still.
/// </para>
/// </summary>
[TestFixture]
public class TablePageTests : PlaywrightPageTest
{
    /// <summary>Both auto-advance timings set to zero, so a game runs as fast as the frame pump allows.</summary>
    private static readonly string FastTableUrl = OfflineTableUrl + "&botThinkMs=0&resolvePauseMs=0";

    /// <summary>
    /// The bot think delay set far longer than the test, so no bot plays while a test looks at the table as dealt.
    /// </summary>
    private static readonly string HeldTableUrl = OfflineTableUrl + "&botThinkMs=600000&resolvePauseMs=0";

    /// <summary>
    /// Bots play at once and the resolve pause does not end during the test, so the table stops at the viewer's
    /// first turn with the cards of the bots before them still in the centre.
    /// </summary>
    private static readonly string HeldResolveTableUrl = OfflineTableUrl + "&botThinkMs=0&resolvePauseMs=600000";

    /// <summary>
    /// The resolve pause and the trick-resolve beat both set far longer than the test, so the beat stays in its
    /// first half: the winning card is indicated and nothing is swept yet.
    /// <para>
    /// <c>beatMs</c> is a client-side timing, not a host one. At its shipped value the beat ends before a test can
    /// assert anything inside it, so the tests lengthen it.
    /// </para>
    /// </summary>
    private static readonly string HeldBeatTableUrl = OfflineTableUrl + "&botThinkMs=0&resolvePauseMs=600000&beatMs=600000";

    /// <summary>
    /// The resolve pause held open and the beat long enough to observe both halves: the winner is indicated, the
    /// four cards sweep, and the centre clears while the pause is still running.
    /// </summary>
    private static readonly string SlowBeatTableUrl = OfflineTableUrl + "&botThinkMs=0&resolvePauseMs=600000&beatMs=4000";

    /// <summary>
    /// A resolve pause that is longer than the beat and then ends, as in the shipped timings but slow enough to
    /// observe: the four cards fly to the winner, the beat ends, and then the host ends the match. This makes it
    /// possible to assert that the results overlay waits for the last trick.
    /// <para>
    /// Both timings are long because several round trips must fit inside the sweep while other test cases run in
    /// parallel and contend for the client's render thread.
    /// </para>
    /// </summary>
    private static readonly string TakenTrickTableUrl = OfflineTableUrl + "&botThinkMs=0&resolvePauseMs=4000&beatMs=2500";

    /// <summary>
    /// The trump intro set far longer than the test, so the word is still on the felt and the disc is still moving
    /// in when the test looks. <c>trumpIntroMs</c> is a client-side timing. At its shipped value the intro ends
    /// before a cold WASM boot finishes.
    /// </summary>
    private static readonly string HeldTrumpIntroTableUrl = HeldTableUrl + "&trumpIntroMs=600000";

    /// <summary>
    /// An intro long enough to see the word on the felt and then see it leave. A zero-length intro would only show
    /// that nothing is drawn, which a broken intro also shows.
    /// </summary>
    private static readonly string ShortTrumpIntroTableUrl = HeldTableUrl + "&trumpIntroMs=8000";

    /// <summary>Matches any suit name as the trump disc writes it in <c>data-suit</c>.</summary>
    private static readonly System.Text.RegularExpressions.Regex AnySuit =
        new System.Text.RegularExpressions.Regex("^(Clubs|Diamonds|Hearts|Spades)$");

    [Test]
    public async Task Table_IntroducesTheTrump_OnAHandItSeesDealt()
    {
        await OpenTableAsync(HeldTrumpIntroTableUrl);

        // The intro plays only as part of the dealing opening, which a client gets when it sees the table from
        // the first card.
        await Expect(Page.GetByTestId("table")).ToHaveAttributeAsync("data-dealing", "true");

        // The word is on the felt and the disc has not yet arrived at the centre.
        await Expect(Page.GetByTestId("trump-intro")).ToBeVisibleAsync();
        await Expect(Page.GetByTestId("trump-intro")).ToHaveTextAsync("Trump");
        await Expect(Page.GetByTestId("trump-suit")).ToHaveCSSAsync("opacity", "0");

        // The disc carries the suit from the first frame, so the information does not depend on the animation.
        await Expect(Page.GetByTestId("trump-suit")).ToHaveAttributeAsync("data-suit", AnySuit);
    }

    /// <summary>
    /// The trump disc is fully opaque after the intro. bUnit has no CSS engine, so this property is asserted here.
    /// The disc's suit, glyph and accessible name, and the absence of <c>trump-intro</c> after the opening, are
    /// asserted in <c>TableRenderTests.Table_ShowsTheTrumpSuitAtTheCentre_AndAnnouncesIt</c>.
    /// </summary>
    [Test]
    public async Task Table_LeavesTheTrumpDiscBehind_WhenTheIntroFinishes()
    {
        await OpenTableAsync(ShortTrumpIntroTableUrl);

        // The intro element is removed, not only faded out. A transparent element would stay over the centre for
        // the rest of the game.
        await Expect(Page.GetByTestId("trump-intro")).ToHaveCountAsync(0, new() { Timeout = TurnTimeoutMs });

        ILocator trump = Page.GetByTestId("trump-suit");
        await Expect(trump).ToBeVisibleAsync();
        await Expect(trump).ToHaveCSSAsync("opacity", "1");
        await Expect(trump).ToHaveAttributeAsync("data-suit", AnySuit);
    }

    [Test]
    public async Task Table_WithMotionReduced_LandsOnTheSettledTrumpDisc_AndSaysNothingElse()
    {
        await Page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.Reduce });
        await OpenTableAsync(HeldTrumpIntroTableUrl);

        // The intro timing is still running, but with reduced motion the word is not drawn and the disc starts in
        // its settled state.
        await Expect(Page.GetByTestId("table")).ToHaveAttributeAsync("data-dealing", "true");
        await Expect(Page.GetByTestId("trump-intro")).Not.ToBeVisibleAsync();

        ILocator trump = Page.GetByTestId("trump-suit");
        await Expect(trump).ToBeVisibleAsync();
        await Expect(trump).ToHaveCSSAsync("opacity", "1");
        await Expect(trump).ToHaveAttributeAsync("data-suit", AnySuit);
        await Expect(trump).ToHaveAttributeAsync("aria-label", $"Trump suit: {await trump.GetAttributeAsync("data-suit")}");
    }

    [Test]
    public async Task Table_KeyboardPlaysACard_EvenAfterADragPutOneBack()
    {
        // With HeldResolveTableUrl, the table stops at the viewer's first turn with the full hand, whatever the deal.
        await OpenTableAsync(HeldResolveTableUrl);

        ILocator card = Page.GetByTestId("legal-card").First;
        await Expect(card).ToBeVisibleAsync(new() { Timeout = TurnTimeoutMs });

        string cardName = await card.GetAttributeAsync("data-card") ?? "";

        // Drag the card sideways and release, so it returns to the hand and nothing is played. The release lands
        // on the drag surface, so the browser's click after it misses the card. The table suppresses that click.
        (float gripX, float gripY) = await GripCardAsync(card);
        await DragSidewaysAsync(gripX, gripY);
        await Expect(Page.Locator(".ts-drag-surface")).ToBeAttachedAsync();
        await Page.Mouse.UpAsync();

        await Expect(Page.Locator(".ts-drag-surface")).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("own-hand-count")).ToHaveTextAsync("5");
        await Expect(Page.GetByTestId("hand-locked")).ToHaveTextAsync("false");

        // Enter on the focused card must play it. A keyboard activation fires a click with no pointer gesture
        // before it, so the click suppression left by the drag must not swallow it.
        ILocator sameCard = Page.Locator($".ts-handcard[data-card='{cardName}']");
        await sameCard.FocusAsync();
        await Page.Keyboard.PressAsync("Enter");
        await Expect(Page.GetByTestId("own-hand-count")).ToHaveTextAsync("4", new() { Timeout = TurnTimeoutMs });
    }

    /// <summary>
    /// A whole game against the offline host, one legal card per trick, ending with the results overlay over the
    /// finished table.
    /// </summary>
    [Test]
    public async Task Table_PlaysAFullFiveTrickGame_WithNoGameServer()
    {
        await OpenTableAsync(FastTableUrl);

        ILocator handCount = Page.GetByTestId("own-hand-count");

        // One turn per trick. Waiting for a legal card also waits for the bots, because a card is marked legal
        // only on this seat's turn.
        for (int cardsLeft = 5; cardsLeft > 0; cardsLeft--)
        {
            await Expect(handCount).ToHaveTextAsync(cardsLeft.ToString(), new() { Timeout = TurnTimeoutMs });

            ILocator card = Page.GetByTestId("legal-card").First;
            await Expect(card).ToBeVisibleAsync(new() { Timeout = TurnTimeoutMs });

            // Every card in hand is either legal or illegal, and illegal cards are disabled.
            int legal   = await Page.GetByTestId("legal-card").CountAsync();
            int illegal = await Page.GetByTestId("illegal-card").CountAsync();
            Assert.That(legal + illegal, Is.EqualTo(cardsLeft), $"with {cardsLeft} cards in hand, every card should be legal or illegal");
            for (int ndx = 0; ndx < illegal; ndx++)
                await Expect(Page.GetByTestId("illegal-card").Nth(ndx)).ToBeDisabledAsync();

            await card.ClickAsync();

            // The card leaves the hand only when the host confirms the play, so this wait also fails the test at
            // the trick where a play is refused. The refusal banner cannot be checked instead, because it clears
            // when the board moves past the refused play.
            await Expect(handCount).ToHaveTextAsync((cardsLeft - 1).ToString(), new() { Timeout = TurnTimeoutMs });

            // The confirming update releases the lift and the lock, so the hand accepts input again.
            await Expect(Page.GetByTestId("hand-locked")).ToHaveTextAsync("false", new() { Timeout = TurnTimeoutMs });
            await Expect(Page.GetByTestId("lifted-card")).ToHaveCountAsync(0);
        }

        await Expect(Page.GetByTestId("match-phase")).ToHaveTextAsync("Ended", new() { Timeout = TurnTimeoutMs });

        // Every card of every seat was played and every trick resolved.
        await Expect(Page.GetByTestId("plays-count")).ToHaveTextAsync("20");
        await Expect(Page.GetByTestId("tricks-resolved")).ToHaveTextAsync("5");

        // The results overlay is drawn over the finished table. The last trick has already swept to its winner,
        // so no trick card remains.
        ILocator results = Page.GetByTestId("results-overlay");
        await Expect(results).ToBeVisibleAsync(new() { Timeout = TurnTimeoutMs });
        await Expect(Page.GetByTestId("trump-suit")).ToBeVisibleAsync();
        await Expect(Page.GetByTestId("trick-card")).ToHaveCountAsync(0);

        // All four standings are shown, not only the winner.
        await Expect(results.GetByRole(AriaRole.Listitem)).ToHaveCountAsync(4);
        for (int rank = 0; rank < 4; rank++)
            await Expect(Page.GetByTestId($"standing-{rank}")).ToBeVisibleAsync();
        await Expect(Page.GetByTestId("results-winner")).Not.ToBeEmptyAsync();

        // The reason line is shown only when first place was not decided by more tricks, because the standings
        // already show trick counts.
        string separation = await Page.GetByTestId("standing-0").GetAttributeAsync("data-separation") ?? "";
        int    reasonLines = separation == "MoreTricks" ? 0 : 1;
        await Expect(Page.GetByTestId("results-reason")).ToHaveCountAsync(reasonLines);
        if (reasonLines == 1)
            await Expect(Page.GetByTestId("results-reason")).Not.ToBeEmptyAsync();

        // Confetti is drawn if and only if the viewer won. The deal decides the outcome, so the test asserts the
        // relation instead of a fixed outcome. The confetti element stays after its one-shot CSS animation, so the
        // count does not depend on how long the game took.
        string outcome = await Page.GetByTestId("results-winner").GetAttributeAsync("data-outcome");
        Assert.That(outcome, Is.AnyOf("won", "lost"));
        await Expect(Page.GetByTestId("confetti")).ToHaveCountAsync(outcome == "won" ? 1 : 0);

        // The abandoned panel is only for a match that never started.
        await Expect(Page.GetByTestId("abandoned-panel")).ToHaveCountAsync(0);
    }

    /// <summary>Play the viewer's first card and wait for the trick it completes to resolve.</summary>
    private async Task PlayIntoTheFirstResolvedTrickAsync(string url)
    {
        await OpenTableAsync(url);

        ILocator card = Page.GetByTestId("legal-card").First;
        await Expect(card).ToBeVisibleAsync(new() { Timeout = TurnTimeoutMs });
        await card.ClickAsync();

        await Expect(Page.GetByTestId("tricks-resolved")).ToHaveTextAsync("1", new() { Timeout = TurnTimeoutMs });
    }

    [Test]
    public async Task Table_IndicatesTheWinningCard_BeforeSweepingTheTrick()
    {
        await PlayIntoTheFirstResolvedTrickAsync(HeldBeatTableUrl);

        // First half of the beat: all four cards are in the centre, the winner's card is marked, and nothing is
        // sweeping. The board has already resolved the trick, and the client keeps it on screen
        // (docs/web-client.md, "The table's two clocks").
        await Expect(Page.GetByTestId("beat-playing")).ToHaveTextAsync("true", new() { Timeout = TurnTimeoutMs });
        await Expect(Page.GetByTestId("trick-card")).ToHaveCountAsync(4);
        await Expect(Page.Locator("[data-testid=trick-card][data-winner=true]")).ToHaveCountAsync(1);
        await Expect(Page.Locator("[data-testid=trick-card][data-sweeping=true]")).ToHaveCountAsync(0);

        // The marked card belongs to the seat the board names as the trick winner.
        string winnerSeat = await Page.GetByTestId("trick-winner-seat").InnerTextAsync();
        await Expect(Page.Locator($"[data-testid=trick-card][data-winner=true][data-seat='{winnerSeat}']")).ToHaveCountAsync(1);

        // No seat is shown on turn while the beat plays, because on screen the next trick has not started.
        await Expect(Page.GetByTestId("presented-seat-on-turn")).ToHaveTextAsync("-1");
        await Expect(Page.Locator("[data-testid^=seat-][data-on-turn=true]")).ToHaveCountAsync(0);
    }

    [Test]
    public async Task Table_SweepsTheTrickToTheWinnersPlaque_AndThenClearsTheCentre()
    {
        await PlayIntoTheFirstResolvedTrickAsync(SlowBeatTableUrl);

        string winnerSeat = await Page.GetByTestId("trick-winner-seat").InnerTextAsync();

        // Second half of the beat: the four cards move to the winning seat, and that seat's plaque takes them.
        //
        // `data-sweeping` is true only in a window once per trick: from `Table.BeatIndicatePercent` of `beatMs`
        // until the beat ends, which `MatchClocks.GetBeatEndsAt` clamps to the resolve pause. The poll lands in
        // the window because the wait above ends on the board change that starts the beat and SlowBeatTableUrl
        // keeps the pause far longer than the beat. Changing either timing, or adding a wait before this line,
        // can make the poll miss the window.
        await Expect(Page.Locator("[data-testid=trick-card][data-sweeping=true]")).ToHaveCountAsync(4, new() { Timeout = TurnTimeoutMs });
        await Expect(Page.Locator($"[data-testid=seat-{winnerSeat}][data-taking-trick=true]")).ToHaveCountAsync(1);

        // The winner's plaque shows the point label as the cards land. The label disappears when the beat ends,
        // so this assertion must come before the beat-end assertions below.
        await Expect(Page.Locator($"[data-testid=seat-{winnerSeat}] [data-testid=point-text]")).ToHaveTextAsync("+1 point!");

        // The beat ends and the centre clears while the host's resolve pause is still running, so the client's
        // beat timing ended it, not a board change.
        await Expect(Page.GetByTestId("beat-playing")).ToHaveTextAsync("false", new() { Timeout = TurnTimeoutMs });
        await Expect(Page.GetByTestId("trick-card")).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("turn-phase")).ToHaveTextAsync("ResolvingTrick");
        await Expect(Page.Locator("[data-testid^=seat-][data-taking-trick=true]")).ToHaveCountAsync(0);
    }

    [Test]
    public async Task Table_TakesTheFinalTrick_BeforeTheResultsOverlayArrives()
    {
        // TakenTrickTableUrl makes the last trick's sweep long enough to assert that the results overlay is
        // absent during it.
        await OpenTableAsync(TakenTrickTableUrl);

        // Play the viewer's whole hand. Each trick's beat ends before the next turn starts, so the loop reaches
        // the last trick without races.
        ILocator handCount = Page.GetByTestId("own-hand-count");
        for (int cardsLeft = 5; cardsLeft > 0; cardsLeft--)
        {
            await Expect(handCount).ToHaveTextAsync(cardsLeft.ToString(), new() { Timeout = TurnTimeoutMs });
            ILocator card = Page.GetByTestId("legal-card").First;
            await Expect(card).ToBeVisibleAsync(new() { Timeout = TurnTimeoutMs });
            await card.ClickAsync();
        }

        // The last trick is resolved on the board and its cards are sweeping to the winner.
        await Expect(Page.GetByTestId("tricks-resolved")).ToHaveTextAsync("5", new() { Timeout = TurnTimeoutMs });

        // The same one-time `data-sweeping` window as in
        // Table_SweepsTheTrickToTheWinnersPlaque_AndThenClearsTheCentre, and the wait above ends on the board
        // change that starts the beat. Here the beat and the pause are close in length, so the margin is smaller.
        // A longer `beatMs` or a shorter `resolvePauseMs` in TakenTrickTableUrl closes the window, and this poll
        // then fails on every run.
        await Expect(Page.Locator("[data-testid=trick-card][data-sweeping=true]")).ToHaveCountAsync(4, new() { Timeout = TurnTimeoutMs });

        // The results overlay must not cover the last trick while it sweeps.
        string winnerSeat = await Page.GetByTestId("trick-winner-seat").InnerTextAsync();
        await Expect(Page.Locator($"[data-testid=seat-{winnerSeat}][data-taking-trick=true]")).ToHaveCountAsync(1);
        await Expect(Page.GetByTestId("results-overlay")).ToHaveCountAsync(0);

        // After the beat ends and the centre clears, the results overlay appears.
        await Expect(Page.GetByTestId("beat-playing")).ToHaveTextAsync("false", new() { Timeout = TurnTimeoutMs });
        await Expect(Page.GetByTestId("trick-card")).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("results-overlay")).ToBeVisibleAsync(new() { Timeout = TurnTimeoutMs });
    }

    [Test]
    public async Task Table_LightsTheCentreAsTheDropTarget_WhileACardIsDragged()
    {
        await OpenTableAsync(HeldResolveTableUrl);

        ILocator centre = Page.GetByTestId("centre");
        await Expect(centre).ToHaveAttributeAsync("data-drop-target", "false");

        ILocator card = Page.GetByTestId("legal-card").First;
        await Expect(card).ToBeVisibleAsync(new() { Timeout = TurnTimeoutMs });

        // Press and hold without releasing. The card does not follow the pointer, because that would cost a
        // render per pointer event. The grabbed card style and the centre's drop-target state show the gesture.
        (float gripX, float gripY) = await GripCardAsync(card);

        await Expect(centre).ToHaveAttributeAsync("data-drop-target", "true", new() { Timeout = TurnTimeoutMs });
        await Expect(Page.Locator(".ts-handcard--grabbed")).ToHaveCountAsync(1);

        // Release away from the centre and beyond the tap threshold, so the card returns to the hand, nothing is
        // played, and the drop-target state clears.
        await DragSidewaysAsync(gripX, gripY);
        await Page.Mouse.UpAsync();

        await Expect(centre).ToHaveAttributeAsync("data-drop-target", "false", new() { Timeout = TurnTimeoutMs });
        await Expect(Page.Locator(".ts-handcard--grabbed")).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("own-hand-count")).ToHaveTextAsync("5");
    }

    [Test]
    public async Task Table_MarksEveryTrickCardAndTheBoss_ConsistentlyWithTheBoard()
    {
        await OpenTableAsync(FastTableUrl);

        int slotsSeen      = 0;
        int trumpSlotsSeen = 0;

        await PlayWholeHandAsync(PlayAndSampleTheCentreAsync);

        Assert.That(slotsSeen, Is.GreaterThan(0),
            $"no trick card was ever on the felt to check ({trumpSlotsSeen} of those seen were trumps)");

        // One turn: sample the centre, play a card, and keep sampling until the trick resolves.
        async Task PlayAndSampleTheCentreAsync()
        {
            await Expect(Page.GetByTestId("legal-card").First).ToBeVisibleAsync(new() { Timeout = TurnTimeoutMs });

            // The centre does not change during the viewer's turn, so this sample is stable.
            (int slots, int trumps) = await AssertCentreConsistencyAsync();
            slotsSeen      += slots;
            trumpSlotsSeen += trumps;

            // Play a trump when one is legal, so the trump marking is exercised and not only the unmarked case.
            ILocator toPlay = await FindLegalTrumpOtherwiseFirstAsync();
            int resolvedBefore = int.Parse(await Page.GetByTestId("tricks-resolved").TextContentAsync() ?? "0");

            await toPlay.ClickAsync();

            // With zero timings, the viewer's card is in the centre only for a few frames before the trick
            // resolves, so the centre is sampled repeatedly until the resolved count goes up.
            DateTime samplingEndsAt = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < samplingEndsAt)
            {
                (int landed, int landedTrumps) = await AssertCentreConsistencyAsync();
                slotsSeen      += landed;
                trumpSlotsSeen += landedTrumps;

                string resolvedNow = await Page.GetByTestId("tricks-resolved").TextContentAsync() ?? "";
                if (resolvedNow == (resolvedBefore + 1).ToString())
                    break;
                await Task.Delay(50);
            }

            await Expect(Page.GetByTestId("tricks-resolved")).ToHaveTextAsync((resolvedBefore + 1).ToString(), new() { Timeout = TurnTimeoutMs });
            await Expect(Page.GetByTestId("hand-locked")).ToHaveTextAsync("false", new() { Timeout = TurnTimeoutMs });
        }
    }

    /// <summary>
    /// Read the centre in one script call and assert that it is consistent: each trick card's <c>data-trump</c>
    /// is true exactly when its suit is the trump suit, and the trump disc's <c>data-powering</c> is true exactly
    /// when at least one trick card is a trump. A single call is used because a card can land between two locator
    /// reads. Returns the number of trick cards and trump trick cards read. A trick may contain no trump, so the
    /// counts are not asserted.
    /// </summary>
    private async Task<(int Slots, int TrumpSlots)> AssertCentreConsistencyAsync()
    {
        string json = await Page.EvaluateAsync<string>(@"() => {
            const boss = document.querySelector('[data-testid=trump-suit]');
            return JSON.stringify({
                suit: boss ? boss.getAttribute('data-suit') : null,
                powering: boss ? boss.getAttribute('data-powering') : null,
                slots: Array.from(document.querySelectorAll('[data-testid=trick-card]')).map(slot => ({
                    trump: slot.getAttribute('data-trump'),
                    suit: (slot.querySelector('.ts-card--face')?.getAttribute('data-card') ?? '').slice(-1)
                }))
            });
        }");

        using System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(json);
        System.Text.Json.JsonElement root = document.RootElement;

        string  trumpSuit = root.GetProperty("suit").GetString() ?? "";
        string  letter    = SuitLetterOf(trumpSuit);
        bool    powering  = root.GetProperty("powering").GetString() == "true";

        int slots      = 0;
        int trumpSlots = 0;
        foreach (System.Text.Json.JsonElement slot in root.GetProperty("slots").EnumerateArray())
        {
            slots += 1;
            string cardSuit = slot.GetProperty("suit").GetString() ?? "";
            bool   isTrump  = slot.GetProperty("trump").GetString() == "true";

            Assert.That(isTrump, Is.EqualTo(cardSuit == letter),
                $"a '{cardSuit}' on the felt against trump {trumpSuit} was marked data-trump={isTrump}");
            if (isTrump)
                trumpSlots += 1;
        }

        Assert.That(powering, Is.EqualTo(trumpSlots > 0),
            $"the boss said powering={powering} with {trumpSlots} trump slot(s) on the stage");

        return (slots, trumpSlots);
    }

    /// <summary>A legal trump card if the viewer has one, otherwise the first legal card.</summary>
    private async Task<ILocator> FindLegalTrumpOtherwiseFirstAsync()
    {
        string? suit  = await Page.GetByTestId("trump-suit").GetAttributeAsync("data-suit");
        string  letter = SuitLetterOf(suit ?? "");

        ILocator legal = Page.GetByTestId("legal-card");
        int count = await legal.CountAsync();
        for (int ndx = 0; ndx < count; ndx++)
        {
            string? card = await legal.Nth(ndx).GetAttributeAsync("data-card");
            if (card != null && card.EndsWith(letter, StringComparison.Ordinal))
                return legal.Nth(ndx);
        }

        return legal.First;
    }

    /// <summary>The suit letter that a card's <c>data-card</c> value ends with, for the named suit.</summary>
    private static string SuitLetterOf(string suit) => suit switch
    {
        "Clubs"    => "C",
        "Diamonds" => "D",
        "Hearts"   => "H",
        _          => "S",
    };

    /// <summary>
    /// The table page has no drag-to-scroll surface and nothing to scroll.
    /// <para>
    /// The shell's drag-to-scroll lets a mouse drag scroll a surface, and it swallows the click at the end of a
    /// drag. The table uses press-and-drag to play a card, so a drag-to-scroll surface on the table would stop
    /// cards from responding.
    /// </para>
    /// </summary>
    [Test]
    public async Task Table_HasNoDragScrollSurface()
    {
        await OpenTableAsync(HeldTableUrl);

        Assert.That(await Page.Locator("[data-drag-scroll]").CountAsync(), Is.Zero,
            "the game route drew a drag-to-scroll surface");

        int overflow = await Page.EvaluateAsync<int>(
            "() => { const m = document.querySelector('main');" +
            " return m ? Math.max(m.scrollHeight - m.clientHeight, m.scrollWidth - m.clientWidth) : -1; }");

        Assert.That(overflow, Is.LessThanOrEqualTo(0), "the table has something to scroll");
    }
}
