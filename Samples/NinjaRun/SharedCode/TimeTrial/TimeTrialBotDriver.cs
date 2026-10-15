using Metaplay.Core;
using Metaplay.Core.Math;
using Metaplay.Core.Model;
using System;
using System.Collections.Generic;

namespace Game.Logic
{
    /// <summary>
    /// How well a scripted runner plays, so a course can be given opponents at more than one pace before any
    /// human has run it.
    /// <para>
    /// The numeric values are not the skill order: <see cref="Clumsy"/> is the slowest level but has the
    /// highest value. The value seeds each level's jitter, so renumbering would change every seed ghost already raced.
    /// </para>
    /// </summary>
    [MetaSerializable]
    public enum TimeTrialBotSkill
    {
        /// <summary>
        /// Misses every jump and walks into every obstacle. Never in a course's pool: it is the opponent a
        /// player's first-ever run is put against so there is someone to beat.
        /// </summary>
        Clumsy = 3,

        /// <summary>Plants its feet early and over-charges every jump. The pace a first-time player beats.</summary>
        Cautious = 0,

        /// <summary>Jumps what has to be jumped, with the charge the obstacle needs. The reference pace.</summary>
        Competent = 1,

        /// <summary>Plants late and charges as little as clears the obstacle. The pace a course is tuned against.</summary>
        Sharp = 2,
    }

    /// <summary>
    /// The handful of numbers that separate one <see cref="TimeTrialBotSkill"/> from another. Not serialized:
    /// a skill level is what travels, and the style it stands for is resolved from code on both sides.
    /// </summary>
    public readonly struct TimeTrialBotStyle
    {
        /// <summary>How far before an obstacle the runner plants its feet and starts charging, metres.</summary>
        public F32 PreJumpGap { get; }

        /// <summary>How far past an obstacle the runner aims to land, metres.</summary>
        public F32 LandingMargin { get; }

        /// <summary>
        /// Charge spent as a permille of what the obstacle needs. Above 1000 is time thrown away.
        /// </summary>
        public int ChargePermille { get; }

        /// <summary>Upper bound on the extra charge permille drawn per jump, so a run does not look metronomic.</summary>
        public int JitterChargePermille { get; }

        /// <summary>Upper bound on the extra steps the button is held past the charge that was aimed for.</summary>
        public int JitterHoldSteps { get; }

        /// <summary>
        /// Every nth obstacle is not jumped at all, or 0 to jump them all.
        /// </summary>
        public int MissEveryNth { get; }

        /// <summary>
        /// How many steps the runner keeps hold of a vine past the first step a jump-off would already come down
        /// on the branch.
        /// </summary>
        public int VineReleaseDelaySteps { get; }

        public TimeTrialBotStyle(F32 preJumpGap, F32 landingMargin, int chargePermille, int jitterChargePermille, int jitterHoldSteps, int missEveryNth, int vineReleaseDelaySteps)
        {
            PreJumpGap            = preJumpGap;
            LandingMargin         = landingMargin;
            ChargePermille        = chargePermille;
            JitterChargePermille  = jitterChargePermille;
            JitterHoldSteps       = jitterHoldSteps;
            MissEveryNth          = missEveryNth;
            VineReleaseDelaySteps = vineReleaseDelaySteps;
        }

        /// <summary>The style a skill level plays with.</summary>
        public static TimeTrialBotStyle For(TimeTrialBotSkill skill)
        {
            // Jitter is only ever added, so a level cannot come out faster than the level above it.
            switch (skill)
            {
                // Every obstacle is missed, so the run takes no input at all and the rest of the style is empty.
                case TimeTrialBotSkill.Clumsy:
                    return new TimeTrialBotStyle(F32.Zero, F32.Zero, chargePermille: 0, jitterChargePermille: 0, jitterHoldSteps: 0, missEveryNth: 1, vineReleaseDelaySteps: 0);

                case TimeTrialBotSkill.Cautious:
                    return new TimeTrialBotStyle(F32.Ratio10(12), F32.Ratio10(25), chargePermille: 1200, jitterChargePermille: 150, jitterHoldSteps: 14, missEveryNth: 2, vineReleaseDelaySteps: 12);

                case TimeTrialBotSkill.Sharp:
                    return new TimeTrialBotStyle(F32.Ratio10(2), F32.Ratio10(6), chargePermille: 1000, jitterChargePermille: 0, jitterHoldSteps: 0, missEveryNth: 0, vineReleaseDelaySteps: 0);

                default:
                    return new TimeTrialBotStyle(F32.Ratio10(4), F32.One, chargePermille: 1050, jitterChargePermille: 80, jitterHoldSteps: 8, missEveryNth: 6, vineReleaseDelaySteps: 4);
            }
        }
    }

    /// <summary>
    /// A scripted runner that plays a course start to finish and hands back the recording it produced. It is
    /// what the load-testing bot submits, what the course tests measure a competent run against, and what a
    /// course's seed ghosts are driven from. Every jump is planned by playing it forward on a copy of the
    /// simulation, so the plan is exactly what the run then does. It is deterministic: the same course, skill
    /// level and variant always yield the same recording, on the client and on the server.
    /// </summary>
    public static class TimeTrialBotDriver
    {
        /// <summary>
        /// Upper bound on the steps a planned input is played forward on a copy of the run: a jump-off until it
        /// comes down, a jump until it catches the rope or lands. Either ends well inside it; a copy still in
        /// the air past it is read as not getting there.
        /// </summary>
        const int PlannedFlightStepLimit = 240;

        /// <summary>
        /// Upper bound on the steps a swing is played forward looking for a jump-off that lands. The rope lets
        /// go of a runner who hangs on well inside it, which ends the search on its own.
        /// </summary>
        const int SwingStepLimit = 900;

        /// <summary>
        /// How far beyond <see cref="TimeTrialSimulation.MaxJumpRange"/> from a vine's anchor the runner starts
        /// trying to plan its catching jump, metres. The rope hangs behind its anchor.
        /// </summary>
        static readonly F32 VineApproachMargin = F32.FromInt(2);

        /// <summary>What <see cref="Outcome"/> reports for a flight that lands clear of everything that follows.</summary>
        const int SafeLanding = -1;

        /// <summary>What <see cref="Outcome"/> reports for a flight that never lands as a runner could go on from: it is planned no further.</summary>
        const int NoLanding = -2;

        /// <summary>Play <paramref name="course"/> at the reference pace.</summary>
        public static TimeTrialRecording Drive(CourseDefinition course, TimeTrialRules rules)
            => Drive(course, rules, TimeTrialBotSkill.Competent);

