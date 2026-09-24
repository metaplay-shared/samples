namespace WebClient.Meta;

/// <summary>
/// Sorts the feature cards on a hub: actionable features first, then by time remaining, then by the
/// <see cref="MetaFeature"/> enum order.
/// <para>
/// Features that are not actionable stay in the list so the player can still find them. The last sort key is
/// fixed so that two equally urgent features do not swap places between renders.
/// </para>
/// </summary>
public static class FeatureOrdering
{
    public static IReadOnlyList<IFeatureView> Sort(IEnumerable<IFeatureView> features) => features
        .OrderByDescending(v => v.State.IsActionable())
        .ThenBy(v => UrgencyKey(v))
        .ThenBy(v => (int)v.Feature)
        .ToList();

    /// <summary>
    /// The time remaining, or <see cref="TimeSpan.MaxValue"/> for a feature without a positive time remaining, so
    /// those sort after every feature with a clock.
    /// </summary>
    private static TimeSpan UrgencyKey(IFeatureView view) =>
        view.UntilDeadline is TimeSpan t && t > TimeSpan.Zero ? t : TimeSpan.MaxValue;
}
