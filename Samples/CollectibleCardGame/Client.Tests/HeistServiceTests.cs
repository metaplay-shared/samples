using Game.Client.Services;
using Game.Logic;
using Metaplay.Core;

namespace Game.Client.Tests;

/// <summary>
/// Which screen a finished match lands on, what its lineup holds, and what a pick says it would do. No browser
/// and no server: the routing is the piece most likely to be got wrong — two tiers that pay nothing were
/// deliberately moved off the Heist screen and onto the plain result panel — and the transfer strings are the
/// screen's whole honesty claim, so both are asserted by running rather than by reading.
/// </summary>
[Parallelizable(ParallelScope.Self)]
[TestFixture]
public class HeistServiceTests
{
    static CardId Card(string id) => CardId.FromString(id);

    static HeistEligibleCard Row(string id, int rank) => new HeistEligibleCard(Card(id), rank);

    static List<HeistEligibleCard> Rows(params HeistEligibleCard[] rows) => new List<HeistEligibleCard>(rows);

    static List<CardId> Cards(params string[] ids)
    {
        List<CardId> cards = new List<CardId>();
        foreach (string id in ids)
            cards.Add(Card(id));
        return cards;
    }

    /// <summary> The one config value the transfer reads, without a config archive to read it from. </summary>
    static GlobalConfig Global => new GlobalConfig();

    static MatchOutcomeRecord Result(int winnerSeat, HeistResult? heist = null)
    {
        MatchOutcomeRecord record = new MatchOutcomeRecord(
            winnerSeat == 0 ? MatchOutcome.Seat0Wins : MatchOutcome.Seat1Wins,
            winnerSeat, finalTurn: 12, MatchEndCause.DenAtZero,
            new List<bool> { true, true }, wasRanked: true, decidedAt: MetaTime.Epoch);

        record.Heist = heist;
        return record;
    }

    static MatchStakes Ranked(StakesTier tier)
        => new MatchStakes(isRanked: true, tier, new List<int> { 60, 60 },
            new List<List<CardId>> { new List<CardId>(), new List<CardId>() });

    // ---------------------------------------------------------------- the routing, one fact

    [Test]
    public void TheHeistScreenIsShownWhileThePhaseIsRunning()
    {
        // Before any pick has landed there is nothing on the record to route on, so the phase is what says a
        // pick is being made — which is also what the screen needs the phase for anyway, to draw the clock.
        Assert.That(HeistPresentation.ShowsHeistScreen(MatchTablePhase.HeistPick, Result(0)), Is.True);
    }

    [Test]
    public void TheHeistScreenSurvivesThePhaseOnceSomethingWasTaken()
    {
        MatchOutcomeRecord took = Result(0, new HeistResult(Cards("EmberKit"), anyPickAutoDefaulted: false));

        Assert.That(HeistPresentation.ShowsHeistScreen(MatchTablePhase.Ended, took), Is.True,
            "the screen is where both players read what moved, which is after the phase rather than during it");
    }

    [Test]
    public void EveryMatchThatMovesNoRankLandsOnThePlainResult()
    {
        // The §4.1 decision table, from the client's side: a draw, an unranked game, a favourite's win, a
        // shielded match and a lineup with nothing pickable in it all leave the record's Heist null — and one
        // null is the whole of the routing, so the client does no tier reasoning and cannot disagree with the
        // server about whether a phase happened.
        Assert.That(HeistPresentation.ShowsHeistScreen(MatchTablePhase.Ended, Result(0)), Is.False);
        Assert.That(HeistPresentation.ShowsHeistScreen(MatchTablePhase.Ended, null), Is.False);

        MatchOutcomeRecord empty = Result(0, new HeistResult(new List<CardId>(), anyPickAutoDefaulted: false));
        Assert.That(HeistPresentation.ShowsHeistScreen(MatchTablePhase.Ended, empty), Is.False,
            "a record of nothing is not a Heist");
    }

    [Test]
    public void OnlyTheWinnerIsAskedForAnything()
    {
        MatchOutcomeRecord result = Result(winnerSeat: 1);

        Assert.That(HeistPresentation.Stage(MatchTablePhase.HeistPick, result, localSeat: 1, picksOwed: 1),
            Is.EqualTo(HeistStage.Picking));
        Assert.That(HeistPresentation.Stage(MatchTablePhase.HeistPick, result, localSeat: 0, picksOwed: 1),
            Is.EqualTo(HeistStage.Watching), "the loser owes nothing here at all");
        Assert.That(HeistPresentation.Stage(MatchTablePhase.Ended, result, localSeat: 1, picksOwed: 1),
            Is.EqualTo(HeistStage.Done));
    }

    [Test]
    public void AWinnerWithEverySlotFilledIsNoLongerPicking()
    {
        MatchOutcomeRecord result = Result(0, new HeistResult(Cards("EmberKit"), anyPickAutoDefaulted: false));

        Assert.That(HeistPresentation.Stage(MatchTablePhase.HeistPick, result, localSeat: 0, picksOwed: 2),
            Is.EqualTo(HeistStage.Picking), "the upset's second slot is still open");
        Assert.That(HeistPresentation.Stage(MatchTablePhase.HeistPick, result, localSeat: 0, picksOwed: 1),
            Is.EqualTo(HeistStage.Watching), "and with nothing owed there is nothing to ask");
    }

    // ---------------------------------------------------------------- the lineup

