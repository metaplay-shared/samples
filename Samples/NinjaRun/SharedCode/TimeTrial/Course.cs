using Metaplay.Core;
using Metaplay.Core.Math;
using Metaplay.Core.Model;
using System;
using System.Collections.Generic;

namespace Game.Logic
{
    /// <summary>
    /// Deterministic contact shape around the runner's feet reference point. The browser projects these same
    /// dimensions for sprite placement and diagnostics, so collision and presentation agree on the body's
    /// forward edge and full height.
    /// </summary>
    public static class TimeTrialPlayerBody
    {
        public static readonly F32 LeftExtent = F32.Ratio10(8);
        public static readonly F32 RightExtent = F32.Ratio(17, 20);
        public static readonly F32 Height = F32.Ratio10(17);
    }

    /// <summary>
    /// Identity of a course. Stored in player state (tickets, best results), so ids are permanent.
    /// </summary>
    [MetaSerializable]
    public class CourseId : StringId<CourseId> { }

    /// <summary>
    /// How a <see cref="CourseId"/> is read out of a game config sheet. The SDK's own reading of a
    /// <c>StringId</c> stops at the first character that is not a letter, digit, underscore, hyphen or dot, and
    /// a map id is part ids joined by <see cref="CourseComposer.PartSeparator"/>, so without this, a cell
    /// naming a map is read as its first part and the rest is an error.
    /// </summary>
    public class CourseConfigParsers : ConfigParserProvider
    {
        static readonly ConfigLexer.CustomTokenSpec CourseIdToken =
            new ConfigLexer.CustomTokenSpec($@"[a-zA-Z_][a-zA-Z0-9_\-.\{CourseComposer.PartSeparator}]*", name: nameof(CourseId));

        public override void RegisterParsers(ConfigParser parser)
        {
            parser.RegisterCustomParseFunc<CourseId>(lexer => CourseId.FromString(lexer.ParseCustomToken(CourseIdToken)));
        }
    }

    /// <summary>
    /// A hash of a course id that is the same on every platform and every run, unlike
    /// <see cref="string.GetHashCode"/>, for values that must stay stable for the lifetime of a course.
    /// FNV-1a over the id's UTF-16 code units.
    /// </summary>
    public static class CourseIdHash
    {
        const ulong FnvOffsetBasis = 14695981039346656037UL;
        const ulong FnvPrime       = 1099511628211UL;

        public static ulong Of(CourseId id) => Of(id?.Value ?? "");

        public static ulong Of(string value)
        {
            ulong hash = FnvOffsetBasis;
            foreach (char c in value)
            {
                hash = (hash ^ (byte)(c & 0xFF)) * FnvPrime;
                hash = (hash ^ (byte)(c >> 8)) * FnvPrime;
            }
            return hash;
        }
    }

    /// <summary>
    /// A stretch of ground the runner can stand on. Coordinates are metres. X runs along the course, Y is up,
    /// and the runner's feet rest on <see cref="TopY"/>.
    /// <para>
    /// Serializable because a course part carries its ground in the published game config
    /// (<see cref="CoursePartInfo"/>).
    /// </para>
    /// </summary>
    [MetaSerializable]
    public sealed class GroundSegment
    {
        [MetaMember(1)] public F32 StartX { get; private set; }
        [MetaMember(2)] public F32 EndX   { get; private set; }
        [MetaMember(3)] public F32 TopY   { get; private set; }

        /// <summary>
        /// Whether this segment is the floor of a pit at its own authored height, between two higher segments
        /// it touches.
        /// </summary>
        [MetaMember(4)] public bool IsPit { get; private set; }

        GroundSegment() { }

        public GroundSegment(F32 startX, F32 endX, F32 topY, bool isPit = false)
        {
            StartX = startX;
            EndX   = endX;
            TopY   = topY;
            IsPit  = isPit;
        }
    }

    /// <summary>
    /// A span of spikes standing on the ground. A runner who enters the span on foot, or lands in it, stumbles
    /// and loses momentum; it never ends the run. A span trips the runner at most once per run.
    /// <para>
    /// Serializable for the same reason <see cref="GroundSegment"/> is: a course part carries its hazards in
    /// the published game config.
    /// </para>
    /// </summary>
    [MetaSerializable]
    public sealed class HazardSpan
    {
        [MetaMember(1)] public F32 StartX { get; private set; }
        [MetaMember(2)] public F32 EndX   { get; private set; }

        HazardSpan() { }

