using Metaplay.Core.Math;
using NUnit.Framework;
using System.Collections.Generic;

namespace Game.Logic.Tests
{
    /// <summary>
    /// The stretch between a climb's two torii, on a small purpose-built course: who takes the runner and when,
    /// what the button does while it has them, and the state the gate is crossed in. The climbs the maps carry
    /// are covered by <see cref="CourseComposerTests"/>.
    /// <para>
    /// The property the whole arrangement exists for is that the climb costs every runner the same and hands
    /// them all through the gate identically, so what a segment is won or lost on is the hazard section in
    /// front of it.
    /// </para>
    /// </summary>
    [TestFixture]
    public class ClimbSimulationTests
    {
        static TimeTrialRules Rules() => new TimeTrialRules();

        /// <summary>
        /// Ground at 0 m carrying a climb's bottom torii at 40 m and on to the foot of its slope at 43 m, the
        /// slope rising 7.5 m to 76 m, and a flat run-out carrying the gate at 85 m and the finish beyond it.
        /// </summary>
        static CourseDefinition ClimbCourse()
            => new CourseDefinition(
                CourseId.FromString("test-climb"), "Climb",
                startX:  F32.Zero,
                finishX: F32.FromInt(110),
                ground: new List<GroundSegment>
                {
                    new GroundSegment(F32.FromInt(-10), F32.FromInt(43), F32.Zero),
                    new GroundSegment(F32.FromInt(76), F32.FromInt(140), F32.Ratio10(75)),
                },
                hazards: new List<HazardSpan>(),
                encounters: new List<CourseEncounterDefinition>
                {
                    new ClimbDefinition("test-stair", F32.FromInt(40), F32.FromInt(76), F32.Ratio10(75), F32.FromInt(85)),
                });

        /// <summary>
        /// Two climbs and three segments, so the whole segment rule can be read off one run: the start line,
        /// two bottom torii that close a segment, the two gates above them that open the next, and the finish.
        /// </summary>
        static CourseDefinition TwoClimbCourse()
            => new CourseDefinition(
                CourseId.FromString("test-two-climbs"), "Two climbs",
                startX:  F32.Zero,
                finishX: F32.FromInt(200),
                ground: new List<GroundSegment>
                {
                    new GroundSegment(F32.FromInt(-10), F32.FromInt(43), F32.Zero),
                    new GroundSegment(F32.FromInt(76), F32.FromInt(123), F32.Ratio10(75)),
                    new GroundSegment(F32.FromInt(156), F32.FromInt(220), F32.FromInt(15)),
                },
                hazards: new List<HazardSpan>(),
                encounters: new List<CourseEncounterDefinition>
                {
                    new ClimbDefinition("test-lower-stair", F32.FromInt(40), F32.FromInt(76), F32.Ratio10(75), F32.FromInt(85)),
                    new ClimbDefinition("test-upper-stair", F32.FromInt(120), F32.FromInt(156), F32.FromInt(15), F32.FromInt(165)),
                });

        static ClimbDefinition Climb(CourseDefinition course) => course.Climbs[0];

        /// <summary>Step until <paramref name="predicate"/> holds or the run is over.</summary>
        static void AdvanceUntil(TimeTrialSimulation simulation, System.Func<TimeTrialSimulation, bool> predicate)
        {
            while (!simulation.IsOver && !predicate(simulation))
                simulation.Step();
        }

