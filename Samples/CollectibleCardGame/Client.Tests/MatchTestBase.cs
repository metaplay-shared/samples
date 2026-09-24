using Microsoft.Playwright;
using NUnit.Framework;
using System.Collections.Generic;

namespace Game.Client.Tests;

/// <summary>
/// Locators and interactions for the match board.
/// <para>
/// Every match fixture is a live-server fixture. Offline mode is the player loop only — matches are never
/// hosted offline, because an in-browser host would be a second implementation of the turn flow and a second
/// answer to every robustness rule.
/// </para>
/// </summary>
public abstract class MatchTestBase : MetaScreenTestBase
{
    /// <summary> A cold WebAssembly boot, a session start, and the table's own set-up. </summary>
    protected const int MatchTimeout = 45000;

    /// <summary>
    /// Long enough for a bot to play out several turns at the local server's shortened delays, and for the
    /// game to reach a Den at zero. The local config zeroes nothing — it shortens — so this is a real game
    /// being played rather than one fast-forwarded.
    /// </summary>
    protected const int GameTimeout = 180000;

    /// <summary>
    /// The browser's own console, captured for the whole fixture. A board that stops responding says nothing
    /// through the DOM; the client's log says what happened to its session.
    /// </summary>
    protected readonly List<string> ConsoleLog = new List<string>();

    [SetUp]
    public void CaptureConsole()
    {
        ConsoleLog.Clear();
        AffordableCardsChecked = 0;
        Page.Console += (_, message) => ConsoleLog.Add($"[{message.Type}] {message.Text}");
        Page.PageError += (_, error) => ConsoleLog.Add($"[pageerror] {error}");
    }

    /// <summary> The tail of the browser console, for a failure the DOM cannot explain. </summary>
    protected string ConsoleTail(int lines = 40)
    {
        int skip = ConsoleLog.Count > lines ? ConsoleLog.Count - lines : 0;
        return string.Join("\n", ConsoleLog.GetRange(skip, ConsoleLog.Count - skip));
    }

    /// <summary>
    /// Every console line that looks like a failure rather than traffic. A board that stops responding is
    /// usually a frame loop that is throwing, and that is a needle in a log of message envelopes.
    /// </summary>
    protected string ConsoleFailures()
    {
        List<string> failures = new List<string>();
        foreach (string line in ConsoleLog)
        {
            if (line.Contains("frame failure") || line.Contains("[pageerror]") || line.Contains("[error]")
                || line.Contains("Exception") || line.Contains(" ERR "))
                failures.Add(line);
        }

        return failures.Count == 0 ? "(none)" : string.Join("\n", failures);
    }

    /// <summary> Reduced motion, so the board is at its end state rather than mid-beat when a test looks. </summary>
    protected static string BoardUrl(string query = "") => $"{BaseUrl}/match?motion=reduced{query}";

    // Every board locator is defined once against a NAMED page and once more as this fixture's own. The
    // two-seat suites drive two pages at a time, and a vocabulary bound to the fixture's single Page cannot
    // reach the second one — so the parameterised form is the definition and the property is the shorthand.

    protected static ILocator HandCardsOn(IPage page) => page.GetByTestId("hand-card");

    protected static ILocator PlayableHandCardsOn(IPage page) => page.Locator("[data-testid='hand-card'][data-playable='true']");

    protected static ILocator MyCrittersOn(IPage page) => page.Locator("[data-testid='critter-row-mine'] [data-testid='board-critter']");

    protected static ILocator SelectableCrittersOn(IPage page) => page.Locator("[data-testid='critter-row-mine'] [data-testid='board-critter'][data-selectable='true']");

    protected static ILocator TargetableCrittersOn(IPage page) => page.Locator("[data-testid='board-critter'][data-targetable='true']");

    protected static ILocator TargetableDensOn(IPage page) => page.Locator("[data-testid^='den-'][data-targetable='true']");

    /// <summary>
    /// The hand cards whose legality is a question about mana alone: critters — the stats line is what says so
    /// — that ask for no target. <c>WhiskerThief</c> is the one critter in the catalogue that asks for one, so
    /// its legality is a fact about the enemy board rather than about mana, and it is excluded by name.
    /// </summary>
    protected static ILocator PlainCritterCardsOn(IPage page) => page.Locator(
        "[data-testid='hand-card']:has([data-testid='hand-card-attack']):not([data-card-id='WhiskerThief'])");

