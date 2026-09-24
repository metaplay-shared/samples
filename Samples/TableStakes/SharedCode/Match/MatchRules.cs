using Metaplay.Core.Model;
using System;
using System.Collections.Generic;

namespace Game.Logic
{
    /// <summary>
    /// One play: the seat and the card it played. The play history is a list of these.
    /// </summary>
    [MetaSerializable]
    public struct PlayRecord
    {
        [MetaMember(1)] public int  Seat { get; private set; }
        [MetaMember(2)] public Card Card { get; private set; }

        public PlayRecord(int seat, Card card)
        {
            Seat = seat;
            Card = card;
        }

        public override string ToString() => $"seat {Seat}: {Card}";
    }

    /// <summary>
    /// Why a seat ranks above the seat directly below it in the standings.
    /// <para>
    /// The standings carry it so the results screen can explain a tie-break, which the trick counts alone do not
    /// show (<c>docs/player.md</c>, "Results screen").
    /// </para>
    /// </summary>
    [MetaSerializable]
    public enum StandingSeparation
    {
        /// <summary>The seat is last, so no seat is below it.</summary>
        None = 0,

        /// <summary>This seat won more tricks than the one below it.</summary>
        MoreTricks = 1,

        /// <summary>Both won the same number of tricks, and this seat won its last trick later.</summary>
        MoreRecentTrick = 2,

        /// <summary>Neither seat won a trick. The lower seat index ranks higher.</summary>
        NoTricksEither = 3,
    }

    /// <summary>
    /// One seat's position in the standings.
    /// </summary>
    [MetaSerializable]
    public struct SeatStanding
    {
        /// <summary>Position in the standings, 0 for the winner.</summary>
        [MetaMember(1)] public int Position { get; private set; }

        [MetaMember(2)] public int Seat { get; private set; }

        [MetaMember(3)] public int TricksWon { get; private set; }

        /// <summary>Index of the last trick this seat won, or -1 if it won none. Used to break ties in tricks won.</summary>
        [MetaMember(4)] public int LastTrickWonIndex { get; private set; }

        /// <summary>Why this seat ranks above the next one, or <see cref="StandingSeparation.None"/> if it is last.</summary>
        [MetaMember(5)] public StandingSeparation SeparatedFromNextBy { get; private set; }

        public SeatStanding(int position, int seat, int tricksWon, int lastTrickWonIndex, StandingSeparation separatedFromNextBy)
        {
            Position            = position;
            Seat                = seat;
            TricksWon           = tricksWon;
            LastTrickWonIndex   = lastTrickWonIndex;
            SeparatedFromNextBy = separatedFromNextBy;
        }

        public override string ToString() => $"#{Position + 1} seat {Seat} ({TricksWon} tricks, last won {LastTrickWonIndex}, ahead by {SeparatedFromNextBy})";
    }

    /// <summary>
    /// Why a move was refused. The client uses it to tell an illegal move from a move that arrived too late.
    /// </summary>
    [MetaSerializable]
    public enum MoveRefusalReason
    {
        /// <summary>The move was accepted.</summary>
        None = 0,

        /// <summary>
        /// The move named a play index other than the current one. <see cref="MatchEngine.PlanMove"/> checks this
        /// first (see there for the races it handles).
        /// </summary>
        StalePlayIndex = 1,

        /// <summary>No seat is on turn: the game is in a resolve pause or has finished.</summary>
        NotInPlayablePhase = 2,

        /// <summary>Another seat is on turn.</summary>
        NotYourTurn = 3,

        /// <summary>The seat does not hold that card.</summary>
        CardNotInHand = 4,

        /// <summary>The seat holds a card of the led suit and must play one.</summary>
        MustFollowSuit = 5,

        /// <summary>
        /// The sender does not occupy the seat the move named. Only <see cref="MatchHost.TrySubmitMove"/> returns
        /// this, never the engine.
        /// </summary>
        NotYourSeat = 6,

