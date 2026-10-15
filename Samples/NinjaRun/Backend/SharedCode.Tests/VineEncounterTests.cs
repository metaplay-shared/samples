using Metaplay.Core.Math;
using NUnit.Framework;
using System;
using System.Collections.Generic;

namespace Game.Logic.Tests
{
    [TestFixture]
    public class VineEncounterTests
    {
        static CourseDefinition VineCourse => TestCourses.Vine;
        static VineEncounterDefinition Vine => (VineEncounterDefinition)VineCourse.Encounters[0];

        [Test]
        public void AirborneRunnerAutomaticallyCatchesTheVine()
        {
            TimeTrialSimulation simulation = StartAndCatchVine(new List<TimeTrialInput>());

            Assert.That(simulation.Mode, Is.EqualTo(TimeTrialMode.Traversing));
            Assert.That(simulation.ActiveEncounterIndex, Is.EqualTo(0));
            Assert.That(simulation.EncounterPhase, Is.EqualTo("tap-to-release"));
        }

        [Test]
        public void ReachablePointsAlongTheVineProduceMeaningfullyDifferentGrips()
        {
            List<(TimeTrialSimulation Simulation, F32 GripLength)> catches = FindDistinctCatches();

            Assert.That(catches.Count, Is.GreaterThanOrEqualTo(3));
            Assert.That(catches[^1].GripLength - catches[0].GripLength, Is.GreaterThan(F32.Ratio10(5)),
                "jump trajectory should choose more than one token grip point");

            TimeTrialSimulation highCatch = catches[0].Simulation;
            TimeTrialSimulation lowCatch  = catches[^1].Simulation;
            for (int step = 0; step < 20; step++)
            {
                highCatch.Step();
                lowCatch.Step();
            }

            F32 separationX = F32.Abs(highCatch.X - lowCatch.X);
            F32 separationY = F32.Abs(highCatch.Y - lowCatch.Y);
            Assert.That(separationX + separationY, Is.GreaterThan(F32.Ratio10(2)),
                "high and low catches should produce different swing positions");
        }

        [Test]
        public void CatchPreservesTheAuthoredVineAngleAndMinimizesPositionCorrection()
        {
            TimeTrialSimulation simulation = StartJumpTowardsVine(new List<TimeTrialInput>(), F32.FromInt(19), 45);
            F32 stepDuration = F32.Ratio(1, TimeTrialConstants.StepsPerSecond);

            while (simulation.Mode != TimeTrialMode.Traversing && !simulation.IsOver)
            {
                F32 expectedVelocityY = simulation.VelocityY - new TimeTrialRules().Gravity * stepDuration;
                F32 expectedX = simulation.X + simulation.VelocityX * stepDuration;
                F32 expectedGripY = simulation.Y + expectedVelocityY * stepDuration + VineEncounterDefinition.RunnerGripHeight;
                simulation.Step();

                if (simulation.EncounterPhase != "tap-to-release")
                    continue;

                F32 actualGripY = simulation.Y + VineEncounterDefinition.RunnerGripHeight;
                F32 caughtAngle = F32.Atan2(simulation.X - Vine.AnchorX, Vine.AnchorY - actualGripY);
                Assert.That(F32.Abs(caughtAngle - Vine.RestAngle), Is.LessThan(F32.Ratio(1, 1000)),
                    "capture must begin in the same angled pose the idle vine shows");

                F32 correctionX = simulation.X - expectedX;
                F32 correctionY = actualGripY - expectedGripY;
                Assert.That(correctionX * correctionX + correctionY * correctionY,
                    Is.LessThanOrEqualTo(Vine.ContactRadius * Vine.ContactRadius),
                    "capture correction must remain inside the swept rope tolerance");
            }

            Assert.That(simulation.EncounterPhase, Is.EqualTo("tap-to-release"));
        }