    protected static ILocator EndTurnOn(IPage page) => page.GetByTestId("end-turn");

    protected static ILocator TurnIndicatorOn(IPage page) => page.GetByTestId("turn-indicator");

    protected static ILocator MatchResultOn(IPage page) => page.GetByTestId("match-result");

    protected ILocator HandCards => HandCardsOn(Page);

    protected ILocator PlayableHandCards => PlayableHandCardsOn(Page);

    protected ILocator BoardCritters => Page.GetByTestId("board-critter");

    protected ILocator MyCritters => MyCrittersOn(Page);

    protected ILocator SelectableCritters => SelectableCrittersOn(Page);

    protected ILocator TargetableCritters => TargetableCrittersOn(Page);

    protected ILocator TargetableDens => TargetableDensOn(Page);

    protected ILocator PlainCritterCards => PlainCritterCardsOn(Page);

    protected ILocator EndTurn => EndTurnOn(Page);

    protected ILocator TurnIndicator => TurnIndicatorOn(Page);

    protected ILocator MatchResult => MatchResultOn(Page);

    /// <summary>
    /// Anything on screen that says the session feeding this board has gone: the app shell's connection pill,
    /// its connection modal, or the board's own waiting state.
    /// <para>
    /// A restart test needs this because the board <em>outlives its session</em>. The client holds the last
    /// committed model, and the detach and the re-attach land in one continuation on the browser's single
    /// thread, so the renderer never gets a frame between them — the board's waiting state is not reliably
    /// observable even though it is entered. Waiting for this to appear and then to clear is what separates
    /// "the session came back" from "the pre-restart board is still on screen", and every value read off a
    /// board before that separation is a pre-restart value.
    /// </para>
    /// </summary>
    protected ILocator SessionGap => Page.Locator(
        "[data-testid='connection-pill'], [data-testid='connection-modal'], [data-testid='board-waiting']");

    /// <summary> A seat's Den hit points, by seat index. </summary>
    protected ILocator DenHp(int seat) => Page.Locator($"[data-testid='den-hp'][data-seat='{seat}']");

    /// <summary>
    /// Start a session on Home and then press Practice, which is the mid-session attach path: the association
    /// arrives on the match slot during a live session, the client buffers the new channel while it settles,
    /// and the page turns exactly once, when there is a board to draw.
    /// </summary>
    /// <summary>
    /// Home to a live board, the way a player gets there.
    /// </summary>
    /// <param name="query">
    /// Extra query parameters for the board page — the client's own test affordances (<c>beat</c>,
    /// <c>motion</c>, <c>stale</c>). Home is entered without them, because Practice is pressed there and the
    /// board is what reads them; they are appended once the navigation to <c>/match</c> has happened.
    /// </param>
    /// <returns>
    /// This account's own display name, read off Home on the way past. It is the one place a fixture can learn
    /// it that is not the thing under test — the board's own plaque is what a seat's identity assertions are
    /// about — so it is returned rather than discarded. Callers that do not want it simply await the call.
    /// </returns>
    protected async Task<string> StartPracticeFromHomeAsync(string query = "")
    {
        await Page.GotoAsync(BaseUrl);
        await Expect(Page.GetByTestId("display-name")).ToBeVisibleAsync(new() { Timeout = BootTimeout });
        string displayName = (await Page.GetByTestId("display-name").TextContentAsync() ?? "").Trim();

        // No deck to build: a fresh account's picker opens on the first starter deck, which is what makes
        // every board fixture cheaper by a whole deckbuilder round trip.
        await Expect(Page.GetByTestId("queue-practice")).ToBeVisibleAsync(new() { Timeout = BootTimeout });
        await Page.GetByTestId("queue-practice").ClickAsync();

        // The page turns on the sub-client becoming active, not on the press.
        await Expect(Page.GetByTestId("match-board")).ToBeVisibleAsync(new() { Timeout = MatchTimeout });

        if (query.Length > 0)
            await ReloadBoardWithAsync(query);

        return displayName;
    }

