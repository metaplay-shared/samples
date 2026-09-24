using Microsoft.Playwright;

namespace Game.Client.Tests;

/// <summary>
/// <b>The Heist, end to end: two accounts, one table, and a rank that moves between two collections.</b> This
/// is the Heist's own proof statement, and the only place the
/// whole chain is exercised at once — the queue, the pairing, the played-out game, the phase, the pick, the
/// loser-first delivery and the two independent applications.
/// <para>
/// <b>These cannot run in parallel</b>, with each other or with anything else, for <c>MatchmakingTests</c>'
/// reason: there is one global queue, so two taps from two fixtures inside one fill wait are pooled into a
/// single match with two humans in it.
/// </para>
/// <para>
/// <b>Two accounts have to be prepared before they queue, and it is not optional.</b> Both are fresh, and a
/// fresh account is inside the newcomer shield — which a shield on <em>either</em> seat extends to the whole
/// match, so two fresh accounts pair at a tier that moves no ranks and no Heist can ever run. Each one
/// therefore lifts its own shield through Home's development-only affordance. And every card a fresh account
/// holds sits at the rank floor, where a decrement does nothing at all, so each account grows a handful of the
/// cards its deck actually plays: without that the loser's side of the transfer is unobservable by
/// construction.
/// </para>
/// </summary>
[TestFixture]
[NonParallelizable]
public class HeistTests : MatchTestBase
{
    /// <summary>
    /// <b>This suite owns its game server</b>, for the reason <see cref="MatchStrikeTests"/> does: it needs two
    /// timings nothing else may have. The shared profile's retry interval is thirty seconds and its pick clock
    /// forty-five, which makes "the winner took longer than the retry" a two-minute fixture and the deadline's
    /// own lapse a three-minute one — and both are cases the transfer was silently lost in, so they have to be
    /// cheap enough to run every time.
    /// <para>
    /// Five seconds and thirty are chosen against each other: the retry has to fire, and be seen not to deliver,
    /// well inside a pick clock that still leaves room for a fixture to read a lineup before it uses it.
    /// </para>
    /// </summary>
    static readonly string[] ServerTimings = { "Match:ResultRetryInterval=00:00:05", "Match:HeistPickDeadline=00:00:30" };

    /// <summary> How long a pick clock runs on this suite's own server. </summary>
    const int PickClockMs = 30000;

    /// <summary> Long enough for a lapse plus the delivery that follows it. </summary>
    const int LapseTimeout = 60000;

    GameServerProcess? _server;

    [OneTimeSetUp]
    public async Task StartServer() => _server = await GameServerProcess.StartAsync(ServerTimings);

    [OneTimeTearDown]
    public async Task StopServer()
    {
        if (_server != null)
            await _server.DisposeAsync();

        _server = null;
    }

    /// <summary> Long enough for the end-to-end profile's own fill wait plus a formation and two cold boards. </summary>
    const int QueueTimeout = 45000;

    /// <summary>
    /// <b>The whole of <c>Fire &amp; Foam</c></b>, which is the deck a fresh account's picker opens on — so every
    /// card either seat can play is above the rank floor and the loser's side of the transfer is observable
    /// whatever the game does.
    /// <para>
    /// All twenty-five rather than a likely handful, and the reason is a fixture that failed: <em>which</em> of
    /// a deck's cards a real game reaches is not something a test chooses, and a lineup of six cards that
    /// happened to miss every grown one leaves the decrement unobservable — a rank at the floor cannot fall.
    /// The list is content the fixture already depends on by using the default picker, so it is named here
    /// rather than inferred.
    /// </para>
    /// </summary>
    static readonly string[] GrownCards =
    {
        "EmberKit", "Foxfire", "FlameDancer", "SizzleWhisker", "CinderStorm",
        "NineTailMatriarch", "PebbleCollector", "TideScholar", "Slipstream", "BubbleDrifter",
        "Undertow", "Riptide", "BerrySnack", "BusyBeaver", "FieldNotes",
        "GreyOwl", "HillPony", "MeadowMouse", "MooseWanderer", "OldBadger",
        "PondFrog", "PricklyHedgehog", "StrayGoat", "TrailRabbit", "WiseTortoise",
    };