        [Test]
        public void MissingTheVineUsesTheSafeFallbackAndFinishes()
        {
            TimeTrialSimulation simulation = new TimeTrialSimulation(VineCourse, new TimeTrialRules());
            bool enteredBasin = false;
            int stumbles = 0;
            TimeTrialMode previousMode = simulation.Mode;

            while (!simulation.IsOver)
            {
                simulation.Step();
                enteredBasin |= simulation.Y == F32.FromInt(-3);
                if (simulation.Mode == TimeTrialMode.Stumbling && previousMode != TimeTrialMode.Stumbling)
                    stumbles++;
                Assert.That(simulation.EncounterPhase, Is.Not.EqualTo("tap-to-release"),
                    "normal running below the elevated vine must not catch it");
                previousMode = simulation.Mode;
            }

            Assert.That(simulation.Result, Is.Not.Null);
            Assert.That(enteredBasin, Is.True);
            Assert.That(stumbles, Is.GreaterThanOrEqualTo(3));
            Assert.That(simulation.Result.Steps, Is.LessThan(TimeTrialConstants.MaxRunSteps / 4));
        }

        [Test]
        public void AntiStallAllowsTwoRightApexesThenReleasesAtTheNextBottomCrossing()
        {
            List<TimeTrialInput> inputs = new List<TimeTrialInput>();
            TimeTrialSimulation simulation = StartAndCatchVine(inputs);
            F32 angleBeforeRelease = Vine.RestAngle;
            int apexCountBeforeRelease = 0;

            while (simulation.IsAttachedToVine)
            {
                angleBeforeRelease = CurrentVineAngle(simulation);
                apexCountBeforeRelease = simulation.VineRightApexCount;
                simulation.Step();

                if (simulation.VineRightApexCount == 1)
                {
                    Assert.That(simulation.IsAttachedToVine, Is.True, "the first right apex must not force release");
                    Assert.That(simulation.VineForcedReleaseArmed, Is.False);
                }
                if (simulation.VineRightApexCount == 2 && simulation.IsAttachedToVine)
                {
                    Assert.That(simulation.VineForcedReleaseArmed, Is.True);
                    Assert.That(simulation.VineBottomCrossed, Is.False);
                }
            }

            F32 releaseAngle = CurrentVineAngle(simulation);
            Assert.That(apexCountBeforeRelease, Is.EqualTo(2));
            Assert.That(angleBeforeRelease, Is.GreaterThan(F32.Zero));
            Assert.That(releaseAngle, Is.LessThanOrEqualTo(F32.Zero));
            Assert.That(simulation.VineBottomCrossed, Is.True);
            Assert.That(simulation.VineReleaseReason, Is.EqualTo("anti-stall-bottom"));
            Assert.That(simulation.Mode, Is.EqualTo(TimeTrialMode.Airborne));
            Assert.That(simulation.VelocityX, Is.LessThan(F32.Zero), "bottom release preserves the leftward tangent instead of snapping velocity");

            while (!simulation.IsOver)
                simulation.Step();
            Assert.That(simulation.IsFinished, Is.True);
            Assert.That(simulation.Result.Steps, Is.LessThan(TimeTrialConstants.MaxRunSteps / 4));
        }

        [Test]
        public void ForcedReleasePreservesPlayerAndRopeContinuityThenVineSettlesFullDown()
        {
            TimeTrialSimulation simulation = StartAndCatchVine(new List<TimeTrialInput>());
            F32 previousX = simulation.X;
            F32 previousY = simulation.Y;
            F32 previousAngle = simulation.VineVisualAngle;

            while (simulation.IsAttachedToVine)
            {
                previousX = simulation.X;
                previousY = simulation.Y;
                previousAngle = simulation.VineVisualAngle;
                simulation.Step();
            }

            F32 expectedX = Vine.AnchorX + simulation.VineGripLength * F32.Sin(simulation.VineVisualAngle);
            F32 expectedY = Vine.AnchorY - simulation.VineGripLength * F32.Cos(simulation.VineVisualAngle)
                - VineEncounterDefinition.RunnerGripHeight;
            F32 expectedVx = simulation.VineGripLength * F32.Cos(simulation.VineVisualAngle)
                * simulation.VineVisualAngularVelocity;
            F32 expectedVy = simulation.VineGripLength * F32.Sin(simulation.VineVisualAngle)
                * simulation.VineVisualAngularVelocity;

            Assert.That(simulation.VineVisualState, Is.EqualTo("settling"));
            Assert.That(F32.Abs(simulation.X - expectedX), Is.LessThan(F32.Ratio(1, 1000)));
            Assert.That(F32.Abs(simulation.Y - expectedY), Is.LessThan(F32.Ratio(1, 1000)));
            Assert.That(F32.Abs(simulation.VelocityX - expectedVx), Is.LessThan(F32.Ratio(1, 1000)));
            Assert.That(F32.Abs(simulation.VelocityY - expectedVy), Is.LessThan(F32.Ratio(1, 1000)));
            Assert.That(F32.Abs(simulation.X - previousX), Is.LessThan(F32.One));
            Assert.That(F32.Abs(simulation.Y - previousY), Is.LessThan(F32.One));
            Assert.That(F32.Abs(simulation.VineVisualAngle - previousAngle), Is.LessThan(F32.Ratio10(1)));

            int settleSteps = 0;
            while (simulation.VineVisualState == "settling" && !simulation.IsOver)
            {
                simulation.Step();
                settleSteps++;
            }

            Assert.That(settleSteps, Is.GreaterThan(1));
            Assert.That(settleSteps, Is.LessThan(600));
            Assert.That(simulation.VineVisualState, Is.EqualTo("hanging"));
            Assert.That(simulation.VineVisualAngle, Is.EqualTo(F32.Zero));
            Assert.That(simulation.VineVisualAngularVelocity, Is.EqualTo(F32.Zero));
        }

