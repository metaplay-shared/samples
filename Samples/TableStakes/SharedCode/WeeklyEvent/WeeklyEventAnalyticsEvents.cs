using Metaplay.Core;
using Metaplay.Core.Analytics;
using Metaplay.Core.Model;
using Metaplay.Core.Player;

namespace Game.Logic
{
    /// <summary>
    /// A weekly themed event's points target was reached, so its reward became claimable. It is emitted once per
    /// event, when the stored <see cref="WeeklyEventProgress.TargetReached"/> flag is set, not on each scoring match.
    /// <para>
    /// The theme name is not included because it is operator-written free text, which analytics events must not
    /// contain. Join on <see cref="EventId"/> instead. It has no correlation ID because reaching the target grants
    /// nothing, and the same match already emits <c>match_finished</c>.
    /// </para>
    /// </summary>
    [AnalyticsEvent(AnalyticsEventCodes.WeeklyEventTargetReached, displayName: "Weekly event target reached", docString: "A weekly themed event's points target was crossed, so its reward is now waiting to be claimed.")]
    [AnalyticsAlias("weekly_event_target_reached")]
    [AnalyticsEventKeywords(AnalyticsKeywords.LiveOpsEvent, AnalyticsKeywords.Progression)]
    public class PlayerEventWeeklyEventTargetReached : PlayerEventBase
    {
        /// <summary>
        /// The event's SDK occurrence ID. Use it to join with the LiveOps timeline, for example to get the
        /// theme name.
        /// </summary>
        [MetaMember(1)] public MetaGuid EventId { get; private set; }

        /// <summary>The points target, included so the event can be read without a join.</summary>
        [MetaMember(2)] public int TargetPoints { get; private set; }

        [MetaMember(3)] public int PointsBefore { get; private set; }
        [MetaMember(4)] public int PointsAfter  { get; private set; }

        /// <summary>How many scoring matches it took to reach the target.</summary>
        [MetaMember(5)] public int ScoringMatches { get; private set; }

        public override string EventDescription =>
            $"Reached the weekly event's target of {TargetPoints} points ({PointsAfter}) in {ScoringMatches} scoring matches.";

        public PlayerEventWeeklyEventTargetReached() { }

        public PlayerEventWeeklyEventTargetReached(MetaGuid eventId, int targetPoints, int pointsBefore, int pointsAfter, int scoringMatches)
        {
            EventId        = eventId;
            TargetPoints   = targetPoints;
            PointsBefore   = pointsBefore;
            PointsAfter    = pointsAfter;
            ScoringMatches = scoringMatches;
        }
    }

    /// <summary>
    /// A weekly themed event's reward was paid. Emitted once, by the commit that pays it, with the same
    /// correlation ID as the wallet's <c>economy_transaction</c> events for the payment.
    /// <para>
    /// It does <b>not</b> list the granted rewards. The wallet events record them, and a second copy here could
    /// disagree with the balance.
    /// </para>
    /// </summary>
    [AnalyticsEvent(AnalyticsEventCodes.WeeklyEventRewardClaimed, displayName: "Weekly event reward claimed", docString: "The player claimed a weekly themed event's reward, and the wallet was credited under the same correlation key.")]
    [AnalyticsAlias("weekly_event_reward_claimed")]
    [AnalyticsEventKeywords(AnalyticsKeywords.LiveOpsEvent, AnalyticsKeywords.Claim, AnalyticsKeywords.Source)]
    public class PlayerEventWeeklyEventRewardClaimed : PlayerEventBase
    {
        /// <summary>The same ID as the wallet events of this claim.</summary>
        [MetaMember(1)] public AnalyticsCorrelationId Correlation { get; private set; }

        [MetaMember(2)] public MetaGuid EventId      { get; private set; }
        [MetaMember(3)] public int    TargetPoints { get; private set; }

        /// <summary>The points the player scored, which is at least the target.</summary>
        [MetaMember(4)] public int PointsScored { get; private set; }

        /// <summary>How many weekly-event rewards the player has been paid, including this one.</summary>
        [MetaMember(5)] public int ClaimOrdinal { get; private set; }

        public override string EventDescription =>
            $"Claimed the weekly event's reward with {PointsScored} of {TargetPoints} points; claim number {ClaimOrdinal}.";

        public PlayerEventWeeklyEventRewardClaimed() { }

        public PlayerEventWeeklyEventRewardClaimed(
            AnalyticsCorrelationId correlation,
            MetaGuid               eventId,
            int                    targetPoints,
            int                    pointsScored,
            int                    claimOrdinal)
        {
            Correlation  = correlation;
            EventId      = eventId;
            TargetPoints = targetPoints;
            PointsScored = pointsScored;
            ClaimOrdinal = claimOrdinal;
        }
    }
}
