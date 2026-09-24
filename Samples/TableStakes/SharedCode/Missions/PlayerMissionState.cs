using Metaplay.Core;
using Metaplay.Core.Model;
using Metaplay.Core.Player;
using Metaplay.Core.Schedule;
using System.Collections.Generic;

namespace Game.Logic
{
    /// <summary>Why a mission claim was refused. The Missions screen has a message for each result.</summary>
    public static partial class ActionResults
    {
        /// <summary>None of the player's stored activations contains that instance ID.</summary>
        public static readonly MetaActionResult NoSuchMission = new MetaActionResult(nameof(NoSuchMission));

        /// <summary>The mission is not complete, so there is no reward to pay.</summary>
        public static readonly MetaActionResult MissionNotComplete = new MetaActionResult(nameof(MissionNotComplete));

        /// <summary>The reward was already paid, for example on a replayed or duplicate claim.</summary>
        public static readonly MetaActionResult MissionAlreadyClaimed = new MetaActionResult(nameof(MissionAlreadyClaimed));

        /// <summary>The claim deadline (<see cref="MissionActivation.ClaimDeadline"/>) has passed.</summary>
        public static readonly MetaActionResult MissionExpired = new MetaActionResult(nameof(MissionExpired));

        /// <summary>The mission or its reward is missing from the active game config.</summary>
        public static readonly MetaActionResult MissionConfigMissing = new MetaActionResult(nameof(MissionConfigMissing));
    }

    /// <summary>
    /// The activations of both cadences at a given time, computed from the calendar without writing anything.
    /// <para>
    /// Rollover is computed by <see cref="PlayerMissionState.RolloverAt"/>, not applied as a separate step. The
    /// screen displays the rollover, and the match-completion observer stores it. Both use the same code, and
    /// the screen can show the current day without changing the player state.
    /// </para>
    /// </summary>
    public readonly struct MissionRollover
    {
        /// <summary>The current activation of each cadence. Never null.</summary>
        public MissionActivation Daily  { get; }
        public MissionActivation Weekly { get; }

        /// <summary>The previous activation, while it still has an unclaimed reward. Null otherwise.</summary>
        public MissionActivation DailyLateClaim  { get; }
        public MissionActivation WeeklyLateClaim { get; }

        /// <summary>
        /// The late-claim snapshot that the last rollover replaced, while it still has an unclaimed reward. Null otherwise.
        /// Only a claim can reach it, never the screen (<c>docs/missions.md</c>, "The claim").
        /// </summary>
        public MissionActivation DailyRetired  { get; }
        public MissionActivation WeeklyRetired { get; }

        internal MissionRollover(
            MissionActivation daily, MissionActivation weekly,
            MissionActivation dailyLateClaim, MissionActivation weeklyLateClaim,
            MissionActivation dailyRetired, MissionActivation weeklyRetired)
        {
            Daily           = daily;
            Weekly          = weekly;
            DailyLateClaim  = dailyLateClaim;
            WeeklyLateClaim = weeklyLateClaim;
            DailyRetired    = dailyRetired;
            WeeklyRetired   = weeklyRetired;
        }

        public MissionActivation ActivationOf(MissionCadence cadence) => cadence == MissionCadence.Weekly ? Weekly : Daily;
    }

    /// <summary>
    /// What one claim would pay, and from which activation. The claim action resolves it on both passes and acts on
    /// it only on the commit pass.
    /// </summary>
    public readonly struct MissionClaim
    {
        public MissionActivation Activation { get; }
        public MissionProgress   Progress   { get; }
        public MissionInfo       Mission    { get; }

        /// <summary>Whether the claim is made after its activation ended, within the late-claim window.</summary>
        public bool IsLateClaim { get; }

        internal MissionClaim(MissionActivation activation, MissionProgress progress, MissionInfo mission, bool isLateClaim)
        {
            Activation  = activation;
            Progress    = progress;
            Mission     = mission;
            IsLateClaim = isLateClaim;
        }

        public MissionInstanceId InstanceId => Activation.InstanceOf(Progress.Id);
        public RewardBundle      Reward     => Mission.Reward;
    }

    /// <summary>
    /// One player's missions: the current daily and weekly activations, and for each cadence up to two older
    /// activations that still have unclaimed rewards, the late-claim snapshot and the retired snapshot
    /// (<c>docs/missions.md</c>). A finished match is delivered by an unsynchronized server action, so this state is a
    /// <c>[NoChecksum]</c> member of <see cref="PlayerModel"/> and writes no other state, including the wallet.
    /// <see cref="PlayerClaimMissionReward"/> pays the rewards. Nothing that can be computed is stored. The current
    /// activation is computed from the player's calendar in constant time, so catching up a player who was away for a
    /// year costs the same as one away for an hour.
    /// </summary>
    [MetaSerializable]
    public class PlayerMissionState : IMatchCompletionObserver
    {
        /// <summary>
        /// How long a completed mission stays claimable after its activation ends, so a reward earned just before
        /// a reset is not lost.
        /// <para>
        /// It is a maximum: <see cref="MissionActivation.ClaimDeadline"/> caps it at the activation's length.
        /// </para>
        /// </summary>
        public static readonly MetaDuration LateClaimWindow = MetaDuration.FromHours(24);

