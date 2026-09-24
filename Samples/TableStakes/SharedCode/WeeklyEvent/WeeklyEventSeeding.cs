using Metaplay.Core;
using Metaplay.Core.LiveOpsEvent;
using System;
using System.Collections.Generic;

namespace Game.Logic
{
    /// <summary>
    /// One week the seeder should create: its index, template, content, window, and the two IDs that
    /// permanently identify it (<c>docs/weekly-event.md</c>).
    /// </summary>
    public readonly struct WeeklyEventSeedWeek
    {
        /// <summary>The week number since <see cref="WeeklyEventSeeding.WeekAnchor"/>. The week's IDs are computed from it.</summary>
        public int WeekIndex { get; }

        /// <summary>The SDK occurrence ID of this week, computed by <see cref="WeeklyEventSeeding.OccurrenceIdFor"/>.</summary>
        public MetaGuid OccurrenceId { get; }

        /// <summary>The SDK spec ID of this week, computed by <see cref="WeeklyEventSeeding.SpecIdFor"/>.</summary>
        public MetaGuid SpecId { get; }

        /// <summary>The template the content was copied from, recorded on the event for operators.</summary>
        public LiveOpsEventTemplateId TemplateId { get; }

        /// <summary>
        /// A <b>copy</b> of the template's content. A config item is shared by every player on the server and the
        /// SDK detects when one is modified, so the event creation path must never receive the config library's
        /// own instance.
        /// </summary>
        public WeeklyEventContent Content { get; }

        /// <summary>The enabled window: when scoring starts and when it ends.</summary>
        public MetaTime EnabledStartsAt { get; }
        public MetaTime EnabledEndsAt   { get; }

        public WeeklyEventSeedWeek(int weekIndex, MetaGuid occurrenceId, MetaGuid specId, LiveOpsEventTemplateId templateId, WeeklyEventContent content, MetaTime enabledStartsAt, MetaTime enabledEndsAt)
        {
            WeekIndex       = weekIndex;
            OccurrenceId    = occurrenceId;
            SpecId          = specId;
            TemplateId      = templateId;
            Content         = content;
            EnabledStartsAt = enabledStartsAt;
            EnabledEndsAt   = enabledEndsAt;
        }

        public override string ToString() => $"week {WeekIndex} ({TemplateId}) {EnabledStartsAt}..{EnabledEndsAt} as {OccurrenceId}";
    }

