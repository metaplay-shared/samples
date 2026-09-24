using Metaplay.Core;
using Metaplay.Core.Model;
using Metaplay.Core.Player;

namespace Game.Logic
{
    /// <summary>
    /// How a claim relates to the previous claim. Together, the values define the streak rules
    /// (<c>docs/daily-rewards.md</c>).
    /// </summary>
    [MetaSerializable]
    public enum DailyStreakTransition
    {
        /// <summary>No claim is possible because this activation was already claimed.</summary>
        None = 0,

        /// <summary>The player's first claim. The streak and the step both become 1.</summary>
        First = 1,

        /// <summary>The activation right after the last claimed one. The streak and the step both advance.</summary>
        Continued = 2,

        /// <summary>Exactly one activation was missed and the cycle's unused skip day covers it.</summary>
        Bridged = 3,

        /// <summary>More than one activation was missed, or the skip day was already used. The streak and cycle restart.</summary>
        Reset = 4,
    }

    /// <summary>Why a claim cannot be made. <see cref="None"/> is the claimable case.</summary>
    [MetaSerializable]
    public enum DailyRewardRefusal
    {
        None = 0,

        /// <summary>The reward for this activation was already claimed.</summary>
        AlreadyClaimed = 1,

        /// <summary>The published schedule has no activation at this time, because it starts in the future.</summary>
        NoActivation = 2,

        /// <summary>The active table or the reset schedule is missing from the published config.</summary>
        ConfigUnavailable = 3,

        /// <summary>The wallet refused the grant, for example because of a balance cap. The activation stays unclaimed.</summary>
        WalletRefused = 4,
    }

    /// <summary>
    /// The effects of one claim, computed before the claim is applied.
    /// <para>
    /// It is computed from the player state and the activation index, never from client input. The claim action
    /// computes it again on both its dry-run and commit passes, so both passes use the same values.
    /// </para>
    /// </summary>
    public readonly struct DailyRewardClaim
    {
        public DailyStreakTransition Transition { get; }

        /// <summary>The activation being claimed.</summary>
        public int ActivationIndex { get; }

        /// <summary>The 1-based cycle position this claim pays, up to <see cref="DailyRewardTableInfo.NumSteps"/>.</summary>
        public int Step { get; }

        public DailyRewardStepId StepId { get; }
        public RewardBundle      Reward { get; }

        /// <summary>The streak before this claim. <see cref="StreakAfter"/> is the streak after it.</summary>
        public int StreakBefore { get; }
        public int StreakAfter       { get; }

        /// <summary>Whether the cycle's skip day is used after this claim commits.</summary>
        public bool SkipDayUsedAfter { get; }

        internal DailyRewardClaim(
            DailyStreakTransition transition,
            int                   activation,
            int                   step,
            DailyRewardStepId     stepId,
            RewardBundle          reward,
            int                   streakBefore,
            int                   streakAfter,
            bool                  skipDayUsedAfter)
        {
            Transition       = transition;
            ActivationIndex  = activation;
            Step             = step;
            StepId           = stepId;
            Reward           = reward;
            StreakBefore     = streakBefore;
            StreakAfter      = streakAfter;
            SkipDayUsedAfter = skipDayUsedAfter;
        }

        public bool Exists => Transition != DailyStreakTransition.None;

        /// <summary>Whether this claim uses the cycle's skip day.</summary>
        public bool UsesSkipDay => Transition == DailyStreakTransition.Bridged;

        /// <summary>Whether this claim restarts the streak. The screen shows this <i>before</i> the player claims.</summary>
        public bool ResetsStreak => Transition == DailyStreakTransition.Reset;

        /// <summary>Whether this claim pays the last step of the cycle, which ends the cycle and restores the skip day.</summary>
        public bool CompletesCycle => Exists && Step == DailyRewardTableInfo.NumSteps;

        public override string ToString() =>
            Exists ? $"{Transition}: step {Step} ({StepId}), streak {StreakBefore}->{StreakAfter}" : "no claim";
    }

