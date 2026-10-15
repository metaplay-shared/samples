using Game.Logic;
using Metaplay.Core.Math;
using System.Collections.Generic;

namespace WebClient.Gameplay;

/// <summary>
/// One row of a part draft: a stretch of ground, a spike bed or a bridge, in whole meters along the part and
/// whole decimeters of height. <see cref="Height"/> and <see cref="IsPit"/> are read only for ground.
/// </summary>
public sealed class CoursePartDraftRow
{
    public int  Start  { get; set; }
    public int  End    { get; set; }
    public int  Height { get; set; }

    /// <summary>Whether this stretch of ground is a pit floor at its own height (<see cref="GroundSegment.IsPit"/>).</summary>
    public bool IsPit  { get; set; }
}

/// <summary>
/// One decoration of a part draft: a kind from the renderer's catalogue, where it stands in whole decimetres
/// along the part, which layer it is drawn in, and whether it is mirrored.
/// </summary>
public sealed class CoursePartDraftDecoration
{
    public string Kind  { get; set; } = "";
    public int    X     { get; set; }
    public bool   Front { get; set; }
    public bool   Flip  { get; set; }

    public CourseDecorationLayer Layer => Front ? CourseDecorationLayer.Front : CourseDecorationLayer.Behind;
}

/// <summary>
/// One hanging vine of a part draft (<see cref="CoursePartVine"/>): its anchor in whole metres along the part
/// and whole decimetres of height, and the platform its swing lands on, from and to whole metres at a height in
/// whole decimetres. The rope itself is the tuned one every vine hangs.
/// </summary>
public sealed class CoursePartDraftVine
{
    public int AnchorX        { get; set; }
    public int AnchorHeight   { get; set; }
    public int PlatformStart  { get; set; }
    public int PlatformEnd    { get; set; }
    public int PlatformHeight { get; set; }

    /// <summary>How high a new vine's anchor hangs, decimetres above the part's level.</summary>
    public const int DefaultAnchorHeight = 80;

    /// <summary>How far past a new vine's anchor its platform starts, metres.</summary>
    public const int DefaultPlatformOffset = 4;

    /// <summary>How long a new vine's platform is, metres.</summary>
    public const int DefaultPlatformLength = 15;

    /// <summary>How high a new vine's platform stands, decimetres above the part's level.</summary>
    public const int DefaultPlatformHeight = 30;

    /// <summary>
    /// A vine hanging at <paramref name="anchorX"/> with the default anchor height and platform placement,
    /// both measured up from <paramref name="baseHeight"/> decimetres: the ground the runner jumps for it from.
    /// </summary>
    public static CoursePartDraftVine At(int anchorX, int baseHeight = 0) => new CoursePartDraftVine
    {
        AnchorX        = anchorX,
        AnchorHeight   = baseHeight + DefaultAnchorHeight,
        PlatformStart  = anchorX + DefaultPlatformOffset,
        PlatformEnd    = anchorX + DefaultPlatformOffset + DefaultPlatformLength,
        PlatformHeight = baseHeight + DefaultPlatformHeight,
    };

    public CoursePartVine ToVine()
        => new CoursePartVine(F32.FromInt(AnchorX), F32.Ratio10(AnchorHeight), F32.FromInt(PlatformStart), F32.FromInt(PlatformEnd), F32.Ratio10(PlatformHeight));

    public static CoursePartDraftVine From(CoursePartVine vine) => new CoursePartDraftVine
    {
        AnchorX        = F32.RoundToInt(vine.AnchorX),
        AnchorHeight   = F32.RoundToInt(vine.AnchorY * F32.FromInt(10)),
        PlatformStart  = F32.RoundToInt(vine.PlatformStartX),
        PlatformEnd    = F32.RoundToInt(vine.PlatformEndX),
        PlatformHeight = F32.RoundToInt(vine.PlatformTopY * F32.FromInt(10)),
    };
}

