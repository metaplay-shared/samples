using Game.Logic;
using Metaplay.Cloud.Entity;
using Metaplay.Core;
using Metaplay.Core.Message;
using Metaplay.Core.Model;

namespace Game.Server.Match
{
    /// <summary>
    /// Asks a table whether it can load. A player's actor sends this before it re-attaches to the table that the
    /// player's state still points at.
    /// <para>
    /// A persisted row that cannot be deserialized, for example after a schema change between deploys, crashes
    /// the table's actor on wake. Without this check, a player pointing at that table would fail the same way on
    /// every login. The player's actor forgets the table if the probe fails with a crash, but keeps it if the
    /// probe times out, because a slow wake under load also times out (<c>docs/match.md</c>, "Lock-out cases").
    /// </para>
    /// </summary>
    [MetaMessage(MessageCodes.MatchProbeRequest, MessageDirection.ServerInternal)]
    public class MatchProbeRequest : EntityAskRequest<MatchProbeResponse>
    {
        public static readonly MatchProbeRequest Instance = new MatchProbeRequest();

        public override string ToString() => "is this table readable?";
    }

    /// <summary>Answer to <see cref="MatchProbeRequest"/>. Receiving it means the table's row loaded.</summary>
    [MetaMessage(MessageCodes.MatchProbeResponse, MessageDirection.ServerInternal)]
    public class MatchProbeResponse : EntityAskResponse
    {
        /// <summary>Whether the match is still being played, as opposed to finished or abandoned.</summary>
        public bool IsPlaying { get; private set; }

        /// <summary>Whether this player still holds a seat here.</summary>
        public bool IsSeated { get; private set; }

        MatchProbeResponse() { }

        public MatchProbeResponse(bool isPlaying, bool isSeated)
        {
            IsPlaying = isPlaying;
            IsSeated  = isSeated;
        }

        public override string ToString() => $"table readable (playing: {IsPlaying}, seated: {IsSeated})";
    }

    /// <summary>
    /// Sent by a finished table to each seated player's actor to record the player's result and clear the
    /// player's pointer to the table.
    /// <para>
    /// The table persists which seats have acknowledged and sends the request again on every wake until each seat
    /// answers, so a restart cannot lose a result. The player's actor records each match only once
    /// (<see cref="Game.Logic.MatchHistoryRules.HasRecorded"/>). An <see cref="MatchPhase.Abandoned"/> table records
    /// nothing but still clears the pointer, because a pointer to a deleted table fails every later login.
    /// </para>
    /// </summary>
    [MetaMessage(MessageCodes.MatchRecordResultRequest, MessageDirection.ServerInternal)]
    public class MatchRecordResultRequest : EntityAskRequest<MatchRecordResultResponse>
    {
        public EntityId   MatchId { get; private set; }
        public MatchPhase Phase   { get; private set; }
        public MetaTime   EndedAt { get; private set; }

        /// <summary>This player's result, or null if the table was abandoned.</summary>
        public MatchSeatResult Result { get; private set; }

        /// <summary>Time from the deal to the end of the game. Used only for analytics.</summary>
        public MetaDuration MatchDuration { get; private set; }

        MatchRecordResultRequest() { }

        public MatchRecordResultRequest(EntityId matchId, MatchPhase phase, MetaTime endedAt, MatchSeatResult result, MetaDuration duration)
        {
            MatchId       = matchId;
            Phase         = phase;
            EndedAt       = endedAt;
            Result        = result;
            MatchDuration = duration;
        }

        public override string ToString() => $"table {MatchId} reached {Phase}: {Result}";
    }

    /// <summary>
    /// Answer to <see cref="MatchRecordResultRequest"/>. The player's actor sends it after the result is recorded
    /// (now or earlier), the pointer is cleared, and the player's state is persisted. The table then marks the
    /// seat as delivered and stops sending the request to it.
    /// </summary>
    [MetaMessage(MessageCodes.MatchRecordResultResponse, MessageDirection.ServerInternal)]
    public class MatchRecordResultResponse : EntityAskResponse
    {
        /// <summary>True if this request recorded the result, false if an earlier request already had.</summary>
        public bool WasNewlyRecorded { get; private set; }

