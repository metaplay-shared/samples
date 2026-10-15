using Metaplay.Core.Math;
using System;

namespace Game.Logic
{
    public sealed partial class TimeTrialSimulation
    {
        bool[] _vineConsumed;
        VineEncounterDefinition _activeVine;
        VineEncounterDefinition _releasedVine;
        F32 _vineAngle;
        F32 _vineAngularVelocity;
        F32 _vineGripLength;
        bool _vineWasCaught;
        bool _vineReachedUpperRoute;
        F32 _vineGripX;
        F32 _vineGripY;
        F32 _vineSwingTravel;
        int _vineRightApexCount;
        bool _vineForcedReleaseArmed;
        bool _vineBottomCrossed;
        VineReleaseKind _vineReleaseReason = VineReleaseKind.None;
        VineVisualKind _vineVisualState = VineVisualKind.Idle;

        /// <summary>Why the runner last let go of a vine, which <see cref="VineReleaseReason"/> names for the renderer.</summary>
        enum VineReleaseKind
        {
            None,
            Attached,
            Manual,
            AntiStallBottom,
        }

        /// <summary>What the rope is doing, which <see cref="VineVisualState"/> names for the renderer.</summary>
        enum VineVisualKind
        {
            Idle,
            Attached,
            Settling,
            Hanging,
        }

        public bool IsAttachedToVine => _activeVine != null;
        public bool HasReleasedVine => _vineWasCaught && !IsAttachedToVine;
        public F32 VineGripLength => _vineGripLength;
        public F32 VineGripX => _vineGripX;
        public F32 VineGripY => _vineGripY;
        public int VineRightApexCount => _vineRightApexCount;
        public bool VineForcedReleaseArmed => _vineForcedReleaseArmed;
        public bool VineBottomCrossed => _vineBottomCrossed;
        public string VineReleaseReason
        {
            get
            {
                switch (_vineReleaseReason)
                {
                    case VineReleaseKind.Attached:        return "attached";
                    case VineReleaseKind.Manual:          return "manual";
                    case VineReleaseKind.AntiStallBottom: return "anti-stall-bottom";
                    default:                              return "none";
                }
            }
        }
        public F32 VineVisualAngle => _vineAngle;
        public F32 VineVisualAngularVelocity => _vineAngularVelocity;
        public string VineVisualState
        {
            get
            {
                switch (_vineVisualState)
                {
                    case VineVisualKind.Attached: return "attached";
                    case VineVisualKind.Settling: return "settling";
                    case VineVisualKind.Hanging:  return "hanging";
                    default:                      return "idle";
                }
            }
        }
        public string VineRouteOutcome
        {
            get
            {
                if (_vineReachedUpperRoute)
                    return "upper";
                if (_vineWasCaught && (Mode == TimeTrialMode.Running || Mode == TimeTrialMode.Stumbling))
                    return "lower";
                return "pending";
            }
        }

        void VineInitializeEncounters()
        {
            _vineConsumed = new bool[_course.Encounters.Count];
        }

        void VineStepPassiveEncounter()
        {
            if (_vineVisualState != VineVisualKind.Settling || _releasedVine == null)
                return;

            F32 angularAcceleration = -_rules.Gravity * F32.Sin(_vineAngle) / _releasedVine.Length;
            _vineAngularVelocity += angularAcceleration * _stepDuration;
            _vineAngularVelocity *= F32.Max(F32.Zero, F32.One - F32.FromInt(2) * _stepDuration);
            _vineAngle += _vineAngularVelocity * _stepDuration;

            F32 settledEpsilon = F32.Ratio(1, 1000);
            if (F32.Abs(_vineAngle) <= settledEpsilon && F32.Abs(_vineAngularVelocity) <= settledEpsilon)
            {
                _vineAngle = F32.Zero;
                _vineAngularVelocity = F32.Zero;
                _vineVisualState = VineVisualKind.Hanging;
            }
        }

        /// <summary>A press while hanging lets go. The rope owns input only while the runner holds it.</summary>
        bool VineApplyEncounterInput(TimeTrialInput input)
        {
            if (Mode != TimeTrialMode.Traversing || _activeVine == null)
                return false;

            if (input.Pressed)
                ReleaseVine(VineReleaseKind.Manual);
            return true;
        }

