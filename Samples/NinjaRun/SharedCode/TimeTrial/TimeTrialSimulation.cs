using Metaplay.Core.Math;
using System;
using System.Collections.Generic;

namespace Game.Logic
{
    /// <summary>
    /// What the runner is doing right now. Drives the animation as well as the step logic. This is transient
    /// run state, never stored, so it carries no serialization contract.
    /// </summary>
    public enum TimeTrialMode
    {
        /// <summary>On the ground, moving towards <see cref="TimeTrialRules.RunSpeed"/>.</summary>
        Running = 0,

        /// <summary>Stopped with the button held, building up launch strength.</summary>
        Charging = 1,

        /// <summary>In the air, under gravity, with horizontal velocity unchanged.</summary>
        Airborne = 2,

        /// <summary>Stopped after touching spikes.</summary>
        Stumbling = 3,

        /// <summary>Across the finish line. The run is over and <see cref="TimeTrialSimulation.Result"/> is set.</summary>
        Finished = 4,

        /// <summary>A contextual course encounter temporarily owns movement and input.</summary>
        Traversing = 5,

        /// <summary>Being carried up a climb's slope. Input does nothing until the climb's gate is behind.</summary>
        Climbing = 6,
    }

    /// <summary>
    /// The deterministic fixed-step time trial simulation: one runner, one course, one set of rules. The web
    /// client drives it live (<see cref="ApplyInput"/> then <see cref="Step"/> each frame) and the server runs
    /// the submitted recording through the very same code (<see cref="Replay"/>), so the two agree on a finish
    /// time without either trusting the other's arithmetic.
    /// <para>
    /// Everything here is fixed-point or integer: <see cref="F32"/> positions and velocities, integer step
    /// counts, an integer finish time in milliseconds. Floating point is only ever read out of it for drawing.
    /// </para>
    /// <para>
    /// This state is transient and is not part of <see cref="PlayerModel"/>: the player model owns the results
    /// of runs, not the 60 Hz state inside one.
    /// </para>
    /// </summary>
    public sealed partial class TimeTrialSimulation
    {
        readonly CourseDefinition _course;
        readonly TimeTrialRules   _rules;
        readonly F32              _stepDuration;

        // Not readonly: Clone gives the copy its own instances of everything a run changes.
        bool[]                    _hazardTripped;
        List<int>                 _gateTimesMs = new List<int>();
        List<int>                 _climbEntryTimesMs = new List<int>();

        /// <summary>
        /// How far below the last stretch of ground a fall off the end of the course is held, metres. A course
        /// carries ground under its finish line, so nothing reaches this; holding the fall keeps the
        /// fixed-point state in range until the step budget ends the run.
        /// </summary>
        static readonly F32 OffCourseFallDepth = F32.FromInt(8);

        int _chargeSteps;
        int _stoppedStepsLeft;

        /// <summary>The step of the latest press made while airborne, or -1 when there is none to carry into a landing.</summary>
        int _airbornePressStep = -1;

        /// <summary>The step the simulation will run next. Zero before the first <see cref="Step"/>.</summary>
        public int StepIndex { get; private set; }

        public TimeTrialMode Mode { get; private set; }

        /// <summary>The runner's feet, in metres along the course.</summary>
        public F32 X { get; private set; }

        /// <summary>The runner's feet, in metres above the course baseline.</summary>
        public F32 Y { get; private set; }

        public F32 VelocityX { get; private set; }
        public F32 VelocityY { get; private set; }

        public F32 BodyRight => X + TimeTrialPlayerBody.RightExtent;
        public F32 BodyBottom => Y;
        public F32 BodyTop => Y + TimeTrialPlayerBody.Height;

        /// <summary>The physical state of the one button after all transitions applied to this step.</summary>
        public bool IsButtonPressed { get; private set; }

        /// <summary>The active entry in <see cref="CourseDefinition.Encounters"/>, or -1 outside an encounter.</summary>
        public int ActiveEncounterIndex { get; private set; } = -1;

        /// <summary>
        /// A renderer-facing phase name owned by the active encounter, or empty outside one.
        /// </summary>
        public string EncounterPhase => NameOf(_encounterPhase);

        /// <summary>The phase of the active encounter, which <see cref="EncounterPhase"/> names.</summary>
        EncounterPhaseKind _encounterPhase;