    /// <summary>
    /// Reload the board with extra query parameters — the client's own test affordances (<c>beat</c>,
    /// <c>motion</c>, <c>stale</c>). The match is already live and the account's pointer is what re-attaches,
    /// so this is the same table coming back with the flags on, hand and all.
    /// <para>
    /// Worth turning on <em>late</em>: <c>stale=1</c> sends an End turn ahead of every intent the page sends,
    /// so each one reaches the table after the turn has passed — including the presses a helper uses to drive
    /// the board somewhere. Reach the state first, then switch the flag on.
    /// </para>
    /// </summary>
    protected async Task ReloadBoardWithAsync(string query)
    {
        string separator = Page.Url.Contains('?') ? "&" : "?";
        await Page.GotoAsync($"{Page.Url}{separator}{query}");
        await Expect(Page.GetByTestId("match-board")).ToBeVisibleAsync(new() { Timeout = MatchTimeout });
        await Expect(HandCards.First).ToBeVisibleAsync(new() { Timeout = MatchTimeout });
    }

    /// <summary>
    /// A card this seat can actually play, ending turns until one appears rather than skipping the test when
    /// the opening hand has nothing cheap in it. Mana grows every turn, so a deck that is legal at all offers
    /// something within a few turns — which is what makes this deterministic where "if nothing is playable,
    /// give up" was a coin flip on the deal.
    /// </summary>
    protected async Task<ILocator> FirstPlayableCardAsync(int maxTurns = 8)
    {
        for (int turn = 0; turn < maxTurns; turn++)
        {
            if (await PlayableHandCards.CountAsync() > 0)
                return PlayableHandCards.First;

            if (await IsGameOverAsync())
                break;

            await Expect(EndTurn).ToBeEnabledAsync(new() { Timeout = MatchTimeout });
            await EndTurn.ClickAsync();

            if (!await WaitForOwnTurnAsync(GameTimeout / 6))
                break;
        }

        Assert.Fail($"no playable card appeared within {maxTurns} turns; the deck or the mana curve has changed");
        return PlayableHandCards.First;
    }

    /// <summary>
    /// Answer the mulligan, keeping the dealt hand. The overlay is the one place the client batches, because
    /// the server resolves it as one step.
    /// </summary>
    protected async Task ResolveMulliganAsync()
    {
        ILocator confirm = Page.GetByTestId("mulligan-confirm");
        if (!await confirm.IsVisibleAsync())
            return;

        // The Weather is held on screen before the mulligan opens — the table accepts nothing while a beat is
        // held — so wait for the confirm to be live rather than pressing into a refusal.
        await Expect(confirm).ToBeEnabledAsync(new() { Timeout = MatchTimeout });
        await confirm.ClickAsync();
        await Expect(Page.GetByTestId("mulligan-overlay")).Not.ToBeVisibleAsync(new() { Timeout = MatchTimeout });
    }

    /// <summary> The cards the fan is offering to put back while the mulligan is open. </summary>
    protected ILocator MarkableHandCards => Page.Locator("[data-testid='hand-card'][data-mulligan='true']");

    /// <summary>
    /// Answer the mulligan by putting one card back, and return the instance that was marked.
    /// <para>
    /// The marks are made on the hand itself — that is the whole interaction, and the overlay's scrim stops
    /// above the fan so that they can be — so this taps a card the fan offers and then presses a confirm that
    /// has become "Replace 1". A mark is local state until the confirm sends it, so what is waited for after
    /// the tap is the card's own <c>data-marked</c> rather than anything from the server.
    /// </para>
    /// <para>
    /// It taps what the board offers rather than a fixed slot, which is the difference between driving the
    /// game and knowing the deal: what the fan offers is the table's own answer, and this presses whatever
    /// that turns out to be.
    /// </para>
    /// </summary>
    protected async Task<string> ReplaceOneCardInTheMulliganAsync()
    {
        ILocator confirm = Page.GetByTestId("mulligan-confirm");

        // The Weather is held on screen before the mulligan opens, and the table accepts nothing while a beat
        // is held.
        await Expect(confirm).ToBeEnabledAsync(new() { Timeout = MatchTimeout });
        await Expect(MarkableHandCards.First).ToBeVisibleAsync(new() { Timeout = MatchTimeout });

        ILocator card     = MarkableHandCards.First;
        string   instance = await card.GetAttributeAsync("data-instance") ?? "";

        await card.ClickAsync();

        await Expect(Page.Locator($"[data-testid='hand-card'][data-instance='{instance}']"))
            .ToHaveAttributeAsync("data-marked", "true", new() { Timeout = MatchTimeout });
        await Expect(Page.GetByTestId("mulligan-selected-count")).ToContainTextAsync("1");
        await Expect(confirm).ToContainTextAsync("Replace 1");

        await confirm.ClickAsync();
        await Expect(Page.GetByTestId("mulligan-overlay")).Not.ToBeVisibleAsync(new() { Timeout = MatchTimeout });

        return instance;
    }

