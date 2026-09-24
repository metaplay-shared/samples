namespace WebClient.Meta;

/// <summary>
/// The state of one step on a step rail. The daily streak milestones and the first-week event days both render
/// with the same rail so that the two features look consistent.
/// </summary>
public enum RailStepState
{
    /// <summary>Passed and completed. Drawn with a check mark, and the connector leading to it is lit.</summary>
    Done,

    /// <summary>The player's current step. The only step drawn in gold.</summary>
    Current,

    /// <summary>Ahead and reachable. Drawn with its number in a muted style.</summary>
    Future,

    /// <summary>Ahead and not reachable. Drawn with a lock instead of a number.</summary>
    Locked,

    /// <summary>The final step that holds the grand prize, such as the first-week event's last day.</summary>
    Prize,

    /// <summary>
    /// Passed and not completed. Drawn as a struck-through number instead of a check mark, so the rail agrees with
    /// the day tile that shows the day as missed.
    /// </summary>
    Missed,
}

/// <summary>
/// One step on a step rail: its label, its state, optional text inside the node, and an optional value shown
/// under it, such as a streak milestone's bonus.
/// </summary>
public sealed record RailStep(string Label, RailStepState State, string? NodeText = null, string? Caption = null);
