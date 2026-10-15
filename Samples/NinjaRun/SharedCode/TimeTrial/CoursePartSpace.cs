using Metaplay.Core.Math;
using System.Collections.Generic;

namespace Game.Logic
{
    /// <summary>
    /// Opening up or closing down a stretch of a part at one X, moving everything past it along with it: the
    /// part editor's add/remove space tool. It works on a whole <see cref="CoursePartInfo"/>, so a part stays in
    /// one piece, and refuses an edit that would leave the part breaking a rule it did not already break.
    /// </summary>
    public static class CoursePartSpace
    {
        /// <summary>
        /// <paramref name="part"/> with <paramref name="amount"/> metres of space opened at <paramref name="x"/>,
        /// or false and why not.
        /// </summary>
        public static bool TryAdd(CoursePartInfo part, F32 x, F32 amount, out CoursePartInfo result, out string refusal)
        {
            result = null;
            if (part == null || part.Ground.Count == 0)
            {
                refusal = "The part has no ground to add space to.";
                return false;
            }
            if (amount <= F32.Zero)
            {
                refusal = "The space to add must be more than 0 m.";
                return false;
            }
            if (x < F32.Zero || x > part.Length)
            {
                refusal = $"{x} m is not on the part, which runs from 0 m to {part.Length} m.";
                return false;
            }

            // Everything at or past x moves on. A span that straddles x is lengthened, and one that starts exactly
            // at x moves whole, which widens the gap before it.
            F32 Shift(F32 c) => c >= x ? c + amount : c;

            CoursePartInfo shifted = Rebuild(part, Shift);
            return Accept(part, shifted, out result, out refusal);
        }

        /// <summary>
        /// <paramref name="part"/> with the span from <paramref name="x"/> to <paramref name="x"/> +
        /// <paramref name="amount"/> taken out, or false and what is in the way.
        /// </summary>
        public static bool TryRemove(CoursePartInfo part, F32 x, F32 amount, out CoursePartInfo result, out string refusal)
        {
            result = null;
            if (part == null || part.Ground.Count == 0)
            {
                refusal = "The part has no ground to remove space from.";
                return false;
            }
            if (amount <= F32.Zero)
            {
                refusal = "The space to remove must be more than 0 m.";
                return false;
            }

            F32 end = x + amount;
            if (x < F32.Zero || end > part.Length)
            {
                refusal = $"The span from {x} m to {end} m is not all on the part, which runs from 0 m to {part.Length} m.";
                return false;
            }

            foreach (HazardSpan hazard in part.Hazards)
            {
                if (hazard.StartX < end && hazard.EndX > x)
                {
                    refusal = $"The span from {x} m to {end} m would cut the spike bed from {hazard.StartX} m to {hazard.EndX} m. Move or remove it first.";
                    return false;
                }
            }
            foreach (CoursePartBridge bridge in part.Bridges)
            {
                if (bridge.StartX < end && bridge.EndX > x)
                {
                    refusal = $"The span from {x} m to {end} m would cut the bridge from {bridge.StartX} m to {bridge.EndX} m. Move or remove it first.";
                    return false;
                }
            }
            foreach (CoursePartDecoration decoration in part.Decorations)
            {
                if (decoration.X > x && decoration.X < end)
                {
                    refusal = $"The span from {x} m to {end} m would take the {decoration.Kind} at {decoration.X} m. Move or remove it first.";
                    return false;
                }
            }
            foreach (CoursePartVine vine in part.Vines)
            {
                F32 vineStart = F32.Min(vine.AnchorX, vine.PlatformStartX);
                F32 vineEnd   = F32.Max(vine.AnchorX, vine.PlatformEndX);
                if (vineStart < end && vineEnd > x)
                {
                    refusal = $"The span from {x} m to {end} m would cut the vine hanging at {vine.AnchorX} m, whose platform runs from {vine.PlatformStartX} m to {vine.PlatformEndX} m. Move or remove it first.";
                    return false;
                }
            }
            for (int ndx = 0; ndx < part.Ground.Count; ndx++)
            {
                GroundSegment segment = part.Ground[ndx];
                if (segment.StartX >= x && segment.EndX <= end)
                {
                    refusal = $"The span from {x} m to {end} m would take the whole stretch of ground from {segment.StartX} m to {segment.EndX} m. Remove the stretch itself, or less space.";
                    return false;
                }
                if (ndx + 1 < part.Ground.Count)
                {
                    F32 gapStart = segment.EndX;
                    F32 gapEnd   = part.Ground[ndx + 1].StartX;
                    if (gapEnd > gapStart && gapStart >= x && gapEnd <= end)
                    {
                        refusal = $"The span from {x} m to {end} m would close the whole gap from {gapStart} m to {gapEnd} m. Remove less space, or join the ground first.";
                        return false;
                    }
                }
            }

            // Everything at or past the span's far end moves back, and a coordinate inside it lands on x. After the
            // checks above, only ground and gaps can reach into the span, and they are shortened, so nothing is
            // split or deleted.
            F32 Collapse(F32 c) => c <= x ? c : c >= end ? c - amount : x;

            CoursePartInfo shifted = Rebuild(part, Collapse);
            return Accept(part, shifted, out result, out refusal);
        }

