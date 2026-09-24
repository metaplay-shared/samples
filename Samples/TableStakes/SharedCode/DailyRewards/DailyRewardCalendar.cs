using Metaplay.Core;
using Metaplay.Core.Player;
using Metaplay.Core.Schedule;
using System;

namespace Game.Logic
{
    /// <summary>
    /// One player-local day of the daily reward: its number, when it starts and when it ends.
    /// <para>
    /// <b>All day arithmetic in the feature uses <see cref="Index"/>.</b> It counts whole local calendar days
    /// from 1970-01-01, not from the published schedule's start, so republishing the schedule with a different
    /// start date cannot renumber stored days. Consecutive days differ by one and one missed day is a gap of
    /// two. The streak rules compare these integers and never add 24 hours to a time, so daylight saving changes
    /// do not affect the streak (<c>docs/daily-rewards.md</c>).
    /// </para>
    /// </summary>
    public readonly struct DailyActivation
    {
        /// <summary>Whether the schedule had an activation to give. False before the schedule's start date.</summary>
        public bool Exists { get; }

        /// <summary>Whole local days from 1970-01-01 to this activation. Claims are keyed by this value.</summary>
        public int Index { get; }

        /// <summary>When this activation starts, in absolute time.</summary>
        public MetaTime StartsAt { get; }

        /// <summary>When this activation ends, which is when the next one starts.</summary>
        public MetaTime EndsAt { get; }

        DailyActivation(bool exists, int index, MetaTime startsAt, MetaTime endsAt)
        {
            Exists   = exists;
            Index    = index;
            StartsAt = startsAt;
            EndsAt   = endsAt;
        }

        /// <summary>Returned when the schedule has no activation at the requested time.</summary>
        public static readonly DailyActivation None = new DailyActivation(false, 0, MetaTime.Epoch, MetaTime.Epoch);

        internal static DailyActivation At(int index, MetaTime startsAt, MetaTime endsAt) =>
            new DailyActivation(true, index, startsAt, endsAt);

        public override string ToString() => Exists ? $"activation {Index} ({StartsAt} to {EndsAt})" : "no activation";
    }

    /// <summary>
    /// Converts the published daily-reset schedule into numbered <see cref="DailyActivation"/> values.
    /// <para>
    /// The schedule is an SDK <see cref="MetaRecurringCalendarSchedule"/> in local time mode that recurs daily,
    /// so the SDK handles calendar rules, daylight saving and local time. This class adds a stable
    /// <b>number</b> for each occasion, which the SDK does not provide, so "the next day" and "one missed day"
    /// can be decided by comparing two integers.
    /// </para>
    /// </summary>
    public static class DailyRewardCalendar
    {
        /// <summary>
        /// The activation covering <paramref name="now"/>, or <see cref="DailyActivation.None"/>.
        /// <para>
        /// The config build rejects a schedule whose duration is shorter than its recurrence, so a published
        /// schedule covers every moment after its start. In practice <i>None</i> is returned only for a time
        /// before the schedule starts.
        /// </para>
        /// </summary>
        public static DailyActivation ActivationAt(MetaRecurringCalendarSchedule schedule, PlayerLocalTime now)
        {
            if (schedule == null)
                return DailyActivation.None;

            MetaScheduleOccasion? occasion = schedule.TryGetCurrentOccasion(now);
            if (!occasion.HasValue)
                return DailyActivation.None;

            // The schedule decides when a day starts. DayZero decides how the day is numbered (see IndexOf).
            MetaTimeRange enabled = occasion.Value.EnabledRange;
            return DailyActivation.At(IndexOf(enabled.Start, now.UtcOffset), enabled.Start, enabled.End);
        }

        /// <summary>
        /// The date that day numbers count from: 1970-01-01, the same date as <see cref="MetaTime.Epoch"/>.
        /// <para>
        /// It must stay a constant and never become config. Stored activation indexes are counted from it, so moving
        /// it later would make every player who has claimed see "already claimed" permanently, and a config build
        /// check cannot see the previously published config to catch that.
        /// </para>
        /// </summary>
        static readonly DateTime DayZero = new DateTime(1970, 1, 1);

        /// <summary>
        /// Returns the activation index of an absolute time, as whole local days since <see cref="DayZero"/>.
        /// <para>
        /// The time is converted to the player's local time and then truncated to a <b>date</b>. A change of UTC
        /// offset, such as a daylight saving change, keeps the same local date, so an already claimed day keeps
        /// its index.
        /// </para>
        /// </summary>
        static int IndexOf(MetaTime instant, MetaDuration utcOffset)
        {
            DateTime local = (instant + utcOffset).ToDateTime();

            return (int)(local.Date - DayZero).TotalDays;
        }
    }
}
