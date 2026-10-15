using Game.Logic;
using Game.Logic.Tests;
using Game.Server.CourseGhosts;
using NUnit.Framework;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Game.Server.Tests
{
    /// <summary>The house field a course's ghost pool is seeded with, driven over every course a run can be opened on.</summary>
    [TestFixture]
    public class TimeTrialHouseFieldTests
    {
        static SharedGameConfig _gameConfig;

        [OneTimeSetUp]
        public async Task SetUp()
        {
            _gameConfig = await TestGameConfig.LoadSharedAsync();
        }

        static IEnumerable<CourseDefinition> FirstRunCourses()
        {
            foreach (CourseId mapId in CourseComposer.LegalMapIds(_gameConfig))
                yield return CourseComposer.Compose(mapId, _gameConfig);
        }

        /// <summary>
        /// On every course a run can be opened on, each house drive finishes, none beats par, and each runs under
        /// a house name of its own that is not the first-run runner's.
        /// </summary>
        [Test]
        public void EveryHouseDriveFinishesEveryCourseUnderItsOwnName()
        {
            TimeTrialRules rules   = _gameConfig.TimeTrialRules;
            int            courses = 0;

            foreach (CourseDefinition course in FirstRunCourses())
            {
                TimeTrialHouseFieldResult field = TimeTrialHouseField.Drive(course, rules);

                Assert.That(field.Ghosts.Count, Is.EqualTo(TimeTrialHouseField.Drives.Count), $"{course.Id}: a house drive never reached the finish line");
                Assert.That(field.EverySeedLevelFinished, Is.True, $"{course.Id}: a seed level never reached the finish line");
                Assert.That(field.SyntheticTimesMs.Count, Is.EqualTo(TimeTrialHouseField.Drives.Count * TimeTrialHouseField.SyntheticsPerDrive));

                HashSet<string> names = new HashSet<string>();
                foreach (TimeTrialGhost ghost in field.Ghosts)
                {
                    Assert.That(names.Add(ghost.DisplayName), Is.True, $"{course.Id}: two house runners are both called {ghost.DisplayName}");
                    Assert.That(ghost.FinishTimeMs, Is.GreaterThanOrEqualTo(field.ParTimeMs), $"{course.Id}: {ghost.DisplayName} beat par");
                }
                Assert.That(names, Has.None.EqualTo(TimeTrialSeedRunners.NameFor(TimeTrialBotSkill.Clumsy)), $"{course.Id}: the first-run runner's name is in the house field");

                courses++;
            }

            TestContext.Out.WriteLine($"{courses} courses: every house drive finished");
        }
    }
}
