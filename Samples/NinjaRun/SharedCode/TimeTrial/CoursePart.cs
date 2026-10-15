using Metaplay.Core;
using Metaplay.Core.Config;
using Metaplay.Core.Math;
using Metaplay.Core.Model;
using System.Collections.Generic;

namespace Game.Logic
{
    /// <summary>
    /// Identity of a course part. A map id is its parts' ids joined together, and recordings, personal bests
    /// and ghost pools are all stored against that map id, so part ids are permanent.
    /// </summary>
    [MetaSerializable]
    public class CoursePartId : StringId<CoursePartId> { }

    /// <summary>
    /// Where in a map a part may sit. A map is three parts, so there are three positions, and a part names
    /// every one it may be composed into.
    /// </summary>
    [MetaSerializable]
    public enum CoursePartPosition
    {
        First  = 0,
        Middle = 1,
        Last   = 2,
    }

    /// <summary>
    /// A drooping log bridge a part hangs over its own ground, in part-local metres, with the gorge waterfall
    /// falling beneath its deck. It is scenery: the ground under it stays one flat stretch and collision reads
    /// that, so a bridge changes nothing a run does. The composer turns it into a
    /// <see cref="DroopingBridgePresentationFeature"/> with the tuned deck.
    /// </summary>
    [MetaSerializable]
    public sealed class CoursePartBridge
    {
        [MetaMember(1)] public F32 StartX { get; private set; }
        [MetaMember(2)] public F32 EndX   { get; private set; }

        CoursePartBridge() { }

        public CoursePartBridge(F32 startX, F32 endX)
        {
            StartX = startX;
            EndX   = endX;
        }
    }

    /// <summary>
    /// A hanging vine a part places, in part-local metres: where its rope hangs from and the elevated platform
    /// a good swing lands on. The rope's shape and feel are the <c>Tuned</c> values in
    /// <see cref="VineEncounterDefinition"/>, so every vine swings the same. The simulation lands a runner on
    /// the platform only after that runner has caught this vine.
    /// </summary>
    [MetaSerializable]
    public sealed class CoursePartVine
    {
        [MetaMember(1)] public F32 AnchorX        { get; private set; }
        [MetaMember(2)] public F32 AnchorY        { get; private set; }
        [MetaMember(3)] public F32 PlatformStartX { get; private set; }
        [MetaMember(4)] public F32 PlatformEndX   { get; private set; }
        [MetaMember(5)] public F32 PlatformTopY   { get; private set; }

        CoursePartVine() { }

        public CoursePartVine(F32 anchorX, F32 anchorY, F32 platformStartX, F32 platformEndX, F32 platformTopY)
        {
            AnchorX        = anchorX;
            AnchorY        = anchorY;
            PlatformStartX = platformStartX;
            PlatformEndX   = platformEndX;
            PlatformTopY   = platformTopY;
        }

        /// <summary>The nearest X a runner on the rope can swing back to: the anchor less the rope's full length.</summary>
        public F32 SwingStartX => AnchorX - VineEncounterDefinition.TunedLength;

        /// <summary>The farthest X a runner on the rope can swing out to: the anchor plus the rope's full length.</summary>
        public F32 SwingEndX => AnchorX + VineEncounterDefinition.TunedLength;

        /// <summary>The lowest a runner's feet go on the rope: at the bottom of the swing, holding its free end.</summary>
        public F32 LowestFeetY => AnchorY - VineEncounterDefinition.TunedLength - VineEncounterDefinition.RunnerGripHeight;

        /// <summary>Where the stretch the vine owns ends: its swing or its platform, whichever reaches farther.</summary>
        public F32 ReachEndX => F32.Max(SwingEndX, PlatformEndX);
    }