        public HazardSpan(F32 startX, F32 endX)
        {
            StartX = startX;
            EndX   = endX;
        }
    }

    /// <summary>
    /// Structural course data for a contextual traversal mechanic. Concrete encounter types own their
    /// placement data; the shared base only gives every authored feature a stable identity and renderer key.
    /// Encounter definitions are immutable course data, not durable player state.
    /// </summary>
    public abstract class CourseEncounterDefinition
    {
        public string Id { get; }
        public string Kind { get; }

        protected CourseEncounterDefinition(string id, string kind)
        {
            Id   = id ?? throw new ArgumentNullException(nameof(id));
            Kind = kind ?? throw new ArgumentNullException(nameof(kind));
        }
    }

    /// <summary>
    /// One finite vertical face with an automatically catchable top lip. The face is solid between its bottom
    /// and top. Contact inside the authored band below the lip starts a deterministic pull-up onto
    /// <see cref="LandingX"/>; lower contact falls naturally until the runner can continue below the face.
    /// <para>
    /// A course never authors one. <see cref="CourseDefinition"/> derives them from its ground, so the
    /// constructor is internal.
    /// </para>
    /// </summary>
    public sealed class LedgeGrabDefinition : CourseEncounterDefinition
    {
        public const string EncounterKind = "ledge-grab";

        public F32 X { get; }
        public F32 BottomY { get; }
        public F32 TopY { get; }
        /// <summary>The lowest feet/contact-origin Y which may begin an automatic grab.</summary>
        public F32 MinimumGrabY { get; }
        public F32 LandingX { get; }

        internal LedgeGrabDefinition(string id, F32 x, F32 bottomY, F32 topY, F32 minimumGrabY, F32 landingX)
            : base(id, EncounterKind)
        {
            if (bottomY >= topY)
                throw new ArgumentException("A ledge face needs positive height", nameof(topY));
            if (minimumGrabY < bottomY || minimumGrabY >= topY)
                throw new ArgumentException("A ledge minimum grab height must lie on its face", nameof(minimumGrabY));
            if (landingX <= x)
                throw new ArgumentException("A ledge landing must be beyond its face", nameof(landingX));

            X          = x;
            BottomY    = bottomY;
            TopY       = topY;
            MinimumGrabY = minimumGrabY;
            LandingX   = landingX;
        }
    }

    /// <summary>
    /// A pit with a floor, derived by <see cref="CourseDefinition"/> from its ground: the shaft a gap in the
    /// ground opens, or the shaft over a segment flagged <see cref="GroundSegment.IsPit"/>.
    /// <para>
    /// The floor is an ordinary <see cref="GroundSegment"/> in <see cref="CourseDefinition.Ground"/> and the far
    /// wall an ordinary <see cref="LedgeGrabDefinition"/> in <see cref="CourseDefinition.Encounters"/>.
    /// </para>
    /// </summary>
    public sealed class PitDefinition
    {
        /// <summary>The near lip: where the ground before the pit ends and the runner leaves the floor.</summary>
        public F32 StartX { get; }

        /// <summary>The far lip: where the ground after the pit begins, and where its climbable wall stands.</summary>
        public F32 EndX { get; }

        /// <summary>The height of the floor the runner lands on and runs along.</summary>
        public F32 FloorY { get; }

        /// <summary>How far the floor lies below the lower of the two lips, metres.</summary>
        public F32 Depth { get; }

        public PitDefinition(F32 startX, F32 endX, F32 floorY, F32 depth)
        {
            if (startX >= endX)
                throw new ArgumentException("A pit needs positive width", nameof(endX));

            StartX = startX;
            EndX   = endX;
            FloorY = floorY;
            Depth  = depth;
        }
    }

    /// <summary>
    /// A slope the runner is carried up at <see cref="TimeTrialRules.RunSpeed"/>, bracketed by two torii: the
    /// bottom one at <see cref="BottomX"/> takes over input, and the top one at <see cref="GateX"/>, a segment
    /// gate, hands it back. Every runner crosses that gate on the ground at running pace, so each carries the
    /// same state into the next segment.
    /// </summary>
    public sealed class ClimbDefinition : CourseEncounterDefinition
    {
        public const string EncounterKind = "climb";

        /// <summary>
        /// Metres of flat ground between the bottom torii and the foot of the slope. The torii stands on the
        /// flat, so the runner passes under it on level ground and meets the slope a moment later.
        /// </summary>
        public static readonly F32 FootFlatLength = F32.FromInt(3);

