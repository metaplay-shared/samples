using Metaplay.Core;
using Metaplay.Core.Analytics;
using Metaplay.Core.Model;

namespace Game.Logic
{
    public static partial class ActionCodes
    {
        // Weekly themed event action. SharedCode/Player/PlayerActions.cs lists every player action code, so check
        // it before taking a new code.
        public const int PlayerClaimWeeklyEventReward = 5013;
    }

    /// <summary>
    /// Pays the reward of a weekly themed event whose target the player has reached. Reaching the target only
    /// marks the reward ready, and this action is the only thing that pays it (<c>docs/weekly-event.md</c>).
    /// <para>
    /// It can be a client action because its checks never pass on the client while failing on the server. The
    /// server scores matches before the client does, so its points are never lower. The claimed flag, the event
    /// phase and the wallet all change at the same tick on both sides (<c>docs/player.md</c>, "Action base
    /// classes"). The payload is only an event ID: the reward is read from the event's stored content. The commit
    /// that pays also marks the event claimed, so claiming twice pays once.
    /// </para>
    /// </summary>
    [ModelAction(ActionCodes.PlayerClaimWeeklyEventReward)]
    public class PlayerClaimWeeklyEventReward : PlayerAction
    {
        /// <summary>The event's SDK occurrence ID. This is the only payload field.</summary>
        public MetaGuid EventId { get; private set; }

        public PlayerClaimWeeklyEventReward() { }

        public PlayerClaimWeeklyEventReward(MetaGuid eventId)
        {
            EventId = eventId;
        }

        public override MetaActionResult Execute(PlayerModel player, bool commit)
        {
            PlayerWeeklyEventState weekly = player.WeeklyEvent;

            MetaActionResult resolved = weekly.ResolveClaim(player.LiveOpsEvents, EventId, out WeeklyEventClaim claim);
            if (resolved != MetaActionResult.Success)
                return resolved;

            // One correlation ID for the whole claim. The wallet's currency events and the claim event below
            // carry it, so they can be joined (docs/analytics.md, "Correlation id").
            AnalyticsCorrelationId correlation = AnalyticsCorrelationId.Create(player.PlayerId, player.CurrentTime, "weekly_event_reward");

            // Apply the wallet change on both passes and before any other write, so when the wallet refuses the
            // grant the reward stays earned and unclaimed.
            MetaActionResult granted = player.ApplyWallet(
                WalletTransaction.Grant(
                    EconomyFeature.WeeklyEvent,
                    EconomyReason.WeeklyEventReward,
                    claim.Reward,
                    EconomyContentId.FromString(EventId.ToString())),
                commit,
                correlation);

            if (granted != MetaActionResult.Success)
                return granted;

            if (commit)
            {
                weekly.ApplyClaim(claim, player.CurrentTime);

                player.EventStream.Event(new PlayerEventWeeklyEventRewardClaimed(
                    correlation,
                    EventId,
                    claim.Content.TargetPoints,
                    claim.Progress.Points,
                    weekly.ClaimCount));

                player.ClientListener.OnWeeklyEventChanged();
            }

            return MetaActionResult.Success;
        }

        public override string ToString() => $"claim weekly-event reward for {EventId}";
    }
}
