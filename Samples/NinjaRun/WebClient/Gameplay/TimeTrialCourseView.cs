using Game.Logic;
using System.Collections.Generic;

namespace WebClient.Gameplay;

/// <summary>
/// The course geometry in the form the canvas renderer wants it: plain numbers in meters, handed over once
/// when a run starts rather than with every frame. Blazor's JS interop serializes the property names to
/// camelCase (<c>startX</c>, <c>topY</c>, …).
/// </summary>
public sealed class TimeTrialCourseView
{
    /// <summary>The course's id, so the renderer can name what it is drawing in logs and test harnesses.</summary>
    public string Id { get; }

    public float StartX { get; }
    public float FinishX { get; }

    /// <summary>
    /// How far behind the start line the ground under it is drawn, at the least. The run-in before the clock
    /// starts is run over it, with the camera following, so it has to reach well past where the run-in begins
    /// and the stretch of view behind the runner.
    /// </summary>
    public const float RunInGroundMetres = 30f;

    /// <summary>
    /// The course's ground, with the stretch under the start line carried back for the run-in the player
    /// makes before the clock starts, and the stretch under the finish line carried on past its end for the
    /// run-out the player makes after crossing the line.
    /// </summary>
    public IReadOnlyList<TimeTrialGroundView> Ground { get; }

    /// <summary>
    /// The pits, in the order the runner meets them. Each one's floor is an ordinary segment of
    /// <see cref="Ground"/> and its far wall an ordinary ledge grab in <see cref="Encounters"/>, so these are
    /// what the renderer draws a shaft from rather than anything it works out from the gaps in the ground.
    /// </summary>
    public IReadOnlyList<TimeTrialPitView> Pits { get; }

    public IReadOnlyList<TimeTrialHazardView> Hazards { get; }
    public IReadOnlyList<TimeTrialCourseFeatureView> Encounters { get; }
    public IReadOnlyList<TimeTrialDroopingBridgeView> DroopingBridges { get; }

    /// <summary>The cosmetic props standing on the ground, each drawn behind the field or in front of it.</summary>
    public IReadOnlyList<TimeTrialDecorationView> Decorations { get; }

    /// <summary>The climbs, each with the slope to draw between its two torii and the gate at its far end.</summary>
    public IReadOnlyList<TimeTrialClimbView> Climbs { get; }

    public TimeTrialPlayerBodyView PlayerBody { get; }

    /// <summary>
    /// The player's effective rules (the ticket's Skill applied) as far as drawing the runner needs them, or
    /// null for a course drawn with no run on it. Without them the renderer plays no high-landing roll.
    /// </summary>
    public TimeTrialRunnerRulesView RunnerRules { get; private init; }

    /// <summary>The same for each opponent, by lineup position: each one's own Skill applied.</summary>
    public IReadOnlyList<TimeTrialRunnerRulesView> GhostRunnerRules { get; private init; } = new List<TimeTrialRunnerRulesView>();

    /// <summary>
    /// The altitude band the course starts in (<see cref="TimeTrialAltitudeBands"/>), or null for the foothills.
    /// Named only for a part previewed or played alone from the part editor, whose theme stands in for the
    /// climbs a map would carry it up by; a map always starts in the foothills.
    /// </summary>
    public string AltitudeBand { get; private init; }

    TimeTrialCourseView(string id, float startX, float finishX, IReadOnlyList<TimeTrialGroundView> ground, IReadOnlyList<TimeTrialPitView> pits, IReadOnlyList<TimeTrialHazardView> hazards, IReadOnlyList<TimeTrialCourseFeatureView> encounters, IReadOnlyList<TimeTrialDroopingBridgeView> droopingBridges, IReadOnlyList<TimeTrialDecorationView> decorations, IReadOnlyList<TimeTrialClimbView> climbs, TimeTrialPlayerBodyView playerBody)
    {
        Id      = id;
        StartX  = startX;
        FinishX = finishX;
        Ground  = ground;
        Pits    = pits;
        Hazards = hazards;
        Encounters = encounters;
        DroopingBridges = droopingBridges;
        Decorations = decorations;
        Climbs = climbs;
        PlayerBody = playerBody;
    }