        /// <summary>
        /// Play <paramref name="course"/> at <paramref name="skill"/> and return the inputs that were pressed.
        /// The run is simulated here, so the recording is by construction one the same simulation accepts.
        /// <para>
        /// The jitter in a run is drawn from a generator seeded with the course, the skill level, the course's
        /// ruleset and <paramref name="variant"/>, so a seed ghost is reproducible from its identity alone.
        /// Variants of one level play the same style with different jitter: the same pace, a different run.
        /// A level with no jitter, such as <see cref="TimeTrialBotSkill.Sharp"/>, runs every variant alike.
        /// </para>
        /// </summary>
        public static TimeTrialRecording Drive(CourseDefinition course, TimeTrialRules rules, TimeTrialBotSkill skill, int variant = 0)
        {
            TimeTrialBotStyle    style      = TimeTrialBotStyle.For(skill);
            RandomPCG            jitter     = CreateJitter(course, skill, variant);
            TimeTrialSimulation  simulation = new TimeTrialSimulation(course, rules);
            List<TimeTrialInput> inputs     = new List<TimeTrialInput>();
            List<Obstacle>       obstacles  = FindObstacles(course);
            TakeOffPlan          plan       = TakeOffPlan.None;
            LevelReach           levelReach = new LevelReach(rules);

            int nextObstacle  = 0;
            int holdStepsLeft = -1;

            // Steps still to hang on once a jump-off would land on the branch, or -1 while none would yet.
            int vineReleaseDelayLeft = -1;

            while (!simulation.IsOver)
            {
                if (holdStepsLeft > 0)
                {
                    holdStepsLeft--;
                }
                else if (holdStepsLeft == 0)
                {
                    Record(simulation, inputs, pressed: false);
                    holdStepsLeft = -1;
                }
                else if (simulation.IsAttachedToVine)
                {
                    // Let go on the first step a jump-off comes down on the branch, or as many steps later as
                    // the style hesitates while that stays true. A swing that carries past the window (the
                    // hesitation outlasting it) lets go at the next window without hesitating again, so the
                    // runner is never left to the rope's own release into the basin.
                    if (ReleaseLandsOnTheBranch(simulation, course))
                    {
                        if (vineReleaseDelayLeft < 0)
                            vineReleaseDelayLeft = style.VineReleaseDelaySteps + (style.JitterHoldSteps > 0 ? jitter.NextInt(style.JitterHoldSteps + 1) : 0);

                        if (vineReleaseDelayLeft == 0)
                        {
                            Record(simulation, inputs, pressed: true);
                            Record(simulation, inputs, pressed: false);
                            vineReleaseDelayLeft = -1;
                        }
                        else
                        {
                            vineReleaseDelayLeft--;
                        }
                    }
                    else if (vineReleaseDelayLeft >= 0)
                    {
                        vineReleaseDelayLeft = 0;
                    }
                }
                else if (simulation.Mode == TimeTrialMode.Running)
                {
                    // Anything the runner is already level with has been missed or flown over; the next jump is
                    // for the first obstacle still ahead. So has anything under a branch the runner stands on:
                    // the branch carries it over the spike beds beneath.
                    while (nextObstacle < obstacles.Count
                        && (obstacles[nextObstacle].StartX <= simulation.X || LiesUnderTheBranchUnderfoot(course, obstacles[nextObstacle], simulation.X, simulation.Y)))
                        nextObstacle++;

                    // A vine within reach is jumped for before anything else: the rope hangs over the basin the
                    // obstacles ahead lie in, and catching it is what carries the runner past them.
                    if (TryPlanVineCatch(simulation, course, rules, style, out int catchHoldSteps))
                    {
                        plan = TakeOffPlan.None;
                        Record(simulation, inputs, pressed: true);
                        if (catchHoldSteps <= 0)
                            Record(simulation, inputs, pressed: false);
                        else
                            holdStepsLeft = catchHoldSteps;
                    }
                    else if (nextObstacle < obstacles.Count)
                    {
                        if (plan.ObstacleIndex != nextObstacle || (plan.IsPending && simulation.StepIndex > plan.RetryAfterStep))
                            plan = PlanTakeOff(simulation, obstacles, nextObstacle, rules, style);

                        // A missed obstacle is run straight into: the runner falls into the pit and mantles out
                        // of it, or walks through the spikes. Nothing on a course ends a run. An obstacle no
                        // full charge clears is run into the same way at every level, because the charge would
                        // be a dead stop in front of a flight that still ends inside it or under its face.
                        if (simulation.StepIndex >= plan.PlantStep && !IsClimbApproach(course, simulation.X, style))
                        {
                            int index = nextObstacle++;
                            if (plan.HoldSteps >= 0)
                            {
                                TakeOff takeOff   = plan.TakeOff != null && plan.PlantStep == simulation.StepIndex ? plan.TakeOff : new TakeOff(simulation, rules);
                                int     holdSteps = PlanCharge(takeOff, course, obstacles, index, plan.HoldSteps, rules, style, jitter, levelReach);

                                Record(simulation, inputs, pressed: true);
                                if (holdSteps <= 0)
                                    Record(simulation, inputs, pressed: false);
                                else
                                    holdStepsLeft = holdSteps;
                            }
                        }
                    }
                }

                simulation.Step();
            }

            return new TimeTrialRecording(inputs);
        }

        static void Record(TimeTrialSimulation simulation, List<TimeTrialInput> inputs, bool pressed)
        {
            TimeTrialInput input = new TimeTrialInput(simulation.StepIndex, pressed);
            inputs.Add(input);
            simulation.ApplyInput(input);
        }

        /// <summary>
        /// Whether letting go of the rope on this step comes down on the branch of the vine the runner holds.
        /// The run itself is left where it was.
        /// </summary>
        static bool ReleaseLandsOnTheBranch(TimeTrialSimulation simulation, CourseDefinition course)
        {
            if (course.Encounters[simulation.ActiveEncounterIndex] is not VineEncounterDefinition vine)
                return false;

            TimeTrialSimulation trial = simulation.Clone();
            trial.ApplyInput(new TimeTrialInput(trial.StepIndex, pressed: true));
            trial.ApplyInput(new TimeTrialInput(trial.StepIndex, pressed: false));
            for (int step = 0; step < PlannedFlightStepLimit && trial.Mode == TimeTrialMode.Airborne && !trial.IsOver; step++)
                trial.Step();

            return trial.Mode == TimeTrialMode.Running
                && trial.Y == vine.PlatformTopY
                && trial.X >= vine.PlatformStartX
                && trial.X < vine.PlatformEndX;
        }

