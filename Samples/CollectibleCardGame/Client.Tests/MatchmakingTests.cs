using Microsoft.Playwright;

namespace Game.Client.Tests;

/// <summary>
/// The ranked queue, end to end. <b>These cannot run in parallel</b>, with each other or with anything else:
/// there is one global queue, so two taps from two fixtures inside one fill wait are pooled into one match
/// with two humans in it — which is the product behaving correctly and every "the opponent is a bot"
/// assertion failing at once. Test isolation here is a property of the runner rather than of the browser
/// contexts (<c>Docs/matchmaking.md</c>, "Testing").
/// <para>
/// There is deliberately <b>no end-to-end test of band separation</b>. Proving that a rating or Power Score
/// gap keeps two players apart needs two contrived accounts and a real wall-clock wait, and it would be a
/// slow, flaky restatement of something <c>MatchmakingPolicyTests</c> asserts exactly. The bands are
/// unit-tested; end to end only proves that a decision reaches the right seats.
/// </para>
/// </summary>
[TestFixture]
[NonParallelizable]
public class MatchmakingTests : MatchTestBase
{
    /// <summary>
    /// Long enough to cover the end-to-end profile's own fill wait plus a formation and a cold board. The
    /// profile shortens the wait to eight seconds precisely so the solo case is cheap
    /// (<c>Backend/Server/Config/Options.e2e.yaml</c>).
    /// </summary>
    const int QueueTimeout = 45000;

    /// <summary>
    /// One player alone is seated against a labelled bot at practice stakes, and the queue's honest worst case
    /// is therefore a game rather than a wait. This is the fill wait observed from outside.
    /// </summary>
    [Test]
    public async Task ASoloTapMeetsALabelledBot()
    {
        await QueueForRankedAsync(Page);

        await Expect(Page.GetByTestId("match-board")).ToBeVisibleAsync(new() { Timeout = QueueTimeout });

        // Practice stakes: rating moves, ranks never do. The chip is the board's own one bit about
        // what is on the table, and it reads the same word a Practice-mode game does — which is exactly true.
        ILocator chip = Page.GetByTestId("stakes-chip");
        await Expect(chip).ToHaveAttributeAsync("data-tier", "Practice", new() { Timeout = MatchTimeout });
        await Expect(chip).ToContainTextAsync("PRACTICE");

        // Labelled, not disguised: the opponent's plaque carries the computer-player mark.
        await Expect(Page.Locator("[data-testid='den-enemy'] [data-testid='den-bot-mark']"))
            .ToBeVisibleAsync(new() { Timeout = MatchTimeout });

        // And the search is over: the dialog came down because there is a board, not because it was told.
        await Expect(Page.GetByTestId("searching-dialog")).ToHaveCountAsync(0);
    }

    /// <summary>
    /// A cancelled search leaves the player where they were, with the entry live again. The dialog dismisses
    /// on the server's answer rather than on the tap, because a cancel that loses the race to a seat
    /// reservation is refused and a board appears instead.
    /// </summary>
    [Test]
    public async Task ACancelledSearchLeavesThePlayerOnHome()
    {
        await QueueForRankedAsync(Page);

        ILocator cancel = Page.GetByTestId("searching-cancel");
        await Expect(cancel).ToBeVisibleAsync(new() { Timeout = BootTimeout });
        await cancel.ClickAsync();

        await Expect(Page.GetByTestId("searching-dialog")).Not.ToBeVisibleAsync(new() { Timeout = BootTimeout });
        await Expect(Page.GetByTestId("display-name")).ToBeVisibleAsync();

        // A cancel is something the player did on purpose, so it leaves no notice to read.
        await Expect(Page.GetByTestId("search-ended-notice")).ToHaveCountAsync(0);

        // And the entry is live again rather than stuck behind a search that never ended.
        await Expect(Page.GetByTestId("queue-ranked")).ToBeEnabledAsync(new() { Timeout = BootTimeout });

        // No board turned up in the meantime: the cancel won its race, which at this point in the fill wait it
        // always does.
        await Expect(Page.GetByTestId("match-board")).ToHaveCountAsync(0);
    }

    /// <summary>
    /// Home to the ranked queue on one page. No deck to build: a fresh account's picker opens on the first
    /// starter deck. The dialog goes up on the tap, before the server has answered anything.
    /// </summary>
    async Task QueueForRankedAsync(IPage page)
    {
        await page.GotoAsync(BaseUrl);
        await Expect(page.GetByTestId("display-name")).ToBeVisibleAsync(new() { Timeout = BootTimeout });

        ILocator ranked = page.GetByTestId("queue-ranked");
        await Expect(ranked).ToBeEnabledAsync(new() { Timeout = BootTimeout });
        await ranked.ClickAsync();

        await Expect(page.GetByTestId("searching-dialog")).ToBeVisibleAsync(new() { Timeout = BootTimeout });
    }

