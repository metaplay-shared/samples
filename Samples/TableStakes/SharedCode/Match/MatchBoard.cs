using Metaplay.Core;
using Metaplay.Core.Model;
using System;
using System.Collections.Generic;

namespace Game.Logic
{
    /// <summary>
    /// The public state of a match, which every seat may see. It holds no hand. It is the part of
    /// <see cref="MatchModel"/> that the SDK sends to clients, and the base of a bot's <see cref="MatchSeatView"/>.
    /// <para>
    /// <see cref="Build"/> copies each public field from the engine by name. The engine's secret state (deck order,
    /// undealt cards, RNG state) has no field here, so it cannot leak (<c>docs/match.md</c>, "Public state and private
    /// state"). Values that follow from the play history, such as tricks won and the seat on turn, are computed from
    /// the history rather than stored, so they cannot disagree with it.
    /// </para>
    /// </summary>
    [MetaSerializable]
    public sealed class MatchBoard
    {
        /// <summary>The card revealed after the deal. Its suit is trump.</summary>
        [MetaMember(1)] public Card TrumpCard { get; private set; }

        /// <summary>The turn phase: waiting for a card, in the resolve pause, or finished.</summary>
        [MetaMember(2)] public MatchTurnPhase TurnPhase { get; private set; }

        /// <summary>The seat that led the first trick.</summary>
        [MetaMember(3)] public int StartingLeaderSeat { get; private set; }

        /// <summary>Every card played so far, with the seat that played it, in play order.</summary>
        [MetaMember(4)] List<PlayRecord> _plays;

        /// <summary>The winning seat of each resolved trick, in trick order.</summary>
        [MetaMember(5)] List<int> _trickWinnerSeats;

        /// <summary>
        /// When the seat on turn runs out of time, or <see cref="MetaTime.Epoch"/> when there is no deadline (see
        /// <see cref="HasMoveDeadline"/>). The match model does not tick, so its model time does not advance. A
        /// countdown must compare the current wall-clock time against this value, not the model time.
        /// </summary>
        [MetaMember(6)] public MetaTime MoveDeadlineAt { get; private set; }

        /// <summary>When the resolve pause ends. An absolute time, so every client ends the pause at the same moment.</summary>
        [MetaMember(7)] public MetaTime ResolvePauseEndsAt { get; private set; }

        /// <summary>
        /// The final standings, written when the game ends and empty until then. <see cref="ComputeStandings"/>
        /// gives the current order at any time.
        /// </summary>
        [MetaMember(8)] List<SeatStanding> _standings;

        MatchBoard() { }

        /// <summary>
        /// Build the public board from an engine. Each field is copied explicitly, so a member added to this type
        /// stays empty until a line is added here.
        /// </summary>
        public static MatchBoard Build(MatchEngine engine)
        {
            if (engine == null)
                throw new ArgumentNullException(nameof(engine));

            MatchBoard board = new MatchBoard();
            board.TrumpCard          = engine.TrumpCard;
            board.TurnPhase          = engine.TurnPhase;
            board.StartingLeaderSeat = engine.StartingLeaderSeat;
            board.MoveDeadlineAt     = engine.MoveDeadlineAt;
            board.ResolvePauseEndsAt = engine.ResolvePauseEndsAt;
            board._plays             = new List<PlayRecord>(engine.Plays);
            board._trickWinnerSeats  = new List<int>(engine.TrickWinnerSeats);
            board._standings         = new List<SeatStanding>();
            return board;
        }

        public Suit TrumpSuit => TrumpCard.Suit;

        /// <summary>
        /// Whether there is a move deadline. False when <see cref="MoveDeadlineAt"/> is <see cref="MetaTime.Epoch"/>,
        /// which happens when no seat has a deadline or the host runs the table without deadlines. A deadline
        /// that has passed still counts as a deadline.
        /// </summary>
        public bool HasMoveDeadline => MoveDeadlineAt > MetaTime.Epoch;

        public IReadOnlyList<PlayRecord> Plays => _plays;

        public IReadOnlyList<int> TrickWinnerSeats => _trickWinnerSeats;

        /// <summary>The final standings, or an empty list while the game is still running.</summary>
        public IReadOnlyList<SeatStanding> Standings => _standings;

        /// <summary>The number of cards played so far, and the index the next move must name.</summary>
        public int PlayIndex => _plays.Count;

