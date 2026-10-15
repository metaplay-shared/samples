using Metaplay.Core.Math;

namespace Game.Logic
{
    /// <summary>
    /// Deterministic runtime for automatic catches on solid, finite ledge faces: the far wall of every pit and
    /// the face of every step up in the ground. Hanging and the top transition
    /// are step counts; the rise between them is a speed, so a catch further down a face costs more.
    /// </summary>
    public sealed partial class TimeTrialSimulation
    {
        bool[] _ledgeConsumed;
        int _ledgePhaseSteps;
        int _blockedLedgeIndex = -1;
        F32 _ledgeRiseStartY;

        void LedgeGrabInitializeEncounters()
        {
            _ledgeConsumed = new bool[_course.Encounters.Count];
        }

        /// <summary>
        /// Hanging and pulling up are one automatic deterministic sequence. Input state is still recorded by
        /// ApplyInput, but no transition can shorten, cancel or otherwise alter the sequence.
        /// </summary>
        bool LedgeGrabApplyEncounterInput()
            => Mode == TimeTrialMode.Traversing && ActiveLedge != null;

        bool LedgeGrabStepEncounter()
        {
            LedgeGrabDefinition ledge = ActiveLedge;
            if (ledge == null)
                return false;

            if (_encounterPhase == EncounterPhaseKind.LedgeHang)
            {
                _ledgePhaseSteps++;
                EncounterProgress = F32.Clamp01(F32.Ratio(_ledgePhaseSteps, _rules.LedgeGrabHangSteps));
                if (_ledgePhaseSteps >= _rules.LedgeGrabHangSteps)
                {
                    _ledgePhaseSteps = 0;
                    _encounterPhase = EncounterPhaseKind.LedgeRise;
                    EncounterProgress = F32.Zero;
                }
                return true;
            }

            if (_encounterPhase == EncounterPhaseKind.LedgeRise)
            {
                // The rise is a speed rather than a step count, so a catch further down the face costs
                // proportionally more time to climb out of.
                F32 clearanceY = ledge.TopY + _rules.LedgeGrabClearanceHeight;

                if (Y >= clearanceY)
                {
                    _ledgePhaseSteps = 0;
                    _encounterPhase = EncounterPhaseKind.LedgeTopTransition;
                    EncounterProgress = F32.Zero;
                    return true;
                }

                X = ledge.X - TimeTrialPlayerBody.RightExtent;
                Y = F32.Min(clearanceY, Y + _rules.LedgeGrabRiseSpeed * _stepDuration);
                EncounterProgress = F32.Clamp01(F32.Div(Y - _ledgeRiseStartY, clearanceY - _ledgeRiseStartY));

                return true;
            }

            if (_ledgePhaseSteps >= _rules.LedgeGrabTopTransitionSteps)
            {
                FinishPullUp();
                return true;
            }

            _ledgePhaseSteps++;
            F32 transitionProgress = F32.Clamp01(F32.Ratio(_ledgePhaseSteps, _rules.LedgeGrabTopTransitionSteps));
            EncounterProgress = transitionProgress;
            X = F32.Lerp(ledge.X - TimeTrialPlayerBody.RightExtent, ledge.LandingX, transitionProgress);
            Y = F32.Lerp(ledge.TopY + _rules.LedgeGrabClearanceHeight, ledge.TopY, transitionProgress);
            return true;
        }

        void LedgeGrabPrepareAirborneEncounterMotion()
        {
            if (_blockedLedgeIndex < 0)
                return;

            LedgeGrabDefinition ledge = (LedgeGrabDefinition)_course.Encounters[_blockedLedgeIndex];
            if (Y + TimeTrialPlayerBody.Height <= ledge.BottomY)
            {
                _blockedLedgeIndex = -1;
                ActiveEncounterIndex = -1;
                _encounterPhase = EncounterPhaseKind.None;
                EncounterProgress = F32.Zero;
                VelocityX = _rules.RunSpeed;
            }
            else
            {
                // Wall contact preserves vertical momentum but never restores horizontal movement while any
                // part of the body overlaps the face.
                VelocityX = F32.Zero;
                EncounterProgress = F32.Clamp01(F32.Div(
                    ledge.TopY - (Y + TimeTrialPlayerBody.Height),
                    ledge.TopY - ledge.BottomY));
            }
        }

