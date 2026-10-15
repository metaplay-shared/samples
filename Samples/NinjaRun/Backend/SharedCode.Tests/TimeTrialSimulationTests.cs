using Metaplay.Core.Math;
using NUnit.Framework;
using System.Collections.Generic;

namespace Game.Logic.Tests
{
    /// <summary>
    /// The deterministic time trial simulation, exercised on small purpose-built courses rather than on maps,
    /// so each rule is visible on its own. The maps the game actually plays are covered by
    /// <see cref="CourseComposerTests"/>.
    /// </summary>
    [TestFixture]
    public class TimeTrialSimulationTests
    {
        static TimeTrialRules Rules() => new TimeTrialRules();

        /// <summary>One unbroken stretch of ground from -10 m to 400 m, with the finish at <paramref name="finishX"/>.</summary>
        static CourseDefinition FlatCourse(int finishX = 120)
            => new CourseDefinition(
                CourseId.FromString("test-flat"), "Flat",
                startX:  F32.Zero,
                finishX: F32.FromInt(finishX),
                ground:  new List<GroundSegment> { Segment(-10, 400, 0) },
                hazards: new List<HazardSpan>());

        static GroundSegment Segment(int startX, int endX, int topDecimeters)
            => new GroundSegment(F32.FromInt(startX), F32.FromInt(endX), F32.Ratio10(topDecimeters));

        static HazardSpan Spikes(int startX, int endX)
            => new HazardSpan(F32.FromInt(startX), F32.FromInt(endX));

        /// <summary>Run <paramref name="steps"/> steps with no input.</summary>
        static void Advance(TimeTrialSimulation simulation, int steps)
        {
            for (int ndx = 0; ndx < steps; ndx++)
                simulation.Step();
        }

        #region Input and jumping

        /// <summary>
        /// A flying start: step 0 has the runner on the start line already at running pace, so the first step
        /// covers a full step of running and the clock starts on a runner in full stride.
        /// </summary>
        [Test]
        public void TheRunStartsOnTheStartLineAtRunningPace()
        {
            TimeTrialRules      rules      = Rules();
            TimeTrialSimulation simulation = new TimeTrialSimulation(FlatCourse(), rules);

            Assert.That(simulation.StepIndex, Is.EqualTo(0));
            Assert.That(simulation.ElapsedMs, Is.EqualTo(0));
            Assert.That(simulation.X, Is.EqualTo(F32.Zero), "the runner starts on the start line");
            Assert.That(simulation.Mode, Is.EqualTo(TimeTrialMode.Running));
            Assert.That(simulation.VelocityX, Is.EqualTo(rules.RunSpeed), "the runner starts at running pace");

            simulation.Step();

            Assert.That(simulation.VelocityX, Is.EqualTo(rules.RunSpeed));
            Assert.That(simulation.X, Is.EqualTo(rules.RunSpeed * F32.Ratio(1, TimeTrialConstants.StepsPerSecond)),
                "the first step is a full step of running");
        }

        /// <summary>A Skill that changes the running pace starts its runner at that pace.</summary>
        [Test]
        public void TheFlyingStartIsTheRunnersOwnPace()
        {
            TimeTrialRules rules = Rules();
            rules.RunSpeed = F32.FromInt(10);

            TimeTrialSimulation simulation = new TimeTrialSimulation(FlatCourse(), rules);

            Assert.That(simulation.VelocityX, Is.EqualTo(F32.FromInt(10)));
        }

        [Test]
        public void PressingWhileRunningStopsTheRunnerDead()
        {
            TimeTrialSimulation simulation = new TimeTrialSimulation(FlatCourse(), Rules());
            Advance(simulation, 60);

            Assert.That(simulation.VelocityX, Is.GreaterThan(F32.Zero), "the runner should be up to speed after a second");

            simulation.ApplyInput(new TimeTrialInput(simulation.StepIndex, pressed: true));

            Assert.That(simulation.Mode, Is.EqualTo(TimeTrialMode.Charging));
            Assert.That(simulation.VelocityX, Is.EqualTo(F32.Zero));

            F32 xAtPress = simulation.X;
            Advance(simulation, 10);
            Assert.That(simulation.X, Is.EqualTo(xAtPress), "charging holds the runner in place");
        }