    [Test]
    public void TheLineupIsEveryCardTheLoserPlayed_LockedOnesIncluded()
    {
        // The locked row is IN the lineup rather than quietly missing: the lineup should be the game the winner
        // just watched, and a card that vanished from it would read as a bug rather than as the loser's
        // foresight. Which padlock is on it is public, so the screen can say whose.
        List<HeistEligibleCard> played   = Rows(Row("EmberKit", 3), Row("WarmBiscuit", 2), Row("GreyOwl", 4));
        List<HeistEligibleCard> eligible = Rows(Row("EmberKit", 3));

        List<HeistLootRow> rows = HeistPresentation.Lineup(
            played, eligible, picks: null,
            loserLocks: Cards("WarmBiscuit"),
            winnerLocks: Cards("GreyOwl"));

        Assert.That(rows.Count, Is.EqualTo(3));

        Assert.That(rows[0].Pickable, Is.True);
        Assert.That(rows[0].LockedBy, Is.EqualTo(HeistLockedBy.Nobody));

        Assert.That(rows[1].Pickable, Is.False);
        Assert.That(rows[1].LockedBy, Is.EqualTo(HeistLockedBy.TheLoser), "their padlock, their standing decision");

        Assert.That(rows[2].Pickable, Is.False);
        Assert.That(rows[2].LockedBy, Is.EqualTo(HeistLockedBy.TheWinner), "a lock runs both ways");
        Assert.That(rows[2].IsFrozen, Is.True);
    }

    [Test]
    public void APickTakesOneOccurrenceOffTheLineupAndMarksIt()
    {
        // A loser who played two copies offers two, so one pick leaves one — and the row that was taken says
        // so rather than looking like a card that was never on the menu.
        List<HeistEligibleCard> played = Rows(Row("EmberKit", 3), Row("EmberKit", 3));

        List<HeistLootRow> rows = HeistPresentation.Lineup(
            played, played, Cards("EmberKit"), loserLocks: null, winnerLocks: null);

        Assert.That(rows[0].Pickable, Is.True, "played order is kept, so the first occurrence is the one still on offer");
        Assert.That(rows[0].Taken, Is.False);
        Assert.That(rows[1].Pickable, Is.False);
        Assert.That(rows[1].Taken, Is.True);
    }

    [Test]
    public void ALineupWithNothingPlayedIsEmptyRatherThanAbsent()
    {
        Assert.That(HeistPresentation.Lineup(null, null, null, null, null), Is.Empty);
        Assert.That(HeistPresentation.Lineup(new List<HeistEligibleCard>(), null, null, null, null), Is.Empty);
    }

    // ---------------------------------------------------------------- the four preview strings

    [Test]
    public void TheWinnersPreviewSaysWhatItsOwnCopyWouldDo()
    {
        // The four cases the design names, each computed from this client's own collection state with no round
        // trip — which is what makes the screen able to state the payout before the player commits to it.
        Assert.That(HeistPresentation.MineLine(HeistPresentation.Mine(isWinner: true, owns: true, rank: 3, locked: false, Global)),
            Is.EqualTo("Your copy: rank 3 → 4."));

        Assert.That(HeistPresentation.MineLine(HeistPresentation.Mine(isWinner: true, owns: false, rank: 0, locked: false, Global)),
            Does.StartWith("NEW!"));

        Assert.That(HeistPresentation.MineLine(HeistPresentation.Mine(isWinner: true, owns: true, rank: Global.RankMax, locked: false, Global)),
            Does.Contain("nothing moves"));

        Assert.That(HeistPresentation.MineLine(HeistPresentation.Mine(isWinner: true, owns: true, rank: 3, locked: true, Global)),
            Does.Contain("frozen"));
    }

    [Test]
    public void TheLosersPreviewSaysWhatLeftTheirOwnCollection()
    {
        Assert.That(HeistPresentation.MineLine(HeistPresentation.Mine(isWinner: false, owns: true, rank: 3, locked: false, Global)),
            Is.EqualTo("Your copy: rank 3 → 2."));

        Assert.That(HeistPresentation.MineLine(HeistPresentation.Mine(isWinner: false, owns: true, rank: Global.RankMin, locked: false, Global)),
            Does.Contain("safe"));
    }

    [Test]
    public void TheWinnersScreenStatesTheLosersSideFromTheFrozenRank()
    {
        // The rank the lineup row carries, which is public the moment the card is played. The loser's screen
        // never states the winner's side, because the winner's collection is not theirs to see.
        Assert.That(HeistPresentation.TheirsLine(HeistPresentation.Theirs(frozenRank: 3, Global)),
            Is.EqualTo("Theirs: rank 3 → 2."));

        Assert.That(HeistPresentation.TheirsLine(HeistPresentation.Theirs(Global.RankMin, Global)),
            Does.Contain("safe"));
    }

    [Test]
    public void TheTierLineNamesTheWagerFromTheReadersOwnSide()
    {
        string evenWinner = HeistPresentation.TierLine(Ranked(StakesTier.Even), Result(0), isWinner: true);
        string evenLoser  = HeistPresentation.TierLine(Ranked(StakesTier.Even), Result(0), isWinner: false);

        Assert.That(evenWinner, Does.Contain("one rank"));
        Assert.That(evenLoser, Is.Not.EqualTo(evenWinner), "the same record, framed for whoever is reading it");

        // Favourite means seat 0 entered ahead, so seat 1 winning is the upset and takes two.
        Assert.That(HeistPresentation.TierLine(Ranked(StakesTier.Favourite), Result(1), isWinner: true),
            Does.Contain("double"));
    }
}