        [Test]
        public void ManualReleaseDoesNotChangePlayerOrRopeInstantaneously()
        {
            List<TimeTrialInput> inputs = new List<TimeTrialInput>();
            TimeTrialSimulation simulation = StartAndCatchVine(inputs);
            for (int step = 0; step < 20; step++)
                simulation.Step();

            F32 x = simulation.X;
            F32 y = simulation.Y;
            F32 vx = simulation.VelocityX;
            F32 vy = simulation.VelocityY;
            F32 angle = simulation.VineVisualAngle;
            F32 angularVelocity = simulation.VineVisualAngularVelocity;

            ApplyAndRecord(simulation, inputs, pressed: true);

            Assert.That(simulation.X, Is.EqualTo(x));
            Assert.That(simulation.Y, Is.EqualTo(y));
            Assert.That(simulation.VelocityX, Is.EqualTo(vx));
            Assert.That(simulation.VelocityY, Is.EqualTo(vy));
            Assert.That(simulation.VineVisualAngle, Is.EqualTo(angle));
            Assert.That(simulation.VineVisualAngularVelocity, Is.EqualTo(angularVelocity));
            Assert.That(simulation.VineVisualState, Is.EqualTo("settling"));
            Assert.That(simulation.VineReleaseReason, Is.EqualTo("manual"));
        }

        [Test]
        public void ManualReleaseOnTheForcedBottomStepTakesPrecedence()
        {
            TimeTrialSimulation forced = StartAndCatchVine(new List<TimeTrialInput>());
            int attachedSteps = 0;
            while (forced.IsAttachedToVine)
            {
                forced.Step();
                attachedSteps++;
            }

            List<TimeTrialInput> inputs = new List<TimeTrialInput>();
            TimeTrialSimulation manual = StartAndCatchVine(inputs);
            for (int step = 0; step < attachedSteps - 1; step++)
                manual.Step();

            Assert.That(manual.IsAttachedToVine, Is.True);
            Assert.That(manual.VineRightApexCount, Is.EqualTo(2));
            Assert.That(manual.VineForcedReleaseArmed, Is.True);
            Assert.That(manual.VineBottomCrossed, Is.False);

            ApplyAndRecord(manual, inputs, pressed: true);
            Assert.That(manual.Mode, Is.EqualTo(TimeTrialMode.Airborne));
            Assert.That(manual.VineReleaseReason, Is.EqualTo("manual"));
            Assert.That(manual.VineBottomCrossed, Is.False);
            manual.Step();
            Assert.That(manual.VineReleaseReason, Is.EqualTo("manual"));
        }

        [Test]
        public void ForcedBottomReleaseLiveRunAndReplayAgreeAndCompleteWithinBounds()
        {
            List<TimeTrialInput> inputs = new List<TimeTrialInput>();
            TimeTrialSimulation live = StartAndCatchVine(inputs);
            while (!live.IsOver)
                live.Step();

            TimeTrialRecording recording = new TimeTrialRecording(inputs);
            TimeTrialResult replay = TimeTrialSimulation.Replay(VineCourse, new TimeTrialRules(), recording);

            Assert.That(TimeTrialRecording.IsWellFormed(recording), Is.True);
            Assert.That(live.VineReleaseReason, Is.EqualTo("anti-stall-bottom"));
            Assert.That(replay, Is.Not.Null);
            Assert.That(replay.FinishTimeMs, Is.EqualTo(live.Result.FinishTimeMs));
            Assert.That(replay.Steps, Is.EqualTo(live.Result.Steps));
            Assert.That(replay.Steps, Is.LessThan(TimeTrialConstants.MaxRunSteps / 4));
        }