        [Test]
        public void TheClimbTakesTheRunnerAtItsBottomToriiAndLocksTheButton()
        {
            CourseDefinition    course     = ClimbCourse();
            TimeTrialSimulation simulation = new TimeTrialSimulation(course, Rules());

            Assert.That(simulation.IsInputLocked, Is.False, "the button is the player's before the climb");

            AdvanceUntil(simulation, sim => sim.Mode == TimeTrialMode.Climbing);

            Assert.That(simulation.Mode, Is.EqualTo(TimeTrialMode.Climbing));
            Assert.That(simulation.IsInputLocked, Is.True, "the climb takes the runner's hands off the button");
            Assert.That(simulation.X, Is.GreaterThanOrEqualTo(Climb(course).BottomX));

            // A press and a release on the slope are recorded, and neither does anything at all.
            F32 xBeforePress = simulation.X;
            simulation.ApplyInput(new TimeTrialInput(simulation.StepIndex, pressed: true));
            Assert.That(simulation.Mode, Is.EqualTo(TimeTrialMode.Climbing), "the press must not plant the runner's feet");

            simulation.Step();
            Assert.That(simulation.X, Is.GreaterThan(xBeforePress), "the climb keeps carrying the runner while the button is held");

            simulation.ApplyInput(new TimeTrialInput(simulation.StepIndex, pressed: false));
            Assert.That(simulation.Mode, Is.EqualTo(TimeTrialMode.Climbing), "the release must not launch a jump");
        }

        /// <summary>
        /// The bottom torii stands on flat ground: the ground below carries it and runs on
        /// <see cref="ClimbDefinition.FootFlatLength"/> to the foot of the slope, where it ends.
        /// </summary>
        [Test]
        public void TheBottomToriiStandsOnTheFlatBeforeTheSlope()
        {
            CourseDefinition course = ClimbCourse();
            ClimbDefinition  climb  = Climb(course);

            Assert.That(climb.SlopeStartX, Is.EqualTo(climb.BottomX + ClimbDefinition.FootFlatLength));

            int toriiGround = course.FindGroundIndexAt(climb.BottomX);
            Assert.That(toriiGround, Is.GreaterThanOrEqualTo(0), "the bottom torii stands on ground");
            Assert.That(course.Ground[toriiGround].EndX, Is.EqualTo(climb.SlopeStartX), "the ground under the torii runs on to the foot of the slope");
            Assert.That(course.FindGroundSegmentEndingAt(climb.SlopeStartX), Is.SameAs(course.Ground[toriiGround]));

            Assert.That(climb.SurfaceYAt(climb.BottomX, F32.Zero), Is.EqualTo(F32.Zero));
            Assert.That(climb.SurfaceYAt(climb.SlopeStartX, F32.Zero), Is.EqualTo(F32.Zero), "the slope starts at its foot, not at the torii");
            Assert.That(climb.SurfaceYAt(climb.SlopeStartX + F32.One, F32.Zero), Is.GreaterThan(F32.Zero));
            Assert.That(climb.SurfaceYAt(climb.TopX, F32.Zero), Is.EqualTo(climb.TopY));
        }

        /// <summary>
        /// The climb takes the runner at the torii and carries it along the flat, hands off, at running pace and
        /// on the ground's own height, and the slope starts rising only past its foot.
        /// </summary>
        [Test]
        public void TheClimbCarriesTheRunnerAlongTheFlatBeforeTheSlope()
        {
            CourseDefinition    course     = ClimbCourse();
            ClimbDefinition     climb      = Climb(course);
            TimeTrialRules      rules      = Rules();
            TimeTrialSimulation simulation = new TimeTrialSimulation(course, rules);

            AdvanceUntil(simulation, sim => sim.Mode == TimeTrialMode.Climbing);
            Assert.That(simulation.X, Is.LessThan(climb.SlopeStartX), "a running runner is taken on the flat");

            int stepsOnFlat = 0;
            while (!simulation.IsOver && simulation.X <= climb.SlopeStartX)
            {
                Assert.That(simulation.Mode, Is.EqualTo(TimeTrialMode.Climbing), "the flat is the climb's");
                Assert.That(simulation.IsInputLocked, Is.True, "the button does nothing on the flat");
                Assert.That(simulation.Y, Is.EqualTo(F32.Zero), "the flat is level");
                Assert.That(simulation.VelocityX, Is.EqualTo(rules.RunSpeed));
                Assert.That(simulation.EncounterProgress, Is.EqualTo(F32.Zero), "the slope's progress starts at its foot");
                simulation.Step();
                stepsOnFlat++;
            }

            Assert.That(stepsOnFlat, Is.GreaterThan(1), "the flat takes more than one step to cross");
            Assert.That(simulation.Mode, Is.EqualTo(TimeTrialMode.Climbing));
            Assert.That(simulation.Y, Is.GreaterThan(F32.Zero), "past its foot the slope rises");
        }