    // ---------------------------------------------------------------- two humans at one table

    /// <summary>
    /// Two browsers tapping Ranked inside one fill wait are seated <b>against each other</b>, see the same
    /// Weather, and see each other's Power Score and the stakes tier it implies.
    /// <para>
    /// This is also where the harness's own assumption is checked: two browser contexts are two
    /// localStorage-backed credential blobs, so each opens as a distinct guest account — the same isolation a
    /// second physical device gets. Nothing else in the suite needs two accounts at once, and every
    /// case below rests on it, so the fixture renames both and asserts the two names reached one table rather
    /// than assuming it.
    /// </para>
    /// </summary>
    [Test]
    public async Task TwoBrowsersMeet()
    {
        await using IBrowserContext contextA = await Browser.NewContextAsync();
        await using IBrowserContext contextB = await Browser.NewContextAsync();

        IPage a = await contextA.NewPageAsync();
        IPage b = await contextB.NewPageAsync();

        await PrepareNamedAccountAsync(a, "Alpaca");
        await PrepareNamedAccountAsync(b, "Bramble");

        // Both taps land inside one fill wait, which is the whole reason both contexts drive one dev server.
        await TapRankedAsync(a);
        await TapRankedAsync(b);

        await Expect(a.GetByTestId("match-board")).ToBeVisibleAsync(new() { Timeout = QueueTimeout });
        await Expect(b.GetByTestId("match-board")).ToBeVisibleAsync(new() { Timeout = QueueTimeout });

        ILocator revealA = a.GetByTestId("pre-match-reveal");
        ILocator revealB = b.GetByTestId("pre-match-reveal");
        await Expect(revealA).ToBeVisibleAsync();
        await Expect(revealB).ToBeVisibleAsync();

        // One table: both seats are the two accounts, and each of them sees both names.
        foreach (ILocator reveal in new[] { revealA, revealB })
        {
            await Expect(reveal.GetByTestId("reveal-name").Nth(0)).Not.ToBeEmptyAsync();
            await Expect(reveal.GetByTestId("reveal-name").Nth(1)).Not.ToBeEmptyAsync();
        }

        string[] namesOnA = await NamesAsync(a);
        string[] namesOnB = await NamesAsync(b);

        Assert.That(namesOnA, Is.EquivalentTo(new[] { "Alpaca", "Bramble" }),
            "two browser contexts should be two accounts, seated at one table");
        Assert.That(namesOnB, Is.EquivalentTo(namesOnA), "and both clients render the same roster");

        // The same Weather, off the banner's own value rather than its wording.
        string weatherA = await a.GetByTestId("weather-banner").GetAttributeAsync("data-weather") ?? "?";
        string weatherB = await b.GetByTestId("weather-banner").GetAttributeAsync("data-weather") ?? "!";
        Assert.That(weatherA, Is.EqualTo(weatherB));
        Assert.That(weatherA, Is.Not.Empty);

        // Neither is playing a computer opponent, and both know it.
        await Expect(a.GetByTestId("reveal-bot-label")).ToHaveCountAsync(0);
        await Expect(b.GetByTestId("reveal-bot-label")).ToHaveCountAsync(0);

        // Each sees the OTHER's Power Score: the record is one object and both clients render all of it.
        Assert.That(await PowerScoresAsync(a), Is.EqualTo(await PowerScoresAsync(b)),
            "both seats' Power Scores are public from the pre-match screen onward");

        // And the same tier, which is one shared field frozen at formation rather than a per-viewer verdict.
        string tierA = await revealA.GetAttributeAsync("data-tier") ?? "?";
        string tierB = await revealB.GetAttributeAsync("data-tier") ?? "!";
        Assert.That(tierA, Is.EqualTo(tierB));

        // Two fresh accounts are both inside the newcomer shield, so the tier is the one that says no rank
        // moves in either direction — which is also the honest thing to show two players on their first game.
        Assert.That(tierA, Is.EqualTo("Shielded"));
        await Expect(a.GetByTestId("stakes-chip")).ToHaveAttributeAsync("data-tier", "Shielded");

        // The board says RANKED rather than PRACTICE: this game is for keeps even though the shield is on.
        await Expect(a.GetByTestId("stakes-chip")).ToContainTextAsync("RANKED");
    }