        [Test]
        public void ALongerChargeLaunchesFurther()
        {
            F32 shortJump = JumpDistance(chargeSteps: 10);
            F32 longJump  = JumpDistance(chargeSteps: 40);

            Assert.That(longJump, Is.GreaterThan(shortJump));
        }

        /// <summary>How far one jump carries the runner, from the press to the landing.</summary>
        static F32 JumpDistance(int chargeSteps)
        {
            TimeTrialSimulation simulation = new TimeTrialSimulation(FlatCourse(300), Rules());
            Advance(simulation, 120);

            F32 launchX = simulation.X;
            simulation.ApplyInput(new TimeTrialInput(simulation.StepIndex, pressed: true));
            Advance(simulation, chargeSteps);
            simulation.ApplyInput(new TimeTrialInput(simulation.StepIndex, pressed: false));

            Assert.That(simulation.Mode, Is.EqualTo(TimeTrialMode.Airborne));

            while (simulation.Mode == TimeTrialMode.Airborne && !simulation.IsOver)
                simulation.Step();

            Assert.That(simulation.Mode, Is.EqualTo(TimeTrialMode.Running), "a jump on flat ground lands the runner back on its feet");
            return simulation.X - launchX;
        }

        [Test]
        public void HoldingPastFullChargeLaunchesOnItsOwn()
        {
            TimeTrialRules      rules      = Rules();
            TimeTrialSimulation simulation = new TimeTrialSimulation(FlatCourse(300), rules);

            simulation.ApplyInput(new TimeTrialInput(0, pressed: true));
            Advance(simulation, rules.MaxChargeSteps);
            Assert.That(simulation.Mode, Is.EqualTo(TimeTrialMode.Charging), "full charge is reached but not passed yet");

            simulation.Step();
            Assert.That(simulation.Mode, Is.EqualTo(TimeTrialMode.Airborne), "holding past full charge launches by itself");
        }

        [Test]
        public void TheForcedLaunchIsTheSameAsReleasingAtFullCharge()
        {
            TimeTrialRules rules = Rules();

            TimeTrialSimulation held = new TimeTrialSimulation(FlatCourse(300), rules);
            held.ApplyInput(new TimeTrialInput(0, pressed: true));
            Advance(held, rules.MaxChargeSteps + 1);

            TimeTrialSimulation released = new TimeTrialSimulation(FlatCourse(300), rules);
            released.ApplyInput(new TimeTrialInput(0, pressed: true));
            Advance(released, rules.MaxChargeSteps);
            released.ApplyInput(new TimeTrialInput(released.StepIndex, pressed: false));
            released.Step();

            Assert.That(held.X, Is.EqualTo(released.X));
            Assert.That(held.Y, Is.EqualTo(released.Y));
            Assert.That(held.VelocityX, Is.EqualTo(released.VelocityX));
            Assert.That(held.VelocityY, Is.EqualTo(released.VelocityY));
        }

        [Test]
        public void LandingKeepsTheSpeedTheJumpCarried()
        {
            TimeTrialRules      rules      = Rules();
            TimeTrialSimulation simulation = new TimeTrialSimulation(FlatCourse(300), rules);
            Advance(simulation, 120);

            // A full-charge launch at the configured angle carries more horizontal speed than running does.
            simulation.ApplyInput(new TimeTrialInput(simulation.StepIndex, pressed: true));
            Advance(simulation, rules.MaxChargeSteps);
            simulation.ApplyInput(new TimeTrialInput(simulation.StepIndex, pressed: false));
            Assert.That(simulation.VelocityX, Is.GreaterThan(rules.RunSpeed));

            while (simulation.Mode == TimeTrialMode.Airborne && !simulation.IsOver)
                simulation.Step();

            Assert.That(simulation.Mode, Is.EqualTo(TimeTrialMode.Running));
            Assert.That(simulation.VelocityX, Is.GreaterThan(rules.RunSpeed), "the landing keeps the momentum the jump carried");
            Assert.That(simulation.VelocityY, Is.EqualTo(F32.Zero));
            Assert.That(simulation.EncounterPhase, Is.Empty);

            // And the ground then pulls it back down to the running pace.
            Advance(simulation, 120);
            Assert.That(simulation.VelocityX, Is.EqualTo(rules.RunSpeed));
        }