        /// <summary>The bottom torii: where the climb takes over, on the flat ground before the slope.</summary>
        public F32 BottomX { get; }

        /// <summary>The foot of the slope, <see cref="FootFlatLength"/> past the bottom torii, where the ground below ends.</summary>
        public F32 SlopeStartX { get; }

        /// <summary>The top of the slope, where the ground above begins and ordinary running resumes.</summary>
        public F32 TopX { get; }

        /// <summary>Height of the ground the slope arrives at.</summary>
        public F32 TopY { get; }

        /// <summary>The top torii: the segment gate, at the far end of the flat run-out.</summary>
        public F32 GateX { get; }

        public ClimbDefinition(string id, F32 bottomX, F32 topX, F32 topY, F32 gateX)
            : base(id, EncounterKind)
        {
            F32 slopeStartX = bottomX + FootFlatLength;
            if (topX <= slopeStartX)
                throw new ArgumentException("A climb slope needs positive width past the flat at its foot", nameof(topX));
            if (gateX <= topX)
                throw new ArgumentException("A climb needs a run-out between its top and its gate", nameof(gateX));

            BottomX     = bottomX;
            SlopeStartX = slopeStartX;
            TopX        = topX;
            TopY        = topY;
            GateX       = gateX;
        }

        /// <summary>Whether the climb owns movement and input at <paramref name="x"/>, from its bottom torii to its gate.</summary>
        public bool Owns(F32 x) => x >= BottomX && x < GateX;

        /// <summary>
        /// The height of the ground the climb carries the runner over at <paramref name="x"/>, given the height
        /// of the flat at its foot: that height up to <see cref="SlopeStartX"/>, the slope's line from there to
        /// <see cref="TopX"/>, and <see cref="TopY"/> beyond.
        /// </summary>
        public F32 SurfaceYAt(F32 x, F32 bottomY)
        {
            if (x <= SlopeStartX)
                return bottomY;
            if (x >= TopX)
                return TopY;
            return F32.Lerp(bottomY, TopY, F32.Div(x - SlopeStartX, TopX - SlopeStartX));
        }

        /// <summary>How far up the slope <paramref name="x"/> is, from 0 on the flat at its foot to 1 at its top.</summary>
        public F32 SlopeProgressAt(F32 x) => F32.Clamp01(F32.Div(x - SlopeStartX, TopX - SlopeStartX));
    }

    /// <summary>
    /// A flexible log bridge drawn over ordinary uninterrupted ground. Its curve and deformation are
    /// presentation only: collision uses the ground segment beneath the complete span. A course keeps its
    /// bridges apart from its encounters, so a bridge cannot own input or change the simulation.
    /// </summary>
    public sealed class DroopingBridgePresentationFeature
    {
        public string Id { get; }
        public F32 StartX { get; }
        public F32 EndX { get; }
        public F32 RestSag { get; }
        public F32 MaxLocalDroop { get; }
        public F32 ResponseRadius { get; }
        public int LogCount { get; }
        public bool HasWaterfall { get; }

        public DroopingBridgePresentationFeature(
            string id,
            F32 startX,
            F32 endX,
            F32 restSag,
            F32 maxLocalDroop,
            F32 responseRadius,
            int logCount,
            bool hasWaterfall)
        {
            Id            = id ?? throw new ArgumentNullException(nameof(id));
            StartX        = startX;
            EndX          = endX;
            RestSag       = restSag;
            MaxLocalDroop = maxLocalDroop;
            ResponseRadius = responseRadius;
            LogCount      = logCount;
            HasWaterfall  = hasWaterfall;
        }

        /// <summary>
        /// The bridge as a course carries it: the tuned sag, droop, response and log count, and the gorge
        /// waterfall under the deck. A part chooses only where it hangs.
        /// </summary>
        public static DroopingBridgePresentationFeature OverGorge(string id, F32 startX, F32 endX)
            => new DroopingBridgePresentationFeature(
                id:             id,
                startX:         startX,
                endX:           endX,
                restSag:        F32.FromInt(1),
                maxLocalDroop:  F32.Ratio10(8),
                responseRadius: F32.Ratio10(35),
                logCount:       26,
                hasWaterfall:   true);
    }

    /// <summary>
    /// An immutable course: the geometry the simulation runs on, the same for play and for the server's own run
    /// of the recording. It derives its own pits and catchable faces from its ground.
    /// <para>
    /// <see cref="CourseResolver"/> is the one place a <see cref="CourseId"/> becomes one of these.
    /// </para>
    /// </summary>
    public sealed class CourseDefinition
    {
        public CourseId Id { get; }
        public string DisplayName { get; }

