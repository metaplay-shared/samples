using Metaplay.Core.Math;
using NUnit.Framework;
using System.Collections.Generic;

namespace Game.Logic.Tests
{
    /// <summary>
    /// The automatic mantle, on the face a course derives where its ground steps up
    /// (<see cref="TestCourses.StepLedge"/>): the catch at the height the runner meets the face, the hang, the
    /// rise and the top transition, and the run resumed on the ground above. The far wall of a pit is the same
    /// face and the same runtime; <see cref="PitFallSimulationTests"/> covers falling in and climbing out.
    /// </summary>
    [TestFixture]
    public class LedgeGrabSimulationTests
    {
        const int CatchChargeSteps = 45;
        const int LowChargeSteps = 16;

        static TimeTrialRules Rules() => new TimeTrialRules();

        [TestCase(26, 41)]
        [TestCase(26, 45)]
        [TestCase(25, 41)]
        [TestCase(25, 45)]
        public void SeveralHigherContactsAutomaticallyCatchWithoutChangingCaptureHeight(int approachX, int chargeSteps)
        {
            TimeTrialSimulation simulation = new TimeTrialSimulation(TestCourses.StepLedge, Rules());
            List<TimeTrialInput> inputs = new List<TimeTrialInput>();

            JumpFromApproach(simulation, inputs, F32.FromInt(approachX), chargeSteps);
            WaitForCatch(simulation);

            Assert.That(inputs, Has.Count.EqualTo(2), "the normal jump press and release are the entire interaction");
            Assert.That(simulation.Mode, Is.EqualTo(TimeTrialMode.Traversing));
            Assert.That(simulation.EncounterPhase, Is.EqualTo("ledge-hang"));
            Assert.That(simulation.ActiveEncounterIndex, Is.EqualTo(0));
            Assert.That(simulation.BodyRight, Is.EqualTo(Ledge().X), "the catch starts when the body's right edge reaches the face");
            Assert.That(simulation.Y, Is.EqualTo(simulation.LedgeGrabStartY));
            Assert.That(simulation.Y, Is.GreaterThanOrEqualTo(Ledge().MinimumGrabY));
        }

        [Test]
        public void CaptureAndFirstAnimationStatePreserveSweptContactYWithoutTeleport()
        {
            TimeTrialRules rules = Rules();
            TimeTrialSimulation simulation = new TimeTrialSimulation(TestCourses.StepLedge, rules);
            List<TimeTrialInput> inputs = new List<TimeTrialInput>();
            JumpFromApproach(simulation, inputs, F32.FromInt(26), CatchChargeSteps);

            F32 expectedContactY = F32.Zero;
            bool captured = false;
            while (!captured && !simulation.IsOver)
            {
                F32 previousX = simulation.X;
                F32 previousY = simulation.Y;
                F32 nextVelocityY = simulation.VelocityY - rules.Gravity * F32.Ratio(1, TimeTrialConstants.StepsPerSecond);
                F32 nextX = previousX + simulation.VelocityX * F32.Ratio(1, TimeTrialConstants.StepsPerSecond);
                F32 nextY = previousY + nextVelocityY * F32.Ratio(1, TimeTrialConstants.StepsPerSecond);
                simulation.Step();
                captured = simulation.Mode == TimeTrialMode.Traversing;
                if (captured)
                {
                    LedgeGrabDefinition ledge = Ledge();
                    F32 previousRight = previousX + TimeTrialPlayerBody.RightExtent;
                    F32 nextRight = nextX + TimeTrialPlayerBody.RightExtent;
                    F32 fraction = F32.Div(ledge.X - previousRight, nextRight - previousRight);
                    expectedContactY = F32.Lerp(previousY, nextY, fraction);
                }
            }

            Assert.That(captured, Is.True);
            Assert.That(simulation.Y, Is.EqualTo(expectedContactY), "capture stores the swept contact without an animation snap");
            Assert.That(simulation.LedgeGrabStartY, Is.EqualTo(expectedContactY));

            F32 capturedY = simulation.Y;
            Advance(simulation, rules.LedgeGrabHangSteps);
            Assert.That(simulation.EncounterPhase, Is.EqualTo("ledge-rise"));
            Assert.That(simulation.EncounterProgress, Is.EqualTo(F32.Zero));
            Assert.That(simulation.Y, Is.EqualTo(capturedY), "the first rise state starts at captured Y");

            simulation.Step();
            Assert.That(simulation.Y, Is.GreaterThan(capturedY));
            Assert.That(simulation.Y - capturedY,
                Is.LessThanOrEqualTo(rules.LedgeGrabRiseSpeed * F32.Ratio(1, TimeTrialConstants.StepsPerSecond)),
                "rise motion is bounded by the climb speed");
        }

