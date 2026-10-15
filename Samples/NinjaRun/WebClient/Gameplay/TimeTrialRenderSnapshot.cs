using Game.Logic;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace WebClient.Gameplay;

/// <summary>
/// What the runner looks like at one instant, handed to the canvas renderer each animation frame. It is a
/// plain data object because it crosses into JavaScript as JSON, written by <see cref="TimeTrialRenderJsonContext"/>
/// with the property names in camelCase (<c>elapsedMs</c>, <c>encounterPhase</c>, …).
/// <para>
/// The simulation is fixed-point throughout, and its values are converted to floating point only to be drawn.
/// </para>
/// <para>
/// The properties marked as diagnostics are read only by the end-to-end tests, never by the renderer or the
/// sound. They are filled only for a frame that asks for them (one on a page the tests opened) and are
/// otherwise null and left out of the serialized frame, so a player's frame neither builds nor sends them.
/// </para>
/// </summary>
public sealed class TimeTrialRenderSnapshot
{
    /// <summary>Steps simulated so far.</summary>
    public int Step { get; }

    /// <summary>Time on the runner's clock, in milliseconds.</summary>
    public int ElapsedMs { get; }

    /// <summary>The runner's feet, in meters along the course.</summary>
    public float X { get; }

    /// <summary>The runner's feet, in meters above the course baseline.</summary>
    public float Y { get; }

    public float Vx { get; }
    public float Vy { get; }

    /// <summary>What the runner is doing, as the name of a <see cref="TimeTrialMode"/> value.</summary>
    public string Mode { get; }

    public int ActiveEncounterIndex { get; }
    public string EncounterPhase { get; }
    public float EncounterProgress { get; }

    public float VineVisualAngle { get; }
    public string VineVisualState { get; }

    /// <summary>How far the jump is charged, 0 to 1.</summary>
    public float Charge { get; }

    /// <summary>True once the finish line has been crossed.</summary>
    public bool Finished { get; }

    /// <summary>Diagnostics: the time the run's own simulation computed, or null until it finishes.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? FinishTimeMs { get; }

    /// <summary>True while the button does nothing: during the run-in, and while a climb owns movement.</summary>
    public bool InputLocked { get; }

    /// <summary>
    /// True while the runner is running in to the start line, before the clock starts. The runner is drawn
    /// behind the line then, by the distance its step-0 pace covers in the time the run-in has left, and the
    /// field is hidden.
    /// </summary>
    public bool RunIn { get; }

    /// <summary>The gate (a climb's top torii) the runner is racing towards, or one past the last one.</summary>
    public int NextGateIndex { get; }

    /// <summary>
    /// The bottom torii the runner is racing towards, or one past the last one. It is the line a segment is
    /// decided at, so it is also how many segments have been placed.
    /// </summary>
    public int NextClimbIndex { get; }

    /// <summary>
    /// The opponents running alongside, in lineup order (fastest expected finish first). Empty when the run
    /// has none, as on a practice run raced against nobody.
    /// </summary>
    public List<GhostRenderSnapshot> Ghosts { get; }

