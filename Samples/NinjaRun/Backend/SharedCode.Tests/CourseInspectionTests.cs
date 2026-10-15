using Metaplay.Core.Math;
using NUnit.Framework;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Game.Logic.Tests
{
    /// <summary>
    /// What the developer course tools compute in the browser: the scripted runners' times over a course, its
    /// segment lengths, a part composed on its own, and the scripted field an offline developer race is run
    /// against. The page shows these as the truth about a course, so they are held to the same code paths the
    /// seeding, the config build and the server's fallback field run.
    /// </summary>
    [TestFixture]
    public class CourseInspectionTests
    {
        static SharedGameConfig _gameConfig;

        [OneTimeSetUp]
        public async Task SetUp()
        {
            _gameConfig = await TestGameConfig.LoadSharedAsync();
        }

        TimeTrialRules Rules => _gameConfig.TimeTrialRules;

        CourseDefinition SomeMap() => CourseResolver.TryResolve(CourseComposer.LegalMapIds(_gameConfig)[0], _gameConfig);

        [Test]
        public void RunnerTimes_ListTheRunWithNoInputAndEveryScriptedLevel_AsTheSeedsWouldTimeThem()
        {
            CourseDefinition map = SomeMap();

            List<CourseRunnerTime> times = CourseInspection.RunnerTimes(map, Rules);

            Assert.That(times, Has.Count.EqualTo(1 + CourseInspection.TimedSkills.Count));
            Assert.That(times[0].Skill, Is.Null, "the run with no input comes first");
            Assert.That(times[0].Result, Is.Not.Null, "a course must finish with no input");

            for (int ndx = 0; ndx < CourseInspection.TimedSkills.Count; ndx++)
            {
                TimeTrialBotSkill skill = CourseInspection.TimedSkills[ndx];
                CourseRunnerTime  time  = times[ndx + 1];
                TimeTrialGhost    seed  = TimeTrialGhost.CreateSeed(map, Rules, skill);

                Assert.That(time.Skill, Is.EqualTo(skill));
                Assert.That(time.Result, Is.Not.Null, $"{skill} did not finish");
                Assert.That(time.Result.FinishTimeMs, Is.EqualTo(seed.FinishTimeMs), $"{skill} is not timed the way its seed ghost is");
                Assert.That(time.Result.GateTimesMs, Is.EqualTo(seed.GateTimesMs), $"{skill}'s gate times are not the seed's");
                Assert.That(time.Result.ClimbEntryTimesMs, Is.EqualTo(seed.ClimbEntryTimesMs), $"{skill}'s climb times are not the seed's");
            }
        }

        /// <summary>
        /// The lengths name one segment per climb and one more, and every segment has length.
        /// </summary>
        [Test]
        public void SegmentLengths_NameEverySegment_EachWithLength()
        {
            // A map has climbs between its segments; a part composed alone is one segment with none.
            List<CourseDefinition> courses = new List<CourseDefinition> { SomeMap() };
            foreach (CoursePartInfo part in _gameConfig.CourseParts.Values)
                courses.Add(CourseInspection.PartAlone(part, _gameConfig.Global));

            foreach (CourseDefinition course in courses)
            {
                List<F32> lengths = CourseInspection.SegmentLengths(course);
                Assert.That(lengths, Has.Count.EqualTo(TimeTrialSegments.CountFor(course.Climbs.Count)), course.Id.Value);

                foreach (F32 length in lengths)
                    Assert.That(length > F32.Zero, Is.True, $"{course.Id}: a segment has no length");
            }
        }

        [Test]
        public void PartAlone_IsThePartOnItsOwn_WithTheLengthItHandsOverAt()
        {
            foreach ((CoursePartId id, CoursePartInfo part) in _gameConfig.CourseParts)
            {
                CourseDefinition alone = CourseInspection.PartAlone(part, _gameConfig.Global);

                Assert.That(alone, Is.Not.Null, $"{id} did not compose on its own");
                Assert.That(alone.Climbs, Is.Empty, $"{id}: a part alone has no climb");
                Assert.That(alone.FinishX - alone.StartX, Is.EqualTo(part.Length), $"{id}: alone, the part is not its own length");
                Assert.That(alone.DroopingBridges.Count, Is.EqualTo(part.Bridges.Count), $"{id}: its bridges did not come with it");
                Assert.That(CourseResolver.TryResolve(alone.Id, _gameConfig), Is.Null, $"{id}: a part alone is not a course a run can be opened on");
            }
        }

        [Test]
        public void PartAlone_IsNothingForAPartThatBreaksItsRules()
        {
            CoursePartInfo broken = new CoursePartInfo(
                CoursePartId.FromString("no-ground"),
                ground:        new List<GroundSegment>(),
                hazards:       new List<HazardSpan>(),
                positions:     new List<CoursePartPosition> { CoursePartPosition.First },
                isSetPiece: false);

            Assert.That(CourseInspection.PartAlone(broken, _gameConfig.Global), Is.Null);
        }

        /// <summary>
        /// The scripted field is the three seed runners, fastest expected first, the same field the server
        /// falls back to when a pool cannot be reached, and what an offline developer race is run against.
        /// </summary>
        [Test]
        public void TheSeedLineup_IsTheThreeScriptedRunnersFastestFirst()
        {
            CourseDefinition map = SomeMap();

            List<TimeTrialGhost> lineup = TimeTrialSeedRunners.Lineup(map, Rules);

            Assert.That(lineup, Has.Count.EqualTo(3));
            Assert.That(lineup[0].DisplayName, Is.EqualTo(TimeTrialSeedRunners.NameFor(TimeTrialBotSkill.Sharp)));
            Assert.That(lineup[1].DisplayName, Is.EqualTo(TimeTrialSeedRunners.NameFor(TimeTrialBotSkill.Competent)));
            Assert.That(lineup[2].DisplayName, Is.EqualTo(TimeTrialSeedRunners.NameFor(TimeTrialBotSkill.Cautious)));
            foreach (TimeTrialGhost ghost in lineup)
            {
                Assert.That(ghost.CourseId, Is.EqualTo(map.Id));
                Assert.That(ghost.Kind, Is.EqualTo(TimeTrialGhostKind.Seed));
            }
        }
    }
}
