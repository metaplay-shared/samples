using Game.Logic;
using System.Collections.Generic;

namespace Game.Client.Services;

/// <summary>
/// Which screen a finished match lands on, what the Heist lineup holds, and what a pick would do in words.
/// <para>
/// <b>Everything here is a function of state both clients already have.</b> The routing is one fact off the
/// outcome record, the lineup is the difference between two public lists, and the transfer is
/// <see cref="MatchHeistPolicy"/>'s own arithmetic asked about the reader's own collection — so the screen
/// states the payout without a round trip and without a second copy of the rule it is stating.
/// </para>
/// <para>
/// It is a pure file so that the routing and the four preview strings are tested by running rather than by
/// reading: the routing is the piece most likely to be got wrong, because two tiers that pay nothing were
/// deliberately moved off this screen and onto the plain result panel.
/// </para>
/// </summary>
public static class HeistPresentation
{
    /// <summary>
    /// Whether the Heist screen is the screen, rather than the plain result panel. Two facts, and neither is
    /// a tier: the phase is running, or it ran and took something.
    /// <para>
    /// <b>No tier reasoning on the client at all</b>, which is what makes it impossible for the two sides to
    /// disagree about whether a phase happened. A draw, an unranked game, a favourite's win, a shielded match
    /// and a lineup with nothing pickable in it all leave <c>Heist</c> null and land on the plain panel, which
    /// is where those two tiers' one sentence already lives.
    /// </para>
    /// </summary>
    public static bool ShowsHeistScreen(MatchTablePhase phase, MatchOutcomeRecord? result)
    {
        if (result == null)
            return false;

        return phase == MatchTablePhase.HeistPick
               || (result.Heist != null && result.Heist.Picks != null && result.Heist.Picks.Count > 0);
    }

    /// <summary> What this client is doing on the screen. </summary>
    public static HeistStage Stage(MatchTablePhase phase, MatchOutcomeRecord? result, int localSeat, int picksOwed)
    {
        if (result == null || phase != MatchTablePhase.HeistPick)
            return HeistStage.Done;

        int taken = result.Heist != null && result.Heist.Picks != null ? result.Heist.Picks.Count : 0;

        // Only the winner has anything to do, and only while a slot is still open. There is no stash step and
        // nothing is asked of the loser: whatever they protected, they protected before they queued.
        return localSeat == result.WinnerSeat && taken < picksOwed ? HeistStage.Picking : HeistStage.Watching;
    }

    /// <summary>
    /// The lineup: <b>every card the loser played this match</b>, in played order, with the locked ones in it
    /// rather than quietly missing.
    /// <para>
    /// That is the whole point of drawing the difference rather than the eligible list alone — the lineup
    /// should be the game the winner just watched, and cards that vanished from it would read as a bug rather
    /// than as the loser's foresight. Which padlock is on a row is public too, because both frozen sets are on
    /// the stakes record, so the screen can say <em>whose</em> lock takes a card off the menu.
    /// </para>
    /// <para>
    /// Pickable is decided <b>by occurrence</b>: a loser who played two copies offers two, and one pick takes
    /// one of them.
    /// </para>
    /// </summary>
    public static List<HeistLootRow> Lineup(
        IReadOnlyList<HeistEligibleCard>? played,
        IReadOnlyList<HeistEligibleCard>? eligible,
        IReadOnlyList<CardId>? picks,
        IReadOnlyList<CardId>? loserLocks,
        IReadOnlyList<CardId>? winnerLocks)
    {
        List<HeistLootRow> rows = new List<HeistLootRow>();
        if (played == null)
            return rows;

        Dictionary<CardId, int> stillOnTheMenu = Counts(MatchHeistPolicy.Remaining(eligible, picks));
        Dictionary<CardId, int> alreadyTaken   = Counts(picks);

        foreach (HeistEligibleCard row in played)
        {
            if (TakeOne(stillOnTheMenu, row.Card))
            {
                rows.Add(new HeistLootRow(row.Card, row.OwnedRank, pickable: true, taken: false, HeistLockedBy.Nobody));
                continue;
            }

            if (TakeOne(alreadyTaken, row.Card))
            {
                rows.Add(new HeistLootRow(row.Card, row.OwnedRank, pickable: false, taken: true, HeistLockedBy.Nobody));
                continue;
            }

            rows.Add(new HeistLootRow(row.Card, row.OwnedRank, pickable: false, taken: false, LockedBy(row.Card, loserLocks, winnerLocks)));
        }

        return rows;
    }

    static HeistLockedBy LockedBy(CardId card, IReadOnlyList<CardId>? loserLocks, IReadOnlyList<CardId>? winnerLocks)
    {
        if (Holds(loserLocks, card))
            return HeistLockedBy.TheLoser;

        // A card the winner froze is off the menu too, and for the same reason read from the other side: a
        // lock runs both ways, so taking it would cost the loser a rank and pay the winner nothing.
        return Holds(winnerLocks, card) ? HeistLockedBy.TheWinner : HeistLockedBy.Nobody;
    }

    static bool Holds(IReadOnlyList<CardId>? cards, CardId card)
    {
        if (cards == null)
            return false;

        foreach (CardId held in cards)
        {
            if (held == card)
                return true;
        }

        return false;
    }

    static Dictionary<CardId, int> Counts(IReadOnlyList<HeistEligibleCard>? rows)
    {
        Dictionary<CardId, int> counts = new Dictionary<CardId, int>();
        if (rows == null)
            return counts;

        foreach (HeistEligibleCard row in rows)
            counts[row.Card] = counts.TryGetValue(row.Card, out int seen) ? seen + 1 : 1;

        return counts;
    }

