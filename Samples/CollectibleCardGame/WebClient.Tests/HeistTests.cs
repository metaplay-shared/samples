using Microsoft.Playwright;

namespace WebClient.Tests;

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
/// holds sits at the rank floor, where a decrement does nothing at all, so each account grows its collection
/// through the collection's development-only rank control: without that the loser's side of the transfer is
/// unobservable by construction.
/// </para>
/// </summary>
[TestFixture]
[NonParallelizable]
public class HeistTests : MatchTestBase
{
    /// <summary>
    /// <b>This suite owns its game server</b>, because it needs two timings nothing else may have. The shared profile's retry interval is thirty seconds and its pick clock
    /// forty-five, which would make "the winner took longer than the retry" a two-minute fixture.
    /// <para>
    /// Two seconds and thirty are chosen against each other: the retry has to fire, and be seen not to deliver,
    /// well inside a pick clock that still leaves room for a fixture to read a lineup before it uses it.
    /// </para>
    /// </summary>
    static readonly string[] ServerTimings = { "Match:ResultRetryInterval=00:00:02", "Match:HeistPickDeadline=00:00:30" };

    static GameServerProcess? _server;

    [OneTimeSetUp]
    public static async Task StartServer() => _server = await GameServerProcess.StartAsync(ServerTimings);

    [OneTimeTearDown]
    public static async Task StopServer()
    {
        if (_server != null)
            await _server.DisposeAsync();

        _server = null;
    }

    /// <summary> Long enough for the end-to-end profile's own fill wait plus a formation and two cold boards. </summary>
    const int QueueTimeout = 45000;

    /// <summary>
    /// <b>The whole of <c>Fire &amp; Foam</c></b>, which is the deck a fresh account's picker opens on — the
    /// cards either seat can play, and so the cards whose ranks the fixture reads back.
    /// <para>
    /// Preparation grows every card the account owns, so all twenty-five are above the rank floor: <em>which</em>
    /// of a deck's cards a real game reaches is not something a test chooses, and a lineup that missed every
    /// grown card would leave the decrement unobservable — a rank at the floor cannot fall. The list is content
    /// the fixture already depends on by using the default picker, so it is named here rather than inferred.
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

    /// <summary> The rank the collection is raised to: high enough to lock, and to have a rank to lose. </summary>
    const int GrownRank = 3;

    /// <summary> This suite's own delivery retry interval, in milliseconds. See <see cref="ServerTimings"/>. </summary>
    const int RetryIntervalMs = 2000;

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
        IPage a = await NewPageAsync();
        IPage b = await NewPageAsync();

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
        // when the game decides, and its delivery waits for the phase: a record delivered with no picks on it
        // and acknowledged by both accounts would leave the pick that lands afterwards undeliverable. The pause
        // is what makes this fixture fail if the delivery ever stops waiting for the phase.
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
            await page.GotoAsync(ClientUrl());
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
    /// The default takes the highest-ranked card on the menu, which is what makes this assertable: every card
    /// either seat can play is at rank three, so whatever is taken has somewhere to fall on the loser's side.
    /// </para>
    /// </summary>
    [Test]
    public async Task ADeadWinnerStillTakesTheDefault()
    {
        IPage a = await NewPageAsync();
        IPage b = await NewPageAsync();

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

    // ---------------------------------------------------------------- preparing an account for real stakes

    /// <summary>
    /// One named account with its shield lifted, its collection grown, and — unless a fixture says otherwise —
    /// two of its cards locked. Both halves are the only routes the build has to the two rules this suite is
    /// about: the stakes tier and a rank that can fall.
    /// </summary>
    /// <param name="locks">Which of <see cref="LockedCards"/>' two lists to freeze, or -1 to freeze nothing.</param>
    async Task PrepareForStakesAsync(IPage page, string name, int locks)
    {
        // <b>The order here is load-bearing.</b> A page load ends the session, and a player action that has
        // not flushed when its session ends is lost — so the collection work happens first and is proven to
        // have reached the server by a reload, and the shield waiver comes last, on the same page the Ranked
        // tap is made from. Waived and enqueued on one timeline, the server applies them in that order and the
        // ticket is frozen with the shield already lifted.
        await page.GotoAsync(ClientUrl("/collection", "dev=ranks"));
        await Expect(page.GetByTestId("collection-grid")).ToBeVisibleAsync(new() { Timeout = BootTimeout });

        // Every owned card in one action. The lock fixtures grow cards one at a time through the card
        // detail's control; this suite needs the whole deck and asserts nothing about the control itself.
        await page.Locator($"[data-testid='dev-set-all-ranks'][data-rank='{GrownRank}']").ClickAsync();
        foreach (string cardId in GrownCards)
            await Expect(Card(cardId, page)).ToHaveAttributeAsync("data-rank", GrownRank.ToString());

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
        await page.GotoAsync(ClientUrl("/collection"));
        await Expect(page.GetByTestId("collection-grid")).ToBeVisibleAsync(new() { Timeout = BootTimeout });

        Dictionary<string, int> ranks = new Dictionary<string, int>();
        foreach (string cardId in GrownCards)
            ranks[cardId] = int.Parse(await Card(cardId, page).GetAttributeAsync("data-rank") ?? "0");

        return ranks;
    }

    /// <summary> One card's rank on one account, off the collection the player is given back. </summary>
    async Task AssertRankOnCollectionAsync(IPage page, string cardId, int rank, string because)
    {
        await page.GotoAsync(ClientUrl("/collection"));
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
