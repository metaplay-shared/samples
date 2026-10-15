using Metaplay.Core.Math;

namespace Game.Logic
{
    /// <summary>
    /// Deterministic runtime for climbs: the stretch between a climb's two torii, where the course carries the
    /// runner and the button does nothing.
    /// </summary>
    public sealed partial class TimeTrialSimulation
    {
        /// <summary>Whether a climb has already taken the runner, so it never claims the same runner twice.</summary>
        bool[] _climbTaken;

        /// <summary>The height of the flat each climb's slope starts from, read off the ground that ends at the slope's foot.</summary>
        F32[] _climbBottomY;

        /// <summary>The climb that holds the button down, or -1 when the input is the player's own.</summary>
        int _climbHoldingInput = -1;

        /// <summary>
        /// The bottom torii the step being simulated reached, and the motion that carried the runner to it. The
        /// torii is timed once the step is complete, against where that motion would have put the runner rather
        /// than where the climb set it down.
        /// </summary>
        bool _climbEntryPending;
        F32  _climbEntryLineX;
        F32  _climbEntryPreviousX;
        F32  _climbEntryReachedX;

        /// <summary>Whether the course is doing the moving and the button does nothing at all.</summary>
        public bool IsInputLocked => _climbHoldingInput >= 0;

        void ClimbInitializeEncounters()
        {
            _climbTaken   = new bool[_course.Encounters.Count];
            _climbBottomY = new F32[_course.Encounters.Count];

            for (int ndx = 0; ndx < _course.Encounters.Count; ndx++)
            {
                if (_course.Encounters[ndx] is not ClimbDefinition climb)
                    continue;

                // CourseDefinition refuses a climb with no ground ending at the foot of its slope.
                _climbBottomY[ndx] = _course.FindGroundSegmentEndingAt(climb.SlopeStartX).TopY;
            }
        }

        bool ClimbApplyEncounterInput()
        {
            ClimbReleaseIfPastGate();

            // The transition is still recorded: the recording is what the runner pressed, not what it was
            // allowed to do with it. But nothing between the torii acts on it.
            return _climbHoldingInput >= 0;
        }

        bool ClimbStepEncounter()
        {
            if (Mode != TimeTrialMode.Climbing)
                return false;

            ClimbDefinition climb = ActiveClimb;
            if (climb == null)
                return false;

            F32 newX = X + _rules.RunSpeed * _stepDuration;
            if (newX >= climb.TopX)
            {
                // Over the plateau and back on the runner's own legs at full pace, which is the pace the whole
                // slope was taken at, so the run-out costs nothing to get back up to speed.
                X                 = climb.TopX;
                Y                 = climb.TopY;
                VelocityX         = _rules.RunSpeed;
                VelocityY         = F32.Zero;
                Mode              = TimeTrialMode.Running;
                _encounterPhase   = EncounterPhaseKind.RunOut;
                EncounterProgress = F32.Zero;
                return true;
            }

            X                 = newX;
            Y                 = climb.SurfaceYAt(X, _climbBottomY[ActiveEncounterIndex]);
            EncounterProgress = climb.SlopeProgressAt(X);
            return true;
        }

        bool ClimbTryEnterEncounter(F32 previousX, F32 newX)
        {
            for (int ndx = 0; ndx < _course.Encounters.Count; ndx++)
            {
                if (_course.Encounters[ndx] is not ClimbDefinition climb)
                    continue;
                if (_climbTaken[ndx] || newX < climb.BottomX)
                    continue;

                // A jump that carried past the bottom torii is caught under it rather than over it: the runner
                // is set down where it got to, on the flat or the slope, and taken from there.
                _climbTaken[ndx]     = true;
                _climbEntryPending   = true;
                _climbEntryLineX     = climb.BottomX;
                _climbEntryPreviousX = previousX;
                _climbEntryReachedX  = newX;
                X                    = F32.Min(F32.Max(newX, climb.BottomX), climb.TopX);
                Y                    = climb.SurfaceYAt(X, _climbBottomY[ndx]);
                VelocityX            = _rules.RunSpeed;
                VelocityY            = F32.Zero;
                Mode                 = TimeTrialMode.Climbing;
                ActiveEncounterIndex = ndx;
                _encounterPhase       = EncounterPhaseKind.Climbing;
                EncounterProgress    = climb.SlopeProgressAt(X);
                _climbHoldingInput   = ndx;
                return true;
            }

            return false;
        }

        void ClimbOnStepCompleted()
        {
            RecordClimbEntry();
            ClimbReleaseIfPastGate();
        }

        /// <summary>
        /// Time the bottom torii the step just run reached, interpolated inside that step exactly as a gate and
        /// the finish line are.
        /// </summary>
        void RecordClimbEntry()
        {
            if (!_climbEntryPending)
                return;

            _climbEntryPending = false;
            _climbEntryTimesMs.Add(CrossingTimeMs(_climbEntryPreviousX, _climbEntryReachedX, _climbEntryLineX));
        }

        /// <summary>
        /// Hand the button and the renderer-facing encounter state back once the gate is behind the runner.
        /// </summary>
        void ClimbReleaseIfPastGate()
        {
            if (_climbHoldingInput < 0)
                return;

            // Input stays locked along the run-out, so every runner crosses a gate on the ground at running pace
            // and nobody opens the next segment in a state of their own choosing.
            ClimbDefinition climb = (ClimbDefinition)_course.Encounters[_climbHoldingInput];
            if (X < climb.GateX)
                return;

            _climbHoldingInput = -1;
            if (Mode != TimeTrialMode.Climbing)
            {
                ActiveEncounterIndex = -1;
                _encounterPhase       = EncounterPhaseKind.None;
                EncounterProgress    = F32.Zero;
            }
        }

        ClimbDefinition ActiveClimb
        {
            get
            {
                if (ActiveEncounterIndex < 0 || ActiveEncounterIndex >= _course.Encounters.Count)
                    return null;
                return _course.Encounters[ActiveEncounterIndex] as ClimbDefinition;
            }
        }
    }
}