        /// <summary>
        /// A climb whose ground ends at its bottom torii, with no flat before the slope, is refused when the
        /// course is built, as is one whose flat starts past the torii.
        /// </summary>
        [Test]
        public void AClimbWithoutItsFlatIsRefused()
        {
            CourseDefinition Build(F32 belowStartX, F32 belowEndX)
                => new CourseDefinition(
                    CourseId.FromString("test-flatless-climb"), "Flatless climb",
                    startX:  F32.Zero,
                    finishX: F32.FromInt(110),
                    ground: new List<GroundSegment>
                    {
                        new GroundSegment(F32.FromInt(-10), belowStartX, F32.Zero),
                        new GroundSegment(belowStartX, belowEndX, F32.Zero),
                        new GroundSegment(F32.FromInt(76), F32.FromInt(140), F32.Ratio10(75)),
                    },
                    hazards: new List<HazardSpan>(),
                    encounters: new List<CourseEncounterDefinition>
                    {
                        new ClimbDefinition("test-stair", F32.FromInt(40), F32.FromInt(76), F32.Ratio10(75), F32.FromInt(85)),
                    });

            Assert.That(() => Build(F32.FromInt(20), F32.FromInt(40)), Throws.ArgumentException, "the ground ends at the torii");
            Assert.That(() => Build(F32.FromInt(41), F32.FromInt(43)), Throws.ArgumentException, "the torii stands off the flat that reaches the slope");
            Assert.That(() => Build(F32.FromInt(20), F32.FromInt(43)), Throws.Nothing, "the torii on the flat that reaches the slope is sound");
        }

        /// <summary>
        /// The slope costs no pace. The runner is carried up it at the running speed it arrived with, so a
        /// climb is time nobody can play rather than time everybody loses.
        /// </summary>
        [Test]
        public void TheSlopeIsTakenAtRunningPace()
        {
            CourseDefinition    course     = ClimbCourse();
            TimeTrialRules      rules      = Rules();
            TimeTrialSimulation simulation = new TimeTrialSimulation(course, rules);

            AdvanceUntil(simulation, sim => sim.Mode == TimeTrialMode.Climbing);

            Assert.That(simulation.VelocityX, Is.EqualTo(rules.RunSpeed), "the capture sets the runner to full pace");

            F32 xAtStart     = simulation.X;
            int stepsOnSlope = 0;
            while (!simulation.IsOver && simulation.Mode == TimeTrialMode.Climbing)
            {
                simulation.Step();
                stepsOnSlope++;
            }

            F32 covered  = simulation.X - xAtStart;
            F32 expected = rules.RunSpeed * F32.Ratio(stepsOnSlope, TimeTrialConstants.StepsPerSecond);

            // The last step is clamped to the top of the slope, so the distance covered is at most what running
            // pace would have covered and within one step of it.
            Assert.That(covered, Is.LessThanOrEqualTo(expected));
            Assert.That(expected - covered, Is.LessThan(rules.RunSpeed * F32.Ratio(1, TimeTrialConstants.StepsPerSecond)));
            Assert.That(simulation.VelocityX, Is.EqualTo(rules.RunSpeed), "the run-out starts at full pace, with nothing to build back up");
        }

