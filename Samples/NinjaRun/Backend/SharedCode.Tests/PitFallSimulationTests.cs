using Metaplay.Core.Math;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Game.Logic.Tests
{
    /// <summary>
    /// Pits on small purpose-built courses under the shipped <see cref="TimeTrialRules"/>: how they are derived
    /// from the ground, and how a runner falls in, climbs out or jumps them.
    /// </summary>
    [TestFixture]
    public class PitFallSimulationTests
    {
        static SharedGameConfig _gameConfig;

        [OneTimeSetUp]
        public async Task SetUp()
        {
            _gameConfig = await TestGameConfig.LoadSharedAsync();
        }

        TimeTrialRules Rules => _gameConfig.TimeTrialRules;

        /// <summary>Where every test course opens its pit, far enough in that the runner arrives at full pace.</summary>
        const int PitStartX = 40;

        /// <summary>A pit wider than a full charge clears at the shipped rules.</summary>
        const int LongPitWidth = 12;

        [Test]
        public void JumpingAPitBeatsFallingIn()
        {
            CourseDefinition course = PitCourse(5);

            TimeTrialResult over = TimeTrialSimulation.Replay(course, Rules, TimeTrialBotDriver.Drive(course, Rules, TimeTrialBotSkill.Sharp));
            TimeTrialResult into = TimeTrialSimulation.Replay(course, Rules, new TimeTrialRecording());

            Assert.That(over, Is.Not.Null, "the scripted run never reached the finish");
            Assert.That(into, Is.Not.Null, "a run with no input never reached the finish");

            TestContext.Out.WriteLine(
                $"5 m pit, {course.Pits[0].Depth} m deep: jumped {over.FinishTimeMs} ms, fell in {into.FinishTimeMs} ms, margin {into.FinishTimeMs - over.FinishTimeMs} ms");

            Assert.That(over.FinishTimeMs, Is.LessThan(into.FinishTimeMs),
                "falling into a pit has to cost more than the charge spent jumping it, or the obstacle asks nothing");
        }

        /// <summary>
        /// A pit wider than a full charge clears is crossed faster by running straight in than by full-charging
        /// at its near lip.
        /// </summary>
        [Test]
        public void FallingIntoALongPitBeatsChargingAtIt()
        {
            CourseDefinition course = PitCourse(LongPitWidth);

            TimeTrialResult charged = TimeTrialSimulation.Replay(course, Rules, FullChargeAtTheNearLip(course));
            TimeTrialResult into    = TimeTrialSimulation.Replay(course, Rules, new TimeTrialRecording());

            Assert.That(charged, Is.Not.Null, "the full-charge run never reached the finish");
            Assert.That(into, Is.Not.Null, "a run with no input never reached the finish");

            TestContext.Out.WriteLine(
                $"{LongPitWidth} m pit, {course.Pits[0].Depth} m deep: full charge {charged.FinishTimeMs} ms, fell in {into.FinishTimeMs} ms, margin {charged.FinishTimeMs - into.FinishTimeMs} ms");

            Assert.That(into.FinishTimeMs, Is.LessThan(charged.FinishTimeMs),
                "a pit no charge clears has to be crossed fastest by running into it, or charging at it is not the loss it is meant to be");
        }

        /// <summary>
        /// No scripted runner charges at a pit it cannot clear, at any level: on a course whose only obstacle
        /// is a long pit, every level runs straight in and finishes exactly where a run with no input does.
        /// </summary>
        [Test]
        public void NoScriptedRunnerJumpsALongPit()
        {
            CourseDefinition course = PitCourse(LongPitWidth);
            TimeTrialResult  into   = TimeTrialSimulation.Replay(course, Rules, new TimeTrialRecording());

            Assert.That(into, Is.Not.Null, "a run with no input never reached the finish");

            foreach (TimeTrialBotSkill skill in new[] { TimeTrialBotSkill.Cautious, TimeTrialBotSkill.Competent, TimeTrialBotSkill.Sharp })
            {
                TimeTrialRecording recording = TimeTrialBotDriver.Drive(course, Rules, skill);
                TimeTrialResult    driven    = TimeTrialSimulation.Replay(course, Rules, recording);

                Assert.That(recording.Inputs, Is.Empty, $"the {skill} runner pressed the button in front of a pit it cannot clear");
                Assert.That(driven, Is.Not.Null, $"the {skill} runner never reached the finish");
                Assert.That(driven.FinishTimeMs, Is.EqualTo(into.FinishTimeMs), $"the {skill} runner did not run straight into the long pit");
            }
        }

        /// <summary>
        /// The jump range the course tools judge a pit by is the one the simulation actually flies: a full
        /// charge over level ground lands within one step's travel short of
        /// <see cref="TimeTrialSimulation.MaxJumpRange"/>, and a full charge from the very lip of a long pit
        /// comes down on its floor, short of the far wall.
        /// </summary>
        [Test]
        public void AFullChargeCannotClearALongPit()
        {
            F32 maxJumpRange = TimeTrialSimulation.MaxJumpRange(Rules);
            F32 stepTravel   = F32.Div(Rules.MaxLaunchSpeed * F32.Cos(F32.DegToRad(Rules.LaunchAngleDegrees)), F32.FromInt(TimeTrialConstants.StepsPerSecond));
            F32 epsilon      = F32.Ratio1000(1);

            // Over level ground: the distance from where the feet were planted to where they come back down.
            TimeTrialSimulation level = new TimeTrialSimulation(LevelCourse(), Rules);
            while (level.X < F32.FromInt(PitStartX))
                level.Step();

            F32 takeOffX = level.X;
            FullCharge(level);
            Assert.That(level.Mode, Is.EqualTo(TimeTrialMode.Airborne), "a full charge did not launch the runner");

            while (level.Mode == TimeTrialMode.Airborne)
                level.Step();

            F32 measuredRange = level.X - takeOffX;
            TestContext.Out.WriteLine($"full charge over level ground: measured {measuredRange} m, {nameof(TimeTrialSimulation.MaxJumpRange)} {maxJumpRange} m, one step's travel {stepTravel} m");

            Assert.That(level.Y, Is.EqualTo(F32.Zero), "the level jump came down somewhere other than the ground it left");
            Assert.That(measuredRange, Is.InRange(maxJumpRange - stepTravel - epsilon, maxJumpRange + epsilon),
                "the simulated full charge does not fly the range the shared helper says it does");

            // From the lip of a long pit: the flight ends on the floor, inside the pit.
            CourseDefinition    course     = PitCourse(LongPitWidth);
            PitDefinition       pit        = course.Pits[0];
            TimeTrialSimulation simulation = new TimeTrialSimulation(course, Rules);

            Assert.That(pit.EndX - pit.StartX, Is.GreaterThan(maxJumpRange), "the long pit under test is not wider than a full charge clears");

            while (simulation.X + Rules.RunSpeed * F32.Ratio(1, TimeTrialConstants.StepsPerSecond) < pit.StartX)
                simulation.Step();

            F32 lipTakeOffX = simulation.X;
            FullCharge(simulation);
            while (simulation.Mode == TimeTrialMode.Airborne)
                simulation.Step();

            TestContext.Out.WriteLine(
                $"full charge from {lipTakeOffX} m at a lip at {pit.StartX} m: landed at {simulation.X} m, {simulation.Y} m, in a pit ending at {pit.EndX} m");

            Assert.That(simulation.Mode, Is.EqualTo(TimeTrialMode.Running), "the full charge ended on the far wall rather than on the floor");
            Assert.That(simulation.Y, Is.EqualTo(pit.FloorY), "the full charge from the lip did not come down on the pit floor");
            Assert.That(simulation.X, Is.InRange(pit.StartX, pit.EndX), "the full charge from the lip landed outside the pit");
        }

        /// <summary>
        /// A full charge from a pit's floor ends on the far wall rather than over the lip, at either end of the
        /// depth range and in a long pit.
        /// </summary>
        [TestCase(4)]
        [TestCase(7)]
        [TestCase(LongPitWidth)]
        public void JumpingOutOfAPitIsImpossible(int gapWidth)
        {
            CourseDefinition    course     = PitCourse(gapWidth);
            PitDefinition       pit        = course.Pits[0];
            TimeTrialSimulation simulation = new TimeTrialSimulation(course, Rules);

            Assert.That(pit.Depth, Is.EqualTo(gapWidth == 4 ? CourseDefinition.MinimumPitDepth : CourseDefinition.MaximumPitDepth));

            while (!(simulation.Mode == TimeTrialMode.Running && simulation.Y == pit.FloorY) && !simulation.IsOver)
                simulation.Step();

            Assert.That(simulation.IsOver, Is.False, "the runner never reached the floor of the pit");
            Assert.That(simulation.X, Is.InRange(pit.StartX, pit.EndX));

            // Plant the feet the moment the floor is underfoot and spend the whole charge on getting out.
            simulation.ApplyInput(new TimeTrialInput(simulation.StepIndex, pressed: true));
            for (int step = 0; step < Rules.MaxChargeSteps; step++)
                simulation.Step();
            simulation.ApplyInput(new TimeTrialInput(simulation.StepIndex, pressed: false));

            while (simulation.Y < F32.Zero && simulation.Mode != TimeTrialMode.Traversing && !simulation.IsOver)
                simulation.Step();

            Assert.That(simulation.Mode, Is.EqualTo(TimeTrialMode.Traversing), "a full charge from the floor carried the runner over the far lip");
            Assert.That(simulation.Y, Is.LessThan(F32.Zero), "the catch is on the face below the lip");
            Assert.That(simulation.BodyRight, Is.EqualTo(pit.EndX));

            while (!simulation.IsOver)
                simulation.Step();

            Assert.That(simulation.IsFinished, Is.True, "the jump that failed still has to leave a finishable run");
        }

        /// <summary>
        /// The whole fall and climb out, frame by frame: the runner never moves backwards, and no step moves it
        /// further than running, launching or climbing at the configured speeds could carry it.
        /// </summary>
        [Test]
        public void AFallIsContinuous()
        {
            CourseDefinition    course       = PitCourse(5);
            TimeTrialSimulation simulation   = new TimeTrialSimulation(course, Rules);
            F32                 stepDuration = F32.Ratio(1, TimeTrialConstants.StepsPerSecond);
            F32                 epsilon      = F32.Ratio1000(1);

            F32  previousX  = simulation.X;
            F32  previousY  = simulation.Y;
            F32  previousVx = simulation.VelocityX;
            bool landedOnTheFloor = false;
            bool mantled          = false;

            while (!simulation.IsOver)
            {
                simulation.Step();

                F32 fastest = F32.Max(
                    F32.Max(F32.Abs(previousVx), F32.Abs(simulation.VelocityX)),
                    F32.Max(F32.Max(Rules.RunSpeed, Rules.LedgeGrabRiseSpeed), Rules.MaxLaunchSpeed));
                F32 reach = fastest * stepDuration + epsilon;

                Assert.That(simulation.X, Is.GreaterThanOrEqualTo(previousX), $"step {simulation.StepIndex} moved the runner backwards");
                Assert.That(simulation.X - previousX, Is.LessThanOrEqualTo(reach), $"step {simulation.StepIndex} moved the runner further along than it could travel");
                Assert.That(F32.Abs(simulation.Y - previousY), Is.LessThanOrEqualTo(reach), $"step {simulation.StepIndex} moved the runner further up or down than it could travel");

                landedOnTheFloor |= simulation.Mode == TimeTrialMode.Running && simulation.Y == course.Pits[0].FloorY;
                mantled          |= simulation.EncounterPhase == "ledge-rise";

                previousX  = simulation.X;
                previousY  = simulation.Y;
                previousVx = simulation.VelocityX;
            }

            Assert.That(landedOnTheFloor, Is.True, "the run never reached the pit floor, so continuity was checked over nothing");
            Assert.That(mantled, Is.True, "the run never climbed the far wall, so continuity was checked over nothing");
            Assert.That(simulation.IsFinished, Is.True);
        }

        /// <summary>
        /// The extra depth of a deeper pit costs its rise at <see cref="TimeTrialRules.LedgeGrabRiseSpeed"/>, and
        /// the rest of the mantle costs the same.
        /// </summary>
        [Test]
        public void TheRiseCostScalesWithCatchHeight()
        {
            CourseDefinition shallow = PitCourse(4);
            CourseDefinition middle  = PitCourse(5);
            CourseDefinition deep    = PitCourse(7);

            int shallowRise = MantleSteps(shallow, out int shallowMantle);
            int middleRise  = MantleSteps(middle,  out int middleMantle);
            int deepRise    = MantleSteps(deep,    out int deepMantle);

            // Printed for all three widths, because a course's pacing and the depth rule are tuned against what
            // a pit costs.
            foreach ((CourseDefinition course, int rise, int mantle) in new[]
                     {
                         (shallow, shallowRise, shallowMantle),
                         (middle,  middleRise,  middleMantle),
                         (deep,    deepRise,    deepMantle),
                     })
            {
                TestContext.Out.WriteLine(
                    $"{course.Pits[0].EndX - course.Pits[0].StartX} m pit, {course.Pits[0].Depth} m deep: rise {rise} steps, whole mantle {mantle} steps ({TimeTrialConstants.StepsToMilliseconds(mantle)} ms)");
            }

            F32 extraDepth   = deep.Pits[0].Depth - shallow.Pits[0].Depth;
            int expectedMore = F32.RoundToInt(F32.Div(extraDepth, Rules.LedgeGrabRiseSpeed) * F32.FromInt(TimeTrialConstants.StepsPerSecond));

            Assert.That(deepRise - shallowRise, Is.EqualTo(expectedMore).Within(1),
                $"the deeper pit did not cost its extra {extraDepth} m at the {Rules.LedgeGrabRiseSpeed} m/s climb speed");
            Assert.That(deepMantle - shallowMantle, Is.EqualTo(expectedMore).Within(1),
                "the hang and the top transition are fixed, so the whole mantle grows by the rise alone");
        }

        /// <summary>
        /// Every gap outside a climb becomes a pit with a floor cut to the depth rule and a catchable far wall, a
        /// step up becomes a face to mantle, and the authored encounters keep their place and id.
        /// </summary>
        [Test]
        public void PitsAreDerivedFromGaps()
        {
            CourseDefinition course = new CourseDefinition(
                CourseId.FromString("test-derived-pits"), "Derived pits",
                startX:  F32.Zero,
                finishX: F32.FromInt(180),
                ground: new List<GroundSegment>
                {
                    Segment(-10,  20,  0),   // a 3 m gap, cut to the shallowest a pit is ever cut
                    Segment( 23,  40,  0),   // a 5 m gap, cut by the rule itself
                    Segment( 45,  60,  0),   // a 7 m gap, cut to the deepest a pit is ever cut
                    Segment( 67,  90,  0),
                    Segment( 90, 110, 15),   // a step up, which is a face rather than a pit
                    Segment(130, 200, 30),   // the gap below the climb, which is the climb's slope
                },
                hazards: new List<HazardSpan>(),
                encounters: new List<CourseEncounterDefinition>
                {
                    new ClimbDefinition("test-stair", F32.FromInt(110) - ClimbDefinition.FootFlatLength, F32.FromInt(130), F32.FromInt(3), F32.FromInt(138)),
                });

            Assert.That(course.Pits, Has.Count.EqualTo(3), "every gap outside a climb is a pit and nothing else is");

            F32[] expectedDepths = { CourseDefinition.MinimumPitDepth, F32.Div(F32.FromInt(5), CourseDefinition.PitWidthPerDepth), CourseDefinition.MaximumPitDepth };
            F32[] expectedStarts = { F32.FromInt(20), F32.FromInt(40), F32.FromInt(60) };
            F32[] expectedEnds   = { F32.FromInt(23), F32.FromInt(45), F32.FromInt(67) };

            Assert.Multiple(() =>
            {
                for (int ndx = 0; ndx < course.Pits.Count; ndx++)
                {
                    PitDefinition pit = course.Pits[ndx];

                    Assert.That(pit.StartX, Is.EqualTo(expectedStarts[ndx]));
                    Assert.That(pit.EndX, Is.EqualTo(expectedEnds[ndx]));
                    Assert.That(pit.Depth, Is.EqualTo(expectedDepths[ndx]), $"the pit at {pit.StartX} m is not cut to the depth rule");
                    Assert.That(pit.FloorY, Is.EqualTo(-expectedDepths[ndx]), "the floor sits that far below the lower of the two lips");

                    int floorIndex = course.FindGroundIndexAt(pit.StartX);
                    Assert.That(floorIndex, Is.GreaterThanOrEqualTo(0));
                    Assert.That(course.Ground[floorIndex].StartX, Is.EqualTo(pit.StartX));
                    Assert.That(course.Ground[floorIndex].EndX, Is.EqualTo(pit.EndX));
                    Assert.That(course.Ground[floorIndex].TopY, Is.EqualTo(pit.FloorY));
                }

                // Six authored stretches of ground plus one floor per pit, still sorted and still gap-free
                // everywhere but under the climb.
                Assert.That(course.Ground, Has.Count.EqualTo(9));
                Assert.That(course.FindGroundIndexAt(F32.FromInt(120)), Is.LessThan(0), "the climb's slope is not ground");

                // The climb the course authored keeps its place at the head of the list and its own id, and the
                // derived faces follow it in the order the runner meets them.
                Assert.That(course.Encounters, Has.Count.EqualTo(5));
                Assert.That(course.Encounters[0].Id, Is.EqualTo("test-stair"));
                Assert.That(course.Encounters[1].Id, Is.EqualTo($"{CourseDefinition.PitWallIdPrefix}1"));
                Assert.That(course.Encounters[2].Id, Is.EqualTo($"{CourseDefinition.PitWallIdPrefix}2"));
                Assert.That(course.Encounters[3].Id, Is.EqualTo($"{CourseDefinition.PitWallIdPrefix}3"));
                Assert.That(course.Encounters[4].Id, Is.EqualTo($"{CourseDefinition.StepLedgeIdPrefix}1"));

                for (int ndx = 0; ndx < course.Pits.Count; ndx++)
                {
                    PitDefinition       pit  = course.Pits[ndx];
                    LedgeGrabDefinition wall = (LedgeGrabDefinition)course.Encounters[ndx + 1];

                    Assert.That(wall.X, Is.EqualTo(pit.EndX), "a pit's wall stands on its far lip");
                    Assert.That(wall.BottomY, Is.EqualTo(pit.FloorY));
                    Assert.That(wall.MinimumGrabY, Is.EqualTo(pit.FloorY), "the whole face is catchable, from the floor up");
                    Assert.That(wall.LandingX, Is.GreaterThan(pit.EndX));
                }

                LedgeGrabDefinition step = (LedgeGrabDefinition)course.Encounters[4];
                Assert.That(step.X, Is.EqualTo(F32.FromInt(90)));
                Assert.That(step.BottomY, Is.EqualTo(F32.Zero));
                Assert.That(step.TopY, Is.EqualTo(F32.Ratio10(15)));
                Assert.That(step.MinimumGrabY, Is.EqualTo(F32.Zero));
            });
        }

        /// <summary>
        /// A course can also author a pit by flagging a stretch of ground as its floor, and gets the same
        /// primitive back: a pit as deep as the lower lip stands above that floor, whatever its width, and a
        /// catchable wall on the far side. The step from the floor up to the far lip is that wall and not a step
        /// of its own, and the floor stays in the ground as it was authored.
        /// </summary>
        [Test]
        public void AFlaggedFloorIsAPitAtItsAuthoredDepth()
        {
            CourseDefinition course = FlaggedPitCourse(40, 46, -32, farLipDecimeters: 5);

            Assert.That(course.Pits, Has.Count.EqualTo(1));
            PitDefinition pit = course.Pits[0];

            Assert.Multiple(() =>
            {
                Assert.That(pit.StartX, Is.EqualTo(F32.FromInt(40)));
                Assert.That(pit.EndX, Is.EqualTo(F32.FromInt(46)));
                Assert.That(pit.FloorY, Is.EqualTo(F32.Ratio10(-32)));
                Assert.That(pit.Depth, Is.EqualTo(F32.Ratio10(32)), "the depth is the authored floor below the lower lip, past the clamp a gap is cut to");

                Assert.That(course.Ground, Has.Count.EqualTo(3), "a flagged floor is cut nothing more");
                Assert.That(course.Ground[1].IsPit, Is.True, "the floor keeps its flag");
                Assert.That(course.Ground[1].TopY, Is.EqualTo(pit.FloorY));

                Assert.That(course.Encounters, Has.Count.EqualTo(1), "the face out of the pit is its wall and not also a step");
                LedgeGrabDefinition wall = (LedgeGrabDefinition)course.Encounters[0];
                Assert.That(wall.Id, Is.EqualTo($"{CourseDefinition.PitWallIdPrefix}1"));
                Assert.That(wall.X, Is.EqualTo(pit.EndX));
                Assert.That(wall.BottomY, Is.EqualTo(pit.FloorY));
                Assert.That(wall.TopY, Is.EqualTo(F32.Ratio10(5)), "the wall rises to the far lip");
                Assert.That(wall.MinimumGrabY, Is.EqualTo(pit.FloorY), "the whole face is catchable, from the floor up");
                Assert.That(wall.LandingX, Is.EqualTo(pit.EndX + CourseDefinition.DerivedLedgeLandingReach));
            });
        }

        /// <summary>
        /// The mantle up a derived face (a pit's far wall and a step's face alike) sets the runner down
        /// <see cref="CourseDefinition.DerivedLedgeLandingReach"/> past the face, with its feet on the ground above
        /// and the whole body above the face's top, and running resumes from exactly there.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public void ADerivedMantleSetsTheRunnerDownItsLandingReachPastTheFace(bool pit)
        {
            List<GroundSegment> ground = pit
                ? new List<GroundSegment> { Segment(-10, PitStartX, 0), Segment(PitStartX + 5, 100, 0) }
                : new List<GroundSegment> { Segment(-10, PitStartX, 0), Segment(PitStartX, 100, 15) };
            CourseDefinition course = new CourseDefinition(
                CourseId.FromString(pit ? "test-landing-pit" : "test-landing-step"), "Landing",
                startX:  F32.Zero,
                finishX: F32.FromInt(80),
                ground:  ground,
                hazards: new List<HazardSpan>());

            LedgeGrabDefinition face = null;
            foreach (CourseEncounterDefinition encounter in course.Encounters)
                face ??= encounter as LedgeGrabDefinition;
            Assert.That(face, Is.Not.Null, "the course derives a face to mantle");

            Assert.That(face.LandingX, Is.EqualTo(face.X + CourseDefinition.DerivedLedgeLandingReach));

            TimeTrialSimulation simulation = new TimeTrialSimulation(course, Rules);
            string previousPhase = "";
            while (!simulation.IsOver && !(previousPhase == "ledge-top-transition" && simulation.Mode == TimeTrialMode.Running))
            {
                previousPhase = simulation.EncounterPhase;
                simulation.Step();
            }

            Assert.Multiple(() =>
            {
                Assert.That(simulation.Mode, Is.EqualTo(TimeTrialMode.Running), "the runner with no input mantles the face");
                Assert.That(simulation.X, Is.EqualTo(face.LandingX), "running resumes where the mantle set the runner down");
                Assert.That(simulation.Y, Is.EqualTo(face.TopY));
                Assert.That(course.HasSurfaceAt(simulation.X, simulation.Y), Is.True, "the feet stand on the ground above the face");
                Assert.That(simulation.BodyBottom, Is.GreaterThanOrEqualTo(face.TopY), "no part of the body is below the face's top");
                Assert.That(simulation.BodyRight, Is.GreaterThan(face.X));
            });
        }

        /// <summary>A pit cut into a gap and a pit on a flagged floor are numbered together, in the order the runner meets them.</summary>
        [Test]
        public void GapPitsAndFlaggedPitsShareOneNumbering()
        {
            CourseDefinition course = new CourseDefinition(
                CourseId.FromString("test-mixed-pits"), "Mixed pits",
                startX:  F32.Zero,
                finishX: F32.FromInt(80),
                ground: new List<GroundSegment>
                {
                    Segment(-10, 20, 0),
                    Segment( 23, 30, 0),     // after a 3 m gap
                    PitFloor(30, 36, -30),   // a flagged floor between two lips
                    Segment( 36, 50, 5),
                    Segment( 55, 100, 0),    // after a 5 m gap
                },
                hazards: new List<HazardSpan>());

            Assert.That(course.Pits, Has.Count.EqualTo(3));

            Assert.Multiple(() =>
            {
                Assert.That(course.Pits[0].StartX, Is.EqualTo(F32.FromInt(20)));
                Assert.That(course.Pits[1].StartX, Is.EqualTo(F32.FromInt(30)));
                Assert.That(course.Pits[1].Depth, Is.EqualTo(F32.FromInt(3)), "the flagged pit is as deep as the lower of its lips stands above it");
                Assert.That(course.Pits[2].StartX, Is.EqualTo(F32.FromInt(50)));

                Assert.That(course.Ground, Has.Count.EqualTo(7), "five authored stretches and a floor for each gap");
                Assert.That(course.Encounters, Has.Count.EqualTo(3), "one wall per pit, and no step out of the flagged floor");
                for (int ndx = 0; ndx < course.Pits.Count; ndx++)
                {
                    LedgeGrabDefinition wall = (LedgeGrabDefinition)course.Encounters[ndx];
                    Assert.That(wall.Id, Is.EqualTo($"{CourseDefinition.PitWallIdPrefix}{ndx + 1}"));
                    Assert.That(wall.X, Is.EqualTo(course.Pits[ndx].EndX));
                    Assert.That(wall.BottomY, Is.EqualTo(course.Pits[ndx].FloorY));
                }
            });
        }

        /// <summary>A course that puts a flagged floor under a climb is refused, naming the rule.</summary>
        [Test]
        public void AFlaggedFloorUnderAClimbIsRefused()
        {
            ArgumentException climbed = Assert.Throws<ArgumentException>(() => new CourseDefinition(
                CourseId.FromString("test-climbed-floor"), "Climbed floor",
                F32.Zero, F32.FromInt(90),
                new List<GroundSegment> { Segment(-12, 40, 0), Segment(50, 54, 30), PitFloor(54, 56, 10), Segment(56, 100, 30) },
                new List<HazardSpan>(),
                encounters: new List<CourseEncounterDefinition> { new ClimbDefinition("climb", F32.FromInt(40) - ClimbDefinition.FootFlatLength, F32.FromInt(50), F32.FromInt(3), F32.FromInt(58)) }));
            Assert.That(climbed.Message, Does.Contain("must be free of pit floors from its foot to its gate"));
        }

        /// <summary>
        /// A runner with no input drops into a flagged pit under ordinary airborne physics, lands on the authored
        /// floor, runs it and climbs the far wall out through the automatic mantle.
        /// </summary>
        [Test]
        public void ARunnerFallsOntoAFlaggedFloorAndMantlesOut()
        {
            CourseDefinition    course     = FlaggedPitCourse(40, 46, -24);
            PitDefinition       pit        = course.Pits[0];
            TimeTrialSimulation simulation = new TimeTrialSimulation(course, Rules);

            bool landedOnTheFloor = false;
            bool mantled          = false;
            while (!simulation.IsOver)
            {
                simulation.Step();
                landedOnTheFloor |= simulation.Mode == TimeTrialMode.Running && simulation.Y == pit.FloorY && simulation.X >= pit.StartX && simulation.X < pit.EndX;
                mantled          |= landedOnTheFloor && simulation.EncounterPhase == "ledge-rise";
            }

            Assert.That(landedOnTheFloor, Is.True, "the runner never ran along the flagged floor");
            Assert.That(mantled, Is.True, "the runner never climbed the far wall out of the flagged pit");
            Assert.That(simulation.IsFinished, Is.True);
        }

        /// <summary>
        /// A flagged floor is one obstacle to the scripted runner, the same one a gap of the same width cut to the
        /// same depth is: the course is the same geometry either way, so the recording is the same press for press.
        /// </summary>
        [Test]
        public void TheScriptedRunnerJumpsAFlaggedPitAsItJumpsAGap()
        {
            CourseDefinition gap     = PitCourse(5);
            CourseDefinition flagged = FlaggedPitCourse(PitStartX, PitStartX + 5, -20, id: gap.Id.Value);

            Assert.That(flagged.Pits[0].FloorY, Is.EqualTo(gap.Pits[0].FloorY), "the two courses under test are not the same geometry");

            foreach (TimeTrialBotSkill skill in new[] { TimeTrialBotSkill.Cautious, TimeTrialBotSkill.Competent, TimeTrialBotSkill.Sharp })
            {
                TimeTrialRecording overGap     = TimeTrialBotDriver.Drive(gap, Rules, skill);
                TimeTrialRecording overFlagged = TimeTrialBotDriver.Drive(flagged, Rules, skill);

                Assert.That(overFlagged.Inputs.Count, Is.EqualTo(overGap.Inputs.Count), $"the {skill} runner met a different number of obstacles");
                for (int ndx = 0; ndx < overGap.Inputs.Count; ndx++)
                {
                    Assert.That(overFlagged.Inputs[ndx].Step, Is.EqualTo(overGap.Inputs[ndx].Step),$"the {skill} runner's input {ndx} moved");
                    Assert.That(overFlagged.Inputs[ndx].Pressed, Is.EqualTo(overGap.Inputs[ndx].Pressed), $"the {skill} runner's input {ndx} changed");
                }

                TimeTrialResult result = TimeTrialSimulation.Replay(flagged, Rules, overFlagged);
                Assert.That(result, Is.Not.Null, $"the {skill} runner never finished the flagged pit course");
            }

            TimeTrialRecording sharp = TimeTrialBotDriver.Drive(flagged, Rules, TimeTrialBotSkill.Sharp);
            Assert.That(sharp.Inputs, Has.Count.EqualTo(2), "the sharp runner jumps the flagged pit once, and not the face out of it as well");
        }

        /// <summary>
        /// A runner with no input drops into a spiked pit, comes down on the spikes at the floor's height and
        /// stumbles there, then runs on to the far wall and mantles out. The stumble is paid on top of the fall.
        /// </summary>
        [Test]
        public void ARunnerWhoFallsIntoASpikedPitStumblesThenMantlesOut()
        {
            CourseDefinition    course     = FlaggedPitCourse(40, 50, -24, hazards: new List<HazardSpan> { Spikes(40, 49) });
            CourseDefinition    bare       = FlaggedPitCourse(40, 50, -24);
            PitDefinition       pit        = course.Pits[0];
            HazardSpan          spikes     = course.Hazards[0];
            TimeTrialSimulation simulation = new TimeTrialSimulation(course, Rules);

            bool stumbledOnTheFloor = false;
            bool mantledAfter       = false;
            while (!simulation.IsOver)
            {
                simulation.Step();
                if (simulation.Mode == TimeTrialMode.Stumbling && !stumbledOnTheFloor)
                {
                    Assert.That(simulation.Y, Is.EqualTo(pit.FloorY), "the stumble was not at the pit floor's height");
                    Assert.That(simulation.X, Is.InRange(spikes.StartX, spikes.EndX), "the stumble was not on the spike bed");
                    stumbledOnTheFloor = true;
                }
                mantledAfter |= stumbledOnTheFloor && simulation.EncounterPhase == "ledge-rise";
            }

            Assert.That(stumbledOnTheFloor, Is.True, "the runner never stumbled on the spikes in the pit");
            Assert.That(mantledAfter, Is.True, "the runner never climbed out after the stumble");
            Assert.That(simulation.IsFinished, Is.True);

            TimeTrialResult spiked   = TimeTrialSimulation.Replay(course, Rules, new TimeTrialRecording());
            TimeTrialResult unspiked = TimeTrialSimulation.Replay(bare, Rules, new TimeTrialRecording());
            Assert.That(spiked.FinishTimeMs, Is.GreaterThan(unspiked.FinishTimeMs), "the spikes on the floor cost nothing to a runner who fell in");
        }

        /// <summary>
        /// The spikes on a pit floor are cleared with the pit, so every scripted level jumps a spiked pit exactly
        /// as it jumps a bare one, press for press.
        /// </summary>
        [Test]
        public void TheScriptedRunnerJumpsASpikedPitAsItJumpsABareOne()
        {
            // The spike bed after the pit is what a level that misses every nth obstacle would count differently.
            List<HazardSpan> afterOnly    = new List<HazardSpan> { Spikes(60, 62) };
            List<HazardSpan> withFloorBed = new List<HazardSpan> { Spikes(PitStartX + 1, PitStartX + 4), Spikes(60, 62) };
            CourseDefinition bare         = FlaggedPitCourse(PitStartX, PitStartX + 5, -20, id: "test-spiked-pit", hazards: afterOnly);
            CourseDefinition spiked       = FlaggedPitCourse(PitStartX, PitStartX + 5, -20, id: "test-spiked-pit", hazards: withFloorBed);

            foreach (TimeTrialBotSkill skill in new[] { TimeTrialBotSkill.Clumsy, TimeTrialBotSkill.Cautious, TimeTrialBotSkill.Competent, TimeTrialBotSkill.Sharp })
            {
                TimeTrialRecording overBare   = TimeTrialBotDriver.Drive(bare, Rules, skill);
                TimeTrialRecording overSpiked = TimeTrialBotDriver.Drive(spiked, Rules, skill);

                Assert.That(overSpiked.Inputs.Count, Is.EqualTo(overBare.Inputs.Count), $"the {skill} runner met a different number of obstacles");
                for (int ndx = 0; ndx < overBare.Inputs.Count; ndx++)
                {
                    Assert.That(overSpiked.Inputs[ndx].Step, Is.EqualTo(overBare.Inputs[ndx].Step), $"the {skill} runner's input {ndx} moved");
                    Assert.That(overSpiked.Inputs[ndx].Pressed, Is.EqualTo(overBare.Inputs[ndx].Pressed), $"the {skill} runner's input {ndx} changed");
                }

                Assert.That(TimeTrialSimulation.Replay(spiked, Rules, overSpiked), Is.Not.Null, $"the {skill} runner never finished the spiked pit course");
            }

            TimeTrialRecording sharp = TimeTrialBotDriver.Drive(spiked, Rules, TimeTrialBotSkill.Sharp);
            Assert.That(sharp.Inputs, Has.Count.EqualTo(4), "the sharp runner jumps the spiked pit once and the spike bed after it once");
        }

        #region Helpers

        static HazardSpan Spikes(int startX, int endX)
            => new HazardSpan(F32.FromInt(startX), F32.FromInt(endX));

        /// <summary>Flat ground with one pit of <paramref name="gapWidth"/> meters opening at <see cref="PitStartX"/>.</summary>
        static CourseDefinition PitCourse(int gapWidth)
            => new CourseDefinition(
                CourseId.FromString($"test-pit-{gapWidth}"), $"Pit {gapWidth} m",
                startX:  F32.Zero,
                finishX: F32.FromInt(80),
                ground: new List<GroundSegment>
                {
                    Segment(-12, PitStartX, 0),
                    Segment(PitStartX + gapWidth, 100, 0),
                },
                hazards: new List<HazardSpan>());

        /// <summary>Flat ground with nothing on it, for measuring a jump over level ground.</summary>
        static CourseDefinition LevelCourse()
            => new CourseDefinition(
                CourseId.FromString("test-level"), "Level ground",
                startX:  F32.Zero,
                finishX: F32.FromInt(80),
                ground: new List<GroundSegment> { Segment(-12, 100, 0) },
                hazards: new List<HazardSpan>());

        /// <summary>Plant the feet where the runner stands, hold to full charge and let go.</summary>
        void FullCharge(TimeTrialSimulation simulation)
        {
            simulation.ApplyInput(new TimeTrialInput(simulation.StepIndex, pressed: true));
            for (int step = 0; step < Rules.MaxChargeSteps; step++)
                simulation.Step();
            simulation.ApplyInput(new TimeTrialInput(simulation.StepIndex, pressed: false));
        }

        /// <summary>
        /// A recording that plants the feet where the sharpest scripted runner would in front of
        /// <paramref name="course"/>'s pit and spends the whole charge, pressing nothing else.
        /// </summary>
        TimeTrialRecording FullChargeAtTheNearLip(CourseDefinition course)
        {
            F32                 preJumpGap = TimeTrialBotStyle.For(TimeTrialBotSkill.Sharp).PreJumpGap;
            TimeTrialSimulation simulation = new TimeTrialSimulation(course, Rules);

            while (simulation.X + preJumpGap < course.Pits[0].StartX)
                simulation.Step();

            return new TimeTrialRecording(new List<TimeTrialInput>
            {
                new TimeTrialInput(simulation.StepIndex, pressed: true),
                new TimeTrialInput(simulation.StepIndex + Rules.MaxChargeSteps, pressed: false),
            });
        }

        static GroundSegment Segment(int startX, int endX, int topDecimeters)
            => new GroundSegment(F32.FromInt(startX), F32.FromInt(endX), F32.Ratio10(topDecimeters));

        static GroundSegment PitFloor(int startX, int endX, int topDecimeters)
            => new GroundSegment(F32.FromInt(startX), F32.FromInt(endX), F32.Ratio10(topDecimeters), isPit: true);

        /// <summary>Flat ground with one flagged pit floor from <paramref name="startX"/> to <paramref name="endX"/>, carrying any spike beds given.</summary>
        static CourseDefinition FlaggedPitCourse(int startX, int endX, int floorDecimeters, int farLipDecimeters = 0, string id = null, List<HazardSpan> hazards = null)
            => new CourseDefinition(
                CourseId.FromString(id ?? $"test-flagged-pit-{startX}-{endX}"), "Flagged pit",
                startX:  F32.Zero,
                finishX: F32.FromInt(80),
                ground: new List<GroundSegment>
                {
                    Segment(-12, startX, 0),
                    PitFloor(startX, endX, floorDecimeters),
                    Segment(endX, 100, farLipDecimeters),
                },
                hazards: hazards ?? new List<HazardSpan>());

        /// <summary>
        /// What climbing out of <paramref name="course"/>'s pit costs a run with no input: the steps spent
        /// rising up the wall, and in <paramref name="mantleSteps"/> the whole hang, rise and pull-up.
        /// </summary>
        int MantleSteps(CourseDefinition course, out int mantleSteps)
        {
            TimeTrialSimulation simulation = new TimeTrialSimulation(course, Rules);
            int                 riseSteps  = 0;

            mantleSteps = 0;

            while (!simulation.IsOver)
            {
                simulation.Step();
                if (simulation.Mode == TimeTrialMode.Traversing)
                    mantleSteps++;
                if (simulation.EncounterPhase == "ledge-rise")
                    riseSteps++;
            }

            Assert.That(simulation.IsFinished, Is.True, $"{course.Id}: a run with no input never reached the finish");
            Assert.That(riseSteps, Is.GreaterThan(0), $"{course.Id}: a run with no input never climbed out of the pit");
            return riseSteps;
        }

        #endregion // Helpers
    }
}
