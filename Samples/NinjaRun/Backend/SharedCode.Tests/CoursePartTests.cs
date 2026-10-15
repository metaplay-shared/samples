using Metaplay.Core.Math;
using NUnit.Framework;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Game.Logic.Tests
{
    /// <summary>
    /// Course parts: the parts in the published archive held to the rules the composer relies on and played
    /// alone under the shipped <see cref="TimeTrialRules"/>, and <see cref="CoursePartInfo.Validate"/> held to
    /// parts that break those rules.
    /// </summary>
    [TestFixture]
    public class CoursePartTests
    {
        static SharedGameConfig _gameConfig;

        [OneTimeSetUp]
        public async Task SetUp()
        {
            _gameConfig = await TestGameConfig.LoadSharedAsync();
        }

        TimeTrialRules Rules => _gameConfig.TimeTrialRules;

        /// <summary>The three positions of a map, which every part names the ones it may be composed into.</summary>
        static readonly CoursePartPosition[] EveryPosition =
        {
            CoursePartPosition.First,
            CoursePartPosition.Middle,
            CoursePartPosition.Last,
        };

        /// <summary>The scripted levels a course is given opponents at, slowest first.</summary>
        static readonly TimeTrialBotSkill[] SeedSkills =
        {
            TimeTrialBotSkill.Cautious,
            TimeTrialBotSkill.Competent,
            TimeTrialBotSkill.Sharp,
        };

        [Test]
        public void TheConfigHoldsSoundParts()
        {
            Assert.That(_gameConfig.CourseParts, Is.Not.Empty, "the archive holds no course parts, so no map could be composed");

            Assert.Multiple(() =>
            {
                foreach ((CoursePartId id, CoursePartInfo part) in _gameConfig.CourseParts)
                {
                    // Joined into one string so a part that breaks a rule fails with the rule it broke rather
                    // than with a count.
                    Assert.That(string.Join(" / ", part.Validate()), Is.Empty, $"{id}: the part breaks a rule the composer relies on");
                }

                foreach (CoursePartPosition position in EveryPosition)
                {
                    bool anyEligible = false;
                    foreach ((CoursePartId _, CoursePartInfo part) in _gameConfig.CourseParts)
                        anyEligible |= part.MayOccupy(position);

                    Assert.That(anyEligible, Is.True, $"no part may be composed into the {position} position, so no map could be composed");
                }
            });
        }

        /// <summary>
        /// <see cref="CoursePartInfo.Validate"/> refuses a part that breaks any one rule and nothing else, and
        /// accepts the baseline part that breaks none.
        /// </summary>
        [Test]
        public void ValidateNamesEveryRuleABrokenPartBreaks()
        {
            Assert.Multiple(() =>
            {
                Assert.That(string.Join(" / ", Part().Validate()), Is.Empty,
                    "the baseline part is not sound, so the broken parts below prove nothing");

                AssertBroken(CoursePartRule.GroundStart, "ground that does not start at part-local zero",
                    Part(ground: new List<GroundSegment> { Segment(2, 20), Segment(25, 45) }));
                AssertBroken(CoursePartRule.EntryLevel, "an entry above its own level",
                    Part(ground: new List<GroundSegment> { Segment(0, 20, topY: 1), Segment(25, 45) }));
                AssertBroken(CoursePartRule.ExitLevel, "an exit above its own level",
                    Part(ground: new List<GroundSegment> { Segment(0, 20), Segment(25, 45, topY: 1) }));
                AssertBroken(CoursePartRule.GroundOrder, "ground that overlaps the segment before it",
                    Part(ground: new List<GroundSegment> { Segment(0, 20), Segment(15, 45) }));
                AssertBroken(CoursePartRule.HazardEntryClearance, "a spike bed inside the entry clearance",
                    Part(hazards: new List<HazardSpan> { Spikes(2, 3) }));
                AssertBroken(CoursePartRule.HazardExitClearance, "a spike bed inside the exit clearance",
                    Part(hazards: new List<HazardSpan> { Spikes(42, 44) }));
                AssertBroken(CoursePartRule.HazardGround, "a spike bed hanging over a pit",
                    Part(hazards: new List<HazardSpan> { Spikes(21, 23) }));
                AssertBroken(CoursePartRule.Id, "an id holding the map id separator",
                    Part(id: $"sound{CourseComposer.PartSeparator}part"));
                AssertBroken(CoursePartRule.Positions, "no position it may be composed into",
                    Part(positions: new List<CoursePartPosition>()));
                AssertBroken(CoursePartRule.Positions, "the same position named twice",
                    Part(positions: new List<CoursePartPosition> { CoursePartPosition.First, CoursePartPosition.First }));

                Assert.That(string.Join(" / ", Part(bridges: new List<CoursePartBridge> { Bridge(6, 17) }).Validate()), Is.Empty,
                    "a bridge over one clear stretch of ground was turned down, so the broken bridges below prove nothing");
                AssertBroken(CoursePartRule.BridgeGround, "a bridge hanging across a pit",
                    Part(bridges: new List<CoursePartBridge> { Bridge(15, 28) }));
                AssertBroken(CoursePartRule.HazardUnderBridge, "a bridge over a spike bed",
                    Part(bridges: new List<CoursePartBridge> { Bridge(27, 35) }));
                AssertBroken(CoursePartRule.BridgeEntryClearance, "a bridge inside the entry clearance",
                    Part(bridges: new List<CoursePartBridge> { Bridge(2, 10) }));
                AssertBroken(CoursePartRule.BridgeExitClearance, "a bridge inside the exit clearance",
                    Part(bridges: new List<CoursePartBridge> { Bridge(34, 43) }));
                AssertBroken(CoursePartRule.BridgeSpan, "a bridge with no span",
                    Part(bridges: new List<CoursePartBridge> { Bridge(10, 10) }));
                AssertBroken(CoursePartRule.BridgeOrder, "bridges that overlap",
                    Part(bridges: new List<CoursePartBridge> { Bridge(6, 14), Bridge(12, 18) }));
            });
        }

        /// <summary>
        /// Every part on its own is finished by a run with no input, with room to spare inside the step budget a
        /// run is cut off at.
        /// </summary>
        [Test]
        public void EveryPartFinishesAloneWithNoInput()
        {
            foreach ((CoursePartId id, CoursePartInfo part) in _gameConfig.CourseParts)
            {
                CourseDefinition alone  = Alone(part);
                TimeTrialResult  result = TimeTrialSimulation.Replay(alone, Rules, new TimeTrialRecording());

                Assert.That(result, Is.Not.Null, $"{id}: a run with no input never reached the end of the part");

                Assert.Multiple(() =>
                {
                    Assert.That(result.Steps, Is.LessThan(TimeTrialConstants.MaxRunSteps / 2), $"{id}: a run with no input came close to the step budget");
                    Assert.That(alone.Climbs, Is.Empty, $"{id}: a part carries no climb of its own — a climb is what joins two parts");
                    Assert.That(alone.Gates, Is.Empty, $"{id}: a part on its own is one segment, so there is no gate inside it");
                    Assert.That(alone.FinishX, Is.EqualTo(part.Length), $"{id}: a part on its own finishes where it hands over");
                    Assert.That(alone.FindGroundIndexAt(alone.StartX), Is.GreaterThanOrEqualTo(0), $"{id}: the runner must start on solid ground");
                    Assert.That(alone.FindGroundIndexAt(alone.FinishX), Is.GreaterThanOrEqualTo(0), $"{id}: there must be ground under the finish line");
                });

                TestContext.Out.WriteLine($"{id}: no input {result.FinishTimeMs} ms ({result.Steps} steps)");
            }
        }

        /// <summary>
        /// A competent run over every part is the sprint one segment of a map is meant to be, and jumping the
        /// obstacles beats walking into them.
        /// </summary>
        [Test]
        public void EveryPartIsASprintForACompetentRun()
        {
            foreach ((CoursePartId id, CoursePartInfo part) in _gameConfig.CourseParts)
            {
                CourseDefinition alone = Alone(part);

                TimeTrialRecording recording = TimeTrialBotDriver.Drive(alone, Rules);
                Assert.That(TimeTrialRecording.IsWellFormed(recording), Is.True, $"{id}: the scripted run produced a recording the server would turn down");

                TimeTrialResult competent = TimeTrialSimulation.Replay(alone, Rules, recording);
                Assert.That(competent, Is.Not.Null, $"{id}: the competent runner never reached the end of the part");

                TimeTrialResult noInput = TimeTrialSimulation.Replay(alone, Rules, new TimeTrialRecording());
                Assert.That(noInput, Is.Not.Null, $"{id}: a run with no input never reached the end of the part");

                // Printed before the pacing is asserted, so a part that has drifted out of the band says by how
                // much rather than only that it has.
                TestContext.Out.WriteLine($"{id}: {recording.Inputs.Count} inputs in the competent run");
                foreach (TimeTrialBotSkill skill in SeedSkills)
                {
                    TimeTrialResult seed = TimeTrialSimulation.Replay(alone, Rules, TimeTrialBotDriver.Drive(alone, Rules, skill));
                    Assert.That(seed, Is.Not.Null, $"{id}: the {skill} runner never reached the end of the part");
                    TestContext.Out.WriteLine($"    {skill,-9} {seed.FinishTimeMs,6} ms");
                }
                TestContext.Out.WriteLine($"    {"no input",-9} {noInput.FinishTimeMs,6} ms");

                Assert.Multiple(() =>
                {
                    Assert.That(competent.FinishTimeMs, Is.InRange(10_000, 40_000), $"{id}: the part is not the sprint one segment of a map is meant to be");
                    Assert.That(competent.FinishTimeMs, Is.LessThan(noInput.FinishTimeMs), $"{id}: jumping the obstacles was no faster than walking into them");
                });
            }
        }

        /// <summary>
        /// A pit floor flagged on a part's own ground is a pit only between two lips of its own in the part: it
        /// touches the stretch before it and the one after, both higher than it, so it is never the part's
        /// first or last stretch. No bridge hangs over it.
        /// </summary>
        [Test]
        public void APitFloorIsOnlySoundBetweenTwoHigherLipsOfItsOwn()
        {
            Assert.That(Part(ground: PitGround()).Validate(), Is.Empty, "a pit floor between two higher lips was refused");

            Assert.Multiple(() =>
            {
                AssertRefusedWith("is the part's first stretch of ground",
                    Part(ground: new List<GroundSegment> { PitFloor(0, 20, -2), Segment(20, 45) }));
                AssertRefusedWith("is the part's last stretch of ground",
                    Part(ground: new List<GroundSegment> { Segment(0, 20), PitFloor(20, 45, -2) }));
                AssertRefusedWith("The pit floor from 20.0 m to 26.0 m does not touch the ground on both sides of it",
                    Part(ground: new List<GroundSegment> { Segment(0, 18), PitFloor(20, 26, -2), Segment(26, 45) }));
                AssertRefusedWith("The pit floor from 20.0 m to 26.0 m does not lie below the ground on both sides of it",
                    Part(ground: new List<GroundSegment> { Segment(0, 20), PitFloor(20, 26, 0), Segment(26, 45) }));
                AssertRefusedWith("The bridge from 21.0 m to 25.0 m hangs over the pit floor from 20.0 m to 26.0 m",
                    Part(ground: PitGround(), bridges: new List<CoursePartBridge> { Bridge(21, 25) }));
            });
        }

        /// <summary>
        /// A spike bed may stand on a pit floor, as on any other stretch of ground, so long as it stays on that
        /// one stretch: a bed that runs from a lip down onto the floor stands on two.
        /// </summary>
        [Test]
        public void ASpikeBedStandsOnAPitFloor()
        {
            Assert.That(Part(ground: PitGround(), hazards: new List<HazardSpan> { Spikes(22, 24) }).Validate(), Is.Empty,
                "a spike bed on a pit floor was refused");
            Assert.That(Part(ground: PitGround(), hazards: new List<HazardSpan> { Spikes(20, 26) }).Validate(), Is.Empty,
                "a spike bed covering a whole pit floor was refused");

            AssertRefusedWith("The spike bed from 19.0 m to 22.0 m does not stand on one stretch of ground",
                Part(ground: PitGround(), hazards: new List<HazardSpan> { Spikes(19, 22) }));
        }

        /// <summary>A decoration may stand on a pit floor, and is planted at the floor's own height.</summary>
        [Test]
        public void ADecorationStandsOnAPitFloor()
        {
            CoursePartInfo part = new CoursePartInfo(
                id:            CoursePartId.FromString("decorated-pit"),
                ground:        PitGround(),
                hazards:       new List<HazardSpan>(),
                positions:     new List<CoursePartPosition>(EveryPosition),
                isSetPiece: false,
                bridges:       new List<CoursePartBridge>(),
                decorations:   new List<CoursePartDecoration> { new CoursePartDecoration("stone-lantern", F32.FromInt(23), CourseDecorationLayer.Behind) });

            Assert.That(part.Validate(), Is.Empty);
            Assert.That(part.TryGroundTopAt(F32.FromInt(23), out F32 topY), Is.True);
            Assert.That(topY, Is.EqualTo(F32.FromInt(-2)));
        }

        #region Helpers

        /// <summary>The course one part makes on its own: head, the part, tail, and no climb anywhere.</summary>
        CourseDefinition Alone(CoursePartInfo part)
            => CourseComposer.Compose(CourseId.FromString(part.Id.Value), new CoursePartInfo[] { part }, _gameConfig.Global);

        /// <summary>
        /// A part that breaks no rule, with one thing at a time replaceable. Its ground is two stretches with a
        /// pit between them, its spikes stand clear of both clearances, and it may be composed anywhere.
        /// </summary>
        static CoursePartInfo Part(
            string                   id            = "sound-part",
            List<GroundSegment>      ground        = null,
            List<HazardSpan>         hazards       = null,
            List<CoursePartPosition> positions     = null,
            List<CoursePartBridge>   bridges       = null)
            => new CoursePartInfo(
                id:            CoursePartId.FromString(id),
                ground:        ground    ?? new List<GroundSegment> { Segment(0, 20), Segment(25, 45) },
                hazards:       hazards   ?? new List<HazardSpan> { Spikes(30, 32) },
                positions:     positions ?? new List<CoursePartPosition>(EveryPosition),
                isSetPiece:    false,
                bridges:       bridges);

        static GroundSegment Segment(int startX, int endX, int topY = 0)
            => new GroundSegment(F32.FromInt(startX), F32.FromInt(endX), F32.FromInt(topY));

        static GroundSegment PitFloor(int startX, int endX, int topY)
            => new GroundSegment(F32.FromInt(startX), F32.FromInt(endX), F32.FromInt(topY), isPit: true);

        /// <summary>Ground 0–20, a pit floor 20–26 two metres down, and ground 26–45.</summary>
        static List<GroundSegment> PitGround()
            => new List<GroundSegment> { Segment(0, 20), PitFloor(20, 26, -2), Segment(26, 45) };

        static void AssertRefusedWith(string expected, CoursePartInfo part)
            => Assert.That(string.Join(" / ", part.Validate()), Does.Contain(expected));

        static HazardSpan Spikes(int startX, int endX)
            => new HazardSpan(F32.FromInt(startX), F32.FromInt(endX));

        static CoursePartBridge Bridge(int startX, int endX)
            => new CoursePartBridge(F32.FromInt(startX), F32.FromInt(endX));

        /// <summary>Asserts <paramref name="part"/> is refused for <paramref name="rule"/> and for no other rule.</summary>
        static void AssertBroken(CoursePartRule rule, string what, CoursePartInfo part)
        {
            HashSet<CoursePartRule> broken = new HashSet<CoursePartRule>();
            foreach (CoursePartProblem problem in part.Problems())
                broken.Add(problem.Rule);

            Assert.That(broken, Is.EquivalentTo(new CoursePartRule[] { rule }),
                $"a part with {what} was not refused for {rule} alone: {string.Join(" / ", part.Validate())}");
        }

        #endregion // Helpers
    }
}