        bool LedgeGrabTryResolveAirborneEncounterVerticalMotion(F32 previousY, F32 newY)
        {
            if (_blockedLedgeIndex < 0 || _encounterPhase != EncounterPhaseKind.LedgeWallRise)
                return false;

            LedgeGrabDefinition ledge = (LedgeGrabDefinition)_course.Encounters[_blockedLedgeIndex];

            X = ledge.X - TimeTrialPlayerBody.RightExtent;
            VelocityX = F32.Zero;

            if (VelocityY > F32.Zero && previousY < ledge.MinimumGrabY && newY >= ledge.MinimumGrabY)
            {
                _blockedLedgeIndex = -1;
                CatchLedge(ActiveEncounterIndex, ledge, ledge.MinimumGrabY);
                return true;
            }

            if (VelocityY <= F32.Zero)
                _encounterPhase = EncounterPhaseKind.LedgeImpact;
            return false;
        }

        void LedgeGrabOnAirborneLanded()
        {
            if (_blockedLedgeIndex < 0)
                return;

            _blockedLedgeIndex = -1;
            ActiveEncounterIndex = -1;
            _encounterPhase = EncounterPhaseKind.None;
            EncounterProgress = F32.Zero;
        }

        bool LedgeGrabTryEnterEncounter(F32 previousX, F32 previousY, F32 newX, F32 newY)
        {
            if (newX <= previousX)
                return false;

            for (int ndx = 0; ndx < _course.Encounters.Count; ndx++)
            {
                if (_course.Encounters[ndx] is not LedgeGrabDefinition ledge)
                    continue;
                F32 previousRight = previousX + TimeTrialPlayerBody.RightExtent;
                F32 newRight = newX + TimeTrialPlayerBody.RightExtent;
                if (previousRight > ledge.X || newRight < ledge.X)
                    continue;

                F32 crossingFraction = F32.Div(ledge.X - previousRight, newRight - previousRight);
                F32 contactY = F32.Lerp(previousY, newY, crossingFraction);
                F32 bodyTop = contactY + TimeTrialPlayerBody.Height;
                if (bodyTop <= ledge.BottomY || contactY >= ledge.TopY)
                    continue;

                X = ledge.X - TimeTrialPlayerBody.RightExtent;
                Y = contactY;
                VelocityX = F32.Zero;

                if (!_ledgeConsumed[ndx] && contactY >= ledge.MinimumGrabY)
                    CatchLedge(ndx, ledge, contactY);
                else
                {
                    _blockedLedgeIndex = ndx;
                    ActiveEncounterIndex = ndx;
                    _encounterPhase = VelocityY > F32.Zero ? EncounterPhaseKind.LedgeWallRise : EncounterPhaseKind.LedgeImpact;
                    EncounterProgress = F32.Clamp01(F32.Div(ledge.TopY - bodyTop, ledge.TopY - ledge.BottomY));
                    Mode = TimeTrialMode.Airborne;
                }

                return true;
            }

            return false;
        }

        LedgeGrabDefinition ActiveLedge
        {
            get
            {
                if (ActiveEncounterIndex < 0 || ActiveEncounterIndex >= _course.Encounters.Count)
                    return null;
                return _course.Encounters[ActiveEncounterIndex] as LedgeGrabDefinition;
            }
        }

        void CatchLedge(int encounterIndex, LedgeGrabDefinition ledge, F32 contactY)
        {
            _ledgeConsumed[encounterIndex] = true;
            _ledgePhaseSteps = 0;
            _ledgeRiseStartY = contactY;
            ActiveEncounterIndex = encounterIndex;
            _encounterPhase = EncounterPhaseKind.LedgeHang;
            EncounterProgress = F32.Zero;
            X = ledge.X - TimeTrialPlayerBody.RightExtent;
            Y = contactY;
            VelocityX = F32.Zero;
            VelocityY = F32.Zero;
            Mode = TimeTrialMode.Traversing;
        }

        void FinishPullUp()
        {
            VelocityX = _rules.LedgeGrabResumeSpeed;
            VelocityY = F32.Zero;
            Mode = TimeTrialMode.Running;
            ActiveEncounterIndex = -1;
            _encounterPhase = EncounterPhaseKind.None;
            EncounterProgress = F32.Zero;
            _ledgePhaseSteps = 0;
        }

        /// <summary>The deterministic running speed restored after a completed mantle.</summary>
        public F32 LedgeGrabResumeSpeed => _rules.LedgeGrabResumeSpeed;

        /// <summary>The actual swept-contact Y from which the current automatic grab rises.</summary>
        public F32 LedgeGrabStartY => _ledgeRiseStartY;
    }
}