    /// <summary>
    /// Take one legal action if the board is offering one, driving off the rendered highlights rather than
    /// off any knowledge of the rules — which is the point: the highlights are what a player has.
    /// </summary>
    protected Task<bool> TakeOneActionAsync() => TakeOneActionOnAsync(Page);

    /// <summary> The same action, on a named page: what the two-seat suites drive each board with. </summary>
    protected async Task<bool> TakeOneActionOnAsync(IPage page)
    {
        if (await page.GetByTestId("peek-overlay").IsVisibleAsync())
        {
            await TryClickAsync(page.GetByTestId("peek-confirm"));
            return true;
        }

        // Attack with a ready critter before playing more: the board is what wins the game.
        //
        // Two presses, because an attack picks its target by hand: the first selects the attacker and
        // highlights what it may hit, the second sends the attack. The Den first — the game is won at the Den
        // — and a body only when a Guard closes it.
        if (await SelectableCrittersOn(page).CountAsync() > 0)
        {
            await TryClickAsync(SelectableCrittersOn(page).First);

            if (await TargetableDensOn(page).CountAsync() > 0)
            {
                if (!await TryClickAsync(TargetableDensOn(page).First))
                    await page.Keyboard.PressAsync("Escape");

                return true;
            }

            if (await TargetableCrittersOn(page).CountAsync() > 0)
            {
                if (!await TryClickAsync(TargetableCrittersOn(page).First))
                    await page.Keyboard.PressAsync("Escape");

                return true;
            }

            // Nothing to swing at after all: drop the selection rather than leaving it live.
            await page.Keyboard.PressAsync("Escape");
        }

        if (await PlayableHandCardsOn(page).CountAsync() > 0)
        {
            await TryClickAsync(PlayableHandCardsOn(page).First);

            // Every targeted card opens the cancel surface. Healing deliberately has no prose banner.
            if (await page.GetByTestId("targeting-layer").IsVisibleAsync())
            {
                bool aimed = false;

                if (await TargetableCrittersOn(page).CountAsync() > 0)
                    aimed = await TryClickAsync(TargetableCrittersOn(page).First);
                else if (await TargetableDensOn(page).CountAsync() > 0)
                    aimed = await TryClickAsync(TargetableDensOn(page).First);

                if (!aimed)
                    await page.Keyboard.PressAsync("Escape");

                await Expect(page.GetByTestId("targeting-layer")).Not.ToBeVisibleAsync(new() { Timeout = MatchTimeout });
            }

            return true;
        }

        return false;
    }

    /// <summary>
    /// Click something on a board that is being played by two sides at once, tolerating the target going away
    /// underneath the click.
    /// <para>
    /// Choosing a target takes two presses, and between them the opponent's step can land and change what is
    /// legal — so the highlight this was aiming at is detached and the press has nothing to hit. That is the
    /// board behaving correctly. Playwright's default is to retry for thirty seconds and then throw, which
    /// turns an ordinary race into a dead fixture; a short timeout and a <c>false</c> lets the caller drop the
    /// selection and take the next thing the board is offering.
    /// </para>
    /// <para>
    /// <b>Both exception types, and the second one is the one that actually happens.</b> A click that runs out
    /// of time raises <see cref="TimeoutException"/> — the framework's, not a Playwright type — so a catch of
    /// <see cref="PlaywrightException"/> alone never engaged and the tolerance this method exists for was
    /// dead code. The symptom was an intermittently red fixture with "element was detached from the DOM" in
    /// its log, which is the exact race the paragraph above describes as correct behaviour.
    /// </para>
    /// </summary>
    protected async Task<bool> TryClickAsync(ILocator target, int timeoutMs = 2000)
    {
        try
        {
            await target.ClickAsync(new LocatorClickOptions { Timeout = timeoutMs });
            return true;
        }
        catch (Exception ex) when (ex is PlaywrightException or TimeoutException)
        {
            return false;
        }
    }

