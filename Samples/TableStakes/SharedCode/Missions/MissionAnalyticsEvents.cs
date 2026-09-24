using Metaplay.Core.Analytics;
using Metaplay.Core.Model;
using Metaplay.Core.Player;

namespace Game.Logic
{
    /// <summary>
    /// A mission reached its target, so its reward became claimable. It is emitted only on completion, not on each
    /// progress step, because analytics needs to know which missions players finish (<c>docs/analytics.md</c>). One
    /// match can complete several missions, and each emits its own event. It has no correlation ID, because completion
    /// grants nothing and the match already emits <c>match_finished</c> at the same time. The claim event, which
    /// causes wallet events, carries the correlation ID.
    /// </summary>
    [AnalyticsEvent(AnalyticsEventCodes.MissionCompleted, displayName: "Mission completed", docString: "A mission's target was crossed for the first time, so its reward is now waiting to be claimed.")]
    [AnalyticsAlias("mission_completed")]
    [AnalyticsEventKeywords(AnalyticsKeywords.Mission, AnalyticsKeywords.Progression)]
    public class PlayerEventMissionCompleted : PlayerEventBase
    {
        /// <summary>The mission instance: one mission in one activation.</summary>
        [MetaMember(1)] public MissionInstanceId Instance { get; private set; }

        /// <summary>The mission set assigned to the player when the activation began.</summary>
        [MetaMember(2)] public MissionSetId SetId { get; private set; }

        [MetaMember(3)] public MissionId        Mission   { get; private set; }
        [MetaMember(4)] public MissionCadence   Cadence   { get; private set; }
        [MetaMember(5)] public MissionObjective Objective { get; private set; }

        /// <summary>The configured target, included so the event can be read without the game config.</summary>
        [MetaMember(6)] public int Target { get; private set; }

        [MetaMember(7)] public int ProgressBefore { get; private set; }
        [MetaMember(8)] public int ProgressAfter  { get; private set; }

        public override string EventDescription => $"Finished {Mission} ({Objective} {ProgressAfter}/{Target}) on the {Cadence} set {SetId}.";

        public PlayerEventMissionCompleted() { }

        public PlayerEventMissionCompleted(
            MissionInstanceId instance,
            MissionSetId      setId,
            MissionId         mission,
            MissionCadence    cadence,
            MissionObjective  objective,
            int               target,
            int               progressBefore,
            int               progressAfter)
        {
            Instance       = instance;
            SetId          = setId;
            Mission        = mission;
            Cadence        = cadence;
            Objective      = objective;
            Target         = target;
            ProgressBefore = progressBefore;
            ProgressAfter  = progressAfter;
        }
    }

    /// <summary>
    /// A mission reward was paid. Emitted once, by the commit that pays it, with the same correlation ID as the
    /// wallet's <c>economy_transaction</c> events for the payment.
    /// </summary>
    [AnalyticsEvent(AnalyticsEventCodes.MissionRewardClaimed, displayName: "Mission reward claimed", docString: "The player claimed a finished mission's reward, and the wallet was credited under the same correlation key.")]
    [AnalyticsAlias("mission_reward_claimed")]
    [AnalyticsEventKeywords(AnalyticsKeywords.Mission, AnalyticsKeywords.Claim, AnalyticsKeywords.Source)]
    public class PlayerEventMissionRewardClaimed : PlayerEventBase
    {
        /// <summary>The same ID as the wallet events of this claim.</summary>
        [MetaMember(1)] public AnalyticsCorrelationId Correlation { get; private set; }

        [MetaMember(2)] public MissionInstanceId Instance { get; private set; }
        [MetaMember(3)] public MissionSetId      SetId    { get; private set; }
        [MetaMember(4)] public MissionId         Mission  { get; private set; }
        [MetaMember(5)] public MissionCadence    Cadence  { get; private set; }

        /// <summary>How many mission rewards the player has been paid, including this one.</summary>
        [MetaMember(6)] public int ClaimOrdinal { get; private set; }

        /// <summary>Whether the reward was claimed after its activation ended, within <see cref="PlayerMissionState.LateClaimWindow"/>.</summary>
        [MetaMember(7)] public bool InGrace { get; private set; }

        public override string EventDescription => $"Claimed {Mission}'s reward{(InGrace ? " during the late-claim window" : "")}; claim number {ClaimOrdinal}.";

        public PlayerEventMissionRewardClaimed() { }

        public PlayerEventMissionRewardClaimed(
            AnalyticsCorrelationId correlation,
            MissionInstanceId      instance,
            MissionSetId           setId,
            MissionId              mission,
            MissionCadence         cadence,
            int                    claimOrdinal,
            bool                   inGrace)
        {
            Correlation  = correlation;
            Instance     = instance;
            SetId        = setId;
            Mission      = mission;
            Cadence      = cadence;
            ClaimOrdinal = claimOrdinal;
            InGrace      = inGrace;
        }
    }
}