    /// <summary> The rank the grown cards are raised to: high enough to lock, and to have a rank to lose. </summary>
    const int GrownRank = 3;

    /// <summary> This suite's own delivery retry interval, in milliseconds. See <see cref="ServerTimings"/>. </summary>
    const int RetryIntervalMs = 5000;

    /// <summary>
    /// What each account locks before it queues, and <b>the two lists are deliberately different</b>: both
    /// accounts play the same deck, so a card one of them froze is a card the other will play, and that is the
    /// only way the winner's own frozen set — the second subtraction, the owner's game-rule call — is
    /// reachable from a live table at all. With one shared list the loser's own lock removed every candidate
    /// first and the case never arose.
    /// <para>
    /// A locked card is never pickable, whichever side locked it, and that is the invariant this suite pins
    /// live; that a padlocked row is <em>drawn</em> rather than quietly missing, and worded for the side
    /// reading it, is pinned deterministically by the board preview — whether a given card gets played is not
    /// a fixture's to decide.
    /// </para>
    /// </summary>
    static readonly string[][] LockedCards =
    {
        new[] { "MeadowMouse", "EmberKit" },
        new[] { "TrailRabbit", "BusyBeaver" },
    };

    /// <summary>
    /// The whole loop, in one game: two accounts queue, meet, play a real match out, and the winner takes a
    /// rank off a card the loser played — with the loser's locked card in the lineup and unpickable, the
    /// transfer spelled out on both sides before the commit, both collections moved afterwards, and both
    /// accounts released back into the queue.
    /// <para>
    /// <b>Both boards are driven, and that is what makes the pick deterministic.</b> Which seat wins is a fact
    /// about a real game rather than something a fixture can choose, so instead of hoping one seat wins, both
    /// are played and the pick is made on whichever board is asking for one. The alternative the spec drew —
    /// the loser leaves and the survivor plays on against a bot — leaves who wins to chance, and a fixture
    /// whose assertions vary run to run proves the property only on some runs.
    /// </para>
    /// </summary>
    [Test]
    public async Task QueuePlayWinStealRebuildRequeue()
    {
        await using IBrowserContext contextA = await Browser.NewContextAsync();
        await using IBrowserContext contextB = await Browser.NewContextAsync();

        IPage a = await contextA.NewPageAsync();
        IPage b = await contextB.NewPageAsync();

        await PrepareForStakesAsync(a, "Grackle", locks: 0);
        await PrepareForStakesAsync(b, "Hazelnut", locks: 1);

        await TapRankedAsync(a);
        await TapRankedAsync(b);

        await Expect(a.GetByTestId("match-board")).ToBeVisibleAsync(new() { Timeout = QueueTimeout });
        await Expect(b.GetByTestId("match-board")).ToBeVisibleAsync(new() { Timeout = QueueTimeout });

        // Ranks are on the table: both shields are lifted, so the gap decides — and two decks grown the same
        // way are an even matchup, which is the tier that pays one pick.
        ILocator reveal = a.GetByTestId("pre-match-reveal");
        await Expect(reveal).ToBeVisibleAsync();
        Assert.That(await reveal.GetAttributeAsync("data-tier"), Is.EqualTo("Even"),
            "two waived shields and two decks grown alike is an even matchup; a Shielded tier here means a waiver did not land");

        await ResolveBothMulligansAsync(a, b);
        await PlayBothToTheEndAsync(a, b);

        // One of the two boards is asking for a pick, and it is the winner's.
        IPage winner = await FindPickingPageAsync(a, b);
        IPage loser  = winner == a ? b : a;

        ILocator screen = winner.GetByTestId("heist-screen");
        await Expect(screen).ToHaveAttributeAsync("data-stage", "Picking");
        await Expect(screen).ToHaveAttributeAsync("data-picks-owed", "1", new() { Timeout = MatchTimeout });

        // The loser's own screen is the same record with nothing asked of it.
        await Expect(loser.GetByTestId("heist-screen")).ToBeVisibleAsync(new() { Timeout = MatchTimeout });
        await Expect(loser.GetByTestId("heist-waiting")).ToBeVisibleAsync();
        await Expect(loser.GetByTestId("heist-snatch")).ToHaveCountAsync(0);

        // The lineup is the game the winner just watched, and it accounts for all of it: every card the loser
        // played is a row, pickable or not.
        int drawn = int.Parse(await winner.GetByTestId("heist-loot").GetAttributeAsync("data-count") ?? "0");
        Assert.That(drawn, Is.GreaterThan(0), "the loser played something, so the lineup has rows");
        Assert.That(await winner.GetByTestId("heist-pick").CountAsync() + await winner.GetByTestId("heist-loot-card").CountAsync(),
            Is.EqualTo(drawn));

        // A locked card is never on the menu, whichever side locked it — and a row that is not pickable is
        // padlocked and says whose padlock it is, rather than being inert for no stated reason.
        await AssertNoLockedCardIsPickableAsync(winner);

        // A pickable card the loser had grown, so its rank has somewhere to fall.
        ILocator pick = await FindGrownPickAsync(winner);
        string picked = await pick.GetAttributeAsync("data-card-id") ?? "?";

        // <b>And a deliberate pause first, past the delivery's own retry interval.</b> The result is recorded
        // when the game decides and the retry stamp used to fire against it whatever the phase was — so a
        // winner who took longer than one interval had a record with no picks on it delivered to both
        // accounts, both acknowledged, and the pick that landed afterwards could never be delivered at all.
        // The pause is what makes this fixture fail if the delivery ever stops waiting for the phase.
        await winner.WaitForTimeoutAsync(RetryIntervalMs * 2);

        await pick.ClickAsync();

        // The transfer, in words and in numbers, on both sides before anything is committed.
        ILocator mine   = winner.Locator("[data-testid='heist-transfer'][data-side='mine']");
        ILocator theirs = winner.Locator("[data-testid='heist-transfer'][data-side='theirs']");
        await Expect(mine).ToBeVisibleAsync();
        await Expect(theirs).ToBeVisibleAsync();

        int winnerBefore = int.Parse(await mine.GetAttributeAsync("data-from") ?? "0");
        int winnerAfter  = int.Parse(await mine.GetAttributeAsync("data-to") ?? "0");
        Assert.That(winnerAfter, Is.EqualTo(winnerBefore + 1), "the winner's own copy gains exactly one rank");
        Assert.That(await theirs.GetAttributeAsync("data-from"), Is.EqualTo(GrownRank.ToString()));
        Assert.That(await theirs.GetAttributeAsync("data-to"), Is.EqualTo((GrownRank - 1).ToString()));

        await winner.GetByTestId("heist-snatch").ClickAsync();

        // Both screens reach the same record, and Play again goes live only once both accounts have folded the
        // outcome in — which is the loser-first delivery observed from outside.
        foreach (IPage page in new[] { winner, loser })
        {
            await Expect(page.GetByTestId("heist-screen")).ToHaveAttributeAsync("data-picks-taken", "1", new() { Timeout = GameTimeout });
            await Expect(page.GetByTestId("play-again")).ToBeEnabledAsync(new() { Timeout = GameTimeout });
            await Expect(page.GetByTestId("heist-auto-defaulted")).ToHaveCountAsync(0);
        }

        // And BOTH are told what moved, which is the point of the screen: the loser is asked for nothing here
        // and can never select a tile, so without this the only thing their screen said about the card was the
        // rank it was frozen at.
        ILocator theirsSettled = winner.Locator("[data-testid='heist-transfer'][data-side='theirs']");
        await Expect(theirsSettled).ToBeVisibleAsync();
        Assert.That(await theirsSettled.GetAttributeAsync("data-to"), Is.EqualTo((GrownRank - 1).ToString()));
        await Expect(winner.GetByTestId("heist-yours-now")).ToHaveAttributeAsync("data-rank", winnerAfter.ToString());

        ILocator mineSettled = loser.Locator("[data-testid='heist-transfer'][data-side='mine']");
        await Expect(mineSettled).ToBeVisibleAsync();
        await Expect(mineSettled).ToContainTextAsync("Your copy");
        Assert.That(await mineSettled.GetAttributeAsync("data-from"), Is.EqualTo(GrownRank.ToString()));
        Assert.That(await mineSettled.GetAttributeAsync("data-to"), Is.EqualTo((GrownRank - 1).ToString()));
        await Expect(loser.Locator("[data-testid='heist-transfer'][data-side='theirs']")).ToHaveCountAsync(0);

        // Play again searches from where the player is standing: the dialog goes up OVER the finished screen
        // rather than in place of it, which is the whole of why the board carries a second mount of it. The
        // cancel puts the account back where it was — a cancel is deliberate, so it leaves no notice.
        await winner.GetByTestId("play-again").ClickAsync();
        await Expect(winner.GetByTestId("searching-dialog")).ToBeVisibleAsync(new() { Timeout = BootTimeout });
        await Expect(winner.GetByTestId("heist-screen")).ToBeVisibleAsync();
        await winner.GetByTestId("searching-cancel").ClickAsync();
        await Expect(winner.GetByTestId("searching-dialog")).Not.ToBeVisibleAsync(new() { Timeout = BootTimeout });
        await Expect(winner.GetByTestId("search-ended-notice")).ToHaveCountAsync(0);

        // And the collections moved, in opposite directions, on the one card that was picked. Waited on rather
        // than read once: the account's own action is enqueued by the delivery handler and the ack is sent
        // without waiting for it to run, so "recorded" and "applied" are two instants and a fixture that read
        // the collection at the first of them would be racing the second.
        await AssertRankOnCollectionAsync(winner, picked, winnerAfter, "the winner's collection is what the screen said it would be");
        await AssertRankOnCollectionAsync(loser, picked, GrownRank - 1, "and the loser's is down one rank on the same card");

        // The pointer was cleared with the acknowledgement, so both accounts are back in the queue's reach.
        foreach (IPage page in new[] { winner, loser })
        {
            await page.GotoAsync(BaseUrl);
            await Expect(page.GetByTestId("queue-ranked")).ToBeEnabledAsync(new() { Timeout = BootTimeout });
        }

        TestContext.Out.WriteLine($"picked {picked}: winner {winnerBefore}→{winnerAfter}, loser {GrownRank}→{GrownRank - 1}");
    }

