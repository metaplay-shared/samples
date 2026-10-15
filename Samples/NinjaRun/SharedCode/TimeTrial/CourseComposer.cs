using Metaplay.Core.Math;
using System;
using System.Collections.Generic;

namespace Game.Logic
{
    /// <summary>
    /// Flattens the course parts in the published game config into an ordinary <see cref="CourseDefinition"/>.
    /// A map is an ordered triplet of parts, and its id is that triplet, so nothing has to be looked up to know
    /// what a map is made of.
    /// <para>
    /// This runs in shared code, so a client and the server compose bit-identically from the archive their
    /// session is pinned to.
    /// </para>
    /// </summary>
    public static class CourseComposer
    {
        /// <summary>What joins part ids into a map id. No part id may hold it.</summary>
        public const char PartSeparator = '+';

        /// <summary>
        /// How many parts a map is composed of. The HUD, the score screen and the Orb payout are built on three
        /// segments, so this is not a free tunable.
        /// </summary>
        public const int PartsPerMap = 3;

        /// <summary>
        /// Ground before the start line, in metres: the run-up a composed map opens on, laid down ahead of the
        /// first part's own ground so the runner reaches the start line already on flat ground.
        /// </summary>
        public static readonly F32 HeadLength = F32.FromInt(12);

        /// <summary>Ground past the finish line, in metres, so the runner crosses it rather than stopping on it.</summary>
        public static readonly F32 TailLength = F32.FromInt(8);

        /// <summary>The position each part of a map occupies, in composition order.</summary>
        public static readonly IReadOnlyList<CoursePartPosition> PositionsInOrder = new[]
        {
            CoursePartPosition.First,
            CoursePartPosition.Middle,
            CoursePartPosition.Last,
        };

        /// <summary>The id of the map composed of these three parts: the part ids joined by <see cref="PartSeparator"/>.</summary>
        public static CourseId MapIdFor(CoursePartId first, CoursePartId middle, CoursePartId last)
            => CourseId.FromString($"{first.Value}{PartSeparator}{middle.Value}{PartSeparator}{last.Value}");

        /// <summary>
        /// Split a map id back into its part ids. False for anything that is not exactly
        /// <see cref="PartsPerMap"/> non-empty pieces.
        /// </summary>
        public static bool TryParseMapId(CourseId mapId, out CoursePartId[] partIds)
        {
            partIds = null;

            if (mapId == null)
                return false;

            string[] pieces = mapId.Value.Split(PartSeparator);
            if (pieces.Length != PartsPerMap)
                return false;

            CoursePartId[] parsed = new CoursePartId[PartsPerMap];
            for (int ndx = 0; ndx < pieces.Length; ndx++)
            {
                if (pieces[ndx].Length == 0)
                    return false;
                parsed[ndx] = CoursePartId.FromString(pieces[ndx]);
            }

            partIds = parsed;
            return true;
        }

        /// <summary>
        /// Whether these three parts may be composed into a map, and why not when they may not. A map is three
        /// different parts, each eligible for the position it is in, and at most one of them a set piece.
        /// </summary>
        public static bool IsLegalTriplet(IReadOnlyList<CoursePartInfo> parts, out string reason)
        {
            if (parts == null || parts.Count != PartsPerMap)
            {
                reason = $"A map is {PartsPerMap} parts, not {parts?.Count ?? 0}.";
                return false;
            }

            int setPieceCount = 0;

            for (int ndx = 0; ndx < parts.Count; ndx++)
            {
                CoursePartInfo part = parts[ndx];
                if (part == null)
                {
                    reason = $"There is no part in the {PositionsInOrder[ndx]} position.";
                    return false;
                }

                for (int earlier = 0; earlier < ndx; earlier++)
                {
                    if (parts[earlier].Id == part.Id)
                    {
                        reason = $"{part.Id} is used twice, and a map takes three different parts.";
                        return false;
                    }
                }

                if (!part.MayOccupy(PositionsInOrder[ndx]))
                {
                    reason = $"{part.Id} may not be composed into the {PositionsInOrder[ndx]} position.";
                    return false;
                }

                if (part.IsSetPiece)
                    setPieceCount++;
            }

            if (setPieceCount > 1)
            {
                reason = $"{setPieceCount} of the parts are set pieces, and a map takes at most one.";
                return false;
            }

            reason = null;
            return true;
        }

