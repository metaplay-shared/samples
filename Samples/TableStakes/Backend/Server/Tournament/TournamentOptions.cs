using Game.Logic;
using Metaplay.Cloud.RuntimeOptions;
using Metaplay.Core;
using Metaplay.Core.League;
using Metaplay.Core.Schedule;
using Metaplay.Server.League;
using System;

namespace Game.Server.Tournament
{
    /// <summary>
    /// The seasonal tournament's schedule (<c>docs/seasonal-tournament.md</c>).
    /// <para>
    /// <b>The schedule is in runtime options, not game config.</b> It affects when the SDK's between-seasons
    /// migration job runs, so an operator changes it with a deploy rather than a designer with a config
    /// publish. Game config holds what designers tune: the milestone thresholds, the placement bands and their
    /// rewards (<see cref="TournamentRewardTableInfo"/>).
    /// </para>
    /// </summary>
    [RuntimeOptions("Tournament", isStatic: false, "Cadence of the seasonal tournament. The player-facing rules live in game config; this is the schedule the season runs on.")]
    public class TournamentOptions : LeagueManagerOptionsBase
    {
        public TournamentOptions()
        {
            // The rest period at the end of each cycle is when the between-seasons migration job runs, and the
            // SDK does not let players join during it. It must stay longer than the job takes.
            SeasonCycleStartDate        = new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc);
            SeasonCycleRecurrence       = new MetaCalendarPeriod(years: 0, months: 0, days: 7, hours: 0, minutes: 0, seconds: 0);
            SeasonCycleRestPeriod       = new MetaCalendarPeriod(years: 0, months: 0, days: 0, hours: 2, minutes: 0, seconds: 0);
            SeasonCycleEndingSoonPeriod = new MetaCalendarPeriod(years: 0, months: 0, days: 0, hours: 4, minutes: 0, seconds: 0);

            // Use the recurring schedule from these options rather than a custom schedule, because the sample
            // needs only regular seasons.
            ScheduleType = LeagueSeasonScheduleType.RuntimeOptions;

            // Fill a group that lost a participant before opening a new group, so joining humans stay together.
            AllowDivisionBackFill = true;

            TimelineColor = "#c9a227";
        }
    }
}