    /// <summary>
    /// One obstacle section: the stretch a player actually races, from a gate to the next bottom torii. Parts
    /// are the authoring unit (a chunk is easy to author, a whole course is not) and
    /// <see cref="CourseComposer"/> flattens three of them into the <see cref="CourseDefinition"/> the
    /// simulation runs on.
    /// <para>
    /// The geometry here is part-local: X runs from 0, and Y is relative to the part's own level. A part
    /// begins and ends on flat, hazard-free ground at local Y=0 and the climbs between parts carry all the
    /// altitude, so a part plays the same wherever in a map it is composed.
    /// </para>
    /// <para>
    /// Authored in <c>GameConfigSource/CourseParts.json</c> rather than in a sheet, and read from it by
    /// <see cref="CoursePartJson"/>; see <see cref="GameConfigBuild"/>.
    /// </para>
    /// </summary>
    [MetaSerializable]
    [MetaBlockedMembers(6)]
    public class CoursePartInfo : IGameConfigData<CoursePartId>
    {
        [MetaMember(1)] public CoursePartId Id { get; private set; }

        /// <summary>
        /// Part-local ground, sorted and non-overlapping. The gaps between segments are pits, and so is every
        /// segment flagged <see cref="GroundSegment.IsPit"/>, which is the floor of one between its two lips.
        /// </summary>
        [MetaMember(2)] public List<GroundSegment> Ground { get; private set; } = new List<GroundSegment>();

        /// <summary>Part-local spike beds, sorted and non-overlapping, each standing on one ground segment.</summary>
        [MetaMember(3)] public List<HazardSpan> Hazards { get; private set; } = new List<HazardSpan>();

        /// <summary>Which of the three positions of a map this part may be composed into.</summary>
        [MetaMember(4)] public List<CoursePartPosition> Positions { get; private set; } = new List<CoursePartPosition>();

        /// <summary>A showcase section of a map. A composition takes at most one set piece.</summary>
        [MetaMember(5)] public bool IsSetPiece { get; private set; }

        /// <summary>Part-local drooping bridges, sorted and non-overlapping, each over one hazard-free stretch of ground.</summary>
        [MetaMember(7)] public List<CoursePartBridge> Bridges { get; private set; } = new List<CoursePartBridge>();

        /// <summary>
        /// Part-local cosmetic props, in the order they were placed, each standing on the ground at its X. They
        /// are drawn and nothing else: see <see cref="CoursePartDecoration"/>.
        /// </summary>
        [MetaMember(8)] public List<CoursePartDecoration> Decorations { get; private set; } = new List<CoursePartDecoration>();

        /// <summary>
        /// Part-local hanging vines, sorted and non-overlapping, each with the platform its swing lands on; see
        /// <see cref="CoursePartVine"/>.
        /// </summary>
        [MetaMember(9)] public List<CoursePartVine> Vines { get; private set; } = new List<CoursePartVine>();

        public CoursePartId ConfigKey => Id;

        /// <summary>Flat, hazard-free metres after local X=0, so a runner arrives on ground it can answer for.</summary>
        public static readonly F32 EntryClearance = F32.FromInt(4);

        /// <summary>Flat, hazard-free metres before <see cref="Length"/>, so nothing is owed at the hand-over.</summary>
        public static readonly F32 ExitClearance = F32.FromInt(4);

        /// <summary>
        /// Where the part hands over, in part-local metres: the bottom torii of the next climb, or the finish
        /// line on the last part of a map.
        /// </summary>
        public F32 Length => Ground[Ground.Count - 1].EndX;

        /// <summary>Whether this part may be composed into <paramref name="position"/>.</summary>
        public bool MayOccupy(CoursePartPosition position) => Positions.Contains(position);

        CoursePartInfo() { }

        /// <summary>A part built from these values. A list left out is an empty one.</summary>
        public CoursePartInfo(
            CoursePartId             id,
            List<GroundSegment>      ground,
            List<HazardSpan>         hazards,
            List<CoursePartPosition> positions,
            bool                     isSetPiece,
            List<CoursePartBridge>   bridges = null,
            List<CoursePartDecoration> decorations = null,
            List<CoursePartVine>     vines = null)
        {
            Id            = id;
            Ground        = ground    ?? new List<GroundSegment>();
            Hazards       = hazards   ?? new List<HazardSpan>();
            Positions     = positions ?? new List<CoursePartPosition>();
            IsSetPiece    = isSetPiece;
            Bridges       = bridges   ?? new List<CoursePartBridge>();
            Decorations   = decorations ?? new List<CoursePartDecoration>();
            Vines         = vines     ?? new List<CoursePartVine>();
        }