    /// <param name="runnerRules">The rules the player's run is simulated by, or null for a course with no run on it.</param>
    /// <param name="ghostRunnerRules">The rules each opponent's run is simulated by, in lineup order.</param>
    /// <param name="altitudeBand">The band the course starts in, or null for the foothills; see <see cref="AltitudeBand"/>.</param>
    public static TimeTrialCourseView FromCourse(CourseDefinition course, TimeTrialRules runnerRules = null, IReadOnlyList<TimeTrialRules> ghostRunnerRules = null, string altitudeBand = null)
    {
        // The ground under the finish line is drawn on well past the course's own end of it, when nothing
        // follows it: the player keeps running after the line, and the run-out needs ground under it and
        // for the camera to follow. The ground under the start line is carried back the same way, when nothing
        // comes before it, for the run-in the player makes before the clock starts. Both are drawing only;
        // the course the run is simulated on is unchanged.
        int   startGroundIndex  = course.FindGroundIndexAt(course.StartX);
        float runInStartX       = course.StartX.Float - RunInGroundMetres;
        int   finishGroundIndex = course.FindGroundIndexAt(course.FinishX);
        float runOutEndX        = course.FinishX.Float + TimeTrialRunOutPose.GroundMetres;
        List<TimeTrialGroundView> ground = new List<TimeTrialGroundView>();
        for (int ndx = 0; ndx < course.Ground.Count; ndx++)
        {
            GroundSegment segment = course.Ground[ndx];
            float         startX  = segment.StartX.Float;
            float         endX    = segment.EndX.Float;
            if (ndx == startGroundIndex && IsFirstGround(course, segment))
                startX = Math.Min(startX, runInStartX);
            if (ndx == finishGroundIndex && IsLastGround(course, segment))
                endX = Math.Max(endX, runOutEndX);
            ground.Add(new TimeTrialGroundView(startX, endX, segment.TopY.Float));
        }

        List<TimeTrialPitView> pits = new List<TimeTrialPitView>();
        foreach (PitDefinition pit in course.Pits)
            pits.Add(new TimeTrialPitView(pit.StartX.Float, pit.EndX.Float, pit.FloorY.Float, pit.Depth.Float));

        List<TimeTrialHazardView> hazards = new List<TimeTrialHazardView>();
        foreach (HazardSpan hazard in course.Hazards)
            hazards.Add(new TimeTrialHazardView(hazard.StartX.Float, hazard.EndX.Float));

        List<TimeTrialCourseFeatureView> encounters = new List<TimeTrialCourseFeatureView>();
        for (int ndx = 0; ndx < course.Encounters.Count; ndx++)
        {
            CourseEncounterDefinition encounter = course.Encounters[ndx];
            if (encounter is VineEncounterDefinition vine)
            {
                encounters.Add(new TimeTrialCourseFeatureView(
                    ndx,
                    encounter.Id,
                    encounter.Kind,
                    vine.AnchorX.Float,
                    vine.AnchorY.Float,
                    vine.Length.Float,
                    vine.RestAngle.Float,
                    VineEncounterDefinition.RunnerGripHeight.Float,
                    vine.PlatformStartX.Float,
                    vine.PlatformEndX.Float,
                    vine.PlatformTopY.Float));
            }
            else if (encounter is LedgeGrabDefinition ledge)
            {
                encounters.Add(new TimeTrialCourseFeatureView(
                    ndx,
                    encounter.Id,
                    encounter.Kind,
                    x:            ledge.X.Float,
                    bottomY:      ledge.BottomY.Float,
                    topY:         ledge.TopY.Float,
                    minimumGrabY: ledge.MinimumGrabY.Float,
                    landingX:     ledge.LandingX.Float));
            }
            else
            {
                encounters.Add(new TimeTrialCourseFeatureView(ndx, encounter.Id, encounter.Kind));
            }
        }

        List<TimeTrialDroopingBridgeView> droopingBridges = new List<TimeTrialDroopingBridgeView>();
        foreach (DroopingBridgePresentationFeature bridge in course.DroopingBridges)
        {
            int groundIndex = course.FindGroundIndexAt(bridge.StartX);
            float collisionY = groundIndex >= 0 ? course.Ground[groundIndex].TopY.Float : 0;
            droopingBridges.Add(new TimeTrialDroopingBridgeView(
                bridge.Id,
                bridge.StartX.Float,
                bridge.EndX.Float,
                collisionY,
                bridge.RestSag.Float,
                bridge.MaxLocalDroop.Float,
                bridge.ResponseRadius.Float,
                bridge.LogCount,
                bridge.HasWaterfall));
        }

        List<TimeTrialDecorationView> decorations = new List<TimeTrialDecorationView>();
        foreach (CourseDecoration decoration in course.Decorations)
        {
            decorations.Add(new TimeTrialDecorationView(
                decoration.Id,
                decoration.Kind,
                decoration.X.Float,
                decoration.Y.Float,
                decoration.Layer == CourseDecorationLayer.Front ? TimeTrialDecorationView.FrontLayer : TimeTrialDecorationView.BehindLayer,
                decoration.Flip));
        }

        // The gap in the ground a climb owns is its slope rather than a pit, so the renderer gets the bottom
        // torii, the slope's two ends and the run-out to the gate and draws them itself.
        List<TimeTrialClimbView> climbs = new List<TimeTrialClimbView>();
        foreach (ClimbDefinition climb in course.Climbs)
        {
            GroundSegment below = course.FindGroundSegmentEndingAt(climb.SlopeStartX);
            climbs.Add(new TimeTrialClimbView(
                climb.Id,
                climb.BottomX.Float,
                below != null ? below.TopY.Float : climb.TopY.Float,
                climb.SlopeStartX.Float,
                climb.TopX.Float,
                climb.TopY.Float,
                climb.GateX.Float));
        }

        string id = course.Id.Value;
        TimeTrialPlayerBodyView playerBody = new TimeTrialPlayerBodyView(
            TimeTrialPlayerBody.LeftExtent.Float,
            TimeTrialPlayerBody.RightExtent.Float,
            TimeTrialPlayerBody.Height.Float);
        List<TimeTrialRunnerRulesView> ghostRules = new List<TimeTrialRunnerRulesView>();
        if (ghostRunnerRules != null)
        {
            foreach (TimeTrialRules rules in ghostRunnerRules)
                ghostRules.Add(rules != null ? new TimeTrialRunnerRulesView(rules) : null);
        }

        return new TimeTrialCourseView(id, course.StartX.Float, course.FinishX.Float, ground, pits, hazards, encounters, droopingBridges, decorations, climbs, playerBody)
        {
            RunnerRules      = runnerRules != null ? new TimeTrialRunnerRulesView(runnerRules) : null,
            GhostRunnerRules = ghostRules,
            AltitudeBand     = altitudeBand,
        };
    }

