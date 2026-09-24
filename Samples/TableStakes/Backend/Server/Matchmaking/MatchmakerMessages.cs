using Game.Logic;
using Metaplay.Cloud.Entity;
using Metaplay.Core;
using Metaplay.Core.Model;

namespace Game.Server.Matchmaking
{
    /// <summary>
    /// Sent by a player's actor to add the player to the matchmaking queue. Every message from a player's actor
    /// to the matchmaker is a cast, not an ask. The matchmaker asks player actors during a formation, so a
    /// player actor asking the matchmaker at the same time would block both until the ask timed out.
    /// </summary>
    [MetaMessage(MessageCodes.MatchmakerEnterQueueMessage, MessageDirection.ServerInternal)]
    public class MatchmakerEnterQueueMessage : MetaMessage
    {
        /// <summary>
        /// The waiting player's public identity. The queue uses it only to identify the waiter. The seat is built
        /// from the identity in <see cref="MatchmakerReserveSeatResponse"/>, so a rename or cosmetic change made
        /// during the search reaches the table.
        /// </summary>
        public PlayerPublicIdentity Identity { get; private set; }

        MatchmakerEnterQueueMessage() { }

        public MatchmakerEnterQueueMessage(PlayerPublicIdentity identity)
        {
            Identity = identity;
        }
    }

    /// <summary>
    /// Sent by a player's actor to remove the player from the queue, because they cancelled or their session
    /// ended. Safe to send at any time: if a formation already took the player's entry, the message does
    /// nothing.
    /// </summary>
    [MetaMessage(MessageCodes.MatchmakerLeaveQueueMessage, MessageDirection.ServerInternal)]
    public class MatchmakerLeaveQueueMessage : MetaMessage
    {
        public EntityId PlayerId { get; private set; }

        MatchmakerLeaveQueueMessage() { }
        public MatchmakerLeaveQueueMessage(EntityId playerId) { PlayerId = playerId; }
    }

    /// <summary>
    /// Sent by the matchmaker to ask a player's actor to commit to a seat at a table it is about to create.
    /// <para>
    /// The player's actor decides once, so a cancel and a seat cannot both win: a cancel that arrives first makes
    /// the actor decline, and a cancel that arrives after acceptance is refused. The actor also declines if the
    /// player has no live <i>connection</i>, because a session lingers longer than a whole search after the
    /// connection closes.
    /// </para>
    /// </summary>
    [MetaMessage(MessageCodes.MatchmakerReserveSeatRequest, MessageDirection.ServerInternal)]
    public class MatchmakerReserveSeatRequest : EntityAskRequest<MatchmakerReserveSeatResponse>
    {
        public MatchmakerReserveSeatRequest() { }
    }

    /// <summary>
    /// A player actor's answer to <see cref="MatchmakerReserveSeatRequest"/>. An acceptance includes the player's
    /// current public identity, which is used instead of the one in the queue entry, so a rename or cosmetic
    /// change made during the search reaches the table (<c>docs/cosmetics.md</c>).
    /// </summary>
    [MetaMessage(MessageCodes.MatchmakerReserveSeatResponse, MessageDirection.ServerInternal)]
    public class MatchmakerReserveSeatResponse : EntityAskResponse
    {
        public bool                 Accepted { get; private set; }
        public PlayerPublicIdentity Identity { get; private set; }

        MatchmakerReserveSeatResponse() { }

        MatchmakerReserveSeatResponse(bool accepted, PlayerPublicIdentity identity)
        {
            Accepted = accepted;
            Identity = identity;
        }

        public static MatchmakerReserveSeatResponse Accept(PlayerPublicIdentity identity) => new MatchmakerReserveSeatResponse(accepted: true, identity);
        public static MatchmakerReserveSeatResponse Decline()                             => new MatchmakerReserveSeatResponse(accepted: false, identity: null);
    }

    /// <summary>
    /// Tells a player's actor which table the player was seated at. The actor stores the table as the player's
    /// current match and declares the entity association with the table.
    /// </summary>
    [MetaMessage(MessageCodes.MatchmakerSeatAssignedMessage, MessageDirection.ServerInternal)]
    public class MatchmakerSeatAssignedMessage : MetaMessage
    {
        public EntityId MatchId { get; private set; }

        /// <summary>
        /// The number of human players seated at the table, including this player. Used for the matchmaking
        /// analytics event, because the player's actor cannot know it otherwise.
        /// </summary>
        public int NumHumanSeats { get; private set; }

        MatchmakerSeatAssignedMessage() { }

        public MatchmakerSeatAssignedMessage(EntityId matchId, int numHumanSeats)
        {
            MatchId       = matchId;
            NumHumanSeats = numHumanSeats;
        }
    }

    /// <summary>
    /// Tells a player's actor that the formation it committed a seat to did not create a table. Without this
    /// message, the player would stay on a searching screen with no Cancel button until the reservation timed
    /// out.
    /// <para>
    /// If the table could not be created, the players who accepted are re-queued with their original enqueue
    /// times. A player whose reservation answer never arrived is <b>not</b> re-queued, because an unresponsive
    /// actor at the head of the queue would stall every following formation (<see cref="IsStillQueued"/>).
    /// </para>
    /// </summary>
    [MetaMessage(MessageCodes.MatchmakerSeatReleasedMessage, MessageDirection.ServerInternal)]
    public class MatchmakerSeatReleasedMessage : MetaMessage
    {
        /// <summary>True if the queue still holds this player's entry, so the actor keeps searching.</summary>
        public bool IsStillQueued { get; private set; }

        MatchmakerSeatReleasedMessage() { }
        public MatchmakerSeatReleasedMessage(bool isStillQueued) { IsStillQueued = isStillQueued; }
    }
}