        /// <summary>
        /// The height of the part-local ground a decoration at <paramref name="x"/> stands on: the segment that
        /// covers <c>[StartX, EndX)</c> there, or the one that ends exactly at it at the part's far end or the
        /// near lip of a gap. False over a gap or off the part.
        /// </summary>
        public bool TryGroundTopAt(F32 x, out F32 topY)
        {
            foreach (GroundSegment segment in Ground)
            {
                if (x >= segment.StartX && x < segment.EndX)
                {
                    topY = segment.TopY;
                    return true;
                }
            }
            foreach (GroundSegment segment in Ground)
            {
                if (x == segment.EndX)
                {
                    topY = segment.TopY;
                    return true;
                }
            }

            topY = F32.Zero;
            return false;
        }

        /// <summary>
        /// Every rule the composer relies on that this part breaks, in plain words. Empty when the part is
        /// sound. <see cref="Problems"/> is the same list with the rule each message breaks.
        /// </summary>
        public IReadOnlyList<string> Validate()
        {
            List<string> messages = new List<string>();
            foreach (CoursePartProblem problem in Problems())
                messages.Add(problem.Message);
            return messages;
        }

        /// <summary>
        /// Every rule this part breaks, each as the rule and the message <see cref="Validate"/> prints for it.
        /// </summary>
        public IReadOnlyList<CoursePartProblem> Problems()
        {
            List<CoursePartProblem> problems = new List<CoursePartProblem>();

            if (Id == null)
                problems.Add(new CoursePartProblem(CoursePartRule.Id, "The part has no id."));
            else if (Id.Value.IndexOf(CourseComposer.PartSeparator) >= 0)
                problems.Add(new CoursePartProblem(CoursePartRule.Id, $"The id '{Id.Value}' holds a '{CourseComposer.PartSeparator}', which is what joins part ids into a map id."));

            if (Ground.Count == 0)
            {
                problems.Add(new CoursePartProblem(CoursePartRule.Ground, "The part has no ground, so there is nothing to run along."));
            }
            else
            {
                ValidateGround(problems);
                ValidateHazards(problems);
                ValidateBridges(problems);
                ValidateDecorations(problems);
                ValidateVines(problems);
            }

            if (Positions.Count == 0)
                problems.Add(new CoursePartProblem(CoursePartRule.Positions, "The part names no position it may be composed into, so no map could ever hold it."));
            else
            {
                for (int ndx = 0; ndx < Positions.Count; ndx++)
                {
                    for (int earlier = 0; earlier < ndx; earlier++)
                    {
                        if (Positions[earlier] == Positions[ndx])
                        {
                            problems.Add(new CoursePartProblem(CoursePartRule.Positions, $"The {Positions[ndx]} position is named twice."));
                            break;
                        }
                    }
                }
            }

            return problems;
        }

