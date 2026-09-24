using Game.Logic;

namespace WebClient.Meta;

/// <summary>
/// Maps routes and features to <see cref="ShellScreen"/> values.
/// <para>
/// Routes not listed map to <see cref="ShellScreen.Unknown"/> and are not reported, so adding a route does not
/// change what the client sends until the route is added here.
/// </para>
/// </summary>
public static class ShellScreens
{
    /// <summary>The screen of each route, keyed by its normalised form (<see cref="MetaRoutes.Normalise"/>).</summary>
    static readonly Dictionary<string, ShellScreen> ScreenByRoute = new Dictionary<string, ShellScreen>
    {
        [MetaRoutes.Normalise(MetaRoutes.Home)]           = ShellScreen.Home,
        [MetaRoutes.Normalise(MetaRoutes.Events)]         = ShellScreen.Events,
        [MetaRoutes.Normalise(MetaRoutes.DailyReward)]    = ShellScreen.DailyReward,
        [MetaRoutes.Normalise(MetaRoutes.FirstWeekEvent)] = ShellScreen.FirstWeekEvent,
        [MetaRoutes.Normalise(MetaRoutes.Missions)]       = ShellScreen.Missions,
        [MetaRoutes.Normalise(MetaRoutes.SpinWheel)]      = ShellScreen.SpinWheel,
        [MetaRoutes.Normalise(MetaRoutes.WeeklyEvent)]    = ShellScreen.WeeklyEvent,
        [MetaRoutes.Normalise(MetaRoutes.Compete)]        = ShellScreen.Compete,
        [MetaRoutes.Normalise(MetaRoutes.Tournament)]     = ShellScreen.Tournament,
        [MetaRoutes.Normalise(MetaRoutes.Shop)]           = ShellScreen.Shop,
        [MetaRoutes.Normalise(MetaRoutes.Profile)]        = ShellScreen.Profile,
        [MetaRoutes.Normalise(MetaRoutes.Cosmetics)]      = ShellScreen.Cosmetics,
        [MetaRoutes.Normalise(MetaRoutes.Table)]          = ShellScreen.Table,
    };

    public static ShellScreen Of(string route) =>
        ScreenByRoute.GetValueOrDefault(MetaRoutes.Normalise(route), ShellScreen.Unknown);

    /// <summary>
    /// The screen for a feature's route. <see cref="MetaFeature.Play"/> maps to <see cref="ShellScreen.Table"/>
    /// because it has no meta route.
    /// </summary>
    public static ShellScreen Of(MetaFeature feature) => feature == MetaFeature.Play
        ? ShellScreen.Table
        : Of(MetaRoutes.For(feature)!);
}

/// <summary>
/// Decides which shell observations to report and filters out duplicates.
/// <para>
/// Re-renders, in-place history replacements and reconnects all repeat an arrival at the same screen without a
/// player action. Nothing is reported before a session exists, because observations are player actions and
/// need a player timeline, so the screen shown before the session is held until it starts
/// (<c>docs/analytics.md</c>). The class has no Blazor dependencies so that tests can cover every case.
/// </para>
/// </summary>
public sealed class ShellObservationPolicy
{
    private bool                     _sessionStarted;
    private ShellScreen              _currentScreen = ShellScreen.Unknown;
    private ShellScreen              _screenBeforeSession = ShellScreen.Unknown;
    private PromotedEntrySelected?   _selectionAwaitingNavigation;

    /// <summary>The screen that was last reported, or <see cref="ShellScreen.Unknown"/> if none.</summary>
    public ShellScreen CurrentScreen => _currentScreen;

    /// <summary>
    /// Marks the session as started. Returns the screen view held back while there was no session, or null if
    /// there is none. Returns null if the session was already started.
    /// <para>
    /// A held-back screen equal to <see cref="CurrentScreen"/> is not returned, so that a reconnect does not report
    /// the screen again.
    /// </para>
    /// </summary>
    public ScreenViewed? SessionStarted()
    {
        if (_sessionStarted)
            return null;

        _sessionStarted = true;

        if (_screenBeforeSession == ShellScreen.Unknown || _screenBeforeSession == _currentScreen)
        {
            _screenBeforeSession = ShellScreen.Unknown;
            return null;
        }

        _currentScreen = _screenBeforeSession;
        _screenBeforeSession = ShellScreen.Unknown;
        return new ScreenViewed(_currentScreen);
    }

