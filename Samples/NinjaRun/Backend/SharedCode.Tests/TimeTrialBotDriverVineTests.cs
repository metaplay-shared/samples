using Metaplay.Core.Math;
using NUnit.Framework;
using System.Collections.Generic;

namespace Game.Logic.Tests
{
    /// <summary>
    /// The scripted runner on a course that hangs a vine: its jump over the basin catches the rope, it lets go
    /// where the fall comes down on the branch, and it runs the branch over whatever lies beneath.
    /// </summary>
    [TestFixture]
    public class TimeTrialBotDriverVineTests
    {
        static readonly TimeTrialRules Rules = new TimeTrialRules();

        static CourseDefinition VineCourse => TestCourses.Vine;

        [TestCase(TimeTrialBotSkill.Competent)]
        [TestCase(TimeTrialBotSkill.Sharp)]
        public void TheRunnerSwingsOntoTheBranchAndFinishes(TimeTrialBotSkill skill)
        {
            TimeTrialRecording recording = TimeTrialBotDriver.Drive(VineCourse, Rules, skill);
            Assert.That(TimeTrialRecording.IsWellFormed(recording), Is.True);

            VineRun run = Play(VineCourse, recording);

            Assert.That(run.Caught, Is.True, "the jump over the basin has to catch the rope");
            Assert.That(run.ReleaseReason, Is.EqualTo("manual"), "the runner lets go itself rather than being shaken off");
            Assert.That(run.RouteOutcome, Is.EqualTo("upper"), "the release comes down on the branch");
            Assert.That(run.Finished, Is.True);

            TimeTrialResult noInput = TimeTrialSimulation.Replay(VineCourse, Rules, new TimeTrialRecording());
            Assert.That(run.FinishTimeMs, Is.LessThan(noInput.FinishTimeMs), "the upper route beats walking the basin");
        }

        [Test]
        public void TheSharpRunnerLetsGoNoLaterThanTheCompetentOne()
        {
            VineRun sharp     = Play(VineCourse, TimeTrialBotDriver.Drive(VineCourse, Rules, TimeTrialBotSkill.Sharp));
            VineRun competent = Play(VineCourse, TimeTrialBotDriver.Drive(VineCourse, Rules, TimeTrialBotSkill.Competent));

            Assert.That(sharp.ReleaseStep, Is.GreaterThan(sharp.CatchStep));
            Assert.That(competent.ReleaseStep, Is.GreaterThan(sharp.ReleaseStep), "the competent runner hesitates on the rope; the sharp one does not");
            Assert.That(sharp.FinishTimeMs, Is.LessThanOrEqualTo(competent.FinishTimeMs));
        }

        /// <summary>
        /// A spike bed lies under the branch. The runner on the branch is carried over it and does not jump it,
        /// and nothing on the branch trips it.
        /// </summary>
        [Test]
        public void TheRunnerRunsTheBranchOverTheBedBeneath()
        {
            CourseDefinition course = TestCourses.VineOverSpikes;
            TimeTrialRecording recording = TimeTrialBotDriver.Drive(course, Rules, TimeTrialBotSkill.Competent);
            VineRun run = Play(course, recording);

            Assert.That(run.RouteOutcome, Is.EqualTo("upper"));
            Assert.That(run.StumblesOnTheBranch, Is.EqualTo(0));
            Assert.That(run.JumpsFromTheBranch, Is.EqualTo(0), "the bed under the branch is nothing to jump");
            Assert.That(run.Finished, Is.True);
        }

        /// <summary>What a recording did on the rope and the branch, read off its own run of the simulation.</summary>
        readonly struct VineRun
        {
            public bool   Caught              { get; }
            public int    CatchStep           { get; }
            public int    ReleaseStep         { get; }
            public string ReleaseReason       { get; }
            public string RouteOutcome        { get; }
            public int    StumblesOnTheBranch { get; }
            public int    JumpsFromTheBranch  { get; }
            public bool   Finished            { get; }
            public int    FinishTimeMs        { get; }

            public VineRun(bool caught, int catchStep, int releaseStep, string releaseReason, string routeOutcome, int stumblesOnTheBranch, int jumpsFromTheBranch, bool finished, int finishTimeMs)
            {
                Caught              = caught;
                CatchStep           = catchStep;
                ReleaseStep         = releaseStep;
                ReleaseReason       = releaseReason;
                RouteOutcome        = routeOutcome;
                StumblesOnTheBranch = stumblesOnTheBranch;
                JumpsFromTheBranch  = jumpsFromTheBranch;
                Finished            = finished;
                FinishTimeMs        = finishTimeMs;
            }
        }

        static VineRun Play(CourseDefinition course, TimeTrialRecording recording)
        {
            VineEncounterDefinition vine = null;
            foreach (CourseEncounterDefinition encounter in course.Encounters)
            {
                if (encounter is VineEncounterDefinition found)
                    vine = found;
            }
            Assert.That(vine, Is.Not.Null, $"{course.Id} hangs no vine");

            TimeTrialSimulation simulation = new TimeTrialSimulation(course, Rules);
            List<TimeTrialInput> inputs = new List<TimeTrialInput>(recording.Inputs);
            int nextInput   = 0;
            int catchStep   = -1;
            int releaseStep = -1;
            int stumbles    = 0;
            int jumps       = 0;
            TimeTrialMode previousMode = simulation.Mode;
            bool wasOnTheBranch = false;

            while (!simulation.IsOver)
            {
                while (nextInput < inputs.Count && inputs[nextInput].Step == simulation.StepIndex)
                    simulation.ApplyInput(inputs[nextInput++]);

                simulation.Step();

                if (catchStep < 0 && simulation.IsAttachedToVine)
                    catchStep = simulation.StepIndex;
                if (catchStep >= 0 && releaseStep < 0 && simulation.HasReleasedVine)
                    releaseStep = simulation.StepIndex;

                bool onTheBranch = simulation.Y == vine.PlatformTopY && simulation.X >= vine.PlatformStartX && simulation.X < vine.PlatformEndX;
                if (onTheBranch && simulation.Mode == TimeTrialMode.Stumbling && previousMode != TimeTrialMode.Stumbling)
                    stumbles++;
                if (wasOnTheBranch && simulation.Mode == TimeTrialMode.Charging && previousMode == TimeTrialMode.Running)
                    jumps++;

                wasOnTheBranch = onTheBranch;
                previousMode   = simulation.Mode;
            }

            return new VineRun(
                caught:              catchStep >= 0,
                catchStep:           catchStep,
                releaseStep:         releaseStep,
                releaseReason:       simulation.VineReleaseReason,
                routeOutcome:        simulation.VineRouteOutcome,
                stumblesOnTheBranch: stumbles,
                jumpsFromTheBranch:  jumps,
                finished:            simulation.IsFinished,
                finishTimeMs:        simulation.Result?.FinishTimeMs ?? int.MaxValue);
        }
    }
}