        /// <summary>
        /// Whether a full-charge jump planted on this step catches the vine ahead and swings where a jump-off
        /// comes down on the branch, and the hold to spend on it.
        /// </summary>
        static bool TryPlanVineCatch(TimeTrialSimulation simulation, CourseDefinition course, TimeTrialRules rules, TimeTrialBotStyle style, out int holdSteps)
        {
            holdSteps = -1;

            // A style that misses every jump walks under the rope.
            if (style.MissEveryNth == 1)
                return false;

            VineEncounterDefinition vine = VineWithinReach(course, simulation.X, rules);
            if (vine == null)
                return false;

            // A full charge carries the hands furthest into the rope and gives the swing the most energy, and it
            // is the jump the rope is tuned on.
            TimeTrialSimulation caught = JumpToCatch(new TakeOff(simulation, rules), vine, FullChargeHold(rules));
            if (caught == null || !SomeReleaseLandsOnTheBranch(caught, course))
                return false;

            holdSteps = FullChargeHold(rules);
            return true;
        }

        /// <summary>
        /// A jump from <paramref name="takeOff"/> held for <paramref name="holdSteps"/>, played forward on a copy
        /// of the run: the copy on the step it caught <paramref name="vine"/>, or null when the flight lands,
        /// catches something else or runs out of steps first.
        /// </summary>
        static TimeTrialSimulation JumpToCatch(TakeOff takeOff, VineEncounterDefinition vine, int holdSteps)
        {
            TimeTrialSimulation trial = takeOff.Release(holdSteps);

            for (int step = 0; step < PlannedFlightStepLimit && !trial.IsOver; step++)
            {
                trial.Step();
                if (trial.IsAttachedToVine)
                    return ReferenceEquals(trial.Course.Encounters[trial.ActiveEncounterIndex], vine) ? trial : null;
                if (trial.Mode != TimeTrialMode.Airborne && trial.Mode != TimeTrialMode.Charging)
                    return null;
            }

            return null;
        }

        /// <summary>
        /// Whether the swing <paramref name="caught"/> is on, played on until the rope lets go of its own
        /// accord, passes some step a jump-off comes down on the branch.
        /// </summary>
        static bool SomeReleaseLandsOnTheBranch(TimeTrialSimulation caught, CourseDefinition course)
        {
            // Every third step is tried: a landing window is many steps wide, so none is missed.
            for (int step = 0; step < SwingStepLimit && caught.IsAttachedToVine && !caught.IsOver; step++)
            {
                if (step % 3 == 0 && ReleaseLandsOnTheBranch(caught, course))
                    return true;
                caught.Step();
            }
            return false;
        }

        /// <summary>
        /// The first vine whose anchor is ahead of <paramref name="x"/>, when it hangs within a full charge's
        /// flight of there: no further than <see cref="TimeTrialSimulation.MaxJumpRange"/> and the approach
        /// margin. Null when there is none, or when the nearest one is further out.
        /// </summary>
        static VineEncounterDefinition VineWithinReach(CourseDefinition course, F32 x, TimeTrialRules rules)
        {
            VineEncounterDefinition nearest = null;
            foreach (CourseEncounterDefinition encounter in course.Encounters)
            {
                if (encounter is VineEncounterDefinition vine && vine.AnchorX > x && (nearest == null || vine.AnchorX < nearest.AnchorX))
                    nearest = vine;
            }

            if (nearest == null || nearest.AnchorX - x > TimeTrialSimulation.MaxJumpRange(rules) + VineApproachMargin)
                return null;
            return nearest;
        }

