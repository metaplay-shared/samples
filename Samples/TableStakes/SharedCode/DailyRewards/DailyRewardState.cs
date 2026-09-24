using Metaplay.Core;
using Metaplay.Core.Model;

namespace Game.Logic
{
    /// <summary>
    /// One player's daily reward progress (<c>docs/daily-rewards.md</c>).
    /// <para>
    /// Only the internal <see cref="Apply"/> writes this state, called from
    /// <see cref="PlayerModel.ApplyDailyRewardClaim"/>, which throws for an activation that was already claimed.
    /// It stores no countdown to the next reward, so availability cannot disagree with the published schedule.
    /// </para>
    /// </summary>
    [MetaSerializable]
    public class DailyRewardState
    {
        /// <summary>
        /// The activation the player last claimed, as <see cref="DailyActivation.Index"/>. Only valid when
        /// <see cref="HasClaimed"/> is true, because zero is also a valid activation index.
        /// </summary>
        [MetaMember(1)] public int LastClaimedActivationIndex { get; private set; }

        /// <summary>When the last claim committed, in server time.</summary>
        [MetaMember(2)] public MetaTime LastClaimedAt { get; private set; }

        /// <summary>Claims in the current streak, including days covered by the skip day. Can exceed the cycle length.</summary>
        [MetaMember(3)] public int StreakDays { get; private set; }

        /// <summary>The 1-based cycle position the last claim paid, or zero before the first claim.</summary>
        [MetaMember(4)] public int LastClaimedStep { get; private set; }

        /// <summary>
        /// Whether the current cycle's skip day has been used. Cleared when the cycle completes or the streak
        /// resets, which are the two ways a new cycle starts.
        /// </summary>
        [MetaMember(5)] public bool SkipDayUsed { get; private set; }

        /// <summary>
        /// How many claims the player has made in total. Only increases. The client compares it with the last
        /// value it saw to decide whether the claim reveal animation has already played.
        /// </summary>
        [MetaMember(6)] public int ClaimCount { get; private set; }

        /// <summary>The reward table the last claim was paid from, so the client can show the paid reward after a reconnect.</summary>
        [MetaMember(7)] public DailyRewardTableId LastClaimedTable { get; private set; }

        /// <summary>The stable ID of the step the last claim paid.</summary>
        [MetaMember(8)] public DailyRewardStepId LastClaimedStepId { get; private set; }

        public DailyRewardState() { }

        /// <summary>Whether the player has ever claimed. The "last claimed" members are only valid when this is true.</summary>
        public bool HasClaimed => ClaimCount > 0;

        /// <summary>
        /// Records one committed claim. See the class summary for who may call it.
        /// </summary>
        internal void Apply(DailyRewardClaim claim, MetaTime at, DailyRewardTableId tableId)
        {
            LastClaimedActivationIndex = claim.ActivationIndex;
            LastClaimedAt              = at;
            StreakDays                 = claim.StreakAfter;
            LastClaimedStep            = claim.Step;
            SkipDayUsed                = claim.SkipDayUsedAfter;
            ClaimCount           += 1;
            LastClaimedTable      = tableId;
            LastClaimedStepId     = claim.StepId;
        }

        public override string ToString() =>
            HasClaimed
                ? $"streak {StreakDays}, step {LastClaimedStep}, activation {LastClaimedActivationIndex}, skip day {(SkipDayUsed ? "used" : "available")}, {ClaimCount} claims"
                : "never claimed";
    }
}
