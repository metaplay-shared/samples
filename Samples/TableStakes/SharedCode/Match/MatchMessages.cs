using Metaplay.Core;
using Metaplay.Core.Message;
using Metaplay.Core.Model;
using System.Collections.Generic;

namespace Game.Logic
{
    /// <summary>
    /// A client's request to play a card. It is a message rather than an action because only the host writes the
    /// timeline, so a refused move never appears on any board (<c>docs/match.md</c>, "Only the host writes the
    /// timeline"). The request names the card and the play index, not a position in the hand, because the hand's
    /// order on screen can change between the tap and the host receiving the request.
    /// </summary>
    [MetaMessage(MessageCodes.MatchPlayCardRequest, MessageDirection.ClientToServer), MessageRoutingRuleEntityChannel]
    public class MatchPlayCardRequest : MetaMessage
    {
        /// <summary>Untrusted. <see cref="MatchHost.TrySubmitMove"/> checks it against the sender's seat.</summary>
        public int  Seat      { get; private set; }
        public int  PlayIndex { get; private set; }
        public Card Card      { get; private set; }

        MatchPlayCardRequest() { }

        public MatchPlayCardRequest(int seat, int playIndex, Card card)
        {
            Seat      = seat;
            PlayIndex = playIndex;
            Card      = card;
        }

        public override string ToString() => $"play {Card} from seat {Seat} at index {PlayIndex}";
    }

    /// <summary>
    /// The host refused the sender's move, with the reason.
    /// <para>
    /// The client needs an explicit refusal. Without it, the selected card would stay raised and the hand would
    /// stay locked for the rest of the game. A refusal usually means the tap arrived after a deadline auto-play or
    /// a covering bot had already played that index. The same tap still reclaims a covered seat.
    /// </para>
    /// </summary>
    [MetaMessage(MessageCodes.MatchMoveRefused, MessageDirection.ServerToClient)]
    public class MatchMoveRefused : MetaMessage
    {
        /// <summary>The play index of the refused move, so the client can tell which tap was refused.</summary>
        public int               PlayIndex { get; private set; }

        public Card              Card      { get; private set; }

        public MoveRefusalReason Reason    { get; private set; }

        MatchMoveRefused() { }

        public MatchMoveRefused(int playIndex, Card card, MoveRefusalReason reason)
        {
            PlayIndex = playIndex;
            Card      = card;
            Reason    = reason;
        }

        public override string ToString() => $"refused {Card} at index {PlayIndex}: {Reason}";
    }

    /// <summary>
    /// One seat's current hand, sent when the host changed the hand without the owner playing a card: a deadline
    /// auto-play, a covering bot's move or a reclaim. The subscribe's private state covers the deal and every
    /// reconnect.
    /// <para>
    /// It carries the play index at which it was built, and the client applies it only when its board reaches that
    /// index. The SDK sends an entity message immediately but sends timeline updates on the host's next flush, so
    /// this message often arrives before the timeline update that caused it.
    /// </para>
    /// </summary>
    [MetaMessage(MessageCodes.MatchHandDelivered, MessageDirection.ServerToClient)]
    public class MatchHandDelivered : MetaMessage
    {
        public int        Seat      { get; private set; }
        public List<Card> Hand      { get; private set; }
        public int        PlayIndex { get; private set; }

        MatchHandDelivered() { }

        public MatchHandDelivered(int seat, IReadOnlyList<Card> hand, int playIndex)
        {
            Seat      = seat;
            Hand      = new List<Card>(hand);
            PlayIndex = playIndex;
        }

        public MatchOwnHand ToOwnHand() => new MatchOwnHand(Seat, Hand, PlayIndex);

        public override string ToString() => $"hand of {Hand.Count} for seat {Seat} at index {PlayIndex}";
    }

    /// <summary>
    /// A client's request to leave the table. Leaving is a disconnect without a grace timer: a bot covers the seat at
    /// once, and the game is still played to the end and recorded, so leaving does not avoid a loss
    /// (<c>docs/match.md</c>, "Leaving the table"). The table then tells the leaver's player actor to clear its
    /// current-match reference and association, which detaches the client. The leaver sees no results screen.
    /// </summary>
    [MetaMessage(MessageCodes.MatchLeaveRequest, MessageDirection.ClientToServer), MessageRoutingRuleEntityChannel]
    public class MatchLeaveRequest : MetaMessage
    {
        public override string ToString() => "leave the table";
    }

    /// <summary>
    /// A client's request for the host's current time, used to measure the offset between the clocks.
    /// <para>
    /// The table's countdowns compare against times the host wrote from its own clock. A device clock can be off
    /// by minutes, so the client times this round trip and passes the reply to <see cref="ServerClockEstimate"/>, which
    /// documents the estimate and its error bound.
    /// </para>
    /// </summary>
    [MetaMessage(MessageCodes.MatchClockSyncRequest, MessageDirection.ClientToServer), MessageRoutingRuleEntityChannel]
    public class MatchClockSyncRequest : MetaMessage
    {
        /// <summary>Identifies the request, so the client can ignore a reply to an older one.</summary>
        public int Nonce { get; private set; }

        MatchClockSyncRequest() { }

        public MatchClockSyncRequest(int nonce)
        {
            Nonce = nonce;
        }

        public override string ToString() => $"clock sync request {Nonce}";
    }

    /// <summary>
    /// The host's time when it answered a <see cref="MatchClockSyncRequest"/>. The client combines it with the
    /// device times at which it sent the request and received the reply.
    /// </summary>
    [MetaMessage(MessageCodes.MatchClockSyncResponse, MessageDirection.ServerToClient)]
    public class MatchClockSyncResponse : MetaMessage
    {
        public int      Nonce      { get; private set; }
        public MetaTime ServerTime { get; private set; }

        MatchClockSyncResponse() { }

        public MatchClockSyncResponse(int nonce, MetaTime serverTime)
        {
            Nonce      = nonce;
            ServerTime = serverTime;
        }

        public override string ToString() => $"clock sync response {Nonce}: {ServerTime}";
    }
}