        [TestCase(8)]
        [TestCase(45)]
        [TestCase(90)]
        public void EarlyGoodAndLateJumpOffsAllComplete(int attachedSteps)
        {
            List<TimeTrialInput> inputs = new List<TimeTrialInput>();
            TimeTrialSimulation simulation = StartAndCatchVine(inputs);

            for (int step = 0; step < attachedSteps; step++)
                simulation.Step();

            ApplyAndRecord(simulation, inputs, pressed: true);
            Assert.That(simulation.Mode, Is.EqualTo(TimeTrialMode.Airborne));
            ApplyAndRecord(simulation, inputs, pressed: false);

            while (!simulation.IsOver)
                simulation.Step();

            Assert.That(simulation.IsFinished, Is.True);
        }

        [Test]
        public void LiveVineRunAndReplayAgreeExactly()
        {
            List<TimeTrialInput> inputs = new List<TimeTrialInput>();
            TimeTrialSimulation live = StartAndCatchVine(inputs);

            for (int step = 0; step < 45; step++)
                live.Step();
            ApplyAndRecord(live, inputs, pressed: true);
            ApplyAndRecord(live, inputs, pressed: false);

            while (!live.IsOver)
                live.Step();

            TimeTrialRecording recording = new TimeTrialRecording(inputs);
            Assert.That(TimeTrialRecording.IsWellFormed(recording), Is.True);

            TimeTrialResult replay = TimeTrialSimulation.Replay(VineCourse, new TimeTrialRules(), recording);
            TimeTrialResult fallback = TimeTrialSimulation.Replay(VineCourse, new TimeTrialRules(), new TimeTrialRecording());
            Assert.That(replay, Is.Not.Null);
            Assert.That(replay.FinishTimeMs, Is.EqualTo(live.Result.FinishTimeMs));
            Assert.That(replay.Steps, Is.EqualTo(live.Result.Steps));
            Assert.That(replay.FinishTimeMs, Is.LessThan(fallback.FinishTimeMs),
                "a well-timed vine jump-off should beat the spike-bed fallback");
        }

        [Test]
        public void AReasonableReleaseTimingBandReachesTheUpperLanding()
        {
            List<int> successfulReleaseSteps = new List<int>();
            for (int releaseStep = 10; releaseStep <= 100; releaseStep += 5)
            {
                List<TimeTrialInput> inputs = new List<TimeTrialInput>();
                TimeTrialSimulation simulation = StartAndCatchVine(inputs);
                for (int step = 0; step < releaseStep; step++)
                    simulation.Step();
                ApplyAndRecord(simulation, inputs, pressed: true);
                ApplyAndRecord(simulation, inputs, pressed: false);

                while (!simulation.IsOver && !IsOnTheBranch(simulation) && simulation.X < F32.FromInt(48))
                    simulation.Step();

                if (IsOnTheBranch(simulation))
                {
                    Assert.That(simulation.VineRouteOutcome, Is.EqualTo("upper"));
                    Assert.That(VineCourse.Hazards[^1].EndX, Is.LessThanOrEqualTo(simulation.X),
                        "the upper landing must begin beyond the lower obstacle gauntlet");
                    successfulReleaseSteps.Add(releaseStep);
                }
            }

            Assert.That(successfulReleaseSteps.Count, Is.GreaterThanOrEqualTo(3),
                $"upper landing timing band was too narrow: {string.Join(", ", successfulReleaseSteps)}");
            Assert.That(successfulReleaseSteps[^1] - successfulReleaseSteps[0], Is.GreaterThanOrEqualTo(10));
        }