        /// <summary>An optional normalized renderer-facing value owned by the active encounter.</summary>
        public F32 EncounterProgress { get; private set; }

        /// <summary>
        /// The interpolated crossing time of each gate the runner has passed, in milliseconds and in course
        /// order.
        /// </summary>
        public IReadOnlyList<int> GateTimesMs => _gateTimesMs;

        /// <summary>The gate the runner is racing towards, or <see cref="CourseDefinition.Gates"/>.Count past the last one.</summary>
        public int NextGateIndex => _gateTimesMs.Count;

        /// <summary>
        /// The interpolated crossing time of each climb's bottom torii the runner has reached, in milliseconds
        /// and in course order. A segment is closed there, so this is timed exactly as the gates are.
        /// </summary>
        public IReadOnlyList<int> ClimbEntryTimesMs => _climbEntryTimesMs;

        /// <summary>The climb the runner is racing towards, or <see cref="CourseDefinition.Climbs"/>.Count past the last one.</summary>
        public int NextClimbIndex => _climbEntryTimesMs.Count;

        /// <summary>The result the simulation computed, or null until the finish line is crossed.</summary>
        public TimeTrialResult Result { get; private set; }

        public CourseDefinition Course => _course;

        /// <summary>True once the finish line has been crossed.</summary>
        public bool IsFinished => Mode == TimeTrialMode.Finished;

        /// <summary>
        /// True once the run can produce nothing further: either it finished, or it used up
        /// <see cref="TimeTrialConstants.MaxRunSteps"/> without reaching the finish line. Stepping past this
        /// does nothing.
        /// </summary>
        public bool IsOver => IsFinished || StepIndex >= TimeTrialConstants.MaxRunSteps;

        /// <summary>How far the jump is charged, 0 at a tap and 1 at the strength that forces a launch.</summary>
        public F32 ChargeFraction =>
            Mode == TimeTrialMode.Charging
                ? F32.Clamp01(F32.Ratio(_chargeSteps, _rules.MaxChargeSteps))
                : F32.Zero;

        /// <summary>Time on the runner's clock: whole steps run so far, in milliseconds.</summary>
        public int ElapsedMs => TimeTrialConstants.StepsToMilliseconds(StepIndex);

        public TimeTrialSimulation(CourseDefinition course, TimeTrialRules rules)
        {
            _course        = course ?? throw new ArgumentNullException(nameof(course));
            _rules         = rules ?? throw new ArgumentNullException(nameof(rules));
            _stepDuration  = F32.Ratio(1, TimeTrialConstants.StepsPerSecond);
            _hazardTripped = new bool[course.Hazards.Count];

            // A flying start: the runner crosses the start line already at running pace, the way the client
            // shows it running in from behind the line, so the clock starts on a runner in full stride.
            X         = course.StartX;
            Mode      = TimeTrialMode.Running;
            VelocityX = rules.RunSpeed;

            int startGround = course.FindGroundIndexAt(X);
            Y               = startGround >= 0 ? course.Ground[startGround].TopY : F32.Zero;

            InitializeEncounters();
        }

        /// <summary>
        /// An independent copy of this simulation at its current step: stepping either one, or applying input to
        /// it, never moves the other. The course, the rules and the per-course tables filled at construction are
        /// shared, since nothing writes to them during a run; every collection a run changes is copied.
        /// </summary>
        public TimeTrialSimulation Clone()
        {
            TimeTrialSimulation copy = (TimeTrialSimulation)MemberwiseClone();
            copy._hazardTripped     = (bool[])_hazardTripped.Clone();
            copy._gateTimesMs       = new List<int>(_gateTimesMs);
            copy._climbEntryTimesMs = new List<int>(_climbEntryTimesMs);
            copy._climbTaken        = (bool[])_climbTaken.Clone();
            copy._vineConsumed      = (bool[])_vineConsumed.Clone();
            copy._ledgeConsumed     = (bool[])_ledgeConsumed.Clone();
            return copy;
        }