        bool VineStepEncounter()
        {
            if (_activeVine == null)
                return false;

            F32 radialSpeed = F32.Zero;
            if (_vineGripLength < _activeVine.Length)
            {
                radialSpeed    = _activeVine.SlideSpeed;
                _vineGripLength = F32.Min(
                    _activeVine.Length,
                    _vineGripLength + radialSpeed * _stepDuration);
                if (_vineGripLength == _activeVine.Length)
                    radialSpeed = F32.Zero;
            }

            F32 previousAngle = _vineAngle;
            F32 previousAngularVelocity = _vineAngularVelocity;
            F32 angularAcceleration = -_rules.Gravity * F32.Sin(_vineAngle) / _vineGripLength;
            _vineAngularVelocity += angularAcceleration * _stepDuration;
            _vineAngularVelocity *= F32.Max(
                F32.Zero,
                F32.One - _activeVine.AngularDamping * _stepDuration);
            _vineAngle += _vineAngularVelocity * _stepDuration;

            F32 sin = F32.Sin(_vineAngle);
            F32 cos = F32.Cos(_vineAngle);
            X = _activeVine.AnchorX + _vineGripLength * sin;
            Y = _activeVine.AnchorY - _vineGripLength * cos - VineEncounterDefinition.RunnerGripHeight;
            F32 newGripX = X;
            F32 newGripY = Y + VineEncounterDefinition.RunnerGripHeight;
            F32 gripDeltaX = newGripX - _vineGripX;
            F32 gripDeltaY = newGripY - _vineGripY;
            _vineSwingTravel += F32.Sqrt(gripDeltaX * gripDeltaX + gripDeltaY * gripDeltaY);
            _vineGripX = newGripX;
            _vineGripY = newGripY;
            VelocityX = radialSpeed * sin + _vineGripLength * cos * _vineAngularVelocity;
            VelocityY = -radialSpeed * cos + _vineGripLength * sin * _vineAngularVelocity;

            if (previousAngularVelocity > F32.Zero
                && _vineAngularVelocity <= F32.Zero
                && _vineAngle > F32.Zero)
            {
                _vineRightApexCount++;
                if (_vineRightApexCount >= 2)
                    _vineForcedReleaseArmed = true;
            }

            EncounterProgress = F32.Clamp01(F32.Ratio(_vineRightApexCount, 2));
            if (_vineForcedReleaseArmed
                && _vineAngularVelocity < F32.Zero
                && previousAngle > F32.Zero
                && _vineAngle <= F32.Zero)
            {
                _vineBottomCrossed = true;
                ReleaseVine(VineReleaseKind.AntiStallBottom);
            }

            return true;
        }

