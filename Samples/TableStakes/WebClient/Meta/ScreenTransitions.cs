namespace WebClient.Meta;

/// <summary>
/// The direction of a navigation, which sets the side the new screen slides in from.
/// </summary>
public enum ScreenTransition
{
    /// <summary>The route did not change, so there is no animation.</summary>
    None,

    /// <summary>Into a child screen, or rightward along the navigation bar.</summary>
    Forward,

    /// <summary>Out to a parent screen, or leftward along the navigation bar.</summary>
    Back,

    /// <summary>
    /// No direction applies: the first screen of a session, entering or leaving the game, or a move between two
    /// unrelated screens at the same depth. The screen appears in place.
    /// </summary>
    Arrive,
}

/// <summary>
/// Chooses the <see cref="ScreenTransition"/> for a navigation from the hub-to-child relationship of the two
/// routes and their order in the navigation bar.
/// <para>
/// It uses the routes instead of browser history, because the shell's Back control does not follow history
/// either (see <see cref="MetaRoutes.ParentOf"/>). It is a pure function so that tests can check every case
/// without a browser.
/// </para>
/// </summary>
public static class ScreenTransitions
{
    /// <summary>
    /// The transition for a navigation from <paramref name="from"/> to <paramref name="to"/>.
    /// <paramref name="from"/> is null on the shell's first render and on every return from the game, which
    /// rebuilds the meta layout.
    /// </summary>
    public static ScreenTransition Between(string? from, string to)
    {
        if (from == null)
            return ScreenTransition.Arrive;

        string fromRoute = MetaRoutes.Normalise(from);
        string toRoute   = MetaRoutes.Normalise(to);

        if (fromRoute == toRoute)
            return ScreenTransition.None;

        // The game is not in the navigation bar or under a hub, so entering or leaving it has no direction.
        if (MetaRoutes.IsGameRoute(fromRoute) || MetaRoutes.IsGameRoute(toRoute))
            return ScreenTransition.Arrive;

        // Check the hub-to-child relationship first, because the player got here through a feature card, the
        // identity row or the header's Back control.
        if (IsAncestor(ancestor: toRoute, of: fromRoute))
            return ScreenTransition.Back;

        if (IsAncestor(ancestor: fromRoute, of: toRoute))
            return ScreenTransition.Forward;

        // Otherwise use the navigation bar order: a tab to the right of the current one slides in from the right.
        if (MetaRoutes.DestinationOf(fromRoute) is NavDestination fromTab &&
            MetaRoutes.DestinationOf(toRoute) is NavDestination toTab &&
            fromTab != toTab)
        {
            return toTab > fromTab ? ScreenTransition.Forward : ScreenTransition.Back;
        }

        // Otherwise compare route depth. This covers links from a hub into another hub's child, such as the Shop's
        // Cosmetics card, which opens a child of Profile.
        int fromDepth = Depth(fromRoute);
        int toDepth   = Depth(toRoute);

        if (fromDepth != toDepth)
            return toDepth > fromDepth ? ScreenTransition.Forward : ScreenTransition.Back;

        // Unrelated screens at the same depth, such as Profile and a hub, or two children of one hub.
        return ScreenTransition.Arrive;
    }

    /// <summary>The number of path segments in a normalised route. Home, the empty path, has zero.</summary>
    private static int Depth(string route) =>
        route.Length == 0 ? 0 : route.Count(character => character == '/') + 1;

    /// <summary>
    /// Whether <paramref name="ancestor"/> is in the <see cref="MetaRoutes.ParentOf"/> chain of <paramref name="of"/>.
    /// </summary>
    private static bool IsAncestor(string ancestor, string of)
    {
        string? walk = MetaRoutes.ParentOf(of);

        while (walk != null)
        {
            if (MetaRoutes.Normalise(walk) == ancestor)
                return true;

            walk = MetaRoutes.ParentOf(walk);
        }

        return false;
    }
}