    /// <summary>Whether no ground on <paramref name="course"/> ends at or before the start of <paramref name="segment"/>.</summary>
    static bool IsFirstGround(CourseDefinition course, GroundSegment segment)
    {
        foreach (GroundSegment other in course.Ground)
        {
            if (other != segment && other.EndX <= segment.StartX)
                return false;
        }
        return true;
    }

    /// <summary>Whether no ground on <paramref name="course"/> begins at or after the end of <paramref name="segment"/>.</summary>
    static bool IsLastGround(CourseDefinition course, GroundSegment segment)
    {
        foreach (GroundSegment other in course.Ground)
        {
            if (other != segment && other.StartX >= segment.EndX)
                return false;
        }
        return true;
    }
}

/// <summary>
/// A climb: the bottom torii on the flat before the slope, the slope from its foot up to the top of the ground
/// above it, and the flat run-out from there out to the top torii, which is the segment gate.
/// </summary>
public sealed class TimeTrialClimbView
{
    public string Id { get; }
    /// <summary>The bottom torii, on the flat ground below the slope.</summary>
    public float BottomX { get; }
    /// <summary>The height of the flat the bottom torii and the foot of the slope stand on.</summary>
    public float BottomY { get; }
    /// <summary>The foot of the slope, where the ground below ends.</summary>
    public float SlopeStartX { get; }
    public float TopX { get; }
    public float TopY { get; }
    public float GateX { get; }

    public TimeTrialClimbView(string id, float bottomX, float bottomY, float slopeStartX, float topX, float topY, float gateX)
    {
        Id          = id;
        BottomX     = bottomX;
        BottomY     = bottomY;
        SlopeStartX = slopeStartX;
        TopX        = topX;
        TopY        = topY;
        GateX       = gateX;
    }
}

/// <summary>A presentation-only log bridge over an unchanged collision surface.</summary>
public sealed class TimeTrialDroopingBridgeView
{
    public string Id { get; }
    public float StartX { get; }
    public float EndX { get; }
    public float CollisionY { get; }
    public float RestSag { get; }
    public float MaxLocalDroop { get; }
    public float ResponseRadius { get; }
    public int LogCount { get; }
    public bool HasWaterfall { get; }

    public TimeTrialDroopingBridgeView(string id, float startX, float endX, float collisionY, float restSag, float maxLocalDroop, float responseRadius, int logCount, bool hasWaterfall)
    {
        Id = id;
        StartX = startX;
        EndX = endX;
        CollisionY = collisionY;
        RestSag = restSag;
        MaxLocalDroop = maxLocalDroop;
        ResponseRadius = responseRadius;
        LogCount = logCount;
        HasWaterfall = hasWaterfall;
    }
}

/// <summary>
/// A cosmetic prop to draw: its kind, a key into the renderer's decoration catalogue
/// (<c>wwwroot/js/courseDecorations.js</c>), the ground point it stands on, and the layer it is drawn in.
/// </summary>
public sealed class TimeTrialDecorationView
{
    public const string BehindLayer = "behind";
    public const string FrontLayer  = "front";

    public string Id { get; }
    public string Kind { get; }
    public float X { get; }
    public float Y { get; }

    /// <summary><see cref="BehindLayer"/> or <see cref="FrontLayer"/>.</summary>
    public string Layer { get; }
    public bool Flip { get; }