        [Test]
        public void HangVerticalRiseClearanceAndTopTransitionAreDistinctAndDeterministic()
        {
            TimeTrialRules rules = Rules();
            TimeTrialSimulation simulation = Catch(rules, out List<TimeTrialInput> inputs);
            LedgeGrabDefinition ledge = Ledge();
            F32 hangY = simulation.LedgeGrabStartY;

            Assert.That(simulation.Y, Is.EqualTo(hangY));
            Advance(simulation, rules.LedgeGrabHangSteps - 1);
            Assert.That(simulation.EncounterPhase, Is.EqualTo("ledge-hang"));
            Assert.That(simulation.BodyRight, Is.EqualTo(ledge.X));
            Assert.That(simulation.Y, Is.EqualTo(hangY));

            simulation.Step();
            Assert.That(simulation.EncounterPhase, Is.EqualTo("ledge-rise"));

            // The rise is a climb speed rather than a step count, so it runs until the feet clear the lip.
            F32 clearanceY = ledge.TopY + rules.LedgeGrabClearanceHeight;
            F32 perStep    = rules.LedgeGrabRiseSpeed * F32.Ratio(1, TimeTrialConstants.StepsPerSecond);
            while (simulation.Y < clearanceY && !simulation.IsOver)
            {
                F32 previousY = simulation.Y;
                simulation.Step();
                Assert.That(simulation.Y, Is.GreaterThan(previousY));
                Assert.That(simulation.Y - previousY, Is.LessThanOrEqualTo(perStep));
                Assert.That(simulation.BodyRight, Is.EqualTo(ledge.X));
            }
            Assert.That(simulation.BodyRight, Is.EqualTo(ledge.X), "the body's right edge must stay flush throughout the vertical rise");
            Assert.That(simulation.Y, Is.GreaterThan(hangY));
            Assert.That(simulation.EncounterPhase, Is.EqualTo("ledge-rise"));
            Assert.That(simulation.Y, Is.EqualTo(clearanceY));
            Assert.That(simulation.EncounterProgress, Is.EqualTo(F32.One));
            F32 finalRiseX = simulation.X;
            F32 finalRiseY = simulation.Y;

            simulation.Step();
            Assert.That(simulation.EncounterPhase, Is.EqualTo("ledge-top-transition"));
            Assert.That(simulation.EncounterProgress, Is.EqualTo(F32.Zero));
            Assert.That(simulation.X, Is.EqualTo(finalRiseX), "the first top-transition position equals the final rise position");
            Assert.That(simulation.Y, Is.EqualTo(finalRiseY), "the phase boundary does not move the runner");
            Assert.That(simulation.BodyRight, Is.EqualTo(ledge.X));
            Assert.That(simulation.Y, Is.EqualTo(ledge.TopY + rules.LedgeGrabClearanceHeight));
            F32 transitionStartX = simulation.X;
            F32 transitionStartY = simulation.Y;
            List<int> horizontalStepDeltas = new List<int>();

            for (int ndx = 0; ndx < rules.LedgeGrabTopTransitionSteps; ndx++)
            {
                F32 previousX = simulation.X;
                simulation.Step();
                horizontalStepDeltas.Add((simulation.X - previousX).Raw);
                Assert.That(simulation.Mode, Is.EqualTo(TimeTrialMode.Traversing));
                Assert.That(simulation.EncounterPhase, Is.EqualTo("ledge-top-transition"));
                Assert.That(simulation.BodyBottom, Is.GreaterThanOrEqualTo(ledge.TopY));
                Assert.That(simulation.X - previousX, Is.LessThanOrEqualTo(F32.Ratio10(2)));
            }

            Assert.That(transitionStartX, Is.EqualTo(ledge.X - TimeTrialPlayerBody.RightExtent));
            Assert.That(transitionStartY, Is.EqualTo(ledge.TopY + rules.LedgeGrabClearanceHeight));
            Assert.That(horizontalStepDeltas, Has.All.Positive);
            Assert.That(Max(horizontalStepDeltas) - Min(horizontalStepDeltas), Is.LessThanOrEqualTo(4), "fixed-point interpolation deltas stay consistent to raw-unit rounding");
            Assert.That(simulation.X, Is.EqualTo(ledge.LandingX));
            Assert.That(simulation.Y, Is.EqualTo(ledge.TopY));
            F32 finalTransitionX = simulation.X;
            F32 finalTransitionY = simulation.Y;

            simulation.Step();
            Assert.That(simulation.Mode, Is.EqualTo(TimeTrialMode.Running));
            Assert.That(simulation.X, Is.EqualTo(finalTransitionX), "resuming running must not snap horizontally");
            Assert.That(simulation.Y, Is.EqualTo(finalTransitionY), "resuming running must not snap vertically");
            Assert.That(simulation.VelocityX, Is.EqualTo(rules.LedgeGrabResumeSpeed));
            Assert.That(simulation.VelocityX, Is.EqualTo(simulation.LedgeGrabResumeSpeed));
            Assert.That(simulation.VelocityY, Is.EqualTo(F32.Zero));
            Assert.That(TestCourses.StepLedge.HasSurfaceAt(simulation.X, simulation.Y), Is.True);
            Assert.That(inputs, Has.Count.EqualTo(2));

            F32 resumedX = simulation.X;
            simulation.Step();
            Assert.That(simulation.VelocityX, Is.EqualTo(simulation.LedgeGrabResumeSpeed + rules.RunAcceleration * F32.Ratio(1, TimeTrialConstants.StepsPerSecond)));
            Assert.That(simulation.X, Is.GreaterThan(resumedX));
        }