        /// <summary>
        /// The run-out is what makes a gate crossing identical for everyone: on the ground, at running pace,
        /// with the button handed back on the far side of it.
        /// </summary>
        [Test]
        public void TheGateIsCrossedOnTheGroundAtRunningPaceWithInputHandedBack()
        {
            CourseDefinition    course     = ClimbCourse();
            TimeTrialRules      rules      = Rules();
            TimeTrialSimulation simulation = new TimeTrialSimulation(course, rules);

            AdvanceUntil(simulation, sim => sim.GateTimesMs.Count > 0);

            Assert.That(simulation.GateTimesMs, Has.Count.EqualTo(1), "the run-out must carry the runner over the gate");
            Assert.That(simulation.Mode, Is.EqualTo(TimeTrialMode.Running), "a gate is never crossed mid-jump or mid-climb");
            Assert.That(simulation.Y, Is.EqualTo(Climb(course).TopY), "the gate stands on the run-out's own ground");
            Assert.That(simulation.VelocityX, Is.EqualTo(rules.RunSpeed), "everyone opens the next segment at the same pace");
            Assert.That(simulation.IsInputLocked, Is.False, "the gate hands the button back");
            Assert.That(simulation.GateTimesMs[0], Is.GreaterThan(0));

            // And the button works again on the far side of it.
            simulation.ApplyInput(new TimeTrialInput(simulation.StepIndex, pressed: true));
            Assert.That(simulation.Mode, Is.EqualTo(TimeTrialMode.Charging));
        }

        /// <summary>
        /// Every runner pays the same for the slope whatever they did in front of it, which is the reason a
        /// segment is raced rather than a whole course.
        /// </summary>
        [Test]
        public void TheClimbCostsEveryRunnerTheSameFromItsFootToItsGate()
        {
            CourseDefinition course = ClimbCourse();
            TimeTrialRules   rules  = Rules();

            TimeTrialSimulation plain   = new TimeTrialSimulation(course, rules);
            TimeTrialSimulation jumping = new TimeTrialSimulation(course, rules);

            // One runner arrives running. The other arrives in the air from a jump taken just short of the foot.
            AdvanceUntil(jumping, sim => sim.X > F32.FromInt(36));
            jumping.ApplyInput(new TimeTrialInput(jumping.StepIndex, pressed: true));
            for (int step = 0; step < 30; step++)
                jumping.Step();
            jumping.ApplyInput(new TimeTrialInput(jumping.StepIndex, pressed: false));

            int PassageSteps(TimeTrialSimulation simulation)
            {
                AdvanceUntil(simulation, sim => sim.Mode == TimeTrialMode.Climbing);
                int enteredAt = simulation.StepIndex;
                AdvanceUntil(simulation, sim => sim.GateTimesMs.Count > 0);
                return simulation.StepIndex - enteredAt;
            }

            Assert.That(PassageSteps(jumping), Is.EqualTo(PassageSteps(plain)),
                "how a runner arrived at the foot of a climb must not change what the climb costs it");
        }

        /// <summary>
        /// The bottom torii closes a segment, so the moment it is reached is timed inside the step it is
        /// reached on, exactly as a gate and the finish line are.
        /// </summary>
        [Test]
        public void TheBottomToriiIsTimedOnTheStepTheClimbTakesTheRunner()
        {
            CourseDefinition    course     = ClimbCourse();
            TimeTrialSimulation simulation = new TimeTrialSimulation(course, Rules());

            Assert.That(simulation.ClimbEntryTimesMs, Is.Empty);
            Assert.That(simulation.NextClimbIndex, Is.Zero);

            int beforeMs = 0;
            while (!simulation.IsOver && simulation.ClimbEntryTimesMs.Count == 0)
            {
                beforeMs = simulation.ElapsedMs;
                simulation.Step();
            }

            Assert.That(simulation.Mode, Is.EqualTo(TimeTrialMode.Climbing), "the torii is timed where the climb takes the runner");
            Assert.That(simulation.ClimbEntryTimesMs, Has.Count.EqualTo(1));
            Assert.That(simulation.NextClimbIndex, Is.EqualTo(1));
            Assert.That(simulation.ClimbEntryTimesMs[0], Is.InRange(beforeMs, simulation.ElapsedMs),
                "the crossing is timed inside the step it happened on");

            AdvanceUntil(simulation, sim => sim.GateTimesMs.Count > 0);
            Assert.That(simulation.ClimbEntryTimesMs[0], Is.LessThan(simulation.GateTimesMs[0]),
                "a climb's bottom torii is behind its gate on the clock as well as on the course");
        }

