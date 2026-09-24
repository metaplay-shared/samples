using Game.Logic;
using Metaplay.Cloud.Entity;
using Metaplay.Core;
using Metaplay.Core.Message;
using Metaplay.Core.Model;

namespace Game.Server.Match
{
    /// <summary>
    /// The result of one match, offered to one seat's account. An ask, so the answer sets the acknowledgement
    /// (<c>Docs/match.md</c>, "Delivering the result and the Heist"). The <see cref="MatchTerminalKind"/>
    /// lets one message serve a result and an abandoned table's pointer clear.
    /// </summary>
    [MetaMessage(MessageCodes.InternalMatchDeliverResult, MessageDirection.ServerInternal)]
    public class InternalMatchDeliverResultRequest : EntityAskRequest<InternalMatchDeliverResultResponse>
    {
        public EntityId            MatchId { get; private set; }
        public int                 Seat    { get; private set; }
        public MatchTerminalKind   Kind    { get; private set; }
        /// <summary> Null when <see cref="Kind"/> is <see cref="MatchTerminalKind.Abandoned"/>. </summary>
        public MatchOutcomeRecord  Result  { get; private set; }

        /// <summary> What this seat's rating moves by, already computed; zero when not ranked. </summary>
        public int                 RatingDelta { get; private set; }

        InternalMatchDeliverResultRequest() { }

        public InternalMatchDeliverResultRequest(EntityId matchId, int seat, MatchTerminalKind kind, MatchOutcomeRecord result, int ratingDelta)
        {
            MatchId     = matchId;
            Seat        = seat;
            Kind        = kind;
            Result      = result;
            RatingDelta = ratingDelta;
        }
    }

    /// <summary> The account has folded the outcome in, or had already. Either way the table may stop asking. </summary>
    [MetaMessage(MessageCodes.InternalMatchDeliverResultOk, MessageDirection.ServerInternal)]
    public class InternalMatchDeliverResultResponse : EntityAskResponse
    {
        public InternalMatchDeliverResultResponse() { }
    }

    /// <summary>
    /// "Are you there?" A player actor asks before re-attaching to its match. An ask to an id nobody set up
    /// spawns a fresh actor that answers <c>IsSetUp: false</c> (<c>Docs/protocol.md</c>, "Two ways a
    /// match pointer locks a player out").
    /// </summary>
    [MetaMessage(MessageCodes.InternalMatchProbe, MessageDirection.ServerInternal)]
    public class InternalMatchProbeRequest : EntityAskRequest<InternalMatchProbeResponse>
    {
        public InternalMatchProbeRequest() { }
    }

    /// <summary> Whether the table is there. Whether it still holds a seat for the asker is a separate question. </summary>
    [MetaMessage(MessageCodes.InternalMatchProbeOk, MessageDirection.ServerInternal)]
    public class InternalMatchProbeResponse : EntityAskResponse
    {
        /// <summary> Whether the table has been set up at all. False means this id names no live table. </summary>
        public bool IsSetUp   { get; private set; }
        /// <summary> Whether the table has stopped for good, with or without a result. </summary>
        public bool IsTerminal { get; private set; }

        InternalMatchProbeResponse() { }

        public InternalMatchProbeResponse(bool isSetUp, bool isTerminal)
        {
            IsSetUp    = isSetUp;
            IsTerminal = isTerminal;
        }
    }

    /// <summary>
    /// Give this table up. Sent by a minter whose setup ask failed, when the table may exist with nothing
    /// pointing at it. Refused for a table anyone has joined.
    /// </summary>
    [MetaMessage(MessageCodes.InternalMatchAbandon, MessageDirection.ServerInternal)]
    public class InternalMatchAbandonRequest : EntityAskRequest<InternalMatchAbandonResponse>
    {
        public InternalMatchAbandonRequest() { }
    }

    /// <summary> Whether the table gave itself up, and why not when it did not. </summary>
    [MetaMessage(MessageCodes.InternalMatchAbandonOk, MessageDirection.ServerInternal)]
    public class InternalMatchAbandonResponse : EntityAskResponse
    {
        public bool   Abandoned { get; private set; }
        public string Reason    { get; private set; }

        InternalMatchAbandonResponse() { }

        public InternalMatchAbandonResponse(bool abandoned, string reason)
        {
            Abandoned = abandoned;
            Reason    = reason;
        }
    }
}