        /// <summary>
        /// Checks the part's ground segments and pit floors.
        /// </summary>
        void ValidateGround(List<CoursePartProblem> problems)
        {
            for (int ndx = 0; ndx < Ground.Count; ndx++)
            {
                GroundSegment segment = Ground[ndx];
                if (segment.EndX <= segment.StartX)
                    problems.Add(new CoursePartProblem(CoursePartRule.GroundSegmentLength, $"The ground segment at {segment.StartX} m ends at {segment.EndX} m, so it has no length."));
                if (ndx > 0 && segment.StartX < Ground[ndx - 1].EndX)
                    problems.Add(new CoursePartProblem(CoursePartRule.GroundOrder, $"The ground segment at {segment.StartX} m starts before the one before it ends, at {Ground[ndx - 1].EndX} m."));
            }

            GroundSegment first = Ground[0];
            GroundSegment last  = Ground[Ground.Count - 1];

            if (first.StartX != F32.Zero)
                problems.Add(new CoursePartProblem(CoursePartRule.GroundStart, $"The part's ground starts at {first.StartX} m. Part-local X runs from 0."));
            if (first.TopY != F32.Zero)
                problems.Add(new CoursePartProblem(CoursePartRule.EntryLevel, $"The part is entered at {first.TopY} m. A part is entered at local Y=0, so the climbs carry all of a map's altitude."));
            if (last.TopY != F32.Zero)
                problems.Add(new CoursePartProblem(CoursePartRule.ExitLevel, $"The part is left at {last.TopY} m. A part is left at local Y=0, so the climbs carry all of a map's altitude."));

            if (first.EndX - first.StartX < EntryClearance)
                problems.Add(new CoursePartProblem(CoursePartRule.EntryGround, $"The part is entered on {first.EndX - first.StartX} m of ground. It needs {EntryClearance} m of flat ground to arrive on."));
            if (last.EndX - last.StartX < ExitClearance)
                problems.Add(new CoursePartProblem(CoursePartRule.ExitGround, $"The part is left from {last.EndX - last.StartX} m of ground. It needs {ExitClearance} m of flat ground to hand over from."));

            // The only place pit floors are checked. Composing sound parts cannot break them, because a pit
            // floor's lips are in its own part and move with it.
            for (int ndx = 0; ndx < Ground.Count; ndx++)
            {
                GroundSegment segment = Ground[ndx];
                if (!segment.IsPit)
                    continue;

                if (ndx == 0 || ndx == Ground.Count - 1)
                {
                    problems.Add(new CoursePartProblem(CoursePartRule.PitFloorAtEnd, $"The pit floor from {segment.StartX} m to {segment.EndX} m is the part's {(ndx == 0 ? "first" : "last")} stretch of ground. A pit's lips are in the same part as its floor."));
                    continue;
                }

                GroundSegment before = Ground[ndx - 1];
                GroundSegment after  = Ground[ndx + 1];
                if (before.EndX != segment.StartX || after.StartX != segment.EndX)
                    problems.Add(new CoursePartProblem(CoursePartRule.PitFloorLips, $"The pit floor from {segment.StartX} m to {segment.EndX} m does not touch the ground on both sides of it. A pit floor runs from one lip to the other."));
                else if (before.TopY <= segment.TopY || after.TopY <= segment.TopY)
                    problems.Add(new CoursePartProblem(CoursePartRule.PitFloorDepth, $"The pit floor from {segment.StartX} m to {segment.EndX} m does not lie below the ground on both sides of it. A pit floor is lower than both of its lips."));
            }
        }

        /// <summary>
        /// Checks the part's spike beds.
        /// </summary>
        void ValidateHazards(List<CoursePartProblem> problems)
        {
            F32 exitClearanceStart = Length - ExitClearance;

            for (int ndx = 0; ndx < Hazards.Count; ndx++)
            {
                HazardSpan hazard = Hazards[ndx];

                if (hazard.EndX <= hazard.StartX)
                    problems.Add(new CoursePartProblem(CoursePartRule.HazardWidth, $"The spike bed at {hazard.StartX} m ends at {hazard.EndX} m, so it has no width."));
                if (ndx > 0 && hazard.StartX < Hazards[ndx - 1].EndX)
                    problems.Add(new CoursePartProblem(CoursePartRule.HazardOrder, $"The spike bed at {hazard.StartX} m starts before the one before it ends, at {Hazards[ndx - 1].EndX} m."));

                if (!LiesWithinOneGroundSegment(hazard.StartX, hazard.EndX))
                    problems.Add(new CoursePartProblem(CoursePartRule.HazardGround, $"The spike bed from {hazard.StartX} m to {hazard.EndX} m does not stand on one stretch of ground."));

                if (hazard.StartX < EntryClearance)
                    problems.Add(new CoursePartProblem(CoursePartRule.HazardEntryClearance, $"The spike bed at {hazard.StartX} m is inside the {EntryClearance} m the part is entered on."));
                if (hazard.EndX > exitClearanceStart)
                    problems.Add(new CoursePartProblem(CoursePartRule.HazardExitClearance, $"The spike bed ending at {hazard.EndX} m is inside the {ExitClearance} m the part hands over from."));
            }
        }

