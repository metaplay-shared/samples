using Game.Logic;
using Metaplay.Core;
using Metaplay.Core.Config;
using Metaplay.Core.LiveOpsEvent;
using Metaplay.Server;
using Metaplay.Server.LiveOpsEvent;
using Metaplay.Server.LiveOpsTimeline;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Game.Server.WeeklyEvent
{
    /// <summary>
    /// Test-only endpoint that creates one weekly event from a config template, with a caller-given schedule and
    /// target, for E2E tests that need a week the seeder cannot produce. It is not a seeding mechanism:
    /// <see cref="WeeklyEventSeederActor"/> keeps the weekly events on the timeline (<c>docs/weekly-event.md</c>).
    /// <para>
    /// By default it concludes every other weekly event whose scoring window overlaps the new one, because only one
    /// weekly event may score at a time. The seeder never recreates a concluded week, because it imports with the
    /// same id, which the timeline already has. Only <see cref="TestRoutesOptions"/> enables the endpoint.
    /// </para>
    /// </summary>
    public class WeeklyEventTestController : TestRouteController
    {
        /// <summary>The JSON response: the created event, its content values, and its scoring window.</summary>
        public class CreatedWeeklyEvent
        {
            public string EventId      { get; set; }
            public string Template     { get; set; }
            public string Theme        { get; set; }
            public int    TargetPoints { get; set; }
            public int    WinBonus     { get; set; }
            public string StartsAt     { get; set; }
            public string EndsAt       { get; set; }
        }

        /// <summary>
        /// Creates one weekly event. The template, target, bonus and every schedule window are optional query
        /// parameters. Without them, the event is a normal week from the first template, starting now.
        /// </summary>
        [HttpPost("test/weeklyevent")]
        public async Task<IActionResult> CreateWeeklyEvent(
            [FromQuery] string template          = null,
            [FromQuery] int?   targetPoints      = null,
            [FromQuery] int?   winBonusPoints    = null,
            [FromQuery] int    startInSeconds    = 0,
            [FromQuery] int    durationSeconds   = 7 * 24 * 60 * 60,
            [FromQuery] int    previewSeconds    = 0,
            [FromQuery] int    endingSoonSeconds = 0,
            [FromQuery] int    reviewSeconds     = 0,
            [FromQuery] bool   concludeOverlapping = true)
        {
            if (durationSeconds <= 0)
                return BadRequest(new { error = "durationSeconds must be positive" });

            FullGameConfig    activeConfig = GlobalStateProxyActor.ActiveGameConfig.Get().BaselineGameConfig;
            SharedGameConfig  shared       = activeConfig.SharedConfig as SharedGameConfig;
            if (shared?.WeeklyEventTemplates == null || shared.WeeklyEventTemplates.Count == 0)
                return NotFound(new { error = "the published config holds no weekly-event templates" });

            LiveOpsEventTemplateId templateId = template == null ? null : LiveOpsEventTemplateId.FromString(template);
            WeeklyEventTemplateInfo chosen = null;
            foreach (WeeklyEventTemplateInfo candidate in shared.WeeklyEventTemplates.Values)
            {
                if (templateId == null || candidate.TemplateId == templateId)
                {
                    chosen = candidate;
                    break;
                }
            }

            if (chosen?.Content == null)
                return NotFound(new { error = $"no weekly-event template named '{template}'" });

            // Copy the content with the overrides instead of editing the config item, which is shared by every
            // player on the server. The SDK detects a mutated config item.
            WeeklyEventContent content = new WeeklyEventContent(
                chosen.Content.Theme,
                chosen.Content.Tagline,
                targetPoints ?? chosen.Content.TargetPoints,
                winBonusPoints ?? chosen.Content.WinBonusPoints,
                chosen.Content.Reward);

            // Run the same content validation as the LiveOps Dashboard's create path, because the overrides may
            // make the content invalid.
            List<string> problems = WeeklyEventSeedPackage.ProblemsWith(content, activeConfig);
            if (problems.Count > 0)
                return BadRequest(new { error = "the weekly event's content is invalid", problems });

            // Truncate to whole seconds, which is what the SDK's calendar schedule stores, so the window in the
            // response matches the event's.
            DateTime startRaw = DateTime.UtcNow.AddSeconds(startInSeconds);
            DateTime start    = new DateTime(startRaw.Year, startRaw.Month, startRaw.Day, startRaw.Hour, startRaw.Minute, startRaw.Second, DateTimeKind.Utc);
            DateTime end      = start.AddSeconds(durationSeconds);

            LiveOpsEventSettings settings = WeeklyEventSeedPackage.SettingsFor(
                chosen.TemplateId, content, MetaTime.FromDateTime(start),
                duration:   MetaDuration.FromSeconds(durationSeconds),
                endingSoon: MetaDuration.FromSeconds(endingSoonSeconds),
                preview:    MetaDuration.FromSeconds(previewSeconds),
                review:     MetaDuration.FromSeconds(reviewSeconds));

            CreateLiveOpsEventResponse response;
            try
            {
                response = await EntityAskAsync(LiveOpsTimelineManager.EntityId, new CreateLiveOpsEventRequest(validateOnly: false, settings));
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = $"the LiveOps timeline did not answer: {ex.Message}" });
            }

            if (!response.IsValid || response.InitialEventOccurrenceId == null)
                return BadRequest(new { error = "the LiveOps timeline refused the event", diagnostics = response.Diagnostics?.DiagnosticsPerScope });

            // Conclude the other events only after the create succeeded. Concluding first and then failing to
            // create would leave no scoring weekly event, and a concluded week cannot be restored. In this order,
            // the two weeks overlap only for the duration of one ask.
            MetaTime enabledStart = MetaTime.FromDateTime(start);
            MetaTime enabledEnd   = MetaTime.FromDateTime(end);

            if (concludeOverlapping)
            {
                try
                {
                    await ConcludeOverlappingWeeklyEventsAsync(response.InitialEventOccurrenceId.Value, enabledStart, enabledEnd);
                }
                catch (Exception ex)
                {
                    return StatusCode(500, new { error = $"the week was created, but the weekly events it overlaps could not be cleared: {ex.Message}" });
                }
            }

            return Ok(new CreatedWeeklyEvent
            {
                EventId      = response.InitialEventOccurrenceId.Value.ToString(),
                Template     = chosen.TemplateId.ToString(),
                Theme        = content.Theme,
                TargetPoints = content.TargetPoints,
                WinBonus     = content.WinBonusPoints,
                StartsAt     = start.ToString("O"),
                EndsAt       = end.ToString("O"),
            });
        }

        /// <summary>
        /// Concludes every weekly event other than <paramref name="keepOccurrenceId"/> whose scoring window overlaps
        /// <paramref name="enabledStart"/> to <paramref name="enabledEnd"/>, so that
        /// <paramref name="keepOccurrenceId"/> is the only weekly event scoring in that window. A weekly event with no
        /// schedule is enabled permanently, so it overlaps every window and is concluded.
        /// <para>
        /// A failure to conclude an event throws, because continuing would leave two scoring events and report
        /// success.
        /// </para>
        /// </summary>
        async Task ConcludeOverlappingWeeklyEventsAsync(MetaGuid keepOccurrenceId, MetaTime enabledStart, MetaTime enabledEnd)
        {
            GetLiveOpsEventsResponse existing = await EntityAskAsync(LiveOpsTimelineManager.EntityId, new GetLiveOpsEventsRequest());

            foreach (LiveOpsEventOccurrence occurrence in existing.Occurrences)
            {
                if (occurrence.OccurrenceId == keepOccurrenceId)
                    continue;
                if (occurrence.EventParams?.Content is not WeeklyEventContent)
                    continue;
                if (occurrence.ExplicitlyConcludedAt.HasValue)
                    continue;

                LiveOpsEventScheduleOccasion occasion = occurrence.UtcScheduleOccasionMaybe;
                if (occasion != null && (occasion.GetEnabledStartTime() >= enabledEnd || occasion.GetEnabledEndTime() <= enabledStart))
                    continue;

                ConcludeLiveOpsEventResponse concluded = await EntityAskAsync(LiveOpsTimelineManager.EntityId, new ConcludeLiveOpsEventRequest(occurrence.OccurrenceId));
                if (!concluded.IsSuccess)
                    throw new InvalidOperationException($"the timeline refused to conclude {occurrence.OccurrenceId}: {concluded.Error}");
            }
        }
    }
}
