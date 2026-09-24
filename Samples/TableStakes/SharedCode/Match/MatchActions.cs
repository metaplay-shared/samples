using Metaplay.Core;
using Metaplay.Core.Model;
using System;
using System.Collections.Generic;

namespace Game.Logic
{
    /// <summary>
    /// Action codes for the match timeline. The player timeline's codes are in
    /// <c>SharedCode/Player/PlayerActions.cs</c>.
    /// </summary>
    public static partial class ActionCodes
    {
        // 5100-5199: match timeline.
        public const int MatchCardPlayed   = 5101;
        public const int MatchAdvanced     = 5102;
        public const int MatchSeatsUpdated = 5103;
        public const int MatchAbandoned    = 5104;
    }

    /// <summary>
    /// The match's <see cref="MetaActionResult"/> values.
    /// </summary>
    public static partial class ActionResults
    {
        public static readonly MetaActionResult NoSuchSeat           = new MetaActionResult(nameof(NoSuchSeat));
        public static readonly MetaActionResult MatchAlreadyFinished = new MetaActionResult(nameof(MatchAlreadyFinished));
        public static readonly MetaActionResult WrongSeatCount       = new MetaActionResult(nameof(WrongSeatCount));
        public static readonly MetaActionResult MatchAlreadyStarted  = new MetaActionResult(nameof(MatchAlreadyStarted));
    }

    /// <summary>
    /// Base class for every action on the match timeline. Only the host writes the timeline. The SDK lets clients
    /// enqueue actions by default, so two guards are both required: the
    /// <see cref="ModelActionExecuteFlags.LeaderSynchronized"/> flag makes the client context refuse a match action,
    /// and the host refuses every client-originated action (<c>docs/match.md</c>, "Only the host writes the timeline").
    /// <para>
    /// Clients replay every action with <see cref="MatchModel.Engine"/> null, so an action changes only the public
    /// board and takes every value from its own fields.
    /// </para>
    /// </summary>
    [MetaSerializable]
    [ModelActionExecuteFlags(ModelActionExecuteFlags.LeaderSynchronized)]
    public abstract class MatchAction : ModelAction<MatchModel>
    {
    }

    /// <summary>
    /// A seat played a card. Carries the card and seat, and the turn phase and timestamps the host's engine
    /// computed after the play, because clients have no engine to compute them.
    /// <see cref="TrickWinnerSeat"/> is -1 unless this play completed a trick.
    /// </summary>
    [ModelAction(ActionCodes.MatchCardPlayed)]
    public class MatchCardPlayed : MatchAction
    {
        public int            Seat               { get; private set; }
        public Card           Card               { get; private set; }
        public MatchTurnPhase TurnPhase          { get; private set; }
        public MetaTime       MoveDeadlineAt     { get; private set; }
        public MetaTime       ResolvePauseEndsAt { get; private set; }
        public int            TrickWinnerSeat    { get; private set; }

        /// <summary>
        /// When the host accepted this move. It is the only value that sets the table's last-activity time. It is
        /// carried rather than read from a clock so every client that replays the action gets the same value
        /// (<c>docs/match.md</c>, "Persistence").
        /// </summary>
        public MetaTime PlayedAt { get; private set; }

        public MatchCardPlayed() { }

        public MatchCardPlayed(int seat, Card card, MatchTurnPhase turnPhase, MetaTime moveDeadlineAt, MetaTime resolvePauseEndsAt, int trickWinnerSeat, MetaTime playedAt)
        {
            Seat               = seat;
            Card               = card;
            TurnPhase          = turnPhase;
            MoveDeadlineAt     = moveDeadlineAt;
            ResolvePauseEndsAt = resolvePauseEndsAt;
            TrickWinnerSeat    = trickWinnerSeat;
            PlayedAt           = playedAt;
        }

