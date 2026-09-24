using Metaplay.Core.Analytics;
using Metaplay.Core.Model;
using Metaplay.Core.Player;

namespace Game.Logic
{
    /// <summary>
    /// The player claimed a daily reward. Records the streak before and after the claim.
    /// Emitted once per committed claim.
    /// <para>
    /// It does not list the granted rewards. The wallet's <c>economy_transaction</c> events record them with the
    /// same correlation ID, and a second copy here could disagree with the balance.
    /// </para>
    /// </summary>
    [AnalyticsEvent(AnalyticsEventCodes.DailyRewardClaimed, displayName: "Daily reward claimed", docString: "The player claimed a daily login reward, with the streak and cycle position it landed on.")]
    [AnalyticsAlias("daily_reward_claimed")]
    [AnalyticsEventKeywords(AnalyticsKeywords.Reward, AnalyticsKeywords.Claim, AnalyticsKeywords.Progression)]
    public class PlayerEventDailyRewardClaimed : PlayerEventBase
    {
        /// <summary>The published reward table the claim was paid from.</summary>
        [MetaMember(1)] public DailyRewardTableId Table { get; private set; }

        /// <summary>The stable ID of the step that paid. Join on this, not on <see cref="Step"/>, which is only its position.</summary>
        [MetaMember(2)] public DailyRewardStepId StepId { get; private set; }

        /// <summary>The step's 1-based position in the cycle, up to <see cref="DailyRewardTableInfo.NumSteps"/>.</summary>
        [MetaMember(3)] public int Step { get; private set; }

        [MetaMember(4)] public int StreakBefore { get; private set; }
        [MetaMember(5)] public int StreakAfter  { get; private set; }

        /// <summary>Whether this claim restarted the streak and the cycle.</summary>
        [MetaMember(6)] public bool ResetStreak { get; private set; }

        /// <summary>Whether this claim used the cycle's skip day to cover one missed day.</summary>
        [MetaMember(7)] public bool UsedGrace { get; private set; }

        /// <summary>Whether this claim paid the last step of the cycle, which restores the skip day.</summary>
        [MetaMember(8)] public bool CompletedCycle { get; private set; }

        /// <summary>How many claims the player has made in total, including this one. Only increases.</summary>
        [MetaMember(9)] public int ClaimOrdinal { get; private set; }

        /// <summary>The same ID as the currency events of this claim, so they can be joined.</summary>
        [MetaMember(10)] public AnalyticsCorrelationId Correlation { get; private set; }

        public override string EventDescription =>
            $"Claimed daily reward step {Step} on a {StreakAfter}-day streak"
            + (UsedGrace ? ", bridging one missed day" : "")
            + (ResetStreak ? ", after the previous streak ended" : "")
            + (CompletedCycle ? ", completing the cycle" : "")
            + ".";

        public PlayerEventDailyRewardClaimed() { }

        public PlayerEventDailyRewardClaimed(
            DailyRewardTableId     table,
            DailyRewardStepId      stepId,
            int                    step,
            int                    streakBefore,
            int                    streakAfter,
            bool                   resetStreak,
            bool                   usedGrace,
            bool                   completedCycle,
            int                    claimOrdinal,
            AnalyticsCorrelationId correlation)
        {
            Table          = table;
            StepId         = stepId;
            Step           = step;
            StreakBefore   = streakBefore;
            StreakAfter    = streakAfter;
            ResetStreak    = resetStreak;
            UsedGrace      = usedGrace;
            CompletedCycle = completedCycle;
            ClaimOrdinal   = claimOrdinal;
            Correlation    = correlation;
        }
    }
}