    public TimeTrialDecorationView(string id, string kind, float x, float y, string layer, bool flip)
    {
        Id    = id;
        Kind  = kind;
        X     = x;
        Y     = y;
        Layer = layer;
        Flip  = flip;
    }
}

/// <summary>
/// The values of one runner's rules that drawing it reads: how far above a lip a mantle's rise lifts the feet,
/// where the climb cycle ends, and how fast the rise climbs and how long the top transition lasts, which pace the
/// pull-over. Presentation only; the simulation reads its rules itself.
/// </summary>
public sealed class TimeTrialRunnerRulesView
{
    public float LedgeGrabClearanceHeight { get; }
    public float LedgeGrabRiseSpeed { get; }
    public float LedgeGrabTopTransitionSeconds { get; }

    public TimeTrialRunnerRulesView(TimeTrialRules rules)
    {
        LedgeGrabClearanceHeight      = rules.LedgeGrabClearanceHeight.Float;
        LedgeGrabRiseSpeed            = rules.LedgeGrabRiseSpeed.Float;
        LedgeGrabTopTransitionSeconds = (float)rules.LedgeGrabTopTransitionSteps / TimeTrialConstants.StepsPerSecond;
    }
}

/// <summary>The deterministic runner contact shape projected without reinterpretation for drawing.</summary>
public sealed class TimeTrialPlayerBodyView
{
    public float LeftExtent { get; }
    public float RightExtent { get; }
    public float Height { get; }

    public TimeTrialPlayerBodyView(float leftExtent, float rightExtent, float height)
    {
        LeftExtent = leftExtent;
        RightExtent = rightExtent;
        Height = height;
    }
}

/// <summary>The stable identity shared by every renderer-facing course feature.</summary>
public sealed class TimeTrialCourseFeatureView
{
    public int Index { get; }
    public string Id { get; }
    public string Kind { get; }
    public float X { get; }
    public float Y { get; }
    public float Length { get; }
    public float RestAngle { get; }
    public float GripHeight { get; }
    public float PlatformStartX { get; }
    public float PlatformEndX { get; }
    public float PlatformTopY { get; }
    public float BottomY { get; }
    public float TopY { get; }
    public float MinimumGrabY { get; }
    public float LandingX { get; }

    public TimeTrialCourseFeatureView(
        int index,
        string id,
        string kind,
        float x = 0,
        float y = 0,
        float length = 0,
        float restAngle = 0,
        float gripHeight = 0,
        float platformStartX = 0,
        float platformEndX = 0,
        float platformTopY = 0,
        float bottomY = 0,
        float topY = 0,
        float minimumGrabY = 0,
        float landingX = 0)
    {
        Index         = index;
        Id            = id;
        Kind          = kind;
        X             = x;
        Y             = y;
        Length            = length;
        RestAngle         = restAngle;
        GripHeight        = gripHeight;
        PlatformStartX    = platformStartX;
        PlatformEndX      = platformEndX;
        PlatformTopY      = platformTopY;
        BottomY           = bottomY;
        TopY              = topY;
        MinimumGrabY      = minimumGrabY;
        LandingX          = landingX;
    }
}

/// <summary>
/// A stretch of ground to draw. Every pit's floor is one of these, so the only gaps left between consecutive
/// segments are the ones a climb's slope fills.
/// </summary>
public sealed class TimeTrialGroundView
{
    public float StartX { get; }
    public float EndX { get; }
    public float TopY { get; }

    public TimeTrialGroundView(float startX, float endX, float topY)
    {
        StartX = startX;
        EndX   = endX;
        TopY   = topY;
    }
}

/// <summary>
/// One pit: the shaft between the ground that ends at <see cref="StartX"/> and the ground that begins at
/// <see cref="EndX"/>. The runner falls in, lands on the floor at <see cref="FloorY"/>, runs the bottom and
/// mantles the far wall, so the renderer draws a floor and two walls rather than an empty gap.
/// </summary>
public sealed class TimeTrialPitView
{
    /// <summary>The near lip: where the ground before the pit ends.</summary>
    public float StartX { get; }

    /// <summary>The far lip: where the ground after the pit begins, and where its climbable wall stands.</summary>
    public float EndX { get; }

    /// <summary>The height of the floor the runner lands on and runs along.</summary>
    public float FloorY { get; }

    /// <summary>How far the floor lies below the lower of the two lips, meters.</summary>
    public float Depth { get; }

    public TimeTrialPitView(float startX, float endX, float floorY, float depth)
    {
        StartX = startX;
        EndX   = endX;
        FloorY = floorY;
        Depth  = depth;
    }
}

/// <summary>A bed of spikes to draw, standing on the ground it lies within.</summary>
public sealed class TimeTrialHazardView
{
    public float StartX { get; }
    public float EndX { get; }

    public TimeTrialHazardView(float startX, float endX)
    {
        StartX = startX;
        EndX   = endX;
    }
}
