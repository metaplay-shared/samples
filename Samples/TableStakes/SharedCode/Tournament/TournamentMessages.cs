using Metaplay.Core;
using Metaplay.Core.Message;
using Metaplay.Core.Model;

namespace Game.Logic
{
    /// <summary>Why a request to enter the tournament was refused.</summary>
    [MetaSerializable]
    public enum TournamentJoinRefusal
    {
        /// <summary>The join was not refused.</summary>
        None = 0,

        /// <summary>No season is running, because the league is in the break between seasons.</summary>
        NoSeasonRunning = 1,

        /// <summary>The player has already joined this season.</summary>
        AlreadyJoined = 2,

        /// <summary>The league is changing seasons and is still moving participants.</summary>
        SeasonChanging = 3,

        /// <summary>A server-side error that the player cannot fix. Retrying is safe.</summary>
        Unavailable = 4,
    }

    /// <summary>
    /// Client to server: join the running season.
    /// <para>
    /// It is a request rather than an action because only the league manager can place the player in a group,
    /// and the response must be able to give a refusal reason. Sending it twice is safe: the second request
    /// returns <see cref="TournamentJoinRefusal.AlreadyJoined"/> and never creates a second participant.
    /// </para>
    /// </summary>
    [MetaMessage(MessageCodes.TournamentJoinRequest, MessageDirection.ClientToServer), MessageRoutingRuleOwnedPlayer]
    public class TournamentJoinRequest : MetaMessage
    {
        /// <summary>Chosen by the client and copied into the response, so the client can match the two.</summary>
        [MetaMember(1)] public int RequestId { get; private set; }

        public TournamentJoinRequest() { }
        public TournamentJoinRequest(int requestId) { RequestId = requestId; }
    }

    /// <summary>Server to client: whether the join succeeded, and the refusal reason if it did not.</summary>
    [MetaMessage(MessageCodes.TournamentJoinResponse, MessageDirection.ServerToClient)]
    public class TournamentJoinResponse : MetaMessage
    {
        [MetaMember(1)] public bool                  Joined    { get; private set; }
        [MetaMember(2)] public TournamentJoinRefusal Refusal   { get; private set; }

        /// <summary>The <see cref="TournamentJoinRequest.RequestId"/> this answers.</summary>
        [MetaMember(3)] public int                   RequestId { get; private set; }

        public TournamentJoinResponse() { }

        public TournamentJoinResponse(int requestId, bool joined, TournamentJoinRefusal refusal)
        {
            RequestId = requestId;
            Joined    = joined;
            Refusal   = refusal;
        }

        public static TournamentJoinResponse Success(int requestId) => new TournamentJoinResponse(requestId, true, TournamentJoinRefusal.None);

        public static TournamentJoinResponse Refused(int requestId, TournamentJoinRefusal refusal) => new TournamentJoinResponse(requestId, false, refusal);
    }

    /// <summary>What a claim is for.</summary>
    [MetaSerializable]
    public enum TournamentClaimKind
    {
        /// <summary>A participation milestone of the joined season.</summary>
        Milestone = 0,

        /// <summary>The placement reward of a concluded season.</summary>
        Placement = 1,
    }

    /// <summary>
    /// Client to server: pay a milestone, or the placement reward of a concluded season.
    /// <para>
    /// The payment is a synchronized server action, and this message asks the server to run it. A client action
    /// could check against tournament progress the client has not received yet
    /// (see <see cref="PlayerSynchronizedServerAction"/>).
    /// </para>
    /// </summary>
    [MetaMessage(MessageCodes.TournamentClaimRequest, MessageDirection.ClientToServer), MessageRoutingRuleOwnedPlayer]
    public class TournamentClaimRequest : MetaMessage
    {
        [MetaMember(1)] public TournamentClaimKind Kind { get; private set; }

        /// <summary>Which milestone, for <see cref="TournamentClaimKind.Milestone"/>.</summary>
        [MetaMember(2)] public int MilestoneIndex { get; private set; }

        /// <summary>Which concluded division, for <see cref="TournamentClaimKind.Placement"/>.</summary>
        [MetaMember(3)] public EntityId DivisionId { get; private set; }

        /// <summary>Chosen by the client and copied into the response, so the client can match the two.</summary>
        [MetaMember(4)] public int RequestId { get; private set; }

        public TournamentClaimRequest() { }

        public static TournamentClaimRequest ForMilestone(int milestone) =>
            new TournamentClaimRequest { Kind = TournamentClaimKind.Milestone, MilestoneIndex = milestone };

        public static TournamentClaimRequest ForPlacement(EntityId divisionId) =>
            new TournamentClaimRequest { Kind = TournamentClaimKind.Placement, DivisionId = divisionId };

        /// <summary>Returns a copy of this request that carries <paramref name="requestId"/>.</summary>
        public TournamentClaimRequest WithRequestId(int requestId) =>
            new TournamentClaimRequest { Kind = Kind, MilestoneIndex = MilestoneIndex, DivisionId = DivisionId, RequestId = requestId };
    }

    /// <summary>Server to client: whether the claim was paid, and the refusal if it was not.</summary>
    [MetaMessage(MessageCodes.TournamentClaimResponse, MessageDirection.ServerToClient)]
    public class TournamentClaimResponse : MetaMessage
    {
        [MetaMember(1)] public TournamentClaimKind Kind { get; private set; }
        [MetaMember(2)] public bool                Paid { get; private set; }

        /// <summary>The refusal's name from <c>ActionResults</c>, or null when the claim was paid.</summary>
        [MetaMember(3)] public string Refusal { get; private set; }

        /// <summary>The <see cref="TournamentClaimRequest.RequestId"/> this answers.</summary>
        [MetaMember(4)] public int RequestId { get; private set; }

        public TournamentClaimResponse() { }

        public TournamentClaimResponse(int requestId, TournamentClaimKind kind, bool paid, string refusal)
        {
            RequestId = requestId;
            Kind      = kind;
            Paid      = paid;
            Refusal   = refusal;
        }
    }
}
