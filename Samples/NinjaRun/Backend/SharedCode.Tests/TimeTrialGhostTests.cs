using Metaplay.Core;
using Metaplay.Core.Math;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Game.Logic.Tests
{
    [TestFixture]
    public class TimeTrialGhostTests
    {
        static readonly MetaTime IssuedAt = MetaTime.FromDateTime(new DateTime(2026, 9, 21, 12, 0, 0, DateTimeKind.Utc));

        static readonly TimeTrialBotSkill[] AllSkills =
        {
            TimeTrialBotSkill.Cautious,
            TimeTrialBotSkill.Competent,
            TimeTrialBotSkill.Sharp,
        };

        static SharedGameConfig _gameConfig;

        [OneTimeSetUp]
        public async Task SetUp()
        {
            _gameConfig = await TestGameConfig.LoadSharedAsync();
        }

        /// <summary>
        /// Every course a first run can be opened on: every map the published parts compose, which is what the
        /// server picks a run's course from.
        /// </summary>
        static IEnumerable<CourseDefinition> FirstRunCourses()
        {
            foreach (CourseId mapId in CourseComposer.LegalMapIds(_gameConfig))
                yield return CourseComposer.Compose(mapId, _gameConfig);
        }

        /// <summary>
        /// On every course a first run can be opened on, the clumsy runner finishes behind the cautious runner,
        /// and on no segment is it ahead of the cautious runner by more than one simulation step.
        /// </summary>
        [Test]
        public void TheClumsySeedFinishesEveryCourseBehindTheCautiousOne()
        {
            TimeTrialRules rules       = _gameConfig.TimeTrialRules;
            int            toleranceMs = TimeTrialConstants.StepsToMilliseconds(1) + 1;
            int            courses     = 0;
            int            minGapMs    = int.MaxValue;
            int            maxGapMs    = int.MinValue;
            long           minPermille = long.MaxValue;
            long           maxPermille = long.MinValue;
            int            tiedSegments = 0;

            foreach (CourseDefinition course in FirstRunCourses())
            {
                TimeTrialGhost clumsy   = TimeTrialGhost.CreateSeed(course, rules, TimeTrialBotSkill.Clumsy);
                TimeTrialGhost cautious = TimeTrialGhost.CreateSeed(course, rules, TimeTrialBotSkill.Cautious);

                Assert.That(clumsy, Is.Not.Null, $"{course.Id}: the clumsy runner never reached the finish line");
                Assert.That(cautious, Is.Not.Null, $"{course.Id}: the cautious runner never reached the finish line");
                Assert.That(clumsy.DisplayName, Is.EqualTo(TimeTrialSeedRunners.NameFor(TimeTrialBotSkill.Clumsy)));
                Assert.That(clumsy.FinishTimeMs, Is.GreaterThan(cautious.FinishTimeMs), $"{course.Id}: the clumsy runner was not slower than the cautious one");

                int segmentCount = TimeTrialSegments.CountFor(course.Climbs.Count);
                for (int segment = 0; segment < segmentCount; segment++)
                {
                    int clumsyMs   = TimeTrialSegments.TimeMs(clumsy.GateTimesMs, clumsy.ClimbEntryTimesMs, clumsy.FinishTimeMs, segment);
                    int cautiousMs = TimeTrialSegments.TimeMs(cautious.GateTimesMs, cautious.ClimbEntryTimesMs, cautious.FinishTimeMs, segment);
                    Assert.That(clumsyMs, Is.GreaterThanOrEqualTo(cautiousMs - toleranceMs),
                        $"{course.Id}: the clumsy runner was ahead of the cautious one on segment {segment + 1}");
                    if (clumsyMs <= cautiousMs + toleranceMs)
                        tiedSegments++;
                }

                int  gapMs    = clumsy.FinishTimeMs - cautious.FinishTimeMs;
                long permille = (long)clumsy.FinishTimeMs * 1000 / cautious.FinishTimeMs;
                courses++;
                minGapMs    = Math.Min(minGapMs, gapMs);
                maxGapMs    = Math.Max(maxGapMs, gapMs);
                minPermille = Math.Min(minPermille, permille);
                maxPermille = Math.Max(maxPermille, permille);
            }

            TestContext.Out.WriteLine(
                $"{courses} courses: clumsy behind cautious by {minGapMs}–{maxGapMs} ms ({minPermille}–{maxPermille} permille of the cautious time); " +
                $"{tiedSegments} segments level within one step");
        }

        [Test]
        public void SeedGhostsAreDeterministicAndPlayableForTheirTicket()
        {
            CourseDefinition course = TestCourses.PracticeMap;
            TimeTrialRules   rules  = new TimeTrialRules();
            TimeTrialTicket  ticket = new TimeTrialTicket(1, course.Id, TimeTrialConstants.RulesetVersion, IssuedAt);

            foreach (TimeTrialBotSkill skill in AllSkills)
            {
                TimeTrialGhost first  = TimeTrialGhost.CreateSeed(course, rules, skill);
                TimeTrialGhost second = TimeTrialGhost.CreateSeed(course, rules, skill);

                Assert.That(first, Is.Not.Null, $"{skill} did not finish {course.Id}");
                Assert.That(first.IsPlayableFor(ticket, course, rules), Is.True);
                Assert.That(first.Kind, Is.EqualTo(TimeTrialGhostKind.Seed));
                Assert.That(first.DisplayName, Is.Not.Null.And.Not.Empty);
                Assert.That(first.FinishTimeMs, Is.EqualTo(second.FinishTimeMs));
                Assert.That(first.Recording.Inputs.Count, Is.EqualTo(second.Recording.Inputs.Count));

                for (int ndx = 0; ndx < first.Recording.Inputs.Count; ndx++)
                {
                    Assert.That(first.Recording.Inputs[ndx].Step, Is.EqualTo(second.Recording.Inputs[ndx].Step));
                    Assert.That(first.Recording.Inputs[ndx].Pressed, Is.EqualTo(second.Recording.Inputs[ndx].Pressed));
                }
            }
        }

        /// <summary>
        /// On every course a run can be opened on, every seed level finishes, sharp ahead of competent ahead of
        /// cautious.
        /// </summary>
        [Test]
        public void EverySeedLevelFinishesEveryCourseInSkillOrder()
        {
            TimeTrialRules rules = new TimeTrialRules();

            foreach (CourseDefinition course in FirstRunCourses())
            {
                TimeTrialGhost cautious  = TimeTrialGhost.CreateSeed(course, rules, TimeTrialBotSkill.Cautious);
                TimeTrialGhost competent = TimeTrialGhost.CreateSeed(course, rules, TimeTrialBotSkill.Competent);
                TimeTrialGhost sharp     = TimeTrialGhost.CreateSeed(course, rules, TimeTrialBotSkill.Sharp);

                Assert.That(cautious,  Is.Not.Null, $"{course.Id}: the cautious runner never reached the finish line");
                Assert.That(competent, Is.Not.Null, $"{course.Id}: the competent runner never reached the finish line");
                Assert.That(sharp,     Is.Not.Null, $"{course.Id}: the sharp runner never reached the finish line");

                Assert.That(sharp.FinishTimeMs, Is.LessThan(competent.FinishTimeMs), $"{course.Id}: the sharp runner was not faster than the competent one");
                Assert.That(competent.FinishTimeMs, Is.LessThan(cautious.FinishTimeMs), $"{course.Id}: the competent runner was not faster than the cautious one");
            }
        }

        [Test]
        public void GhostMustMatchTheTicketsCourseAndRuleset()
        {
            CourseDefinition course = TestCourses.PracticeMap;
            TimeTrialRules rules = new TimeTrialRules();
            TimeTrialGhost ghost = TimeTrialGhost.CreateSeed(course, rules, TimeTrialBotSkill.Competent);

            TimeTrialTicket wrongCourse = new TimeTrialTicket(1, TestCourses.OtherMap.Id, TimeTrialConstants.RulesetVersion, IssuedAt);
            TimeTrialTicket wrongRules  = new TimeTrialTicket(1, course.Id, TimeTrialConstants.RulesetVersion + 1, IssuedAt);

            Assert.That(ghost.IsPlayableFor(wrongCourse, course, rules), Is.False);
            Assert.That(ghost.IsPlayableFor(wrongRules, course, rules), Is.False);
        }

        [Test]
        public void MalformedOrNonFinishingGhostRecordingIsNotPlayable()
        {
            CourseDefinition course = TestCourses.PracticeMap;
            TimeTrialRules rules = new TimeTrialRules();
            TimeTrialTicket ticket = new TimeTrialTicket(1, course.Id, TimeTrialConstants.RulesetVersion, IssuedAt);

            TimeTrialGhost malformed = new TimeTrialGhost(
                course.Id,
                TimeTrialConstants.RulesetVersion,
                new TimeTrialRecording(new List<TimeTrialInput>
                {
                    new TimeTrialInput(20, pressed: true),
                    new TimeTrialInput(10, pressed: false),
                }),
                "Malformed",
                29_000,
                TimeTrialGhostKind.Player);
            CourseDefinition overlongCourse = new CourseDefinition(
                CourseId.FromString("test-overlong-ghost"), "Overlong",
                F32.Zero, F32.FromInt(10_000),
                new List<GroundSegment> { new GroundSegment(F32.FromInt(-10), F32.FromInt(20_000), F32.Zero) },
                new List<HazardSpan>());
            TimeTrialTicket overlongTicket = new TimeTrialTicket(
                2, overlongCourse.Id, TimeTrialConstants.RulesetVersion, IssuedAt);
            TimeTrialGhost nonFinishing = new TimeTrialGhost(
                overlongCourse.Id,
                TimeTrialConstants.RulesetVersion,
                new TimeTrialRecording(),
                "Never finishes",
                0,
                TimeTrialGhostKind.Player);

            Assert.That(malformed.IsPlayableFor(ticket, course, rules), Is.False);
            Assert.That(nonFinishing.IsPlayableFor(overlongTicket, overlongCourse, rules), Is.False);
        }
    }
}
