using Metaplay.Core.Math;

namespace Game.Logic
{
    /// <summary>
    /// The dispatch from <see cref="TimeTrialSimulation"/>'s step logic to every traversal mechanic a course may
    /// place, and the renderer-facing names of the encounter phases.
    /// <para>
    /// A mechanic's own file decides from the course geometry whether it has anything to do, so the order here
    /// only settles which one claims a step when two could: a method that can be claimed returns whether it
    /// was, and a mechanic that has already claimed the step is not offered it again. Nothing keeps mechanics
    /// apart in X, a vine's platform can stand over the far walls of the pits beneath it, which are ledges,
    /// so the order is the priority: a climb first, then a vine, then a ledge.
    /// </para>
    /// </summary>
    public sealed partial class TimeTrialSimulation
    {
        /// <summary>
        /// The phase of the active encounter. The simulation branches on these; only <see cref="EncounterPhase"/>
        /// turns them into the names the renderer reads.
        /// </summary>
        enum EncounterPhaseKind
        {
            None,
            Climbing,
            RunOut,
            TapToRelease,
            LedgeHang,
            LedgeRise,
            LedgeTopTransition,
            LedgeWallRise,
            LedgeImpact,
        }

        static string NameOf(EncounterPhaseKind phase)
        {
            switch (phase)
            {
                case EncounterPhaseKind.Climbing:           return "climbing";
                case EncounterPhaseKind.RunOut:             return "run-out";
                case EncounterPhaseKind.TapToRelease:       return "tap-to-release";
                case EncounterPhaseKind.LedgeHang:          return "ledge-hang";
                case EncounterPhaseKind.LedgeRise:          return "ledge-rise";
                case EncounterPhaseKind.LedgeTopTransition: return "ledge-top-transition";
                case EncounterPhaseKind.LedgeWallRise:      return "ledge-wall-rise";
                case EncounterPhaseKind.LedgeImpact:        return "ledge-impact";
                default:                                    return "";
            }
        }

        void InitializeEncounters()
        {
            VineInitializeEncounters();
            LedgeGrabInitializeEncounters();
            ClimbInitializeEncounters();
        }

        void StepPassiveEncounter()
        {
            VineStepPassiveEncounter();
        }

        /// <summary>Whether an encounter took this input, so the ordinary running and jumping rules do not.</summary>
        bool ApplyEncounterInput(TimeTrialInput input)
            => ClimbApplyEncounterInput()
            || VineApplyEncounterInput(input)
            || LedgeGrabApplyEncounterInput();

        /// <summary>Whether an encounter ran this step.</summary>
        bool StepEncounter()
            => ClimbStepEncounter()
            || VineStepEncounter()
            || LedgeGrabStepEncounter();

        void PrepareAirborneEncounterMotion()
        {
            LedgeGrabPrepareAirborneEncounterMotion();
        }

        /// <summary>Whether an encounter resolved this airborne step's vertical motion on its own.</summary>
        bool TryResolveAirborneEncounterVerticalMotion(F32 previousY, F32 newY)
            => LedgeGrabTryResolveAirborneEncounterVerticalMotion(previousY, newY);

        void OnAirborneLanded()
        {
            LedgeGrabOnAirborneLanded();
        }

        /// <summary>Whether an encounter took the runner over on the move from the previous position to the new one.</summary>
        bool TryEnterEncounter(F32 previousX, F32 previousY, F32 newX, F32 newY)
            => ClimbTryEnterEncounter(previousX, newX)
            || VineTryEnterEncounter(previousX, previousY, newX, newY)
            || LedgeGrabTryEnterEncounter(previousX, previousY, newX, newY);

        /// <summary>Whether the fall lands on a surface an encounter unlocked, and that surface's top.</summary>
        bool TryFindEncounterLanding(F32 x, F32 previousY, F32 newY, out F32 topY)
            => VineTryFindPlatformLanding(x, previousY, newY, out topY);

        /// <summary>Whether a surface an encounter unlocked supports feet at exactly this position.</summary>
        bool HasEncounterSurfaceAt(F32 x, F32 topY)
            => VineHasPlatformSurfaceAt(x, topY);

        void OnStepCompleted()
        {
            ClimbOnStepCompleted();
        }
    }
}