        /// <summary>
        /// The move was legal, but publishing it to the timeline failed, so the card was not played. Only
        /// <see cref="MatchHost.TrySubmitMove"/> returns this, never the engine. The client still gets a refusal
        /// so the selected card is released.
        /// </summary>
        NotPublished = 7,
    }

    /// <summary>
    /// The rules of Table Stakes as pure functions. The engine validates moves with them and the client
    /// highlights legal cards with them, so both use the same definition (see <c>docs/game-rules.md</c>,
    /// "Playing a trick").
    /// </summary>
    public static class MatchRules
    {
        public const int NumSeats      = 4;
        public const int CardsPerSeat  = 5;
        public const int NumTricks     = 5;
        public const int CardsPerTrick = NumSeats;
        public const int NumPlays      = NumTricks * CardsPerTrick;

        /// <summary>
        /// Whether <paramref name="card"/> may be played from <paramref name="hand"/>. With no led suit (a lead),
        /// any card is legal. Otherwise the seat must follow the led suit if it holds one. Assumes the card is in
        /// the hand.
        /// </summary>
        public static bool IsLegalPlay(IReadOnlyList<Card> hand, Suit? ledSuit, Card card)
        {
            if (!ledSuit.HasValue)
                return true;
            if (card.Suit == ledSuit.Value)
                return true;
            return !HasSuit(hand, ledSuit.Value);
        }

        /// <summary>
        /// The cards of <paramref name="hand"/> that may legally be played, in hand order.
        /// </summary>
        public static List<Card> GetLegalPlays(IReadOnlyList<Card> hand, Suit? ledSuit)
        {
            List<Card> legal = new List<Card>(hand.Count);
            foreach (Card card in hand)
            {
                if (IsLegalPlay(hand, ledSuit, card))
                    legal.Add(card);
            }
            return legal;
        }