    /// <summary>
    /// <b>A winner who is not there still takes the pick.</b> Both players leave straight after the mulligan,
    /// which covers both seats and makes the table play the rest of the game out at once — so the winner is
    /// absent by construction and the phase is resolved by the deterministic default rather than by a clock.
    /// <para>
    /// It needs no shortened deadline and no forced stamp: an absent winner has every slot defaulted at phase
    /// entry, because a clock for somebody who is not there is time the <em>loser's</em> collection spends.
    /// </para>
    /// <para>
    /// The default takes the highest-ranked card on the menu, which is what makes this assertable: the grown
    /// cards are at rank three and everything else is at the floor, so whatever is taken is one of them and
    /// the loser's side of the transfer has somewhere to fall.
    /// </para>
    /// </summary>
    [Test]
    public async Task ADeadWinnerStillTakesTheDefault()
    {
        await using IBrowserContext contextA = await Browser.NewContextAsync();
        await using IBrowserContext contextB = await Browser.NewContextAsync();

        IPage a = await contextA.NewPageAsync();
        IPage b = await contextB.NewPageAsync();

        await PrepareForStakesAsync(a, "Ibisbill", locks: -1);
        await PrepareForStakesAsync(b, "Juniper", locks: -1);

        await TapRankedAsync(a);
        await TapRankedAsync(b);

        await Expect(a.GetByTestId("match-board")).ToBeVisibleAsync(new() { Timeout = QueueTimeout });
        await Expect(b.GetByTestId("match-board")).ToBeVisibleAsync(new() { Timeout = QueueTimeout });

        await ResolveBothMulligansAsync(a, b);

        // Leaving is a disconnect that skips grace. With both seats covered the table has lost every human, so
        // it plays the game out at once — and the seat that wins is a covered one, which is the whole case.
        await a.GetByTestId("leave-match").ClickAsync();
        await b.GetByTestId("leave-match").ClickAsync();

        await Expect(a.GetByTestId("queue-ranked")).ToBeEnabledAsync(new() { Timeout = GameTimeout });
        await Expect(b.GetByTestId("queue-ranked")).ToBeEnabledAsync(new() { Timeout = GameTimeout });

        (string card, int moved) onA = OneMove(GrownRanks(), await GrownRanksNowAsync(a), "the first account");
        (string card, int moved) onB = OneMove(GrownRanks(), await GrownRanksNowAsync(b), "the second account");

        Assert.That(onA.card, Is.EqualTo(onB.card), "one pick, so both collections moved on the same card");
        Assert.That(onA.moved, Is.EqualTo(-onB.moved), "and by equal and opposite amounts: one rank, moved rather than minted");
        Assert.That(Math.Abs(onA.moved), Is.EqualTo(1));

        TestContext.Out.WriteLine($"the default took {onA.card}: {onA.moved:+#;-#;0} on one account, {onB.moved:+#;-#;0} on the other");
    }