/// <summary>
/// A part as the developer part editor (<c>/dev/parts</c>) holds it while it is being drawn: plain editable state
/// that becomes a <see cref="CoursePartInfo"/> only to be checked, looked at composed alone, and written out
/// as a <c>GameConfigSource/CourseParts.json</c> part object by <see cref="CoursePartJson"/>. Nothing here is
/// saved to the server or the config.
/// </summary>
public sealed class CoursePartDraft
{
    /// <summary>What the draft was started from: a part id, or <see cref="BlankSource"/>.</summary>
    public string Source        { get; set; } = BlankSource;
    public string Id            { get; set; } = "new-part";
    public bool   First         { get; set; } = true;

    /// <summary>
    /// The draft's theme: the altitude band (<see cref="TimeTrialAltitudeBands"/>) it is previewed and played in,
    /// since a part alone has no climb to carry it up the mountain. Editor state, kept with the draft and never
    /// written into the part: a map's parts take their band from their position.
    /// </summary>
    public string AltitudeBand  { get; set; } = TimeTrialAltitudeBands.Foothills;
    public bool   Middle        { get; set; } = true;
    public bool   Last          { get; set; } = true;
    public bool   IsSetPiece    { get; set; }

    /// <summary>What the part object's <c>notes</c> says, if anything: the config keeps no notes, so a draft started from a part has none.</summary>
    public string Notes         { get; set; } = "";

    public List<CoursePartDraftRow> Ground  { get; set; } = new List<CoursePartDraftRow>();
    public List<CoursePartDraftRow> Spikes  { get; set; } = new List<CoursePartDraftRow>();
    public List<CoursePartDraftRow> Bridges { get; set; } = new List<CoursePartDraftRow>();
    public List<CoursePartDraftDecoration> Decorations { get; set; } = new List<CoursePartDraftDecoration>();
    public List<CoursePartDraftVine> Vines { get; set; } = new List<CoursePartDraftVine>();

    /// <summary>The source a draft started from nothing names.</summary>
    public const string BlankSource = "blank";

    /// <summary>A sound part to start from nothing with: one flat 30 m stretch.</summary>
    public static CoursePartDraft Blank()
    {
        CoursePartDraft draft = new CoursePartDraft();
        draft.Ground.Add(new CoursePartDraftRow { Start = 0, End = 30, Height = 0 });
        return draft;
    }

    /// <summary>A copy of <paramref name="part"/>'s geometry, rounded to whole meters and decimeters.</summary>
    public static CoursePartDraft FromPart(CoursePartInfo part)
    {
        CoursePartDraft draft = new CoursePartDraft
        {
            Source        = part.Id.Value,
            Id            = part.Id.Value,
            First         = part.MayOccupy(CoursePartPosition.First),
            Middle        = part.MayOccupy(CoursePartPosition.Middle),
            Last          = part.MayOccupy(CoursePartPosition.Last),
            IsSetPiece    = part.IsSetPiece,
        };

        foreach (GroundSegment segment in part.Ground)
            draft.Ground.Add(new CoursePartDraftRow { Start = F32.RoundToInt(segment.StartX), End = F32.RoundToInt(segment.EndX), Height = F32.RoundToInt(segment.TopY * F32.FromInt(10)), IsPit = segment.IsPit });
        foreach (HazardSpan hazard in part.Hazards)
            draft.Spikes.Add(new CoursePartDraftRow { Start = F32.RoundToInt(hazard.StartX), End = F32.RoundToInt(hazard.EndX) });
        foreach (CoursePartBridge bridge in part.Bridges)
            draft.Bridges.Add(new CoursePartDraftRow { Start = F32.RoundToInt(bridge.StartX), End = F32.RoundToInt(bridge.EndX) });
        CopyDecorations(part, draft.Decorations);
        CopyVines(part, draft.Vines);

        return draft;
    }

    static void CopyVines(CoursePartInfo part, List<CoursePartDraftVine> into)
    {
        foreach (CoursePartVine vine in part.Vines ?? new List<CoursePartVine>())
        {
            if (vine != null)
                into.Add(CoursePartDraftVine.From(vine));
        }
    }