    /// <param name="ghostHideReasons">
    /// Why each opponent in <paramref name="ghosts"/> is off screen, or that it is not, in the same order. An
    /// opponent with no entry here is drawn.
    /// </param>
    /// <param name="runOut">
    /// Where the runner is drawn past the finish line, once <paramref name="simulation"/> has finished; null
    /// while it is still running. It stands in for the simulation's own position, velocity and mode, which
    /// stop at the crossing, so the runner is seen running on rather than standing on the line. Only the
    /// player has one: an opponent is hidden the moment its own simulation crosses the line.
    /// </param>
    /// <param name="runInSeconds">
    /// How long the run-in before the start line has left, in seconds, and 0 once the run is on. While it is
    /// positive, the runner is drawn that much running time short of the start line.
    /// </param>
    /// <param name="diagnostics">Whether to fill the diagnostics properties, which only the end-to-end tests read.</param>
    public TimeTrialRenderSnapshot(TimeTrialSimulation simulation, IReadOnlyList<TimeTrialSimulation> ghosts = null, IReadOnlyList<GhostHideReason> ghostHideReasons = null, TimeTrialRunOutPose? runOut = null, float runInSeconds = 0f, bool diagnostics = false)
    {
        Step         = simulation.StepIndex;
        ElapsedMs    = simulation.ElapsedMs;
        if (runOut is TimeTrialRunOutPose pose)
        {
            X    = pose.X;
            Y    = pose.Y;
            Vx   = pose.Vx;
            Vy   = pose.Vy;
            Mode = pose.Mode;
        }
        else
        {
            X    = simulation.X.Float;
            Y    = simulation.Y.Float;
            Vx   = simulation.VelocityX.Float;
            Vy   = simulation.VelocityY.Float;
            Mode = simulation.Mode.ToString();
        }

        RunIn = runOut == null && runInSeconds > 0f;
        if (RunIn)
            X -= Vx * runInSeconds;

        ActiveEncounterIndex = simulation.ActiveEncounterIndex;
        EncounterPhase       = simulation.EncounterPhase;
        EncounterProgress    = simulation.EncounterProgress.Float;
        VineVisualAngle      = simulation.VineVisualAngle.Float;
        VineVisualState      = simulation.VineVisualState;
        Charge       = simulation.ChargeFraction.Float;
        Finished     = simulation.IsFinished;
        InputLocked  = RunIn || simulation.IsInputLocked;
        NextGateIndex  = simulation.NextGateIndex;
        NextClimbIndex = simulation.NextClimbIndex;

        if (diagnostics)
            FinishTimeMs = simulation.Result?.FinishTimeMs;

        Ghosts = new List<GhostRenderSnapshot>();
        if (ghosts == null)
            return;

        for (int ndx = 0; ndx < ghosts.Count; ndx++)
        {
            GhostHideReason hideReason = ghostHideReasons != null && ndx < ghostHideReasons.Count ? ghostHideReasons[ndx] : GhostHideReason.None;
            Ghosts.Add(new GhostRenderSnapshot(ghosts[ndx], hideReason));
        }
    }
}

/// <summary>
/// The generated JSON serializer for <see cref="TimeTrialRenderSnapshot"/>, which writes a frame without
/// reflection.
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(TimeTrialRenderSnapshot))]
public sealed partial class TimeTrialRenderJsonContext : JsonSerializerContext
{
}

/// <summary>
/// Why an opponent is off screen. The field is on the course only between the torii at its two ends, so each
/// reason is a torii or the stretch before one: the run-in before the start torii, the bottom torii of the
/// opponent's own climb (until the player's regroup at the gate above it), and the finish line.
/// </summary>
public enum GhostHideReason
{
    /// <summary>On screen.</summary>
    None,

    /// <summary>The run-in has not ended: the field arrives under the start torii on step 0.</summary>
    RunIn,

    /// <summary>Its own simulation reached the bottom torii of a climb; it comes back at the player's regroup.</summary>
    Climb,

    /// <summary>Its own simulation crossed the finish line.</summary>
    Finish,
}

/// <summary>
/// One opponent at the same instant. It carries only what the renderer draws: where the runner is, what it
/// is doing, and the rope pose when it is on a vine, because a ghost is presentation: it has no charge
/// meter, no result and no clock of its own on screen.
/// </summary>
public sealed class GhostRenderSnapshot
{
    public float X { get; }
    public float Y { get; }
    public float Vx { get; }
    public float Vy { get; }
    public string Mode { get; }
    public int ActiveEncounterIndex { get; }
    public string EncounterPhase { get; }
    public float EncounterProgress { get; }
    public bool VineAttached { get; }
    public float VineGripLength { get; }
    public float VineGripX { get; }
    public float VineGripY { get; }

    public bool Finished { get; }

