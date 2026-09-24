using Metaplay.Core;
using Metaplay.Core.Analytics;
using Metaplay.Core.Model;

namespace Game.Logic
{
    public static partial class ActionCodes
    {
        // First-week event action. SharedCode/Player/PlayerActions.cs lists every player action code, so check
        // it before taking a new code.
        public const int PlayerClaimFirstWeekReward = 5009;
    }

    /// <summary>
    /// Pays the reward of one completed first-week day.
    /// <para>
    /// It is a client action, so it changes the checksummed wallet at the same tick on both sides. The
    /// unsynchronized server action that delivers a finished match only marks a day's reward ready
    /// (<c>docs/first-week-event.md</c>). The payload is only a day ID. The reward is read from the player's state
    /// and assigned schedule, and <see cref="PlayerFirstWeekState.ResolveClaim"/> refuses a claimed day before the
    /// wallet changes.
    /// </para>
    /// </summary>
    [ModelAction(ActionCodes.PlayerClaimFirstWeekReward)]
    public class PlayerClaimFirstWeekReward : PlayerAction
    {
        public FirstWeekDayId DayId { get; private set; }

        public PlayerClaimFirstWeekReward() { }

        public PlayerClaimFirstWeekReward(FirstWeekDayId dayId)
        {
            DayId = dayId;
        }

        public override MetaActionResult Execute(PlayerModel player, bool commit)
        {
            PlayerFirstWeekState firstWeek = player.FirstWeek;

            MetaActionResult resolved = firstWeek.ResolveClaim(player.GameConfig, DayId, out FirstWeekClaim claim);
            if (resolved != MetaActionResult.Success)
                return resolved;

            // One correlation ID for the whole claim. The wallet's currency events and the claim event below
            // carry it, so they can be joined (docs/analytics.md, "Correlation id").
            AnalyticsCorrelationId correlation = AnalyticsCorrelationId.Create(player.PlayerId, player.CurrentTime, "first_week_reward");

            // Apply the wallet change on both passes and before any other write, so when the wallet refuses the
            // grant the day stays completed and unclaimed.
            MetaActionResult granted = player.ApplyWallet(
                WalletTransaction.Grant(
                    EconomyFeature.FirstWeekEvent,
                    EconomyReason.FirstWeekReward,
                    claim.Reward,
                    EconomyContentId.FromString(claim.DayId.Value)),
                commit,
                correlation);

            if (granted != MetaActionResult.Success)
                return granted;

            if (commit)
            {
                firstWeek.ApplyClaim(claim, player.CurrentTime);

                player.EventStream.Event(new PlayerEventFirstWeekRewardClaimed(
                    correlation,
                    firstWeek.ScheduleId,
                    claim.DayId,
                    claim.Day.Day,
                    firstWeek.ClaimCount,
                    firstWeek.ClaimedDayCount,
                    firstWeek.MissedDaysAt(player.GameConfig, player.CurrentTime),
                    claim.Day.Day == FirstWeekScheduleInfo.NumDays));

                player.ClientListener.OnFirstWeekChanged();
            }

            return MetaActionResult.Success;
        }

        public override string ToString() => $"claim first-week reward for {DayId}";
    }
}
