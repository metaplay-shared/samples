using Metaplay.Core;
using Metaplay.Core.Model;
using System;
using System.Collections.Generic;

namespace Game.Logic
{
    /// <summary>
    /// A player's progress on one first-week day and the state of its reward. The goal and reward come from
    /// the player's assigned schedule, and the day's time window comes from the player's start time.
    /// </summary>
    [MetaSerializable]
    public class FirstWeekDayProgress
    {
        /// <summary>The ID of the published day. A claim names this ID.</summary>
        [MetaMember(1)] public FirstWeekDayId Id { get; private set; }

        /// <summary>Qualifying matches counted, never more than the day's target.</summary>
        [MetaMember(2)] public int Count { get; private set; }

        /// <summary>When the target was reached, or <see cref="MetaTime.Epoch"/> if it has not been.</summary>
        [MetaMember(3)] public MetaTime CompletedAt { get; private set; }

        /// <summary>When the reward was paid, or <see cref="MetaTime.Epoch"/> if it has not been.</summary>
        [MetaMember(4)] public MetaTime ClaimedAt { get; private set; }

        public FirstWeekDayProgress() { }

        public FirstWeekDayProgress(FirstWeekDayId id)
        {
            Id = id;
        }

        /// <summary>
        /// Whether the goal was met within the day's window. <b>Once true, it stays true</b>, so a completed day's
        /// reward stays claimable with no deadline (<c>docs/first-week-event.md</c>).
        /// </summary>
        public bool IsComplete => CompletedAt != MetaTime.Epoch;

        public bool IsClaimed => ClaimedAt != MetaTime.Epoch;

        /// <summary>Whether the day is complete and its reward is not yet claimed.</summary>
        public bool IsRewardReady => IsComplete && !IsClaimed;

        /// <summary>
        /// Counts one more qualifying match, up to <paramref name="target"/>, and records the completion time
        /// when the target is reached. Returns true only for the match that completes the day.
        /// </summary>
        internal bool Advance(int target, MetaTime at, out int before, out int after)
        {
            before = Count;
            Count  = Math.Min(target, Count + 1);
            after  = Count;

            if (IsComplete || Count < target)
                return false;

            CompletedAt = at;
            return true;
        }

        internal void MarkClaimed(MetaTime at) => ClaimedAt = at;

        public override string ToString() =>
            $"{Id}: {Count}{(IsClaimed ? " (claimed)" : IsComplete ? " (ready)" : "")}";
    }

    /// <summary>
    /// The state of one first-week day at a given time: the published day, the player's progress on it, and
    /// its time window. Computing it writes nothing.
    /// </summary>
    public readonly struct FirstWeekDayStatus
    {
        /// <summary>The published day. Never null.</summary>
        public FirstWeekDayInfo Info { get; }

        /// <summary>The player's progress, or null if no match has been counted for this day.</summary>
        public FirstWeekDayProgress Progress { get; }

        /// <summary>The start and end of the player's personal window for this day.</summary>
        public MetaTime StartsAt { get; }
        public MetaTime EndsAt   { get; }

        /// <summary>Whether the requested time falls in this day's window.</summary>
        public bool IsActive { get; }

        /// <summary>Whether the window has closed.</summary>
        public bool HasElapsed { get; }

        internal FirstWeekDayStatus(FirstWeekDayInfo info, FirstWeekDayProgress progress, MetaTime startsAt, MetaTime endsAt, MetaTime at)
        {
            Info       = info;
            Progress   = progress;
            StartsAt   = startsAt;
            EndsAt     = endsAt;
            IsActive   = at >= startsAt && at < endsAt;
            HasElapsed = at >= endsAt;
        }

        public int  Day        => Info.Day;
        public int  Target     => Info.MatchesRequired;
        public int  Count      => Progress?.Count ?? 0;
        public bool IsComplete => Progress != null && Progress.IsComplete;
        public bool IsClaimed  => Progress != null && Progress.IsClaimed;

        /// <summary>Whether the day is complete and unclaimed. A ready reward never expires.</summary>
        public bool IsRewardReady => Progress != null && Progress.IsRewardReady;

        /// <summary>
        /// Whether the day's window closed before the goal was met. A missed day does not affect later days.
        /// </summary>
        public bool IsMissed => HasElapsed && !IsComplete;

        /// <summary>Whether the day's window has not started yet. The screen still shows its goal and reward.</summary>
        public bool IsFuture => !IsActive && !HasElapsed;

        public override string ToString() =>
            $"day {Day}: {Count}/{Target}{(IsClaimed ? " claimed" : IsRewardReady ? " ready" : IsMissed ? " missed" : "")}";
    }

    /// <summary>
    /// The state of the whole first-week event at a given time: which day is active, the state of every day,
    /// and whether the event has ended. It is computed from the player's start time and assigned schedule and
    /// writes nothing.
    /// </summary>
    public readonly struct FirstWeekOutlook
    {
        /// <summary>Every day of the event, in order. Null when the event has not started or its schedule is missing.</summary>
        public IReadOnlyList<FirstWeekDayStatus> Days { get; }

        /// <summary>The schedule assigned to the player.</summary>
        public FirstWeekScheduleId ScheduleId { get; }

        /// <summary>
        /// The zero-based index of the day at the requested time (see <see cref="PlayerFirstWeekState.DayIndexAt"/>).
        /// </summary>
        public int DayIndex { get; }

        /// <summary>When the event started for the player, and when its last day window ends.</summary>
        public MetaTime StartedAt { get; }
        public MetaTime EndsAt    { get; }

        internal FirstWeekOutlook(
            IReadOnlyList<FirstWeekDayStatus> days,
            FirstWeekScheduleId               scheduleId,
            int                               dayIndex,
            MetaTime                          startedAt,
            MetaTime                          endsAt)
        {
            Days       = days;
            ScheduleId = scheduleId;
            DayIndex   = dayIndex;
            StartedAt  = startedAt;
            EndsAt     = endsAt;
        }

        /// <summary>Whether the event has started and its schedule exists in the config.</summary>
        public bool IsResolved => Days != null && Days.Count > 0;

        /// <summary>Whether the last day window has closed. Completed days stay claimable afterwards.</summary>
        public bool HasEnded => DayIndex >= FirstWeekScheduleInfo.NumDays;

        /// <summary>The day at the requested time, or null before the event or after it has ended.</summary>
        public FirstWeekDayStatus? ActiveDay =>
            IsResolved && DayIndex >= 0 && DayIndex < Days.Count ? Days[DayIndex] : null;

        /// <summary>The time left in the active day, or null when there is no active day.</summary>
        public MetaDuration? UntilActiveDayEnds(MetaTime at)
        {
            FirstWeekDayStatus? day = ActiveDay;
            if (day == null)
                return null;

            return MetaDuration.Max(day.Value.EndsAt - at, MetaDuration.Zero);
        }

        public int ClaimableCount => Count(day => day.IsRewardReady);
        public int MissedCount    => Count(day => day.IsMissed);

        int Count(Func<FirstWeekDayStatus, bool> predicate)
        {
            if (Days == null)
                return 0;

            int count = 0;
            foreach (FirstWeekDayStatus day in Days)
            {
                if (predicate(day))
                    count++;
            }
            return count;
        }
    }
}
