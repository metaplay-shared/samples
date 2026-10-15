using Metaplay.Core.Math;

namespace Game.Logic
{
    /// <summary>
    /// One hanging vine which catches an airborne runner near its authored grab point. The runner slides
    /// towards <see cref="Length"/> while swinging, then returns to ordinary airborne movement.
    /// </summary>
    public sealed class VineEncounterDefinition : CourseEncounterDefinition
    {
        public const string EncounterKind = "vine";

        public F32 AnchorX { get; }
        public F32 AnchorY { get; }
        public F32 PlatformStartX { get; }
        public F32 PlatformEndX { get; }
        public F32 PlatformTopY { get; }

        /// <summary>The shortest grip on the rope a catch can take; every vine's is <see cref="TunedMinimumGripLength"/>.</summary>
        public F32 MinimumGripLength => TunedMinimumGripLength;

        /// <summary>The rope's full length; every vine's is <see cref="TunedLength"/>.</summary>
        public F32 Length => TunedLength;

        /// <summary>The angle the rope hangs at before it is caught; every vine's is <see cref="TunedRestAngle"/>.</summary>
        public F32 RestAngle => TunedRestAngle;

        /// <summary>How close the grip must pass to catch the rope; every vine's is <see cref="TunedContactRadius"/>.</summary>
        public F32 ContactRadius => TunedContactRadius;

        /// <summary>How fast the grip slides down the rope; every vine's is <see cref="TunedSlideSpeed"/>.</summary>
        public F32 SlideSpeed => TunedSlideSpeed;

        /// <summary>How quickly the swing loses energy; every vine's is <see cref="TunedAngularDamping"/>.</summary>
        public F32 AngularDamping => TunedAngularDamping;

        /// <summary>The rope attaches to the runner above their feet; shared with browser projection.</summary>
        public static readonly F32 RunnerGripHeight = F32.Ratio10(12);

        /// <summary>
        /// The rope's shape and feel as the vine prototype settled them. Every vine is built with these, so only
        /// where it hangs and where its platform stands change from one vine to the next.
        /// </summary>
        public static readonly F32 TunedMinimumGripLength = F32.Ratio10(48);

        /// <summary>The rope's full length, from the anchor to its free end; see <see cref="TunedMinimumGripLength"/>.</summary>
        public static readonly F32 TunedLength = F32.Ratio10(63);

        /// <summary>The angle the rope hangs at before it is caught, radians; see <see cref="TunedMinimumGripLength"/>.</summary>
        public static readonly F32 TunedRestAngle = -F32.DegToRad(F32.FromInt(20));

        /// <summary>How close the runner's grip must pass the rope to catch it; see <see cref="TunedMinimumGripLength"/>.</summary>
        public static readonly F32 TunedContactRadius = F32.Ratio100(45);

        /// <summary>How fast the grip slides down the rope while swinging; see <see cref="TunedMinimumGripLength"/>.</summary>
        public static readonly F32 TunedSlideSpeed = F32.Ratio10(8);

        /// <summary>How quickly the swing loses energy; see <see cref="TunedMinimumGripLength"/>.</summary>
        public static readonly F32 TunedAngularDamping = F32.Ratio10(1);

        /// <summary>
        /// A vine with the tuned rope, hanging from (<paramref name="anchorX"/>, <paramref name="anchorY"/>), and
        /// the elevated platform a good swing lands on, all in course metres.
        /// </summary>
        public VineEncounterDefinition(string id, F32 anchorX, F32 anchorY, F32 platformStartX, F32 platformEndX, F32 platformTopY)
            : base(id, EncounterKind)
        {
            AnchorX        = anchorX;
            AnchorY        = anchorY;
            PlatformStartX = platformStartX;
            PlatformEndX   = platformEndX;
            PlatformTopY   = platformTopY;
        }
    }
}