    static Dictionary<CardId, int> Counts(IReadOnlyList<CardId>? cards)
    {
        Dictionary<CardId, int> counts = new Dictionary<CardId, int>();
        if (cards == null)
            return counts;

        foreach (CardId card in cards)
            counts[card] = counts.TryGetValue(card, out int seen) ? seen + 1 : 1;

        return counts;
    }

    static bool TakeOne(Dictionary<CardId, int> counts, CardId? card)
    {
        if (card == null || !counts.TryGetValue(card, out int left) || left <= 0)
            return false;

        counts[card] = left - 1;
        return true;
    }

    // ---------------------------------------------------------------- the transfer, in words

    /// <summary>
    /// What taking this card would do to <b>the reader's own</b> copy, from the reader's own live collection
    /// state. The winner gains, the loser loses, and the rule is the one the server applies.
    /// </summary>
    public static HeistRankMove Mine(bool isWinner, bool owns, int rank, bool locked, GlobalConfig global)
        => isWinner
            ? MatchHeistPolicy.WinnerGain(owns, rank, locked, global)
            : MatchHeistPolicy.LoserLoss(owns, rank, locked, global);

    /// <summary>
    /// What it would do to the <b>loser's</b> copy, from the rank the lineup row carries — the rank its owner
    /// brought the card at, which is public the moment the card is played.
    /// <para>
    /// Only the winner's screen draws this side. The loser cannot be shown what the winner's copy does,
    /// because the winner's collection is not the loser's to see, and inventing a number for it would be worse
    /// than saying nothing.
    /// </para>
    /// </summary>
    public static HeistRankMove Theirs(int frozenRank, GlobalConfig global)
        => MatchHeistPolicy.LoserLoss(owns: true, frozenRank, locked: false, global);

    /// <summary>
    /// The reader's own side of the transfer, spelled out. Which way it goes needs no argument: the move's own
    /// kind carries it, because only a winner acquires or hits the ceiling and only a loser hits the floor.
    /// </summary>
    public static string MineLine(HeistRankMove move)
    {
        switch (move.Kind)
        {
            case HeistMove.Acquired:
                return $"NEW! It joins your collection at rank {move.To}.";

            case HeistMove.Moved:
                return $"Your copy: rank {move.From} → {move.To}.";

            case HeistMove.AtCeiling:
                return $"Your copy is already at rank {move.From}, so nothing moves.";

            case HeistMove.AtFloor:
                return $"Your copy is at rank {move.From} and safe there: nothing moves.";

            case HeistMove.Frozen:
                return $"You have frozen your own copy at rank {move.From}, so nothing moves.";

            default:
                return "Nothing moves.";
        }
    }

    /// <summary> The loser's side of the transfer, as the winner's screen states it. </summary>
    public static string TheirsLine(HeistRankMove move)
    {
        switch (move.Kind)
        {
            case HeistMove.Moved:
                return $"Theirs: rank {move.From} → {move.To}.";

            case HeistMove.AtFloor:
                return $"Theirs is at rank {move.From} already, and rank {move.From} is safe.";

            default:
                return "Theirs does not move.";
        }
    }

    /// <summary>
    /// The tier's own sentence on this screen: what the wager both players read before the first card was
    /// dealt is paying out now. The tier is kept for the copy and never for the routing.
    /// </summary>
    public static string TierLine(MatchStakes? stakes, MatchOutcomeRecord? result, bool isWinner)
    {
        if (stakes == null || result == null)
            return "";

        int owed = MatchHeistPolicy.PicksOwedForMatch(stakes, result);

        if (owed > 1)
        {
            return isWinner
                ? "The upset pays double: two of their cards, a rank each."
                : "You brought the stronger deck and lost it: they take two of your cards, a rank each.";
        }

        return isWinner
            ? "An even matchup: one card the loser played, one rank."
            : "An even matchup: they take one rank off one card you played.";
    }
}

/// <summary> What this client is doing on the Heist screen. </summary>
public enum HeistStage
{
    /// <summary> This client holds the winner's seat and a slot is still open. </summary>
    Picking  = 0,
    /// <summary> Watching somebody else decide — which is the loser's whole part in it. </summary>
    Watching = 1,
    /// <summary> Every pick is in, and both sides are reading the same record. </summary>
    Done     = 2,
}

/// <summary> Whose padlock takes a lineup row off the menu. </summary>
public enum HeistLockedBy
{
    Nobody    = 0,
    /// <summary> The loser froze it before they queued: their standing decision, taken in daylight. </summary>
    TheLoser  = 1,
    /// <summary> The winner froze their own copy, so taking it would pay them nothing. </summary>
    TheWinner = 2,
}

/// <summary> One row of the lineup: a card the loser played, and what may be done with it. </summary>
public readonly struct HeistLootRow
{
    public readonly CardId        Card;
    /// <summary> The rank the loser brought it at, frozen with their deck at enqueue. </summary>
    public readonly int           OwnedRank;
    public readonly bool          Pickable;
    /// <summary> Whether this occurrence has already been taken this phase. </summary>
    public readonly bool          Taken;
    public readonly HeistLockedBy LockedBy;

    public HeistLootRow(CardId card, int ownedRank, bool pickable, bool taken, HeistLockedBy lockedBy)
    {
        Card      = card;
        OwnedRank = ownedRank;
        Pickable  = pickable;
        Taken     = taken;
        LockedBy  = lockedBy;
    }

    /// <summary> Whether the row draws a padlock: either side's freeze reads as one on the card frame. </summary>
    public bool IsFrozen => LockedBy != HeistLockedBy.Nobody;
}
