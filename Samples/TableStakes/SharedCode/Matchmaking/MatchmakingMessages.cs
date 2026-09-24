using Metaplay.Core;
using Metaplay.Core.Model;

namespace Game.Logic
{
    /// <summary>
    /// The player's matchmaking state, as the player actor reports it in <see cref="MatchmakingStatusUpdate"/>.
    /// It is sent in a message rather than stored on the player model because the queue is in memory only. A
    /// persisted "searching" flag would survive a server restart that removed the queue entry, and the player
    /// would stay on the searching screen.
    /// </summary>
    [MetaSerializable]
    public enum MatchmakingStatus
    {
        /// <summary>Not looking for a table.</summary>
        NotSearching = 0,

        /// <summary>In the queue. The player can cancel.</summary>
        Searching = 1,

        /// <summary>
        /// A seat at a newly formed table is reserved for this player, but not yet taken. The table arrives shortly
        /// after as an entity association. The searching screen stays up, but the player can no longer cancel.
        /// </summary>
        SeatReserved = 2,

        /// <summary>
        /// The request was refused: the player is already at a table, or the matchmaker did not accept them. The
        /// searching screen closes and tells the player.
        /// </summary>
        Unavailable = 3,
    }

    /// <summary>
    /// Client to server: add the player to the matchmaking queue.
    /// <para>
    /// The message has no fields. The client does not choose a table or opponents, and the player actor already
    /// has everything the seat needs (<c>docs/matchmaking.md</c>, "What the client sees").
    /// </para>
    /// </summary>
    [MetaMessage(MessageCodes.MatchmakingEnterRequest, MessageDirection.ClientToServer), MessageRoutingRuleOwnedPlayer]
    public class MatchmakingEnterRequest : MetaMessage
    {
        public MatchmakingEnterRequest() { }

        public override string ToString() => "matchmaking: put me in the queue";
    }

    /// <summary>
    /// Client to server: remove the player from the matchmaking queue.
    /// <para>
    /// The server always answers with a <see cref="MatchmakingStatusUpdate"/>. A cancel that arrives after the
    /// player's seat was reserved is refused, and the answer is <see cref="MatchmakingStatus.SeatReserved"/>, so
    /// the client keeps waiting for the table instead of showing the menu.
    /// </para>
    /// </summary>
    [MetaMessage(MessageCodes.MatchmakingCancelRequest, MessageDirection.ClientToServer), MessageRoutingRuleOwnedPlayer]
    public class MatchmakingCancelRequest : MetaMessage
    {
        public MatchmakingCancelRequest() { }

        public override string ToString() => "matchmaking: take me out of the queue";
    }

    /// <summary>
    /// Server to client: the player's matchmaking state. Sent on every change and in answer to both requests.
    /// </summary>
    [MetaMessage(MessageCodes.MatchmakingStatusUpdate, MessageDirection.ServerToClient)]
    public class MatchmakingStatusUpdate : MetaMessage
    {
        public MatchmakingStatus Status { get; private set; }

        /// <summary>
        /// The latest time by which the player should be at a table, or <see cref="MetaTime.Epoch"/> when there is
        /// nothing to wait for. The searching dialog counts down to it. It is an upper bound, because the queue forms a
        /// table early once it has enough players. It is an absolute time, not a duration, because the client compares
        /// it with its estimate of the server clock (<c>docs/web-client.md</c>, "The table's two clocks").
        /// </summary>
        public MetaTime SeatDeadlineAt { get; private set; }

        MatchmakingStatusUpdate() { }

        public MatchmakingStatusUpdate(MatchmakingStatus status) : this(status, MetaTime.Epoch) { }

        public MatchmakingStatusUpdate(MatchmakingStatus status, MetaTime seatBy)
        {
            Status = status;
            SeatDeadlineAt = seatBy;
        }

        public override string ToString() => $"matchmaking: {Status}";
    }
}