    /// <summary>
    /// Play this seat's whole turn and end it.
    /// <para>
    /// A held peek is answered <em>before</em> the End-turn guard, and that order is the whole method. A peek
    /// is exclusive — the only intent the table accepts while one is outstanding is the choice itself — so
    /// End turn is disabled, and a guard that returned on a disabled End turn would leave the question
    /// unanswered forever. The outer loop then sees a board that is still waiting on this seat, calls back
    /// in, and returns immediately again: the fixture spins, the table waits, and the game never reaches a
    /// result.
    /// </para>
    /// </summary>
    protected Task PlayOneTurnAsync() => PlayOneTurnOnAsync(Page);

    /// <summary> The same turn, on a named page. </summary>
    protected async Task PlayOneTurnOnAsync(IPage page)
    {
        for (int action = 0; action < 20; action++)
        {
            if (await page.GetByTestId("peek-overlay").IsVisibleAsync())
            {
                // Keeping nothing is a legal answer, so the press needs no selection — but the confirm is
                // held while the board is animating or a beat is up, exactly as the mulligan's is, so wait
                // for it to be live rather than pressing into a refusal.
                ILocator peekConfirm = page.GetByTestId("peek-confirm");
                await Expect(peekConfirm).ToBeEnabledAsync(new() { Timeout = MatchTimeout });
                await peekConfirm.ClickAsync();
                await Expect(page.GetByTestId("peek-overlay")).Not.ToBeVisibleAsync(new() { Timeout = MatchTimeout });
                continue;
            }

            if (!await EndTurnOn(page).IsEnabledAsync())
                return;

            // Off a board that has caught up, so what it offers is this position's answer and not the last
            // one's. Whatever the previous action resolved into, the mana on screen still buys what it says.
            await WaitUntilCaughtUpOnAsync(page);
            await AssertAffordableCardsAreOfferedOnAsync(page);

            if (!await TakeOneActionOnAsync(page))
                break;

            // One intent at a time: the input locks until the server confirms or refuses.
            await page.WaitForTimeoutAsync(120);
        }

        if (await EndTurnOn(page).IsEnabledAsync())
            await EndTurnOn(page).ClickAsync();
    }

    /// <summary> Whether the game is over, by the one thing that says so. </summary>
    protected Task<bool> IsGameOverAsync() => IsGameOverOnAsync(Page);

    /// <summary>
    /// The same question on a named page, and it asks about <b>both</b> terminal screens: a match that put
    /// ranks at stake ends on the Heist screen instead of the plain result panel, so a fixture that watched
    /// only for the panel would wait out a game that had already finished.
    /// </summary>
    protected static async Task<bool> IsGameOverOnAsync(IPage page)
        => await MatchResultOn(page).IsVisibleAsync() || await page.GetByTestId("heist-screen").IsVisibleAsync();

    /// <summary> The board slots a seat has, which is the one thing besides mana that refuses a critter. </summary>
    const int MaxBoardCritters = 6;

    /// <summary> This seat's unspent mana, off the acorn row's own value rather than out of its prose. </summary>
    protected Task<int> ManaAsync() => ManaOnAsync(Page);

    protected static async Task<int> ManaOnAsync(IPage page)
        => int.Parse(await page.GetByTestId("mana-row").GetAttributeAsync("data-mana") ?? "0");

    /// <summary> How many affordable cards this fixture has checked, so a vacuous run is visible in the log. </summary>
    protected int AffordableCardsChecked;

    /// <summary>
    /// Every card the mana on screen can pay for is still on offer. Asked whenever the seat is on turn with
    /// the board caught up — which is precisely the moment a stale legal set is observable.
    /// <para>
    /// A play is refused for three things and no others: the mana, a full board, and a target the card asked
    /// for and did not get (<c>Legality.CanPlayCard</c>). So a critter that asks for no target, costs no more
    /// than the unspent mana and has room on the board is legal, and a board that is not offering it is
    /// showing an answer computed for a position it has since left.
    /// </para>
    /// <para>
    /// That is what a legal set cached across an answered peek looks like from the outside: the answer moves
    /// neither the phase nor the hand, so the empty set the pause produced would be served for the rest of the
    /// action window and the whole hand would go dark with the mana to play it.
    /// </para>
    /// </summary>
    protected Task AssertAffordableCardsAreOfferedAsync() => AssertAffordableCardsAreOfferedOnAsync(Page);