        /// <summary>The runner starts here, standing on the ground segment under it.</summary>
        public F32 StartX { get; }

        /// <summary>Crossing this X finishes the run.</summary>
        public F32 FinishX { get; }

        /// <summary>
        /// Sorted by <see cref="GroundSegment.StartX"/>, non-overlapping. The course must have ground under
        /// <see cref="StartX"/> and <see cref="FinishX"/>. This is the authored ground plus a floor segment for
        /// every pit cut into a gap, so the only gaps left in it are the ones a climb's slope fills.
        /// </summary>
        public IReadOnlyList<GroundSegment> Ground { get; }

        /// <summary>
        /// The pits, in the order the runner meets them: every gap in the authored ground that no climb owns,
        /// and every authored segment flagged <see cref="GroundSegment.IsPit"/>. Each one's floor is in
        /// <see cref="Ground"/> and each one's far wall is in <see cref="Encounters"/>.
        /// </summary>
        public IReadOnlyList<PitDefinition> Pits { get; }

        /// <summary>Sorted by <see cref="HazardSpan.StartX"/>, non-overlapping, each lying within one ground segment.</summary>
        public IReadOnlyList<HazardSpan> Hazards { get; }

        /// <summary>The climbs in <see cref="Encounters"/>, in the order the runner meets them.</summary>
        public IReadOnlyList<ClimbDefinition> Climbs { get; }

        /// <summary>
        /// The segment gates, in order: the top torii of each climb. They split the course into segments raced
        /// and placed one by one, and the finish line closes the last one. Empty on a course raced as a whole.
        /// </summary>
        public IReadOnlyList<F32> Gates { get; }

        /// <summary>
        /// Contextual mechanics which may temporarily own movement and the one-button input: the climbs and
        /// vines the course was given, then the catchable faces it derived from its ground.
        /// </summary>
        public IReadOnlyList<CourseEncounterDefinition> Encounters { get; }

        /// <summary>The drooping bridges the course's parts hang: presentation with no effect on deterministic movement.</summary>
        public IReadOnlyList<DroopingBridgePresentationFeature> DroopingBridges { get; }

        /// <summary>
        /// Cosmetic props standing on the ground, in the order their parts placed them. Only the renderer reads
        /// them; see <see cref="CourseDecoration"/>.
        /// </summary>
        public IReadOnlyList<CourseDecoration> Decorations { get; }

        /// <summary>
        /// Metres of gap width per metre of pit depth, before the clamps. A pit is exactly as deep as its gap
        /// is wide over this, which holds the cost of falling in at a near-constant margin behind the cost of
        /// jumping over, whatever the width.
        /// </summary>
        public static readonly F32 PitWidthPerDepth = F32.Ratio10(25);

        /// <summary>The shallowest a pit is ever cut, metres: still deep enough that the mantle out of it costs.</summary>
        public static readonly F32 MinimumPitDepth = F32.Ratio10(16);

        /// <summary>The deepest a pit is ever cut, metres: above the apex of a full charge, so it cannot be jumped out of.</summary>
        public static readonly F32 MaximumPitDepth = F32.Ratio10(26);

        /// <summary>How deep the pit cut into a gap <paramref name="width"/> metres wide is, metres.</summary>
        public static F32 PitDepthForGap(F32 width) => F32.Clamp(F32.Div(width, PitWidthPerDepth), MinimumPitDepth, MaximumPitDepth);

        /// <summary>How far past a derived face the mantle sets the runner down, metres.</summary>
        /// <remarks>
        /// Tuned by eye. Any positive reach puts the runner's feet (the point the ground is read under) on the
        /// top ground and the whole body above the face's top; short of the body's left extent, the trailing edge
        /// of its box is still over the lip it was pulled over.
        /// </remarks>
        public static readonly F32 DerivedLedgeLandingReach = F32.Ratio10(3);

        /// <summary>What the derived far wall of a pit is called, followed by the pit's number in course order.</summary>
        public const string PitWallIdPrefix = "pit-wall-";

        /// <summary>What the derived face of a step up is called, followed by the step's number in course order.</summary>
        public const string StepLedgeIdPrefix = "step-";

