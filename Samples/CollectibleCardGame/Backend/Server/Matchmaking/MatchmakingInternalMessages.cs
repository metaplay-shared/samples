using Game.Logic;
using Metaplay.Cloud.Entity;
using Metaplay.Core;
using Metaplay.Core.Message;

namespace Game.Server.Matchmaking
{
    /// <summary>
    /// A player's ticket, on its way into the queue. Cast rather than asked: the player's own search bound
    /// covers a cast that never lands.
    /// </summary>
    [MetaMessage(MessageCodes.InternalMatchmakingEnqueue, MessageDirection.ServerInternal)]
    public class InternalMatchmakingEnqueue : MetaMessage
    {
        public MatchmakingTicket Ticket { get; private set; }

        InternalMatchmakingEnqueue() { }

        public InternalMatchmakingEnqueue(MatchmakingTicket ticket)
        {
            Ticket = ticket;
        }
    }

    /// <summary>
    /// Take this player out of the queue. <b>Fire-and-forget, not an ask</b>: a lost cancel is not a
    /// correctness problem, because removing an id that is not present is a no-op and because the real defence
    /// against "cancelled but seated anyway" lives on the player actor's own commit point, independent of
    /// whether the matchmaker's list still holds a stale entry for a few more milliseconds.
    /// </summary>
    [MetaMessage(MessageCodes.InternalMatchmakingCancel, MessageDirection.ServerInternal)]
    public class InternalMatchmakingCancel : MetaMessage
    {
        public EntityId PlayerId { get; private set; }

        InternalMatchmakingCancel() { }

        public InternalMatchmakingCancel(EntityId playerId)
        {
            PlayerId = playerId;
        }
    }

    /// <summary>
    /// The pairing this seat committed to dissolved, and this seat answered — so it is demonstrably
    /// responsive. Its reservation is released and its ticket goes back with the arrival stamp it came with:
    /// it keeps the band it had earned and loses a fraction of a second.
    /// </summary>
    [MetaMessage(MessageCodes.InternalMatchmakingReservationReleased, MessageDirection.ServerInternal)]
    public class InternalMatchmakingReservationReleased : MetaMessage
    {
        public InternalMatchmakingReservationReleased() { }
    }

    /// <summary>
    /// The pairing dissolved and this seat's answer was lost, so whether it committed is unknowable. It is not
    /// re-queued: an actor that could not answer once would be re-taken by the next formation and stall that
    /// one too.
    /// </summary>
    [MetaMessage(MessageCodes.InternalMatchmakingSeatGone, MessageDirection.ServerInternal)]
    public class InternalMatchmakingSeatGone : MetaMessage
    {
        public InternalMatchmakingSeatGone() { }
    }

    /// <summary>
    /// A table exists and this account is at it. The handler points the account at the match, associates it
    /// onto the client's match slot and records the deck, as the practice path does.
    /// </summary>
    [MetaMessage(MessageCodes.InternalMatchmakingFormed, MessageDirection.ServerInternal)]
    public class InternalMatchmakingFormed : MetaMessage
    {
        public EntityId   MatchId    { get; private set; }
        public int        Seat       { get; private set; }
        /// <summary> The deck the ticket was frozen with, so the account records what it was actually seated with. </summary>
        public DeckChoice DeckChoice { get; private set; }

        InternalMatchmakingFormed() { }

        public InternalMatchmakingFormed(EntityId matchId, int seat, DeckChoice deckChoice)
        {
            MatchId    = matchId;
            Seat       = seat;
            DeckChoice = deckChoice;
        }
    }

    /// <summary>
    /// Commit to a seat, or decline it. <b>This ask is the entire cancel-versus-formation race</b>, and it
    /// needs no locking: the player's own actor is a single-threaded entity and the check-then-commit lives in
    /// one handler on it, so whichever of the cancel and this ask reaches the mailbox first is the outcome.
    /// </summary>
    [MetaMessage(MessageCodes.InternalPlayerSeatInMatchRequest, MessageDirection.ServerInternal)]
    public class InternalPlayerSeatInMatchRequest : EntityAskRequest<InternalPlayerSeatInMatchResponse>
    {
        public InternalPlayerSeatInMatchRequest() { }
    }

    /// <summary> Whether the seat was taken, and why not when it was not. </summary>
    [MetaMessage(MessageCodes.InternalPlayerSeatInMatchResponse, MessageDirection.ServerInternal)]
    public class InternalPlayerSeatInMatchResponse : EntityAskResponse
    {
        public bool Accepted { get; private set; }

        /// <summary>
        /// Why the seat was declined. It is here because a declined seat and a lost answer read identically in
        /// the matchmaker's log otherwise, and the dissolved-pairing rule turns entirely on telling them
        /// apart: one is re-queued and the other is not.
        /// </summary>
        public string Reason { get; private set; }

        InternalPlayerSeatInMatchResponse() { }

        public InternalPlayerSeatInMatchResponse(bool accepted, string reason)
        {
            Accepted = accepted;
            Reason   = reason;
        }
    }
}