        [Test]
        public void InputDuringAutomaticSequenceIsIgnored()
        {
            TimeTrialRules rules = Rules();
            TimeTrialSimulation untouched = Catch(rules, out List<TimeTrialInput> untouchedInputs);
            TimeTrialSimulation noisy = Catch(rules, out List<TimeTrialInput> noisyInputs);

            Record(noisy, noisyInputs, true);
            noisy.Step();
            Record(noisy, noisyInputs, false);
            Advance(untouched, 1);

            int remainingSteps = rules.LedgeGrabHangSteps + LongestRiseSteps(rules, Ledge()) + rules.LedgeGrabTopTransitionSteps + 2;
            for (int ndx = 0; ndx < remainingSteps; ndx++)
            {
                untouched.Step();
                noisy.Step();
            }

            Assert.Multiple(() =>
            {
                Assert.That(noisy.Mode, Is.EqualTo(untouched.Mode));
                Assert.That(noisy.X, Is.EqualTo(untouched.X));
                Assert.That(noisy.Y, Is.EqualTo(untouched.Y));
                Assert.That(noisy.VelocityX, Is.EqualTo(untouched.VelocityX));
            });
        }

        /// <summary>
        /// Whatever the charge, a jump that reaches the face is stopped on it and caught there: the swept test
        /// against the face lets no launch speed carry the body through it.
        /// </summary>
        [TestCase(14)]
        [TestCase(16)]
        [TestCase(30)]
        [TestCase(45)]
        public void AJumpMeetsTheFaceAndIsCaughtThereWhateverItsCharge(int chargeSteps)
        {
            TimeTrialSimulation simulation = new TimeTrialSimulation(TestCourses.StepLedge, Rules());
            List<TimeTrialInput> inputs = new List<TimeTrialInput>();
            LedgeGrabDefinition ledge = Ledge();
            JumpFromApproach(simulation, inputs, F32.FromInt(26), chargeSteps);

            while (simulation.BodyRight < ledge.X && !simulation.IsOver)
                simulation.Step();

            Assert.Multiple(() =>
            {
                Assert.That(simulation.BodyRight, Is.EqualTo(ledge.X), "the body stops flush with the face");
                Assert.That(simulation.VelocityX, Is.EqualTo(F32.Zero));
                Assert.That(simulation.Mode, Is.EqualTo(TimeTrialMode.Traversing));
                Assert.That(simulation.EncounterPhase, Is.EqualTo("ledge-hang"));
            });
        }