        [MetaMember(1)] MissionActivation _daily;
        [MetaMember(2)] MissionActivation _weekly;
        [MetaMember(3)] MissionActivation _dailyLateClaim;
        [MetaMember(4)] MissionActivation _weeklyLateClaim;

        /// <summary>
        /// How many mission rewards the player has been paid. Changes only on a committed claim. Reported in
        /// analytics, and used by the client after a reconnect to tell whether a claim went through.
        /// </summary>
        [MetaMember(5)] public int ClaimCount { get; private set; }

        /// <summary>
        /// The last reward paid, and when. A client that reconnects during the reward animation uses these to
        /// tell whether the claim was paid.
        /// </summary>
        [MetaMember(6)] public MissionInstanceId LastClaimedInstance { get; private set; }
        [MetaMember(7)] public MissionId         LastClaimedMission  { get; private set; }
        [MetaMember(8)] public MetaTime          LastClaimedAt       { get; private set; }

        // Member ID 9 is retired. Never reuse a member ID.
        //
        // Missions do not store counted match IDs. PlayerModel.TryRecordMatch drops a re-delivered match that is
        // still in the match history. A re-delivery that arrives after its entry has left the bounded history
        // does reach the observers, with its original completion time. ResolveCadence (rollover only moves
        // forward) and MissionActivation.ContainsCompletionTime (the match must fall in the activation's window) keep
        // it from counting into the wrong window.

        /// <summary>
        /// The late-claim snapshot that the last rollover replaced, kept until the next rollover while it has an unclaimed
        /// reward. The screen never shows it. Rollover runs in the unsynchronized match-completion action, which the
        /// server runs first, so a claim the client sent before seeing the rollover can execute on the server after
        /// it. Keeping this snapshot lets that claim succeed on both sides, so the checksummed wallets agree. The SDK
        /// disconnects a client more than <c>PlayerOptions.ClientTimeMaxBehind</c> behind the server, which is far
        /// shorter than an activation, so by the next rollover no connected client can still claim it.
        /// </summary>
        [MetaMember(10)] MissionActivation _dailyRetired;
        [MetaMember(11)] MissionActivation _weeklyRetired;

        public PlayerMissionState() { }

        #region Reading the calendar

        /// <summary>
        /// Returns each cadence's activation at <paramref name="at"/> and the older activations kept for it, as a
        /// rollover would produce them but without storing them, so the screen and the observer use the same result.
        /// The result shares the stored <see cref="MissionProgress"/> objects, so callers must treat it as read-only.
        /// Only the match-completion observer writes through it.
        /// </summary>
        /// <exception cref="MissionConfigUnavailableException">
        /// The published config has no set for a cadence that needs a new activation. Thrown before any result is
        /// returned, so a caller cannot store a partial rollover.
        /// </exception>
        public MissionRollover RolloverAt(SharedGameConfig config, PlayerLocalTime at)
        {
            ResolveCadence(config, at, MissionCadence.Daily, _daily, _dailyLateClaim, _dailyRetired,
                out MissionActivation daily, out MissionActivation dailyLateClaim, out MissionActivation dailyRetired);
            ResolveCadence(config, at, MissionCadence.Weekly, _weekly, _weeklyLateClaim, _weeklyRetired,
                out MissionActivation weekly, out MissionActivation weeklyLateClaim, out MissionActivation weeklyRetired);

            return new MissionRollover(daily, weekly, dailyLateClaim, weeklyLateClaim, dailyRetired, weeklyRetired);
        }