    /// <summary>
    /// The player's daily reward status at one moment, for the screen and the server. Computed, never stored.
    /// </summary>
    public readonly struct DailyRewardOutlook
    {
        /// <summary>The activation at the time this outlook was computed.</summary>
        public DailyActivation Activation { get; }

        /// <summary>What claiming now would do. <see cref="DailyRewardClaim.Exists"/> is false when nothing is claimable.</summary>
        public DailyRewardClaim Claim { get; }

        /// <summary>Why nothing is claimable, or <see cref="DailyRewardRefusal.None"/> when a claim is possible.</summary>
        public DailyRewardRefusal Refusal { get; }

        /// <summary>The player's current streak, before any claim now.</summary>
        public int StreakDays { get; }

        /// <summary>Whether the cycle's skip day is still unused.</summary>
        public bool SkipDayAvailable { get; }

        /// <summary>
        /// The step shown on today's tile: the step a claim would pay, or the step already paid today. Null
        /// when the player has not claimed and cannot claim.
        /// </summary>
        public DailyRewardStepInfo Today { get; }

        /// <summary>The step the next claim pays if the player claims on the next day.</summary>
        public DailyRewardStepInfo Tomorrow { get; }

        /// <summary>
        /// How many steps of the current cycle the player has claimed, from 0 to
        /// <see cref="DailyRewardTableInfo.NumSteps"/>. The screen shows every step and marks this many as claimed.
        /// </summary>
        public int StepsClaimedInCycle { get; }

        internal DailyRewardOutlook(
            DailyActivation      activation,
            DailyRewardClaim     claim,
            DailyRewardRefusal   refusal,
            int                  streakDays,
            bool                 skipDayAvailable,
            DailyRewardStepInfo  today,
            DailyRewardStepInfo  tomorrow,
            int                  stepsClaimedInCycle)
        {
            Activation          = activation;
            Claim               = claim;
            Refusal             = refusal;
            StreakDays          = streakDays;
            SkipDayAvailable    = skipDayAvailable;
            Today               = today;
            Tomorrow            = tomorrow;
            StepsClaimedInCycle = stepsClaimedInCycle;
        }

        public bool IsClaimable => Refusal == DailyRewardRefusal.None && Claim.Exists;

        /// <summary>How long until the next reward unlocks, from <paramref name="now"/>. Never negative.</summary>
        public MetaDuration UntilNextAvailable(MetaTime now)
        {
            if (!Activation.Exists || Activation.EndsAt <= now)
                return MetaDuration.Zero;
            return Activation.EndsAt - now;
        }
    }

    /// <summary>
    /// The streak, skip day and cycle rules, as pure functions.
    /// <para>
    /// These methods read only their arguments and write nothing, so unit tests can cover the streak rules
    /// without a server or waiting for a real day to pass. The claim action, the server actor and the browser
    /// client all use <see cref="ClaimFor"/> and <see cref="OutlookAt"/>.
    /// </para>
    /// </summary>
    public static class DailyRewardPolicy
    {
        /// <summary>
        /// The active reward table, or null when the published config has none. Works on a config built in a test
        /// (<see cref="ConfigRefs.Resolve"/>).
        /// </summary>
        public static DailyRewardTableInfo ActiveTable(SharedGameConfig config) =>
            ConfigRefs.Resolve(config?.Global?.ActiveDailyRewardTable, config?.DailyRewards);

        /// <summary>
        /// The player's local time at <paramref name="now"/> for the daily reward. The server and the client both
        /// use it, so they agree on which day it is for the player.
        /// <para>
        /// <c>GetCorrected</c> clamps the offset to the range of real time zones, because the client reports it
        /// and an extreme offset would put the player on a wrong day. A player with no time zone uses UTC. The
        /// SDK's <c>GetCurrentLocalTime</c> does not clamp, so it is not used.
        /// </para>
        /// </summary>
        public static PlayerLocalTime LocalTimeOf(IPlayerModelBase player, MetaTime now) =>
            new PlayerLocalTime(now, player.TimeZoneInfo?.GetCorrected().CurrentUtcOffset ?? MetaDuration.Zero);

        /// <summary>
        /// Returns the transition that claiming <paramref name="activation"/> would make, given the player's
        /// last claim.
        /// <para>
        /// <b>Days are compared as integers.</b> A gap of one is the next day, a gap of two is one missed day,
        /// and a larger gap resets the streak. A gap of zero or less returns <see cref="DailyStreakTransition.None"/>:
        /// it is the same day claimed again, or local time moved backwards, for example after a device time
        /// change or travel across the date line.
        /// </para>
        /// </summary>
        public static DailyStreakTransition TransitionFor(DailyRewardState state, int activation)
        {
            if (state == null || !state.HasClaimed)
                return DailyStreakTransition.First;

            int gap = activation - state.LastClaimedActivationIndex;

            if (gap <= 0)
                return DailyStreakTransition.None;
            if (gap == 1)
                return DailyStreakTransition.Continued;
            if (gap == 2 && !state.SkipDayUsed)
                return DailyStreakTransition.Bridged;

            return DailyStreakTransition.Reset;
        }