    /// <summary>
    /// Whether this opponent is off screen for the moment, for the reason <see cref="HideReason"/> names. The
    /// renderer drops a hidden opponent entirely.
    /// </summary>
    public bool Hidden { get; }

    /// <summary>
    /// Why the opponent is hidden, as a <see cref="GhostHideReason"/> name in camelCase (<c>runIn</c>,
    /// <c>climb</c> or <c>finish</c>) or empty while it is drawn. The renderer plays a poof for an opponent
    /// that a climb's bottom torii or the finish line takes off the course, and none for the run-in.
    /// </summary>
    public string HideReason { get; }

    public GhostRenderSnapshot(TimeTrialSimulation simulation, GhostHideReason hideReason)
    {
        X    = simulation.X.Float;
        Y    = simulation.Y.Float;
        Vx   = simulation.VelocityX.Float;
        Vy   = simulation.VelocityY.Float;
        Mode = simulation.Mode.ToString();
        ActiveEncounterIndex = simulation.ActiveEncounterIndex;
        EncounterPhase       = simulation.EncounterPhase;
        EncounterProgress    = simulation.EncounterProgress.Float;
        VineAttached         = simulation.IsAttachedToVine;
        VineGripLength       = simulation.VineGripLength.Float;
        VineGripX            = simulation.VineGripX.Float;
        VineGripY            = simulation.VineGripY.Float;
        Finished             = simulation.IsFinished;
        Hidden               = hideReason != GhostHideReason.None;
        HideReason           = hideReason switch
        {
            GhostHideReason.RunIn  => "runIn",
            GhostHideReason.Climb  => "climb",
            GhostHideReason.Finish => "finish",
            _                      => "",
        };
    }
}

/// <summary>
/// Where the player is drawn once past the finish line, where the simulation stops. It is drawing only.
/// </summary>
public readonly struct TimeTrialRunOutPose
{
    /// <summary>
    /// How far past the finish line the course view draws the ground for the run-out.
    /// </summary>
    public const float GroundMetres = 60f;

    public float  X    { get; }
    public float  Y    { get; }
    public float  Vx   { get; }
    public float  Vy   { get; }
    public string Mode { get; }

    TimeTrialRunOutPose(float x, float y, float vx, float vy, string mode)
    {
        X    = x;
        Y    = y;
        Vx   = vx;
        Vy   = vy;
        Mode = mode;
    }

    /// <summary>
    /// The pose <paramref name="stepsSinceFinish"/> simulation steps after <paramref name="finished"/>
    /// crossed the finish line of <paramref name="course"/>, running on at <paramref name="rules"/>' run speed
    /// from where it crossed, and coming down onto the ground under the line first if it crossed in the air.
    /// </summary>
    public static TimeTrialRunOutPose After(TimeTrialSimulation finished, CourseDefinition course, TimeTrialRules rules, int stepsSinceFinish)
    {
        float seconds = stepsSinceFinish / (float)TimeTrialConstants.StepsPerSecond;
        float speed   = rules.RunSpeed.Float;
        float x       = finished.X.Float + speed * seconds;
        float y       = finished.Y.Float;
        float vy      = 0f;
        string mode   = TimeTrialMode.Running.ToString();

        int   groundIndex = course.FindGroundIndexAt(course.FinishX);
        float groundY     = groundIndex >= 0 ? course.Ground[groundIndex].TopY.Float : y;
        if (y > groundY)
        {
            // Crossed in the air: the simulation keeps no velocity past the line, so the fall starts level.
            float gravity = rules.Gravity.Float;
            float fallen  = y - 0.5f * gravity * seconds * seconds;
            if (fallen > groundY)
            {
                y    = fallen;
                vy   = -gravity * seconds;
                mode = TimeTrialMode.Airborne.ToString();
            }
            else
            {
                y = groundY;
            }
        }

        return new TimeTrialRunOutPose(x, y, speed, vy, mode);
    }
}