        [Test]
        public void JumpsAndRunsOnFootNeverMoveBackwardsAndAllFinishWithinBound()
        {
            AssertMonotonicAndFinish(26, CatchChargeSteps);
            AssertMonotonicAndFinish(27, LowChargeSteps);
            AssertMonotonicAndFinish(27, null);
        }

        /// <summary>
        /// The rise up the face is a speed, so a catch high up the face costs less than one at its foot: the
        /// mantle after a jump that meets the face high is over sooner than the one after a run on foot that
        /// meets it at the bottom.
        /// </summary>
        [Test]
        public void AHighCatchIsAShorterMantleThanACatchAtTheFoot()
        {
            TimeTrialRules rules = Rules();

            TimeTrialSimulation high = new TimeTrialSimulation(TestCourses.StepLedge, rules);
            JumpFromApproach(high, new List<TimeTrialInput>(), F32.FromInt(26), CatchChargeSteps);
            WaitForCatch(high);

            TimeTrialSimulation foot = new TimeTrialSimulation(TestCourses.StepLedge, rules);
            WaitForCatch(foot);

            Assert.That(foot.LedgeGrabStartY, Is.EqualTo(Ledge().BottomY), "a runner on foot meets the face at its foot");
            Assert.That(high.LedgeGrabStartY, Is.GreaterThan(foot.LedgeGrabStartY), "the jump has to meet the face higher up for the case to mean anything");
            Assert.That(MantleSteps(high), Is.LessThan(MantleSteps(foot)));
        }

        /// <summary>Steps from a catch until the runner is running again on the ground above.</summary>
        static int MantleSteps(TimeTrialSimulation simulation)
        {
            int steps = 0;
            while (simulation.Mode == TimeTrialMode.Traversing && !simulation.IsOver)
            {
                simulation.Step();
                steps++;
            }
            return steps;
        }

        [Test]
        public void LiveCatchAndReplayAgreeExactly()
        {
            TimeTrialRules rules = Rules();
            TimeTrialResult live = Play(rules, 27, CatchChargeSteps, out TimeTrialRecording recording);
            TimeTrialResult replayed = TimeTrialSimulation.Replay(TestCourses.StepLedge, rules, recording);

            Assert.That(TimeTrialRecording.IsWellFormed(recording), Is.True);
            Assert.That(replayed, Is.Not.Null);
            Assert.That(replayed.FinishTimeMs, Is.EqualTo(live.FinishTimeMs));
            Assert.That(replayed.Steps, Is.EqualTo(live.Steps));
        }

        static TimeTrialSimulation Catch(TimeTrialRules rules, out List<TimeTrialInput> inputs)
        {
            TimeTrialSimulation simulation = new TimeTrialSimulation(TestCourses.StepLedge, rules);
            inputs = new List<TimeTrialInput>();
            JumpFromApproach(simulation, inputs, F32.FromInt(26), CatchChargeSteps);
            WaitForCatch(simulation);
            return simulation;
        }

