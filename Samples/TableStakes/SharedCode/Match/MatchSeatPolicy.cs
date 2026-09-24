using Metaplay.Core;
using System;
using System.Collections.Generic;

namespace Game.Logic
{
    /// <summary>
    /// The rules for players who stop playing (<c>docs/match.md</c>, "When players stop playing"), as pure functions
    /// of the seats, the board and a time, so they can be unit tested without an actor. <see cref="MatchHost"/> turns
    /// their answers into actions. Two rules underlie the rest:
    /// <list type="bullet">
    /// <item>A seat never has a move deadline and a grace timer at once, so a disconnected player collects no lapses
    /// for turns they could not take (<see cref="MoveDeadlineFor"/>).</item>
    /// <item>A table with no human left is played out. A table is abandoned only if play never started.</item>
    /// </list>
    /// </summary>
    public static class MatchSeatPolicy
    {
        /// <summary>
        /// The move deadline for <paramref name="seat"/> when it comes on turn, or <see cref="MetaDuration.Zero"/>
        /// for no deadline.
        /// <para>
        /// Only a connected seat played by its human owner gets a deadline. A disconnected owner has a grace timer,
        /// a covering bot uses a think delay, and a bot seat has no timer. No seat therefore has two timers at once.
        /// </para>
        /// </summary>
        public static MetaDuration MoveDeadlineFor(MatchSeat seat, MatchTimings timings) =>
            seat == null ? MetaDuration.Zero : MoveDeadlineFor(MatchSeatState.Of(seat), timings);

        /// <summary><see cref="MoveDeadlineFor(MatchSeat, MatchTimings)"/> for a seat state not yet applied to the model.</summary>
        public static MetaDuration MoveDeadlineFor(MatchSeatState state, MatchTimings timings)
        {
            if (state.Occupancy != MatchSeatOccupancy.Human || !state.IsConnected)
                return MetaDuration.Zero;
            return timings.MoveDeadline;
        }

        /// <summary>The move deadline duration for the seat on turn, or zero when no seat is on turn.</summary>
        static MetaDuration MoveDeadlineForSeatOnTurn(MatchModel model, MatchTimings timings)
        {
            CheckModel(model);
            int seat = model.Board.SeatOnTurn;
            if (seat < 0)
                return MetaDuration.Zero;
            return MoveDeadlineFor(model.GetSeat(seat), timings);
        }

        /// <summary>
        /// Whether the board's move deadline must change for the seat on turn, and the new deadline in
        /// <paramref name="deadlineAt"/>.
        /// <para>
        /// It only compares whether a deadline exists with whether one should. It does not check when a running
        /// deadline started, because restarting a running deadline on every call could extend the turn without
        /// limit.
        /// </para>
        /// </summary>
        public static bool NeedsDeadlineChange(MatchModel model, MatchTimings timings, MetaTime now, out MetaTime deadlineAt)
        {
            CheckModel(model);
            deadlineAt = model.Board.MoveDeadlineAt;

            if (model.Board.TurnPhase != MatchTurnPhase.AwaitingMove)
                return false;

            MetaDuration owedDeadlineDuration = MoveDeadlineForSeatOnTurn(model, timings);
            bool         hasDeadline          = model.Board.HasMoveDeadline;

            if (owedDeadlineDuration > MetaDuration.Zero && !hasDeadline)
            {
                deadlineAt = now + owedDeadlineDuration;
                return true;
            }
            if (owedDeadlineDuration == MetaDuration.Zero && hasDeadline)
            {
                deadlineAt = MetaTime.Epoch;
                return true;
            }
            return false;
        }

        #region Before the first turn

        /// <summary>Whether any seat has an owner. A table of only bots has nobody to wait for.</summary>
        public static bool HasHumanSeat(MatchModel model)
        {
            CheckModel(model);
            foreach (MatchSeat seat in model.Seats)
            {
                if (seat.HasOwner)
                    return true;
            }
            return false;
        }