    /// <summary>
    /// <b>The clock takes the pick, and the ranks still move.</b> The winner is at the table and simply does
    /// not choose; the deadline lapses, the deterministic default is applied, and both collections move.
    /// <para>
    /// This is the case that was unconditionally broken and silent: the retry stamp fired against a record with
    /// no picks on it, both accounts acknowledged, and the pick the lapse then took reached neither of them —
    /// while both screens said a rank had moved. Nothing logged anything. The lapse is always later than the
    /// retry, so no human slowness was needed at all.
    /// </para>
    /// </summary>
    [Test]
    public async Task ALapsedPickClockStillMovesBothRanks()
    {
        await using IBrowserContext contextA = await Browser.NewContextAsync();
        await using IBrowserContext contextB = await Browser.NewContextAsync();

        IPage a = await contextA.NewPageAsync();
        IPage b = await contextB.NewPageAsync();

        await PrepareForStakesAsync(a, "Kestrel", locks: -1);
        await PrepareForStakesAsync(b, "Larkspur", locks: -1);

        await TapRankedAsync(a);
        await TapRankedAsync(b);

        await Expect(a.GetByTestId("match-board")).ToBeVisibleAsync(new() { Timeout = QueueTimeout });
        await Expect(b.GetByTestId("match-board")).ToBeVisibleAsync(new() { Timeout = QueueTimeout });

        await ResolveBothMulligansAsync(a, b);
        await PlayBothToTheEndAsync(a, b);

        IPage winner = await FindPickingPageAsync(a, b);
        IPage loser  = winner == a ? b : a;

        // The winner's own clock is running and they touch nothing. Everything below is what the table does
        // for a player who is present and silent.
        await Expect(winner.GetByTestId("heist-snatch")).ToBeDisabledAsync();

        await Expect(winner.GetByTestId("heist-screen"))
            .ToHaveAttributeAsync("data-picks-taken", "1", new() { Timeout = PickClockMs + LapseTimeout });

        foreach (IPage page in new[] { winner, loser })
        {
            await Expect(page.GetByTestId("heist-auto-defaulted")).ToBeVisibleAsync(new() { Timeout = LapseTimeout });
            await Expect(page.GetByTestId("play-again")).ToBeEnabledAsync(new() { Timeout = LapseTimeout });
        }

        // The default takes the highest-ranked card on the menu, and every card either deck holds is grown —
        // so whatever it took, the loser had a rank to lose and the winner one to gain.
        (string card, int moved) onLoser  = OneMove(GrownRanks(), await GrownRanksNowAsync(loser), "the loser");
        (string card, int moved) onWinner = OneMove(GrownRanks(), await GrownRanksNowAsync(winner), "the winner");

        Assert.That(onLoser.card, Is.EqualTo(onWinner.card), "one pick, so both collections moved on the same card");
        Assert.That(onLoser.moved, Is.EqualTo(-1));
        Assert.That(onWinner.moved, Is.EqualTo(1));

        TestContext.Out.WriteLine($"the clock took {onLoser.card}: {GrownRank} → {GrownRank - 1} on one account, "
            + $"{GrownRank} → {GrownRank + 1} on the other");
    }