        /// <summary>
        /// Tap-jump on flat ground and return the step the landing happens on: the <see cref="TimeTrialSimulation.StepIndex"/>
        /// before the <see cref="TimeTrialSimulation.Step"/> that touches down. The tap is pressed and released
        /// on step 60.
        /// </summary>
        static int TapJumpLandingStep(TimeTrialRules rules)
        {
            TimeTrialSimulation simulation = StartTapJump(rules);
            while (simulation.Mode == TimeTrialMode.Airborne)
                simulation.Step();
            return simulation.StepIndex - 1;
        }

        static TimeTrialSimulation StartTapJump(TimeTrialRules rules)
        {
            TimeTrialSimulation simulation = new TimeTrialSimulation(FlatCourse(300), rules);
            Advance(simulation, 60);
            simulation.ApplyInput(new TimeTrialInput(simulation.StepIndex, pressed: true));
            simulation.ApplyInput(new TimeTrialInput(simulation.StepIndex, pressed: false));
            Assert.That(simulation.Mode, Is.EqualTo(TimeTrialMode.Airborne));
            return simulation;
        }

        /// <summary>Tap-jump, press in the air <paramref name="stepsBeforeLanding"/> steps before touchdown and hold, and step through the landing.</summary>
        static TimeTrialSimulation PressBeforeLanding(TimeTrialRules rules, int stepsBeforeLanding)
        {
            int                 landingStep = TapJumpLandingStep(rules);
            TimeTrialSimulation simulation  = StartTapJump(rules);
            Advance(simulation, landingStep - stepsBeforeLanding - simulation.StepIndex);

            simulation.ApplyInput(new TimeTrialInput(simulation.StepIndex, pressed: true));
            Assert.That(simulation.Mode, Is.EqualTo(TimeTrialMode.Airborne), "the press is made in the air");

            Advance(simulation, stepsBeforeLanding + 1);
            return simulation;
        }

        [Test]
        public void APressJustBeforeLandingHeldThroughItStartsTheCharge()
        {
            TimeTrialRules      rules      = Rules();
            TimeTrialSimulation simulation = PressBeforeLanding(rules, stepsBeforeLanding: rules.JumpBufferSteps - 1);

            Assert.That(simulation.Mode, Is.EqualTo(TimeTrialMode.Charging), "the landing plants the feet");
            Assert.That(simulation.VelocityX, Is.EqualTo(F32.Zero));
            Assert.That(simulation.Y, Is.EqualTo(F32.Zero), "the charge starts on the ground");

            simulation.ApplyInput(new TimeTrialInput(simulation.StepIndex, pressed: false));
            Assert.That(simulation.Mode, Is.EqualTo(TimeTrialMode.Airborne), "releasing the buffered charge jumps");
        }

        [Test]
        public void APressTooLongBeforeLandingIsIgnored()
        {
            TimeTrialRules      rules      = Rules();
            TimeTrialSimulation simulation = PressBeforeLanding(rules, stepsBeforeLanding: rules.JumpBufferSteps);

            Assert.That(simulation.Mode, Is.EqualTo(TimeTrialMode.Running));
        }

        [Test]
        public void APressReleasedBeforeLandingIsIgnored()
        {
            TimeTrialRules      rules       = Rules();
            int                 landingStep = TapJumpLandingStep(rules);
            TimeTrialSimulation simulation  = StartTapJump(rules);
            Advance(simulation, landingStep - 2 - simulation.StepIndex);

            simulation.ApplyInput(new TimeTrialInput(simulation.StepIndex, pressed: true));
            simulation.Step();
            simulation.ApplyInput(new TimeTrialInput(simulation.StepIndex, pressed: false));
            Advance(simulation, 2);

            Assert.That(simulation.Mode, Is.EqualTo(TimeTrialMode.Running));
        }

        [Test]
        public void AZeroJumpBufferIgnoresEveryPressInTheAir()
        {
            TimeTrialRules rules = Rules();
            rules.JumpBufferSteps = 0;
            TimeTrialSimulation simulation = PressBeforeLanding(rules, stepsBeforeLanding: 0);

            Assert.That(simulation.Mode, Is.EqualTo(TimeTrialMode.Running));
        }

        #endregion

        #region Hazards, pits and ledges