    /// <summary> The same check on a named page. </summary>
    protected async Task AssertAffordableCardsAreOfferedOnAsync(IPage page)
    {
        // End turn is live with nothing in flight, no beat held and no server choice pending. A local target
        // selection may still be active; that intentionally makes the rest of the hand inert until aimed.
        if (await EndTurnOn(page).CountAsync() == 0 || !await EndTurnOn(page).IsEnabledAsync())
            return;

        if (await page.GetByTestId("targeting-layer").IsVisibleAsync())
            return;

        if (await MyCrittersOn(page).CountAsync() >= MaxBoardCritters)
            return;

        int mana = await ManaOnAsync(page);

        foreach (ILocator card in await PlainCritterCardsOn(page).AllAsync())
        {
            int cost = int.Parse(await card.GetAttributeAsync("data-cost") ?? "99");
            if (cost > mana)
                continue;

            string cardId = await card.GetAttributeAsync("data-card-id") ?? "?";
            Assert.That(await card.GetAttributeAsync("data-playable"), Is.EqualTo("true"),
                $"{cardId} costs {cost}, {mana} mana is unspent and the board has room, but it is not offered; "
                + $"hand={await page.GetByTestId("hand").GetAttributeAsync("class")}, "
                + $"targeting={await page.GetByTestId("targeting-layer").CountAsync()}, "
                + $"trailing={await TurnIndicatorOn(page).GetAttributeAsync("data-trailing")}, "
                + $"endTurn={await EndTurnOn(page).IsEnabledAsync()}");

            AffordableCardsChecked++;
        }
    }

    /// <summary> Wait until it is this client's turn, or the game is over, or the board has gone away. </summary>
    protected Task<bool> WaitForOwnTurnAsync(int timeoutMs) => WaitForOwnTurnOnAsync(Page, timeoutMs);

    /// <summary> The same wait on a named page. </summary>
    protected static async Task<bool> WaitForOwnTurnOnAsync(IPage page, int timeoutMs)
    {
        int waited = 0;
        while (waited < timeoutMs)
        {
            if (await IsGameOverOnAsync(page))
                return false;

            // The board can leave the screen — the session ended, or the player navigated — and asking a
            // missing control whether it is enabled throws rather than answering.
            if (!await page.GetByTestId("match-board").IsVisibleAsync())
                return false;

            // The mulligan and a held peek are both "the table is waiting on this seat", and neither leaves
            // End turn enabled: a peek is exclusive, so the only intent the table accepts is the choice.
            if (await page.GetByTestId("mulligan-overlay").IsVisibleAsync())
                return true;

            if (await page.GetByTestId("peek-overlay").IsVisibleAsync())
                return true;

            if (await EndTurnOn(page).CountAsync() > 0 && await EndTurnOn(page).IsEnabledAsync())
                return true;

            await page.WaitForTimeoutAsync(200);
            waited += 200;
        }

        return false;
    }

    /// <summary>
    /// Wait until the board has caught up with the server: no beat running and none queued.
    /// <para>
    /// Any count read off a <em>trailing</em> board is a count from the past: the model is already at the
    /// server's position while the presented board is still playing beats towards it. Nothing about the table
    /// gates this — the table can be perfectly idle and the board still behind — which is why it is asked of
    /// the board rather than waited out.
    /// </para>
    /// </summary>
    protected Task WaitUntilCaughtUpAsync(int timeoutMs = 10000) => WaitUntilCaughtUpOnAsync(Page, timeoutMs);

    /// <summary> The same wait on a named page. </summary>
    protected Task WaitUntilCaughtUpOnAsync(IPage page, int timeoutMs = 10000)
        => Expect(TurnIndicatorOn(page)).ToHaveAttributeAsync("data-trailing", "false", new() { Timeout = timeoutMs });