        /// <summary>
        /// The branch is ground to a runner who swung onto it: it runs along it, and a press plants its feet
        /// and charges a jump exactly as on the ground, which comes back down on the branch further along.
        /// </summary>
        [Test]
        public void ARunnerOnTheBranchChargesAndJumpsLikeOnTheGround()
        {
            List<TimeTrialInput> inputs = new List<TimeTrialInput>();
            TimeTrialSimulation simulation = LandOnTheBranch(inputs);
            F32 landedX = simulation.X;

            for (int step = 0; step < 6; step++)
                simulation.Step();
            Assert.That(simulation.Mode, Is.EqualTo(TimeTrialMode.Running));
            Assert.That(simulation.Y, Is.EqualTo(Vine.PlatformTopY));
            Assert.That(simulation.X, Is.GreaterThan(landedX), "the runner runs along the branch");
            Assert.That(simulation.ActiveEncounterIndex, Is.EqualTo(-1), "on the branch the vine no longer owns the runner");
            Assert.That(simulation.EncounterPhase, Is.EqualTo(""));

            ApplyAndRecord(simulation, inputs, pressed: true);
            Assert.That(simulation.Mode, Is.EqualTo(TimeTrialMode.Charging), "a press on the branch plants the feet");
            for (int step = 0; step < 10; step++)
                simulation.Step();
            Assert.That(simulation.Mode, Is.EqualTo(TimeTrialMode.Charging));
            Assert.That(simulation.ChargeFraction, Is.GreaterThan(F32.Zero));

            ApplyAndRecord(simulation, inputs, pressed: false);
            simulation.Step();
            Assert.That(simulation.Mode, Is.EqualTo(TimeTrialMode.Airborne), "the release launches");
            Assert.That(simulation.Y, Is.GreaterThan(Vine.PlatformTopY));

            int airborneSteps = 0;
            while (simulation.Mode == TimeTrialMode.Airborne && airborneSteps < 120)
            {
                simulation.Step();
                airborneSteps++;
            }

            Assert.That(simulation.Mode, Is.EqualTo(TimeTrialMode.Running), "the jump comes back down");
            Assert.That(simulation.Y, Is.EqualTo(Vine.PlatformTopY), "and lands on the branch again");
            Assert.That(simulation.X, Is.LessThan(Vine.PlatformEndX));

            while (!simulation.IsOver)
                simulation.Step();
            Assert.That(simulation.IsFinished, Is.True);

            TimeTrialResult replay = TimeTrialSimulation.Replay(VineCourse, new TimeTrialRules(), new TimeTrialRecording(inputs));
            Assert.That(replay, Is.Not.Null);
            Assert.That(replay.FinishTimeMs, Is.EqualTo(simulation.Result.FinishTimeMs), "the server's own run of the recording agrees");
        }

        /// <summary>A runner runs off the far end of the branch into the air, as off the end of any ground.</summary>
        [Test]
        public void ARunnerRunsOffTheFarEndOfTheBranch()
        {
            TimeTrialSimulation simulation = LandOnTheBranch(new List<TimeTrialInput>());

            while (simulation.Mode == TimeTrialMode.Running && simulation.X < Vine.PlatformEndX)
            {
                Assert.That(simulation.Y, Is.EqualTo(Vine.PlatformTopY));
                simulation.Step();
            }

            Assert.That(simulation.Mode, Is.EqualTo(TimeTrialMode.Airborne));
            Assert.That(simulation.X, Is.GreaterThanOrEqualTo(Vine.PlatformEndX));

            while (!simulation.IsOver)
                simulation.Step();
            Assert.That(simulation.IsFinished, Is.True);
        }

        /// <summary>
        /// The branch stands over the low route. A runner on it never touches the spike beds beneath, and a
        /// runner below never lands on it: its top is a surface only for a runner who caught the rope.
        /// </summary>
        [Test]
        public void SpikesUnderTheBranchNeverTripARunnerOnIt()
        {
            List<HazardSpan> hazards = new List<HazardSpan>(VineCourse.Hazards)
            {
                new HazardSpan(Vine.PlatformStartX + F32.One, Vine.PlatformEndX - F32.One),
            };
            CourseDefinition spikedBranch = new CourseDefinition(
                CourseId.FromString("test-vine-spiked-branch"),
                "Spiked Branch",
                VineCourse.StartX,
                VineCourse.FinishX,
                VineCourse.Ground,
                hazards,
                encounters: VineCourse.Encounters);

            TimeTrialSimulation simulation = LandOnTheBranch(new List<TimeTrialInput>(), spikedBranch);
            while (simulation.Mode == TimeTrialMode.Running && simulation.X < Vine.PlatformEndX)
            {
                Assert.That(simulation.Y, Is.EqualTo(Vine.PlatformTopY));
                simulation.Step();
                Assert.That(simulation.Mode, Is.Not.EqualTo(TimeTrialMode.Stumbling), $"tripped at {simulation.X} m on the branch");
            }

            Assert.That(simulation.X, Is.GreaterThanOrEqualTo(Vine.PlatformEndX));
        }

