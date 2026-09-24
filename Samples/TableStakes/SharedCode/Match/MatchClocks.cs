using Metaplay.Core;
using System;

namespace Game.Logic
{
    // Client presentation logic. It lives in SharedCode so Backend/SharedCode.Tests can cover it. The server and
    // the match engine do not use it. The types are not [MetaSerializable] because they are never sent over the
    // network, so changing them does not require regenerating the WASM serializer.

    /// <summary>
    /// A reading of the estimated server clock. Use it for decisions the server also makes, such as whether a move
    /// will still be accepted or whether a deadline has passed. It has no conversion to or from
    /// <see cref="PresentedTime"/>, so code cannot mix the two clocks without a compile error
    /// (<c>docs/web-client.md</c>, "The table's two clocks").
    /// <para>
    /// The host writes deadlines from its own clock, so <see cref="Now"/> uses <see cref="ServerClockEstimate.Shared"/>
    /// instead of the device clock. Before the first clock measurement it returns the device clock.
    /// </para>
    /// </summary>
    public readonly struct AuthoritativeTime
    {
        public MetaTime Timestamp { get; }

        AuthoritativeTime(MetaTime timestamp)
        {
            Timestamp = timestamp;
        }

        /// <summary>The current server time, as estimated by <see cref="ServerClockEstimate.Shared"/>.</summary>
        public static AuthoritativeTime Now => new AuthoritativeTime(ServerClockEstimate.Shared.EstimateNow(MetaTime.Now));

        /// <summary>A reading at a given server time, for tests and for callers that already read <see cref="Now"/>.</summary>
        public static AuthoritativeTime At(MetaTime timestamp) => new AuthoritativeTime(timestamp);

        /// <summary>Whether this reading is at or past <paramref name="stamp"/>. True exactly on the stamp.</summary>
        public bool HasReached(MetaTime stamp) => Timestamp >= stamp;

        /// <summary>How long until <paramref name="stamp"/>, clamped at zero once it has passed.</summary>
        public MetaDuration RemainingUntil(MetaTime stamp) => MetaDuration.Max(stamp - Timestamp, MetaDuration.Zero);

        public override string ToString() => $"authoritative {Timestamp}";
    }

    /// <summary>
    /// The time of the board as the player sees it. It is behind <see cref="AuthoritativeTime"/> by the length of
    /// the animation still playing. Use it for anything the player sees.
    /// <para>
    /// The client renders only state the server has confirmed, so presented time is never ahead of authoritative
    /// time and <see cref="TrailingBy"/> is never negative.
    /// </para>
    /// </summary>
    public readonly struct PresentedTime
    {
        public MetaTime Timestamp { get; }

        /// <summary>How far the drawn board is behind the authoritative state.</summary>
        public MetaDuration TrailingBy { get; }

        PresentedTime(MetaTime timestamp, MetaDuration trailingBy)
        {
            Timestamp  = timestamp;
            TrailingBy = trailingBy;
        }

        /// <summary>Whether the board on screen has caught up with the state the server has confirmed.</summary>
        public bool IsCaughtUp => TrailingBy == MetaDuration.Zero;

        /// <summary>A reading for a board that shows the confirmed state, with no animation still playing.</summary>
        public static PresentedTime CaughtUpWith(AuthoritativeTime now) => new PresentedTime(now.Timestamp, MetaDuration.Zero);

        /// <summary>
        /// A reading for a board with <paramref name="trailingBy"/> of animation left before it shows the confirmed
        /// state. A negative value is clamped to zero.
        /// </summary>
        public static PresentedTime Trailing(AuthoritativeTime now, MetaDuration trailingBy)
        {
            MetaDuration behind = MetaDuration.Max(trailingBy, MetaDuration.Zero);
            return new PresentedTime(now.Timestamp - behind, behind);
        }

        /// <summary>Whether this reading is at or past <paramref name="stamp"/>. True exactly on the stamp.</summary>
        public bool HasReached(MetaTime stamp) => Timestamp >= stamp;

        /// <summary>How long until <paramref name="stamp"/>, clamped at zero once it has passed.</summary>
        public MetaDuration RemainingUntil(MetaTime stamp) => MetaDuration.Max(stamp - Timestamp, MetaDuration.Zero);

        public override string ToString() => $"presented {Timestamp} (trailing by {TrailingBy})";
    }