        /// <summary>
        /// Checks the part's bridges.
        /// </summary>
        void ValidateBridges(List<CoursePartProblem> problems)
        {
            F32 exitClearanceStart = Length - ExitClearance;

            for (int ndx = 0; ndx < Bridges.Count; ndx++)
            {
                CoursePartBridge bridge = Bridges[ndx];

                if (bridge.EndX <= bridge.StartX)
                    problems.Add(new CoursePartProblem(CoursePartRule.BridgeSpan, $"The bridge at {bridge.StartX} m ends at {bridge.EndX} m, so it has no span."));
                if (ndx > 0 && bridge.StartX < Bridges[ndx - 1].EndX)
                    problems.Add(new CoursePartProblem(CoursePartRule.BridgeOrder, $"The bridge at {bridge.StartX} m starts before the one before it ends, at {Bridges[ndx - 1].EndX} m."));

                // The renderer hides the art of the ground under a deck while collision keeps reading it. This is
                // the only place a bridge over a pit floor is refused: composing sound parts cannot put one there,
                // because a part's bridges and pit floors move together and both stay clear of its ends.
                if (!LiesWithinOneGroundSegment(bridge.StartX, bridge.EndX))
                    problems.Add(new CoursePartProblem(CoursePartRule.BridgeGround, $"The bridge from {bridge.StartX} m to {bridge.EndX} m does not hang over one stretch of ground."));
                else if (FindPitFloorUnder(bridge.StartX, bridge.EndX) is GroundSegment floor)
                    problems.Add(new CoursePartProblem(CoursePartRule.BridgeOverPitFloor, $"The bridge from {bridge.StartX} m to {bridge.EndX} m hangs over the pit floor from {floor.StartX} m to {floor.EndX} m. A bridge hangs over ordinary ground."));

                foreach (HazardSpan hazard in Hazards)
                {
                    if (hazard.StartX < bridge.EndX && hazard.EndX > bridge.StartX)
                        problems.Add(new CoursePartProblem(CoursePartRule.HazardUnderBridge, $"The spike bed at {hazard.StartX} m lies under the bridge from {bridge.StartX} m to {bridge.EndX} m."));
                }

                if (bridge.StartX < EntryClearance)
                    problems.Add(new CoursePartProblem(CoursePartRule.BridgeEntryClearance, $"The bridge at {bridge.StartX} m is inside the {EntryClearance} m the part is entered on."));
                if (bridge.EndX > exitClearanceStart)
                    problems.Add(new CoursePartProblem(CoursePartRule.BridgeExitClearance, $"The bridge ending at {bridge.EndX} m is inside the {ExitClearance} m the part hands over from."));
            }
        }

        /// <summary>
        /// Checks the part's decorations.
        /// </summary>
        void ValidateDecorations(List<CoursePartProblem> problems)
        {
            // A decoration changes nothing a run does, so placement is checked only against the ground it is
            // planted on. Over a spike bed, in a clearance or beside another is fine.
            foreach (CoursePartDecoration decoration in Decorations)
            {
                string kind = decoration.Kind ?? "";
                if (!CoursePartDecoration.IsWellFormedKind(kind))
                    problems.Add(new CoursePartProblem(CoursePartRule.DecorationKind, $"The decoration at {decoration.X} m names the kind '{kind}'. A kind is lower-case letters and digits joined by hyphens."));

                if (decoration.Layer != CourseDecorationLayer.Behind && decoration.Layer != CourseDecorationLayer.Front)
                    problems.Add(new CoursePartProblem(CoursePartRule.DecorationLayer, $"The decoration at {decoration.X} m is in layer {(int)decoration.Layer}, which is neither Behind nor Front."));

                if (!TryGroundTopAt(decoration.X, out F32 _))
                    problems.Add(new CoursePartProblem(CoursePartRule.DecorationGround, $"The decoration at {decoration.X} m does not stand on the part's ground."));

                foreach (CoursePartBridge bridge in Bridges)
                {
                    if (decoration.X > bridge.StartX && decoration.X < bridge.EndX)
                        problems.Add(new CoursePartProblem(CoursePartRule.DecorationOnBridge, $"The decoration at {decoration.X} m stands on the bridge from {bridge.StartX} m to {bridge.EndX} m, whose deck sags away from the ground it would be planted on."));
                }
            }
        }