        /// <summary>
        /// Computes one cadence's rollover in constant time. If the calendar's window at <paramref name="at"/> is later
        /// than the stored activation, a new activation replaces it, the replaced activation becomes the late-claim snapshot,
        /// and the previous late-claim snapshot becomes the retired snapshot (<see cref="_dailyRetired"/>). A snapshot is
        /// kept only while it has an unclaimed reward. An earlier window never causes a rollover: a re-delivered match
        /// completion can arrive late with its original time, and because instance IDs are built from cadence,
        /// activation start and mission ID, a new activation for an old window would reuse claimed IDs with the claims
        /// cleared and pay the rewards twice (<c>docs/player.md</c>, "Observer rules"). This method reads no clock.
        /// The claim action and the screen decide whether a kept snapshot is still claimable.
        /// </summary>
        static void ResolveCadence(
            SharedGameConfig      config,
            PlayerLocalTime       at,
            MissionCadence        cadence,
            MissionActivation     storedCurrent,
            MissionActivation     storedLateClaim,
            MissionActivation     storedRetired,
            out MissionActivation current,
            out MissionActivation lateClaim,
            out MissionActivation retired)
        {
            MetaScheduleBase     schedule = cadence == MissionCadence.Weekly ? PlayerCalendar.Weekly : PlayerCalendar.Daily;
            PlayerCalendarWindow window   = PlayerCalendar.WindowAt(schedule, at);

            // Compare order, not equality. An equality check would treat an older window as a rollover and move
            // the activation backwards.
            if (storedCurrent != null && window.StartsAt <= storedCurrent.StartsAt)
            {
                current   = storedCurrent;
                lateClaim = KeepIfAnythingIsOwed(storedLateClaim);
                retired   = KeepIfAnythingIsOwed(storedRetired);
                return;
            }

            current   = NewActivation(config, cadence, window);
            lateClaim = KeepIfAnythingIsOwed(storedCurrent?.AsLateClaimSnapshot());
            retired   = KeepIfAnythingIsOwed(storedLateClaim);
        }

        /// <summary>Returns <paramref name="activation"/> if it has an unclaimed reward, otherwise null.</summary>
        static MissionActivation KeepIfAnythingIsOwed(MissionActivation activation) =>
            activation != null && activation.HasUnclaimedReward ? activation : null;

        /// <summary>Creates a zero-progress activation from the published config's set for <paramref name="cadence"/>.</summary>
        static MissionActivation NewActivation(SharedGameConfig config, MissionCadence cadence, PlayerCalendarWindow window)
        {
            MissionSetInfo set = ActiveSet(config, cadence);
            if (set == null)
                throw new MissionConfigUnavailableException($"the published config assigns no {cadence} mission set");

            List<MissionId> missionIds = new List<MissionId>();
            foreach (MetaRef<MissionInfo> reference in set.Missions)
            {
                if (reference == null)
                    throw new MissionConfigUnavailableException($"mission set '{set.Id}' holds an empty slot");
                missionIds.Add((MissionId)reference.KeyObject);
            }

            return new MissionActivation(cadence, window, set.Id, missionIds);
        }

        static MissionSetInfo ActiveSet(SharedGameConfig config, MissionCadence cadence)
        {
            MetaRef<MissionSetInfo> pointer = cadence == MissionCadence.Weekly
                ? config?.Global?.ActiveWeeklyMissionSet
                : config?.Global?.ActiveDailyMissionSet;

            return ConfigRefs.Resolve(pointer, config?.MissionSets);
        }

        /// <summary>Stores an rollover as the player state. This is the only method that writes the activation members.</summary>
        void Adopt(in MissionRollover rollover)
        {
            _daily           = rollover.Daily;
            _weekly          = rollover.Weekly;
            _dailyLateClaim  = rollover.DailyLateClaim;
            _weeklyLateClaim = rollover.WeeklyLateClaim;
            _dailyRetired    = rollover.DailyRetired;
            _weeklyRetired   = rollover.WeeklyRetired;
        }

        #endregion

        #region A finished game

        /// <summary>
        /// Rolls the activations forward to the match's completion time and counts the match toward every
        /// current mission it qualifies for. One match can complete several missions, and each completed mission
        /// emits an event. Progress steps that do not complete a mission emit nothing.
        /// </summary>
        void IMatchCompletionObserver.OnMatchCompleted(in MatchCompletionContext context)
        {
            // Compute the rollover before writing anything, so a missing set (which throws) leaves the mission
            // state unchanged.
            MissionRollover rollover = RolloverAt(context.GameConfig, context.CompletedAtLocal);
            Adopt(rollover);

            Advance(rollover.Daily, context);
            Advance(rollover.Weekly, context);
        }

        /// <summary>
        /// Counts the match toward the missions of <paramref name="activation"/>, <b>only if the match's
        /// completion time falls in the activation's window</b>.
        /// <para>
        /// A re-delivered match completion can arrive after its window has closed. Rollover does not go back,
        /// so that window no longer exists, and counting the match into the current window would credit an old
        /// match. Such a match is not counted, in the same way incomplete progress expires at a reset.
        /// </para>
        /// </summary>
        static void Advance(MissionActivation activation, in MatchCompletionContext context)
        {
            MatchCompletion completion = context.Completion;

            if (!activation.ContainsCompletionTime(completion.CompletedAt))
                return;

            foreach (MissionProgress progress in activation.Missions)
            {
                if (progress.IsComplete)
                    continue;

                MissionInfo mission = context.GameConfig.Missions?.GetValueOrDefault(progress.Id);
                if (mission == null)
                    continue;

                int delta = DeltaFor(mission.Objective, completion);
                if (delta <= 0)
                    continue;

                if (!progress.Advance(delta, mission.TargetCount, completion.CompletedAt, out int progressBefore, out int progressAfter))
                    continue;

                context.Emit(new PlayerEventMissionCompleted(
                    activation.InstanceOf(mission.Id), activation.SetId, mission.Id, activation.Cadence,
                    mission.Objective, mission.TargetCount, progressBefore, progressAfter));
            }
        }

