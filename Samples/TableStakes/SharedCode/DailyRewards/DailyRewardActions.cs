using Metaplay.Core;
using Metaplay.Core.Model;

namespace Game.Logic
{
    public static partial class ActionResults
    {
        /// <summary>The reward for this activation was already claimed. This makes claiming idempotent.</summary>
        public static readonly MetaActionResult DailyRewardAlreadyClaimed = new MetaActionResult(nameof(DailyRewardAlreadyClaimed));

        /// <summary>The published config has no usable reward table. Nothing is claimed and the streak does not change.</summary>
        public static readonly MetaActionResult DailyRewardUnavailable = new MetaActionResult(nameof(DailyRewardUnavailable));
    }

    /// <summary>
    /// Pays one daily reward and advances the streak.
    /// <para>
    /// It is a synchronized server action because it changes checksummed wallet balances, which both sides must
    /// change at the same tick. The payload carries only the activation and the time. The step, streak and reward
    /// are computed from the player's state and the published table, so a client cannot get paid more, twice, or
    /// early (<c>docs/daily-rewards.md</c>). <see cref="DailyRewardPolicy.ClaimFor"/> refuses a second claim for
    /// the same activation before the wallet changes.
    /// </para>
    /// </summary>
    [ModelAction(ActionCodes.PlayerDailyRewardClaimed)]
    public class PlayerDailyRewardClaimed : PlayerSynchronizedServerAction
    {
        /// <summary>The player-local day being claimed, as <see cref="DailyActivation.Index"/>.</summary>
        public int ActivationIndex { get; private set; }

        /// <summary>The server time when it accepted the claim. Stored in the state. Never taken from the client.</summary>
        public MetaTime ClaimedAt { get; private set; }

        public PlayerDailyRewardClaimed() { }

        public PlayerDailyRewardClaimed(int activation, MetaTime claimedAt)
        {
            ActivationIndex = activation;
            ClaimedAt       = claimedAt;
        }

        public override MetaActionResult Execute(PlayerModel player, bool commit)
        {
            DailyRewardTableInfo table = DailyRewardPolicy.ActiveTable(player.GameConfig);

            DailyRewardClaim claim = DailyRewardPolicy.ClaimFor(player.DailyReward, table, ActivationIndex, out DailyRewardRefusal refusal);

            if (refusal == DailyRewardRefusal.AlreadyClaimed)
                return ActionResults.DailyRewardAlreadyClaimed;
            if (refusal != DailyRewardRefusal.None)
                return ActionResults.DailyRewardUnavailable;

            // One correlation ID for the whole claim, computed from the server's timestamp so the client and
            // the server create the same ID. The wallet's currency events and the claim event below carry it.
            AnalyticsCorrelationId correlation = AnalyticsCorrelationId.Create(player.PlayerId, ClaimedAt, "daily_reward");

            WalletTransaction grant = WalletTransaction.Grant(
                EconomyFeature.DailyReward,
                EconomyReason.DailyReward,
                claim.Reward,
                EconomyContentId.FromString(claim.StepId.Value));

            // Apply the wallet change on both passes and before any other write, so when the wallet refuses the
            // grant (for example, a balance cap) the activation stays unclaimed and the streak is unchanged.
            MetaActionResult result = player.ApplyWallet(grant, commit, correlation);
            if (result != MetaActionResult.Success)
                return result;

            if (commit)
            {
                // ApplyDailyRewardClaim is the only place the daily reward state is written. It throws if the
                // activation is already claimed, so "once per activation" does not depend on this method.
                player.ApplyDailyRewardClaim(claim, ClaimedAt, table.Id);

                player.EventStream.Event(new PlayerEventDailyRewardClaimed(
                    table.Id, claim.StepId, claim.Step,
                    claim.StreakBefore, claim.StreakAfter,
                    claim.ResetsStreak, claim.UsesSkipDay, claim.CompletesCycle,
                    player.DailyReward.ClaimCount,
                    correlation));
            }

            return MetaActionResult.Success;
        }

        public override string ToString() => $"claim daily reward for activation {ActivationIndex}";
    }
}