        /// <summary>
        /// A jump that carries past the foot of the slope is caught on it rather than carried over it, set down on
        /// the slope where it got to, so nobody can skip the stretch the course owns. The segment behind is still
        /// closed where the bottom torii stands rather than where the runner came down.
        /// </summary>
        [Test]
        public void AMidAirCaptureStillTimesTheBottomTorii()
        {
            CourseDefinition    course     = ClimbCourse();
            TimeTrialSimulation simulation = new TimeTrialSimulation(course, Rules());

            AdvanceUntil(simulation, sim => sim.X > F32.FromInt(36));

            simulation.ApplyInput(new TimeTrialInput(simulation.StepIndex, pressed: true));
            for (int step = 0; step < 40; step++)
                simulation.Step();
            simulation.ApplyInput(new TimeTrialInput(simulation.StepIndex, pressed: false));

            int           beforeMs   = 0;
            TimeTrialMode modeBefore = simulation.Mode;
            while (!simulation.IsOver && simulation.ClimbEntryTimesMs.Count == 0)
            {
                beforeMs   = simulation.ElapsedMs;
                modeBefore = simulation.Mode;
                simulation.Step();
            }

            Assert.That(modeBefore, Is.EqualTo(TimeTrialMode.Airborne), "the runner was still in the air when the torii went by");
            Assert.That(simulation.Mode, Is.EqualTo(TimeTrialMode.Climbing), "a jump over the bottom torii is still claimed by the climb");
            Assert.That(simulation.X, Is.InRange(Climb(course).BottomX, Climb(course).TopX), "the runner was set down on the slope");
            Assert.That(simulation.Y, Is.EqualTo(Climb(course).SurfaceYAt(simulation.X, F32.Zero)), "and stands on the slope, not above it");
            Assert.That(simulation.ClimbEntryTimesMs, Has.Count.EqualTo(1));
            Assert.That(simulation.ClimbEntryTimesMs[0], Is.InRange(beforeMs, simulation.ElapsedMs),
                "the torii is timed inside the step the runner reached it on, in the air or not");
        }

        /// <summary>
        /// The segment rule on a course with two climbs: the first segment ends at the first bottom torii, the
        /// second runs from the first gate to the second bottom torii, and the last runs from the second gate
        /// to the finish. The two climbs in between belong to no segment, which is what makes the segment times
        /// and the climb times add up to the whole run.
        /// </summary>
        [Test]
        public void SegmentsRunFromEachGateToTheNextBottomToriiAndTheClimbsBelongToNobody()
        {
            CourseDefinition course = TwoClimbCourse();
            TimeTrialResult  result = TimeTrialSimulation.Replay(course, Rules(), new TimeTrialRecording());

            Assert.That(result, Is.Not.Null);
            Assert.That(result.ClimbEntryTimesMs, Has.Count.EqualTo(2));
            Assert.That(result.GateTimesMs, Has.Count.EqualTo(2));
            Assert.That(result.SegmentCount, Is.EqualTo(3));

            Assert.That(result.SegmentTimeMs(0), Is.EqualTo(result.ClimbEntryTimesMs[0]),
                "the first segment runs from the start line to the first bottom torii");
            Assert.That(result.SegmentTimeMs(1), Is.EqualTo(result.ClimbEntryTimesMs[1] - result.GateTimesMs[0]),
                "the second runs from the first gate to the second bottom torii");
            Assert.That(result.SegmentTimeMs(2), Is.EqualTo(result.FinishTimeMs - result.GateTimesMs[1]),
                "the last runs from the second gate to the finish line");

            int raced = 0;
            for (int segment = 0; segment < result.SegmentCount; segment++)
                raced += result.SegmentTimeMs(segment);

            int climbed = 0;
            for (int climb = 0; climb < result.ClimbEntryTimesMs.Count; climb++)
                climbed += result.GateTimesMs[climb] - result.ClimbEntryTimesMs[climb];

            Assert.That(raced + climbed, Is.EqualTo(result.FinishTimeMs),
                "every millisecond of a run is either raced on a segment or spent between a climb's torii");
        }
    }
}