        [Test]
        public void SpikesTripTheRunnerOnceAndOnlyOnce()
        {
            CourseDefinition course = new CourseDefinition(
                CourseId.FromString("test-spikes"), "Spikes",
                startX:  F32.Zero,
                finishX: F32.FromInt(60),
                ground:  new List<GroundSegment> { Segment(-10, 100, 0) },
                hazards: new List<HazardSpan> { Spikes(20, 26) });

            TimeTrialSimulation simulation  = new TimeTrialSimulation(course, Rules());
            TimeTrialMode       previousMode = simulation.Mode;
            int                 stumbles     = 0;

            while (!simulation.IsOver)
            {
                simulation.Step();
                if (simulation.Mode == TimeTrialMode.Stumbling && previousMode != TimeTrialMode.Stumbling)
                    stumbles++;
                previousMode = simulation.Mode;
            }

            Assert.That(simulation.IsFinished, Is.True, "spikes cost time, they do not end the run");
            Assert.That(stumbles, Is.EqualTo(1), "the runner walks out of the bed it stumbled in without tripping again");
        }

        [Test]
        public void FallingIntoAPitLandsOnItsFloorAndMantlesTheFarWall()
        {
            CourseDefinition course = new CourseDefinition(
                CourseId.FromString("test-pit"), "Pit",
                startX:  F32.Zero,
                finishX: F32.FromInt(60),
                ground:  new List<GroundSegment> { Segment(-10, 20, 0), Segment(32, 100, 0) },
                hazards: new List<HazardSpan>());

            Assert.That(course.Pits, Has.Count.EqualTo(1));
            PitDefinition pit = course.Pits[0];

            TimeTrialSimulation simulation = new TimeTrialSimulation(course, Rules());

            while (simulation.Y > pit.FloorY && !simulation.IsOver)
                simulation.Step();

            Assert.That(simulation.Mode, Is.EqualTo(TimeTrialMode.Running), "the runner lands on the pit floor and runs along it");
            Assert.That(simulation.Y, Is.EqualTo(pit.FloorY));
            Assert.That(simulation.X, Is.InRange(pit.StartX, pit.EndX));
            Assert.That(simulation.VelocityX, Is.GreaterThan(F32.Zero), "the fall keeps the speed it carried in");

            while (simulation.Mode != TimeTrialMode.Traversing && !simulation.IsOver)
                simulation.Step();

            Assert.That(simulation.EncounterPhase, Is.EqualTo("ledge-hang"), "the far wall is climbed out of by the automatic mantle");

            while (!simulation.IsOver)
                simulation.Step();

            Assert.That(simulation.IsFinished, Is.True, "a pit costs time, it does not end the run");
        }

        [Test]
        public void RunningIntoARaisedLedgeMantlesOntoIt()
        {
            CourseDefinition course = new CourseDefinition(
                CourseId.FromString("test-ledge"), "Ledge",
                startX:  F32.Zero,
                finishX: F32.FromInt(60),
                ground:  new List<GroundSegment> { Segment(-10, 20, 0), Segment(20, 100, 15) },
                hazards: new List<HazardSpan>());

            Assert.That(course.Pits, Is.Empty, "two touching segments leave no gap to cut a pit into");
            Assert.That(course.Encounters, Has.Count.EqualTo(1), "the step up carries a face of its own");

            LedgeGrabDefinition step = (LedgeGrabDefinition)course.Encounters[0];
            Assert.Multiple(() =>
            {
                Assert.That(step.Id, Does.StartWith(CourseDefinition.StepLedgeIdPrefix));
                Assert.That(step.X, Is.EqualTo(F32.FromInt(20)));
                Assert.That(step.BottomY, Is.EqualTo(F32.Zero));
                Assert.That(step.TopY, Is.EqualTo(F32.Ratio10(15)));
                Assert.That(step.MinimumGrabY, Is.EqualTo(F32.Zero), "the whole face of a step up is catchable");
            });

            TimeTrialSimulation simulation = new TimeTrialSimulation(course, Rules());

            while (simulation.Mode != TimeTrialMode.Traversing && !simulation.IsOver)
                simulation.Step();

            Assert.That(simulation.EncounterPhase, Is.EqualTo("ledge-hang"), "running into the side of the ledge catches it");
            Assert.That(simulation.BodyRight, Is.EqualTo(step.X));
            Assert.That(simulation.Y, Is.EqualTo(F32.Zero), "the catch is where the runner already stood, with no snap");

            while (simulation.Mode == TimeTrialMode.Traversing && !simulation.IsOver)
                simulation.Step();

            Assert.That(simulation.Mode, Is.EqualTo(TimeTrialMode.Running));
            Assert.That(simulation.Y, Is.EqualTo(F32.Ratio10(15)), "the runner ends up standing on top of the ledge");
            Assert.That(simulation.X, Is.EqualTo(step.LandingX));

            while (!simulation.IsOver)
                simulation.Step();

            Assert.That(simulation.IsFinished, Is.True);
        }

