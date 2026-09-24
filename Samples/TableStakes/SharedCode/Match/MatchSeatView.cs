using Metaplay.Core.Model;
using System;
using System.Collections.Generic;

namespace Game.Logic
{
    /// <summary>
    /// What one seat may know: the public board and that seat's own hand. Bots decide from it, and the client builds
    /// one for its own seat. The board is the same <see cref="MatchBoard"/> type that clients receive, so a bot sees
    /// exactly what a client sees.
    /// <para>
    /// It cannot hold another seat's cards. It has one card list, <see cref="Hand"/>, and one seat index, and no field
    /// or factory accepts a second hand, so a bot that reads other hands would not compile (<c>docs/bots.md</c>).
    /// </para>
    /// </summary>
    [MetaSerializable]
    public sealed class MatchSeatView
    {
        /// <summary>The seat this view belongs to, and the only seat whose cards it holds.</summary>
        [MetaMember(1)] public int Seat { get; private set; }

        [MetaMember(2)] MatchBoard _board;

        /// <summary>The cards seat <see cref="Seat"/> still holds, in deal order.</summary>
        [MetaMember(3)] List<Card> _hand;

        MatchSeatView() { }

        /// <summary>
        /// Build the view for seat <paramref name="seat"/> from an engine, reading only that seat's hand with
        /// <see cref="MatchEngine.GetHand"/>. Host only, because only the host has an engine.
        /// </summary>
        public static MatchSeatView ForSeat(MatchEngine engine, int seat)
        {
            if (engine == null)
                throw new ArgumentNullException(nameof(engine));
            MatchRules.ThrowIfInvalidSeat(seat);

            return FromBoard(MatchBoard.Build(engine), seat, engine.GetHand(seat));
        }

        /// <summary>
        /// Build the view from a board and a hand. The client uses this, because it receives the board on the
        /// timeline and the hand separately.
        /// </summary>
        public static MatchSeatView FromBoard(MatchBoard board, int seat, IReadOnlyList<Card> hand)
        {
            if (board == null)
                throw new ArgumentNullException(nameof(board));
            if (hand == null)
                throw new ArgumentNullException(nameof(hand));
            MatchRules.ThrowIfInvalidSeat(seat);

            MatchSeatView view = new MatchSeatView();
            view.Seat   = seat;
            view._board = board;
            view._hand  = new List<Card>(hand);
            return view;
        }

        public MatchBoard Board => _board;

        public IReadOnlyList<Card> Hand => _hand;

        public Suit TrumpSuit => _board.TrumpSuit;

        public Suit? LedSuit => _board.LedSuit;

        /// <summary>Whether this seat must play a card now.</summary>
        public bool IsOnTurn => _board.TurnPhase == MatchTurnPhase.AwaitingMove && _board.SeatOnTurn == Seat;

        /// <summary>
        /// The cards this seat may legally play now, in hand order, from <see cref="MatchRules.GetLegalPlays"/>.
        /// Empty when the seat is not on turn.
        /// </summary>
        public List<Card> GetLegalPlays()
        {
            if (!IsOnTurn)
                return new List<Card>();
            return MatchRules.GetLegalPlays(_hand, _board.LedSuit);
        }

    }
}
