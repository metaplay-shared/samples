namespace WebClient.Meta;

/// <summary>
/// Where a badge can appear. <see cref="Profile"/> is not a navigation tab: it is the Profile button on Home's
/// identity row. Its rule is kept in <see cref="BadgePolicy"/> with the others instead of in a component.
/// </summary>
public enum BadgeTarget
{
    Home,
    Events,
    Play,
    Compete,
    Shop,
    Profile,
}

/// <summary>
/// Decides whether a <see cref="BadgeTarget"/> shows a badge dot.
/// <para>
/// A badge means that something is actionable or unseen. It never shows a balance, a score or a timer. The rules
/// live here instead of in the navigation component so that tests can check that every badge clears once the
/// player acts on it (docs/meta-shell.md, "MetaSnapshot").
/// </para>
/// </summary>
public static class BadgePolicy
{
    public static bool ShouldBadge(BadgeTarget target, MetaSnapshot snapshot, SeenState seen) => target switch
    {
        // Home is the landing screen, and Play is always available, so neither has anything to point out.
        BadgeTarget.Home => false,
        BadgeTarget.Play => false,

        BadgeTarget.Events  => EventsNeedAttention(snapshot),
        BadgeTarget.Compete => CompeteNeedsAttention(snapshot),
        BadgeTarget.Shop    => snapshot.Shop.State.IsLive() &&
                               snapshot.Shop.UnseenTargeted(seen.SeenOfferIds).Any(),
        BadgeTarget.Profile => snapshot.Cosmetics.State.IsLive() &&
                               snapshot.Cosmetics.HasUnacknowledgedAcquisition &&
                               !seen.CosmeticAcknowledged,

        _ => false,
    };

    /// <summary>
    /// Whether any Events feature has a claimable reward, a first-week goal ending soon, a spin to use, or a new weekly
    /// event.
    /// </summary>
    private static bool EventsNeedAttention(MetaSnapshot snapshot)
    {
        if (snapshot.FirstWeek.NeedsAttention)
            return true;

        if (snapshot.DailyReward.State.IsLive() && snapshot.DailyReward.HasClaimableReward)
            return true;

        if (snapshot.Missions.State.IsLive() && snapshot.Missions.ClaimableCount > 0)
            return true;

        // A spin token to use, or a paid result the player has not seen. The badge clears when no tokens remain
        // and no result is pending.
        if (snapshot.SpinWheel.State.IsLive() && (snapshot.SpinWheel.SpinsAvailable > 0 || snapshot.SpinWheel.HasPendingReceipt))
            return true;

        // A weekly reward to collect, or a newly available event. Only HasClaimableReward comes from the real player
        // state. There is no per-player "seen" flag for a LiveOps event, so IsNewlyAvailable is false outside
        // fixtures.
        if (snapshot.WeeklyEvent.State.IsLive() && (snapshot.WeeklyEvent.HasClaimableReward || snapshot.WeeklyEvent.IsNewlyAvailable))
            return true;

        return false;
    }

    /// <summary>A result or reward awaiting collection, or a material change of rank or season.</summary>
    private static bool CompeteNeedsAttention(MetaSnapshot snapshot)
    {
        if (snapshot.Tournament.State.IsLive() &&
            (snapshot.Tournament.HasClaimableReward || snapshot.Tournament.HasMaterialChange))
            return true;

        return false;
    }

    /// <summary>Whether a navigation tab shows a badge.</summary>
    public static bool ShouldBadge(NavDestination destination, MetaSnapshot snapshot, SeenState seen) =>
        ShouldBadge(destination switch
        {
            NavDestination.Home    => BadgeTarget.Home,
            NavDestination.Events  => BadgeTarget.Events,
            NavDestination.Play    => BadgeTarget.Play,
            NavDestination.Compete => BadgeTarget.Compete,
            _                      => BadgeTarget.Shop,
        }, snapshot, seen);
}
