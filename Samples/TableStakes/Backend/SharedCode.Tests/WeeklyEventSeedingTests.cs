using Metaplay.Core;
using Metaplay.Core.LiveOpsEvent;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.Logic.Tests
{
    /// <summary>
    /// Tests for <see cref="WeeklyEventSeeding"/>, the calendar that weekly events are created from
    /// (<c>docs/weekly-event.md</c>).
    /// <para>
    /// A second seeding pass must produce the same ids for the same weeks, so the SDK does not create a second
    /// copy. A change to the anchor, the week length or an id namespace would give every future week new ids and
    /// silently drop player progress, so the ids of two known weeks are pinned as literals.
    /// </para>
    /// </summary>
    [TestFixture]
    public class WeeklyEventSeedingTests
    {
        #region Fixture

        static readonly LiveOpsEventTemplateId Alpha = LiveOpsEventTemplateId.FromString("a-first");
        static readonly LiveOpsEventTemplateId Bravo = LiveOpsEventTemplateId.FromString("b-second");
        static readonly LiveOpsEventTemplateId Delta = LiveOpsEventTemplateId.FromString("d-third");

        static WeeklyEventTemplateInfo Template(LiveOpsEventTemplateId id, string theme, int target = 100) =>
            new WeeklyEventTemplateInfo(id, new WeeklyEventContent(
                theme:          theme,
                tagline:        "Every trick counts.",
                targetPoints:   target,
                winBonusPoints: 5,
                reward:         new RewardBundle(CurrencyAmount.Coins(2000), CurrencyAmount.Gems(100))));

        /// <summary>Two templates, like the shipped config, listed out of id order on purpose.</summary>
        static List<WeeklyEventTemplateInfo> TwoTemplates() =>
            new List<WeeklyEventTemplateInfo> { Template(Bravo, "High Suit"), Template(Alpha, "Trickster's Table") };

        static MetaTime At(int year, int month, int day, int hour = 12) =>
            MetaTime.FromDateTime(new DateTime(year, month, day, hour, 0, 0, DateTimeKind.Utc));

        #endregion

        #region The anchor and the identity a week is created under

        /// <summary>
        /// The anchor is a Monday at midnight UTC, so every week starts on Monday at midnight UTC. A week that
        /// started mid-week would look like a mistake on the LiveOps Dashboard timeline.
        /// </summary>
        [Test]
        public void WeekZeroOpensOnAMondayAtMidnightUtc()
        {
            DateTime anchor = WeeklyEventSeeding.WeekAnchor.ToDateTime();

            Assert.That(anchor.DayOfWeek, Is.EqualTo(DayOfWeek.Monday));
            Assert.That(anchor.TimeOfDay, Is.EqualTo(TimeSpan.Zero));
            Assert.That(anchor.Kind, Is.EqualTo(DateTimeKind.Utc));
        }

        /// <summary>
        /// <b>Stands in for a redeploy test.</b> A re-import offers the SDK these ids, and the SDK keeps a week it
        /// already has by matching them exactly. Changing the anchor, the week length or either namespace constant
        /// changes the ids of every future week. A week with new ids is a second event over the same days, and
        /// progress on the first event is left on ids that nothing reads.
        /// </summary>
        [Test]
        public void KnownWeeksHaveKnownIdsForever()
        {
            Assert.That(WeeklyEventSeeding.OccurrenceIdFor(0).ToString(), Is.EqualTo("03c8a66a14b4000-0-5745454b4c590001"), "week zero's occurrence id");
            Assert.That(WeeklyEventSeeding.SpecIdFor(0).ToString(),       Is.EqualTo("03c8a66a14b4000-0-5745454b4c590002"), "week zero's spec id");

            Assert.That(WeeklyEventSeeding.OccurrenceIdFor(100).ToString(), Is.EqualTo("03eb0769a744000-0-5745454b4c590001"), "week one hundred's occurrence id");
            Assert.That(WeeklyEventSeeding.SpecIdFor(100).ToString(),       Is.EqualTo("03eb0769a744000-0-5745454b4c590002"), "week one hundred's spec id");
        }

        /// <summary>
        /// Every week has unique ids, because the SDK's duplicate detection matches on ids. A week's occurrence id
        /// and spec id must also differ from each other: the SDK's import refuses a package whose spec id is
        /// already attached to a different occurrence. With one value for both, every week would collide with the
        /// previous week, and the import, which is all-or-nothing, would be refused.
        /// </summary>
        [Test]
        public void EveryWeekIsNamedDifferently()
        {
            HashSet<MetaGuid> ids = new HashSet<MetaGuid>();
            for (int week = 0; week < 260; week++)
            {
                Assert.That(ids.Add(WeeklyEventSeeding.OccurrenceIdFor(week)), Is.True, $"week {week}'s occurrence id repeats an earlier one");
                Assert.That(ids.Add(WeeklyEventSeeding.SpecIdFor(week)), Is.True, $"week {week}'s spec id repeats an earlier one");
            }
        }

        /// <summary>The time part of a week's id is the week's start, so ids sort by date.</summary>
        [Test]
        public void AnIdCarriesTheWeekItNames()
        {
            for (int week = 0; week < 60; week++)
                Assert.That(WeeklyEventSeeding.OccurrenceIdFor(week).GetDateTime(), Is.EqualTo(WeeklyEventSeeding.WeekStart(week).ToDateTime()), $"week {week}");
        }

        #endregion

        #region Which week an instant belongs to

        /// <summary>
        /// The anchor is the first instant of week zero. An instant before the anchor belongs to the week that
        /// contains it, not the week nearer zero. No deployment uses such instants, but truncating division would
        /// put week -1 and week 0 into one week index, and nothing else would catch it.
        /// </summary>
        [TestCase(0L,            0)]
        [TestCase(1L,            0)]
        [TestCase(-1L,           -1)]
        [TestCase(-604_800_000L, -1)]
        public void AnInstantBelongsToTheWeekThatContainsIt(long msFromAnchor, int expectedWeek)
        {
            Assert.That(WeeklyEventSeeding.WeekIndexAt(WeeklyEventSeeding.WeekAnchor + MetaDuration.FromMilliseconds(msFromAnchor)), Is.EqualTo(expectedWeek));
        }

        [Test]
        public void AWeekEndsExactlyWhereTheNextOneBegins()
        {
            MetaTime boundary = WeeklyEventSeeding.WeekStart(9);

            Assert.That(WeeklyEventSeeding.WeekEnd(8), Is.EqualTo(boundary), "no gap and no overlap between consecutive weeks");
            Assert.That(WeeklyEventSeeding.WeekIndexAt(boundary - MetaDuration.FromMilliseconds(1)), Is.EqualTo(8));
            Assert.That(WeeklyEventSeeding.WeekIndexAt(boundary), Is.EqualTo(9));
        }

        #endregion

        #region What a pass decides to create

        /// <summary>
        /// A plan starts at the current week, not the next one, so a newly created environment has a live event.
        /// The SDK fast-forwards an imported occurrence to the current time, so the current week starts in its
        /// active phase.
        /// </summary>
        [Test]
        public void APlanOpensWithTheWeekThatIsRunningNow()
        {
            MetaTime now = At(2026, 9, 3);

            List<WeeklyEventSeedWeek> plan = WeeklyEventSeeding.Plan(now, horizonWeeks: 8, TwoTemplates());

            Assert.That(plan, Has.Count.EqualTo(8));
            Assert.That(plan[0].WeekIndex, Is.EqualTo(WeeklyEventSeeding.WeekIndexAt(now)));
            Assert.That(plan[0].EnabledStartsAt, Is.LessThanOrEqualTo(now));
            Assert.That(plan[0].EnabledEndsAt, Is.GreaterThan(now));
        }

        /// <summary>
        /// Consecutive enabled windows have no gap and no overlap. A gap would show the empty state every week,
        /// and an overlap would let two events score the same finished game, which the model allows but the
        /// game must not (<c>docs/weekly-event.md</c>).
        /// </summary>
        [Test]
        public void ConsecutiveWeeksAbutExactly()
        {
            List<WeeklyEventSeedWeek> plan = WeeklyEventSeeding.Plan(At(2026, 9, 3), horizonWeeks: 12, TwoTemplates());

            for (int index = 1; index < plan.Count; index++)
            {
                Assert.That(plan[index].EnabledStartsAt, Is.EqualTo(plan[index - 1].EnabledEndsAt),
                    $"week {plan[index].WeekIndex} does not begin exactly where week {plan[index - 1].WeekIndex} ends");
            }

            foreach (WeeklyEventSeedWeek week in plan)
                Assert.That(week.EnabledEndsAt - week.EnabledStartsAt, Is.EqualTo(WeeklyEventSeeding.WeekLength), $"week {week.WeekIndex} is not a week long");
        }

        /// <summary>
        /// <b>Two passes in the same week plan the same weeks under the same ids.</b> This is what a redeploy
        /// within a week does: the second pass offers the SDK the ids the first pass created, and the SDK keeps
        /// what it has. No state from the first pass is needed, because the ids are derived from the calendar
        /// week.
        /// </summary>
        [Test]
        public void TwoPassesInsideOneWeekPlanTheSameWeeks()
        {
            List<WeeklyEventSeedWeek> first  = WeeklyEventSeeding.Plan(At(2026, 9, 2, hour: 3),  horizonWeeks: 8, TwoTemplates());
            List<WeeklyEventSeedWeek> second = WeeklyEventSeeding.Plan(At(2026, 9, 2, hour: 21), horizonWeeks: 8, TwoTemplates());

            Assert.That(second.Select(w => w.OccurrenceId), Is.EqualTo(first.Select(w => w.OccurrenceId)).AsCollection);
            Assert.That(second.Select(w => w.SpecId),       Is.EqualTo(first.Select(w => w.SpecId)).AsCollection);
            Assert.That(second.Select(w => w.TemplateId),   Is.EqualTo(first.Select(w => w.TemplateId)).AsCollection);
        }

        /// <summary>
        /// A pass a week later plans one new week and re-offers the others that are still in the horizon. Every
        /// pass creates the weeks that came into range since the last pass, and the SDK ignores the re-offered
        /// ones.
        /// </summary>
        [Test]
        public void APassAWeekLaterExtendsTheHorizonByOneWeek()
        {
            List<WeeklyEventSeedWeek> before = WeeklyEventSeeding.Plan(At(2026, 9, 2), horizonWeeks: 8, TwoTemplates());
            List<WeeklyEventSeedWeek> after  = WeeklyEventSeeding.Plan(At(2026, 9, 9), horizonWeeks: 8, TwoTemplates());

            HashSet<MetaGuid> already = before.Select(w => w.OccurrenceId).ToHashSet();
            List<MetaGuid>    fresh   = after.Where(w => !already.Contains(w.OccurrenceId)).Select(w => w.OccurrenceId).ToList();

            Assert.That(fresh, Has.Count.EqualTo(1), "a pass a week later should have exactly one week the earlier pass did not");
            Assert.That(after.Count(w => already.Contains(w.OccurrenceId)), Is.EqualTo(7), "and should re-offer the seven still inside the horizon");
            Assert.That(WeeklyEventSeeding.SeededThrough(after) - WeeklyEventSeeding.SeededThrough(before), Is.EqualTo(WeeklyEventSeeding.WeekLength));
        }

        /// <summary>
        /// <see cref="WeeklyEventSeeding.SeededThrough"/> is the end of the last planned week, which operators see as
        /// how far ahead events exist.
        /// </summary>
        [Test]
        public void TheHorizonIsWhereTheLastPlannedWeekStopsScoring()
        {
            List<WeeklyEventSeedWeek> plan = WeeklyEventSeeding.Plan(At(2026, 9, 3), horizonWeeks: 8, TwoTemplates());

            Assert.That(WeeklyEventSeeding.SeededThrough(plan), Is.EqualTo(plan[^1].EnabledEndsAt));
            Assert.That(WeeklyEventSeeding.SeededThrough(plan) - plan[0].EnabledStartsAt, Is.EqualTo(WeeklyEventSeeding.WeekLength * 8));
        }

        [Test]
        public void AHorizonIsClampedRatherThanTrusted()
        {
            Assert.That(WeeklyEventSeeding.Plan(At(2026, 9, 3), horizonWeeks: 0, TwoTemplates()), Has.Count.EqualTo(1), "a horizon of nothing still keeps the current week alive");
            Assert.That(WeeklyEventSeeding.Plan(At(2026, 9, 3), horizonWeeks: -5, TwoTemplates()), Has.Count.EqualTo(1));
            Assert.That(WeeklyEventSeeding.Plan(At(2026, 9, 3), horizonWeeks: 100000, TwoTemplates()), Has.Count.EqualTo(WeeklyEventSeeding.MaxHorizonWeeks));
        }

        /// <summary>
        /// With no templates there is nothing to create a week from, so the plan is empty. The player sees the
        /// empty state rather than an event with no theme.
        /// </summary>
        [Test]
        public void NoTemplatesPlansNoWeeks()
        {
            Assert.That(WeeklyEventSeeding.Plan(At(2026, 9, 3), horizonWeeks: 8, new List<WeeklyEventTemplateInfo>()), Is.Empty);
            Assert.That(WeeklyEventSeeding.Plan(At(2026, 9, 3), horizonWeeks: 8, null), Is.Empty);
        }

        #endregion

        #region The rotation

        /// <summary>
        /// The rotation uses sorted template id order, not library order, so the theme of a calendar week depends
        /// only on the content, not on the order in which a config build lists the library.
        /// </summary>
        [Test]
        public void TheRotationIsSortedByTemplateIdRatherThanByLibraryOrder()
        {
            List<WeeklyEventTemplateInfo> shuffled = new List<WeeklyEventTemplateInfo>
            {
                Template(Delta, "third"), Template(Bravo, "second"), Template(Alpha, "first"),
            };

            Assert.That(WeeklyEventSeeding.RotationOrder(shuffled).Select(t => t.TemplateId),
                Is.EqualTo(new[] { Alpha, Bravo, Delta }).AsCollection);
        }

        /// <summary>With two templates, consecutive weeks alternate between them.</summary>
        [Test]
        public void ConsecutiveWeeksAlternateThroughTheRotation()
        {
            List<WeeklyEventSeedWeek> plan = WeeklyEventSeeding.Plan(At(2026, 9, 3), horizonWeeks: 6, TwoTemplates());

            for (int index = 1; index < plan.Count; index++)
                Assert.That(plan[index].TemplateId, Is.Not.EqualTo(plan[index - 1].TemplateId), $"week {plan[index].WeekIndex} repeats the previous week's theme");

            Assert.That(plan[0].TemplateId, Is.EqualTo(plan[2].TemplateId), "a two-template rotation comes back round every second week");
        }

        [Test]
        public void ARotationOfOneTemplateRepeatsIt()
        {
            List<WeeklyEventSeedWeek> plan = WeeklyEventSeeding.Plan(At(2026, 9, 3), horizonWeeks: 4,
                new List<WeeklyEventTemplateInfo> { Template(Alpha, "only") });

            Assert.That(plan.Select(w => w.TemplateId).Distinct().Count(), Is.EqualTo(1));
            Assert.That(plan, Has.Count.EqualTo(4), "one template is still enough to keep every week filled");
        }

        /// <summary>A template with no content cannot be shown, so it is left out of the rotation.</summary>
        [Test]
        public void ATemplateWithNoContentIsNotRotatedInto()
        {
            List<WeeklyEventTemplateInfo> templates = new List<WeeklyEventTemplateInfo>
            {
                Template(Alpha, "real"), new WeeklyEventTemplateInfo(Bravo, content: null),
            };

            Assert.That(WeeklyEventSeeding.RotationOrder(templates).Select(t => t.TemplateId), Is.EqualTo(new[] { Alpha }).AsCollection);
        }

        #endregion

        #region The content a seeded week carries

        /// <summary>
        /// A seeded week's content is a <b>copy</b>. A config item is one object shared by every player on the
        /// server, and the SDK reports a mutated one, so passing the library's own instance to the creation path
        /// would risk editing the shared object.
        /// </summary>
        [Test]
        public void AWeeksContentIsACopyOfItsTemplatesRatherThanTheSameObject()
        {
            List<WeeklyEventTemplateInfo> templates = TwoTemplates();
            List<WeeklyEventSeedWeek>     plan      = WeeklyEventSeeding.Plan(At(2026, 9, 3), horizonWeeks: 4, templates);

            foreach (WeeklyEventSeedWeek week in plan)
            {
                WeeklyEventTemplateInfo source = templates.Single(t => t.TemplateId == week.TemplateId);

                Assert.That(week.Content, Is.Not.SameAs(source.Content), $"week {week.WeekIndex} carries the library's own content object");
                Assert.That(week.Content.Theme, Is.EqualTo(source.Content.Theme));
                Assert.That(week.Content.Tagline, Is.EqualTo(source.Content.Tagline));
                Assert.That(week.Content.TargetPoints, Is.EqualTo(source.Content.TargetPoints));
                Assert.That(week.Content.WinBonusPoints, Is.EqualTo(source.Content.WinBonusPoints));
                Assert.That(week.Content.Reward, Is.EqualTo(source.Content.Reward));
            }
        }

        /// <summary>
        /// Every planned week passes <see cref="WeeklyEventScoring.ProblemsWith"/>. The seeder runs the same check
        /// before the import, because the SDK's import validates only the event settings, not the game's content
        /// rules.
        /// </summary>
        [Test]
        public void EveryPlannedWeekPassesTheContentChecks()
        {
            List<WeeklyEventSeedWeek> plan = WeeklyEventSeeding.Plan(At(2026, 9, 3), horizonWeeks: 12, TwoTemplates());

            foreach (WeeklyEventSeedWeek week in plan)
                Assert.That(WeeklyEventScoring.ProblemsWith(week.Content), Is.Empty, $"week {week.WeekIndex} ({week.TemplateId})");
        }

        /// <summary>
        /// The preview and review windows make neighbouring weeks visible at the same time: next week's preview
        /// starts before this week ends, and this week's late-claim window runs into next week. The test checks the
        /// weeks a plan produces rather than comparing the constants, because the constants alone do not show
        /// what the schedule does with them.
        /// <para>
        /// Both overlaps are intended. Preview and review are not enabled phases, so a player who sees two weekly
        /// events still scores into only one, as <see cref="ConsecutiveWeeksAbutExactly"/> checks.
        /// </para>
        /// </summary>
        [Test]
        public void EachWeekIsVisibleAcrossItsNeighboursWhileOnlyOneOfThemScores()
        {
            List<WeeklyEventSeedWeek> plan = WeeklyEventSeeding.Plan(At(2026, 9, 3), horizonWeeks: 4, TwoTemplates());

            for (int index = 1; index < plan.Count; index++)
            {
                WeeklyEventSeedWeek earlier = plan[index - 1];
                WeeklyEventSeedWeek later   = plan[index];

                MetaTime laterPreviewStarts = later.EnabledStartsAt - WeeklyEventSeeding.PreviewLength;
                MetaTime earlierReviewEnds  = earlier.EnabledEndsAt + WeeklyEventSeeding.ReviewLength;

                Assert.That(laterPreviewStarts, Is.LessThan(earlier.EnabledEndsAt),
                    $"week {later.WeekIndex} does not become visible before week {earlier.WeekIndex} stops scoring, so the card is empty in between");
                Assert.That(laterPreviewStarts, Is.GreaterThan(earlier.EnabledStartsAt),
                    $"week {later.WeekIndex}'s preview reaches back past the start of week {earlier.WeekIndex}");

                Assert.That(earlierReviewEnds, Is.GreaterThan(later.EnabledStartsAt),
                    $"week {earlier.WeekIndex}'s late-claim window ends before week {later.WeekIndex} opens, so a reward earned at the death is gone the instant scoring closes");
                Assert.That(earlierReviewEnds, Is.LessThan(later.EnabledEndsAt),
                    $"week {earlier.WeekIndex}'s late-claim window outlives the whole of week {later.WeekIndex}");
            }

            // The ending-soon phase is the last part of the enabled window. A length of a week or more would keep
            // the event in its ending-soon phase for its whole run.
            Assert.That(WeeklyEventSeeding.EndingSoonLength, Is.GreaterThan(MetaDuration.Zero));
            Assert.That(WeeklyEventSeeding.EndingSoonLength, Is.LessThan(WeeklyEventSeeding.WeekLength));
        }

        #endregion
    }
}