        /// <summary>
        /// Checks the part's vines keep the simulation's catch, swing and landing well-defined inside the part.
        /// </summary>
        void ValidateVines(List<CoursePartProblem> problems)
        {
            F32 exitClearanceStart = Length - ExitClearance;

            for (int ndx = 0; ndx < Vines.Count; ndx++)
            {
                CoursePartVine vine = Vines[ndx];

                string named = $"The vine hanging at {vine.AnchorX} m";
                string platform = $"The platform of the vine hanging at {vine.AnchorX} m";

                if (ndx > 0 && vine.SwingStartX < Vines[ndx - 1].ReachEndX)
                    problems.Add(new CoursePartProblem(CoursePartRule.VineOrder, $"{named} swings back to {vine.SwingStartX} m, before the reach of the one before it ends, at {Vines[ndx - 1].ReachEndX} m."));

                // Anchor clear of both clearances and the whole swing on the part, so no climb's torii is ever
                // passed on the rope.
                if (vine.AnchorX < EntryClearance)
                    problems.Add(new CoursePartProblem(CoursePartRule.VineEntryClearance, $"{named} is inside the {EntryClearance} m the part is entered on."));
                if (vine.AnchorX > exitClearanceStart)
                    problems.Add(new CoursePartProblem(CoursePartRule.VineExitClearance, $"{named} is inside the {ExitClearance} m the part hands over from."));

                if (vine.SwingStartX < F32.Zero)
                    problems.Add(new CoursePartProblem(CoursePartRule.VineSwingStart, $"{named} swings back to {vine.SwingStartX} m, off the start of the part."));
                if (vine.SwingEndX > Length)
                    problems.Add(new CoursePartProblem(CoursePartRule.VineSwingEnd, $"{named} swings out to {vine.SwingEndX} m, past where the part hands over at {Length} m."));

                // Nothing collides with the ground while the rope carries the runner.
                foreach (GroundSegment segment in Ground)
                {
                    if (segment.StartX < vine.SwingEndX && segment.EndX > vine.SwingStartX && segment.TopY > vine.LowestFeetY)
                    {
                        problems.Add(new CoursePartProblem(CoursePartRule.VineHeight, $"{named} swings a runner's feet down to {vine.LowestFeetY} m, below the ground from {segment.StartX} m to {segment.EndX} m at {segment.TopY} m. A vine hangs high enough that the bottom of its swing clears the ground."));
                        break;
                    }
                }

                if (vine.PlatformEndX <= vine.PlatformStartX)
                    problems.Add(new CoursePartProblem(CoursePartRule.PlatformWidth, $"{platform} starts at {vine.PlatformStartX} m and ends at {vine.PlatformEndX} m, so it has no width."));
                if (vine.PlatformStartX < vine.AnchorX)
                    problems.Add(new CoursePartProblem(CoursePartRule.PlatformBehindAnchor, $"{platform} starts at {vine.PlatformStartX} m, behind the anchor. A vine's platform lies ahead of it, where the swing carries a runner."));

                if (vine.PlatformStartX < F32.Zero || vine.PlatformEndX > Length)
                    problems.Add(new CoursePartProblem(CoursePartRule.PlatformOffPart, $"{platform} runs from {vine.PlatformStartX} m to {vine.PlatformEndX} m, off the part, which runs from 0 m to {Length} m."));
                else if (vine.PlatformEndX > exitClearanceStart)
                    problems.Add(new CoursePartProblem(CoursePartRule.PlatformExitClearance, $"{platform} ends at {vine.PlatformEndX} m, inside the {ExitClearance} m the part hands over from."));

                foreach (GroundSegment segment in Ground)
                {
                    if (segment.StartX < vine.PlatformEndX && segment.EndX > vine.PlatformStartX && segment.TopY >= vine.PlatformTopY)
                    {
                        problems.Add(new CoursePartProblem(CoursePartRule.PlatformHeight, $"{platform} stands at {vine.PlatformTopY} m, not above the ground from {segment.StartX} m to {segment.EndX} m at {segment.TopY} m."));
                        break;
                    }
                }

                foreach (HazardSpan hazard in Hazards)
                {
                    if (hazard.StartX < vine.PlatformEndX && hazard.EndX > vine.PlatformStartX)
                        problems.Add(new CoursePartProblem(CoursePartRule.HazardUnderPlatform, $"The spike bed at {hazard.StartX} m lies under the platform of the vine hanging at {vine.AnchorX} m."));
                }

                foreach (CoursePartBridge bridge in Bridges)
                {
                    if (bridge.StartX < vine.PlatformEndX && bridge.EndX > vine.PlatformStartX)
                        problems.Add(new CoursePartProblem(CoursePartRule.BridgeUnderPlatform, $"The bridge from {bridge.StartX} m to {bridge.EndX} m lies under the platform of the vine hanging at {vine.AnchorX} m."));
                }
            }
        }