        public override MetaActionResult InvokeExecute(MatchModel match, bool commit)
        {
            if (Seat < 0 || Seat >= MatchRules.NumSeats)
                return ActionResults.NoSuchSeat;
            if (match.Board.PlayIndex >= MatchRules.NumPlays)
                return ActionResults.MatchAlreadyFinished;

            if (commit)
            {
                match.Board.ApplyPlay(Seat, Card, TurnPhase, MoveDeadlineAt, ResolvePauseEndsAt, TrickWinnerSeat);

                // Only a played card updates the last-activity time. If a snapshot or a reconnect updated it, a
                // table where nobody plays would look active and its retention would keep being extended.
                match.ApplyActivity(PlayedAt);

                match.ClientListener.OnCardPlayed(Seat, Card);
                if (TrickWinnerSeat >= 0)
                    match.ClientListener.OnTrickResolved(TrickWinnerSeat);
            }

            return MetaActionResult.Success;
        }
    }

    /// <summary>
    /// The resolve pause ended: either the next trick begins, or the game is over and the match enters a
    /// terminal phase.
    /// <para>
    /// The action does not carry the standings. Each client computes them with
    /// <see cref="MatchRules.ComputeStandings"/> from the trick history already on the board.
    /// </para>
    /// </summary>
    [ModelAction(ActionCodes.MatchAdvanced)]
    public class MatchAdvanced : MatchAction
    {
        public MatchTurnPhase TurnPhase      { get; private set; }
        public MatchPhase     Phase          { get; private set; }
        public MetaTime       MoveDeadlineAt { get; private set; }

        /// <summary>
        /// When the match entered its terminal phase, or <see cref="MetaTime.Epoch"/> while it is still being
        /// played. Carried rather than read from a clock so every client that replays the action gets the host's
        /// value.
        /// </summary>
        public MetaTime EndedAt { get; private set; }

        public MatchAdvanced() { }

        public MatchAdvanced(MatchTurnPhase turnPhase, MatchPhase phase, MetaTime moveDeadlineAt, MetaTime endedAt)
        {
            TurnPhase      = turnPhase;
            Phase          = phase;
            MoveDeadlineAt = moveDeadlineAt;
            EndedAt        = endedAt;
        }

        public override MetaActionResult InvokeExecute(MatchModel match, bool commit)
        {
            if (match.Phase != MatchPhase.Playing)
                return ActionResults.MatchAlreadyFinished;

            if (commit)
            {
                match.Board.ApplyAdvance(TurnPhase, MoveDeadlineAt);

                if (Phase != MatchPhase.Playing)
                {
                    List<SeatStanding> standings = MatchRules.ComputeStandings(match.Board.TrickWinnerSeats);
                    match.Board.ApplyStandings(standings);
                    match.ApplyPhase(Phase, EndedAt);
                    match.ClientListener.OnMatchEnded();
                }
            }

            return MetaActionResult.Success;
        }
    }

    /// <summary>
    /// One seat's full state as <see cref="MatchSeatsUpdated"/> carries it. It holds every field of a seat that
    /// can change, so an update replaces the seat state completely.
    /// </summary>
    [MetaSerializable]
    public struct MatchSeatState : IEquatable<MatchSeatState>
    {
        [MetaMember(1)] public MatchSeatOccupancy Occupancy;
        [MetaMember(2)] public bool               IsConnected;
        [MetaMember(3)] public bool               HasEverConnected;
        [MetaMember(4)] public int                ConsecutiveMissedDeadlines;
        [MetaMember(5)] public MetaTime           GraceEndsAt;

        /// <summary>Whether a grace timer is running for this seat.</summary>
        public bool IsInGrace => GraceEndsAt > MetaTime.Epoch;

        public static MatchSeatState Of(MatchSeat seat) => new MatchSeatState
        {
            Occupancy                  = seat.Occupancy,
            IsConnected                = seat.IsConnected,
            HasEverConnected           = seat.HasEverConnected,
            ConsecutiveMissedDeadlines = seat.ConsecutiveMissedDeadlines,
            GraceEndsAt                = seat.GraceEndsAt,
        };

        /// <summary>Whether every field is equal, so an update to <paramref name="other"/> would change nothing.</summary>
        public bool Equals(MatchSeatState other) =>
            Occupancy == other.Occupancy
            && IsConnected == other.IsConnected
            && HasEverConnected == other.HasEverConnected
            && ConsecutiveMissedDeadlines == other.ConsecutiveMissedDeadlines
            && GraceEndsAt == other.GraceEndsAt;

