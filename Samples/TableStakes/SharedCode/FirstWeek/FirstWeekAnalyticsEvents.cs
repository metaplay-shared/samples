using Metaplay.Core.Analytics;
using Metaplay.Core.Model;
using Metaplay.Core.Player;

namespace Game.Logic
{
    /// <summary>
    /// A first-week day's goal was met within the day's window, so its reward became claimable.
    /// Emitted only when the day completes, not on each progress step (<c>docs/analytics.md</c>).
    /// <para>
    /// It has no correlation ID, because completion grants nothing and the finished match already emits
    /// <c>match_finished</c>. The claim event, which causes wallet events, carries the correlation ID.
    /// </para>
    /// </summary>
    [AnalyticsEvent(AnalyticsEventCodes.FirstWeekDayCompleted, displayName: "First-week day completed", docString: "A first-week day's match goal was met inside its window, so its reward is now waiting to be claimed.")]
    [AnalyticsAlias("first_week_day_completed")]
    [AnalyticsEventKeywords(AnalyticsKeywords.Reward, AnalyticsKeywords.Progression)]
    public class PlayerEventFirstWeekDayCompleted : PlayerEventBase
    {
        /// <summary>The schedule assigned to the player when their first week began.</summary>
        [MetaMember(1)] public FirstWeekScheduleId Schedule { get; private set; }

        /// <summary>The stable ID of the completed day. Join on this, not on <see cref="Day"/>, which is only its position.</summary>
        [MetaMember(2)] public FirstWeekDayId DayId { get; private set; }

        /// <summary>The day's 1-based position in the week, up to <see cref="FirstWeekScheduleInfo.NumDays"/>.</summary>
        [MetaMember(3)] public int Day { get; private set; }

        /// <summary>The configured match goal, included so the event can be read without the game config.</summary>
        [MetaMember(4)] public int Target { get; private set; }

        [MetaMember(5)] public int ProgressBefore { get; private set; }
        [MetaMember(6)] public int ProgressAfter  { get; private set; }

        /// <summary>
        /// The zero-based index of the player's personal day window the match completed in. It is computed from
        /// the player's start time, not from the schedule. For a schedule whose days are in order it equals
        /// <see cref="Day"/> minus one.
        /// </summary>
        [MetaMember(7)] public int DayIndex { get; private set; }

        /// <summary>Whether this is the last day of the week.</summary>
        [MetaMember(8)] public bool IsFinalDay { get; private set; }

        public override string EventDescription =>
            $"Completed first-week day {Day} ({ProgressAfter}/{Target} matches) on schedule {Schedule}.";

        public PlayerEventFirstWeekDayCompleted() { }

        public PlayerEventFirstWeekDayCompleted(
            FirstWeekScheduleId schedule,
            FirstWeekDayId      dayId,
            int                 day,
            int                 target,
            int                 progressBefore,
            int                 progressAfter,
            int                 dayIndex,
            bool                isFinalDay)
        {
            Schedule       = schedule;
            DayId          = dayId;
            Day            = day;
            Target         = target;
            ProgressBefore = progressBefore;
            ProgressAfter  = progressAfter;
            DayIndex       = dayIndex;
            IsFinalDay     = isFinalDay;
        }
    }

    /// <summary>
    /// A first-week reward was paid. Emitted once, by the commit that pays it, with the same correlation ID as
    /// the wallet's <c>economy_transaction</c> events for the payment.
    /// <para>
    /// It does <b>not</b> list the granted rewards. The wallet events record them, and a second copy here could
    /// disagree with the balance.
    /// </para>
    /// </summary>
    [AnalyticsEvent(AnalyticsEventCodes.FirstWeekRewardClaimed, displayName: "First-week reward claimed", docString: "The player claimed a completed first-week day's reward, and the wallet was credited under the same correlation key.")]
    [AnalyticsAlias("first_week_reward_claimed")]
    [AnalyticsEventKeywords(AnalyticsKeywords.Reward, AnalyticsKeywords.Claim, AnalyticsKeywords.Source)]
    public class PlayerEventFirstWeekRewardClaimed : PlayerEventBase
    {
        /// <summary>The same ID as the wallet events of this claim.</summary>
        [MetaMember(1)] public AnalyticsCorrelationId Correlation { get; private set; }

        [MetaMember(2)] public FirstWeekScheduleId Schedule { get; private set; }
        [MetaMember(3)] public FirstWeekDayId      DayId    { get; private set; }
        [MetaMember(4)] public int                 Day      { get; private set; }

        /// <summary>How many first-week rewards the player has been paid, including this one.</summary>
        [MetaMember(5)] public int ClaimOrdinal { get; private set; }

        /// <summary>How many days are claimed, including this one.</summary>
        [MetaMember(6)] public int ClaimedDays { get; private set; }

        /// <summary>How many day windows had closed without their goal being met at the time of the claim.</summary>
        [MetaMember(7)] public int MissedDays { get; private set; }

        [MetaMember(8)] public bool IsFinalDay { get; private set; }

        public override string EventDescription =>
            $"Claimed first-week day {Day}'s reward; claim number {ClaimOrdinal}, {ClaimedDays} of {FirstWeekScheduleInfo.NumDays} days collected.";

        public PlayerEventFirstWeekRewardClaimed() { }

        public PlayerEventFirstWeekRewardClaimed(
            AnalyticsCorrelationId correlation,
            FirstWeekScheduleId    schedule,
            FirstWeekDayId         dayId,
            int                    day,
            int                    claimOrdinal,
            int                    claimedDays,
            int                    missedDays,
            bool                   isFinalDay)
        {
            Correlation  = correlation;
            Schedule     = schedule;
            DayId        = dayId;
            Day          = day;
            ClaimOrdinal = claimOrdinal;
            ClaimedDays  = claimedDays;
            MissedDays   = missedDays;
            IsFinalDay   = isFinalDay;
        }
    }
}