        /// <summary>
        /// Apply one recorded button transition for the current <see cref="StepIndex"/>. It takes effect before
        /// that step is simulated.
        /// </summary>
        public void ApplyInput(TimeTrialInput input)
        {
            if (input.Step != StepIndex)
                throw new InvalidOperationException($"Input is for step {input.Step} but the simulation is at step {StepIndex}");

            if (IsOver)
                return;

            IsButtonPressed = input.Pressed;

            if (ApplyEncounterInput(input))
                return;

            if (input.Pressed)
            {
                // Pressing while running plants the feet: horizontal movement stops immediately and the jump
                // starts charging from nothing.
                if (Mode == TimeTrialMode.Running)
                {
                    Mode         = TimeTrialMode.Charging;
                    VelocityX    = F32.Zero;
                    _chargeSteps = 0;
                }
                // A press in the air is remembered, so held into a landing it charges from there.
                else if (Mode == TimeTrialMode.Airborne)
                {
                    _airbornePressStep = StepIndex;
                }
            }
            else
            {
                _airbornePressStep = -1;
                if (Mode == TimeTrialMode.Charging)
                    Launch();
            }
        }

        /// <summary>Simulate one fixed step.</summary>
        public void Step()
        {
            if (IsOver)
                return;

            F32 previousX = X;

            StepPassiveEncounter();

            switch (Mode)
            {
                case TimeTrialMode.Running:    StepRunning();    break;
                case TimeTrialMode.Charging:   StepCharging();   break;
                case TimeTrialMode.Airborne:   StepAirborne();   break;
                case TimeTrialMode.Stumbling:  StepStopped();    break;
                case TimeTrialMode.Traversing:
                case TimeTrialMode.Climbing:
                    if (!StepEncounter())
                        throw new InvalidOperationException($"{Mode} mode has no encounter runtime");
                    break;
            }

            StepIndex++;

            RecordGateCrossings(previousX);
            OnStepCompleted();

            if (previousX < _course.FinishX && X >= _course.FinishX)
                Finish(previousX);
        }

        /// <summary>
        /// Time each gate crossed during this step, interpolated inside the step exactly as the finish time is,
        /// so a segment time means the same thing wherever the gate sits inside a frame. A gate is timed on the
        /// first crossing only.
        /// </summary>
        void RecordGateCrossings(F32 previousX)
        {
            while (_gateTimesMs.Count < _course.Gates.Count)
            {
                F32 gate = _course.Gates[_gateTimesMs.Count];
                if (previousX >= gate || X < gate)
                    break;

                _gateTimesMs.Add(CrossingTimeMs(previousX, gate));
            }
        }

        /// <summary>
        /// Where in the step just run <paramref name="lineX"/> was crossed, on the runner's clock in
        /// milliseconds.
        /// </summary>
        int CrossingTimeMs(F32 previousX, F32 lineX) => CrossingTimeMs(previousX, X, lineX);

        /// <summary>
        /// The same timing, measured against <paramref name="reachedX"/>, where the step's motion carried the
        /// runner, for a step that ended with the runner set down somewhere other than that.
        /// </summary>
        int CrossingTimeMs(F32 previousX, F32 reachedX, F32 lineX)
        {
            // Where in the step the line was crossed, as a fraction of the distance covered by it.
            F32 crossingFraction = F32.Div(lineX - previousX, reachedX - previousX);

            // Steps up to the crossing, in 16.16: the steps completed before this one, plus that fraction.
            long one            = F32.One.Raw;
            long stepsAtCrossing = (long)(StepIndex - 1) * one + crossingFraction.Raw;
            long perSecond      = (long)TimeTrialConstants.StepsPerSecond * one;

            return (int)((stepsAtCrossing * 1000 + perSecond / 2) / perSecond);
        }

        /// <summary>
        /// Run a whole recording from the start and return the result it produces, or null if the runner never
        /// reached the finish line. This is the server's route: it takes the submitted inputs and recomputes
        /// the outcome for itself rather than reading a time off the client.
        /// <para>
        /// The recording must already be well formed (<see cref="TimeTrialRecording.IsWellFormed"/>).
        /// </para>
        /// </summary>
        public static TimeTrialResult Replay(CourseDefinition course, TimeTrialRules rules, TimeTrialRecording recording)
        {
            TimeTrialSimulation  simulation = new TimeTrialSimulation(course, rules);
            List<TimeTrialInput> inputs     = recording?.Inputs ?? new List<TimeTrialInput>();
            int                  nextInput  = 0;

            while (!simulation.IsOver)
            {
                while (nextInput < inputs.Count && inputs[nextInput].Step == simulation.StepIndex)
                    simulation.ApplyInput(inputs[nextInput++]);

                simulation.Step();
            }

            return simulation.Result;
        }