    /// <summary>
    /// The turn the board is on. Read off the indicator's <c>data-turn</c> rather than its copy, because the
    /// copy is deliberately empty while the board trails — the turn indicator is withheld then, and a test
    /// that parsed the prose would read a trailing board as turn zero.
    /// </summary>
    protected async Task<int> BoardTurnAsync()
        => int.Parse(await TurnIndicator.GetAttributeAsync("data-turn") ?? "0");

    // ---------------------------------------------------------------- two seats at one table

    /// <summary>
    /// One account, named, on Home. Two browser contexts are two localStorage-backed credential blobs, so
    /// each opens as its own guest account — the same isolation a second physical device gets, and the only
    /// way to seat two humans at one table. Naming them is what lets a fixture assert that the two accounts it
    /// meant reached one table rather than inferring it from two random numbers happening to differ.
    /// </summary>
    /// <param name="query">
    /// Extra query on Home, for the screen's own development-only affordances (<c>dev=shield</c>). Dropped by
    /// every in-app link, so each entry that wants one asks for it.
    /// </param>
    protected async Task PrepareNamedAccountAsync(IPage page, string name, string query = "")
    {
        await page.GotoAsync(query.Length > 0 ? $"{BaseUrl}/?{query}" : BaseUrl);
        await Expect(page.GetByTestId("display-name")).ToBeVisibleAsync(new() { Timeout = BootTimeout });

        await page.GetByTestId("display-name-edit").ClickAsync();
        await Expect(page.GetByTestId("display-name-input")).ToBeVisibleAsync();
        await page.GetByTestId("display-name-input").FillAsync(name);
        await page.GetByTestId("save-name").ClickAsync();
        await Expect(page.GetByTestId("display-name")).ToHaveTextAsync(name);
    }

    /// <summary> Tap Ranked and see the wait go up, which happens on the tap rather than on an answer. </summary>
    protected async Task TapRankedAsync(IPage page)
    {
        ILocator ranked = page.GetByTestId("queue-ranked");
        await Expect(ranked).ToBeEnabledAsync(new() { Timeout = BootTimeout });
        await ranked.ClickAsync();
        await Expect(page.GetByTestId("searching-dialog")).ToBeVisibleAsync(new() { Timeout = BootTimeout });
    }

    /// <summary>
    /// Answer the mulligan on one of two human seats, keeping the dealt hand.
    /// <para>
    /// <b>The overlay does not go away when this seat answers, and that is the point of having a helper of its
    /// own.</b> The table leaves the mulligan only when both seats have answered or the shared deadline lapses,
    /// so a seat that has answered waits — the scrim stays, every control that would act is gone, and the panel says so. Waiting
    /// for the overlay to clear here, the way the practice helper can (its opponent is a bot that answers in
    /// the same breath), would instead wait out the shared deadline and then find the other seat's confirm
    /// already gone.
    /// </para>
    /// </summary>
    protected async Task ResolveMulliganOnAsync(IPage page)
    {
        ILocator confirm = page.GetByTestId("mulligan-confirm");
        await Expect(confirm).ToBeEnabledAsync(new() { Timeout = MatchTimeout });
        await confirm.ClickAsync();

        await Expect(page.GetByTestId("mulligan-waiting")).ToBeVisibleAsync(new() { Timeout = MatchTimeout });
    }

    /// <summary> Both seats answer, and only then does the table leave the mulligan on either board. </summary>
    protected async Task ResolveBothMulligansAsync(IPage a, IPage b)
    {
        await ResolveMulliganOnAsync(a);
        await ResolveMulliganOnAsync(b);

        await Expect(a.GetByTestId("mulligan-overlay")).Not.ToBeVisibleAsync(new() { Timeout = MatchTimeout });
        await Expect(b.GetByTestId("mulligan-overlay")).Not.ToBeVisibleAsync(new() { Timeout = MatchTimeout });
    }

    /// <summary> The instance identities in this seat's hand, in hand order. </summary>
    protected async Task<string[]> HandInstancesAsync()
    {
        IReadOnlyList<ILocator> cards = await HandCards.AllAsync();
        List<string> instances = new List<string>(cards.Count);

        foreach (ILocator card in cards)
            instances.Add(await card.GetAttributeAsync("data-instance") ?? "");

        return instances.ToArray();
    }
}