        /// <summary>
        /// The branch a vine's swing lands on is a one-way surface the catch unlocks: a runner descending
        /// through its top, inside its span, lands on it only once that runner has caught the rope in this run.
        /// Before the catch it is not there, so a jump from the ground below passes through it. Reports the
        /// branch's top, for an ordinary landing on it.
        /// </summary>
        bool VineTryFindPlatformLanding(F32 x, F32 previousY, F32 newY, out F32 topY)
        {
            topY = F32.Zero;
            if (VelocityY > F32.Zero)
                return false;

            for (int ndx = 0; ndx < _course.Encounters.Count; ndx++)
            {
                if (!_vineConsumed[ndx] || _course.Encounters[ndx] is not VineEncounterDefinition vine)
                    continue;

                if (previousY >= vine.PlatformTopY
                    && newY < vine.PlatformTopY
                    && x >= vine.PlatformStartX
                    && x < vine.PlatformEndX)
                {
                    _vineReachedUpperRoute = true;
                    topY = vine.PlatformTopY;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Whether a runner standing at (<paramref name="x"/>, <paramref name="topY"/>) stands on the branch of a
        /// vine it has caught. Once landed there the runner runs, charges and jumps exactly as on the ground, and
        /// leaves it off its far end; the ground under it never sees the runner.
        /// </summary>
        bool VineHasPlatformSurfaceAt(F32 x, F32 topY)
        {
            for (int ndx = 0; ndx < _course.Encounters.Count; ndx++)
            {
                if (!_vineConsumed[ndx] || _course.Encounters[ndx] is not VineEncounterDefinition vine)
                    continue;

                if (x >= vine.PlatformStartX && x < vine.PlatformEndX && vine.PlatformTopY == topY)
                    return true;
            }

            return false;
        }

        bool VineTryEnterEncounter(F32 previousX, F32 previousY, F32 newX, F32 newY)
        {
            for (int ndx = 0; ndx < _course.Encounters.Count; ndx++)
            {
                if (_course.Encounters[ndx] is not VineEncounterDefinition vine)
                    continue;

                if (_vineConsumed[ndx])
                    continue;

                // Only a runner near the rope is tested against it. The swept test squares distances, and a
                // fixed-point square wraps negative once the runner is far enough down the course from the
                // anchor, which would read as a catch from anywhere on a long map.
                F32 reach = vine.Length + vine.ContactRadius;
                if (F32.Max(previousX, newX) < vine.AnchorX - reach || F32.Min(previousX, newX) > vine.AnchorX + reach)
                    continue;

                F32 restSin = F32.Sin(vine.RestAngle);
                F32 restCos = F32.Cos(vine.RestAngle);
                F32 vineStartX = vine.AnchorX + vine.MinimumGripLength * restSin;
                F32 vineStartY = vine.AnchorY - vine.MinimumGripLength * restCos;
                F32 vineEndX   = vine.AnchorX + vine.Length * restSin;
                F32 vineEndY   = vine.AnchorY - vine.Length * restCos;
                F32 vineFraction = F32.Zero;
                if (!SweptSegmentsAreWithinRadius(
                    previousX,
                    previousY + VineEncounterDefinition.RunnerGripHeight,
                    newX,
                    newY + VineEncounterDefinition.RunnerGripHeight,
                    vineStartX,
                    vineStartY,
                    vineEndX,
                    vineEndY,
                    vine.ContactRadius,
                    ref vineFraction))
                    continue;

                F32 gripLength = F32.Lerp(vine.MinimumGripLength, vine.Length, vineFraction);

                _vineConsumed[ndx]     = true;
                _vineWasCaught         = true;
                _activeVine            = vine;
                _vineGripLength        = gripLength;
                _vineAngle             = vine.RestAngle;
                _vineAngularVelocity   = (VelocityX * F32.Cos(_vineAngle) + VelocityY * F32.Sin(_vineAngle)) / _vineGripLength;
                _vineRightApexCount    = 0;
                _vineForcedReleaseArmed = false;
                _vineBottomCrossed     = false;
                _vineReleaseReason     = VineReleaseKind.Attached;
                _vineVisualState       = VineVisualKind.Attached;
                _releasedVine          = null;

                Mode                   = TimeTrialMode.Traversing;
                ActiveEncounterIndex   = ndx;
                _encounterPhase         = EncounterPhaseKind.TapToRelease;
                EncounterProgress      = F32.Zero;
                X                      = vine.AnchorX + _vineGripLength * F32.Sin(_vineAngle);
                Y                      = vine.AnchorY - _vineGripLength * F32.Cos(_vineAngle) - VineEncounterDefinition.RunnerGripHeight;
                _vineGripX             = X;
                _vineGripY             = Y + VineEncounterDefinition.RunnerGripHeight;
                _vineSwingTravel       = F32.Zero;
                return true;
            }

            return false;
        }

        void ReleaseVine(VineReleaseKind reason)
        {
            VineEncounterDefinition vine = _activeVine ?? throw new InvalidOperationException("No vine is active");
            Mode = TimeTrialMode.Airborne;
            ActiveEncounterIndex = -1;
            _encounterPhase       = EncounterPhaseKind.None;
            EncounterProgress    = F32.Zero;
            _vineReleaseReason   = reason;
            _vineVisualState     = VineVisualKind.Settling;
            _releasedVine        = vine;
            _activeVine          = null;
        }

        static bool SweptSegmentsAreWithinRadius(
            F32 startX,
            F32 startY,
            F32 endX,
            F32 endY,
            F32 vineStartX,
            F32 vineStartY,
            F32 vineEndX,
            F32 vineEndY,
            F32 radius,
            ref F32 vineFraction)
        {
            F32 bestDistanceSquared = F32.MaxValue;
            F32 bestVineFraction = F32.Zero;

            F32 movementX = endX - startX;
            F32 movementY = endY - startY;
            F32 vineX = vineEndX - vineStartX;
            F32 vineY = vineEndY - vineStartY;
            F32 denominator = movementX * vineY - movementY * vineX;
            if (denominator != F32.Zero)
            {
                F32 offsetX = vineStartX - startX;
                F32 offsetY = vineStartY - startY;
                F32 movementFraction = (offsetX * vineY - offsetY * vineX) / denominator;
                F32 crossingVineFraction = (offsetX * movementY - offsetY * movementX) / denominator;
                if (movementFraction >= F32.Zero && movementFraction <= F32.One
                    && crossingVineFraction >= F32.Zero && crossingVineFraction <= F32.One)
                {
                    bestDistanceSquared = F32.Zero;
                    bestVineFraction = crossingVineFraction;
                }
            }

            ConsiderPointAgainstSegment(startX, startY, vineStartX, vineStartY, vineEndX, vineEndY, ref bestDistanceSquared, ref bestVineFraction);
            ConsiderPointAgainstSegment(endX, endY, vineStartX, vineStartY, vineEndX, vineEndY, ref bestDistanceSquared, ref bestVineFraction);

            F32 movementLengthSquared = movementX * movementX + movementY * movementY;
            if (movementLengthSquared > F32.Zero)
            {
                ConsiderVinePointAgainstMovement(vineStartX, vineStartY, F32.Zero, startX, startY, movementX, movementY, movementLengthSquared, ref bestDistanceSquared, ref bestVineFraction);
                ConsiderVinePointAgainstMovement(vineEndX, vineEndY, F32.One, startX, startY, movementX, movementY, movementLengthSquared, ref bestDistanceSquared, ref bestVineFraction);
            }

            if (bestDistanceSquared > radius * radius)
                return false;

            vineFraction = bestVineFraction;
            return true;
        }

        static void ConsiderPointAgainstSegment(
            F32 pointX,
            F32 pointY,
            F32 segmentStartX,
            F32 segmentStartY,
            F32 segmentEndX,
            F32 segmentEndY,
            ref F32 bestDistanceSquared,
            ref F32 bestSegmentFraction)
        {
            F32 segmentX = segmentEndX - segmentStartX;
            F32 segmentY = segmentEndY - segmentStartY;
            F32 segmentLengthSquared = segmentX * segmentX + segmentY * segmentY;
            F32 fraction = segmentLengthSquared > F32.Zero
                ? F32.Clamp01(((pointX - segmentStartX) * segmentX + (pointY - segmentStartY) * segmentY) / segmentLengthSquared)
                : F32.Zero;
            F32 closestX = segmentStartX + segmentX * fraction;
            F32 closestY = segmentStartY + segmentY * fraction;
            F32 distanceX = pointX - closestX;
            F32 distanceY = pointY - closestY;
            F32 distanceSquared = distanceX * distanceX + distanceY * distanceY;
            if (distanceSquared < bestDistanceSquared)
            {
                bestDistanceSquared = distanceSquared;
                bestSegmentFraction = fraction;
            }
        }

        static void ConsiderVinePointAgainstMovement(
            F32 vinePointX,
            F32 vinePointY,
            F32 vineFraction,
            F32 movementStartX,
            F32 movementStartY,
            F32 movementX,
            F32 movementY,
            F32 movementLengthSquared,
            ref F32 bestDistanceSquared,
            ref F32 bestVineFraction)
        {
            F32 movementFraction = F32.Clamp01(
                ((vinePointX - movementStartX) * movementX + (vinePointY - movementStartY) * movementY)
                / movementLengthSquared);
            F32 closestX = movementStartX + movementX * movementFraction;
            F32 closestY = movementStartY + movementY * movementFraction;
            F32 distanceX = vinePointX - closestX;
            F32 distanceY = vinePointY - closestY;
            F32 distanceSquared = distanceX * distanceX + distanceY * distanceY;
            if (distanceSquared < bestDistanceSquared)
            {
                bestDistanceSquared = distanceSquared;
                bestVineFraction = vineFraction;
            }
        }
    }
}