    /// <summary>
    /// <b>Two accounts at one table</b>, a real result, and both of
    /// them told — which is unobservable without the queue, because every table that could otherwise form had one
    /// human on it and the delivery order was pinned by pure tests alone.
    /// <para>
    /// The drive is the cheap one: both players leave, which covers both seats and makes the table play itself
    /// out immediately to a real result rather than tearing itself down — the design pillar that leaving costs
    /// exactly what staying would.
    /// </para>
    /// <para>
    /// <b>It asserts conservation, not the order</b>, and it is named for what it asserts: the two accounts move
    /// by equal and opposite amounts, which is the property loser-first exists to protect — no rating gained
    /// that was not lost — but winner-first delivery would satisfy it too. The <em>order</em> is only visible in
    /// the server log, and is checked by reading it; owning a second server
    /// process here the way <c>MatchAbandonTests</c> does would buy one assertion at the cost of this suite's
    /// ability to share the one the rest of it needs.
    /// </para>
    /// </summary>
    [Test]
    public async Task BothAccountsRecordTheGameAndTheRatingsCancel()
    {
        await using IBrowserContext contextA = await Browser.NewContextAsync();
        await using IBrowserContext contextB = await Browser.NewContextAsync();

        IPage a = await contextA.NewPageAsync();
        IPage b = await contextB.NewPageAsync();

        await PrepareNamedAccountAsync(a, "Cobweb");
        await PrepareNamedAccountAsync(b, "Dandelion");

        int    ratingBeforeA = await RatingAsync(a);
        int    ratingBeforeB = await RatingAsync(b);
        string recordBeforeA = await RecordAsync(a);
        string recordBeforeB = await RecordAsync(b);

        await TapRankedAsync(a);
        await TapRankedAsync(b);

        await Expect(a.GetByTestId("match-board")).ToBeVisibleAsync(new() { Timeout = QueueTimeout });
        await Expect(b.GetByTestId("match-board")).ToBeVisibleAsync(new() { Timeout = QueueTimeout });

        // Both answer the mulligan, so the game is genuinely under way rather than sitting in the deal.
        await ResolveBothMulligansAsync(a, b);

        // Leaving is a disconnect that skips grace. With both seats covered the table has lost every human,
        // so it plays the rest of the game out at once and delivers a real result to both accounts.
        await a.GetByTestId("leave-match").ClickAsync();
        await b.GetByTestId("leave-match").ClickAsync();

        await Expect(a.GetByTestId("display-name")).ToBeVisibleAsync(new() { Timeout = GameTimeout });
        await Expect(b.GetByTestId("display-name")).ToBeVisibleAsync(new() { Timeout = GameTimeout });

        // Both accounts are released, which only happens once the table has delivered and each of them has
        // folded the outcome in.
        await Expect(a.GetByTestId("queue-ranked")).ToBeEnabledAsync(new() { Timeout = GameTimeout });
        await Expect(b.GetByTestId("queue-ranked")).ToBeEnabledAsync(new() { Timeout = GameTimeout });

        // Both records moved, which is the client seeing the result its own account folded in.
        await Expect(a.GetByTestId("record-summary")).Not.ToHaveTextAsync(recordBeforeA, new() { Timeout = GameTimeout });
        await Expect(b.GetByTestId("record-summary")).Not.ToHaveTextAsync(recordBeforeB, new() { Timeout = GameTimeout });

        string recordA = await RecordAsync(a);
        string recordB = await RecordAsync(b);

        // The two results are the two halves of one game. Both Dens falling in the same resolution is a draw,
        // which is a legal outcome of a played-out table — so the pair is asserted as a pair rather than as
        // "these two differ", which a draw makes false.

        bool decided = (await a.GetByTestId("ranked-wins").InnerTextAsync() == "1"
                        && await b.GetByTestId("ranked-losses").InnerTextAsync() == "1")
                       || (await b.GetByTestId("ranked-wins").InnerTextAsync() == "1"
                           && await a.GetByTestId("ranked-losses").InnerTextAsync() == "1");
        bool drawn = await a.GetByTestId("ranked-draws").InnerTextAsync() == "1"
                     && await b.GetByTestId("ranked-draws").InnerTextAsync() == "1";

        Assert.That(decided || drawn, Is.True,
            $"one game, two accounts: either one won and one lost, or both drew. A={recordA}, B={recordB}");

        // The conservation the loser-first barrier exists to protect: what one side gained the other lost, so
        // nothing was minted out of nothing. A draw between two evenly rated accounts moves neither, which is
        // the same statement with both sides at zero.
        int movedA = await RatingAsync(a) - ratingBeforeA;
        int movedB = await RatingAsync(b) - ratingBeforeB;

        Assert.That(movedA, Is.EqualTo(-movedB), $"the two moves should cancel: {movedA} against {movedB}");
        Assert.That(decided ? movedA != 0 : true, Is.True, "a decided ranked result moves rating");
    }

