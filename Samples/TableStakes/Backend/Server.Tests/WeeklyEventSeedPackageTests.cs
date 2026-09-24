using Game.Logic;
using Game.Server.WeeklyEvent;
using Metaplay.Core;
using Metaplay.Core.LiveOpsEvent;
using Metaplay.Core.Schedule;
using Metaplay.Server.LiveOpsEvent;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.Server.Tests
{
    /// <summary>
    /// Tests the package that a seeding pass sends to the SDK's import (<c>docs/weekly-event.md</c>).
    /// <para>
    /// It is a server test because <c>LiveOpsEventSettings</c> is in the SDK's server assembly, which
    /// <c>SharedCode/</c> cannot reference. The calendar is shared code and tested there. These tests cover the
    /// conversion: the schedule, the audience, and the round trip through the package's base64 encoding, which
    /// is what the timeline reads.
    /// </para>
    /// </summary>
    [TestFixture]
    public class WeeklyEventSeedPackageTests
    {
        static readonly LiveOpsEventTemplateId TrickstersWeek = LiveOpsEventTemplateId.FromString("tricksters-week");
        static readonly LiveOpsEventTemplateId HighSuitSeason = LiveOpsEventTemplateId.FromString("high-suit-season");

        static List<WeeklyEventTemplateInfo> Templates() => new List<WeeklyEventTemplateInfo>
        {
            new WeeklyEventTemplateInfo(TrickstersWeek, new WeeklyEventContent("Trickster's Week", "Every trick counts.", 100, 5,
                new RewardBundle(CurrencyAmount.Coins(2000), CurrencyAmount.Gems(100)))),
            new WeeklyEventTemplateInfo(HighSuitSeason, new WeeklyEventContent("High Suit Season", "The table is worth more.", 120, 8,
                new RewardBundle(CurrencyAmount.Coins(2500), CurrencyAmount.Gems(125)))),
        };

        static List<WeeklyEventSeedWeek> Plan(int horizonWeeks = 8) =>
            WeeklyEventSeeding.Plan(MetaTime.FromDateTime(new DateTime(2026, 9, 3, 12, 0, 0, DateTimeKind.Utc)), horizonWeeks, Templates());

        #region The envelope the import reads

        [Test]
        public void ThePackageDeclaresTheOnlyFormatVersionTheImportAccepts()
        {
            Assert.That(WeeklyEventSeedPackage.Build(Plan()).PackageFormatVersion, Is.EqualTo(1));
        }

        /// <summary>
        /// The package uses the calendar's ids, which is how a repeated import recognizes an existing week. The
        /// import refuses the whole package if it names one occurrence or spec twice, so the calendar's ids being
        /// unique (<c>WeeklyEventSeedingTests</c>) is what lets every week be created.
        /// </summary>
        [Test]
        public void ThePackageCarriesTheCalendarsOwnIds()
        {
            List<WeeklyEventSeedWeek>                    plan   = Plan();
            List<LiveOpsEventExportImport.ExportedEvent> events = WeeklyEventSeedPackage.Build(plan).Events;

            Assert.That(events, Has.Count.EqualTo(plan.Count), "the package holds an entry the plan does not");
            for (int index = 0; index < plan.Count; index++)
            {
                Assert.That(events[index].OccurrenceId, Is.EqualTo(WeeklyEventSeeding.OccurrenceIdFor(plan[index].WeekIndex)));
                Assert.That(events[index].SpecId, Is.EqualTo(WeeklyEventSeeding.SpecIdFor(plan[index].WeekIndex)));
            }
        }

        #endregion

        #region The schedule shape the weekly event holds seeding to

        [Test]
        public void EveryWeekIsAUtcCalendarWeekWithNoRecurrence()
        {
            foreach (WeeklyEventSeedWeek week in Plan(horizonWeeks: 6))
            {
                LiveOpsEventSettings settings = WeeklyEventSeedPackage.SettingsFor(week);

                Assert.That(settings.ScheduleMaybe, Is.InstanceOf<MetaRecurringCalendarSchedule>(), $"week {week.WeekIndex}");
                MetaRecurringCalendarSchedule schedule = (MetaRecurringCalendarSchedule)settings.ScheduleMaybe;

                Assert.That(schedule.TimeMode, Is.EqualTo(MetaScheduleTimeMode.Utc), "the weekly event is a shared window, not a personal one");
                // The SDK refuses a recurring LiveOps event schedule, so every week is a separate event.
                Assert.That(schedule.Recurrence, Is.Null);
                Assert.That(schedule.NumRepeats, Is.Null);

                Assert.That(schedule.Start.ToDateTime(), Is.EqualTo(week.EnabledStartsAt.ToDateTime()));
                Assert.That(schedule.Duration.Days, Is.EqualTo(7));
                Assert.That(schedule.Preview.Days, Is.EqualTo(2));
                Assert.That(schedule.Review.Days, Is.EqualTo(1));
                Assert.That(schedule.EndingSoon.Days, Is.EqualTo(1));
            }
        }

        [Test]
        public void ADurationBecomesTheCalendarPeriodTheScheduleTakes()
        {
            Assert.That(WeeklyEventSeedPackage.PeriodOf(MetaDuration.FromDays(7)).Days, Is.EqualTo(7));
            Assert.That(WeeklyEventSeedPackage.PeriodOf(MetaDuration.FromHours(30)).Days, Is.EqualTo(1));
            Assert.That(WeeklyEventSeedPackage.PeriodOf(MetaDuration.FromHours(30)).Hours, Is.EqualTo(6));
            Assert.That(WeeklyEventSeedPackage.PeriodOf(MetaDuration.Zero).IsNone, Is.True);
        }

        #endregion

        #region What the event says, and to whom

        /// <summary>
        /// Every seeded week is untargeted, so every player has a running event (<c>docs/weekly-event.md</c>,
        /// "Audience").
        /// </summary>
        [Test]
        public void EverySeededWeekIsUntargeted()
        {
            foreach (WeeklyEventSeedWeek week in Plan(horizonWeeks: 6))
            {
                LiveOpsEventParams eventParams = WeeklyEventSeedPackage.SettingsFor(week).EventParams;

                Assert.That(eventParams.TargetPlayersMaybe, Is.Null, $"week {week.WeekIndex}");
                Assert.That(eventParams.TargetConditionMaybe, Is.Null, $"week {week.WeekIndex}");
            }
        }

        /// <summary>
        /// The template id is stored on the event. The game does not read it, because the content is copied, but
        /// it tells an operator which template the week came from.
        /// </summary>
        [Test]
        public void TheTemplateAWeekWasCreatedFromIsRecordedOnIt()
        {
            foreach (WeeklyEventSeedWeek week in Plan(horizonWeeks: 6))
                Assert.That(WeeklyEventSeedPackage.SettingsFor(week).EventParams.TemplateIdMaybe, Is.EqualTo(week.TemplateId));
        }

        [Test]
        public void TheEventIsNamedAndDescribedByItsOwnTheme()
        {
            foreach (WeeklyEventSeedWeek week in Plan(horizonWeeks: 4))
            {
                LiveOpsEventParams eventParams = WeeklyEventSeedPackage.SettingsFor(week).EventParams;

                // The import refuses settings with no display name.
                Assert.That(eventParams.DisplayName, Is.EqualTo(week.Content.Theme));
                Assert.That(eventParams.DisplayName, Is.Not.Empty);
                Assert.That(eventParams.Description, Is.EqualTo(week.Content.Tagline));
            }
        }

        [Test]
        public void TheEventUsesTheDashboardsMetaplayColor()
        {
            List<LiveOpsEventExportImport.ExportedEvent> events = WeeklyEventSeedPackage.Build(Plan(horizonWeeks: 4)).Events;

            foreach (LiveOpsEventExportImport.ExportedEvent exportedEvent in events)
                Assert.That(exportedEvent.DecodeSettings().EventParams.Color, Is.EqualTo(WeeklyEventSeedPackage.TimelineColor));
        }

        /// <summary>
        /// The settings are stored in the package as base64, and the timeline creates the event from the decoded
        /// settings. The test decodes them, so it also covers the serialization.
        /// </summary>
        [Test]
        public void TheContentSurvivesTheRoundTripThroughThePackage()
        {
            List<WeeklyEventSeedWeek>                    plan   = Plan(horizonWeeks: 4);
            List<LiveOpsEventExportImport.ExportedEvent> events = WeeklyEventSeedPackage.Build(plan).Events;

            for (int index = 0; index < plan.Count; index++)
            {
                WeeklyEventContent decoded = events[index].DecodeSettings().EventParams.Content as WeeklyEventContent;

                Assert.That(decoded, Is.Not.Null, $"week {plan[index].WeekIndex} did not come back as weekly-event content");
                Assert.That(decoded.Theme, Is.EqualTo(plan[index].Content.Theme));
                Assert.That(decoded.Tagline, Is.EqualTo(plan[index].Content.Tagline));
                Assert.That(decoded.TargetPoints, Is.EqualTo(plan[index].Content.TargetPoints));
                Assert.That(decoded.WinBonusPoints, Is.EqualTo(plan[index].Content.WinBonusPoints));
                // Compare each amount rather than ToString output, which could omit a currency and still match.
                Assert.That(decoded.Reward, Is.Not.Null);
                Assert.That(decoded.Reward.Amounts.Count, Is.EqualTo(plan[index].Content.Reward.Amounts.Count));
                for (int amount = 0; amount < decoded.Reward.Amounts.Count; amount++)
                {
                    Assert.That(decoded.Reward.Amounts[amount].Currency, Is.EqualTo(plan[index].Content.Reward.Amounts[amount].Currency));
                    Assert.That(decoded.Reward.Amounts[amount].Amount, Is.EqualTo(plan[index].Content.Reward.Amounts[amount].Amount));
                }
                Assert.That(decoded.AudienceMembershipIsSticky, Is.True, "a challenge that vanishes with the progress in it is worse than one never offered");
            }
        }

        #endregion
    }
}