        #region Step logic

        void StepRunning()
        {
            // Ground speed eases towards the running pace from either side, so a landing that came in fast
            // keeps its momentum for a moment and a runner who stopped to charge or stumbled builds back up.
            F32 speedChange = _rules.RunAcceleration * _stepDuration;
            if (VelocityX < _rules.RunSpeed)
                VelocityX = F32.Min(_rules.RunSpeed, VelocityX + speedChange);
            else if (VelocityX > _rules.RunSpeed)
                VelocityX = F32.Max(_rules.RunSpeed, VelocityX - speedChange);

            F32 newX = X + VelocityX * _stepDuration;
            if (TryEnterEncounter(X, Y, newX, Y))
                return;

            X = newX;

            if (!HasSurfaceAt(X, Y))
            {
                // Ran off the end of the ground or a vine's branch, or onto ground at a different
                // height. Either way the runner leaves the floor with the speed it had; the airborne rules sort
                // out the landing, the fall into the pit, or the ledge that was run into.
                Mode      = TimeTrialMode.Airborne;
                VelocityY = F32.Zero;
                return;
            }

            TripHazardAt(X);
        }

        void StepCharging()
        {
            // Holding past full charge launches on its own, so a held button cannot stall a run. Charging for
            // exactly MaxChargeSteps steps and releasing there produce the same launch.
            if (_chargeSteps >= _rules.MaxChargeSteps)
            {
                Launch();
                StepAirborne();
                return;
            }

            _chargeSteps++;
        }

        void StepAirborne()
        {
            PrepareAirborneEncounterMotion();
            VelocityY -= _rules.Gravity * _stepDuration;

            F32 previousY = Y;
            F32 newX      = X + VelocityX * _stepDuration;
            F32 newY      = Y + VelocityY * _stepDuration;

            if (TryResolveAirborneEncounterVerticalMotion(previousY, newY))
                return;

            if (TryEnterEncounter(X, previousY, newX, newY))
                return;

            // A surface an encounter has unlocked (a caught vine's branch) lands the runner above whatever
            // ground lies under it, and without touching the spikes there.
            if (TryFindEncounterLanding(newX, previousY, newY, out F32 encounterTopY))
            {
                CompleteLanding(newX, encounterTopY, tripHazard: false);
                return;
            }

            int groundIndex = _course.FindGroundIndexAt(newX);
            if (groundIndex >= 0)
            {
                GroundSegment segment = _course.Ground[groundIndex];

                if (newY < segment.TopY)
                {
                    if (previousY >= segment.TopY && VelocityY <= F32.Zero)
                    {
                        // Came down through the top of the ground: land on it, keeping horizontal momentum.
                        CompleteLanding(newX, segment.TopY, tripHazard: true);
                        return;
                    }

                    // Inside ground that was never crossed from above. Every step up in the ground carries a
                    // catchable face, so the runner is taken by that face before it can get here; the residual
                    // case stands it on the surface where it already is rather than moving it along the course.
                    CompleteLanding(X, segment.TopY, tripHazard: false);
                    return;
                }

                X = newX;
                Y = newY;
                return;
            }

            // Off the end of the course, with nothing below. The fall is held at OffCourseFallDepth.
            X = newX;
            Y = newY;

            if (_course.Ground.Count > 0)
            {
                F32 held = _course.Ground[_course.Ground.Count - 1].TopY - OffCourseFallDepth;
                if (Y < held)
                {
                    Y         = held;
                    VelocityY = F32.Zero;
                }
            }
        }

        void StepStopped()
        {
            _stoppedStepsLeft--;
            if (_stoppedStepsLeft <= 0)
                Mode = TimeTrialMode.Running;
        }