    /// <summary>
    /// A new vine with the default anchor height and platform, added in order among the others at the first
    /// whole metre along the part where it breaks no rule more often than the draft already did. When there is
    /// no such place it is hung past the last vine, or past the entry clearance, and the rules say what is wrong.
    /// </summary>
    public CoursePartDraftVine AddVine()
    {
        // Problems are compared by rule rather than by message, because the messages name coordinates and vine
        // numbers that the new vine shifts.
        Dictionary<CoursePartRule, int> already = RuleCounts(ToPart(out string _));
        int                             length  = Ground.Count > 0 ? Ground[Ground.Count - 1].End : 0;

        CoursePartDraftVine chosen = null;
        for (int x = F32.RoundToInt(CoursePartInfo.EntryClearance); x <= length && chosen == null; x++)
        {
            CoursePartDraftVine candidate = CoursePartDraftVine.At(x, GroundHeightAt(x));
            InsertVine(candidate);
            bool fits = true;
            foreach ((CoursePartRule rule, int count) in RuleCounts(ToPart(out string _)))
            {
                if (count > already.GetValueOrDefault(rule))
                {
                    fits = false;
                    break;
                }
            }
            Vines.Remove(candidate);
            if (fits)
                chosen = candidate;
        }

        if (chosen == null)
        {
            int after = Vines.Count > 0
                ? Vines[Vines.Count - 1].PlatformEnd + F32.RoundToInt(F32.Ceil(VineEncounterDefinition.TunedLength))
                : F32.RoundToInt(CoursePartInfo.EntryClearance + F32.Ceil(VineEncounterDefinition.TunedLength));
            chosen = CoursePartDraftVine.At(after, GroundHeightAt(after));
        }

        InsertVine(chosen);
        return chosen;
    }

    /// <summary>How many times <paramref name="part"/> breaks each rule it breaks.</summary>
    static Dictionary<CoursePartRule, int> RuleCounts(CoursePartInfo part)
    {
        Dictionary<CoursePartRule, int> counts = new Dictionary<CoursePartRule, int>();
        foreach (CoursePartProblem problem in part.Problems())
            counts[problem.Rule] = counts.GetValueOrDefault(problem.Rule) + 1;
        return counts;
    }

    /// <summary>
    /// A new vine hung at <paramref name="anchorX"/> whole metres, the default anchor height and platform
    /// above the ground there, in order among the others: the overlay's Place tool.
    /// </summary>
    public CoursePartDraftVine PlaceVine(int anchorX)
    {
        CoursePartDraftVine vine = CoursePartDraftVine.At(anchorX, GroundHeightAt(anchorX));
        InsertVine(vine);
        return vine;
    }

    /// <summary>The height of the ground row that covers <paramref name="x"/> whole metres, decimetres; 0 over a gap.</summary>
    public int GroundHeightAt(int x)
    {
        foreach (CoursePartDraftRow row in Ground)
        {
            if (x >= row.Start && x < row.End)
                return row.Height;
        }
        return 0;
    }

    /// <summary>Put <paramref name="vine"/> in anchor order among the draft's vines.</summary>
    public void InsertVine(CoursePartDraftVine vine)
    {
        int at = Vines.FindIndex(existing => existing.AnchorX > vine.AnchorX);
        Vines.Insert(at < 0 ? Vines.Count : at, vine);
    }

    static void CopyDecorations(CoursePartInfo part, List<CoursePartDraftDecoration> into)
    {
        foreach (CoursePartDecoration decoration in part.Decorations ?? new List<CoursePartDecoration>())
        {
            if (decoration == null)
                continue;
            into.Add(new CoursePartDraftDecoration
            {
                Kind  = decoration.Kind ?? "",
                X     = F32.RoundToInt(decoration.X * F32.FromInt(10)),
                Front = decoration.Layer == CourseDecorationLayer.Front,
                Flip  = decoration.Flip,
            });
        }
    }