        static TimeTrialResult Play(TimeTrialRules rules, int approachX, int chargeSteps, out TimeTrialRecording recording)
        {
            TimeTrialSimulation simulation = new TimeTrialSimulation(TestCourses.StepLedge, rules);
            List<TimeTrialInput> inputs = new List<TimeTrialInput>();
            JumpFromApproach(simulation, inputs, F32.FromInt(approachX), chargeSteps);
            Finish(simulation);
            recording = new TimeTrialRecording(inputs);
            return simulation.Result;
        }

        static void AssertMonotonicAndFinish(int approachX, int? chargeSteps)
        {
            TimeTrialSimulation simulation = new TimeTrialSimulation(TestCourses.StepLedge, Rules());
            List<TimeTrialInput> inputs = new List<TimeTrialInput>();
            if (chargeSteps.HasValue)
                JumpFromApproach(simulation, inputs, F32.FromInt(approachX), chargeSteps.Value);

            F32 previousX = simulation.X;
            while (!simulation.IsOver)
            {
                simulation.Step();
                Assert.That(simulation.X, Is.GreaterThanOrEqualTo(previousX));
                Assert.That(simulation.VelocityX, Is.GreaterThanOrEqualTo(F32.Zero));
                previousX = simulation.X;
            }

            Assert.That(simulation.IsFinished, Is.True);
            Assert.That(simulation.StepIndex, Is.LessThan(TimeTrialConstants.MaxRunSteps / 2));
        }

        static void JumpFromApproach(TimeTrialSimulation simulation, List<TimeTrialInput> inputs, F32 approachX, int chargeSteps)
        {
            AdvanceUntilX(simulation, approachX);
            Record(simulation, inputs, true);
            Advance(simulation, chargeSteps);
            Record(simulation, inputs, false);
        }

        static void WaitForCatch(TimeTrialSimulation simulation)
        {
            while (simulation.Mode != TimeTrialMode.Traversing && !simulation.IsOver)
                simulation.Step();
            Assert.That(simulation.IsOver, Is.False, "the jump never reached the ledge catch band");
        }


        static LedgeGrabDefinition Ledge() => (LedgeGrabDefinition)TestCourses.StepLedge.Encounters[0];

        /// <summary>Steps the longest possible rise up <paramref name="ledge"/> takes, from its bottom to its clearance height.</summary>
        static int LongestRiseSteps(TimeTrialRules rules, LedgeGrabDefinition ledge)
        {
            F32 perStep = rules.LedgeGrabRiseSpeed * F32.Ratio(1, TimeTrialConstants.StepsPerSecond);
            F32 climb   = ledge.TopY + rules.LedgeGrabClearanceHeight - ledge.BottomY;
            int steps   = 0;

            while (perStep * F32.FromInt(steps) < climb)
                steps++;

            return steps;
        }

        static void Finish(TimeTrialSimulation simulation)
        {
            while (!simulation.IsOver)
                simulation.Step();
            Assert.That(simulation.Result, Is.Not.Null);
        }

        static void AdvanceUntilX(TimeTrialSimulation simulation, F32 x)
        {
            while (simulation.X < x && !simulation.IsOver)
                simulation.Step();
        }

        static void Advance(TimeTrialSimulation simulation, int steps)
        {
            for (int ndx = 0; ndx < steps; ndx++)
                simulation.Step();
        }

        static void Record(TimeTrialSimulation simulation, List<TimeTrialInput> inputs, bool pressed)
        {
            TimeTrialInput input = new TimeTrialInput(simulation.StepIndex, pressed);
            inputs.Add(input);
            simulation.ApplyInput(input);
        }

        static int Min(List<int> values)
        {
            int result = values[0];
            for (int ndx = 1; ndx < values.Count; ndx++)
                result = System.Math.Min(result, values[ndx]);
            return result;
        }

        static int Max(List<int> values)
        {
            int result = values[0];
            for (int ndx = 1; ndx < values.Count; ndx++)
                result = System.Math.Max(result, values[ndx]);
            return result;
        }
    }
}