        /// <summary>Whether every seat with an owner has subscribed at least once.</summary>
        public static bool EveryHumanSeatHasArrived(MatchModel model)
        {
            CheckModel(model);
            foreach (MatchSeat seat in model.Seats)
            {
                if (seat.HasOwner && !seat.HasEverConnected)
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Whether play may begin: every human seat has subscribed, or the join window has ended. Play does not
        /// begin when the table is created, because a client that is still loading would miss the first cards.
        /// </summary>
        public static bool ShouldBeginPlay(MatchModel model, MetaTime now)
        {
            CheckModel(model);
            if (model.PlayHasBegun)
                return false;
            return EveryHumanSeatHasArrived(model) || now >= model.JoinWindowEndsAt;
        }

        /// <summary>
        /// Whether the table is abandoned instead of started: play has not begun, the table has at least one owned
        /// seat, and no owner ever subscribed. This is the only case of abandonment. A table that loses its humans
        /// after play begins is played out (see <see cref="MatchHost"/> play-out).
        /// </summary>
        public static bool IsAbandonedAtStart(MatchModel model)
        {
            CheckModel(model);
            if (model.PlayHasBegun || model.Board.PlayIndex > 0)
                return false;
            if (!HasHumanSeat(model))
                return false;

            foreach (MatchSeat seat in model.Seats)
            {
                if (seat.HasOwner && seat.HasEverConnected)
                    return false;
            }
            return true;
        }

        #endregion

        #region Grace, strikes and cover

        /// <summary>Whether this seat's grace timer has ended, so a bot takes over the seat.</summary>
        public static bool GraceHasLapsed(MatchSeat seat, MetaTime now)
        {
            if (seat == null)
                return false;
            return seat.IsInGrace && now >= seat.GraceEndsAt;
        }

        /// <summary>
        /// Whether a seat that has missed <paramref name="consecutiveMissedDeadlines"/> deadlines in a row is covered
        /// by a bot: true at <see cref="MatchTimings.StrikesBeforeCover"/> lapses, or at one lapse when that value is
        /// not positive.
        /// </summary>
        public static bool ShouldCoverAfterLapses(int consecutiveMissedDeadlines, MatchTimings timings)
        {
            int missedDeadlinesToCover = timings.StrikesBeforeCover > 0 ? timings.StrikesBeforeCover : 1;
            return consecutiveMissedDeadlines >= missedDeadlinesToCover;
        }

        /// <summary>
        /// Whether the seat on turn has passed its move deadline. A seat with no deadline, such as a disconnected
        /// seat, never does.
        /// </summary>
        public static bool MoveDeadlineHasLapsed(MatchModel model, MetaTime now)
        {
            CheckModel(model);
            MatchBoard board = model.Board;
            if (board.TurnPhase != MatchTurnPhase.AwaitingMove || !board.HasMoveDeadline)
                return false;
            return now >= board.MoveDeadlineAt;
        }

        #endregion

        #region Losing everyone

        /// <summary>
        /// Whether no human can return, so the table is played out.
        /// <para>
        /// Restoring a table clears every connected flag, so connection state alone would make a restored game in
        /// progress look deserted. This returns false while any owned seat is connected or has a running grace
        /// timer, including the restart grace from <see cref="StatesAfterColdWake"/> (<c>docs/match.md</c>, "Restart
        /// and cold wake").
        /// </para>
        /// </summary>
        public static bool NobodyIsComingBack(MatchModel model, MetaTime now)
        {
            CheckModel(model);
            if (model.Phase != MatchPhase.Playing || !model.PlayHasBegun)
                return false;
            if (!HasHumanSeat(model))
                return false;

            foreach (MatchSeat seat in model.Seats)
            {
                if (!seat.HasOwner)
                    continue;

                // A connected owner counts even when a bot covers the seat, because they can reclaim it by
                // playing a card.
                if (seat.IsConnected)
                    return false;

                // A running grace timer means the owner may still reconnect.
                if (seat.IsInGrace && now < seat.GraceEndsAt)
                    return false;
            }
            return true;
        }

        #endregion

        #region Building the roster an action carries

        /// <summary>The current state of every seat, as the starting point for a <see cref="MatchSeatsUpdated"/>.</summary>
        public static List<MatchSeatState> CurrentStates(MatchModel model)
        {
            CheckModel(model);
            List<MatchSeatState> states = new List<MatchSeatState>(MatchRules.NumSeats);
            foreach (MatchSeat seat in model.Seats)
                states.Add(MatchSeatState.Of(seat));
            return states;
        }

        /// <summary>
        /// The seat's owner has arrived or returned. The grace timer stops, the lapse count resets, and a covering
        /// bot returns the seat.
        /// <para>
        /// The reclaim does not require an accepted move. The covering bot often plays the same turn first, so a
        /// reclaim that waited for an accepted move would rarely succeed (<c>docs/match.md</c>, "The play index").
        /// </para>
        /// </summary>
        public static MatchSeatState Present(MatchSeatState state)
        {
            state.Occupancy                  = state.Occupancy == MatchSeatOccupancy.Bot ? MatchSeatOccupancy.Bot : MatchSeatOccupancy.Human;
            state.IsConnected                = true;
            state.HasEverConnected           = true;
            state.ConsecutiveMissedDeadlines = 0;
            state.GraceEndsAt                = MetaTime.Epoch;
            return state;
        }

        /// <summary>
        /// The seat's owner has gone. The seat gets a grace timer, unless <paramref name="skipGrace"/> is set (the
        /// Leave control) or <paramref name="grace"/> is zero, in which case a bot covers it immediately.
        /// </summary>
        public static MatchSeatState Absent(MatchSeatState state, MetaTime now, MetaDuration grace, bool skipGrace)
        {
            state.IsConnected = false;

            if (state.Occupancy == MatchSeatOccupancy.Bot)
                return state;

            if (skipGrace || grace <= MetaDuration.Zero)
            {
                state.Occupancy   = MatchSeatOccupancy.HumanCoveredByBot;
                state.GraceEndsAt = MetaTime.Epoch;
                return state;
            }

            // A seat a bot already covers gets no grace timer. The owner reclaims it by coming back.
            if (state.Occupancy == MatchSeatOccupancy.HumanCoveredByBot)
                state.GraceEndsAt = MetaTime.Epoch;
            else
                state.GraceEndsAt = now + grace;

            return state;
        }

        /// <summary>A bot covers this human seat and its grace timer stops. The owner can reclaim the seat.</summary>
        public static MatchSeatState Cover(MatchSeatState state)
        {
            if (state.Occupancy == MatchSeatOccupancy.Human)
                state.Occupancy = MatchSeatOccupancy.HumanCoveredByBot;
            state.GraceEndsAt = MetaTime.Epoch;
            return state;
        }

        /// <summary>
        /// The seat states after the table is restored from the database. Every connected flag is cleared, because no
        /// client is subscribed to a just-restored actor. Every owned seat that was connected gets the restart grace,
        /// because all clients reconnect at once. That includes a connected owner whose seat a bot covers. Without the
        /// grace, that seat would be neither connected nor in grace, and <see cref="NobodyIsComingBack"/> would let
        /// the table be played out before anyone could reconnect. A seat that was not connected, or already has a
        /// running grace timer, gets no new grace. The actor shuts down before the restart grace ends, so a later
        /// restore acts on the ended grace, and a new grace on each restore would keep the table alive forever.
        /// </summary>
        public static List<MatchSeatState> StatesAfterColdWake(MatchModel model, MetaTime now, MetaDuration restartGrace)
        {
            CheckModel(model);
            List<MatchSeatState> states = CurrentStates(model);

            for (int seat = 0; seat < states.Count; seat++)
            {
                MatchSeatState state        = states[seat];
                bool           wasConnected = state.IsConnected;

                state.IsConnected = false;

                if (model.GetSeat(seat).HasOwner && wasConnected && !state.IsInGrace)
                    state.GraceEndsAt = now + restartGrace;

                states[seat] = state;
            }
            return states;
        }

        #endregion

        static void CheckModel(MatchModel model)
        {
            if (model == null)
                throw new ArgumentNullException(nameof(model));
        }
    }
}
