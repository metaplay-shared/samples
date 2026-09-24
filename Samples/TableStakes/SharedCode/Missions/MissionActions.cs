using Metaplay.Core;
using Metaplay.Core.Analytics;
using Metaplay.Core.Model;

namespace Game.Logic
{
    public static partial class ActionCodes
    {
        // Mission action. SharedCode/Player/PlayerActions.cs lists every player action code, so check it before
        // taking a new code.
        public const int PlayerClaimMissionReward = 5008;
    }

    /// <summary>
    /// Pays the reward of one completed mission (<c>docs/missions.md</c>). It is a client action, so it runs on the
    /// client and the server at the same tick. The wallet is checksummed, so the unsynchronized server action that
    /// delivers a finished match only marks the reward ready, and this action is the only thing that pays it.
    /// <para>
    /// The payload is only a mission instance ID, and the reward is read from player state and config, so a client
    /// cannot supply wrong values. The commit that pays also marks the mission claimed, and
    /// <see cref="PlayerMissionState.ResolveClaim"/> refuses a claimed mission, so a duplicate claim pays nothing.
    /// </para>
    /// </summary>
    [ModelAction(ActionCodes.PlayerClaimMissionReward)]
    public class PlayerClaimMissionReward : PlayerAction
    {
        public MissionInstanceId InstanceId { get; private set; }

        public PlayerClaimMissionReward() { }

        public PlayerClaimMissionReward(MissionInstanceId instanceId)
        {
            InstanceId = instanceId;
        }

        public override MetaActionResult Execute(PlayerModel player, bool commit)
        {
            PlayerMissionState missions = player.Missions;

            MetaActionResult resolved = missions.ResolveClaim(player.GameConfig, InstanceId, player.CurrentTime, out MissionClaim claim);
            if (resolved != MetaActionResult.Success)
                return resolved;

            // One correlation ID for the whole claim. The wallet's currency events and the claim event below
            // carry it. It is computed from the model's current time, which is the same on the client and the
            // server because both run this action at the same tick (docs/analytics.md, "Correlation id").
            AnalyticsCorrelationId correlation = AnalyticsCorrelationId.Create(player.PlayerId, player.CurrentTime, "mission_reward");

            // Apply the wallet change on both passes and before any other write, so when the wallet refuses the
            // grant the mission stays completed and unclaimed.
            MetaActionResult granted = player.ApplyWallet(
                WalletTransaction.Grant(EconomyFeature.Missions, EconomyReason.MissionReward, claim.Reward, EconomyContentId.FromString(claim.Progress.Id.Value)),
                commit,
                correlation);

            if (granted != MetaActionResult.Success)
                return granted;

            if (commit)
            {
                missions.ApplyClaim(claim, player.CurrentTime);

                player.EventStream.Event(new PlayerEventMissionRewardClaimed(
                    correlation, claim.InstanceId, claim.Activation.SetId, claim.Progress.Id, claim.Activation.Cadence,
                    missions.ClaimCount, claim.IsLateClaim));

                player.ClientListener.OnMissionsChanged();
            }

            return MetaActionResult.Success;
        }
    }
}
