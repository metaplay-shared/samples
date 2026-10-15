using Game.Logic;
using Metaplay.Core.Math;
using System;
using System.Collections.Generic;

namespace WebClient.Gameplay;

/// <summary>
/// One pit in a draft's ground (a gap between its rows, or a row flagged as a pit floor) with its depth and
/// which kind of pit it is: <c>jump</c> for one a full charge clears, <c>run</c> for a long pit, and <c>poor</c> for a long pit so
/// near the jump range that a full charge catches the far face near its top.
/// </summary>
public sealed class CoursePartDraftGap
{
    public int    StartX { get; }
    public int    EndX   { get; }
    public int    Width  { get; }
    public F32    Depth  { get; }
    public string Kind   { get; }
    public string Label  { get; }

    /// <summary>The height of the lower of the pit's two lips, decimetres: the top its depth is measured down from.</summary>
    public int    LipHeight { get; }

    /// <summary>The ground row the pit comes before (a gap between rows) or that floors it (a pit floor).</summary>
    public int    RowIndex  { get; }

    /// <summary>Whether the pit is a ground row flagged as a pit floor rather than a gap between two rows.</summary>
    public bool   IsFloor   { get; }

    public CoursePartDraftGap(int startX, int endX, int width, F32 depth, string kind, string label, int lipHeight, int rowIndex, bool isFloor)
    {
        StartX    = startX;
        EndX      = endX;
        Width     = width;
        Depth     = depth;
        Kind      = kind;
        Label     = label;
        LipHeight = lipHeight;
        RowIndex  = rowIndex;
        IsFloor   = isFloor;
    }
}

/// <summary>
/// Everything the part editor reads off a <see cref="CoursePartDraft"/>: the part it describes, the rules it
/// breaks, the course it composes into alone when it breaks none, its gaps, and its
/// <c>GameConfigSource/CourseParts.json</c> part object.
/// </summary>
public sealed class CoursePartDraftReview
{
    /// <summary>How far past the jump range a gap is still poor either way, meters.</summary>
    public static readonly F32 PoorGapMargin = F32.Ratio10(15);

    public CoursePartInfo        Part      { get; }
    public IReadOnlyList<string> Problems  { get; }

    /// <summary>The draft composed alone, or null when it breaks a rule or cannot be composed.</summary>
    public CourseDefinition      Course    { get; }

    /// <summary>The draft as one <c>CourseParts.json</c> part object.</summary>
    public string                Code      { get; }

    public IReadOnlyList<CoursePartDraftGap> Gaps { get; }

    /// <summary>The widest level gap a full charge clears under the config's rules, meters.</summary>
    public F32                   JumpRange { get; }

    CoursePartDraftReview(CoursePartInfo part, IReadOnlyList<string> problems, CourseDefinition course, string code, IReadOnlyList<CoursePartDraftGap> gaps, F32 jumpRange)
    {
        Part      = part;
        Problems  = problems;
        Course    = course;
        Code      = code;
        Gaps      = gaps;
        JumpRange = jumpRange;
    }

    public static CoursePartDraftReview Of(CoursePartDraft draft, SharedGameConfig config)
    {
        CoursePartInfo part     = draft.ToPart(out string idProblem);
        List<string>   problems = new List<string>();
        if (idProblem != null)
            problems.Add(idProblem);
        problems.AddRange(part.Validate());

        CourseDefinition course = null;
        if (problems.Count == 0)
        {
            try
            {
                course = CourseInspection.PartAlone(part, config.Global);
            }
            catch (Exception composeFailure)
            {
                problems.Add($"The draft cannot be composed alone: {composeFailure.Message}");
            }
        }

        F32    jumpRange = TimeTrialSimulation.MaxJumpRange(config.TimeTrialRules);
        string notes     = string.IsNullOrWhiteSpace(draft.Notes) ? null : draft.Notes.Trim();
        return new CoursePartDraftReview(part, problems, course, CoursePartJson.Write(part, notes), GapsOf(draft.Ground, jumpRange), jumpRange);
    }

    /// <summary>
    /// Every pit the draft's ground opens, in row order: the pits <see cref="CourseDefinition.Pits"/> holds for
    /// the part.
    /// </summary>
    public static List<CoursePartDraftGap> GapsOf(IReadOnlyList<CoursePartDraftRow> ground, F32 jumpRange)
    {
        List<CoursePartDraftGap> gaps = new List<CoursePartDraftGap>();
        for (int ndx = 0; ndx < ground.Count; ndx++)
        {
            CoursePartDraftRow row    = ground[ndx];
            CoursePartDraftRow before = ndx > 0 ? ground[ndx - 1] : null;
            CoursePartDraftRow after  = ndx + 1 < ground.Count ? ground[ndx + 1] : null;

            if (before != null && row.Start > before.End)
            {
                F32 depth = CourseDefinition.PitDepthForGap(F32.FromInt(row.Start - before.End));
                gaps.Add(PitOf(before.End, row.Start, depth, jumpRange, "pit", Math.Min(before.Height, row.Height), ndx, isFloor: false));
            }

            // A flagged row that breaks the pit-floor rule opens no pit. CoursePartInfo.Validate reports it.
            bool floorsPit = row.IsPit
                && before != null && after != null
                && before.End == row.Start && after.Start == row.End && row.End > row.Start
                && before.Height > row.Height && after.Height > row.Height;
            if (floorsPit)
            {
                int lipHeight = Math.Min(before.Height, after.Height);
                gaps.Add(PitOf(row.Start, row.End, F32.Ratio10(lipHeight - row.Height), jumpRange, "pit floor", lipHeight, ndx, isFloor: true));
            }
        }
        return gaps;
    }

