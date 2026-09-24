using Game.Logic;

namespace Game.Server.Match
{
    /// <summary>
    /// What may happen to a seat, and what happens to it, as pure functions over one <see cref="MatchSeat"/>,
    /// so the strike and cover rules are testable without an actor.
    /// </summary>
    public static class MatchSeatPolicy
    {
        /// <summary>
        /// Whether the <b>turn deadline</b> governs this seat. A connected seat is governed by the deadline and
        /// a seat whose player has gone by grace, never both (<c>Docs/match.md</c>, "When players stop
        /// playing"). Bot and covered seats are governed: a lapse there auto-plays, which is what their driver
        /// would do anyway.
        /// </summary>
        public static bool TurnDeadlineGoverns(MatchSeat seat)
            => seat.IsBotDriven || seat.IsConnectedHuman;

        /// <summary> Whether a seat is a person who is not here: the one case grace, and only grace, holds. </summary>
        public static bool IsAwayHuman(MatchSeat seat)
            => seat.Occupancy == SeatOccupancy.Human && !seat.IsConnected;

        /// <summary> Whether this seat can be covered right now. A seat already covered is not re-covered. </summary>
        public static bool CanCover(MatchSeat seat)
            => seat.Occupancy == SeatOccupancy.Human;

        /// <summary>
        /// A bot takes over a human's seat. The human's identity is kept: the plaque keeps their name and adds
        /// the computer-player mark.
        /// </summary>
        public static void Cover(MatchSeat seat)
        {
            if (!CanCover(seat))
                return;

            seat.Occupancy      = SeatOccupancy.HumanCoveredByBot;
            seat.ReclaimPending = false;
        }

        /// <summary>
        /// Whether a covered seat can be handed back. A match that has already decided cannot be reclaimed
        /// into; the result is written the moment the game decides, ahead of the phase.
        /// </summary>
        public static bool CanReclaim(MatchSeat seat, bool matchHasDecided)
            => seat.Occupancy == SeatOccupancy.HumanCoveredByBot && !matchHasDecided;

        /// <summary>
        /// Whether the table refuses this seat's rules intents: a bot is playing it and the game is still on.
        /// The owner's intent still counts as the owner being present (<see cref="RequestReclaim"/>). Once the
        /// game has decided, the rules refuse every intent on their own.
        /// </summary>
        public static bool RefusesIntents(MatchSeat seat, bool matchHasDecided)
            => seat.Occupancy == SeatOccupancy.HumanCoveredByBot && !matchHasDecided;

        /// <summary>
        /// A covered seat's owner is back: the run of strikes is broken and the seat is marked to be handed back
        /// at the next turn boundary. The bot keeps playing until then, so the owner never races it mid-turn
        /// (<c>Docs/match.md</c>, "Seats"). Answers whether anything changed.
        /// </summary>
        public static bool RequestReclaim(MatchSeat seat, bool matchHasDecided)
        {
            if (!CanReclaim(seat, matchHasDecided) || (seat.ReclaimPending && seat.Strikes == 0))
                return false;

            seat.ReclaimPending = true;
            ClearStrikes(seat);
            return true;
        }

        /// <summary> The owner went again before the boundary: the seat stays covered. </summary>
        public static void CancelReclaim(MatchSeat seat)
            => seat.ReclaimPending = false;

        /// <summary> Whether a turn boundary hands this seat back to its owner. </summary>
        public static bool ReclaimsAtTurnBoundary(MatchSeat seat, bool matchHasDecided)
            => seat.ReclaimPending && CanReclaim(seat, matchHasDecided);

        /// <summary> A turn began and the seat's owner asked for it back, so they have it. </summary>
        public static void Reclaim(MatchSeat seat, bool matchHasDecided)
        {
            if (!ReclaimsAtTurnBoundary(seat, matchHasDecided))
                return;

            seat.Occupancy      = SeatOccupancy.Human;
            seat.ReclaimPending = false;
            ClearStrikes(seat);
        }

        // ---------------------------------------------------------------- strikes

        /// <summary>
        /// Whether a lapsed turn deadline on this seat is a strike: only for a person who was here. Zero
        /// <paramref name="strikesBeforeCover"/> turns the count off, and a lapse then only auto-plays.
        /// </summary>
        public static bool LapseCountsAsAStrike(MatchSeat seat, int strikesBeforeCover)
            => strikesBeforeCover > 0 && seat.IsConnectedHuman;

        /// <summary>
        /// Whether this seat's run of lapses hands it to a bot. One lapse is being slow; two is being gone
        /// (<c>Docs/match.md</c>, "When players stop playing"). Barred once the game has decided, for
        /// the reason <see cref="CanReclaim"/> is.
        /// </summary>
        public static bool ShouldCoverAfterStrike(MatchSeat seat, int strikesBeforeCover, bool matchHasDecided)
        {
            if (strikesBeforeCover <= 0 || matchHasDecided || !CanCover(seat))
                return false;

            return seat.Strikes >= strikesBeforeCover;
        }

        /// <summary> The run is broken: any action from the seat resets the count. </summary>
        public static void ClearStrikes(MatchSeat seat)
            => seat.Strikes = 0;
    }
}