        /// <summary>
        /// A runner who never caught the rope passes through the branch: a jump from the ground that crosses its
        /// top inside its span comes back down to the ground.
        /// </summary>
        [Test]
        public void AJumpFromTheGroundPassesThroughABranchWhoseRopeWasNotCaught()
        {
            F32 branchTopY = F32.Ratio10(15);
            VineEncounterDefinition highVine = new VineEncounterDefinition(
                "high-vine",
                anchorX:        F32.FromInt(27),
                anchorY:        F32.FromInt(14),
                platformStartX: F32.FromInt(26),
                platformEndX:   F32.FromInt(40),
                platformTopY:   branchTopY);
            CourseDefinition course = new CourseDefinition(
                CourseId.FromString("low-branch"),
                "Low Branch",
                F32.Zero,
                F32.FromInt(80),
                new List<GroundSegment> { new GroundSegment(F32.FromInt(-12), F32.FromInt(100), F32.Zero) },
                new List<HazardSpan>(),
                encounters: new List<CourseEncounterDefinition> { highVine });

            TimeTrialSimulation simulation = new TimeTrialSimulation(course, new TimeTrialRules());
            while (simulation.X < F32.FromInt(24))
                simulation.Step();
            simulation.ApplyInput(new TimeTrialInput(simulation.StepIndex, pressed: true));
            for (int step = 0; step < 45; step++)
                simulation.Step();
            simulation.ApplyInput(new TimeTrialInput(simulation.StepIndex, pressed: false));

            bool roseAboveTheBranch = false;
            while (simulation.Mode != TimeTrialMode.Running && !simulation.IsOver)
            {
                simulation.Step();
                roseAboveTheBranch |= simulation.Y > branchTopY && simulation.X >= highVine.PlatformStartX && simulation.X < highVine.PlatformEndX;
                Assert.That(simulation.Mode, Is.Not.EqualTo(TimeTrialMode.Traversing), "the rope hangs out of a jump's reach");
            }

            Assert.That(roseAboveTheBranch, Is.True, "the jump has to cross the branch for the case to mean anything");
            Assert.That(simulation.Mode, Is.EqualTo(TimeTrialMode.Running));
            Assert.That(simulation.Y, Is.EqualTo(F32.Zero), "the landing is on the ground, not the branch");
            Assert.That(simulation.VineRouteOutcome, Is.EqualTo("pending"));
        }

        /// <summary>
        /// A vine far behind the runner never catches it. The swept test against the rope squares distances,
        /// and a fixed-point square wraps negative past about 181 m, which would read as a catch from anywhere
        /// on a long map.
        /// </summary>
        [Test]
        public void AVineFarBehindTheRunnerNeverCatchesIt()
        {
            VineEncounterDefinition vine = new VineEncounterDefinition(
                "far-vine",
                anchorX:        F32.FromInt(30),
                anchorY:        F32.FromInt(8),
                platformStartX: F32.FromInt(34),
                platformEndX:   F32.FromInt(49),
                platformTopY:   F32.FromInt(3));
            CourseDefinition course = new CourseDefinition(
                CourseId.FromString("long-run-past-a-vine"),
                "Long Run Past A Vine",
                F32.Zero,
                F32.FromInt(400),
                new List<GroundSegment> { new GroundSegment(F32.FromInt(-12), F32.FromInt(420), F32.Zero) },
                new List<HazardSpan>(),
                encounters: new List<CourseEncounterDefinition> { vine });

            TimeTrialSimulation simulation = new TimeTrialSimulation(course, new TimeTrialRules());
            while (!simulation.IsOver)
            {
                simulation.Step();
                Assert.That(simulation.Mode, Is.Not.EqualTo(TimeTrialMode.Traversing), $"caught the rope from {simulation.X} m");
            }

            Assert.That(simulation.IsFinished, Is.True);
            Assert.That(simulation.VineRouteOutcome, Is.EqualTo("pending"));
        }

