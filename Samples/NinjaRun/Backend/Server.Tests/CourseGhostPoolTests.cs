using Game.Logic;
using Game.Logic.Tests;
using Game.Server.CourseGhosts;
using Metaplay.Core;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.Server.Tests
{
    /// <summary>
    /// The course-scoped opponent pool: what it keeps, what it throws away, who it puts in front of a player,
    /// and where it says a time stands, driven through the state the course's ghost pool entity hosts.
    /// </summary>
    [TestFixture]
    public class CourseGhostPoolTests
    {
        static readonly MetaTime Now = MetaTime.FromDateTime(new DateTime(2026, 9, 21, 12, 0, 0, DateTimeKind.Utc));

        static readonly TimeTrialRules Rules = new TimeTrialRules();

        static readonly CourseDefinition Course = TestCourses.PracticeMap;

        static EntityId Player(int index) => EntityId.Create(EntityKindCore.Player, (ulong)index);

        static CourseGhostPoolState SeededPool()
        {
            CourseGhostPoolState pool = new CourseGhostPoolState();
            Assert.That(pool.Seed(Course, Rules), Is.True);
            return pool;
        }

        /// <summary>A recording the simulation accepts, so entries the pool keeps are ones it could hand out.</summary>
        static TimeTrialRecording ValidRecording() => TimeTrialBotDriver.Drive(Course, Rules, TimeTrialBotSkill.Competent);

        /// <summary>
        /// The torii times that recording produces. The server has them from its own replay, so an entry the
        /// pool keeps always carries both lists and a segment can be placed against it without replaying it
        /// again.
        /// </summary>
        static TimeTrialResult ValidResult() => TimeTrialSimulation.Replay(Course, Rules, ValidRecording());

        static List<int> ValidGateTimes() => ValidResult().GateTimesMs;

        static List<int> ValidClimbEntryTimes() => ValidResult().ClimbEntryTimesMs;

        [Test]
        public void SeedingTakesParAndFirstOpponentsFromTheScriptedRunners()
        {
            CourseGhostPoolState pool = SeededPool();

            Assert.That(pool.IsSeeded, Is.True);
            Assert.That(pool.CourseId, Is.EqualTo(Course.Id));
            Assert.That(pool.RulesetVersion, Is.EqualTo(TimeTrialConstants.RulesetVersion));
            Assert.That(pool.EntryCount, Is.EqualTo(TimeTrialHouseField.Drives.Count), "a fresh pool holds exactly the house field's scripted runners");

            TimeTrialGhost sharp = TimeTrialGhost.CreateSeed(Course, Rules, TimeTrialBotSkill.Sharp);
            Assert.That(pool.ParTimeMs, Is.EqualTo(sharp.FinishTimeMs), "par is the sharp runner's time");
        }

        [Test]
        public void AnUnseededPoolKeepsNothing()
        {
            CourseGhostPoolState pool = new CourseGhostPoolState();

            pool.AcceptRun(Player(1), "Runner", 30_000, isNewBest: true, previousBestMs: 0, ValidRecording(), ValidGateTimes(), ValidClimbEntryTimes(), Now);

            Assert.That(pool.IsSeeded, Is.False);
            Assert.That(pool.EntryCount, Is.Zero);
            Assert.That(pool.Distribution.RunCount, Is.Zero);
            Assert.That(pool.BandFor(30_000), Is.EqualTo(-1));
        }

        [Test]
        public void BandsFollowParRatherThanAbsoluteTime()
        {
            CourseGhostPoolState pool = SeededPool();
            int par = pool.ParTimeMs;

            Assert.That(pool.BandFor(par - 1), Is.EqualTo(0), "a time under par is in the fastest band");
            Assert.That(pool.BandFor(par * 2), Is.EqualTo(CourseGhostPoolState.BandCount - 1), "anything far off par lands in the last band");
        }

        [Test]
        public void AnAcceptedRunIsCountedAndItsBestIsKeptAsAnOpponent()
        {
            CourseGhostPoolState pool = SeededPool();
            int par = pool.ParTimeMs;
            int seeds = pool.EntryCount;
            int house = pool.Distribution.HouseCount;

            pool.AcceptRun(Player(1), "First", par * 1020 / 1000, isNewBest: true, previousBestMs: 0, ValidRecording(), ValidGateTimes(), ValidClimbEntryTimes(), Now);

            Assert.That(pool.Distribution.RunCount, Is.EqualTo(1));
            Assert.That(pool.Distribution.PlayerCount, Is.EqualTo(house + 1));
            Assert.That(pool.EntryCount, Is.EqualTo(seeds + 1), "the run joined the scripted runners");
        }

        [Test]
        public void ARunThatIsNotAPersonalBestIsCountedButNotKept()
        {
            CourseGhostPoolState pool = SeededPool();
            int par = pool.ParTimeMs;
            int seeds = pool.EntryCount;
            int house = pool.Distribution.HouseCount;

            pool.AcceptRun(Player(1), "First", par * 1020 / 1000, isNewBest: true, previousBestMs: 0, ValidRecording(), ValidGateTimes(), ValidClimbEntryTimes(), Now);
            pool.AcceptRun(Player(1), "First", par * 1080 / 1000, isNewBest: false, previousBestMs: par * 1020 / 1000, ValidRecording(), ValidGateTimes(), ValidClimbEntryTimes(), Now);

            Assert.That(pool.Distribution.RunCount, Is.EqualTo(2), "both runs count towards the course's spread of times");
            Assert.That(pool.Distribution.PlayerCount, Is.EqualTo(house + 1), "the player is still one player");
            Assert.That(pool.EntryCount, Is.EqualTo(seeds + 1), "the slower run did not take a second slot");
        }

        [Test]
        public void APlayerImprovingMovesRatherThanAccumulates()
        {
            CourseGhostPoolState pool = SeededPool();
            int par = pool.ParTimeMs;

            pool.AcceptRun(Player(1), "First", par * 1080 / 1000, isNewBest: true, previousBestMs: 0, ValidRecording(), ValidGateTimes(), ValidClimbEntryTimes(), Now);
            int afterFirst = pool.EntryCount;

            pool.AcceptRun(Player(1), "First", par * 1010 / 1000, isNewBest: true, previousBestMs: par * 1080 / 1000, ValidRecording(), ValidGateTimes(), ValidClimbEntryTimes(), Now);

            Assert.That(pool.EntryCount, Is.EqualTo(afterFirst), "the player holds one slot, not one per improvement");
            Assert.That(pool.Distribution.PlayerCount, Is.EqualTo(pool.Distribution.HouseCount + 1), "and is counted once in the player histogram");
            Assert.That(pool.Distribution.RunCount, Is.EqualTo(2));
        }

        [Test]
        public void ThePoolStaysBoundedHoweverManyRunsArrive()
        {
            CourseGhostPoolState pool = SeededPool();
            int par = pool.ParTimeMs;
            TimeTrialRecording recording = ValidRecording();
            List<int> gateTimes = ValidGateTimes();
            List<int> climbEntryTimes = ValidClimbEntryTimes();

            for (int ndx = 0; ndx < 500; ndx++)
            {
                int time = par * (1000 + ndx % 120) / 1000;
                pool.AcceptRun(Player(ndx + 1), $"Runner {ndx}", time, isNewBest: true, previousBestMs: 0, recording, gateTimes, climbEntryTimes, Now);
            }

            Assert.That(pool.EntryCount, Is.LessThanOrEqualTo(CourseGhostPoolState.BandCount * CourseGhostPoolState.SlotsPerBand));
            Assert.That(pool.Distribution.RunCount, Is.EqualTo(500), "every run still counts towards the distribution");
            Assert.That(pool.Distribution.PlayerCount, Is.EqualTo(pool.Distribution.HouseCount + 500));
        }

        [Test]
        public void ValidatedRunsDisplaceTheScriptedRunnersFirst()
        {
            CourseGhostPoolState pool = SeededPool();
            int par = pool.ParTimeMs;
            TimeTrialRecording recording = ValidRecording();
            List<int> gateTimes = ValidGateTimes();
            List<int> climbEntryTimes = ValidClimbEntryTimes();

            // Fill the band the sharp runner sits in with validated runs at the same pace.
            for (int ndx = 0; ndx < CourseGhostPoolState.SlotsPerBand * 2; ndx++)
                pool.AcceptRun(Player(ndx + 1), $"Runner {ndx}", par, isNewBest: true, previousBestMs: 0, recording, gateTimes, climbEntryTimes, Now);

            List<TimeTrialGhost> lineup = pool.SelectLineup(Player(999), par, maxGhosts: 3, rng: null);

            Assert.That(lineup, Is.Not.Empty);
            Assert.That(lineup[0].Kind, Is.Not.EqualTo(TimeTrialGhostKind.Seed), "a band full of players should no longer field a scripted runner");
        }

        [Test]
        public void ANewPlayerIsPacedAgainstTheScriptedRunners()
        {
            CourseGhostPoolState pool = SeededPool();

            List<TimeTrialGhost> lineup = pool.SelectLineup(Player(1), playerBestMs: 0, maxGhosts: 3, rng: null);

            Assert.That(lineup.Count, Is.EqualTo(3), "a fresh course fields every scripted runner it has, and no more");
            foreach (TimeTrialGhost ghost in lineup)
            {
                Assert.That(ghost.Kind, Is.EqualTo(TimeTrialGhostKind.Seed));
                Assert.That(ghost.CourseId, Is.EqualTo(Course.Id));
                Assert.That(ghost.RulesetVersion, Is.EqualTo(TimeTrialConstants.RulesetVersion));
                Assert.That(ghost.FinishTimeMs, Is.GreaterThan(0));
            }

            Assert.That(lineup.Select(ghost => ghost.DisplayName).Distinct().Count(), Is.EqualTo(lineup.Count),
                "the same opponent must not fill more than one slot");
        }

        /// <summary>
        /// A full lineup is a ladder around the player's own pace: someone they are already beating, someone
        /// just up the road and someone clearly up it. That is what makes a field of four readable rather than
        /// a pack all in one place, and it is what puts a run at the player's own pace third.
        /// </summary>
        [Test]
        public void AFullLineupLaddersAroundThePlayersPace()
        {
            CourseGhostPoolState pool = SeededPool();
            int par = pool.ParTimeMs;
            TimeTrialRecording recording = ValidRecording();
            List<int> gateTimes = ValidGateTimes();
            List<int> climbEntryTimes = ValidClimbEntryTimes();

            for (int ndx = 0; ndx < 30; ndx++)
                pool.AcceptRun(Player(ndx + 1), $"Runner {ndx}", par * (1000 + ndx * 4) / 1000, isNewBest: true, previousBestMs: 0, recording, gateTimes, climbEntryTimes, Now);

            int playerBest = par * 1060 / 1000;
            List<TimeTrialGhost> lineup = pool.SelectLineup(Player(500), playerBest, maxGhosts: 3, rng: null);

            Assert.That(lineup.Count, Is.EqualTo(3));
            Assert.That(lineup.Select(ghost => ghost.FinishTimeMs).Distinct().Count(), Is.EqualTo(3), "every slot is a different runner");

            for (int ndx = 1; ndx < lineup.Count; ndx++)
                Assert.That(lineup[ndx].FinishTimeMs, Is.GreaterThan(lineup[ndx - 1].FinishTimeMs), "the lineup is ordered fastest first");

            Assert.That(lineup[0].FinishTimeMs, Is.LessThan(playerBest), "nobody in the lineup is worth chasing");
            Assert.That(lineup[lineup.Count - 1].FinishTimeMs, Is.GreaterThan(playerBest), "the lineup has nobody the player is already beating");
        }

        [Test]
        public void TheLineupIsPacedOnThePlayersOwnTimeAndNeverIsThePlayer()
        {
            CourseGhostPoolState pool = SeededPool();
            int par = pool.ParTimeMs;
            TimeTrialRecording recording = ValidRecording();
            List<int> gateTimes = ValidGateTimes();
            List<int> climbEntryTimes = ValidClimbEntryTimes();

            for (int ndx = 0; ndx < 12; ndx++)
                pool.AcceptRun(Player(ndx + 1), $"Runner {ndx}", par * (1000 + ndx * 10) / 1000, isNewBest: true, previousBestMs: 0, recording, gateTimes, climbEntryTimes, Now);

            int playerBest = par * 1100 / 1000;
            pool.AcceptRun(Player(100), "Me", playerBest, isNewBest: true, previousBestMs: 0, recording, gateTimes, climbEntryTimes, Now);

            List<TimeTrialGhost> lineup = pool.SelectLineup(Player(100), playerBest, maxGhosts: 3, rng: null);

            Assert.That(lineup, Is.Not.Empty);
            foreach (TimeTrialGhost ghost in lineup)
                Assert.That(ghost.DisplayName, Is.Not.EqualTo("Me"), "a player must not be handed their own recording as an opponent");

            Assert.That(lineup[0].FinishTimeMs, Is.LessThan(playerBest), "the lineup opens with a runner ahead of the player's own pace");
        }

        /// <summary>
        /// Par is the sharpest scripted runner at the base rules, and a player with a Skill equipped can beat
        /// it. The fastest band is open at the fast end for exactly that reason, so a run under par is kept and
        /// handed out rather than falling off the bottom of the ladder.
        /// </summary>
        [Test]
        public void ARunFasterThanParIsKeptWithTheSkillItWasRunWith()
        {
            CourseGhostPoolState pool = SeededPool();
            int     par     = pool.ParTimeMs;
            SkillId skillId = SkillId.FromString("wind-stride");
            int     seeds   = pool.EntryCount;

            Assert.That(pool.BandFor(par * 900 / 1000), Is.Zero, "a run well under par belongs in the fastest band");

            pool.AcceptRun(Player(1), "River Hawk", par * 900 / 1000, isNewBest: true, previousBestMs: 0, ValidRecording(), ValidGateTimes(), ValidClimbEntryTimes(), Now, skillId);

            Assert.That(pool.EntryCount, Is.EqualTo(seeds + 1), "the run joined the scripted runners");

            List<TimeTrialGhost> lineup = pool.SelectLineup(Player(999), par, maxGhosts: 3, rng: null);
            TimeTrialGhost       fastest = lineup[0];

            Assert.That(fastest.DisplayName, Is.EqualTo("River Hawk"));
            Assert.That(fastest.FinishTimeMs, Is.LessThan(par), "the fastest opponent in the field beat par");
            Assert.That(fastest.SkillId, Is.EqualTo(skillId), "an opponent has to carry the Skill it ran with, or it cannot be replayed as it ran");
        }

        /// <summary>
        /// A segment ends at a climb's bottom torii and the next starts at the top one, so an entry is only
        /// placeable if it carries both lists. The pool keeps both on everything it stores (seeded and
        /// validated alike) and hands both out with the opponent.
        /// </summary>
        [Test]
        public void EveryEntryCarriesBothToriiTimesAndHandsThemOut()
        {
            CourseGhostPoolState pool = SeededPool();
            int                  par  = pool.ParTimeMs;

            TimeTrialResult run = ValidResult();
            pool.AcceptRun(Player(1), "River Hawk", par * 900 / 1000, isNewBest: true, previousBestMs: 0, ValidRecording(), run.GateTimesMs, run.ClimbEntryTimesMs, Now);

            List<TimeTrialGhost> lineup = pool.SelectLineup(Player(999), par, maxGhosts: 3, rng: null);
            Assert.That(lineup, Is.Not.Empty);

            int segmentCount = TimeTrialSegments.CountFor(Course.Climbs.Count);
            foreach (TimeTrialGhost ghost in lineup)
            {
                Assert.That(TimeTrialSegments.Covers(ghost.GateTimesMs, ghost.ClimbEntryTimesMs, segmentCount), Is.True,
                    $"{ghost.DisplayName} cannot have a segment placed against it");

                for (int climb = 0; climb < Course.Climbs.Count; climb++)
                {
                    Assert.That(ghost.ClimbEntryTimesMs[climb], Is.LessThan(ghost.GateTimesMs[climb]),
                        $"{ghost.DisplayName} reached climb {climb + 1}'s bottom torii after its gate");
                }
            }

            TimeTrialGhost fastest = lineup[0];
            Assert.That(fastest.DisplayName, Is.EqualTo("River Hawk"));
            Assert.That(fastest.ClimbEntryTimesMs, Is.EqualTo(run.ClimbEntryTimesMs), "the run's own bottom torii times came back unchanged");
            Assert.That(fastest.GateTimesMs, Is.EqualTo(run.GateTimesMs), "and so did its gate times");
        }

        [Test]
        public void AScriptedRunnerCarriesNoSkill()
        {
            CourseGhostPoolState pool = SeededPool();

            foreach (TimeTrialGhost ghost in pool.SelectLineup(Player(1), playerBestMs: 0, maxGhosts: 3, rng: null))
                Assert.That(ghost.SkillId, Is.Null, "par is measured at the base rules, so a seed must run with no Skill");
        }

        [Test]
        public void StandingCarriesACopyOfTheField()
        {
            CourseGhostPoolState pool = SeededPool();
            int par = pool.ParTimeMs;
            TimeTrialRecording recording = ValidRecording();
            List<int> gateTimes = ValidGateTimes();
            List<int> climbEntryTimes = ValidClimbEntryTimes();

            for (int ndx = 0; ndx < 20; ndx++)
                pool.AcceptRun(Player(ndx + 1), $"Runner {ndx}", par * (1000 + ndx * 5) / 1000, isNewBest: true, previousBestMs: 0, recording, gateTimes, climbEntryTimes, Now);

            TimeTrialStanding first = pool.StandingFor(par);

            Assert.That(first.PlayerBestCounts, Is.Not.Null);
            Assert.That(first.ParTimeMs, Is.EqualTo(par));
            Assert.That(first.PlayerBestCounts.Length, Is.EqualTo(CourseTimeBuckets.BucketCount));
            Assert.That(first.PlayerBestCounts.Sum(), Is.EqualTo(pool.Distribution.PlayerCount), "one entry per player at their best");
            Assert.That(first.PlayerBestCounts, Is.Not.SameAs(pool.Distribution.PlayerBestCounts), "the pool's own counts are never handed out");

            first.PlayerBestCounts[3] += 100;
            TimeTrialStanding second = pool.StandingFor(par);

            Assert.That(second.PlayerBestCounts.Sum(), Is.EqualTo(pool.Distribution.PlayerCount), "changing a standing's counts leaves the pool's alone");
        }

        /// <summary>Every real drive that finishes on the test course, and its synthetics.</summary>
        static int ExpectedHouseCount()
        {
            TimeTrialHouseFieldResult field = TimeTrialHouseField.Drive(Course, Rules);
            return field.Ghosts.Count * (1 + TimeTrialHouseField.SyntheticsPerDrive);
        }

        [Test]
        public void AFreshPoolHoldsItsHouseEntriesAsBestTimes()
        {
            CourseGhostPoolState      pool  = SeededPool();
            TimeTrialHouseFieldResult field = TimeTrialHouseField.Drive(Course, Rules);

            Assert.That(field.Ghosts.Count, Is.EqualTo(TimeTrialHouseField.Drives.Count), "every house drive finishes the test course");
            Assert.That(pool.Distribution.HouseCount, Is.EqualTo(ExpectedHouseCount()));
            Assert.That(pool.Distribution.PlayerCount, Is.EqualTo(pool.Distribution.HouseCount), "the house entries are counted as best times");
            Assert.That(pool.Distribution.PlayerBestCounts.Sum(), Is.EqualTo(pool.Distribution.PlayerCount));
            Assert.That(pool.Distribution.RunCount, Is.Zero, "they are not accepted runs");
            Assert.That(pool.Distribution.RunCounts.Sum(), Is.Zero);
            Assert.That(pool.EntryCount, Is.EqualTo(field.Ghosts.Count), "every real drive is kept as an opponent");
        }

        /// <summary>
        /// A synthetic time is its run plus one or two botched jumps, each a small share of par, so it is always
        /// slower than the run it hangs off and never by more than the botches can cost.
        /// </summary>
        [Test]
        public void EverySyntheticIsSlowerThanItsRun()
        {
            TimeTrialHouseFieldResult field = TimeTrialHouseField.Drive(Course, Rules);
            int par = field.ParTimeMs;

            Assert.That(field.SyntheticTimesMs.Count, Is.EqualTo(field.Ghosts.Count * TimeTrialHouseField.SyntheticsPerDrive));

            int maxCostMs = par * TimeTrialHouseField.BotchMaxPermille * TimeTrialHouseField.MaxBotchesPerSynthetic / 1000;
            int minCostMs = par * TimeTrialHouseField.BotchMinPermille / 1000;
            for (int ghost = 0; ghost < field.Ghosts.Count; ghost++)
            {
                int anchorMs = field.Ghosts[ghost].FinishTimeMs;
                for (int ndx = 0; ndx < TimeTrialHouseField.SyntheticsPerDrive; ndx++)
                {
                    int timeMs = field.SyntheticTimesMs[ghost * TimeTrialHouseField.SyntheticsPerDrive + ndx];
                    Assert.That(timeMs, Is.GreaterThan(anchorMs), $"a synthetic off {field.Ghosts[ghost].DisplayName} is not slower than it");
                    Assert.That(timeMs - anchorMs, Is.InRange(minCostMs, maxCostMs), $"a synthetic off {field.Ghosts[ghost].DisplayName} is off by more than its botches");
                }
            }
        }

        [Test]
        public void TheHouseFieldIsTheSameEveryTimeItIsDriven()
        {
            TimeTrialHouseFieldResult first  = TimeTrialHouseField.Drive(Course, Rules);
            TimeTrialHouseFieldResult second = TimeTrialHouseField.Drive(Course, Rules);

            Assert.That(second.SyntheticTimesMs, Is.EqualTo(first.SyntheticTimesMs));
            Assert.That(second.Ghosts.Select(ghost => ghost.FinishTimeMs), Is.EqualTo(first.Ghosts.Select(ghost => ghost.FinishTimeMs)));
            Assert.That(second.Ghosts.Select(ghost => ghost.DisplayName), Is.EqualTo(first.Ghosts.Select(ghost => ghost.DisplayName)));

            int[] once  = SeededPool().Distribution.PlayerBestCounts;
            int[] again = SeededPool().Distribution.PlayerBestCounts;
            Assert.That(again, Is.EqualTo(once), "two seedings count the same field");
        }

        /// <summary>
        /// Variant 0 of each level is that level's seed ghost exactly: the same recording, times and name the
        /// level drives on its own.
        /// </summary>
        [Test]
        public void TheFirstRunOfEachLevelIsItsSeedGhost()
        {
            TimeTrialHouseFieldResult field = TimeTrialHouseField.Drive(Course, Rules);

            foreach (TimeTrialBotSkill skill in new[] { TimeTrialBotSkill.Sharp, TimeTrialBotSkill.Competent, TimeTrialBotSkill.Cautious })
            {
                TimeTrialGhost seed  = TimeTrialGhost.CreateSeed(Course, Rules, skill);
                TimeTrialGhost house = field.Ghosts.Single(ghost => ghost.DisplayName == TimeTrialSeedRunners.NameFor(skill));

                Assert.That(house.FinishTimeMs, Is.EqualTo(seed.FinishTimeMs), $"{skill}'s house run is not its seed");
                Assert.That(house.Recording.Inputs.Select(input => (input.Step, input.Pressed)),
                    Is.EqualTo(seed.Recording.Inputs.Select(input => (input.Step, input.Pressed))), $"{skill}'s house recording is not its seed's");
                Assert.That(house.GateTimesMs, Is.EqualTo(seed.GateTimesMs));
                Assert.That(house.ClimbEntryTimesMs, Is.EqualTo(seed.ClimbEntryTimesMs));
                Assert.That(TimeTrialSeedRunners.NameFor(skill, 0), Is.EqualTo(TimeTrialSeedRunners.NameFor(skill)));
            }
        }

        [Test]
        public void APlayerRunJoinsTheHouseEntries()
        {
            CourseGhostPoolState pool = SeededPool();
            int par   = pool.ParTimeMs;
            int house = pool.Distribution.HouseCount;

            pool.AcceptRun(Player(1), "First", par * 1020 / 1000, isNewBest: true, previousBestMs: 0, ValidRecording(), ValidGateTimes(), ValidClimbEntryTimes(), Now);

            Assert.That(pool.Distribution.HouseCount, Is.EqualTo(house), "a player's run is not a house entry");
            Assert.That(pool.Distribution.PlayerCount, Is.EqualTo(house + 1));
            Assert.That(pool.StandingFor(par * 1020 / 1000).PlayerBestCounts.Sum(), Is.EqualTo(house + 1));
        }

        [Test]
        public void AnUnseededPoolGivesNoField()
        {
            TimeTrialStanding standing = new CourseGhostPoolState().StandingFor(30_000);

            Assert.That(standing.PlayerBestCounts, Is.Null, "with no par there are no buckets to copy");
        }

        /// <summary>
        /// The distribution is the only thing that grows with the number of runs, and it must not: it is two
        /// fixed arrays, whatever arrives.
        /// </summary>
        [Test]
        public void TheDistributionCountsEveryTimeIntoAFixedNumberOfBuckets()
        {
            CourseTimeDistribution distribution = new CourseTimeDistribution();
            const int par = 30_000;

            distribution.AddRun(par / 2, par);          // far faster than par
            distribution.AddRun(par, par);
            distribution.AddRun(par * 10, par);         // far slower than par

            Assert.That(distribution.RunCount, Is.EqualTo(3));
            Assert.That(distribution.RunCounts.Length, Is.EqualTo(CourseTimeBuckets.BucketCount));
            Assert.That(distribution.RunCounts[0], Is.EqualTo(1), "a time under the first edge lands in the open bucket at the fast end");
            Assert.That(distribution.RunCounts[CourseTimeBuckets.BucketCount - 1], Is.EqualTo(1), "and one past the last edge in the open bucket at the slow end");
        }

        /// <summary>
        /// A first run's field is as big as any other: the clumsy runner takes the slowest opponent's place in
        /// a full lineup, and the field still comes fastest first.
        /// </summary>
        [Test]
        public void TheFirstRunGhostTakesTheSlowestPlaceInAFullField()
        {
            CourseGhostPoolState pool   = SeededPool();
            List<TimeTrialGhost> lineup = pool.SelectLineup(Player(1), playerBestMs: 0, maxGhosts: 3, rng: null);
            Assert.That(lineup, Has.Count.EqualTo(3));
            int slowestBefore = lineup.Max(ghost => ghost.FinishTimeMs);

            List<TimeTrialGhost> field = TimeTrialSeedRunners.WithFirstRunGhost(lineup, Course, Rules, maxGhosts: 3);

            string clumsyName = TimeTrialSeedRunners.NameFor(TimeTrialBotSkill.Clumsy);
            Assert.That(field, Has.Count.EqualTo(3), "the field kept its size");
            Assert.That(field[^1].DisplayName, Is.EqualTo(clumsyName), "the clumsy runner comes in last");
            Assert.That(field[^1].Kind, Is.EqualTo(TimeTrialGhostKind.Seed));
            Assert.That(field[^1].FinishTimeMs, Is.GreaterThan(slowestBefore), "the clumsy runner is slower than anyone it could replace");
            Assert.That(field.Select(ghost => ghost.FinishTimeMs), Is.Ordered, "the field is still fastest first");
            Assert.That(field.Take(2).Select(ghost => ghost.FinishTimeMs), Is.EqualTo(lineup.Take(2).Select(ghost => ghost.FinishTimeMs)),
                "only the slowest opponent was given up");
            Assert.That(lineup, Has.Count.EqualTo(3), "the lineup handed in is left as it was");
        }

        /// <summary>A field with room in it takes the clumsy runner as well, rather than in place of anyone.</summary>
        [Test]
        public void TheFirstRunGhostJoinsAShortField()
        {
            CourseGhostPoolState pool   = SeededPool();
            List<TimeTrialGhost> lineup = pool.SelectLineup(Player(1), playerBestMs: 0, maxGhosts: 2, rng: null);
            Assert.That(lineup, Has.Count.EqualTo(2));

            List<TimeTrialGhost> field = TimeTrialSeedRunners.WithFirstRunGhost(lineup, Course, Rules, maxGhosts: 3);

            Assert.That(field, Has.Count.EqualTo(3));
            Assert.That(field[^1].DisplayName, Is.EqualTo(TimeTrialSeedRunners.NameFor(TimeTrialBotSkill.Clumsy)));
            Assert.That(field.Take(2).Select(ghost => ghost.FinishTimeMs), Is.EqualTo(lineup.Select(ghost => ghost.FinishTimeMs)));

            List<TimeTrialGhost> alone = TimeTrialSeedRunners.WithFirstRunGhost(new List<TimeTrialGhost>(), Course, Rules, maxGhosts: 3);
            Assert.That(alone, Has.Count.EqualTo(1), "an empty field is given the clumsy runner alone");
        }

        /// <summary>
        /// Only a player who has never completed a run is given the clumsy runner. The server and the offline
        /// developer race both decide it through the same helper, so a player with a run behind them races
        /// exactly the field that was selected.
        /// </summary>
        [Test]
        public void OnlyAFirstRunIsGivenTheClumsyRunner()
        {
            CourseGhostPoolState pool   = SeededPool();
            List<TimeTrialGhost> lineup = pool.SelectLineup(Player(1), playerBestMs: 0, maxGhosts: 3, rng: null);
            string clumsyName = TimeTrialSeedRunners.NameFor(TimeTrialBotSkill.Clumsy);

            List<TimeTrialGhost> veteran = TimeTrialSeedRunners.FieldFor(hasCompletedARun: true, lineup, Course, Rules, maxGhosts: 3);
            Assert.That(veteran, Is.SameAs(lineup), "a player with a completed run races the field as selected");
            Assert.That(veteran.Select(ghost => ghost.DisplayName), Has.None.EqualTo(clumsyName));

            List<TimeTrialGhost> newcomer = TimeTrialSeedRunners.FieldFor(hasCompletedARun: false, lineup, Course, Rules, maxGhosts: 3);
            Assert.That(newcomer, Has.Count.EqualTo(3));
            Assert.That(newcomer.Select(ghost => ghost.DisplayName), Has.One.EqualTo(clumsyName));
        }

        /// <summary>
        /// A run's field is the lineup a pool selected when there is one, and the course's scripted runners
        /// when there is none (the server's fallback and the field the client drives for a race with no pool
        /// alike) held to the run's field size either way.
        /// </summary>
        [Test]
        public void ARunFieldFallsBackToTheScriptedRunners()
        {
            CourseGhostPoolState pool   = SeededPool();
            List<TimeTrialGhost> lineup = pool.SelectLineup(Player(1), playerBestMs: 0, maxGhosts: TimeTrialSeedRunners.GhostsPerRun, rng: null);

            List<TimeTrialGhost> selected = TimeTrialSeedRunners.RunField(hasCompletedARun: true, lineup, Course, Rules);
            Assert.That(selected, Is.SameAs(lineup), "a selected lineup is raced as it is");

            List<TimeTrialGhost> fallback = TimeTrialSeedRunners.RunField(hasCompletedARun: true, null, Course, Rules);
            Assert.That(fallback.Select(ghost => ghost.DisplayName),
                Is.EqualTo(TimeTrialSeedRunners.Lineup(Course, Rules).Select(ghost => ghost.DisplayName)),
                "no lineup falls back to the scripted runners");

            List<TimeTrialGhost> firstRun = TimeTrialSeedRunners.RunField(hasCompletedARun: false, new List<TimeTrialGhost>(), Course, Rules);
            Assert.That(firstRun, Has.Count.EqualTo(TimeTrialSeedRunners.GhostsPerRun));
            Assert.That(firstRun.Select(ghost => ghost.DisplayName), Has.One.EqualTo(TimeTrialSeedRunners.NameFor(TimeTrialBotSkill.Clumsy)));
        }
    }
}
