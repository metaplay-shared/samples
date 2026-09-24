using Metaplay.Core;
using Metaplay.Core.Model;
using System;
using System.Collections.Generic;

namespace Game.Logic
{
    /// <summary>
    /// Identifies one mission in one activation. A player claims an instance, not the recurring mission
    /// definition in game config.
    /// <para>
    /// It is built from the cadence, the activation's start time and the mission ID, so the same mission in the
    /// next activation has a different ID and needs its own claim. Every part comes from config or the
    /// schedule, never from player input, so it is safe to use in analytics events.
    /// </para>
    /// </summary>
    [MetaSerializable]
    public class MissionInstanceId : StringId<MissionInstanceId>
    {
        public static MissionInstanceId Create(MissionCadence cadence, MetaTime activationStart, MissionId missionId) =>
            FromString($"{cadence}:{activationStart.MillisecondsSinceEpoch}:{missionId}");
    }

    /// <summary>
    /// A player's progress on one mission and the state of its reward. Everything else a mission card shows is
    /// computed from this and from config.
    /// </summary>
    [MetaSerializable]
    public class MissionProgress
    {
        [MetaMember(1)] public MissionId Id { get; private set; }

        /// <summary>The progress count, never more than the mission's target.</summary>
        [MetaMember(2)] public int Count { get; private set; }

        /// <summary>When the target was reached, or <see cref="MetaTime.Epoch"/> if it has not been.</summary>
        [MetaMember(3)] public MetaTime CompletedAt { get; private set; }

        /// <summary>When the reward was claimed, or <see cref="MetaTime.Epoch"/> if it has not been.</summary>
        [MetaMember(4)] public MetaTime ClaimedAt { get; private set; }

        public MissionProgress() { }

        public MissionProgress(MissionId id)
        {
            Id = id;
        }

        public bool IsComplete => CompletedAt != MetaTime.Epoch;
        public bool IsClaimed  => ClaimedAt != MetaTime.Epoch;

        /// <summary>
        /// Adds <paramref name="delta"/>, up to <paramref name="target"/>, and records the completion time when
        /// the target is reached. Returns true only for the call that completes the mission.
        /// </summary>
        internal bool Advance(int delta, int target, MetaTime at, out int before, out int after)
        {
            before = Count;
            Count  = Math.Min(target, Count + delta);
            after  = Count;

            if (IsComplete || Count < target)
                return false;

            CompletedAt = at;
            return true;
        }

        internal void MarkClaimed(MetaTime at) => ClaimedAt = at;

        public override string ToString() => $"{Id}: {Count}{(IsClaimed ? " (claimed)" : IsComplete ? " (ready)" : "")}";
    }

    /// <summary>
    /// One period of one cadence (daily or weekly): its time window, the mission set assigned when the window
    /// started, and the progress of each mission.
    /// <para>
    /// <b>The set ID is stored here.</b> When a new set is published, a running activation keeps its set, so
    /// the player gets the objectives and rewards they were shown. The new set applies from the next window
    /// (<c>docs/missions.md</c>).
    /// </para>
    /// </summary>
    [MetaSerializable]
    public class MissionActivation
    {
        [MetaMember(1)] public MissionCadence Cadence  { get; private set; }
        [MetaMember(2)] public MetaTime       StartsAt { get; private set; }
        [MetaMember(3)] public MetaTime       EndsAt   { get; private set; }
        [MetaMember(4)] public MissionSetId   SetId    { get; private set; }
        [MetaMember(5)] List<MissionProgress> _missions = new List<MissionProgress>();

        /// <summary>The missions of this activation, in the order the set names them.</summary>
        public IReadOnlyList<MissionProgress> Missions => _missions ?? EmptyMissions;

        static readonly List<MissionProgress> EmptyMissions = new List<MissionProgress>();

        public MissionActivation() { }

        public MissionActivation(MissionCadence cadence, PlayerCalendarWindow window, MissionSetId setId, IEnumerable<MissionId> missionIds)
        {
            Cadence   = cadence;
            StartsAt  = window.StartsAt;
            EndsAt    = window.EndsAt;
            SetId     = setId;
            _missions = new List<MissionProgress>();
            foreach (MissionId mission in missionIds)
                _missions.Add(new MissionProgress(mission));
        }

        /// <summary>The instance ID of one of this activation's missions.</summary>
        public MissionInstanceId InstanceOf(MissionId missionId) => MissionInstanceId.Create(Cadence, StartsAt, missionId);

        public MissionProgress Find(MissionId missionId)
        {
            foreach (MissionProgress progress in Missions)
            {
                if (progress.Id == missionId)
                    return progress;
            }
            return null;
        }

        /// <summary>
        /// Whether a match that finished at <paramref name="completedAt"/> falls in this activation's window.
        /// <para>
        /// A re-delivered match completion arrives late with its <i>original</i> completion time. This check
        /// stops it from counting into a window the match was not played in.
        /// </para>
        /// </summary>
        public bool ContainsCompletionTime(MetaTime completedAt) => completedAt >= StartsAt && completedAt < EndsAt;

        /// <summary>Whether any mission is complete and its reward unclaimed.</summary>
        public bool HasUnclaimedReward
        {
            get
            {
                foreach (MissionProgress progress in Missions)
                {
                    if (progress.IsComplete && !progress.IsClaimed)
                        return true;
                }
                return false;
            }
        }

        /// <summary>The activation's length. The schedule decides it, so do not assume it is one day.</summary>
        public MetaDuration Length => EndsAt - StartsAt;

        /// <summary>
        /// The time after which rewards from this activation can no longer be claimed: the activation's end plus
        /// <see cref="PlayerMissionState.LateClaimWindow"/>, so a mission finished just before a reset is not lost.
        /// <para>
        /// <b>The late-claim window is capped at the activation's <see cref="Length"/>.</b> Only one previous activation is
        /// kept as the late-claim snapshot, and it is replaced at the next rollover. A longer late-claim window would let a
        /// claimable reward disappear at that rollover without any error.
        /// </para>
        /// </summary>
        public MetaTime ClaimDeadline => EndsAt + MetaDuration.Min(PlayerMissionState.LateClaimWindow, Length);

        /// <summary>
        /// Returns a copy of this activation that holds only its completed, unclaimed missions. Incomplete
        /// progress expires at the rollover and claimed missions have nothing left to pay (<c>docs/missions.md</c>).
        /// </summary>
        internal MissionActivation AsLateClaimSnapshot()
        {
            MissionActivation snapshot = new MissionActivation
            {
                Cadence   = Cadence,
                StartsAt  = StartsAt,
                EndsAt    = EndsAt,
                SetId     = SetId,
                _missions = new List<MissionProgress>(),
            };

            foreach (MissionProgress progress in Missions)
            {
                if (progress.IsComplete && !progress.IsClaimed)
                    snapshot._missions.Add(progress);
            }

            return snapshot;
        }

        public override string ToString() => $"{Cadence} {SetId} from {StartsAt}";
    }

    /// <summary>
    /// The published config has no mission set for a cadence, or a set has an empty slot. Thrown before
    /// anything is written, so mission state does not change. It is thrown instead of ignored so the player
    /// actor's log shows which set is missing (<c>docs/missions.md</c>, "Calendar and rollover").
    /// </summary>
    public class MissionConfigUnavailableException : Exception
    {
        public MissionConfigUnavailableException(string message) : base(message) { }
    }
}
