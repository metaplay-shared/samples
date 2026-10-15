using Metaplay.Core.Config;
using Metaplay.Core.Math;
using Metaplay.Core.Model;

namespace Game.Logic
{
    /// <summary>
    /// Designer-tunable movement values for the time trial simulation. Built from
    /// <c>GameConfigSource/TimeTrialRules.csv</c> (written as <c>Member,Value</c> rows). Units are metres and
    /// seconds; the simulation converts per-second values to per-step values using
    /// <see cref="TimeTrialConstants.StepsPerSecond"/>.
    /// <para>
    /// All values are fixed-point (<see cref="F32"/>) or integers so the simulation is deterministic across the
    /// browser and the server. The C# defaults are only what a test gets when it constructs the rules directly;
    /// the sheet is the source of truth for the shipped values.
    /// </para>
    /// <para>
    /// The complete set of values is fixed when a run begins and belongs to the recording's ruleset. Publishing
    /// a new sheet while runs are open changes what the server recomputes them against; that gap is accepted.
    /// </para>
    /// </summary>
    [MetaSerializable]
    public class TimeTrialRules : GameConfigKeyValue<TimeTrialRules>
    {
        /// <summary>Horizontal speed on the ground at full pace, m/s.</summary>
        [MetaMember(1)] public F32 RunSpeed = F32.FromInt(8);

        /// <summary>Downward acceleration while airborne, m/s².</summary>
        [MetaMember(2)] public F32 Gravity = F32.FromInt(30);

        /// <summary>Launch direction, degrees above the horizontal. Fixed for the whole run.</summary>
        [MetaMember(3)] public F32 LaunchAngleDegrees = F32.FromInt(50);

        /// <summary>Launch speed along the launch direction at zero charge (a tap), m/s.</summary>
        [MetaMember(4)] public F32 MinLaunchSpeed = F32.FromInt(6);

        /// <summary>Launch speed along the launch direction at full charge, m/s.</summary>
        [MetaMember(5)] public F32 MaxLaunchSpeed = F32.FromInt(16);

        /// <summary>Steps of holding the button that reach full charge; holding longer forces the launch.</summary>
        [MetaMember(6)] public int MaxChargeSteps = 45;

        /// <summary>Horizontal acceleration back towards <see cref="RunSpeed"/> after a stop or landing, m/s².</summary>
        [MetaMember(7)] public F32 RunAcceleration = F32.FromInt(12);

        /// <summary>Steps the runner is stopped after touching a hazard.</summary>
        [MetaMember(8)] public int StumbleSteps = 30;

        /// <summary>
        /// Steps before touching down that a press in the air still counts: a press made that close to the
        /// landing and held through it starts the charge on the landing step. Zero ignores every press in the air.
        /// </summary>
        [MetaMember(25)] public int JumpBufferSteps = 9;

        // Member ids 9, 10 and 19 are retired and may not be reused.

        /// <summary>Steps spent visibly hanging after an automatic ledge catch.</summary>
        [MetaMember(18)] public int LedgeGrabHangSteps = 10;

        /// <summary>How fast the runner's feet rise up the face from the catch to the lip, m/s.</summary>
        [MetaMember(23)] public F32 LedgeGrabRiseSpeed = F32.FromInt(4);

        /// <summary>Steps spent moving horizontally from above the face to the upper surface landing point.</summary>
        [MetaMember(21)] public int LedgeGrabTopTransitionSteps = 10;

        /// <summary>How far the runner's feet rise above the lip before horizontal movement begins, metres.</summary>
        [MetaMember(22)] public F32 LedgeGrabClearanceHeight = F32.Ratio10(3);

        /// <summary>The running speed a completed mantle sets the runner down at, m/s.</summary>
        [MetaMember(24)] public F32 LedgeGrabResumeSpeed = F32.FromInt(4);

        /// <summary>
        /// A separate instance holding the same values. This is what a Skill's modifiers are applied to, so the
        /// published sheet the whole game reads is never written to. Every member is listed here: one that is
        /// left out would silently fall back to its C# default for every run played with a Skill equipped.
        /// </summary>
        public TimeTrialRules Copy() => new TimeTrialRules
        {
            RunSpeed                    = RunSpeed,
            Gravity                     = Gravity,
            LaunchAngleDegrees          = LaunchAngleDegrees,
            MinLaunchSpeed              = MinLaunchSpeed,
            MaxLaunchSpeed              = MaxLaunchSpeed,
            MaxChargeSteps              = MaxChargeSteps,
            RunAcceleration             = RunAcceleration,
            StumbleSteps                = StumbleSteps,
            JumpBufferSteps             = JumpBufferSteps,
            LedgeGrabHangSteps          = LedgeGrabHangSteps,
            LedgeGrabRiseSpeed          = LedgeGrabRiseSpeed,
            LedgeGrabTopTransitionSteps = LedgeGrabTopTransitionSteps,
            LedgeGrabClearanceHeight    = LedgeGrabClearanceHeight,
            LedgeGrabResumeSpeed        = LedgeGrabResumeSpeed,
        };
    }
}