        /// <summary>The pit-flagged ground segment the span from <paramref name="startX"/> to <paramref name="endX"/> overlaps, or null.</summary>
        GroundSegment FindPitFloorUnder(F32 startX, F32 endX)
        {
            foreach (GroundSegment segment in Ground)
            {
                if (segment.IsPit && startX < segment.EndX && endX > segment.StartX)
                    return segment;
            }
            return null;
        }

        /// <summary>Whether one ground segment covers the whole of the span from <paramref name="startX"/> to <paramref name="endX"/>.</summary>
        bool LiesWithinOneGroundSegment(F32 startX, F32 endX)
        {
            foreach (GroundSegment segment in Ground)
            {
                if (startX >= segment.StartX && endX <= segment.EndX)
                    return true;
            }
            return false;
        }
    }

    /// <summary>
    /// Which of a part's rules a <see cref="CoursePartProblem"/> breaks. A problem's message carries the
    /// coordinates it is about, and those move when the part is edited; the rule does not.
    /// </summary>
    public enum CoursePartRule
    {
        Id,
        Ground,
        Positions,
        GroundSegmentLength,
        GroundOrder,
        GroundStart,
        EntryLevel,
        ExitLevel,
        EntryGround,
        ExitGround,
        PitFloorAtEnd,
        PitFloorLips,
        PitFloorDepth,
        HazardWidth,
        HazardOrder,
        HazardGround,
        HazardEntryClearance,
        HazardExitClearance,
        BridgeSpan,
        BridgeOrder,
        BridgeGround,
        BridgeOverPitFloor,
        HazardUnderBridge,
        BridgeEntryClearance,
        BridgeExitClearance,
        DecorationKind,
        DecorationLayer,
        DecorationGround,
        DecorationOnBridge,
        VineOrder,
        VineEntryClearance,
        VineExitClearance,
        VineSwingStart,
        VineSwingEnd,
        VineHeight,
        PlatformWidth,
        PlatformBehindAnchor,
        PlatformOffPart,
        PlatformExitClearance,
        PlatformHeight,
        HazardUnderPlatform,
        BridgeUnderPlatform,
    }

    /// <summary>One rule a part breaks, and what <see cref="CoursePartInfo.Validate"/> says about it.</summary>
    public readonly struct CoursePartProblem
    {
        public CoursePartRule Rule    { get; }
        public string         Message { get; }

        public CoursePartProblem(CoursePartRule rule, string message)
        {
            Rule    = rule;
            Message = message;
        }

        public override string ToString() => Message;
    }
}