        /// <summary>
        /// Whether <paramref name="obstacle"/> lies wholly under the branch a runner standing at
        /// (<paramref name="x"/>, <paramref name="y"/>) is on. A runner only ever stands on a branch it swung
        /// onto, and the branch carries it over everything beneath.
        /// </summary>
        static bool LiesUnderTheBranchUnderfoot(CourseDefinition course, Obstacle obstacle, F32 x, F32 y)
        {
            foreach (CourseEncounterDefinition encounter in course.Encounters)
            {
                if (encounter is VineEncounterDefinition vine
                    && x >= vine.PlatformStartX && x < vine.PlatformEndX && y == vine.PlatformTopY
                    && obstacle.StartX >= vine.PlatformStartX && obstacle.ClearX <= vine.PlatformEndX)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// When the runner takes off for <paramref name="obstacles"/>[<paramref name="index"/>], worked out from
        /// where it runs now, or a pending plan when it cannot be worked out yet.
        /// </summary>
        static TakeOffPlan PlanTakeOff(TimeTrialSimulation simulation, List<Obstacle> obstacles, int index, TimeTrialRules rules, TimeTrialBotStyle style)
        {
            Obstacle obstacle = obstacles[index];
            F32      plantX   = obstacle.StartX - style.PreJumpGap;
            bool     missed   = style.MissEveryNth > 0 && (index + 1) % style.MissEveryNth == 0;
            bool     raised   = !missed && obstacle.LandingTopY > simulation.Y;

            // A raised landing is planned from a full charge's flight before its face, so the take-off can be
            // pulled back from the style's plant. Anything else is planned at the plant itself.
            F32 planFromX = raised ? F32.Min(plantX, obstacle.FaceX - TimeTrialPlayerBody.RightExtent - TimeTrialSimulation.MaxJumpRange(rules)) : plantX;
            if (simulation.X < planFromX)
                return TakeOffPlan.Pending(index, simulation.StepIndex);

            // A run-up that leaves the ground before the plant, off a step down or into a climb, leaves the plan
            // pending until the runner is running again past that step.
            List<TimeTrialSimulation> runUp = new List<TimeTrialSimulation> { simulation.Clone() };
            if (!TryRunUpTo(runUp, plantX, obstacle.FaceX))
                return TakeOffPlan.Pending(index, simulation.StepIndex + runUp.Count);

            int plantStep = simulation.StepIndex + runUp.Count - 1;
            if (missed || (!raised && IsBeyondJumpRange(simulation, obstacle, rules)))
                return TakeOffPlan.RunInto(index, plantStep);

            if (!raised)
                return TakeOffPlan.At(index, plantStep, 0);

            if (!TryPlanRaisedTakeOff(runUp, obstacle, rules, style, out TakeOff takeOff, out int takeOffSteps, out int holdSteps))
                return TakeOffPlan.RunInto(index, plantStep);

            return TakeOffPlan.At(index, simulation.StepIndex + takeOffSteps, holdSteps, takeOff);
        }

        /// <summary>
        /// Where along <paramref name="runUp"/> to take off for the raised <paramref name="obstacle"/>, and the
        /// hold to spend from there, with the <paramref name="takeOff"/> there. <paramref name="runUp"/> is the
        /// run played forward on a copy, one entry per step from now to the style's plant. False when no take-off
        /// and hold gets the runner over the face and past the obstacle, and it is run into.
        /// </summary>
        static bool TryPlanRaisedTakeOff(List<TimeTrialSimulation> runUp, Obstacle obstacle, TimeTrialRules rules, TimeTrialBotStyle style, out TakeOff takeOff, out int takeOffSteps, out int holdSteps)
        {
            // A flight's shape does not depend on where it is launched from, so the arcs measured once from the
            // furthest take-off hold for every take-off on the run-up.
            TakeOff      furthest = new TakeOff(runUp[0], rules);
            ArcsToHeight arcs     = new ArcsToHeight(furthest, obstacle.LandingTopY, rules);
            F32          targetX  = obstacle.ClearX + style.LandingMargin;

            takeOffSteps = runUp.Count - 1;
            holdSteps    = -1;
            takeOff      = null;

            // A flight that meets the face below its lip is caught on it and mantled, which costs the charge and
            // the mantle both. So the take-off is pulled back from the style's plant until the exact charge from
            // there is up at the landing's height before the body's leading edge reaches the face. The charge
            // depends on where the take-off is and the take-off on the charge, so the take-off moves back for
            // the charge the landing takes from there, never forward again, until it is far enough back.
            for (int round = 0; round <= FullChargeHold(rules); round++)
            {
                takeOff = takeOffSteps == 0 ? furthest : new TakeOff(runUp[takeOffSteps], rules);

                if (!arcs.TryLeastHoldComingDownPast(targetX - runUp[takeOffSteps].X, out holdSteps, out bool reaches))
                    holdSteps = ExactHold(takeOff, targetX, rules, -1, null, out reaches);

                // A launch that cannot reach the landing from this take-off takes a full charge.
                F32 approach = reaches ? arcs.Approach(holdSteps) : F32.FromInt(-1);
                if (approach < F32.Zero)
                {
                    holdSteps = FullChargeHold(rules);
                    approach  = arcs.Approach(holdSteps);
                }
                if (approach < F32.Zero)
                    return false;

                F32 farEnough = obstacle.FaceX - TimeTrialPlayerBody.RightExtent - approach;
                if (runUp[takeOffSteps].X < farEnough)
                    return true;

                while (takeOffSteps >= 0 && runUp[takeOffSteps].X >= farEnough)
                    takeOffSteps--;

                if (takeOffSteps < 0)
                {
                    // Already too close for the charge the landing takes: a full charge now, if it gets past.
                    takeOffSteps = 0;
                    takeOff      = furthest;
                    holdSteps    = FullChargeHold(rules);
                    return furthest.Fly(holdSteps).ReachX >= obstacle.ClearX;
                }
            }

            return true;
        }

        /// <summary>
        /// The jumps from one take-off measured against one landing height above it, each played forward on a
        /// copy of the simulation once and only as far as needed: how far along the course a hold is when its
        /// feet are first up at that height (its approach), and how far along it is on the step it comes back
        /// down through it, which is where it lands on ground of that height (its descent). A flight's shape does
        /// not depend on where it is launched from, so both hold for a take-off anywhere on the same ground.
        /// </summary>
        sealed class ArcsToHeight
        {
            readonly TakeOff _takeOff;
            readonly F32     _heightY;
            readonly F32[]   _approach;
            readonly F32[]   _descent;
            readonly bool[]  _measured;
            readonly bool[]  _complete;

            public ArcsToHeight(TakeOff takeOff, F32 heightY, TimeTrialRules rules)
            {
                _takeOff  = takeOff;
                _heightY  = heightY;
                _approach = new F32[FullChargeHold(rules) + 1];
                _descent  = new F32[_approach.Length];
                _measured = new bool[_approach.Length];
                _complete = new bool[_approach.Length];
            }

            /// <summary>How far a jump held for <paramref name="holdSteps"/> travels before it is up at the height, or negative when it never is.</summary>
            public F32 Approach(int holdSteps)
            {
                Measure(holdSteps);
                return _approach[holdSteps];
            }

            /// <summary>
            /// The least hold whose descent is at least <paramref name="distance"/>: the exact charge for a
            /// landing that far along on ground at the height. A full charge, with <paramref name="reaches"/>
            /// false, when no hold comes down that far. False when a hold the search needed came down onto
            /// something before its descent could be measured.
            /// </summary>
            public bool TryLeastHoldComingDownPast(F32 distance, out int holdSteps, out bool reaches)
            {
                int fullHold = _approach.Length - 1;
                int shortOf  = -1;
                int reach    = fullHold;

                holdSteps = fullHold;
                reaches   = false;

                // A descent only lengthens with the hold, so the range of holds is halved.
                while (reach - shortOf > 1)
                {
                    int middle = (shortOf + reach) / 2;
                    if (!TryComesDownPast(middle, distance, out bool past))
                        return false;
                    if (past)
                        reach = middle;
                    else
                        shortOf = middle;
                }

                if (!TryComesDownPast(reach, distance, out reaches))
                    return false;

                holdSteps = reach;
                return true;
            }

            bool TryComesDownPast(int holdSteps, F32 distance, out bool past)
            {
                Measure(holdSteps);
                past = _descent[holdSteps] >= distance;
                return _complete[holdSteps];
            }

            /// <summary>
            /// Fly the hold until it comes back down through the height, lands, or meets something.
            /// </summary>
            void Measure(int holdSteps)
            {
                if (_measured[holdSteps])
                    return;

                TimeTrialSimulation trial = _takeOff.Release(holdSteps);
                F32                 fromX = _takeOff.Simulation.X;
                F32                 up    = F32.FromInt(-1);
                F32                 down  = F32.FromInt(-1);
                bool                done  = false;

                for (int step = 0; step < PlannedFlightStepLimit && !trial.IsOver && !done; step++)
                {
                    trial.Step();

                    if (trial.Mode == TimeTrialMode.Airborne && !IsAtAFace(trial))
                    {
                        if (up < F32.Zero && trial.Y >= _heightY)
                        {
                            up = trial.X - fromX;
                        }
                        else if (up >= F32.Zero && trial.Y < _heightY)
                        {
                            down = trial.X - fromX;
                            done = true;
                        }
                        continue;
                    }

                    // Landed on ground at the height, which is the descent, or stopped. A flight stopped before it
                    // was ever up at the height never is from any take-off further on either, so that is a
                    // complete measurement with no approach and no descent. One stopped after it is not.
                    if (up >= F32.Zero && (trial.Mode == TimeTrialMode.Running || trial.Mode == TimeTrialMode.Stumbling) && trial.Y == _heightY)
                        down = trial.X - fromX;
                    done = down >= F32.Zero || up < F32.Zero;
                    break;
                }

                _approach[holdSteps] = up;
                _descent[holdSteps]  = down;
                _complete[holdSteps] = done;
                _measured[holdSteps] = true;
            }
        }

        /// <summary>
        /// Whether <paramref name="obstacle"/>, whose landing is no higher than the take-off, is wider than a
        /// full charge clears even when planted on the last step the runner is still short of it.
        /// </summary>
        static bool IsBeyondJumpRange(TimeTrialSimulation simulation, Obstacle obstacle, TimeTrialRules rules)
        {
            // Judged from the obstacle's edge rather than the style's plant, so every skill level reads the same
            // pit the same way.
            TimeTrialSimulation edge = simulation.Clone();
            while (true)
            {
                TimeTrialSimulation next = edge.Clone();
                next.Step();
                if (next.IsOver || next.Mode != TimeTrialMode.Running || next.X >= obstacle.StartX)
                    break;
                edge = next;
            }

            return new TakeOff(edge, rules).Fly(FullChargeHold(rules)).ReachX < obstacle.ClearX;
        }

        /// <summary>
        /// Run <paramref name="runUp"/>'s last copy on, a step at a time, adding each step's copy, until it
        /// reaches <paramref name="x"/> or comes up against the face at <paramref name="faceX"/> short of it: the
        /// last step it is still running is as far as it gets. False when it leaves the ground first any other
        /// way, a face before that one included.
        /// </summary>
        static bool TryRunUpTo(List<TimeTrialSimulation> runUp, F32 x, F32 faceX)
        {
            TimeTrialSimulation last = runUp[runUp.Count - 1];
            while (last.X < x)
            {
                if (last.IsOver)
                    return false;

                TimeTrialSimulation next = last.Clone();
                next.Step();
                if (next.Mode != TimeTrialMode.Running)
                    return IsAtAFace(next) && next.BodyRight == faceX;

                runUp.Add(next);
                last = next;
            }
            return true;
        }

        /// <summary>Whether <paramref name="simulation"/>'s runner is up against a face: blocked by it, or caught on it.</summary>
        static bool IsAtAFace(TimeTrialSimulation simulation)
            => simulation.ActiveEncounterIndex >= 0 && simulation.Course.Encounters[simulation.ActiveEncounterIndex] is LedgeGrabDefinition;

        /// <summary>
        /// How long to hold the button for <paramref name="obstacles"/>[<paramref name="index"/>] from where the
        /// runner stands, at least <paramref name="plantedHoldSteps"/>.
        /// </summary>
        static int PlanCharge(TakeOff takeOff, CourseDefinition course, List<Obstacle> obstacles, int index, int plantedHoldSteps, TimeTrialRules rules, TimeTrialBotStyle style, RandomPCG jitter, LevelReach levelReach)
        {
            int extraPermille = style.JitterChargePermille > 0 ? jitter.NextInt(style.JitterChargePermille + 1) : 0;
            int extraSteps    = style.JitterHoldSteps > 0 ? jitter.NextInt(style.JitterHoldSteps + 1) : 0;

            // The floor is the exact charge that lands the style's margin past the obstacle, or what the plant was
            // worked out for, whichever is more.
            int target     = index;
            int targetHold = ExactHold(takeOff, obstacles[target].ClearX + style.LandingMargin, rules, plantedHoldSteps, levelReach, out bool _);
            int floor      = Math.Max(plantedHoldSteps, targetHold);
            int outcome    = Outcome(takeOff.Fly(floor), obstacles, index, rules, out F32 _);

            // A landing that comes to grief on a later obstacle is planned over that one as well, margin past
            // it, for as long as a charge reaches that far and clears every face on the way. When none does, the
            // floor is spent and the landing takes what it finds.
            for (int attempt = 0; outcome >= 0 && attempt < obstacles.Count; attempt++)
            {
                int over     = outcome;
                int overHold = ExactHold(takeOff, obstacles[over].ClearX + style.LandingMargin, rules, -1, levelReach, out bool _);
                int hold     = Math.Max(floor, overHold);
                int extended = Outcome(takeOff.Fly(hold), obstacles, index, rules, out F32 _);
                while (extended == over && hold < FullChargeHold(rules))
                    extended = Outcome(takeOff.Fly(++hold), obstacles, index, rules, out F32 _);

                if (extended == over || extended == NoLanding || over < target)
                    break;

                floor      = hold;
                target     = over;
                targetHold = overHold;
                outcome    = extended;
            }

            // A vine within a full charge's flight of the landing is caught from a short stretch of ground
            // just past the obstacle, so the runner comes down as near it as its margin allows: charge spent
            // beyond the floor would carry it past the take-off for the rope.
            if (VineWithinReach(course, obstacles[target].ClearX, rules) != null)
                return floor;

            // The style's own charge and this jump's jitter come on top, as far as the flight stays clear and
            // comes down no further than the next obstacle: extra charge never buys an obstacle flown over for
            // free.
            int ceiling = Math.Max(floor, Math.Min(FullChargeHold(rules), (int)((long)targetHold * (style.ChargePermille + extraPermille) / 1000) + extraSteps));
            F32 stopX   = target + 1 < obstacles.Count ? obstacles[target + 1].StartX : F32.MaxValue;
            int clear   = -1;

            for (int hold = ceiling; hold > floor; hold--)
            {
                if (Outcome(takeOff.Fly(hold), obstacles, index, rules, out F32 landingX) != SafeLanding)
                    continue;
                if (landingX <= stopX)
                    return hold;
                if (clear < 0)
                    clear = hold;
            }

            return clear >= 0 ? clear : floor;
        }

        /// <summary>
        /// The least hold whose flight from <paramref name="takeOff"/> comes down at or past
        /// <paramref name="targetX"/>: the exact charge for a landing there. A full charge, with
        /// <paramref name="reaches"/> false, when no hold gets that far. <paramref name="hint"/> is a hold
        /// expected to be near it, or not above zero to start from what <paramref name="levelReach"/> suggests.
        /// </summary>
        static int ExactHold(TakeOff takeOff, F32 targetX, TimeTrialRules rules, int hint, LevelReach levelReach, out bool reaches)
        {
            if (hint <= 0 && levelReach != null)
                hint = levelReach.Hint(targetX - takeOff.Simulation.X);

            int fullHold = FullChargeHold(rules);

            bool Reaches(int hold)
            {
                Flight flight = takeOff.Fly(hold);
                levelReach?.Record(takeOff, hold, flight);
                return flight.ReachX >= targetX;
            }

            // A flight only carries further the longer the button is held, so the search strides out from the
            // hint, doubling until it passes the target, then halves. It keeps a hold that falls short and one
            // that reaches: -1 stands for a hold that falls short without being flown, and the full charge for
            // one that reaches until it is flown.
            int shortOf;
            int reach;
            if (hint < 0 || hint >= fullHold)
            {
                shortOf = -1;
                reach   = fullHold;
            }
            else if (Reaches(hint))
            {
                shortOf = -1;
                reach   = hint;
                for (int stride = 1; reach > 0; stride *= 2)
                {
                    int probe = Math.Max(0, reach - stride);
                    if (!Reaches(probe))
                    {
                        shortOf = probe;
                        break;
                    }
                    reach = probe;
                }
            }
            else
            {
                shortOf = hint;
                reach   = -1;
                for (int stride = 1; reach < 0; stride *= 2)
                {
                    int probe = Math.Min(fullHold, shortOf + stride);
                    if (Reaches(probe))
                    {
                        reach = probe;
                    }
                    else if (probe == fullHold)
                    {
                        reaches = false;
                        return fullHold;
                    }
                    else
                    {
                        shortOf = probe;
                    }
                }
            }

            while (reach - shortOf > 1)
            {
                int middle = (shortOf + reach) / 2;
                if (Reaches(middle))
                    reach = middle;
                else
                    shortOf = middle;
            }

            // A search that ends on the full charge has not necessarily flown it: it is taken on trust until then.
            reaches = reach < fullHold || Reaches(fullHold);
            return reach;
        }

        /// <summary>
        /// The shortest hold that launches at full charge. Holding any longer launches the same flight, only on
        /// its own.
        /// </summary>
        static int FullChargeHold(TimeTrialRules rules)
            => Math.Max(1, rules.MaxChargeSteps - 1);

        /// <summary>
        /// Fly <paramref name="trial"/>, a copy of the run let go on this step, until it comes down, is caught on
        /// a face, catches a rope or is carried on by the course.
        /// </summary>
        static Flight FlyOn(TimeTrialSimulation trial)
        {
            bool rising = trial.VelocityY > F32.Zero;

            for (int step = 0; step <= PlannedFlightStepLimit; step++)
            {
                if (trial.IsFinished)
                    return new Flight(FlightEnd.CarriedOn, trial, F32.MaxValue, rising);
                if (trial.ActiveEncounterIndex >= 0 && trial.Course.Encounters[trial.ActiveEncounterIndex] is LedgeGrabDefinition ledge)
                    return new Flight(FlightEnd.CaughtOnFace, trial, trial.X, rising, ledge.X);
                if (trial.IsAttachedToVine || trial.Mode == TimeTrialMode.Climbing || trial.Mode == TimeTrialMode.Traversing)
                    return new Flight(FlightEnd.CarriedOn, trial, trial.X, rising);
                if (trial.Mode == TimeTrialMode.Running || trial.Mode == TimeTrialMode.Stumbling)
                    return new Flight(FlightEnd.Landed, trial, trial.X, rising);
                if (trial.IsOver)
                    break;

                rising = trial.VelocityY > F32.Zero;
                trial.Step();
            }

            return new Flight(FlightEnd.NoLanding, trial, trial.X, rising);
        }

        /// <summary>
        /// What a planned flight comes to for the obstacles from <paramref name="fromIndex"/> on:
        /// <see cref="SafeLanding"/>, <see cref="NoLanding"/>, or the index of the obstacle it comes to grief
        /// on. <paramref name="landingX"/> is where it came down.
        /// </summary>
        static int Outcome(Flight flight, List<Obstacle> obstacles, int fromIndex, TimeTrialRules rules, out F32 landingX)
        {
            landingX = flight.ReachX;
            switch (flight.End)
            {
                case FlightEnd.CarriedOn:
                    return SafeLanding;

                case FlightEnd.CaughtOnFace:
                {
                    for (int ndx = fromIndex; ndx < obstacles.Count; ndx++)
                    {
                        if (obstacles[ndx].FaceX == flight.FaceX)
                            return ndx;
                    }
                    return NoLanding;
                }

                case FlightEnd.Landed:
                {
                    TimeTrialSimulation landed = flight.Trial;
                    for (int ndx = fromIndex; ndx < obstacles.Count; ndx++)
                    {
                        // Whatever the runner is level with when it lands is flown over, as the drive counts it,
                        // unless the landing is inside it.
                        Obstacle ahead = obstacles[ndx];
                        if (ahead.StartX <= landed.X)
                        {
                            if (landed.X < ahead.ClearX)
                                return ndx;
                            continue;
                        }

                        return IsTooCloseToClear(landed, ahead, rules) ? ndx : SafeLanding;
                    }
                    return SafeLanding;
                }

                default:
                    return NoLanding;
            }
        }

        /// <summary>
        /// Whether a runner standing where <paramref name="landed"/> came down is too close to the raised
        /// <paramref name="ahead"/> for any jump to clear its face: a full charge planted there is caught on the
        /// face while still rising.
        /// </summary>
        static bool IsTooCloseToClear(TimeTrialSimulation landed, Obstacle ahead, TimeTrialRules rules)
        {
            if (landed.Mode != TimeTrialMode.Running || ahead.LandingTopY <= landed.Y || ahead.FaceX - landed.BodyRight > TimeTrialSimulation.MaxJumpRange(rules))
                return false;

            Flight flight = new TakeOff(landed, rules).Fly(FullChargeHold(rules));
            return flight.End == FlightEnd.CaughtOnFace && flight.FaceX == ahead.FaceX && flight.RisingAtTheEnd;
        }

        /// <summary>How a flight played forward on a copy of the simulation ended.</summary>
        enum FlightEnd
        {
            /// <summary>Came down on ground, a pit floor or a bed of spikes, and ran on or stumbled.</summary>
            Landed,

            /// <summary>Met a face below its lip: blocked by it or caught on it to be mantled.</summary>
            CaughtOnFace,

            /// <summary>Taken on by the course: a rope caught, a climb entered, or the finish line crossed.</summary>
            CarriedOn,

            /// <summary>Still in the air when the planning step limit ran out.</summary>
            NoLanding,
        }

        /// <summary>
        /// A flight played forward on a copy of the simulation: how it ended, the copy on the step it ended,
        /// how far along the course it got, whether it was still rising on the step before, and the face it was
        /// caught on when it was.
        /// </summary>
        readonly struct Flight
        {
            public FlightEnd           End            { get; }
            public TimeTrialSimulation Trial          { get; }
            public F32                 ReachX         { get; }
            public bool                RisingAtTheEnd { get; }
            public F32                 FaceX          { get; }

            public Flight(FlightEnd end, TimeTrialSimulation trial, F32 reachX, bool risingAtTheEnd, F32 faceX = default)
            {
                End            = end;
                Trial          = trial;
                ReachX         = reachX;
                RisingAtTheEnd = risingAtTheEnd;
                FaceX          = faceX;
            }
        }

        /// <summary>
        /// When the runner plants its feet for one obstacle and the hold that plant was worked out for, or -1 to
        /// run into it. A pending plan has no plant step yet and is made again once the runner is running past
        /// <see cref="RetryAfterStep"/>. A plan worked out on a copy of the run keeps the <see cref="TakeOff"/>
        /// it was worked out from, whose jumps are the ones the runner has to choose from on the plant step.
        /// </summary>
        readonly struct TakeOffPlan
        {
            public int     ObstacleIndex  { get; }
            public int     PlantStep      { get; }
            public int     HoldSteps      { get; }
            public int     RetryAfterStep { get; }
            public TakeOff TakeOff        { get; }

            public bool IsPending => PlantStep == int.MaxValue;

            TakeOffPlan(int obstacleIndex, int plantStep, int holdSteps, int retryAfterStep, TakeOff takeOff)
            {
                ObstacleIndex  = obstacleIndex;
                PlantStep      = plantStep;
                HoldSteps      = holdSteps;
                RetryAfterStep = retryAfterStep;
                TakeOff        = takeOff;
            }

            public static TakeOffPlan None => new TakeOffPlan(-1, int.MaxValue, -1, -1, null);

            public static TakeOffPlan Pending(int obstacleIndex, int retryAfterStep) => new TakeOffPlan(obstacleIndex, int.MaxValue, -1, retryAfterStep, null);

            public static TakeOffPlan At(int obstacleIndex, int plantStep, int holdSteps, TakeOff takeOff = null) => new TakeOffPlan(obstacleIndex, plantStep, holdSteps, -1, takeOff);

            public static TakeOffPlan RunInto(int obstacleIndex, int plantStep) => new TakeOffPlan(obstacleIndex, plantStep, -1, -1, null);
        }

        /// <summary>
        /// How far along the course each hold has come down on ground as high as it was launched from, in the
        /// jumps a drive has flown so far. A flight's shape does not depend on where it is launched from, so a
        /// hold that has landed level once lands as far again from anywhere else on level ground. It only tells
        /// the search for an exact charge (<see cref="ExactHold"/>) where to start: the jumps from the take-off
        /// itself decide it.
        /// </summary>
        sealed class LevelReach
        {
            readonly F32[]  _distance;
            readonly bool[] _known;

            public LevelReach(TimeTrialRules rules)
            {
                _distance = new F32[FullChargeHold(rules) + 1];
                _known    = new bool[_distance.Length];
            }

            /// <summary>Note how far <paramref name="flight"/>, held for <paramref name="holdSteps"/> from <paramref name="takeOff"/>, came down, if it landed level.</summary>
            public void Record(TakeOff takeOff, int holdSteps, Flight flight)
            {
                if (holdSteps < 0 || holdSteps >= _distance.Length || flight.End != FlightEnd.Landed || flight.Trial.Y != takeOff.Simulation.Y)
                    return;

                _distance[holdSteps] = flight.ReachX - takeOff.Simulation.X;
                _known[holdSteps]    = true;
            }

            /// <summary>
            /// The least hold known to come down at least <paramref name="distance"/> along, or the one after the
            /// longest known to fall short of it; -1 when nothing is known.
            /// </summary>
            public int Hint(F32 distance)
            {
                int shortOf = -1;
                for (int hold = 0; hold < _distance.Length; hold++)
                {
                    if (!_known[hold])
                        continue;
                    if (_distance[hold] >= distance)
                        return hold;
                    shortOf = hold;
                }
                return shortOf < 0 ? -1 : Math.Min(shortOf + 1, _distance.Length - 1);
            }
        }

        /// <summary>
        /// A step the runner could plant its feet on, and the jumps from there played forward on copies of the
        /// simulation, with the press, hold and release timed exactly as <see cref="Drive"/> records them. The
        /// charge is played once and each hold flown at most once.
        /// </summary>
        sealed class TakeOff
        {
            /// <summary>The run pressed on the take-off step, then one entry per step charged after it.</summary>
            readonly List<TimeTrialSimulation> _charged = new List<TimeTrialSimulation>();
            readonly Flight[]                  _flights;
            readonly bool[]                    _flown;

            public TimeTrialSimulation Simulation { get; }

            public TakeOff(TimeTrialSimulation simulation, TimeTrialRules rules)
            {
                Simulation = simulation;
                _flights   = new Flight[FullChargeHold(rules) + 1];
                _flown     = new bool[_flights.Length];
            }

            /// <summary>A copy of the run planted here and held for <paramref name="holdSteps"/>, on the step it is let go.</summary>
            public TimeTrialSimulation Release(int holdSteps)
            {
                if (_charged.Count == 0)
                {
                    TimeTrialSimulation pressed = Simulation.Clone();
                    pressed.ApplyInput(new TimeTrialInput(pressed.StepIndex, pressed: true));
                    _charged.Add(pressed);
                }

                // The press step and every held step charge, so a hold of n launches at n + 1 steps of charge, and
                // a press released in its own step is a tap.
                int chargedSteps = holdSteps <= 0 ? 0 : holdSteps + 1;
                while (_charged.Count <= chargedSteps)
                {
                    TimeTrialSimulation next = _charged[_charged.Count - 1].Clone();
                    next.Step();
                    _charged.Add(next);
                }

                TimeTrialSimulation trial = _charged[chargedSteps].Clone();
                trial.ApplyInput(new TimeTrialInput(trial.StepIndex, pressed: false));
                return trial;
            }

            /// <summary>The flight of a jump from here held for <paramref name="holdSteps"/> (<see cref="FlyOn"/>).</summary>
            public Flight Fly(int holdSteps)
            {
                if (holdSteps < 0 || holdSteps >= _flights.Length)
                    return FlyOn(Release(holdSteps));

                if (!_flown[holdSteps])
                {
                    _flights[holdSteps] = FlyOn(Release(holdSteps));
                    _flown[holdSteps]   = true;
                }
                return _flights[holdSteps];
            }
        }

        /// <summary>
        /// The generator a run's jitter is drawn from. Seeded from the course, the skill level and the current
        /// <see cref="TimeTrialConstants.RulesetVersion"/>, so the same seed ghost comes out wherever it is driven and a new
        /// ruleset gives a course fresh opponents rather than replaying stale ones.
        /// <para>
        /// A non-zero <paramref name="variant"/> is folded in last, and variant 0 folds in nothing, so variant 0
        /// is exactly the level's seed ghost and its recording does not depend on how many variants are driven.
        /// </para>
        /// </summary>
        static RandomPCG CreateJitter(CourseDefinition course, TimeTrialBotSkill skill, int variant)
        {
            ulong seed = CourseIdHash.Of(course.Id);
            seed ^= (ulong)((int)skill + 1) * 0x9E3779B97F4A7C15UL;
            seed ^= unchecked((ulong)TimeTrialConstants.RulesetVersion * 0xC2B2AE3D27D4EB4FUL);
            if (variant != 0)
                seed ^= (ulong)(uint)variant * 0x165667B19E3779F9UL;
            return RandomPCG.CreateFromSeed(seed);
        }

        /// <summary>
        /// Whether the runner is close enough to a climb that planting its feet would strand it: from a stride
        /// before the bottom torii, where a charge would only be wasted, to the gate the climb hands input back
        /// at. The driver runs straight into a climb, which is the only thing a climb accepts.
        /// </summary>
        static bool IsClimbApproach(CourseDefinition course, F32 x, TimeTrialBotStyle style)
        {
            foreach (ClimbDefinition climb in course.Climbs)
            {
                if (x + style.PreJumpGap >= climb.BottomX && x < climb.GateX)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Everything on the course worth jumping, in the order the runner meets it: the pits, the steps up in
        /// the ground, and the beds of spikes outside the pits.
        /// </summary>
        static List<Obstacle> FindObstacles(CourseDefinition course)
        {
            List<Obstacle> obstacles = new List<Obstacle>();

            foreach (PitDefinition pit in course.Pits)
            {
                int landingIndex = course.FindGroundIndexAt(pit.EndX);
                F32 landingTopY  = landingIndex >= 0 ? course.Ground[landingIndex].TopY : F32.Zero;
                Insert(obstacles, new Obstacle(pit.StartX, pit.EndX, landingTopY, faceX: pit.EndX));
            }

            // A step up is ground meeting higher ground edge to edge. The step from a pit's floor up to its far
            // lip is the pit itself, and a step a bed of spikes runs up to is that bed's face, so neither is a
            // second obstacle.
            for (int ndx = 0; ndx + 1 < course.Ground.Count; ndx++)
            {
                GroundSegment here = course.Ground[ndx];
                GroundSegment next = course.Ground[ndx + 1];

                if (next.StartX != here.EndX || next.TopY <= here.TopY || IsPitFloor(course, here) || HasHazardEndingAt(course, next.StartX))
                    continue;

                Insert(obstacles, new Obstacle(next.StartX, next.StartX, next.TopY, faceX: next.StartX));
            }

            foreach (HazardSpan hazard in course.Hazards)
            {
                // Spikes in a pit are cleared with the pit, and counting them separately would shift which
                // obstacles a style misses.
                if (LiesInPit(course, hazard))
                    continue;

                // The landing is the ground past the far edge of the bed, as it is for a pit: spikes laid at the
                // foot of a step up are cleared together with the step, so the jump is charged for the step's
                // height and planted for its face (which stands where that ground begins) rather than flown
                // into it.
                int groundIndex = course.FindGroundIndexAt(hazard.EndX);
                F32 landingTopY = groundIndex >= 0 ? course.Ground[groundIndex].TopY : F32.Zero;
                F32 faceX       = groundIndex >= 0 ? F32.Max(hazard.StartX, course.Ground[groundIndex].StartX) : hazard.EndX;
                Insert(obstacles, new Obstacle(hazard.StartX, hazard.EndX, landingTopY, faceX));
            }

            return obstacles;
        }

        /// <summary>
        /// Whether <paramref name="hazard"/> lies on the floor of one of the course's pits.
        /// </summary>
        static bool LiesInPit(CourseDefinition course, HazardSpan hazard)
        {
            foreach (PitDefinition pit in course.Pits)
            {
                if (hazard.StartX >= pit.StartX && hazard.EndX <= pit.EndX)
                    return true;
            }
            return false;
        }

        /// <summary>Whether a bed of spikes outside every pit ends exactly at <paramref name="x"/>.</summary>
        static bool HasHazardEndingAt(CourseDefinition course, F32 x)
        {
            foreach (HazardSpan hazard in course.Hazards)
            {
                if (hazard.EndX == x && !LiesInPit(course, hazard))
                    return true;
            }
            return false;
        }

        /// <summary>Whether <paramref name="segment"/> is the floor of one of the course's pits.</summary>
        static bool IsPitFloor(CourseDefinition course, GroundSegment segment)
        {
            foreach (PitDefinition pit in course.Pits)
            {
                if (segment.StartX == pit.StartX && segment.EndX == pit.EndX)
                    return true;
            }
            return false;
        }

        /// <summary>Insert in order of where the runner meets it, keeping the list deterministic.</summary>
        static void Insert(List<Obstacle> obstacles, Obstacle obstacle)
        {
            int position = obstacles.Count;
            while (position > 0 && obstacles[position - 1].StartX > obstacle.StartX)
                position--;
            obstacles.Insert(position, obstacle);
        }

        /// <summary>Something the runner has to get over, and the ground it lands on afterwards.</summary>
        readonly struct Obstacle
        {
            /// <summary>Where the trouble starts; the runner plants its feet just before this.</summary>
            public F32 StartX { get; }

            /// <summary>Where the runner is clear of it again.</summary>
            public F32 ClearX { get; }

            /// <summary>Height of the ground on the far side, which decides how much the jump has to climb.</summary>
            public F32 LandingTopY { get; }

            /// <summary>
            /// Where the ground rises to <see cref="LandingTopY"/>: the face a flight still below that height is
            /// caught on. Meaningful only when the landing is above the take-off.
            /// </summary>
            public F32 FaceX { get; }

            public Obstacle(F32 startX, F32 clearX, F32 landingTopY, F32 faceX)
            {
                StartX      = startX;
                ClearX      = clearX;
                LandingTopY = landingTopY;
                FaceX       = faceX;
            }
        }
    }
}