        public override bool Equals(object obj) => obj is MatchSeatState other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Occupancy, IsConnected, HasEverConnected, ConsecutiveMissedDeadlines, GraceEndsAt);

        public override string ToString() => $"{Occupancy}, {(IsConnected ? "connected" : "away")}, {ConsecutiveMissedDeadlines} lapses";
    }

    /// <summary>
    /// The seat roster, the move deadline, and whether play has begun. These change together because a seat
    /// change affects the deadline: a seat that leaves loses its deadline, and a seat that returns gets one.
    /// <para>
    /// The action carries every seat, not only the one that changed. The payload stays small, and the model
    /// cannot end up with a mix of old and new seat states.
    /// </para>
    /// </summary>
    [ModelAction(ActionCodes.MatchSeatsUpdated)]
    public class MatchSeatsUpdated : MatchAction
    {
        public List<MatchSeatState> Seats { get; private set; }

        /// <summary>
        /// The move deadline of the seat on turn, or <see cref="MetaTime.Epoch"/> for no deadline. A disconnected
        /// seat, a covered seat and a table with no deadline configured all get no deadline.
        /// </summary>
        public MetaTime MoveDeadlineAt { get; private set; }

        /// <summary>
        /// True only on the update that starts play. The model keeps its flag set once applied, so later updates
        /// send false.
        /// </summary>
        public bool PlayHasBegun { get; private set; }

        /// <summary>
        /// A new end time for the join window, or <see cref="MetaTime.Epoch"/> to keep the current one. Only the
        /// test-only force-expire sets it, together with the other timers it moves forward
        /// (<c>docs/testing.md</c>, "Forcing timers").
        /// </summary>
        public MetaTime JoinWindowEndsAt { get; private set; }

        public MatchSeatsUpdated() { }

        public MatchSeatsUpdated(List<MatchSeatState> seats, MetaTime moveDeadlineAt, bool playHasBegun, MetaTime joinWindowEndsAt)
        {
            Seats            = seats;
            MoveDeadlineAt   = moveDeadlineAt;
            PlayHasBegun     = playHasBegun;
            JoinWindowEndsAt = joinWindowEndsAt;
        }

        public override MetaActionResult InvokeExecute(MatchModel match, bool commit)
        {
            if (Seats == null || Seats.Count != MatchRules.NumSeats)
                return ActionResults.WrongSeatCount;
            if (match.Phase != MatchPhase.Playing)
                return ActionResults.MatchAlreadyFinished;

            if (commit)
            {
                for (int seat = 0; seat < MatchRules.NumSeats; seat++)
                {
                    match.ApplySeatState(seat, Seats[seat]);
                }

                match.Board.ApplyMoveDeadline(MoveDeadlineAt);

                if (JoinWindowEndsAt > MetaTime.Epoch)
                    match.ApplyJoinWindowEndsAt(JoinWindowEndsAt);

                if (PlayHasBegun)
                    match.ApplyPlayHasBegun();
            }

            return MetaActionResult.Success;
        }
    }

    /// <summary>
    /// The table never started: the join window ended and no human ever subscribed. This is the only way a
    /// match is abandoned. Once the first card is played, the game always finishes, and a table that loses every
    /// human during play is played out by bots (<c>docs/match.md</c>, "Phases").
    /// <para>
    /// An abandoned match has no standings. The client shows the abandoned panel instead of results.
    /// </para>
    /// </summary>
    [ModelAction(ActionCodes.MatchAbandoned)]
    public class MatchAbandoned : MatchAction
    {
        public MetaTime EndedAt { get; private set; }

        public MatchAbandoned() { }

        public MatchAbandoned(MetaTime endedAt)
        {
            EndedAt = endedAt;
        }

        public override MetaActionResult InvokeExecute(MatchModel match, bool commit)
        {
            if (match.Phase != MatchPhase.Playing)
                return ActionResults.MatchAlreadyFinished;
            if (match.Board.PlayIndex > 0)
                return ActionResults.MatchAlreadyStarted;

            if (commit)
            {
                match.Board.ApplyMoveDeadline(MetaTime.Epoch);
                match.ApplyPhase(MatchPhase.Abandoned, EndedAt);
                match.ClientListener.OnMatchEnded();
            }

            return MetaActionResult.Success;
        }
    }
}
