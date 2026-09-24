using Metaplay.Core;
using Metaplay.Core.Message;
using Metaplay.Core.Model;

namespace Game.Logic
{
    /// <summary>
    /// The searching dialog's whole input: how long until the player expects to be seated. Sent once, at
    /// enqueue, because the fill wait never changes for the ticket's life. An upper bound rather than a
    /// prediction, and it carries no population count or band state (<c>Docs/matchmaking.md</c>, "What
    /// the player sees"). A duration rather than an instant, so the client counts it down on its own clock
    /// from the moment it arrives and no device clock has to agree with the server's.
    /// </summary>
    [MetaMessage(MessageCodes.MatchmakingStatusUpdate, MessageDirection.ServerToClient)]
    public class MatchmakingStatusUpdate : MetaMessage
    {
        /// <summary>
        /// What is left, when sent, of the server's bound on this wait: the ticket's arrival plus the fill wait.
        /// </summary>
        public MetaDuration Remaining { get; private set; }

        MatchmakingStatusUpdate() { }

        public MatchmakingStatusUpdate(MetaDuration remaining)
        {
            Remaining = remaining;
        }
    }

    /// <summary> Why a search stopped without a board appearing. </summary>
    [MetaSerializable]
    public enum MatchmakingEndReason
    {
        /// <summary> The player's own cancel landed before the seat reservation did. </summary>
        Cancelled = 0,

        /// <summary>
        /// The player-side bound elapsed with nothing having happened, most plausibly because the matchmaker
        /// restarted and took the in-memory queue with it.
        /// </summary>
        TimedOut = 1,

        /// <summary>
        /// This actor answered a seat reservation and the answer was lost, so the formation dissolved around
        /// a seat that had already committed. It pays one tap; everyone else pays nothing.
        /// </summary>
        SeatLost = 2,

        /// <summary>
        /// The entry was refused: this account is already searching or already at a table, or the deck no
        /// longer exists.
        /// </summary>
        Refused = 4,
    }

    /// <summary>
    /// The search is over and no board is coming. There is no "you have been matched" message: a formed match
    /// arrives as the association on the match slot, and the dialog closes because there is a board
    /// (<c>Docs/matchmaking.md</c>, "The client never picks an opponent").
    /// </summary>
    [MetaMessage(MessageCodes.MatchmakingEnded, MessageDirection.ServerToClient)]
    public class MatchmakingEnded : MetaMessage
    {
        public MatchmakingEndReason Reason { get; private set; }

        MatchmakingEnded() { }

        public MatchmakingEnded(MatchmakingEndReason reason)
        {
            Reason = reason;
        }
    }
}
