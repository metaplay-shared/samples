using Game.Logic;
using Metaplay.Core;
using Metaplay.Core.Config;
using Metaplay.Core.LiveOpsEvent;
using Metaplay.Core.Schedule;
using Metaplay.Server.LiveOpsEvent;
using System.Collections.Generic;

namespace Game.Server.WeeklyEvent
{
    /// <summary>
    /// Converts the weekly event plan from the shared calendar into the SDK's LiveOps event export-import
    /// package. Import is the only SDK creation path that lets the caller choose the event ids
    /// (<c>docs/weekly-event.md</c>).
    /// <para>
    /// <b>It has no side effects</b>, so tests can check what the seeder will send without a server. The
    /// schedule, the untargeted audience, the content and the ids are all decided here.
    /// </para>
    /// </summary>
    public static class WeeklyEventSeedPackage
    {
        /// <summary>The only package format version the SDK's import accepts.</summary>
        public const int PackageFormatVersion = 1;

        /// <summary>The LiveOps Dashboard color name used for every weekly event on the timeline.</summary>
        public const string TimelineColor = "Metaplay";

        /// <summary>
        /// Creates the SDK event settings for one planned week: a non-recurring UTC calendar schedule, and
        /// untargeted params with a copy of the week's content.
        /// </summary>
        public static LiveOpsEventSettings SettingsFor(in WeeklyEventSeedWeek week) =>
            SettingsFor(
                week.TemplateId, week.Content, week.EnabledStartsAt,
                WeeklyEventSeeding.WeekLength, WeeklyEventSeeding.EndingSoonLength,
                WeeklyEventSeeding.PreviewLength, WeeklyEventSeeding.ReviewLength);

        /// <summary>
        /// Creates the SDK event settings for a weekly event with any schedule. Both the seeder and the test route
        /// use this method, so their events have the same structure. <paramref name="enabledStartsAt"/> is
        /// truncated to whole seconds, because the SDK's calendar schedule stores whole seconds.
        /// </summary>
        public static LiveOpsEventSettings SettingsFor(
            LiveOpsEventTemplateId templateId,
            WeeklyEventContent     content,
            MetaTime               enabledStartsAt,
            MetaDuration           duration,
            MetaDuration           endingSoon,
            MetaDuration           preview,
            MetaDuration           review)
        {
            MetaRecurringCalendarSchedule schedule = new MetaRecurringCalendarSchedule(
                timeMode:   MetaScheduleTimeMode.Utc,
                start:      MetaCalendarDateTime.FromDateTime(enabledStartsAt.ToDateTime()),
                duration:   PeriodOf(duration),
                endingSoon: PeriodOf(endingSoon),
                preview:    PeriodOf(preview),
                review:     PeriodOf(review),
                // No recurrence, because the SDK refuses a recurring LiveOps event schedule. Each seeded week is a
                // separate event, so an operator can edit or conclude one week without affecting the others.
                recurrence: null,
                numRepeats: null);

            return new LiveOpsEventSettings(
                schedule,
                new LiveOpsEventParams(
                    displayName:          content.Theme,
                    description:          content.Tagline,
                    color:                TimelineColor,
                    targetPlayersMaybe:   null,
                    // Untargeted, so that every player has the event (docs/weekly-event.md, "Audience").
                    targetConditionMaybe: null,
                    templateIdMaybe:      templateId,
                    content:              content));
        }

        /// <summary>
        /// Creates one package for the whole plan. The ids come from the plan, so importing it again changes nothing.
        /// </summary>
        public static LiveOpsEventExportImport.Package Build(IReadOnlyList<WeeklyEventSeedWeek> plan)
        {
            List<LiveOpsEventExportImport.ExportedEvent> events = new List<LiveOpsEventExportImport.ExportedEvent>();

            if (plan != null)
            {
                foreach (WeeklyEventSeedWeek week in plan)
                    events.Add(LiveOpsEventExportImport.ExportedEvent.Create(week.OccurrenceId, week.SpecId, SettingsFor(week)));
            }

            return new LiveOpsEventExportImport.Package(PackageFormatVersion, events);
        }

        /// <summary>
        /// Returns the validation errors for the planned weeks, from the content's own
        /// <see cref="LiveOpsEventContent.Validate"/>.
        /// <para>
        /// This is the same validation the LiveOps Dashboard's create path runs. It runs <b>before</b> the import,
        /// because the SDK's import validates only the settings (schedule type, display name, non-null content),
        /// not the game's content rules. An invalid week is therefore refused with the problem named, instead of
        /// reaching players as an event that cannot be completed.
        /// </para>
        /// </summary>
        public static List<string> ProblemsWith(IReadOnlyList<WeeklyEventSeedWeek> plan, FullGameConfig activeGameConfig)
        {
            List<string> problems = new List<string>();
            if (plan == null)
                return problems;

            foreach (WeeklyEventSeedWeek week in plan)
            {
                foreach (string problem in ProblemsWith(week.Content, activeGameConfig))
                    problems.Add($"{week.TemplateId} in week {week.WeekIndex}: {problem}");
            }

            return problems;
        }

        /// <summary>Returns the validation errors for one week's content.</summary>
        public static List<string> ProblemsWith(WeeklyEventContent content, FullGameConfig activeGameConfig)
        {
            CollectingValidationLog log = new CollectingValidationLog();
            content.Validate(log, activeGameConfig);
            return log.Errors;
        }

        /// <summary>
        /// Converts a duration to the calendar period the SDK's schedule takes, dropping fractions of a second.
        /// </summary>
        public static MetaCalendarPeriod PeriodOf(MetaDuration duration) => MetaCalendarPeriod.FromMetaDuration(duration);

        /// <summary>
        /// Collects the errors reported by <see cref="LiveOpsEventContent.Validate"/>. Warnings are ignored.
        /// </summary>
        sealed class CollectingValidationLog : ILiveOpsEventValidationLog
        {
            public List<string> Errors { get; } = new List<string>();

            public void Error(string msg, string memberNameOrNull = null) => Errors.Add(memberNameOrNull == null ? msg : $"{memberNameOrNull}: {msg}");
            public void Warning(string msg, string memberNameOrNull = null) { }
        }
    }
}
