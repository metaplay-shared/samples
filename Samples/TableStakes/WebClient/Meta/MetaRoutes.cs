namespace WebClient.Meta;

/// <summary>
/// The navigation bar's tabs. <see cref="Play"/> opens no screen: it starts matchmaking. Profile has no tab and is
/// opened from the identity row on Home (docs/meta-shell.md, "Screens and routes").
/// </summary>
public enum NavDestination
{
    Home,
    Events,
    Play,
    Compete,
    Shop,
}

/// <summary>
/// Every meta feature that the shell shows.
/// <para>
/// The declaration order breaks ties in <see cref="NextActionPolicy"/> and in <see cref="FeatureOrdering"/>, so
/// that tied features keep the same order on every render. It matches the order of the features on their hub.
/// </para>
/// </summary>
public enum MetaFeature
{
    // Events children, in hub order.
    FirstWeekEvent,
    DailyReward,
    Missions,
    SpinWheel,
    WeeklyEvent,

    // The only feature under Compete (docs/seasonal-tournament.md).
    Tournament,

    // Features outside the Events and Compete hubs.
    Shop,
    Profile,
    Cosmetics,

    /// <summary>
    /// The game itself, used by the last rung of <see cref="NextActionPolicy"/> and by analytics. It has no meta
    /// screen, so <see cref="MetaRoutes.For(MetaFeature)"/> returns null for it.
    /// </summary>
    Play,
}
/// <summary>
/// The meta shell's routes, the navigation tab each route selects, and where each route's Back control leads.
/// <para>
/// Both relationships depend only on the route, not on navigation history. A deep link to <c>/events/spin</c>
/// selects the Events tab and its Back control leads to the Events hub, so a refreshed page or a shared URL still
/// has a way back.
/// </para>
/// </summary>
public static class MetaRoutes
{
    public const string Home           = "/";
    public const string Events         = "/events";
    public const string DailyReward    = "/events/daily";
    public const string FirstWeekEvent = "/events/first-week";
    public const string Missions       = "/events/missions";
    public const string SpinWheel      = "/events/spin";
    public const string WeeklyEvent    = "/events/weekly";
    public const string Compete        = "/compete";
    public const string Tournament     = "/compete/tournament";
    public const string Shop           = "/shop";
    public const string Profile        = "/profile";
    public const string Cosmetics      = "/profile/cosmetics";

    /// <summary>The game table. It shows no HUD or navigation (see <see cref="IsGameRoute"/>).</summary>
    public const string Table = "/table";

    /// <summary>
    /// The route of a feature's screen, or null for <see cref="MetaFeature.Play"/>. Callers start matchmaking
    /// instead of navigating when this returns null.
    /// </summary>
    public static string? For(MetaFeature feature) => feature switch
    {
        MetaFeature.FirstWeekEvent => FirstWeekEvent,
        MetaFeature.DailyReward    => DailyReward,
        MetaFeature.Missions       => Missions,
        MetaFeature.SpinWheel      => SpinWheel,
        MetaFeature.WeeklyEvent    => WeeklyEvent,
        MetaFeature.Tournament     => Tournament,
        MetaFeature.Shop           => Shop,
        MetaFeature.Profile        => Profile,
        MetaFeature.Cosmetics      => Cosmetics,
        MetaFeature.Play           => null,
        _                          => Home,
    };

    /// <summary>
    /// The navigation tab to select for a route, or null if none. Profile routes and unknown routes return null.
    /// <see cref="NavDestination.Play"/> is never returned because it has no route.
    /// </summary>
    public static NavDestination? DestinationOf(string route)
    {
        string path = Normalise(route);

        if (path == Normalise(Home))
            return NavDestination.Home;
        if (path.StartsWith("events", StringComparison.Ordinal))
            return NavDestination.Events;
        if (path.StartsWith("compete", StringComparison.Ordinal))
            return NavDestination.Compete;
        if (path.StartsWith("shop", StringComparison.Ordinal))
            return NavDestination.Shop;

        return null;
    }

    /// <summary>
    /// The route a tab opens, or null for <see cref="NavDestination.Play"/>, which starts matchmaking instead.
    /// </summary>
    public static string? For(NavDestination destination) => destination switch
    {
        NavDestination.Home    => Home,
        NavDestination.Events  => Events,
        NavDestination.Play    => null,
        NavDestination.Compete => Compete,
        NavDestination.Shop    => Shop,
        _                      => Home,
    };

    /// <summary>
    /// The route that a child page's Back control leads to, or null for Home, a hub or an unknown route. It does
    /// not use browser history, because history would take a player who arrived through a deep link out of the
    /// app.
    /// </summary>
    public static string? ParentOf(string route)
    {
        string path = Normalise(route);

        if (path == Normalise(Home) || path == "events" || path == "compete" || path == "shop")
            return null;

        if (path.StartsWith("events/", StringComparison.Ordinal))
            return Events;
        if (path.StartsWith("compete/", StringComparison.Ordinal))
            return Compete;
        if (path == "profile")
            return Home;
        if (path.StartsWith("profile/", StringComparison.Ordinal))
            return Profile;

        return null;
    }

    /// <summary>
    /// Whether a route is the game table instead of a meta shell screen. The table shows no HUD or navigation bar,
    /// so the player cannot leave a game in progress with a single tap.
    /// </summary>
    public static bool IsGameRoute(string route) => Normalise(route) == Normalise(Table);

    /// <summary>
    /// Removes any query or fragment and the leading and trailing slashes, and converts the route to lower case.
    /// </summary>
    public static string Normalise(string route)
    {
        string path = route;

        int cut = path.IndexOfAny(new[] { '?', '#' });
        if (cut >= 0)
            path = path.Substring(0, cut);

        path = path.Trim('/');
        return path.ToLowerInvariant();
    }
}