    /// <summary>
    /// Open <paramref name="amount"/> whole metres of space at <paramref name="x"/> (<paramref name="add"/>),
    /// or take the span from <paramref name="x"/> out; everything past it moves with it. The rows are replaced
    /// only when <see cref="CoursePartSpace"/> accepts the change, and <paramref name="refusal"/> says why when
    /// it does not. The id, positions and notes are untouched.
    /// </summary>
    public bool TryShiftSpace(bool add, int x, int amount, out string refusal)
    {
        CoursePartInfo part = ToPart(out string _);
        F32            at   = F32.FromInt(x);
        F32            by   = F32.FromInt(amount);
        bool accepted = add
            ? CoursePartSpace.TryAdd(part, at, by, out CoursePartInfo shifted, out refusal)
            : CoursePartSpace.TryRemove(part, at, by, out shifted, out refusal);
        if (!accepted)
            return false;

        Ground.Clear();
        foreach (GroundSegment segment in shifted.Ground)
            Ground.Add(new CoursePartDraftRow { Start = F32.RoundToInt(segment.StartX), End = F32.RoundToInt(segment.EndX), Height = F32.RoundToInt(segment.TopY * F32.FromInt(10)), IsPit = segment.IsPit });
        Spikes.Clear();
        foreach (HazardSpan hazard in shifted.Hazards)
            Spikes.Add(new CoursePartDraftRow { Start = F32.RoundToInt(hazard.StartX), End = F32.RoundToInt(hazard.EndX) });
        Bridges.Clear();
        foreach (CoursePartBridge bridge in shifted.Bridges)
            Bridges.Add(new CoursePartDraftRow { Start = F32.RoundToInt(bridge.StartX), End = F32.RoundToInt(bridge.EndX) });
        Decorations.Clear();
        CopyDecorations(shifted, Decorations);
        Vines.Clear();
        CopyVines(shifted, Vines);
        return true;
    }

    /// <summary>
    /// The part this draft describes. An id the config's id type refuses leaves the part without one, which
    /// <see cref="CoursePartInfo.Validate"/> reports, and says why in <paramref name="idProblem"/>.
    /// </summary>
    public CoursePartInfo ToPart(out string idProblem)
    {
        idProblem = null;
        CoursePartId id = null;
        if (!string.IsNullOrWhiteSpace(Id))
        {
            try
            {
                id = CoursePartId.FromString(Id.Trim());
            }
            catch (System.ArgumentException refused)
            {
                idProblem = $"The id '{Id}' is not one a part can carry: {refused.Message}";
            }
        }

        List<GroundSegment> ground = new List<GroundSegment>();
        foreach (CoursePartDraftRow row in Ground)
            ground.Add(new GroundSegment(F32.FromInt(row.Start), F32.FromInt(row.End), F32.Ratio10(row.Height), row.IsPit));

        List<HazardSpan> hazards = new List<HazardSpan>();
        foreach (CoursePartDraftRow row in Spikes)
            hazards.Add(new HazardSpan(F32.FromInt(row.Start), F32.FromInt(row.End)));

        List<CoursePartBridge> bridges = new List<CoursePartBridge>();
        foreach (CoursePartDraftRow row in Bridges)
            bridges.Add(new CoursePartBridge(F32.FromInt(row.Start), F32.FromInt(row.End)));

        List<CoursePartDecoration> decorations = new List<CoursePartDecoration>();
        foreach (CoursePartDraftDecoration decoration in Decorations)
            decorations.Add(new CoursePartDecoration(decoration.Kind ?? "", F32.Ratio10(decoration.X), decoration.Layer, decoration.Flip));

        List<CoursePartVine> vines = new List<CoursePartVine>();
        foreach (CoursePartDraftVine vine in Vines)
            vines.Add(vine.ToVine());

        List<CoursePartPosition> positions = new List<CoursePartPosition>();
        if (First)
            positions.Add(CoursePartPosition.First);
        if (Middle)
            positions.Add(CoursePartPosition.Middle);
        if (Last)
            positions.Add(CoursePartPosition.Last);

        return new CoursePartInfo(id, ground, hazards, positions, IsSetPiece, bridges, decorations, vines);
    }
}