        public static bool HasSuit(IReadOnlyList<Card> hand, Suit suit)
        {
            foreach (Card card in hand)
            {
                if (card.Suit == suit)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// The seat that wins a completed trick, by the rules in <see cref="Beats"/>. Throws
        /// <see cref="ArgumentException"/> unless <paramref name="trick"/> has exactly <see cref="CardsPerTrick"/>
        /// plays.
        /// </summary>
        /// <param name="trick">The trick's plays in play order. The first is the lead.</param>
        public static int ResolveTrick(IReadOnlyList<PlayRecord> trick, Suit trumpSuit)
        {
            if (trick == null || trick.Count != CardsPerTrick)
                throw new ArgumentException($"A trick is exactly {CardsPerTrick} plays", nameof(trick));

            return trick[GetBestPlayNdx(trick, trumpSuit)].Seat;
        }

        /// <summary>
        /// Index into <paramref name="plays"/> of the play currently winning the trick. Unlike
        /// <see cref="ResolveTrick"/>, it accepts an incomplete trick, so a bot can find the card to beat and the
        /// client can mark the winning card. Throws <see cref="ArgumentException"/> when there are no plays.
        /// </summary>
        /// <param name="plays">One trick's plays so far, in play order. The first is the lead.</param>
        public static int GetBestPlayNdx(IReadOnlyList<PlayRecord> plays, Suit trumpSuit)
        {
            if (plays == null || plays.Count == 0)
                throw new ArgumentException("A trick with no cards on the table has no leading play", nameof(plays));

            Suit ledSuit = plays[0].Card.Suit;
            int  bestNdx = 0;
            for (int ndx = 1; ndx < plays.Count; ndx++)
            {
                if (Beats(plays[ndx].Card, plays[bestNdx].Card, ledSuit, trumpSuit))
                    bestNdx = ndx;
            }
            return bestNdx;
        }

        /// <summary>
        /// Whether <paramref name="candidate"/> beats <paramref name="currentWinner"/> in a trick led with
        /// <paramref name="ledSuit"/>. A trump beats any non-trump. Between two trumps, or two cards of the led
        /// suit, the higher rank wins. A card that is neither trump nor of the led suit never wins.
        /// </summary>
        public static bool Beats(Card candidate, Card currentWinner, Suit ledSuit, Suit trumpSuit)
        {
            bool candidateIsTrump = candidate.Suit == trumpSuit;
            bool winnerIsTrump    = currentWinner.Suit == trumpSuit;

            if (candidateIsTrump != winnerIsTrump)
                return candidateIsTrump;

            // Both are trumps, or neither is. Only a card of the contested suit (trump, or else the led suit) can
            // win.
            Suit contestedSuit = candidateIsTrump ? trumpSuit : ledSuit;
            if (candidate.Suit != contestedSuit)
                return false;
            if (currentWinner.Suit != contestedSuit)
                return true;
            return candidate.Rank > currentWinner.Rank;
        }

        /// <summary>
        /// All seats ranked, best first.
        /// <para>
        /// The game rules define a tie-break only for first place. This applies the same tie-break to every
        /// position because the results screen shows all of them. Seats are ordered by tricks won, and a tie goes
        /// to the seat that won its last trick later. Two seats with equal trick counts can have the same last
        /// trick index only if neither won a trick, so the seat index only orders seats with no tricks.
        /// </para>
        /// </summary>
        /// <param name="trickWinnerSeats">The winning seat of each resolved trick, in trick order.</param>
        public static List<SeatStanding> ComputeStandings(IReadOnlyList<int> trickWinnerSeats)
        {
            int[] tricksWon         = new int[NumSeats];
            int[] lastTrickWonIndex = new int[NumSeats];
            for (int seat = 0; seat < NumSeats; seat++)
                lastTrickWonIndex[seat] = -1;

            for (int trickNdx = 0; trickNdx < trickWinnerSeats.Count; trickNdx++)
            {
                int winnerSeat = trickWinnerSeats[trickNdx];
                tricksWon[winnerSeat]         += 1;
                lastTrickWonIndex[winnerSeat]  = trickNdx;
            }

            List<int> seatsInRankOrder = new List<int>(NumSeats);
            for (int seat = 0; seat < NumSeats; seat++)
                seatsInRankOrder.Add(seat);

            seatsInRankOrder.Sort((int a, int b) =>
            {
                if (tricksWon[a] != tricksWon[b])
                    return tricksWon[b].CompareTo(tricksWon[a]);
                if (lastTrickWonIndex[a] != lastTrickWonIndex[b])
                    return lastTrickWonIndex[b].CompareTo(lastTrickWonIndex[a]);
                return a.CompareTo(b);
            });

            List<SeatStanding> standings = new List<SeatStanding>(NumSeats);
            for (int position = 0; position < seatsInRankOrder.Count; position++)
            {
                int seat = seatsInRankOrder[position];
                standings.Add(new SeatStanding(position, seat, tricksWon[seat], lastTrickWonIndex[seat], SeparationBelow(seatsInRankOrder, tricksWon, lastTrickWonIndex, position)));
            }
            return standings;
        }

        /// <summary>
        /// Which comparison in <see cref="ComputeStandings"/> put the seat at <paramref name="position"/> above the next
        /// one. It reads the same arrays as the sort, so it always matches the order.
        /// </summary>
        static StandingSeparation SeparationBelow(List<int> seatsInRankOrder, int[] tricksWon, int[] lastTrickWonIndex, int position)
        {
            if (position + 1 >= seatsInRankOrder.Count)
                return StandingSeparation.None;

            int above = seatsInRankOrder[position];
            int below = seatsInRankOrder[position + 1];

            if (tricksWon[above] != tricksWon[below])
                return StandingSeparation.MoreTricks;
            if (lastTrickWonIndex[above] != lastTrickWonIndex[below])
                return StandingSeparation.MoreRecentTrick;
            return StandingSeparation.NoTricksEither;
        }

        /// <summary>
        /// Throw <see cref="ArgumentOutOfRangeException"/> when <paramref name="seat"/> is not a valid seat index.
        /// Every type that indexes by seat uses this check.
        /// </summary>
        public static void ThrowIfInvalidSeat(int seat)
        {
            if (seat < 0 || seat >= NumSeats)
                throw new System.ArgumentOutOfRangeException(nameof(seat), seat, $"Seat must be in [0, {NumSeats})");
        }

        #region Derivations from the play history

        // These are pure functions of the play history and the trick winners. MatchEngine (host) and MatchBoard
        // (client) both hold these lists and both call these functions. The host validates a move with them and
        // the client offers moves with them, so a single implementation keeps the two from disagreeing.

        /// <summary>How many cards of the current trick have been played, below <see cref="CardsPerTrick"/>.</summary>
        public static int PositionInTrick(int playCount) => playCount % CardsPerTrick;

        /// <summary>The zero-based index of the trick in progress, or <see cref="NumTricks"/> once the game is over.</summary>
        public static int TrickIndex(int playCount) => playCount / CardsPerTrick;

        /// <summary>
        /// The seat leading the trick in progress, or between tricks, the seat that leads the next one. The last
        /// trick's winner leads. Before any trick is resolved, <paramref name="startingLeaderSeat"/> leads.
        /// </summary>
        public static int CurrentLeaderSeat(IReadOnlyList<int> trickWinnerSeats, int startingLeaderSeat) =>
            trickWinnerSeats.Count == 0 ? startingLeaderSeat : trickWinnerSeats[trickWinnerSeats.Count - 1];

        /// <summary>
        /// The seat that must play a card, or -1 in any phase other than <see cref="MatchTurnPhase.AwaitingMove"/>.
        /// </summary>
        public static int SeatOnTurn(MatchTurnPhase phase, int playCount, IReadOnlyList<int> trickWinnerSeats, int startingLeaderSeat) =>
            phase == MatchTurnPhase.AwaitingMove
                ? (CurrentLeaderSeat(trickWinnerSeats, startingLeaderSeat) + PositionInTrick(playCount)) % NumSeats
                : -1;

        /// <summary>The suit that must be followed, or null when the next card played is a lead.</summary>
        public static Suit? LedSuit(IReadOnlyList<PlayRecord> plays)
        {
            int positionInTrick = PositionInTrick(plays.Count);
            return positionInTrick == 0 ? (Suit?)null : plays[plays.Count - positionInTrick].Card.Suit;
        }

        /// <summary>How many cards seat <paramref name="seat"/> still holds, counted from the play history.</summary>
        public static int GetCardsRemaining(IReadOnlyList<PlayRecord> plays, int seat)
        {
            ThrowIfInvalidSeat(seat);
            // Indexed loops in these helpers: foreach over an IReadOnlyList allocates an enumerator per call, and the
            // table screen calls them every frame.
            int played = 0;
            for (int ndx = 0; ndx < plays.Count; ndx++)
            {
                if (plays[ndx].Seat == seat)
                    played++;
            }
            return CardsPerSeat - played;
        }

        /// <summary>How many tricks seat <paramref name="seat"/> has won.</summary>
        public static int GetTricksWon(IReadOnlyList<int> trickWinnerSeats, int seat)
        {
            ThrowIfInvalidSeat(seat);
            int won = 0;
            for (int ndx = 0; ndx < trickWinnerSeats.Count; ndx++)
            {
                if (trickWinnerSeats[ndx] == seat)
                    won++;
            }
            return won;
        }

        /// <summary>Whether <paramref name="card"/> is already in the play history.</summary>
        public static bool HasBeenPlayed(IReadOnlyList<PlayRecord> plays, Card card)
        {
            for (int ndx = 0; ndx < plays.Count; ndx++)
            {
                if (plays[ndx].Card == card)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// The cards on the table, in play order: the trick in progress, or the completed trick during the resolve
        /// pause.
        /// </summary>
        public static List<PlayRecord> GetCurrentTrickPlays(IReadOnlyList<PlayRecord> plays, MatchTurnPhase phase)
        {
            int countInTrick = phase == MatchTurnPhase.ResolvingTrick ? CardsPerTrick : PositionInTrick(plays.Count);
            List<PlayRecord> trick = new List<PlayRecord>(countInTrick);
            for (int ndx = plays.Count - countInTrick; ndx < plays.Count; ndx++)
                trick.Add(plays[ndx]);
            return trick;
        }

        #endregion
    }
}