    /// <summary>
    /// <b>A winner who leaves mid-pick pays anyway.</b> Leaving during the Heist takes no grace and covers no
    /// seat — the game is decided, so there is no turn to hold — and the picks that seat still owed are
    /// defaulted at once rather than waiting out a clock nobody is watching.
    /// <para>
    /// It presses the screen's own Leave. Closing the tab is not the same event — the SDK session survives a
    /// dropped socket, so the clock would resolve the phase instead, which is a different arm of the same
    /// file — and the board chrome's pill is not reachable at all: every overlay in this game covers it.
    /// </para>
    /// </summary>
    [Test]
    public async Task AWinnerWhoLeavesMidPickStillPays()
    {
        await using IBrowserContext contextA = await Browser.NewContextAsync();
        await using IBrowserContext contextB = await Browser.NewContextAsync();

        IPage a = await contextA.NewPageAsync();
        IPage b = await contextB.NewPageAsync();

        await PrepareForStakesAsync(a, "Mistletoe", locks: -1);
        await PrepareForStakesAsync(b, "Nettle", locks: -1);

        await TapRankedAsync(a);
        await TapRankedAsync(b);

        await Expect(a.GetByTestId("match-board")).ToBeVisibleAsync(new() { Timeout = QueueTimeout });
        await Expect(b.GetByTestId("match-board")).ToBeVisibleAsync(new() { Timeout = QueueTimeout });

        await ResolveBothMulligansAsync(a, b);
        await PlayBothToTheEndAsync(a, b);

        IPage winner = await FindPickingPageAsync(a, b);
        IPage loser  = winner == a ? b : a;

        // The screen's own way out, not the board chrome's: the chrome sits at z-index 35 and every overlay in
        // this game covers it, so while the Heist is up the only reachable exit is the one the screen offers.
        await winner.GetByTestId("heist-screen").GetByTestId("leave-match").ClickAsync();

        // The loser is told at once rather than in thirty seconds' time: the departure resolves the phase.
        await Expect(loser.GetByTestId("heist-screen"))
            .ToHaveAttributeAsync("data-picks-taken", "1", new() { Timeout = LapseTimeout });
        await Expect(loser.GetByTestId("heist-auto-defaulted")).ToBeVisibleAsync();

        (string card, int moved) onLoser  = OneMove(GrownRanks(), await GrownRanksNowAsync(loser), "the loser");
        (string card, int moved) onWinner = OneMove(GrownRanks(), await GrownRanksNowAsync(winner), "the winner who left");

        Assert.That(onLoser.card, Is.EqualTo(onWinner.card));
        Assert.That(onLoser.moved, Is.EqualTo(-1));
        Assert.That(onWinner.moved, Is.EqualTo(1), "leaving costs exactly what staying would, in both directions");
    }