    /// <summary>
    /// Marks the session as ended. When the session starts again, the current screen is not reported again, but
    /// a screen the player moved to while disconnected is.
    /// <para>
    /// <see cref="CurrentScreen"/> is kept because the client cannot tell a resumed session from a new one.
    /// Clearing it would make every reconnect report a duplicate, and a missing row is preferred over a wrong one.
    /// A page reload creates a new policy, so only sessions that restart without a reload are affected.
    /// </para>
    /// </summary>
    public void SessionEnded()
    {
        _sessionStarted              = false;
        _selectionAwaitingNavigation = null;
    }

    /// <summary>
    /// Records that the router is at <paramref name="route"/>. Returns the observation to send, or null if the
    /// screen is unknown, unchanged, or there is no session.
    /// </summary>
    public ScreenViewed? Arrived(string route)
    {
        ShellScreen screen = ShellScreens.Of(route);

        // An unknown route is not reported and does not change the current screen.
        if (screen == ShellScreen.Unknown)
            return null;

        if (!_sessionStarted)
        {
            _screenBeforeSession = screen;
            return null;
        }

        // A navigation completed, so clear the duplicate-tap guard in Selected. Clear it even when the screen is
        // unchanged, so the guard cannot stay set.
        _selectionAwaitingNavigation = null;

        if (screen == _currentScreen)
            return null;

        _currentScreen = screen;
        return new ScreenViewed(screen);
    }

    /// <summary>
    /// Records that the player selected a promoted entry leading to <paramref name="destination"/>. Returns the
    /// observation to send, or null if there is no session, no current screen, or the same selection repeats
    /// before the navigation it causes.
    /// </summary>
    public PromotedEntrySelected? Selected(PromotedEntryPlacement placement, MetaFeature destination) =>
        Selected(placement, ShellScreens.Of(destination));

    /// <summary>
    /// The same as the <see cref="MetaFeature"/> overload, for an entry that leads to a <see cref="ShellScreen"/>
    /// directly.
    /// </summary>
    public PromotedEntrySelected? Selected(PromotedEntryPlacement placement, ShellScreen destination)
    {
        if (!_sessionStarted || _currentScreen == ShellScreen.Unknown)
            return null;

        if (placement == PromotedEntryPlacement.Unknown || destination == ShellScreen.Unknown)
            return null;

        PromotedEntrySelected selection = new PromotedEntrySelected(placement, _currentScreen, destination);

        // An identical selection with no navigation in between is a repeated tap, not a second decision.
        if (selection == _selectionAwaitingNavigation)
            return null;

        // Set the guard only when the selection navigates away. A selection that leads to the current screen causes
        // no navigation to clear the guard, so setting it would block that entry for the rest of the session.
        _selectionAwaitingNavigation = destination == _currentScreen ? null : selection;
        return selection;
    }
}

/// <summary>
/// Connects the session state, <see cref="ShellObservationPolicy"/> and the <see cref="IShellAnalytics"/> sink.
/// <para>
/// Each call syncs the session state before passing the arrival or selection to the policy. The shell learns
/// about a disconnect on the same call as the navigation. Tests drive this class instead of the policy so that
/// they use the same call order as the shell.
/// </para>
/// </summary>
public sealed class ShellObservationRecorder
{
    private readonly IShellAnalytics        _sink;
    private readonly ShellObservationPolicy _policy = new ShellObservationPolicy();

    public ShellObservationRecorder(IShellAnalytics sink)
    {
        _sink = sink;
    }

    /// <summary>The screen that was last reported.</summary>
    public ShellScreen CurrentScreen => _policy.CurrentScreen;

    /// <param name="route">The router's current route.</param>
    /// <param name="hasSession">Whether a session exists to report observations to.</param>
    public void Arrived(string route, bool hasSession)
    {
        SyncSession(hasSession);

        if (_policy.Arrived(route) is ScreenViewed viewed)
            _sink.Observe(viewed);
    }

    public void Selected(PromotedEntryPlacement placement, MetaFeature destination, bool hasSession)
    {
        SyncSession(hasSession);

        if (_policy.Selected(placement, destination) is PromotedEntrySelected selected)
            _sink.Observe(selected);
    }

    private void SyncSession(bool hasSession)
    {
        if (!hasSession)
        {
            _policy.SessionEnded();
            return;
        }

        if (_policy.SessionStarted() is ScreenViewed pending)
            _sink.Observe(pending);
    }
}