        public CourseDefinition(
            CourseId id,
            string displayName,
            F32 startX,
            F32 finishX,
            IReadOnlyList<GroundSegment> ground,
            IReadOnlyList<HazardSpan> hazards,
            IReadOnlyList<CourseEncounterDefinition> encounters = null,
            IReadOnlyList<DroopingBridgePresentationFeature> droopingBridges = null,
            IReadOnlyList<CourseDecoration> decorations = null)
        {
            Id             = id ?? throw new ArgumentNullException(nameof(id));
            DisplayName    = displayName ?? throw new ArgumentNullException(nameof(displayName));
            StartX         = startX;
            FinishX        = finishX;
            Ground         = ground ?? throw new ArgumentNullException(nameof(ground));
            Hazards        = hazards ?? throw new ArgumentNullException(nameof(hazards));
            Encounters     = encounters ?? Array.Empty<CourseEncounterDefinition>();
            DroopingBridges = droopingBridges ?? Array.Empty<DroopingBridgePresentationFeature>();
            Decorations    = decorations ?? Array.Empty<CourseDecoration>();

            List<ClimbDefinition> climbs = new List<ClimbDefinition>();
            List<F32>             gates  = new List<F32>();
            foreach (CourseEncounterDefinition encounter in Encounters)
            {
                if (encounter is ClimbDefinition climb)
                {
                    climbs.Add(climb);
                    gates.Add(climb.GateX);
                }
            }

            Climbs = climbs;
            Gates  = gates;

            ValidateClimbs();

            List<GroundSegment>            cutGround     = new List<GroundSegment>();
            List<CourseEncounterDefinition> cutEncounters = new List<CourseEncounterDefinition>(Encounters);

            Pits       = CutPits(Ground, cutGround, cutEncounters);
            Ground     = cutGround;
            Encounters = cutEncounters;
        }

        /// <summary>
        /// Adds a pit floor to <paramref name="ground"/> and a catchable face to <paramref name="encounters"/>
        /// for every pit and step up in the authored ground, and returns the pits in course order.
        /// </summary>
        IReadOnlyList<PitDefinition> CutPits(
            IReadOnlyList<GroundSegment>    authoredGround,
            List<GroundSegment>             ground,
            List<CourseEncounterDefinition> encounters)
        {
            List<PitDefinition> pits  = new List<PitDefinition>();
            int                 steps = 0;

            for (int ndx = 0; ndx < authoredGround.Count; ndx++)
            {
                GroundSegment segment = authoredGround[ndx];
                ground.Add(segment);

                if (segment.IsPit)
                {
                    // A flagged floor lies between two lips it touches, both higher than it: the part's own rules
                    // (CoursePartInfo.Validate) hold every pit floor to that, and composing moves a floor and its
                    // lips together.
                    GroundSegment before = authoredGround[ndx - 1];
                    GroundSegment after  = authoredGround[ndx + 1];

                    F32 lipY = F32.Min(before.TopY, after.TopY);
                    pits.Add(new PitDefinition(segment.StartX, segment.EndX, segment.TopY, lipY - segment.TopY));

                    // The far wall of the pit is the face out of it, so it is never also a step. Every derived
                    // face is catchable down to its bottom, so a runner who reaches the floor and one who meets
                    // the wall on the way down both climb out through the same ledge grab.
                    encounters.Add(new LedgeGrabDefinition(
                        id:           $"{PitWallIdPrefix}{pits.Count}",
                        x:            segment.EndX,
                        bottomY:      segment.TopY,
                        topY:         after.TopY,
                        minimumGrabY: segment.TopY,
                        landingX:     segment.EndX + DerivedLedgeLandingReach));
                    continue;
                }

                if (ndx + 1 >= authoredGround.Count)
                    continue;

                GroundSegment next = authoredGround[ndx + 1];

                if (next.StartX > segment.EndX)
                {
                    // The gap a climb's slope fills is the course carrying the runner, not a pit.
                    if (FindClimbOwning(segment.EndX) != null)
                        continue;

                    F32 depth  = PitDepthForGap(next.StartX - segment.EndX);
                    F32 floorY = F32.Min(segment.TopY, next.TopY) - depth;

                    pits.Add(new PitDefinition(segment.EndX, next.StartX, floorY, depth));
                    ground.Add(new GroundSegment(segment.EndX, next.StartX, floorY));
                    encounters.Add(new LedgeGrabDefinition(
                        id:           $"{PitWallIdPrefix}{pits.Count}",
                        x:            next.StartX,
                        bottomY:      floorY,
                        topY:         next.TopY,
                        minimumGrabY: floorY,
                        landingX:     next.StartX + DerivedLedgeLandingReach));
                }
                else if (next.StartX == segment.EndX && next.TopY > segment.TopY)
                {
                    steps++;
                    encounters.Add(new LedgeGrabDefinition(
                        id:           $"{StepLedgeIdPrefix}{steps}",
                        x:            next.StartX,
                        bottomY:      segment.TopY,
                        topY:         next.TopY,
                        minimumGrabY: segment.TopY,
                        landingX:     next.StartX + DerivedLedgeLandingReach));
                }
            }

            return pits;
        }