    /// <summary>
    /// A reclaim with a <b>real opponent</b> on the other side rather than a bot: one of two humans reloads
    /// mid-match — a full session teardown — and gets its board, its hand and its history back while the other
    /// human is still at the table with a live board of their own.
    /// </summary>
    [Test]
    public async Task AReclaimAgainstARealOpponent()
    {
        await using IBrowserContext contextA = await Browser.NewContextAsync();
        await using IBrowserContext contextB = await Browser.NewContextAsync();

        IPage a = await contextA.NewPageAsync();
        IPage b = await contextB.NewPageAsync();

        await PrepareNamedAccountAsync(a, "Ermine");
        await PrepareNamedAccountAsync(b, "Fernwick");

        await TapRankedAsync(a);
        await TapRankedAsync(b);

        await Expect(a.GetByTestId("match-board")).ToBeVisibleAsync(new() { Timeout = QueueTimeout });
        await Expect(b.GetByTestId("match-board")).ToBeVisibleAsync(new() { Timeout = QueueTimeout });

        await ResolveBothMulligansAsync(a, b);

        await Expect(a.GetByTestId("hand-card").First).ToBeVisibleAsync(new() { Timeout = MatchTimeout });

        // A full reload: the SDK's resume fails, the shell starts a new session, and the player actor
        // re-associates from the account's own match pointer. None of that is client code this test drives.
        await a.ReloadAsync();

        await Expect(a.GetByTestId("match-board")).ToBeVisibleAsync(new() { Timeout = MatchTimeout });
        await Expect(a.GetByTestId("hand-card").First).ToBeVisibleAsync(new() { Timeout = MatchTimeout });

        Assert.That(await a.GetByTestId("hand-card").CountAsync(), Is.GreaterThan(0),
            "the hand came back with the subscribe");
        Assert.That(await a.GetByTestId("event-line").CountAsync(), Is.GreaterThan(0),
            "and the history came back with the board");

        // The mulligan is behind this seat: a board that had restarted from the deal would be asking for one
        // again. That, the hand and the history are what say the match resumed rather than began — a turn
        // number compared against itself is satisfied by a table that never moved at all.
        await Expect(a.GetByTestId("mulligan-overlay")).ToHaveCountAsync(0);

        // And the opponent never left: their board is the same table, still live, with the reclaiming seat
        // shown as present again rather than away.
        await Expect(b.GetByTestId("match-board")).ToBeVisibleAsync();
        await Expect(b.Locator("[data-testid='den-enemy'] [data-testid='den-away-mark']"))
            .ToHaveCountAsync(0, new() { Timeout = MatchTimeout });
        await Expect(b.Locator("[data-testid='den-enemy'] [data-testid='den-bot-mark']")).ToHaveCountAsync(0);
    }

    // ---------------------------------------------------------------- driving two pages

    // The two-context vocabulary — a named account, the Ranked tap, and the shared mulligan — is
    // MatchTestBase's, because the Heist's own two-seat fixtures need the same three things.

    /// <summary> Both names on the reveal, in seat order. </summary>
    async Task<string[]> NamesAsync(IPage page)
    {
        List<string> names = new List<string>();
        foreach (ILocator name in await page.GetByTestId("reveal-name").AllAsync())
            names.Add((await name.TextContentAsync() ?? "").Trim());

        return names.ToArray();
    }

    /// <summary> Both Power Scores on the reveal, in seat order, off their own values rather than the copy. </summary>
    async Task<string[]> PowerScoresAsync(IPage page)
    {
        List<string> scores = new List<string>();
        foreach (ILocator score in await page.GetByTestId("reveal-power-score").AllAsync())
            scores.Add(await score.GetAttributeAsync("data-power-score") ?? "?");

        return scores.ToArray();
    }

    /// <summary> Home's record line, as the player reads it. </summary>
    async Task<string> RecordAsync(IPage page)
    {
        ILocator record = page.GetByTestId("record-summary");
        await Expect(record).ToBeVisibleAsync(new() { Timeout = BootTimeout });

        return (await record.TextContentAsync() ?? "").Trim();
    }

    /// <summary> The account's rating, off Home's record line. </summary>
    async Task<int> RatingAsync(IPage page)
    {
        ILocator record = page.GetByTestId("record-summary");
        await Expect(record).ToBeVisibleAsync(new() { Timeout = BootTimeout });

        return int.Parse(await record.GetAttributeAsync("data-rating") ?? "0");
    }
}