        [Test]
        public void SteppingDownOffALedgeFallsToTheGroundBelow()
        {
            CourseDefinition course = new CourseDefinition(
                CourseId.FromString("test-drop"), "Drop",
                startX:  F32.Zero,
                finishX: F32.FromInt(60),
                ground:  new List<GroundSegment> { Segment(-10, 20, 20), Segment(20, 100, 0) },
                hazards: new List<HazardSpan>());

            TimeTrialSimulation simulation = new TimeTrialSimulation(course, Rules());

            while (simulation.Mode != TimeTrialMode.Airborne && !simulation.IsOver)
                simulation.Step();

            Assert.That(simulation.Mode, Is.EqualTo(TimeTrialMode.Airborne), "the runner leaves the edge rather than snapping down");

            while (simulation.Mode == TimeTrialMode.Airborne && !simulation.IsOver)
                simulation.Step();

            Assert.That(simulation.Mode, Is.EqualTo(TimeTrialMode.Running));
            Assert.That(simulation.Y, Is.EqualTo(F32.Zero), "and lands on the lower ground");
        }

        #endregion

        #region Finishing

        [Test]
        public void TheFinishTimeLiesInsideTheStepTheLineWasCrossedOn()
        {
            TimeTrialResult result = TimeTrialSimulation.Replay(FlatCourse(), Rules(), new TimeTrialRecording());

            Assert.That(result, Is.Not.Null);
            Assert.That(result.FinishTimeMs, Is.GreaterThanOrEqualTo(TimeTrialConstants.StepsToMilliseconds(result.Steps - 1)));
            Assert.That(result.FinishTimeMs, Is.LessThanOrEqualTo(TimeTrialConstants.StepsToMilliseconds(result.Steps)));
        }

        [Test]
        public void TheFinishTimeIsFinerThanAWholeStep()
        {
            // Two finish lines a fraction of a step's travel apart. Timed only to whole steps these would read
            // as the same result. Interpolating inside the crossing step separates them without ever spanning
            // a whole step.
            TimeTrialResult nearer  = TimeTrialSimulation.Replay(FlatCourse(120), Rules(), new TimeTrialRecording());
            TimeTrialResult further = TimeTrialSimulation.Replay(
                new CourseDefinition(
                    CourseId.FromString("test-flat"), "Flat",
                    startX:  F32.Zero,
                    finishX: F32.FromInt(120) + F32.Ratio100(6),
                    ground:  new List<GroundSegment> { Segment(-10, 400, 0) },
                    hazards: new List<HazardSpan>()),
                Rules(),
                new TimeTrialRecording());

            int difference = further.FinishTimeMs - nearer.FinishTimeMs;

            Assert.That(difference, Is.GreaterThan(0), "moving the line further out must cost time");
            Assert.That(difference, Is.LessThan(1000 / TimeTrialConstants.StepsPerSecond), "and less than a whole step");
        }

        [Test]
        public void ARunThatNeverReachesTheFinishProducesNoResult()
        {
            // A finish line further out than three minutes of running reaches. The step budget ends the run and
            // there is no time to record, which is what a submission without a finish comes to.
            CourseDefinition course = new CourseDefinition(
                CourseId.FromString("test-unreachable"), "Unreachable",
                startX:  F32.Zero,
                finishX: F32.FromInt(5_000),
                ground:  new List<GroundSegment> { Segment(-10, 20_000, 0) },
                hazards: new List<HazardSpan>());

            TimeTrialResult result = TimeTrialSimulation.Replay(course, Rules(), new TimeTrialRecording());

            Assert.That(result, Is.Null);
        }

        #endregion

        #region Live play and the server's own run