        /// <summary>
        /// Checks every climb fits the ground it is authored into.
        /// </summary>
        void ValidateClimbs()
        {
            F32 previousEnd = StartX;
            foreach (ClimbDefinition climb in Climbs)
            {
                if (climb.BottomX <= previousEnd)
                    throw new ArgumentException($"Climb {climb.Id} must start after the start line and after the climb before it");
                if (climb.GateX >= FinishX)
                    throw new ArgumentException($"Climb {climb.Id} must reach its gate before the finish line");

                GroundSegment below = FindGroundSegmentEndingAt(climb.SlopeStartX);
                if (below == null)
                    throw new ArgumentException($"Climb {climb.Id} must start its slope where the ground below it ends, {ClimbDefinition.FootFlatLength} m past its bottom torii");
                if (below.StartX > climb.BottomX)
                    throw new ArgumentException($"Climb {climb.Id} must stand its bottom torii on the flat ground that runs on to the foot of its slope");
                if (below.TopY >= climb.TopY)
                    throw new ArgumentException($"Climb {climb.Id} must rise above the ground it starts from");

                int topIndex = FindGroundIndexAt(climb.TopX);
                if (topIndex < 0 || Ground[topIndex].StartX != climb.TopX || Ground[topIndex].TopY != climb.TopY)
                    throw new ArgumentException($"Climb {climb.Id} must arrive on ground that begins at its top, at its top height");

                int gateIndex = FindGroundIndexAt(climb.GateX);
                if (gateIndex < 0 || Ground[gateIndex].TopY != climb.TopY)
                    throw new ArgumentException($"Climb {climb.Id} must carry its run-out to the gate on ground at its top height");

                // The runner cannot answer for anything between the two torii.
                foreach (HazardSpan hazard in Hazards)
                {
                    if (hazard.StartX < climb.GateX && hazard.EndX > climb.BottomX)
                        throw new ArgumentException($"Climb {climb.Id} must be free of hazards from its foot to its gate");
                }

                foreach (GroundSegment segment in Ground)
                {
                    if (segment.IsPit && segment.StartX <= climb.GateX && segment.EndX >= climb.BottomX)
                        throw new ArgumentException($"Climb {climb.Id} must be free of pit floors from its foot to its gate, but the one from {segment.StartX} m to {segment.EndX} m touches it");
                }

                previousEnd = climb.GateX;
            }
        }

        /// <summary>
        /// The ground segment under <paramref name="x"/>, or -1 where no segment covers it. A segment covers
        /// <c>[StartX, EndX)</c>, so two touching segments hand the runner over without a gap or an overlap.
        /// </summary>
        public int FindGroundIndexAt(F32 x)
        {
            for (int ndx = 0; ndx < Ground.Count; ndx++)
            {
                GroundSegment segment = Ground[ndx];
                if (x >= segment.StartX && x < segment.EndX)
                    return ndx;
            }
            return -1;
        }

        /// <summary>The ground segment whose far end is exactly <paramref name="x"/>, or null if none ends there.</summary>
        public GroundSegment FindGroundSegmentEndingAt(F32 x)
        {
            foreach (GroundSegment segment in Ground)
            {
                if (segment.EndX == x)
                    return segment;
            }
            return null;
        }

        /// <summary>The climb that owns <paramref name="x"/>, from its bottom torii to its gate, or null outside every climb.</summary>
        public ClimbDefinition FindClimbOwning(F32 x)
        {
            foreach (ClimbDefinition climb in Climbs)
            {
                if (climb.Owns(x))
                    return climb;
            }
            return null;
        }

        /// <summary>Whether the ground supports feet at exactly this position.</summary>
        public bool HasSurfaceAt(F32 x, F32 topY)
        {
            int groundIndex = FindGroundIndexAt(x);
            return groundIndex >= 0 && Ground[groundIndex].TopY == topY;
        }
    }
}