        /// <summary>The zero-based index of the trick in progress, or <see cref="MatchRules.NumTricks"/> once the game is over.</summary>
        public int TrickIndex => MatchRules.TrickIndex(_plays.Count);

        /// <summary>How many cards of the current trick have been played, below <see cref="MatchRules.CardsPerTrick"/>.</summary>
        public int PositionInTrick => MatchRules.PositionInTrick(_plays.Count);

        /// <summary>The seat leading the trick in progress, or the seat that will lead the next one.</summary>
        public int CurrentLeaderSeat => MatchRules.CurrentLeaderSeat(_trickWinnerSeats, StartingLeaderSeat);

        /// <summary>The seat that owes a card, or -1 when no seat is on turn.</summary>
        public int SeatOnTurn => MatchRules.SeatOnTurn(TurnPhase, _plays.Count, _trickWinnerSeats, StartingLeaderSeat);

        /// <summary>The suit that must be followed, or null when the next card played is a lead.</summary>
        public Suit? LedSuit => MatchRules.LedSuit(_plays);

        /// <summary>How many cards seat <paramref name="seat"/> still holds, counted from the play history.</summary>
        public int GetCardsRemaining(int seat) => MatchRules.GetCardsRemaining(_plays, seat);

        /// <summary>How many tricks seat <paramref name="seat"/> has won.</summary>
        public int GetTricksWon(int seat) => MatchRules.GetTricksWon(_trickWinnerSeats, seat);

        /// <summary>Whether <paramref name="card"/> is already in the play history.</summary>
        public bool HasBeenPlayed(Card card) => MatchRules.HasBeenPlayed(_plays, card);

        /// <summary>
        /// The cards on the table, in play order: the trick in progress, or the completed trick during the resolve
        /// pause.
        /// </summary>
        public List<PlayRecord> GetCurrentTrickPlays() => MatchRules.GetCurrentTrickPlays(_plays, TurnPhase);

        /// <summary>
        /// The cards of trick <paramref name="trickIndex"/>, in play order. For a trick in progress it returns the
        /// cards played so far, and for a trick not yet started an empty list.
        /// </summary>
        public List<PlayRecord> GetTrickPlays(int trickIndex)
        {
            int first = trickIndex * MatchRules.CardsPerTrick;
            int last  = System.Math.Min(first + MatchRules.CardsPerTrick, _plays.Count);
            List<PlayRecord> plays = new List<PlayRecord>(MatchRules.CardsPerTrick);
            for (int ndx = first; ndx < last; ndx++)
                plays.Add(_plays[ndx]);
            return plays;
        }

        /// <summary>All seats ranked from the tricks resolved so far.</summary>
        public List<SeatStanding> ComputeStandings() => MatchRules.ComputeStandings(_trickWinnerSeats);

        #region Written only by the match actions

        // Internal so that only code in this assembly can change the board. Only the actions in MatchActions.cs
        // should call these, because the actions run the same way on the host and on every client.

        /// <summary>
        /// Record a played card and the turn state after it. The caller supplies every value because this also
        /// runs on clients, which have no engine.
        /// </summary>
        internal void ApplyPlay(int seat, Card card, MatchTurnPhase turnPhase, MetaTime moveDeadlineAt, MetaTime resolvePauseEndsAt, int trickWinnerSeat)
        {
            _plays.Add(new PlayRecord(seat, card));
            if (trickWinnerSeat >= 0)
                _trickWinnerSeats.Add(trickWinnerSeat);

            TurnPhase          = turnPhase;
            MoveDeadlineAt     = moveDeadlineAt;
            ResolvePauseEndsAt = resolvePauseEndsAt;
        }

        /// <summary>End a resolve pause: the next trick begins, or the game is over.</summary>
        internal void ApplyAdvance(MatchTurnPhase turnPhase, MetaTime moveDeadlineAt)
        {
            TurnPhase          = turnPhase;
            MoveDeadlineAt     = moveDeadlineAt;
            ResolvePauseEndsAt = MetaTime.Epoch;
        }

        /// <summary>
        /// Set only the move deadline of the seat on turn. <see cref="MetaTime.Epoch"/> means no deadline.
        /// </summary>
        internal void ApplyMoveDeadline(MetaTime moveDeadlineAt)
        {
            MoveDeadlineAt = moveDeadlineAt;
        }

        /// <summary>Store the final standings when the game ends.</summary>
        internal void ApplyStandings(List<SeatStanding> standings)
        {
            _standings = standings;
        }

        #endregion

    }
}
