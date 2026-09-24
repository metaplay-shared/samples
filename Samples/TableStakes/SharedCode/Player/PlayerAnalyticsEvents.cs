using Metaplay.Core;
using Metaplay.Core.Analytics;
using Metaplay.Core.Model;
using Metaplay.Core.Player;

namespace Game.Logic
{
    /// <summary>
    /// This player was seated at a table.
    /// <para>
    /// The match events in this file are player events rather than server events. The player actor already has
    /// the SDK's analytics handler, and a player event also appears in the player's event log. A table-level
    /// event would need an analytics batcher on the match actor (<c>docs/analytics.md</c>, "Defining an
    /// event").
    /// </para>
    /// </summary>
    [AnalyticsEvent(AnalyticsEventCodes.MatchStarted, displayName: "Match started", docString: "The player took a seat at a table, and what the wait for it cost.")]
    [AnalyticsAlias("match_started")]
    [AnalyticsEventKeywords(AnalyticsKeywords.Gameplay)]
    public class PlayerEventMatchStarted : PlayerEventBase
    {
        [MetaMember(1)] public EntityId MatchId { get; private set; }

        /// <summary>The number of seats filled by bots when the table formed.</summary>
        [MetaMember(2)] public int BotSeats { get; private set; }

        /// <summary>How long this player waited in the matchmaking queue before the table formed.</summary>
        [MetaMember(3)] public MetaDuration Waited { get; private set; }

        /// <summary>
        /// The same id as the <see cref="PlayerEventMatchmakingWait"/> that this seating ended. Unset when the
        /// player actor did not see the search start, because then no wait event exists.
        /// </summary>
        [MetaMember(4)] public AnalyticsCorrelationId Correlation { get; private set; }

        public override string EventDescription => $"Seated at {MatchId} with {BotSeats} computer players after {Waited}.";

        public PlayerEventMatchStarted() { }

        public PlayerEventMatchStarted(EntityId matchId, int botSeats, MetaDuration waited, AnalyticsCorrelationId correlation)
        {
            MatchId     = matchId;
            BotSeats    = botSeats;
            Waited      = waited;
            Correlation = correlation;
        }
    }

    /// <summary>
    /// A table this player sat at reached a terminal phase: it ended with a result, or it was abandoned before
    /// it started.
    /// </summary>
    [AnalyticsEvent(AnalyticsEventCodes.MatchFinished, displayName: "Match finished", docString: "A table the player sat at reached a terminal phase, and where they placed at it.")]
    [AnalyticsAlias("match_finished")]
    [AnalyticsEventKeywords(AnalyticsKeywords.Gameplay, AnalyticsKeywords.Progression)]
    public class PlayerEventMatchFinished : PlayerEventBase
    {
        [MetaMember(1)] public EntityId MatchId { get; private set; }

        /// <summary>The terminal phase the table reached.</summary>
        [MetaMember(2)] public MatchPhase Phase { get; private set; }

        /// <summary>The player's zero-based position in the final standings, 0 for the winner, or -1 for an abandoned table.</summary>
        [MetaMember(3)] public int Rank { get; private set; }

        [MetaMember(4)] public int TricksWon { get; private set; }

        [MetaMember(5)] public int HumanOpponents { get; private set; }

        /// <summary>Whether this player was still at the table when the last trick resolved.</summary>
        [MetaMember(6)] public bool FinishedByPlayer { get; private set; }

        /// <summary>Why the player lost the seat during the game, if they did. The record's counters do not store this.</summary>
        [MetaMember(7)] public MatchSeatLossReason SeatLossReason { get; private set; }

        /// <summary>The time from the deal to the end of the table.</summary>
        [MetaMember(8)] public MetaDuration Duration { get; private set; }

        public override string EventDescription =>
            Phase == MatchPhase.Ended
                ? $"Finished {MatchId} at #{Rank + 1} with {TricksWon} tricks in {Duration}."
                : $"Table {MatchId} was abandoned before it started.";

        public PlayerEventMatchFinished() { }

        public PlayerEventMatchFinished(EntityId matchId, MatchPhase phase, int rank, int tricksWon, int humanOpponents, bool finishedByPlayer, MatchSeatLossReason seatLossReason, MetaDuration duration)
        {
            MatchId          = matchId;
            Phase            = phase;
            Rank             = rank;
            TricksWon        = tricksWon;
            HumanOpponents   = humanOpponents;
            FinishedByPlayer = finishedByPlayer;
            SeatLossReason   = seatLossReason;
            Duration         = duration;
        }
    }

    /// <summary>
    /// The player stopped playing their seat, emitted when it happened, with the reason.
    /// <para>
    /// The reason is the purpose of this event. A deliberate leave and a closed browser tab leave the seat in
    /// the same state, and only the table knows which one happened.
    /// </para>
    /// </summary>
    [AnalyticsEvent(AnalyticsEventCodes.MatchSeatLost, displayName: "Match seat lost", docString: "The player stopped playing their seat, and why: they left, they timed out, or they went away.")]
    [AnalyticsAlias("match_seat_lost")]
    [AnalyticsEventKeywords(AnalyticsKeywords.Gameplay)]
    public class PlayerEventMatchSeatLost : PlayerEventBase
    {
        [MetaMember(1)] public EntityId            MatchId { get; private set; }
        [MetaMember(2)] public MatchSeatLossReason Reason  { get; private set; }

        public override string EventDescription => $"Seat at {MatchId} lost: {Reason}.";

        public PlayerEventMatchSeatLost() { }

        public PlayerEventMatchSeatLost(EntityId matchId, MatchSeatLossReason reason)
        {
            MatchId = matchId;
            Reason  = reason;
        }
    }

    /// <summary>
    /// A matchmaking search ended: the player was seated, cancelled, or the search ended without a table.
    /// </summary>
    [AnalyticsEvent(AnalyticsEventCodes.MatchmakingWait, displayName: "Matchmaking wait", docString: "A matchmaking search ended, however it ended: seated, cancelled, or given up on.")]
    [AnalyticsAlias("matchmaking_wait")]
    [AnalyticsEventKeywords(AnalyticsKeywords.Gameplay)]
    public class PlayerEventMatchmakingWait : PlayerEventBase
    {
        /// <summary>The time from the player tapping Play to the end of the search.</summary>
        [MetaMember(1)] public MetaDuration Waited { get; private set; }

        /// <summary>The number of human players seated at the formed table, including this player. Zero if no table formed.</summary>
        [MetaMember(2)] public int HumansGathered { get; private set; }

        /// <summary>Whether the wait ended in a seat at a table.</summary>
        [MetaMember(3)] public bool WasSeated { get; private set; }

        /// <summary>
        /// Set only when the player was seated, and then the same id as the <see cref="PlayerEventMatchStarted"/>
        /// for that seat. Unset otherwise, because no related event exists.
        /// </summary>
        [MetaMember(4)] public AnalyticsCorrelationId Correlation { get; private set; }

        public override string EventDescription =>
            WasSeated ? $"Waited {Waited} and was seated with {HumansGathered - 1} other people."
                      : $"Waited {Waited} and was not seated.";

        public PlayerEventMatchmakingWait() { }

        public PlayerEventMatchmakingWait(MetaDuration waited, int humansGathered, bool wasSeated, AnalyticsCorrelationId correlation)
        {
            Waited         = waited;
            HumansGathered = humansGathered;
            WasSeated      = wasSeated;
            Correlation    = correlation;
        }
    }
}
