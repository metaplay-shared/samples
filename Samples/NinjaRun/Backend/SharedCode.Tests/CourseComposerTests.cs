using Metaplay.Core.Math;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Game.Logic.Tests
{
    /// <summary>
    /// Tests for <see cref="CourseComposer"/>: map ids, the legal-triplet rules, and the soundness and pacing of
    /// every map the shipped parts compose under the shipped <see cref="TimeTrialRules"/>.
    /// </summary>
    [TestFixture]
    public class CourseComposerTests
    {
        static SharedGameConfig _gameConfig;

        [OneTimeSetUp]
        public async Task SetUp()
        {
            _gameConfig = await TestGameConfig.LoadSharedAsync();
        }

        TimeTrialRules Rules => _gameConfig.TimeTrialRules;

        /// <summary>The three positions of a map, in composition order.</summary>
        static readonly CoursePartPosition[] EveryPosition =
        {
            CoursePartPosition.First,
            CoursePartPosition.Middle,
            CoursePartPosition.Last,
        };

        /// <summary>The scripted levels a map is given opponents at, slowest first.</summary>
        static readonly TimeTrialBotSkill[] SeedSkills =
        {
            TimeTrialBotSkill.Cautious,
            TimeTrialBotSkill.Competent,
            TimeTrialBotSkill.Sharp,
        };

        /// <summary>
        /// A map id reads back into the three parts it names, and anything that is not three parts reads back as
        /// no map.
        /// </summary>
        [Test]
        public void AMapIdIsItsTriplet()
        {
            CoursePartId first  = CoursePartId.FromString("alpha");
            CoursePartId middle = CoursePartId.FromString("beta");
            CoursePartId last   = CoursePartId.FromString("gamma");

            CourseId mapId = CourseComposer.MapIdFor(first, middle, last);
            Assert.That(mapId.Value, Is.EqualTo($"alpha{CourseComposer.PartSeparator}beta{CourseComposer.PartSeparator}gamma"));

            Assert.That(CourseComposer.TryParseMapId(mapId, out CoursePartId[] parsed), Is.True, "a map id must read back into the parts it was made from");
            Assert.That(parsed, Is.EqualTo(new CoursePartId[] { first, middle, last }));

            Assert.Multiple(() =>
            {
                Assert.That(CourseComposer.TryParseMapId(CourseId.FromString("not-a-map"), out CoursePartId[] _), Is.False,
                    "one name is not a map");
                Assert.That(CourseComposer.TryParseMapId(CourseId.FromString("alpha+beta"), out CoursePartId[] _), Is.False,
                    "two parts are not a map");
                Assert.That(CourseComposer.TryParseMapId(CourseId.FromString("alpha+beta+gamma+delta"), out CoursePartId[] _), Is.False,
                    "four parts are not a map");
                Assert.That(CourseComposer.TryParseMapId(CourseId.FromString("alpha++gamma"), out CoursePartId[] _), Is.False,
                    "a missing part is not a part");
            });
        }

        /// <summary>
        /// Composing an id that is not a legal map throws, and <see cref="CourseComposer.IsLegalTriplet"/> turns
        /// down an illegal triplet with a reason.
        /// </summary>
        [Test]
        public void ComposeRejectsWhatIsNotALegalMap()
        {
            CourseId sample = CourseComposer.LegalMapIds(_gameConfig).First();
            Assert.That(CourseComposer.TryParseMapId(sample, out CoursePartId[] sampleParts), Is.True);

            Assert.Multiple(() =>
            {
                Assert.Throws<ArgumentException>(() => CourseComposer.Compose(CourseId.FromString("not-a-map"), _gameConfig),
                    "a course id that is not a triplet was composed");
                Assert.Throws<ArgumentException>(
                    () => CourseComposer.Compose(CourseComposer.MapIdFor(sampleParts[0], CoursePartId.FromString("no-such-part"), sampleParts[2]), _gameConfig),
                    "a map naming a part the archive does not hold was composed");
                Assert.Throws<ArgumentException>(
                    () => CourseComposer.Compose(CourseComposer.MapIdFor(sampleParts[0], sampleParts[0], sampleParts[0]), _gameConfig),
                    "a map made of one part three times was composed");
            });

            CoursePartInfo anywhere  = Part("anywhere");
            CoursePartInfo alsoAnywhere = Part("also-anywhere");
            CoursePartInfo firstOnly  = Part("first-only", positions: new List<CoursePartPosition> { CoursePartPosition.First });
            CoursePartInfo setPieceA  = Part("set-piece-a", isSetPiece: true);
            CoursePartInfo setPieceB  = Part("set-piece-b", isSetPiece: true);

            AssertNotALegalTriplet("two parts", new CoursePartInfo[] { anywhere, alsoAnywhere });
            AssertNotALegalTriplet("a part in a position it may not occupy", new CoursePartInfo[] { anywhere, firstOnly, alsoAnywhere });
            AssertNotALegalTriplet("two set pieces", new CoursePartInfo[] { setPieceA, setPieceB, anywhere });

            Assert.That(CourseComposer.IsLegalTriplet(new CoursePartInfo[] { setPieceA, anywhere, alsoAnywhere }, out string _), Is.True,
                "one set piece is what a map takes, so this triplet proves the rule is not simply refusing everything");
        }

        /// <summary>
        /// Every legal map composes into a course with sound geometry, climbs built from the
        /// <see cref="GlobalConfig"/> tunables, and the same course from the same id every time.
        /// </summary>
        [Test]
        public void EveryLegalMapComposesIntoASoundCourse()
        {
            List<CourseId> mapIds = LegalMapIds();

            Assert.That(mapIds, Is.Not.Empty, "the published parts compose into no map at all");
            Assert.That(mapIds, Is.Unique, "a map id was offered twice");
            Assert.That(mapIds.Count, Is.EqualTo(CountLegalTripletsTheLongWay()),
                "the maps on offer are not the triplets the composition rules accept");

            GlobalConfig global = _gameConfig.Global;

            foreach (CourseId mapId in mapIds)
            {
                CourseDefinition course = CourseComposer.Compose(mapId, _gameConfig);
                Assert.That(CourseComposer.TryParseMapId(mapId, out CoursePartId[] partIds), Is.True);

                int hazardCount = 0;
                int bridgeCount = 0;
                foreach (CoursePartId partId in partIds)
                {
                    hazardCount += _gameConfig.CourseParts[partId].Hazards.Count;
                    bridgeCount += _gameConfig.CourseParts[partId].Bridges.Count;
                }

                Assert.Multiple(() =>
                {
                    Assert.That(course.Id, Is.EqualTo(mapId), $"{mapId}: a composed course is named by the map it was composed from");
                    Assert.That(course.StartX, Is.LessThan(course.FinishX), $"{mapId}: the finish must be ahead of the start");

                    Assert.That(course.Climbs.Count, Is.EqualTo(CourseComposer.PartsPerMap - 1), $"{mapId}: a map's parts are joined by one climb fewer than there are parts");
                    Assert.That(course.Gates.Count, Is.EqualTo(CourseComposer.PartsPerMap - 1), $"{mapId}: every climb's top torii is a segment gate");
                    for (int ndx = 0; ndx < course.Climbs.Count; ndx++)
                        Assert.That(course.Climbs[ndx].Id, Is.EqualTo($"climb-{ndx + 1}"), $"{mapId}: the climbs are named in course order");

                    Assert.That(course.Ground[0].StartX, Is.EqualTo(-CourseComposer.HeadLength), $"{mapId}: a map opens on the composer's run-up to the start line");
                    Assert.That(course.Ground[course.Ground.Count - 1].EndX, Is.EqualTo(course.FinishX + CourseComposer.TailLength),
                        $"{mapId}: the runner crosses the finish line rather than stopping on it");

                    F32 previousEnd = F32.MinValue;
                    foreach (GroundSegment segment in course.Ground)
                    {
                        Assert.That(segment.StartX, Is.LessThan(segment.EndX), $"{mapId}: a ground segment must have length");
                        Assert.That(segment.StartX, Is.GreaterThanOrEqualTo(previousEnd), $"{mapId}: ground segments must be sorted and must not overlap");
                        previousEnd = segment.EndX;
                    }

                    Assert.That(course.FindGroundIndexAt(course.StartX), Is.GreaterThanOrEqualTo(0), $"{mapId}: the runner must start on solid ground");
                    Assert.That(course.FindGroundIndexAt(course.FinishX), Is.GreaterThanOrEqualTo(0), $"{mapId}: there must be ground under the finish line");

                    Assert.That(course.Hazards.Count, Is.EqualTo(hazardCount), $"{mapId}: a map carries exactly the spike beds its parts carry");

                    Assert.That(course.DroopingBridges.Count, Is.EqualTo(bridgeCount), $"{mapId}: a map carries exactly the bridges its parts carry");
                    foreach (DroopingBridgePresentationFeature bridge in course.DroopingBridges)
                    {
                        Assert.That(bridge.HasWaterfall, Is.True, $"{mapId}: a part's bridge hangs over the gorge waterfall");
                        int groundIndex = course.FindGroundIndexAt(bridge.StartX);
                        Assert.That(groundIndex, Is.GreaterThanOrEqualTo(0), $"{mapId}: {bridge.Id} starts over no ground");
                        if (groundIndex >= 0)
                            Assert.That(bridge.EndX, Is.LessThanOrEqualTo(course.Ground[groundIndex].EndX),
                                $"{mapId}: one uninterrupted ground segment must carry the whole of {bridge.Id}");
                        foreach (HazardSpan hazard in course.Hazards)
                            Assert.That(hazard.StartX < bridge.EndX && hazard.EndX > bridge.StartX, Is.False,
                                $"{mapId}: {bridge.Id} must stay clear of every spike bed");
                    }

                    foreach (ClimbDefinition climb in course.Climbs)
                    {
                        Assert.That(climb.SlopeStartX - climb.BottomX, Is.EqualTo(ClimbDefinition.FootFlatLength), $"{mapId}: {climb.Id} does not carry the runner along the flat at its foot");
                        Assert.That(climb.TopX - climb.SlopeStartX, Is.EqualTo(global.CourseClimbSlopeLength), $"{mapId}: {climb.Id} is not the slope the tunable asks for");
                        Assert.That(climb.GateX - climb.TopX, Is.EqualTo(global.CourseClimbRunOutLength), $"{mapId}: {climb.Id} is not the run-out the tunable asks for");
                        Assert.That(course.FindGroundSegmentEndingAt(climb.SlopeStartX)?.TopY, Is.EqualTo(climb.TopY - global.CourseClimbRise),
                            $"{mapId}: {climb.Id} does not rise the tunable's height above the ground it starts from");
                    }

                    // Each bottom torii stands where the part before it hands over, so the flat at the slope's foot
                    // is laid past the part rather than taken out of it.
                    F32 handOver = F32.Zero;
                    for (int ndx = 0; ndx < course.Climbs.Count; ndx++)
                    {
                        ClimbDefinition climb = course.Climbs[ndx];
                        Assert.That(climb.BottomX, Is.EqualTo(handOver + _gameConfig.CourseParts[partIds[ndx]].Length),
                            $"{mapId}: {climb.Id}'s bottom torii does not stand at the end of the part before it");
                        handOver = climb.GateX;
                    }
                });

                // A stored recording is replayed against the course its map id composes into.
                Assert.That(Describe(CourseComposer.Compose(mapId, _gameConfig)), Is.EqualTo(Describe(course)),
                    $"{mapId}: composing the same map id twice did not give the same course");
            }
        }

        /// <summary>
        /// The offline practice map's first segment carries a bridge over the waterfall, with a spike bed or pit
        /// ahead of it.
        /// </summary>
        [Test]
        public void OfflinePracticeMeetsTheWaterfallInItsFirstSegmentAfterAHazard()
        {
            CourseDefinition course = CourseComposer.Compose(_gameConfig.Global.OfflinePracticeMapId, _gameConfig);
            Assert.That(course.Climbs, Is.Not.Empty, "the practice map has to have a first segment that ends somewhere");

            F32 firstSegmentEnd = course.Climbs[0].BottomX;

            DroopingBridgePresentationFeature waterfall = null;
            foreach (DroopingBridgePresentationFeature bridge in course.DroopingBridges)
            {
                if (bridge.HasWaterfall && bridge.EndX <= firstSegmentEnd)
                {
                    waterfall = bridge;
                    break;
                }
            }
            Assert.That(waterfall, Is.Not.Null, "the practice map's first segment carries no bridge over the waterfall");

            F32 firstHazardX = F32.MaxValue;
            foreach (HazardSpan hazard in course.Hazards)
                firstHazardX = F32.Min(firstHazardX, hazard.StartX);
            foreach (PitDefinition pit in course.Pits)
                firstHazardX = F32.Min(firstHazardX, pit.StartX);

            Assert.That(firstHazardX, Is.GreaterThan(course.StartX), "the practice map has no hazard past its start line");
            Assert.That(firstHazardX, Is.LessThan(waterfall.StartX), "the waterfall is the first thing on the practice map, ahead of every hazard");
        }

        /// <summary>
        /// A run with no input reaches the finish of every legal map, timing every gate and climb on the way.
        /// </summary>
        [Test]
        public void EveryLegalMapFinishesWithNoInput()
        {
            foreach (CourseId mapId in LegalMapIds())
            {
                CourseDefinition course = CourseComposer.Compose(mapId, _gameConfig);
                TimeTrialResult  result = TimeTrialSimulation.Replay(course, Rules, new TimeTrialRecording());

                Assert.That(result, Is.Not.Null, $"{mapId}: a run with no input never reached the finish");

                Assert.Multiple(() =>
                {
                    Assert.That(result.Steps, Is.LessThan(TimeTrialConstants.MaxRunSteps / 2), $"{mapId}: a run with no input came close to the step budget");
                    Assert.That(result.GateTimesMs.Count, Is.EqualTo(CourseComposer.PartsPerMap - 1), $"{mapId}: a finished run times every gate");
                    Assert.That(result.ClimbEntryTimesMs.Count, Is.EqualTo(CourseComposer.PartsPerMap - 1), $"{mapId}: a finished run times every climb's bottom torii");
                });

                TestContext.Out.WriteLine($"{mapId}: no input {result.FinishTimeMs} ms ({result.Steps} steps)");
            }
        }

        /// <summary>
        /// The three scripted levels finish every legal map in skill order, each segment is a sprint, and the
        /// cautious runner finishes the whole map inside one sitting.
        /// </summary>
        [Test]
        public void TheScriptedRunnersFinishEveryLegalMapInSkillOrder()
        {
            foreach (CourseId mapId in LegalMapIds())
            {
                CourseDefinition course = CourseComposer.Compose(mapId, _gameConfig);

                TimeTrialRecording recording = TimeTrialBotDriver.Drive(course, Rules);
                Assert.That(TimeTrialRecording.IsWellFormed(recording), Is.True, $"{mapId}: the scripted run produced a recording the server would turn down");

                TimeTrialResult cautious  = Seed(course, TimeTrialBotSkill.Cautious);
                TimeTrialResult competent = Seed(course, TimeTrialBotSkill.Competent);
                TimeTrialResult sharp     = Seed(course, TimeTrialBotSkill.Sharp);

                // A map's par, pace bands and first lineups are built from these times.
                TestContext.Out.WriteLine($"{mapId}: {recording.Inputs.Count} inputs in the competent run");
                foreach (TimeTrialBotSkill skill in SeedSkills)
                {
                    TimeTrialResult seed = skill == TimeTrialBotSkill.Cautious ? cautious : (skill == TimeTrialBotSkill.Competent ? competent : sharp);
                    TestContext.Out.WriteLine($"    {skill,-9} {seed.FinishTimeMs,6} ms  segments [{Segments(seed)}]  climbs [{ClimbCosts(seed)}]");
                }

                Assert.Multiple(() =>
                {
                    // Strict order with no minimum gap: the gap is what the parts add up to, not a tuned value.
                    Assert.That(sharp.FinishTimeMs, Is.LessThan(competent.FinishTimeMs), $"{mapId}: the sharp runner did not beat the competent one");
                    Assert.That(competent.FinishTimeMs, Is.LessThan(cautious.FinishTimeMs), $"{mapId}: the competent runner did not beat the cautious one");

                    Assert.That(competent.SegmentCount, Is.EqualTo(CourseComposer.PartsPerMap), $"{mapId}: a map is raced as one segment per part");
                    for (int segment = 0; segment < competent.SegmentCount; segment++)
                    {
                        Assert.That(competent.SegmentTimeMs(segment), Is.InRange(10_000, 40_000),
                            $"{mapId}: segment {segment + 1} is not the sprint a segment is meant to be");
                    }

                    Assert.That(cautious.FinishTimeMs, Is.LessThan(120_000), $"{mapId}: even the cautious runner has to finish inside two minutes");
                });
            }
        }

        /// <summary>
        /// Every scripted runner takes the same time from a climb's bottom torii to the gate above it.
        /// </summary>
        [Test]
        public void EverySeedPaysTheSameForEveryClimb()
        {
            // One simulation step, which is what the clamp onto the top of the slope can cost.
            int toleranceMs = TimeTrialConstants.StepsToMilliseconds(1) + 1;

            foreach (CourseId mapId in LegalMapIds())
            {
                CourseDefinition course = CourseComposer.Compose(mapId, _gameConfig);

                List<int[]> perSkill = new List<int[]>();
                foreach (TimeTrialBotSkill skill in SeedSkills)
                {
                    TimeTrialResult seed = Seed(course, skill);
                    Assert.That(seed.ClimbEntryTimesMs.Count, Is.EqualTo(course.Climbs.Count), $"{mapId}: a finished run reaches every bottom torii");
                    Assert.That(seed.GateTimesMs.Count, Is.EqualTo(course.Climbs.Count), $"{mapId}: a finished run crosses every gate");

                    int[] climbDurations = new int[course.Climbs.Count];
                    for (int climb = 0; climb < course.Climbs.Count; climb++)
                        climbDurations[climb] = seed.GateTimesMs[climb] - seed.ClimbEntryTimesMs[climb];

                    perSkill.Add(climbDurations);
                }

                for (int climb = 0; climb < course.Climbs.Count; climb++)
                {
                    int fastest = int.MaxValue;
                    int slowest = int.MinValue;
                    foreach (int[] climbDurations in perSkill)
                    {
                        fastest = climbDurations[climb] < fastest ? climbDurations[climb] : fastest;
                        slowest = climbDurations[climb] > slowest ? climbDurations[climb] : slowest;
                    }

                    Assert.That(slowest - fastest, Is.LessThanOrEqualTo(toleranceMs),
                        $"{mapId}: climb {climb + 1} cost the scripted runners {fastest}-{slowest} ms, so it is a stretch somebody can gain on");
                }
            }
        }

        /// <summary>
        /// One part on its own composes into a one-segment course: head, the part, tail, and no climb.
        /// </summary>
        [Test]
        public void OnePartComposesIntoAOneSegmentCourse()
        {
            CoursePartInfo   part   = _gameConfig.CourseParts.Values.First();
            CourseDefinition course = CourseComposer.Compose(CourseId.FromString(part.Id.Value), new CoursePartInfo[] { part }, _gameConfig.Global);

            Assert.Multiple(() =>
            {
                Assert.That(course.Climbs, Is.Empty, $"{part.Id}: a climb is what joins two parts, so one part has none");
                Assert.That(course.Gates, Is.Empty, $"{part.Id}: with no climb there is no gate, so the part is raced as one segment");
                Assert.That(course.StartX, Is.EqualTo(F32.Zero), $"{part.Id}: a part's local X=0 is the start line");
                Assert.That(course.FinishX, Is.EqualTo(part.Length), $"{part.Id}: a part on its own finishes where it hands over");
                Assert.That(course.Ground[0].StartX, Is.EqualTo(-CourseComposer.HeadLength), $"{part.Id}: the run-up to the start line is the composer's");
                Assert.That(course.Ground[course.Ground.Count - 1].EndX, Is.EqualTo(course.FinishX + CourseComposer.TailLength),
                    $"{part.Id}: the ground past the finish line is the composer's");
            });
        }

        /// <summary>
        /// A part's pit floor is laid down with its flag, raised with the part like the rest of its ground, and
        /// the composed course cuts it the pit it marks.
        /// </summary>
        [Test]
        public void APartsPitFloorKeepsItsFlagInTheComposedCourse()
        {
            CoursePartInfo pitted = new CoursePartInfo(
                id:            CoursePartId.FromString("pitted"),
                ground:        new List<GroundSegment>
                {
                    new GroundSegment(F32.Zero, F32.FromInt(20), F32.Zero),
                    new GroundSegment(F32.FromInt(20), F32.FromInt(26), F32.FromInt(-2), isPit: true),
                    new GroundSegment(F32.FromInt(26), F32.FromInt(40), F32.Zero),
                },
                hazards:       new List<HazardSpan>(),
                positions:     new List<CoursePartPosition>(EveryPosition),
                isSetPiece: false);

            CourseDefinition course = CourseComposer.Compose(
                CourseId.FromString("test-pitted"), new CoursePartInfo[] { Part("flat-first"), pitted }, _gameConfig.Global);

            ClimbDefinition climb = course.Climbs[0];
            int floorIndex = course.FindGroundIndexAt(climb.GateX + F32.FromInt(22));

            Assert.That(floorIndex, Is.GreaterThanOrEqualTo(0));
            GroundSegment floor = course.Ground[floorIndex];
            Assert.Multiple(() =>
            {
                Assert.That(floor.IsPit, Is.True, "the pit floor lost its flag in the composition");
                Assert.That(floor.StartX, Is.EqualTo(climb.GateX + F32.FromInt(20)));
                Assert.That(floor.EndX, Is.EqualTo(climb.GateX + F32.FromInt(26)));
                Assert.That(floor.TopY, Is.EqualTo(climb.TopY - F32.FromInt(2)), "the floor is raised with its part");

                Assert.That(course.Pits, Has.Count.EqualTo(1));
                Assert.That(course.Pits[0].StartX, Is.EqualTo(floor.StartX));
                Assert.That(course.Pits[0].Depth, Is.EqualTo(F32.FromInt(2)));

                foreach (GroundSegment segment in course.Ground)
                {
                    if (segment != floor)
                        Assert.That(segment.IsPit, Is.False, $"the ground from {segment.StartX} m was flagged a pit floor");
                }
            });
        }

        /// <summary>
        /// Every gap in a map's ground that no climb owns is a pit with a floor cut into it and a climbable wall on
        /// its far side, reaching all the way down to the floor.
        /// </summary>
        [Test]
        public void EveryGapOutsideAClimbIsAPitWithAFloorAndAClimbableFarWall()
        {
            foreach (CourseId mapId in LegalMapIds())
            {
                CourseDefinition course = CourseComposer.Compose(mapId, _gameConfig);

                foreach (PitDefinition pit in course.Pits)
                {
                    Assert.Multiple(() =>
                    {
                        Assert.That(course.FindClimbOwning(pit.StartX), Is.Null, $"{mapId}: a climb's slope is not a pit");

                        int floorIndex = course.FindGroundIndexAt(pit.StartX);
                        Assert.That(floorIndex, Is.GreaterThanOrEqualTo(0), $"{mapId}: a pit at {pit.StartX} m has no floor to land on");
                        if (floorIndex >= 0)
                            Assert.That(course.Ground[floorIndex].TopY, Is.EqualTo(pit.FloorY), $"{mapId}: a pit's floor is not where the pit says it is");

                        LedgeGrabDefinition wall = FarWall(course, pit);
                        Assert.That(wall, Is.Not.Null, $"{mapId}: a pit at {pit.StartX} m has no wall to climb out of");
                        if (wall == null)
                            return;
                        Assert.That(wall.BottomY, Is.EqualTo(pit.FloorY), $"{mapId}: a pit's wall must reach its floor");
                        Assert.That(wall.MinimumGrabY, Is.EqualTo(pit.FloorY), $"{mapId}: the whole of a pit's wall must be catchable");
                        Assert.That(wall.LandingX, Is.GreaterThan(pit.EndX));
                    });
                }
            }
        }

        /// <summary>
        /// A simulation cloned anywhere in a run finishes exactly where the run itself did, unaffected by the run
        /// going on without it.
        /// </summary>
        [Test]
        public void ASimulationClonedMidRunFinishesLikeTheRunItWasClonedFrom()
        {
            const int ClonePeriodSteps = 97;

            List<CourseDefinition> courses = new List<CourseDefinition>();
            foreach (CourseId mapId in LegalMapIds())
                courses.Add(CourseComposer.Compose(mapId, _gameConfig));
            courses.Add(TestCourses.Vine);

            foreach (CourseDefinition course in courses)
            {
                foreach (TimeTrialBotSkill skill in new[] { TimeTrialBotSkill.Competent, TimeTrialBotSkill.Cautious })
                {
                    List<TimeTrialInput> inputs = TimeTrialBotDriver.Drive(course, Rules, skill).Inputs;
                    TimeTrialSimulation  run    = new TimeTrialSimulation(course, Rules);
                    List<(TimeTrialSimulation Clone, int NextInput)> clones = new List<(TimeTrialSimulation, int)>();
                    int nextInput = 0;

                    while (!run.IsOver)
                    {
                        if (run.StepIndex % ClonePeriodSteps == 0)
                            clones.Add((run.Clone(), nextInput));

                        while (nextInput < inputs.Count && inputs[nextInput].Step == run.StepIndex)
                            run.ApplyInput(inputs[nextInput++]);
                        run.Step();
                    }

                    TimeTrialResult replayed = TimeTrialSimulation.Replay(course, Rules, new TimeTrialRecording(inputs));
                    Assert.That(run.Result?.FinishTimeMs, Is.EqualTo(replayed?.FinishTimeMs), $"{course.Id} {skill}: taking clones changed the run they were taken from");

                    foreach ((TimeTrialSimulation clone, int cloneNextInput) in clones)
                    {
                        int fromStep = clone.StepIndex;
                        int cursor   = cloneNextInput;

                        // The run went on to the finish after the clone was taken; none of that shows in the clone.
                        Assert.That(clone.GateTimesMs.Count, Is.EqualTo(CrossedBy(run.GateTimesMs, fromStep)), $"{course.Id} {skill}, cloned at step {fromStep}: gates");
                        Assert.That(clone.ClimbEntryTimesMs.Count, Is.EqualTo(CrossedBy(run.ClimbEntryTimesMs, fromStep)), $"{course.Id} {skill}, cloned at step {fromStep}: climbs");

                        while (!clone.IsOver)
                        {
                            while (cursor < inputs.Count && inputs[cursor].Step == clone.StepIndex)
                                clone.ApplyInput(inputs[cursor++]);
                            clone.Step();
                        }

                        string where = $"{course.Id} {skill}, cloned at step {fromStep}";
                        Assert.That(clone.StepIndex, Is.EqualTo(run.StepIndex), where);
                        Assert.That(clone.Mode, Is.EqualTo(run.Mode), where);
                        Assert.That(clone.X, Is.EqualTo(run.X), where);
                        Assert.That(clone.Y, Is.EqualTo(run.Y), where);
                        Assert.That(clone.GateTimesMs, Is.EqualTo(run.GateTimesMs), where);
                        Assert.That(clone.ClimbEntryTimesMs, Is.EqualTo(run.ClimbEntryTimesMs), where);
                        Assert.That(clone.Result?.FinishTimeMs, Is.EqualTo(run.Result?.FinishTimeMs), where);
                    }
                }
            }
        }

        #region Helpers

        static List<CourseId> LegalMapIds() => new List<CourseId>(CourseComposer.LegalMapIds(_gameConfig));

        TimeTrialResult Seed(CourseDefinition course, TimeTrialBotSkill skill)
        {
            TimeTrialResult result = TimeTrialSimulation.Replay(course, Rules, TimeTrialBotDriver.Drive(course, Rules, skill));
            Assert.That(result, Is.Not.Null, $"{course.Id}: the {skill} runner never reached the finish");
            return result;
        }

        /// <summary>
        /// How many ordered triplets of the published parts <see cref="CourseComposer.IsLegalTriplet"/> accepts,
        /// counted independently of <see cref="CourseComposer.LegalMapIds"/>.
        /// </summary>
        static int CountLegalTripletsTheLongWay()
        {
            List<CoursePartInfo> parts   = new List<CoursePartInfo>(_gameConfig.CourseParts.Values);
            CoursePartInfo[]     triplet = new CoursePartInfo[CourseComposer.PartsPerMap];
            int                  legal   = 0;

            foreach (CoursePartInfo first in parts)
            {
                triplet[0] = first;
                foreach (CoursePartInfo middle in parts)
                {
                    triplet[1] = middle;
                    foreach (CoursePartInfo last in parts)
                    {
                        triplet[2] = last;
                        if (CourseComposer.IsLegalTriplet(triplet, out string _))
                            legal++;
                    }
                }
            }

            return legal;
        }

        /// <summary>How many of <paramref name="crossingTimesMs"/> fall inside the first <paramref name="steps"/> steps.</summary>
        static int CrossedBy(IReadOnlyList<int> crossingTimesMs, int steps)
        {
            int count = 0;
            foreach (int timeMs in crossingTimesMs)
            {
                if (timeMs <= TimeTrialConstants.StepsToMilliseconds(steps))
                    count++;
            }
            return count;
        }

        /// <summary>The face standing on a pit's far lip, or null if the course cut none.</summary>
        static LedgeGrabDefinition FarWall(CourseDefinition course, PitDefinition pit)
        {
            foreach (CourseEncounterDefinition encounter in course.Encounters)
            {
                if (encounter is LedgeGrabDefinition ledge && ledge.X == pit.EndX)
                    return ledge;
            }
            return null;
        }

        static void AssertNotALegalTriplet(string what, IReadOnlyList<CoursePartInfo> parts)
        {
            Assert.That(CourseComposer.IsLegalTriplet(parts, out string reason), Is.False, $"{what} was accepted as a map");
            Assert.That(reason, Is.Not.Empty, $"{what} was turned down without saying why");
        }

        /// <summary>A composed course as comparable text: everything the composer decided, and nothing else.</summary>
        static string Describe(CourseDefinition course)
        {
            List<string> lines = new List<string> { $"start {course.StartX} finish {course.FinishX}" };
            foreach (GroundSegment segment in course.Ground)
                lines.Add($"ground {segment.StartX} {segment.EndX} {segment.TopY}");
            foreach (HazardSpan hazard in course.Hazards)
                lines.Add($"spikes {hazard.StartX} {hazard.EndX}");
            foreach (ClimbDefinition climb in course.Climbs)
                lines.Add($"climb {climb.Id} {climb.BottomX} {climb.TopX} {climb.TopY} {climb.GateX}");
            return string.Join("\n", lines);
        }

        /// <summary>What each climb held the runner for, in course order, for the pacing print-out.</summary>
        static string ClimbCosts(TimeTrialResult result)
        {
            List<string> costs = new List<string>();
            for (int climb = 0; climb < result.ClimbEntryTimesMs.Count; climb++)
                costs.Add($"{result.GateTimesMs[climb] - result.ClimbEntryTimesMs[climb]}");
            return string.Join(" / ", costs);
        }

        /// <summary>A result's segment times in course order, for the pacing print-out.</summary>
        static string Segments(TimeTrialResult result)
        {
            List<string> segments = new List<string>();
            for (int segment = 0; segment < result.SegmentCount; segment++)
                segments.Add($"{result.SegmentTimeMs(segment)}");
            return string.Join(" / ", segments);
        }

        /// <summary>A flat part that breaks no rule, with the given positions and set-piece flag.</summary>
        static CoursePartInfo Part(
            string                   id,
            List<CoursePartPosition> positions     = null,
            bool                     isSetPiece = false)
            => new CoursePartInfo(
                id:            CoursePartId.FromString(id),
                ground:        new List<GroundSegment> { new GroundSegment(F32.Zero, F32.FromInt(20), F32.Zero) },
                hazards:       new List<HazardSpan>(),
                positions:     positions ?? new List<CoursePartPosition>(EveryPosition),
                isSetPiece: isSetPiece);

        #endregion // Helpers
    }
}