    static CoursePartDraftGap PitOf(int startX, int endX, F32 depth, F32 jumpRange, string noun, int lipHeight, int rowIndex, bool isFloor)
    {
        int width  = endX - startX;
        F32 widthF = F32.FromInt(width);
        (string kind, string label) = widthF <= jumpRange
            ? ("jump", $"ordinary {noun}, jumped")
            : widthF <= jumpRange + PoorGapMargin
                ? ("poor", $"long {noun}, poor either way: a full charge catches the far face near its top")
                : ("run", $"long {noun}, run into");
        return new CoursePartDraftGap(startX, endX, width, depth, kind, label, lipHeight, rowIndex, isFloor);
    }
}

/// <summary>
/// One pit as the part editor's canvas overlay draws it: its span in whole metres, its depth and lower lip in
/// metres, its kind, and the ground row it comes before or that floors it.
/// </summary>
public sealed class CoursePartEditorGap
{
    public int    Start { get; }
    public int    End   { get; }
    public int    Width { get; }
    public float  Depth { get; }
    public float  LipY  { get; }

    /// <summary><c>jump</c>, <c>poor</c> or <c>run</c>.</summary>
    public string Kind  { get; }
    public int    Row   { get; }
    public bool   Floor { get; }

    CoursePartEditorGap(CoursePartDraftGap gap)
    {
        Start = gap.StartX;
        End   = gap.EndX;
        Width = gap.Width;
        Depth = gap.Depth.Float;
        LipY  = F32.Ratio10(gap.LipHeight).Float;
        Kind  = gap.Kind;
        Row   = gap.RowIndex;
        Floor = gap.IsFloor;
    }

    public static List<CoursePartEditorGap> Of(CoursePartDraftReview review)
    {
        List<CoursePartEditorGap> gaps = new List<CoursePartEditorGap>(review.Gaps.Count);
        foreach (CoursePartDraftGap gap in review.Gaps)
            gaps.Add(new CoursePartEditorGap(gap));
        return gaps;
    }
}

/// <summary>
/// The draft as the part editor's canvas overlay (<c>wwwroot/js/coursePartEditor.js</c>) takes it: the rows
/// in whole meters and decimeters, the pits the review finds in them, the clearances the overlay labels, and
/// the selection. Blazor's JS interop serializes the property names to camelCase.
/// </summary>
public sealed class CoursePartEditorView
{
    public IReadOnlyList<CoursePartDraftRow> Ground  { get; }
    public IReadOnlyList<CoursePartDraftRow> Spikes  { get; }
    public IReadOnlyList<CoursePartDraftRow> Bridges { get; }

    /// <summary>The draft's decorations: kind, X in whole decimetres, layer and flip.</summary>
    public IReadOnlyList<CoursePartDraftDecoration> Decorations { get; }

    /// <summary>The draft's vines: anchor X in whole metres and height in decimetres, platform span in metres and height in decimetres.</summary>
    public IReadOnlyList<CoursePartDraftVine> Vines { get; }

    /// <summary>The tuned rope's full length and rest angle, so the overlay draws the rope a run hangs.</summary>
    public float VineLength       { get; }
    public float VineRestAngle    { get; }

    /// <summary>Whether the draft breaks no rule, so the course drawn under the overlay is this draft.</summary>
    public bool  Sound            { get; }

    /// <summary>Every pit the draft's ground opens, as <see cref="CoursePartDraftReview.Gaps"/> finds them.</summary>
    public IReadOnlyList<CoursePartEditorGap> Gaps { get; }
    public float EntryClearance   { get; }
    public float ExitClearance    { get; }

    /// <summary><c>ground</c>, <c>spike</c>, <c>bridge</c>, <c>decoration</c>, <c>vine</c>, or empty for no selection.</summary>
    public string SelectedKind    { get; }
    public int    SelectedIndex   { get; }

    /// <summary>Where the add/remove space tool stands, whole metres along the part, and how much it moves.</summary>
    public int    SpaceX          { get; }
    public int    SpaceAmount     { get; }

    public CoursePartEditorView(CoursePartDraft draft, CoursePartDraftReview review, string selectedKind, int selectedIndex, int spaceX = 0, int spaceAmount = 0)
    {
        Ground           = draft.Ground;
        Spikes           = draft.Spikes;
        Bridges          = draft.Bridges;
        Decorations      = draft.Decorations;
        Vines            = draft.Vines;
        VineLength       = VineEncounterDefinition.TunedLength.Float;
        VineRestAngle    = VineEncounterDefinition.TunedRestAngle.Float;
        Sound            = review.Course != null;
        Gaps             = CoursePartEditorGap.Of(review);
        EntryClearance   = CoursePartInfo.EntryClearance.Float;
        ExitClearance    = CoursePartInfo.ExitClearance.Float;
        SelectedKind     = selectedKind ?? "";
        SelectedIndex    = selectedIndex;
        SpaceX           = spaceX;
        SpaceAmount      = spaceAmount;
    }
}