    /// <summary>
    /// Computes which weekly events should exist, their content and their permanent IDs
    /// (<c>docs/weekly-event.md</c>). Weeks are computed from an anchor, lengths and a template rotation, with no
    /// end date, because a fixed list of dates would run out and the events would stop without any error.
    /// Every method is pure and deterministic, so every server and deploy computes the same weeks with the same
    /// IDs. The import is therefore idempotent: a repeated pass offers IDs the SDK already has.
    /// </summary>
    public static class WeeklyEventSeeding
    {
        /// <summary>
        /// The start of week zero: a Monday at 00:00 UTC, because the event uses UTC for all players.
        /// <para>
        /// This value must never change. Every week's IDs are computed from it, so a change would create a second
        /// event for the same days and silently reset every player's progress, which is keyed by event ID. A unit
        /// test checks this value and two known week IDs.
        /// </para>
        /// </summary>
        public static readonly MetaTime WeekAnchor = MetaTime.FromDateTime(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        /// <summary>The length of a week's enabled window, so consecutive windows have no gap and no overlap.</summary>
        public static readonly MetaDuration WeekLength = MetaDuration.FromDays(7);

        /// <summary>
        /// How long a week is visible before its enabled window starts. It overlaps the end of the previous
        /// week, so for part of every week a player has two weekly events, one in Preview and one scoring.
        /// Preview does not score, so only one week scores at a time.
        /// </summary>
        public static readonly MetaDuration PreviewLength = MetaDuration.FromDays(2);

        /// <summary>
        /// How long a reward stays claimable after scoring ends. It overlaps the start of the next week, so a
        /// reward earned in the previous week can still be claimed while the current week scores.
        /// </summary>
        public static readonly MetaDuration ReviewLength = MetaDuration.FromDays(1);

        /// <summary>
        /// The final part of the enabled window, which the SDK reports as the <c>EndingSoon</c> phase. The shell
        /// uses its own threshold for its "ending soon" text. This value only makes seeded weeks look like
        /// hand-authored weeks on the Dashboard's timeline.
        /// </summary>
        public static readonly MetaDuration EndingSoonLength = MetaDuration.FromDays(1);

        /// <summary>
        /// The value part of every seeded occurrence ID. Must never change, for the same reason as
        /// <see cref="WeekAnchor"/>: it is part of every week's ID.
        /// </summary>
        public const ulong OccurrenceIdValue = 0x5745_454B_4C59_0001;

        /// <summary>
        /// The value part of every seeded spec ID. It must differ from <see cref="OccurrenceIdValue"/>, because
        /// the SDK's import refuses a spec ID that already belongs to a different occurrence.
        /// </summary>
        public const ulong SpecIdValue = 0x5745_454B_4C59_0002;

        /// <summary>
        /// The default number of weeks a pass keeps created, including the current week.
        /// <para>
        /// It is counted from each pass, so every pass extends it. It decides how far ahead operators can see and
        /// edit weeks on the Dashboard's timeline, and how many generated weeks they see there. It is further
        /// ahead than themed weeks are usually planned, and short enough to keep the timeline readable.
        /// </para>
        /// </summary>
        public const int DefaultHorizonWeeks = 8;

        /// <summary>The most weeks one pass creates, so a misconfigured horizon cannot fill the timeline.</summary>
        public const int MaxHorizonWeeks = 104;

        /// <summary>The index of the week that contains <paramref name="instant"/>. Negative before the anchor.</summary>
        public static int WeekIndexAt(MetaTime instant)
        {
            long elapsedMs    = (instant - WeekAnchor).Milliseconds;
            long weekLengthMs = WeekLength.Milliseconds;

            // Floor division, not truncation, so a time before the anchor maps to the week that contains it.
            long index = elapsedMs >= 0 ? elapsedMs / weekLengthMs : -(((-elapsedMs) + weekLengthMs - 1) / weekLengthMs);
            return checked((int)index);
        }

        /// <summary>When a week's enabled window starts.</summary>
        public static MetaTime WeekStart(int weekIndex) => WeekAnchor + WeekLength * weekIndex;

        /// <summary>When a week's enabled window ends, which is when the next week's starts.</summary>
        public static MetaTime WeekEnd(int weekIndex) => WeekStart(weekIndex + 1);

        /// <summary>
        /// The occurrence ID of a week, built with <see cref="MetaGuid.FromTimeAndValue"/> from the week's start,
        /// so IDs sort by date and show which week they belong to.
        /// </summary>
        public static MetaGuid OccurrenceIdFor(int weekIndex) => MetaGuid.FromTimeAndValue(WeekStart(weekIndex), OccurrenceIdValue);

        /// <summary>The spec ID of a week, built the same way as <see cref="OccurrenceIdFor"/>.</summary>
        public static MetaGuid SpecIdFor(int weekIndex) => MetaGuid.FromTimeAndValue(WeekStart(weekIndex), SpecIdValue);

        /// <summary>
        /// Returns the templates in rotation order, <b>sorted by template ID</b>.
        /// <para>
        /// They are sorted because the config library's iteration order is not defined, and the theme of a
        /// given week must depend only on the templates. Adding a template changes the themes of weeks not yet
        /// created. Created weeks keep their content, which was copied onto the event at creation.
        /// </para>
        /// </summary>
        public static List<WeeklyEventTemplateInfo> RotationOrder(IEnumerable<WeeklyEventTemplateInfo> templates)
        {
            List<WeeklyEventTemplateInfo> ordered = new List<WeeklyEventTemplateInfo>();
            if (templates == null)
                return ordered;

            foreach (WeeklyEventTemplateInfo template in templates)
            {
                if (template?.TemplateId != null && template.Content != null)
                    ordered.Add(template);
            }

            ordered.Sort((left, right) => string.CompareOrdinal(left.TemplateId.Value, right.TemplateId.Value));
            return ordered;
        }

        /// <summary>The template a week is created from, cycling through <paramref name="rotation"/> by week index.</summary>
        public static WeeklyEventTemplateInfo TemplateForWeek(int weekIndex, IReadOnlyList<WeeklyEventTemplateInfo> rotation)
        {
            if (rotation == null || rotation.Count == 0)
                return null;

            int rotationIndex = weekIndex % rotation.Count;
            if (rotationIndex < 0)
                rotationIndex += rotation.Count;

            return rotation[rotationIndex];
        }

        /// <summary>
        /// Returns the weeks that should exist at <paramref name="now"/>: the current week and the
        /// <paramref name="horizonWeeks"/> - 1 weeks after it. It starts at the current week so a new environment has
        /// a running event at once: the SDK moves an imported event whose start is in the past to its current phase.
        /// It does not read the server's existing events. The SDK's import compares them with the plan, so two
        /// servers cannot both create the same week.
        /// </summary>
        public static List<WeeklyEventSeedWeek> Plan(MetaTime now, int horizonWeeks, IEnumerable<WeeklyEventTemplateInfo> templates)
        {
            List<WeeklyEventSeedWeek> plan = new List<WeeklyEventSeedWeek>();

            List<WeeklyEventTemplateInfo> rotation = RotationOrder(templates);
            if (rotation.Count == 0)
                return plan;

            int weekCount = Math.Clamp(horizonWeeks, 1, MaxHorizonWeeks);

            int firstWeekIndex = WeekIndexAt(now);
            for (int offset = 0; offset < weekCount; offset++)
            {
                int                     weekIndex = firstWeekIndex + offset;
                WeeklyEventTemplateInfo template  = TemplateForWeek(weekIndex, rotation);

                plan.Add(new WeeklyEventSeedWeek(
                    weekIndex,
                    OccurrenceIdFor(weekIndex),
                    SpecIdFor(weekIndex),
                    template.TemplateId,
                    CopyOf(template.Content),
                    WeekStart(weekIndex),
                    WeekEnd(weekIndex)));
            }

            return plan;
        }

        /// <summary>When the last week of <paramref name="plan"/> stops scoring, or <see cref="WeekAnchor"/> for an empty plan.</summary>
        public static MetaTime SeededThrough(IReadOnlyList<WeeklyEventSeedWeek> plan)
            => plan == null || plan.Count == 0 ? WeekAnchor : plan[plan.Count - 1].EnabledEndsAt;

        /// <summary>
        /// Returns a new content object with the template's values. See <see cref="WeeklyEventSeedWeek.Content"/>
        /// for why the template's own instance must not be used.
        /// </summary>
        static WeeklyEventContent CopyOf(WeeklyEventContent content) =>
            new WeeklyEventContent(content.Theme, content.Tagline, content.TargetPoints, content.WinBonusPoints, content.Reward);
    }
}