        [Test]
        public void PlayingLiveAndRunningTheRecordingAgreeExactly()
        {
            // The whole authority story rests on this: the client steps the simulation as the player presses,
            // and the server takes only the recording and arrives at the same time.
            CourseDefinition course = TestCourses.PracticeMap;
            TimeTrialRules   rules  = Rules();

            TimeTrialSimulation  live   = new TimeTrialSimulation(course, rules);
            List<TimeTrialInput> inputs = new List<TimeTrialInput>();

            // A fixed pattern of holds, so the run covers charging, launching, landing and the pits fallen into.
            int[] pressSteps  = { 30, 200, 420, 700, 1010 };
            int[] holdLengths = { 12, 45,  25,  5,   38   };
            int   nextJump    = 0;
            int   releaseAt   = -1;

            while (!live.IsOver)
            {
                if (releaseAt < 0 && nextJump < pressSteps.Length && live.StepIndex >= pressSteps[nextJump] && live.Mode == TimeTrialMode.Running)
                {
                    TimeTrialInput press = new TimeTrialInput(live.StepIndex, pressed: true);
                    inputs.Add(press);
                    live.ApplyInput(press);
                    releaseAt = live.StepIndex + holdLengths[nextJump];
                    nextJump++;
                }
                else if (live.StepIndex == releaseAt)
                {
                    TimeTrialInput release = new TimeTrialInput(live.StepIndex, pressed: false);
                    inputs.Add(release);
                    live.ApplyInput(release);
                    releaseAt = -1;
                }

                live.Step();
            }

            TimeTrialRecording recording = new TimeTrialRecording(inputs);
            Assert.That(TimeTrialRecording.IsWellFormed(recording), Is.True, "live play must produce a recording the server accepts");

            TimeTrialResult replayed = TimeTrialSimulation.Replay(course, rules, recording);

            Assert.That(live.Result, Is.Not.Null);
            Assert.That(replayed, Is.Not.Null);
            Assert.That(replayed.FinishTimeMs, Is.EqualTo(live.Result.FinishTimeMs));
            Assert.That(replayed.Steps, Is.EqualTo(live.Result.Steps));
        }

        #endregion

        #region Recording validity

        [Test]
        public void AWellFormedRecordingIsAccepted()
        {
            TimeTrialRecording recording = new TimeTrialRecording(new List<TimeTrialInput>
            {
                new TimeTrialInput(10, true),
                new TimeTrialInput(40, false),
                new TimeTrialInput(40, true),   // a tap on the same step is fine
                new TimeTrialInput(41, false),
            });

            Assert.That(TimeTrialRecording.IsWellFormed(recording), Is.True);
        }

        [Test]
        public void AnEmptyRecordingIsAccepted()
        {
            Assert.That(TimeTrialRecording.IsWellFormed(new TimeTrialRecording()), Is.True, "playing a course without touching the button is a run");
        }

        [Test]
        public void ARecordingWhoseStepsRunBackwardsIsRejected()
        {
            TimeTrialRecording recording = new TimeTrialRecording(new List<TimeTrialInput>
            {
                new TimeTrialInput(40, true),
                new TimeTrialInput(10, false),
            });

            Assert.That(TimeTrialRecording.IsWellFormed(recording), Is.False);
        }

        [Test]
        public void ARecordingThatDoesNotAlternateIsRejected()
        {
            Assert.That(
                TimeTrialRecording.IsWellFormed(new TimeTrialRecording(new List<TimeTrialInput>
                {
                    new TimeTrialInput(10, true),
                    new TimeTrialInput(20, true),
                })),
                Is.False,
                "two presses with no release between them");

            Assert.That(
                TimeTrialRecording.IsWellFormed(new TimeTrialRecording(new List<TimeTrialInput>
                {
                    new TimeTrialInput(10, false),
                })),
                Is.False,
                "a release with nothing to release");
        }

        [Test]
        public void ARecordingReachingPastTheStepBudgetIsRejected()
        {
            TimeTrialRecording recording = new TimeTrialRecording(new List<TimeTrialInput>
            {
                new TimeTrialInput(TimeTrialConstants.MaxRunSteps, true),
            });

            Assert.That(TimeTrialRecording.IsWellFormed(recording), Is.False);
        }

        [Test]
        public void AnOversizedRecordingIsRejected()
        {
            List<TimeTrialInput> inputs = new List<TimeTrialInput>();
            for (int ndx = 0; ndx <= TimeTrialConstants.MaxInputEvents; ndx++)
                inputs.Add(new TimeTrialInput(ndx, pressed: ndx % 2 == 0));

            Assert.That(TimeTrialRecording.IsWellFormed(new TimeTrialRecording(inputs)), Is.False);
        }

        #endregion
    }
}
