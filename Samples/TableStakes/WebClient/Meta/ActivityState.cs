namespace WebClient.Meta;

/// <summary>
/// The display state of a meta surface, such as a hub card or feature screen. Every surface renders each of
/// these states explicitly.
/// <para>
/// <see cref="Ready"/> also covers "non-actionable": the surface has loaded and the player has nothing to do
/// right now.
/// </para>
/// </summary>
public enum ActivityState
{
    /// <summary>Data has not arrived. Renders a skeleton that reserves the final layout's space.</summary>
    Loading,

    /// <summary>Loaded, with nothing for the player to do right now. The surface must say why.</summary>
    Ready,

    /// <summary>Something can be claimed, spun, bought or started right now. Only this state shows a badge.</summary>
    Actionable,

    /// <summary>
    /// Started and not finished, for example a partly done mission or an event in the middle of a phase.
    /// </summary>
    InProgress,

    /// <summary>
    /// Finished and collected. Unlike <see cref="Ready"/>, the surface shows that the player completed it.
    /// </summary>
    Completed,

    /// <summary>Nothing to show, or not offered to this player. The surface shows an explicit empty state.</summary>
    Unavailable,

    /// <summary>The time window has closed. The surface shows an end state instead of a negative countdown.</summary>
    Expired,

    /// <summary>The connection is down. The surface shows possibly stale data and says that it may be stale.</summary>
    Offline,

    /// <summary>Loading failed. The surface offers a retry.</summary>
    Error,
}

/// <summary>Predicates shared by the hub sort, the badge rules and the next-action policy.</summary>
public static class ActivityStates
{
    /// <summary>Whether the player can do something here right now.</summary>
    public static bool IsActionable(this ActivityState state) => state == ActivityState.Actionable;

    /// <summary>
    /// Whether the surface has loaded real state. Returns false for <see cref="ActivityState.Loading"/>,
    /// <see cref="ActivityState.Offline"/> and <see cref="ActivityState.Error"/>, so that a card showing placeholder
    /// defaults is not sorted, badged or offered as a next action.
    /// </summary>
    public static bool IsLive(this ActivityState state) =>
        state != ActivityState.Loading && state != ActivityState.Offline && state != ActivityState.Error;
}