    // ---------------------------------------------------------------- preparing an account for real stakes

    /// <summary>
    /// One named account with its shield lifted, a handful of its deck's cards grown, and — unless a fixture
    /// says otherwise — one of them locked. Both halves are the only routes the build has to the two rules
    /// this suite is about: the stakes tier and a rank that can fall.
    /// </summary>
    /// <param name="locks">Which of <see cref="LockedCards"/>' two lists to freeze, or -1 to freeze nothing.</param>
    async Task PrepareForStakesAsync(IPage page, string name, int locks)
    {
        // <b>The order here is load-bearing.</b> A page load ends the session, and a player action that has
        // not flushed when its session ends is lost — so the collection work happens first and is proven to
        // have reached the server by a reload, and the shield waiver comes last, on the same page the Ranked
        // tap is made from. Waived and enqueued on one timeline, the server applies them in that order and the
        // ticket is frozen with the shield already lifted.
        await page.GotoAsync($"{BaseUrl}/collection?dev=ranks");
        await Expect(page.GetByTestId("collection-grid")).ToBeVisibleAsync(new() { Timeout = BootTimeout });

        foreach (string cardId in GrownCards)
            await SetCardRankAsync(cardId, GrownRank, page);

        if (locks >= 0)
        {
            foreach (string cardId in LockedCards[locks])
            {
                await ToggleCardLockAsync(cardId, page);
                await Expect(Card(cardId, page)).ToHaveAttributeAsync("data-locked", "true");
            }
        }

        // The reload is the flush's own proof: the model comes back from the account rather than from the tab,
        // so a rank that reads three here is a rank the server holds.
        await page.ReloadAsync();
        await Expect(page.GetByTestId("collection-grid")).ToBeVisibleAsync(new() { Timeout = BootTimeout });

        foreach (string cardId in GrownCards)
            await Expect(Card(cardId, page)).ToHaveAttributeAsync("data-rank", GrownRank.ToString(), new() { Timeout = BootTimeout });

        if (locks >= 0)
        {
            foreach (string cardId in LockedCards[locks])
                await Expect(Card(cardId, page)).ToHaveAttributeAsync("data-locked", "true");
        }

        await PrepareNamedAccountAsync(page, name, query: "dev=shield");

        // From the next queue entry onwards, which is the one the caller is about to make.
        await page.GetByTestId("dev-waive-shield").ClickAsync();
    }