        MatchRecordResultResponse() { }

        public MatchRecordResultResponse(bool wasNewlyRecorded)
        {
            WasNewlyRecorded = wasNewlyRecorded;
        }

        public override string ToString() => WasNewlyRecorded ? "recorded" : "already recorded";
    }

    /// <summary>
    /// Tells a player's actor that the player no longer plays their seat, and why.
    /// <para>
    /// This is a cast because it only feeds analytics. The same information also reaches the player's state
    /// through the seat result in <see cref="MatchRecordResultRequest"/>, so nothing depends on this message
    /// arriving.
    /// </para>
    /// </summary>
    [MetaMessage(MessageCodes.MatchSeatLostNotification, MessageDirection.ServerInternal)]
    public class MatchSeatLostNotification : MetaMessage
    {
        public EntityId            MatchId { get; private set; }
        public MatchSeatLossReason Reason  { get; private set; }

        MatchSeatLostNotification() { }

        public MatchSeatLostNotification(EntityId matchId, MatchSeatLossReason reason)
        {
            MatchId = matchId;
            Reason  = reason;
        }

        public override string ToString() => $"seat at table {MatchId} lost: {Reason}";
    }

    /// <summary>
    /// Tells the actor of a player who used the Leave control that the table has released them, so the actor
    /// clears its pointer and association and the client detaches before the game ends.
    /// <para>
    /// A lost cast only keeps the client attached until its session ends. The table sends it before its next run,
    /// because at an all-bot table that run sends <see cref="MatchRecordResultRequest"/>, which clears the pointer
    /// that the release needs.
    /// </para>
    /// </summary>
    [MetaMessage(MessageCodes.MatchReleaseNotification, MessageDirection.ServerInternal)]
    public class MatchReleaseNotification : MetaMessage
    {
        public EntityId MatchId { get; private set; }

        MatchReleaseNotification() { }

        public MatchReleaseNotification(EntityId matchId)
        {
            MatchId = matchId;
        }

        public override string ToString() => $"table {MatchId} released its leaver";
    }

    /// <summary>
    /// Test only. Moves every pending deadline of the table to now: the join window, each seat's grace period,
    /// the move deadline, and a bot's think delay.
    /// <para>
    /// Only the test endpoint that sends it can reach it. Tests use it to make timers fire on demand instead of
    /// waiting for their configured durations (<c>docs/testing.md</c>, "Forcing timers").
    /// </para>
    /// </summary>
    [MetaMessage(MessageCodes.MatchForceExpireRequest, MessageDirection.ServerInternal)]
    public class MatchForceExpireRequest : EntityAskRequest<MatchForceExpireResponse>
    {
        public static readonly MatchForceExpireRequest Instance = new MatchForceExpireRequest();

        public override string ToString() => "force every pending deadline forward";
    }

    /// <summary>
    /// Answer to <see cref="MatchForceExpireRequest"/>: how many deadlines were moved, and the table's phase and
    /// play index afterwards. Tests poll with this instead of sleeping.
    /// </summary>
    [MetaMessage(MessageCodes.MatchForceExpireResponse, MessageDirection.ServerInternal)]
    public class MatchForceExpireResponse : EntityAskResponse
    {
        public int        NumDeadlinesMoved { get; private set; }
        public MatchPhase Phase             { get; private set; }
        public int        PlayIndex         { get; private set; }

        MatchForceExpireResponse() { }

        public MatchForceExpireResponse(int expired, MatchPhase phase, int playIndex)
        {
            NumDeadlinesMoved = expired;
            Phase             = phase;
            PlayIndex         = playIndex;
        }

        public override string ToString() => $"expired {NumDeadlinesMoved} deadlines; {Phase} at index {PlayIndex}";
    }
}