        /// <summary>
        /// Returns how much a finished match adds to a mission with <paramref name="objective"/>.
        /// </summary>
        static int DeltaFor(MissionObjective objective, in MatchCompletion completion)
        {
            switch (objective)
            {
                case MissionObjective.MatchesCompleted: return 1;
                case MissionObjective.MatchesWon:       return completion.IsWin ? 1 : 0;
                default:                                return 0;
            }
        }

        #endregion

        #region Claiming

        /// <summary>
        /// Finds <paramref name="instanceId"/> in the player's stored activations, including the late-claim and
        /// retired snapshots, and checks whether its reward can be paid.
        /// <para>
        /// It does not roll the calendar forward. A stored activation that is out of date still has its own end
        /// time and <see cref="MissionActivation.ClaimDeadline"/>, which are checked the same way as for a late-claim
        /// snapshot. Rollover happens only on match completion.
        /// </para>
        /// </summary>
        public MetaActionResult ResolveClaim(SharedGameConfig config, MissionInstanceId instanceId, MetaTime at, out MissionClaim claim)
        {
            claim = default;
            if (instanceId == null)
                return ActionResults.NoSuchMission;

            foreach ((MissionActivation activation, bool isGrace) in HeldActivations())
            {
                MissionProgress progress = FindByInstance(activation, instanceId);
                if (progress == null)
                    continue;

                MissionInfo mission = config?.Missions?.GetValueOrDefault(progress.Id);
                if (mission == null || mission.Reward == null)
                    return ActionResults.MissionConfigMissing;

                if (progress.IsClaimed)
                    return ActionResults.MissionAlreadyClaimed;
                if (!progress.IsComplete)
                    return ActionResults.MissionNotComplete;
                if (at >= activation.ClaimDeadline)
                    return ActionResults.MissionExpired;

                claim = new MissionClaim(activation, progress, mission, isGrace || at >= activation.EndsAt);
                return MetaActionResult.Success;
            }

            return ActionResults.NoSuchMission;
        }

        /// <summary>
        /// Records a paid claim. Called only from the commit pass of <see cref="PlayerClaimMissionReward"/>,
        /// after the wallet accepted the grant, so <see cref="ClaimCount"/> counts only paid rewards.
        /// </summary>
        internal void ApplyClaim(in MissionClaim claim, MetaTime at)
        {
            claim.Progress.MarkClaimed(at);
            ClaimCount         += 1;
            LastClaimedInstance = claim.InstanceId;
            LastClaimedMission  = claim.Progress.Id;
            LastClaimedAt       = at;
        }

        static MissionProgress FindByInstance(MissionActivation activation, MissionInstanceId instanceId)
        {
            foreach (MissionProgress progress in activation.Missions)
            {
                if (activation.InstanceOf(progress.Id) == instanceId)
                    return progress;
            }
            return null;
        }

        /// <summary>The player's stored activations, in the order current, late-claim, retired.</summary>
        IEnumerable<(MissionActivation Activation, bool IsGrace)> HeldActivations()
        {
            if (_daily != null)           yield return (_daily, false);
            if (_weekly != null)          yield return (_weekly, false);
            if (_dailyLateClaim != null)  yield return (_dailyLateClaim, true);
            if (_weeklyLateClaim != null) yield return (_weeklyLateClaim, true);
            if (_dailyRetired != null)    yield return (_dailyRetired, true);
            if (_weeklyRetired != null)   yield return (_weeklyRetired, true);
        }

        #endregion

        /// <summary>The stored activations, for the LiveOps Dashboard's model view and for tests.</summary>
        public MissionActivation StoredDaily           => _daily;
        public MissionActivation StoredWeekly          => _weekly;
        public MissionActivation StoredDailyLateClaim  => _dailyLateClaim;
        public MissionActivation StoredWeeklyLateClaim => _weeklyLateClaim;
        public MissionActivation StoredDailyRetired    => _dailyRetired;
        public MissionActivation StoredWeeklyRetired   => _weeklyRetired;

        public override string ToString() => $"missions: daily {_daily}, weekly {_weekly}, {ClaimCount} claimed";
    }
}