        /// <summary>
        /// The course a map id names, composed from the parts in <paramref name="config"/>. Throws
        /// <see cref="ArgumentException"/> when this config composes no such map.
        /// </summary>
        public static CourseDefinition Compose(CourseId mapId, SharedGameConfig config)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));

            if (!TryLookUpParts(mapId, config, out List<CoursePartInfo> parts, out string reason))
                throw new ArgumentException(reason, nameof(mapId));

            return Compose(mapId, parts, config.Global);
        }

        /// <summary>
        /// The course a map id names, or false and no course when this config composes no such map. This is the
        /// form for a course id that arrives from outside, where not being a map is an ordinary answer.
        /// <para>
        /// The parts themselves are trusted, because the config build refuses to publish a part that breaks
        /// one of its rules.
        /// </para>
        /// </summary>
        public static bool TryCompose(CourseId mapId, SharedGameConfig config, out CourseDefinition course)
        {
            course = null;

            if (!TryLookUpParts(mapId, config, out List<CoursePartInfo> parts, out string _))
                return false;

            course = Compose(mapId, parts, config.Global);
            return true;
        }

        /// <summary>
        /// The parts a map id names, in composition order, or false and the reason it names none. This is the
        /// one place that decides what counts as a map.
        /// </summary>
        public static bool TryLookUpParts(CourseId mapId, SharedGameConfig config, out List<CoursePartInfo> parts, out string reason)
        {
            parts = null;

            if (config == null)
            {
                reason = "There is no config to compose a map from.";
                return false;
            }

            if (!TryParseMapId(mapId, out CoursePartId[] partIds))
            {
                reason = $"'{mapId}' is not a map id: a map id is {PartsPerMap} part ids joined by '{PartSeparator}'.";
                return false;
            }

            List<CoursePartInfo> named = new List<CoursePartInfo>(partIds.Length);
            foreach (CoursePartId partId in partIds)
            {
                CoursePartInfo part = config.CourseParts?.GetValueOrDefault(partId);
                if (part == null)
                {
                    reason = $"Map '{mapId}' names the part '{partId}', which the published config does not hold.";
                    return false;
                }

                named.Add(part);
            }

            if (!IsLegalTriplet(named, out string illegal))
            {
                reason = $"Map '{mapId}' is not a legal composition: {illegal}";
                return false;
            }

            parts  = named;
            reason = null;
            return true;
        }

        /// <summary>
        /// The general form: one or more parts joined by one fewer climbs. A single part is how one part is
        /// looked at on its own.
        /// <para>
        /// The parts must already keep their own rules (<see cref="CoursePartInfo.Validate"/>).
        /// </para>
        /// </summary>
        public static CourseDefinition Compose(CourseId courseId, IReadOnlyList<CoursePartInfo> parts, GlobalConfig global)
        {
            if (courseId == null)
                throw new ArgumentNullException(nameof(courseId));
            if (global == null)
                throw new ArgumentNullException(nameof(global));
            if (parts == null || parts.Count == 0)
                throw new ArgumentException("A course is composed of at least one part.", nameof(parts));

            List<GroundSegment>             ground     = new List<GroundSegment>();
            List<HazardSpan>                hazards    = new List<HazardSpan>();
            List<CourseEncounterDefinition> encounters = new List<CourseEncounterDefinition>();
            List<DroopingBridgePresentationFeature> bridges = new List<DroopingBridgePresentationFeature>();
            List<CourseDecoration>          decorations = new List<CourseDecoration>();

            //          start                      gate                       gate                     finish
            //            |                          |                          |                        |
            //  |--head---+----- part 1 ----|--climb-+----- part 2 ----|--climb-+----- part 3 -----------+--tail--|
            //   HeadLength local X from 0   flat     local X from 0    flat     local X from 0          TailLength
            //                               +slope                    +slope
            //                               +run-out                  +run-out
            //  Y: ------- 0 ----------------/------ rise --------------/----- rise + rise ---------------------
            //
            // Every part is entered and left at local Y=0, so each climb raises the level the next part sits at.

            // Where the part being laid down has its local X=0 and its local Y=0, and where its first ground
            // segment has to begin so that what came before it hands the runner over without a gap.
            F32 cursor      = F32.Zero;
            F32 altitude    = F32.Zero;
            F32 groundStart = -HeadLength;
            F32 finishX     = F32.Zero;

            for (int partNdx = 0; partNdx < parts.Count; partNdx++)
            {
                CoursePartInfo part   = parts[partNdx];
                bool           isLast = partNdx == parts.Count - 1;

                // A part's first segment carries what came before it, the head or the preceding climb's run-out,
                // because CourseDefinition requires the ground above a climb to begin exactly at the climb's top.
                // Its last segment carries what comes after it: the flat on to the foot of the next climb's
                // slope, or the tail past the finish.
                for (int ndx = 0; ndx < part.Ground.Count; ndx++)
                {
                    GroundSegment segment = part.Ground[ndx];
                    F32           startX  = ndx == 0 ? groundStart : cursor + segment.StartX;
                    bool          isExit  = ndx == part.Ground.Count - 1;
                    F32           endX    = !isExit ? cursor + segment.EndX
                                            : isLast  ? cursor + part.Length + TailLength
                                            : cursor + part.Length + ClimbDefinition.FootFlatLength;

                    ground.Add(new GroundSegment(startX, endX, altitude + segment.TopY, segment.IsPit));
                }

                foreach (HazardSpan hazard in part.Hazards)
                    hazards.Add(new HazardSpan(cursor + hazard.StartX, cursor + hazard.EndX));

                for (int ndx = 0; ndx < part.Bridges.Count; ndx++)
                {
                    CoursePartBridge bridge = part.Bridges[ndx];
                    bridges.Add(DroopingBridgePresentationFeature.OverGorge(
                        $"part-{partNdx + 1}-bridge-{ndx + 1}", cursor + bridge.StartX, cursor + bridge.EndX));
                }

                // A decoration stands on the part's ground at its local X, raised with the part to the level
                // the climbs before it carried the part to.
                List<CoursePartDecoration> partDecorations = part.Decorations;
                for (int ndx = 0; ndx < partDecorations.Count; ndx++)
                {
                    CoursePartDecoration decoration = partDecorations[ndx];
                    part.TryGroundTopAt(decoration.X, out F32 localTopY);
                    decorations.Add(new CourseDecoration(
                        id:    $"part-{partNdx + 1}-decoration-{ndx + 1}",
                        kind:  decoration.Kind,
                        x:     cursor + decoration.X,
                        y:     altitude + localTopY,
                        layer: decoration.Layer,
                        flip:  decoration.Flip));
                }

                // A vine hangs from its part-local anchor with the tuned rope, and its platform is carried with
                // it inside the encounter: the simulation lands a runner on it only after that runner caught the
                // rope. Both are raised with the part to the level the climbs before it carried the part to.
                List<CoursePartVine> partVines = part.Vines;
                for (int ndx = 0; ndx < partVines.Count; ndx++)
                {
                    CoursePartVine vine = partVines[ndx];
                    encounters.Add(new VineEncounterDefinition(
                        id:             $"part-{partNdx + 1}-vine-{ndx + 1}",
                        anchorX:        cursor + vine.AnchorX,
                        anchorY:        altitude + vine.AnchorY,
                        platformStartX: cursor + vine.PlatformStartX,
                        platformEndX:   cursor + vine.PlatformEndX,
                        platformTopY:   altitude + vine.PlatformTopY));
                }

                finishX = cursor + part.Length;

                if (isLast)
                    break;

                // The bottom torii stands where the part hands over, and the flat at the slope's foot is laid
                // past it rather than taken out of the part, so nothing the part places moves towards the torii.
                F32 bottomX = cursor + part.Length;
                F32 topX    = bottomX + ClimbDefinition.FootFlatLength + global.CourseClimbSlopeLength;
                F32 topY    = altitude + global.CourseClimbRise;
                F32 gateX   = topX + global.CourseClimbRunOutLength;

                encounters.Add(new ClimbDefinition(
                    id:      $"climb-{partNdx + 1}",
                    bottomX: bottomX,
                    topX:    topX,
                    topY:    topY,
                    gateX:   gateX));

                altitude    = topY;
                cursor      = gateX;
                groundStart = topX;
            }

            return new CourseDefinition(
                id:             courseId,
                displayName:    courseId.Value,
                startX:         F32.Zero,
                finishX:        finishX,
                ground:         ground,
                hazards:        hazards,
                encounters:     encounters,
                droopingBridges: bridges,
                decorations:    decorations);
        }

        /// <summary>
        /// Every map that can legally be composed from the parts in <paramref name="config"/>, in ordinal part
        /// id order, so the same archive always yields the same list.
        /// </summary>
        public static IReadOnlyList<CourseId> LegalMapIds(SharedGameConfig config)
        {
            List<CourseId> mapIds = new List<CourseId>();
            if (config == null || config.CourseParts == null)
                return mapIds;

            List<CoursePartInfo> parts = new List<CoursePartInfo>(config.CourseParts.Values);
            parts.Sort((a, b) => string.CompareOrdinal(a.Id.Value, b.Id.Value));

            CoursePartInfo[] triplet = new CoursePartInfo[PartsPerMap];
            foreach (CoursePartInfo first in parts)
            {
                triplet[0] = first;
                foreach (CoursePartInfo middle in parts)
                {
                    triplet[1] = middle;
                    foreach (CoursePartInfo last in parts)
                    {
                        triplet[2] = last;
                        if (IsLegalTriplet(triplet, out string _))
                            mapIds.Add(MapIdFor(first.Id, middle.Id, last.Id));
                    }
                }
            }

            return mapIds;
        }
    }
}