    /// <summary>
    /// The time-based checks the table page needs. Each uses exactly one of the two clocks
    /// (<c>docs/web-client.md</c>, "The table's two clocks").
    /// <para>
    /// Each method reads the timestamps from the board or model itself and takes no model time from the caller.
    /// The match entity does not tick, so its model time does not advance, and a countdown against it would never
    /// move (<c>docs/match.md</c>, "Timers").
    /// </para>
    /// </summary>
    public static class MatchTableTime
    {
        /// <summary>
        /// The time remaining for the move-deadline ring. Returns false, and no ring is shown, when no seat is
        /// waiting for a move, when the board is still animating (the player has not yet seen the turn start), or
        /// when there is no deadline (<see cref="MatchBoard.HasMoveDeadline"/>).
        /// </summary>
        public static bool TryGetMoveDeadlineRemaining(MatchBoard board, PresentedTime now, out MetaDuration remaining)
        {
            CheckBoard(board);

            if (board.TurnPhase != MatchTurnPhase.AwaitingMove || !board.HasMoveDeadline || !now.IsCaughtUp)
            {
                remaining = MetaDuration.Zero;
                return false;
            }

            remaining = now.RemainingUntil(board.MoveDeadlineAt);
            return true;
        }

        /// <summary>
        /// Whether the animation beat from <paramref name="startedAt"/> to <paramref name="endsAt"/> is still playing.
        /// Both times are written from <see cref="AuthoritativeTime"/>, so the check uses the same clock. With a
        /// device-clock reading, a device whose clock is ahead would never count the beat as playing.
        /// <para>
        /// A reading before <paramref name="startedAt"/> means the clock estimate moved backwards, and the beat counts
        /// as over. Ending a beat early only drops an animation, while a beat that never ends would hide the result.
        /// </para>
        /// </summary>
        public static bool IsBeatPlaying(MetaTime startedAt, MetaTime endsAt, AuthoritativeTime now)
            => now.Timestamp >= startedAt && now.Timestamp < endsAt;

        /// <summary>
        /// When a beat starting now ends: <paramref name="beatDuration"/> from now, but no later than
        /// <see cref="MatchBoard.ResolvePauseEndsAt"/>, because the beat must fit inside the resolve pause. Outside the
        /// resolve pause it returns <paramref name="now"/>, which <see cref="IsBeatPlaying"/> treats as a finished beat.
        /// The host wrote the pause end from its own clock, so this uses authoritative time.
        /// </summary>
        public static MetaTime GetBeatEndsAt(MatchBoard board, AuthoritativeTime now, MetaDuration beatDuration)
        {
            CheckBoard(board);

            if (board.TurnPhase != MatchTurnPhase.ResolvingTrick)
                return now.Timestamp;

            return MetaTime.Min(now.Timestamp + beatDuration, board.ResolvePauseEndsAt);
        }

        /// <summary>
        /// The seat the table highlights as on turn, or <see cref="SeatRotation.NoSeat"/> while the board is still
        /// animating. The model already knows who leads the next trick during the animation, and highlighting
        /// that seat early would show a turn the player has not seen start yet.
        /// </summary>
        public static int GetPresentedSeatOnTurn(MatchBoard board, PresentedTime now)
        {
            CheckBoard(board);
            return now.IsCaughtUp ? board.SeatOnTurn : SeatRotation.NoSeat;
        }

        /// <summary>
        /// The phase the table renders. Returns <see cref="MatchPhase.Playing"/> instead of a terminal phase while
        /// the board is still animating, so the results overlay does not appear before the last trick's animation
        /// ends.
        /// </summary>
        public static MatchPhase GetPresentedPhase(MatchModel model, PresentedTime now)
        {
            if (model == null)
                throw new ArgumentNullException(nameof(model));

            if (model.Phase != MatchPhase.Playing && !now.IsCaughtUp)
                return MatchPhase.Playing;
            return model.Phase;
        }

        /// <summary>
        /// Whether the server would still accept a move for the current play index. The hand uses this to decide
        /// whether it takes input. It uses authoritative time because, with presented time, the hand would stay
        /// enabled during an animation after the host had already auto-played the move.
        /// </summary>
        public static bool CanStillSubmitMove(MatchBoard board, AuthoritativeTime now)
        {
            CheckBoard(board);
            if (board.TurnPhase != MatchTurnPhase.AwaitingMove)
                return false;

            // With no deadline, the turn cannot expire. Treating MetaTime.Epoch as a passed deadline would lock
            // the hand for the whole game at a table without deadlines.
            if (!board.HasMoveDeadline)
                return true;

            return !now.HasReached(board.MoveDeadlineAt);
        }

        static void CheckBoard(MatchBoard board)
        {
            if (board == null)
                throw new ArgumentNullException(nameof(board));
        }
    }
}