        /// <summary>
        /// A copy of <paramref name="part"/> with every X (the ends of ground, spike beds, bridges and vine
        /// platforms, and the places of decorations and vine anchors) passed through <paramref name="mapX"/>.
        /// </summary>
        static CoursePartInfo Rebuild(CoursePartInfo part, System.Func<F32, F32> mapX)
        {
            List<GroundSegment> ground = new List<GroundSegment>();
            foreach (GroundSegment segment in part.Ground)
                ground.Add(new GroundSegment(mapX(segment.StartX), mapX(segment.EndX), segment.TopY, segment.IsPit));

            List<HazardSpan> hazards = new List<HazardSpan>();
            foreach (HazardSpan hazard in part.Hazards)
                hazards.Add(new HazardSpan(mapX(hazard.StartX), mapX(hazard.EndX)));

            List<CoursePartBridge> bridges = new List<CoursePartBridge>();
            foreach (CoursePartBridge bridge in part.Bridges)
                bridges.Add(new CoursePartBridge(mapX(bridge.StartX), mapX(bridge.EndX)));

            List<CoursePartDecoration> decorations = new List<CoursePartDecoration>();
            foreach (CoursePartDecoration decoration in part.Decorations)
                decorations.Add(new CoursePartDecoration(decoration.Kind, mapX(decoration.X), decoration.Layer, decoration.Flip));

            List<CoursePartVine> vines = new List<CoursePartVine>();
            foreach (CoursePartVine vine in part.Vines)
                vines.Add(new CoursePartVine(mapX(vine.AnchorX), vine.AnchorY, mapX(vine.PlatformStartX), mapX(vine.PlatformEndX), vine.PlatformTopY));

            return new CoursePartInfo(
                part.Id,
                ground,
                hazards,
                new List<CoursePartPosition>(part.Positions),
                part.IsSetPiece,
                bridges,
                decorations,
                vines);
        }

        /// <summary>
        /// The shifted part, unless it breaks a rule more often than the part it came from did.
        /// </summary>
        static bool Accept(CoursePartInfo before, CoursePartInfo after, out CoursePartInfo result, out string refusal)
        {
            // Problems are counted by rule, not by message, because a message names coordinates the edit has
            // just moved. Matched by message, a problem the part already had would read as new.
            Dictionary<CoursePartRule, int> already = new Dictionary<CoursePartRule, int>();
            foreach (CoursePartProblem problem in before.Problems())
                already[problem.Rule] = already.GetValueOrDefault(problem.Rule) + 1;

            foreach (CoursePartProblem problem in after.Problems())
            {
                int left = already.GetValueOrDefault(problem.Rule);
                if (left == 0)
                {
                    result  = null;
                    refusal = $"That would break a rule: {problem.Message}";
                    return false;
                }

                already[problem.Rule] = left - 1;
            }

            result  = after;
            refusal = null;
            return true;
        }
    }
}