        /// <summary>
        /// Returns what claiming <paramref name="activation"/> would pay and the resulting streak.
        /// <para>
        /// <b>This is the only place a claim is decided.</b> It takes an activation index instead of a time, so
        /// the client and the server compute the same result from the replicated state without reading a clock.
        /// </para>
        /// </summary>
        public static DailyRewardClaim ClaimFor(DailyRewardState state, DailyRewardTableInfo table, int activation, out DailyRewardRefusal refusal)
        {
            if (table == null)
            {
                refusal = DailyRewardRefusal.ConfigUnavailable;
                return default;
            }

            DailyStreakTransition transition = TransitionFor(state, activation);
            if (transition == DailyStreakTransition.None)
            {
                refusal = DailyRewardRefusal.AlreadyClaimed;
                return default;
            }

            bool continues = transition == DailyStreakTransition.Continued || transition == DailyStreakTransition.Bridged;
            int  step      = continues ? DailyRewardTableInfo.NextStepAfter(state.LastClaimedStep) : 1;

            DailyRewardStepInfo stepInfo = table.StepAt(step);
            if (stepInfo?.Reward == null)
            {
                refusal = DailyRewardRefusal.ConfigUnavailable;
                return default;
            }

            int streakBefore = state?.StreakDays ?? 0;
            int streakAfter  = continues ? streakBefore + 1 : 1;

            // Bridging a missed day uses the skip day, and completing the cycle restores it. A last-step claim
            // that also bridged therefore ends with the skip day available, so every new cycle starts with a skip day.
            bool skipDayUsedAfter = continues && (state.SkipDayUsed || transition == DailyStreakTransition.Bridged);
            if (step == DailyRewardTableInfo.NumSteps)
                skipDayUsedAfter = false;

            refusal = DailyRewardRefusal.None;
            return new DailyRewardClaim(transition, activation, step, stepInfo.Id, stepInfo.Reward, streakBefore, streakAfter, skipDayUsedAfter);
        }

        /// <summary>
        /// Returns the player's daily reward status and what claiming now would do.
        /// <para>
        /// <paramref name="now"/> is the caller's clock. On the server it is authoritative and decides the claim.
        /// In the browser it is the model's time and only affects what is displayed. A client with its clock set
        /// forward shows a claimable reward, and the server refuses the claim.
        /// </para>
        /// </summary>
        public static DailyRewardOutlook OutlookAt(DailyRewardState state, GlobalConfig global, DailyRewardTableInfo table, PlayerLocalTime now)
        {
            int  streak           = state?.StreakDays ?? 0;
            bool skipDayAvailable = state == null || !state.SkipDayUsed;

            if (global?.DailyResetSchedule == null || table == null)
                return Unclaimable(DailyActivation.None, DailyRewardRefusal.ConfigUnavailable, state, table, streak, skipDayAvailable);

            DailyActivation activation = DailyRewardCalendar.ActivationAt(global.DailyResetSchedule, now);
            if (!activation.Exists)
                return Unclaimable(activation, DailyRewardRefusal.NoActivation, state, table, streak, skipDayAvailable);

            DailyRewardClaim claim = ClaimFor(state, table, activation.Index, out DailyRewardRefusal refusal);
            if (refusal != DailyRewardRefusal.None)
                return Unclaimable(activation, refusal, state, table, streak, skipDayAvailable);

            return new DailyRewardOutlook(
                activation,
                claim,
                DailyRewardRefusal.None,
                streak,
                skipDayAvailable,
                table.StepAt(claim.Step),
                table.StepAt(DailyRewardTableInfo.NextStepAfter(claim.Step)),
                stepsClaimedInCycle: claim.Step - 1);
        }

        /// <summary>
        /// Returns the outlook when nothing is claimable. It still fills in the cycle fields, because the screen
        /// shows what today paid and what the next day pays.
        /// </summary>
        static DailyRewardOutlook Unclaimable(
            DailyActivation      activation,
            DailyRewardRefusal   refusal,
            DailyRewardState     state,
            DailyRewardTableInfo table,
            int                  streak,
            bool                 skipDayAvailable)
        {
            int lastStep = state != null && state.HasClaimed ? state.LastClaimedStep : 0;

            return new DailyRewardOutlook(
                activation,
                default,
                refusal,
                streak,
                skipDayAvailable,
                lastStep >= 1 ? table?.StepAt(lastStep) : null,
                table?.StepAt(DailyRewardTableInfo.NextStepAfter(lastStep)),
                stepsClaimedInCycle: lastStep);
        }
    }
}