        void CompleteLanding(F32 x, F32 surfaceY, bool tripHazard)
        {
            X               = x;
            Y               = surfaceY;
            VelocityY       = F32.Zero;
            Mode            = TimeTrialMode.Running;
            OnAirborneLanded();
            if (tripHazard)
                TripHazardAt(X);

            // A press made in the air no more than JumpBufferSteps before this step, and still held, plants the
            // feet on touchdown exactly as a press on the ground would. A landing that stumbles or is taken by
            // an encounter drops it.
            int pressedStepsAgo = StepIndex - _airbornePressStep;
            if (Mode == TimeTrialMode.Running && IsButtonPressed && _airbornePressStep >= 0 && pressedStepsAgo < _rules.JumpBufferSteps)
            {
                Mode         = TimeTrialMode.Charging;
                VelocityX    = F32.Zero;
                _chargeSteps = 0;
            }
            _airbornePressStep = -1;
        }

        void Launch()
        {
            F32 charge    = F32.Clamp01(F32.Ratio(_chargeSteps, _rules.MaxChargeSteps));
            F32 speed     = F32.Lerp(_rules.MinLaunchSpeed, _rules.MaxLaunchSpeed, charge);
            F32 angle     = F32.DegToRad(_rules.LaunchAngleDegrees);

            VelocityX    = speed * F32.Cos(angle);
            VelocityY    = speed * F32.Sin(angle);
            Mode         = TimeTrialMode.Airborne;
            _chargeSteps = 0;
        }

        /// <summary>
        /// The widest gap a full charge clears over level ground under <paramref name="rules"/>, metres: how far
        /// past its take-off point a runner launched at <see cref="TimeTrialRules.MaxLaunchSpeed"/> comes back
        /// down to the height it left from. A simulated flight lands up to one step's travel short of this, never
        /// beyond it.
        /// </summary>
        public static F32 MaxJumpRange(TimeTrialRules rules)
        {
            // Level-ground projectile range, v² sin(2θ) / g. The fixed step applies gravity before it moves the
            // runner, which is why a simulated flight falls short of it.
            F32 launchSpread = F32.Sin(F32.DegToRad(rules.LaunchAngleDegrees * 2));
            if (launchSpread <= F32.Zero || rules.Gravity <= F32.Zero)
                return F32.Zero;

            return F32.Div(rules.MaxLaunchSpeed * rules.MaxLaunchSpeed * launchSpread, rules.Gravity);
        }

        void TripHazardAt(F32 x)
        {
            int groundIndex = _course.FindGroundIndexAt(x);
            if (groundIndex < 0 || _course.Ground[groundIndex].TopY != Y)
                return;

            for (int ndx = 0; ndx < _course.Hazards.Count; ndx++)
            {
                if (_hazardTripped[ndx])
                    continue;

                HazardSpan hazard = _course.Hazards[ndx];
                if (x >= hazard.StartX && x < hazard.EndX)
                {
                    _hazardTripped[ndx] = true;
                    Mode                = TimeTrialMode.Stumbling;
                    VelocityX           = F32.Zero;
                    VelocityY           = F32.Zero;
                    _stoppedStepsLeft   = _rules.StumbleSteps;
                    return;
                }
            }
        }

        /// <summary>
        /// Whether a runner standing at (<paramref name="x"/>, <paramref name="topY"/>) has something under its
        /// feet: the course's ground, or a surface an encounter has unlocked for this run.
        /// </summary>
        bool HasSurfaceAt(F32 x, F32 topY)
        {
            return _course.HasSurfaceAt(x, topY) || HasEncounterSurfaceAt(x, topY);
        }

        /// <summary>
        /// End the run on the step the finish line was crossed, timing the crossing inside that step the same
        /// way every torii before it was timed.
        /// </summary>
        void Finish(F32 previousX)
        {
            int finishTimeMs = CrossingTimeMs(previousX, _course.FinishX);

            Mode      = TimeTrialMode.Finished;
            ActiveEncounterIndex = -1;
            _encounterPhase      = EncounterPhaseKind.None;
            EncounterProgress    = F32.Zero;
            VelocityX = F32.Zero;
            VelocityY = F32.Zero;
            Result    = new TimeTrialResult(
                finishTimeMs:      finishTimeMs,
                steps:             StepIndex,
                gateTimesMs:       new List<int>(_gateTimesMs),
                climbEntryTimesMs: new List<int>(_climbEntryTimesMs));
        }

        #endregion
    }
}