    /// <summary> What the grown cards are held at once <see cref="PrepareForStakesAsync"/> has run. </summary>
    static Dictionary<string, int> GrownRanks()
    {
        Dictionary<string, int> ranks = new Dictionary<string, int>();
        foreach (string cardId in GrownCards)
            ranks[cardId] = GrownRank;

        return ranks;
    }

    /// <summary> What this account holds the grown cards at now, off the collection screen's own tiles. </summary>
    async Task<Dictionary<string, int>> GrownRanksNowAsync(IPage page)
    {
        await page.GotoAsync($"{BaseUrl}/collection");
        await Expect(page.GetByTestId("collection-grid")).ToBeVisibleAsync(new() { Timeout = BootTimeout });

        Dictionary<string, int> ranks = new Dictionary<string, int>();
        foreach (string cardId in GrownCards)
            ranks[cardId] = int.Parse(await Card(cardId, page).GetAttributeAsync("data-rank") ?? "0");

        return ranks;
    }

    /// <summary> One card's rank on one account, off the collection the player is given back. </summary>
    async Task AssertRankOnCollectionAsync(IPage page, string cardId, int rank, string because)
    {
        await page.GotoAsync($"{BaseUrl}/collection");
        await Expect(page.GetByTestId("collection-grid")).ToBeVisibleAsync(new() { Timeout = BootTimeout });

        try
        {
            await Expect(Card(cardId, page)).ToHaveAttributeAsync("data-rank", rank.ToString(), new() { Timeout = BootTimeout });
        }
        catch (Exception)
        {
            Assert.Fail($"{because}: {cardId} reads rank "
                + $"{await Card(cardId, page).GetAttributeAsync("data-rank")} rather than {rank}");
        }
    }

    /// <summary> The one card that moved between two readings, or a failure naming what it found instead. </summary>
    static (string Card, int Moved) OneMove(Dictionary<string, int> before, Dictionary<string, int> after, string whose)
    {
        List<(string Card, int Moved)> moves = new List<(string, int)>();
        foreach ((string card, int was) in before)
        {
            if (after[card] != was)
                moves.Add((card, after[card] - was));
        }

        Assert.That(moves.Count, Is.EqualTo(1),
            $"{whose} should have exactly one card moved by one pick, and had {moves.Count}: "
            + string.Join(", ", moves.ConvertAll(move => $"{move.Card} {move.Moved:+#;-#;0}")));

        return moves[0];
    }

