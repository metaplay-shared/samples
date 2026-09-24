using Metaplay.Core;
using Metaplay.Core.Player;
using Metaplay.Core.Schedule;
using System;

namespace Game.Logic
{
    /// <summary>
    /// One window of a recurring player-local schedule, from <see cref="StartsAt"/> (inclusive) to
    /// <see cref="EndsAt"/> (exclusive). <see cref="StartsAt"/> identifies the window, and late-claim deadlines are
    /// measured from <see cref="EndsAt"/>.
    /// </summary>
    public readonly struct PlayerCalendarWindow
    {
        public MetaTime StartsAt { get; }
        public MetaTime EndsAt   { get; }

        public PlayerCalendarWindow(MetaTime startsAt, MetaTime endsAt)
        {
            StartsAt = startsAt;
            EndsAt   = endsAt;
        }

        public override string ToString() => $"{StartsAt} .. {EndsAt}";
    }

    /// <summary>
    /// The game's day and week in the player's local time: a day runs from local midnight to local midnight, and a
    /// week starts at local midnight on Monday. Meta features that reset daily or weekly use these schedules, so they
    /// all reset at the same time (<c>docs/missions.md</c>, "Calendar and rollover"). The windows come from the SDK's
    /// <see cref="MetaRecurringCalendarSchedule"/> in <see cref="MetaScheduleTimeMode.Local"/> mode, the same code the
    /// SDK's activables use, so a daylight saving change applies from the player's next login for both.
    /// <para>
    /// If a schedule moves to config, validate it as <see cref="DailyResetScheduleRules"/> validates
    /// <see cref="GlobalConfig.DailyResetSchedule"/>, which must give the same days as <see cref="Daily"/>
    /// (<c>MissionTests</c> checks this). A new start date would end every player's current window and its progress.
    /// </para>
    /// </summary>
    public static class PlayerCalendar
    {
        /// <summary>
        /// The start date of <see cref="Daily"/>. Any local midnight would give the same windows. It is at the
        /// Unix epoch so that every reachable time, including one shifted earlier by a negative UTC offset, is
        /// after the start.
        /// </summary>
        static readonly MetaCalendarDateTime DailyEpoch = new MetaCalendarDateTime(1970, 1, 1, 0, 0, 0);

        /// <summary>
        /// The start date of <see cref="Weekly"/>. It must be a Monday, because every week starts on the same
        /// weekday as this date. It is the Monday before the Unix epoch.
        /// </summary>
        static readonly MetaCalendarDateTime WeeklyEpoch = new MetaCalendarDateTime(1969, 12, 29, 0, 0, 0);

        /// <summary>One window per day, from local midnight to local midnight, with no end.</summary>
        public static readonly MetaRecurringCalendarSchedule Daily = new MetaRecurringCalendarSchedule(
            timeMode:   MetaScheduleTimeMode.Local,
            start:      DailyEpoch,
            duration:   new MetaCalendarPeriod(0, 0, days: 1, 0, 0, 0),
            endingSoon: new MetaCalendarPeriod(),
            preview:    new MetaCalendarPeriod(),
            review:     new MetaCalendarPeriod(),
            recurrence: new MetaCalendarPeriod(0, 0, days: 1, 0, 0, 0),
            numRepeats: null);

        /// <summary>One window per week, from local midnight on Monday to local midnight on the next Monday, with no end.</summary>
        public static readonly MetaRecurringCalendarSchedule Weekly = new MetaRecurringCalendarSchedule(
            timeMode:   MetaScheduleTimeMode.Local,
            start:      WeeklyEpoch,
            duration:   new MetaCalendarPeriod(0, 0, days: 7, 0, 0, 0),
            endingSoon: new MetaCalendarPeriod(),
            preview:    new MetaCalendarPeriod(),
            review:     new MetaCalendarPeriod(),
            recurrence: new MetaCalendarPeriod(0, 0, days: 7, 0, 0, 0),
            numRepeats: null);

        /// <summary>
        /// The window of <paramref name="schedule"/> that contains <paramref name="at"/>. The SDK computes it
        /// directly, so the cost does not grow with the time since the schedule's start.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// No window contains <paramref name="at"/>: it is before the schedule's start or in a gap between
        /// windows. <see cref="Daily"/> and <see cref="Weekly"/> never throw, because each starts at or before
        /// the epoch, has no end, and has a duration equal to its recurrence. A schedule with a duration shorter
        /// than its recurrence has gaps.
        /// </exception>
        public static PlayerCalendarWindow WindowAt(MetaScheduleBase schedule, PlayerLocalTime at)
        {
            MetaScheduleOccasion? occasion = schedule.TryGetCurrentOccasion(at);
            if (occasion == null)
                throw new InvalidOperationException($"the schedule has no window covering {at.Time} at offset {at.UtcOffset}; it is not total");

            return new PlayerCalendarWindow(occasion.Value.EnabledRange.Start, occasion.Value.EnabledRange.End);
        }
    }
}