        /// <summary>Whether the runner stands on the vine's branch: landed, at its height, inside its span.</summary>
        static bool IsOnTheBranch(TimeTrialSimulation simulation)
            => simulation.Mode == TimeTrialMode.Running
                && simulation.Y == Vine.PlatformTopY
                && simulation.X >= Vine.PlatformStartX
                && simulation.X < Vine.PlatformEndX;

        /// <summary>
        /// Catch the vine and let go on the earliest step of the timing band that comes down on the
        /// branch, returning the run on the step it landed there.
        /// </summary>
        static TimeTrialSimulation LandOnTheBranch(List<TimeTrialInput> inputs, CourseDefinition course = null)
        {
            for (int releaseStep = 10; releaseStep <= 100; releaseStep += 5)
            {
                List<TimeTrialInput> attempt = new List<TimeTrialInput>();
                TimeTrialSimulation simulation = StartAndCatchVine(attempt, course);
                for (int step = 0; step < releaseStep; step++)
                    simulation.Step();
                ApplyAndRecord(simulation, attempt, pressed: true);
                ApplyAndRecord(simulation, attempt, pressed: false);

                while (!simulation.IsOver && simulation.Mode == TimeTrialMode.Airborne)
                    simulation.Step();

                if (IsOnTheBranch(simulation))
                {
                    inputs.AddRange(attempt);
                    return simulation;
                }
            }

            Assert.Fail("no release in the timing band came down on the branch");
            return null;
        }

        static TimeTrialSimulation StartAndCatchVine(List<TimeTrialInput> inputs, CourseDefinition course = null)
        {
            TimeTrialSimulation simulation = StartJumpTowardsVine(inputs, F32.FromInt(19), 45, course);

            while (simulation.Mode != TimeTrialMode.Traversing && !simulation.IsOver)
                simulation.Step();

            Assert.That(simulation.Mode, Is.EqualTo(TimeTrialMode.Traversing), "the jump missed the vine's grab window");
            return simulation;
        }

        static TimeTrialSimulation StartJumpTowardsVine(List<TimeTrialInput> inputs, F32 pressX, int chargeSteps, CourseDefinition course = null)
        {
            TimeTrialSimulation simulation = new TimeTrialSimulation(course ?? VineCourse, new TimeTrialRules());
            while (simulation.X < pressX)
                simulation.Step();

            ApplyAndRecord(simulation, inputs, pressed: true);
            for (int chargeStep = 0; chargeStep < chargeSteps; chargeStep++)
                simulation.Step();
            ApplyAndRecord(simulation, inputs, pressed: false);
            return simulation;
        }

        static List<(TimeTrialSimulation Simulation, F32 GripLength)> FindDistinctCatches()
        {
            List<(TimeTrialSimulation Simulation, F32 GripLength)> catches = new List<(TimeTrialSimulation, F32)>();
            for (int pressTenths = 175; pressTenths <= 200; pressTenths += 5)
            {
                for (int chargeSteps = 30; chargeSteps <= 45; chargeSteps += 3)
                {
                    TimeTrialSimulation simulation = StartJumpTowardsVine(
                        new List<TimeTrialInput>(),
                        F32.Ratio10(pressTenths),
                        chargeSteps);
                    while (simulation.Mode != TimeTrialMode.Traversing && !simulation.IsOver && simulation.X < F32.FromInt(31))
                        simulation.Step();
                    if (simulation.EncounterPhase != "tap-to-release")
                        continue;

                    F32 gripY = simulation.Y + VineEncounterDefinition.RunnerGripHeight;
                    F32 offsetX = simulation.X - Vine.AnchorX;
                    F32 offsetY = Vine.AnchorY - gripY;
                    F32 gripLength = F32.Sqrt(offsetX * offsetX + offsetY * offsetY);
                    catches.Add((simulation, gripLength));
                }
            }

            catches.Sort((left, right) => left.GripLength.CompareTo(right.GripLength));
            return catches;
        }

        static F32 CurrentVineAngle(TimeTrialSimulation simulation)
        {
            F32 gripY = simulation.Y + VineEncounterDefinition.RunnerGripHeight;
            return F32.Atan2(simulation.X - Vine.AnchorX, Vine.AnchorY - gripY);
        }

        static void ApplyAndRecord(TimeTrialSimulation simulation, List<TimeTrialInput> inputs, bool pressed)
        {
            TimeTrialInput input = new TimeTrialInput(simulation.StepIndex, pressed);
            inputs.Add(input);
            simulation.ApplyInput(input);
        }
    }
}