    /// <summary>
    /// No row a pick may land on is a card either side had locked, and every row that is <em>not</em> pickable
    /// is padlocked with a stated owner. The second half is what stops "inert" from meaning "for no reason a
    /// player can see": at this point in the phase nothing has been taken yet, so a lock is the only thing
    /// that can hold a played card back.
    /// </summary>
    async Task AssertNoLockedCardIsPickableAsync(IPage winner)
    {
        foreach (ILocator row in await winner.GetByTestId("heist-pick").AllAsync())
        {
            string cardId = await row.GetAttributeAsync("data-card-id") ?? "?";

            // Neither side's frozen set may be on the menu. The winner's is the half that pays nothing — a
            // pick of it would cost the loser a rank and gain the winner none — and it is off the menu rather
            // than on it as a trap.
            Assert.That(Array.IndexOf(LockedCards[0], cardId), Is.LessThan(0), $"{cardId} was locked before the queue and must not be pickable");
            Assert.That(Array.IndexOf(LockedCards[1], cardId), Is.LessThan(0), $"{cardId} was locked before the queue and must not be pickable");
            await Expect(row).ToHaveAttributeAsync("data-locked-by", "Nobody");
        }

        List<string> padlocked = new List<string>();
        foreach (ILocator row in await winner.GetByTestId("heist-loot-card").AllAsync())
        {
            string cardId = await row.GetAttributeAsync("data-card-id") ?? "?";
            string by     = await row.GetAttributeAsync("data-locked-by") ?? "?";

            Assert.That(await row.GetAttributeAsync("data-taken"), Is.EqualTo("false"), "nothing has been taken yet");
            Assert.That(by, Is.Not.EqualTo("Nobody"), $"{cardId} is on the lineup but not pickable, and nothing says why");
            await Expect(row.GetByTestId("lock-mark")).ToBeVisibleAsync();

            padlocked.Add($"{cardId} ({by})");
        }

        TestContext.Out.WriteLine(padlocked.Count == 0
            ? "no locked card reached the lineup this game"
            : $"padlocked rows on the lineup: {string.Join(", ", padlocked)}");
    }

    // ---------------------------------------------------------------- driving two boards

    /// <summary>
    /// Play the game out, taking each seat's turn on the page that owns it. Neither page is the fixture's own
    /// <c>Page</c>: a two-seat table needs the board vocabulary pointed at a named page, which is why
    /// <see cref="MatchTestBase"/> defines it that way.
    /// </summary>
    async Task PlayBothToTheEndAsync(IPage a, IPage b)
    {
        for (int turn = 0; turn < 80; turn++)
        {
            if (await IsGameOverOnAsync(a) || await IsGameOverOnAsync(b))
                return;

            foreach (IPage page in new[] { a, b })
            {
                if (await IsGameOverOnAsync(page))
                    return;

                if (!await WaitForOwnTurnOnAsync(page, 3000))
                    continue;

                await PlayOneTurnOnAsync(page);
            }
        }

        TestContext.Out.WriteLine("the game did not finish inside eighty rounds of driving both seats");
    }

    /// <summary> Whichever of the two boards is asking its player for a pick. </summary>
    async Task<IPage> FindPickingPageAsync(IPage a, IPage b)
    {
        for (int waited = 0; waited < GameTimeout; waited += 250)
        {
            foreach (IPage page in new[] { a, b })
            {
                ILocator screen = page.GetByTestId("heist-screen");
                if (await screen.CountAsync() > 0 && await screen.GetAttributeAsync("data-stage") == "Picking")
                    return page;
            }

            await a.WaitForTimeoutAsync(250);
        }

        Assert.Fail("neither board reached the Heist's picking stage; "
            + $"A={await StageOfAsync(a)}, B={await StageOfAsync(b)}");
        return a;
    }

    static async Task<string> StageOfAsync(IPage page)
    {
        ILocator screen = page.GetByTestId("heist-screen");
        if (await screen.CountAsync() == 0)
            return await MatchResultOn(page).CountAsync() > 0 ? "the plain result panel" : "no terminal screen";

        return await screen.GetAttributeAsync("data-stage") ?? "?";
    }

    /// <summary>
    /// A pickable lineup row the loser had grown. The rank is on the tile — a played card's rank is public the
    /// moment it is played — so the fixture reads which rows are worth taking rather than assuming.
    /// </summary>
    async Task<ILocator> FindGrownPickAsync(IPage winner)
    {
        IReadOnlyList<ILocator> pickable = await winner.GetByTestId("heist-pick").AllAsync();
        List<string> seen = new List<string>();

        foreach (ILocator row in pickable)
        {
            string cardId = await row.GetAttributeAsync("data-card-id") ?? "?";
            int    rank   = int.Parse(await row.Locator("[data-testid='heist-loot-tile']").GetAttributeAsync("data-rank") ?? "0");

            seen.Add($"{cardId} r{rank}");

            if (rank == GrownRank)
                return row;
        }

        Assert.Fail($"the loser played none of the grown cards, so no pick can take a rank off them: lineup was {string.Join(", ", seen)}");
        return pickable[0];
    }
}
