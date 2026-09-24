using Game.Logic;
using WebClient.Meta;

namespace WebClient.Tests;

/// <summary>
/// Tests for the client-side analytics events: which event names the client may submit.
/// <para>
/// The client may submit only the allow-listed events. The per-player event log is an audit trail, and UI events
/// mixed into it make it hard to read (<c>docs/analytics.md</c>). These tests fail when a name is added.
/// </para>
/// </summary>
[TestFixture]
public class ShellAnalyticsTests
{
    [Test]
    public void TheClientMaySubmitExactlyTwoObservations()
    {
        Assert.That(ShellAnalytics.All, Is.EquivalentTo(new[] { "screen_viewed", "promoted_entry_selected" }));
    }

    /// <summary>
    /// No name on <c>ShellAnalytics.Excluded</c> may appear on the allow-list. Allowing one is a deliberate
    /// contract change that must also remove it from the excluded list.
    /// </summary>
    [Test]
    public void NothingOnTheExcludedListIsAllowedBack()
    {
        Assert.That(ShellAnalytics.Excluded, Is.Unique);

        foreach (string excluded in ShellAnalytics.Excluded)
            Assert.That(ShellAnalytics.All, Does.Not.Contain(excluded),
                $"'{excluded}' is excluded by the analytics contract; the fact is either an SDK event already or UI noise");
    }

    [Test]
    public void EveryNameIsLowerSnakeCase()
    {
        foreach (string name in ShellAnalytics.All.Concat(ShellAnalytics.Excluded))
        {
            Assert.That(name, Does.Match("^[a-z][a-z0-9_]*$"), name);
            Assert.That(name, Does.Not.Contain("__"), name);
        }
    }

    /// <summary>
    /// The shell reports player actions, not economy changes. The server's feature code already records every
    /// grant and spend, so a client event for the same change would double-count it.
    /// </summary>
    [Test]
    public void TheShellClaimsNoEconomyEvent()
    {
        string[] forbidden = { "currency", "granted", "purchase", "spend", "balance", "reward_granted" };

        foreach (string name in ShellAnalytics.All)
        foreach (string word in forbidden)
            Assert.That(name, Does.Not.Contain(word), $"{name} looks like an economy event, which the shell does not own");
    }

    /// <summary>
    /// The shell's event names match the aliases of the shared-code analytics events they are submitted as. The
    /// names are separate constants so the shell does not depend on the event types, and this test keeps them
    /// in sync.
    /// </summary>
    [Test]
    public void TheNamesMatchTheEventsTheyAreSubmittedAs()
    {
        Assert.That(AnalyticsEventCodes.EntryOf(AnalyticsEventCodes.ScreenViewed)!.Alias, Is.EqualTo(ShellAnalytics.ScreenViewed));
        Assert.That(AnalyticsEventCodes.EntryOf(AnalyticsEventCodes.PromotedEntrySelected)!.Alias, Is.EqualTo(ShellAnalytics.PromotedEntrySelected));
    }

    [Test]
    public void AnObservationKnowsWhichNameItIsRecordedUnder()
    {
        Assert.That(new ScreenViewed(ShellScreen.Home).Name, Is.EqualTo("screen_viewed"));
        Assert.That(new PromotedEntrySelected(PromotedEntryPlacement.Teaser, ShellScreen.Home, ShellScreen.Shop).Name,
            Is.EqualTo("promoted_entry_selected"));
    }
}

/// <summary>
/// Tests for <see cref="ShellObservationPolicy"/>: which navigations and selections are reported, and which
/// duplicates are suppressed.
/// </summary>
[TestFixture]
public class ShellObservationPolicyTests
{
    #region Screens

    [Test]
    public void EveryRouteInTheShellHasAScreen()
    {
        string[] routes =
        {
            MetaRoutes.Home, MetaRoutes.Events, MetaRoutes.DailyReward, MetaRoutes.FirstWeekEvent,
            MetaRoutes.Missions, MetaRoutes.SpinWheel, MetaRoutes.WeeklyEvent,
            MetaRoutes.Compete, MetaRoutes.Tournament, MetaRoutes.Shop,
            MetaRoutes.Profile, MetaRoutes.Cosmetics, MetaRoutes.Table,
        };

        foreach (string route in routes)
            Assert.That(ShellScreens.Of(route), Is.Not.EqualTo(ShellScreen.Unknown), $"{route} has no screen in the taxonomy");

        Assert.That(routes.Select(ShellScreens.Of), Is.Unique, "two routes report as the same screen");
    }

    /// <summary>
    /// A Play selection is reported with the table as its destination, because Play has no screen of its own
    /// in the meta shell.
    /// </summary>
    [Test]
    public void PlayIsReportedAsTheTable()
    {
        Assert.That(ShellScreens.Of(MetaFeature.Play), Is.EqualTo(ShellScreen.Table));
        Assert.That(ShellScreens.Of("/play"), Is.EqualTo(ShellScreen.Unknown),
            "the route is gone; an arrival there is not reported at all");
    }

    [Test]
    public void EveryFeatureHasAScreen()
    {
        foreach (MetaFeature feature in Enum.GetValues<MetaFeature>())
            Assert.That(ShellScreens.Of(feature), Is.Not.EqualTo(ShellScreen.Unknown), $"{feature} has no screen in the taxonomy");
    }

    /// <summary>
    /// A query string, trailing slash, letter case or fragment does not change the screen. The shell's fixture
    /// and timing options are passed in the query string, so changing a scenario must not count as a new arrival.
    /// </summary>
    [TestCase("/events?meta=busy", ShellScreen.Events)]
    [TestCase("/events/",          ShellScreen.Events)]
    [TestCase("/Events",           ShellScreen.Events)]
    [TestCase("/#top",             ShellScreen.Home)]
    public void AQueryStringDoesNotMakeANewScreen(string route, ShellScreen expected)
    {
        Assert.That(ShellScreens.Of(route), Is.EqualTo(expected));
    }

    [Test]
    public void ARouteWithNoScreenIsNotReported()
    {
        Shell shell = Shell.Connected();

        shell.Navigate("/does-not-exist");

        Assert.That(shell.Log, Is.Empty);
        Assert.That(shell.CurrentScreen, Is.EqualTo(ShellScreen.Unknown), "an unnamed route moved where the player is on record as being");
    }

    #endregion

    #region Debouncing arrivals

    [Test]
    public void AnArrivalIsReportedOnce()
    {
        Shell shell = Shell.Connected();

        shell.Navigate(MetaRoutes.Events);
        shell.Navigate(MetaRoutes.Events);
        shell.Navigate("/events?meta=busy");

        Assert.That(shell.Log, Is.EqualTo(new[] { new ScreenViewed(ShellScreen.Events) }),
            "a re-render or a history replacement reported the same screen again");
    }

    [Test]
    public void GoingAwayAndComingBackIsTwoArrivals()
    {
        Shell shell = Shell.Connected();

        shell.Navigate(MetaRoutes.Home);
        shell.Navigate(MetaRoutes.Shop);
        shell.Navigate(MetaRoutes.Home);

        Assert.That(shell.Log, Has.Count.EqualTo(3));
    }

    /// <summary>
    /// A reconnect rebuilds the layout, which reports the current route again. The player did not move, so no
    /// arrival is reported.
    /// </summary>
    [Test]
    public void AReconnectDoesNotReportTheScreenAgain()
    {
        Shell shell = Shell.Connected();
        shell.Navigate(MetaRoutes.Shop);

        shell.Disconnect();
        shell.Navigate(MetaRoutes.Shop);

        shell.Resume();
        shell.Navigate(MetaRoutes.Shop);

        Assert.That(shell.Log, Is.EqualTo(new[] { new ScreenViewed(ShellScreen.Shop) }));
    }

    /// <summary>
    /// While disconnected, the player navigated away and back to the screen last reported. Nothing is reported
    /// while disconnected, and on resume the screen matches the last reported one, so nothing is reported.
    /// </summary>
    [Test]
    public void ARoundTripTakenWhileDisconnectedReportsNothing()
    {
        Shell shell = Shell.Connected();
        shell.Navigate(MetaRoutes.Home);

        shell.Disconnect();
        shell.Navigate(MetaRoutes.Shop);
        shell.Navigate(MetaRoutes.Home);

        shell.Resume();
        shell.Navigate(MetaRoutes.Home);

        Assert.That(shell.Log, Is.EqualTo(new[] { new ScreenViewed(ShellScreen.Home) }));
    }

    #endregion

    #region The session gate

    [Test]
    public void NothingIsObservedBeforeASessionExists()
    {
        Shell shell = Shell.Disconnected();

        shell.Navigate(MetaRoutes.Shop);
        shell.Select(PromotedEntryPlacement.FeatureCard, MetaFeature.Shop);

        Assert.That(shell.Log, Is.Empty);
    }

    /// <summary>
    /// The screen shown before the session connects is reported when the session arrives. Otherwise the log would
    /// start at the player's second screen.
    /// </summary>
    [Test]
    public void TheScreenTheyBootedIntoIsReleasedWhenTheSessionArrives()
    {
        Shell shell = Shell.Disconnected();
        shell.Navigate(MetaRoutes.SpinWheel);

        shell.Resume();
        shell.Navigate(MetaRoutes.SpinWheel);

        Assert.That(shell.Log, Is.EqualTo(new[] { new ScreenViewed(ShellScreen.SpinWheel) }));
    }

    [Test]
    public void OnlyTheLastScreenBeforeTheSessionCounts()
    {
        Shell shell = Shell.Disconnected();
        shell.Navigate(MetaRoutes.Home);
        shell.Navigate(MetaRoutes.Shop);

        shell.Resume();
        shell.Navigate(MetaRoutes.Shop);

        Assert.That(shell.Log, Is.EqualTo(new[] { new ScreenViewed(ShellScreen.Shop) }));
    }

    [Test]
    public void ANavigationWhileDisconnectedIsReleasedOnResume()
    {
        Shell shell = Shell.Connected();
        shell.Navigate(MetaRoutes.Home);

        shell.Disconnect();
        shell.Navigate(MetaRoutes.Shop);

        shell.Resume();
        shell.Navigate(MetaRoutes.Shop);

        Assert.That(shell.Log, Is.EqualTo(new ShellObservation[]
        {
            new ScreenViewed(ShellScreen.Home),
            new ScreenViewed(ShellScreen.Shop),
        }));
    }

    #endregion

    #region Selections

    [Test]
    public void ASelectionCarriesWhereItWasTakenFromAndWhereItLed()
    {
        Shell shell = Shell.Connected();
        shell.Navigate(MetaRoutes.Home);
        shell.Select(PromotedEntryPlacement.NextActionCard, MetaFeature.SpinWheel);

        Assert.That(shell.Log[1], Is.EqualTo(
            new PromotedEntrySelected(PromotedEntryPlacement.NextActionCard, ShellScreen.Home, ShellScreen.SpinWheel)));
    }

    [Test]
    public void ADoubleTapIsOneSelection()
    {
        Shell shell = Shell.Connected();
        shell.Navigate(MetaRoutes.Home);

        shell.Select(PromotedEntryPlacement.Teaser, MetaFeature.Shop);
        shell.Select(PromotedEntryPlacement.Teaser, MetaFeature.Shop);

        Assert.That(shell.Log, Has.Count.EqualTo(2), "one gesture was counted twice");
    }

    [Test]
    public void TakingTheSameEntryAgainLaterIsASecondSelection()
    {
        Shell shell = Shell.Connected();
        shell.Navigate(MetaRoutes.Home);
        shell.Select(PromotedEntryPlacement.Teaser, MetaFeature.Shop);

        shell.Navigate(MetaRoutes.Shop);
        shell.Navigate(MetaRoutes.Home);
        shell.Select(PromotedEntryPlacement.Teaser, MetaFeature.Shop);

        Assert.That(shell.Log.OfType<PromotedEntrySelected>().Count(), Is.EqualTo(2));
    }

    /// <summary>
    /// Selecting an entry that leads to the current screen causes no navigation. The double-tap guard must still
    /// reset on the next navigation, or the entry could be reported only once per session.
    /// </summary>
    [Test]
    public void AnEntryThatLeadsNowhereIsStillReportableTwice()
    {
        Shell shell = Shell.Connected();
        shell.Navigate(MetaRoutes.Shop);

        shell.Select(PromotedEntryPlacement.FeatureCard, MetaFeature.Shop);
        shell.Navigate(MetaRoutes.Shop);
        shell.Select(PromotedEntryPlacement.FeatureCard, MetaFeature.Shop);

        Assert.That(shell.Log.OfType<PromotedEntrySelected>().Count(), Is.EqualTo(2),
            "a promoted entry became unreportable for the rest of the session");
    }

    [Test]
    public void ASelectionFromNowhereIsNotReported()
    {
        Shell shell = Shell.Connected();

        shell.Select(PromotedEntryPlacement.FeatureCard, MetaFeature.Shop);

        Assert.That(shell.Log, Is.Empty, "a selection was reported before the player was known to be anywhere");
    }

    [Test]
    public void AnUnnamedPlacementIsNotReported()
    {
        Shell shell = Shell.Connected();
        shell.Navigate(MetaRoutes.Home);

        shell.Select(PromotedEntryPlacement.Unknown, MetaFeature.Shop);

        Assert.That(shell.Log.OfType<PromotedEntrySelected>(), Is.Empty);
    }

    #endregion

    /// <summary>
    /// Drives a <see cref="ShellObservationRecorder"/> with a connection flag and records what reaches the sink.
    /// <para>
    /// Tests use this instead of calling <see cref="ShellObservationPolicy"/> directly, because the shell passes
    /// the connection state on the same call that reports the navigation. A test that set the two separately
    /// could produce a call order the shell never produces.
    /// </para>
    /// </summary>
    private sealed class Shell
    {
        private readonly RecordingShellAnalytics  _sink = new RecordingShellAnalytics();
        private readonly ShellObservationRecorder _recorder;
        private bool                              _connected;

        private Shell(bool connected)
        {
            _connected = connected;
            _recorder  = new ShellObservationRecorder(_sink);
        }

        public static Shell Connected() => new Shell(connected: true);

        public static Shell Disconnected() => new Shell(connected: false);

        public IReadOnlyList<ShellObservation> Log => _sink.Observations;

        public ShellScreen CurrentScreen => _recorder.CurrentScreen;

        public void Disconnect() => _connected = false;

        public void Resume() => _connected = true;

        public void Navigate(string route) => _recorder.Arrived(route, _connected);

        public void Select(PromotedEntryPlacement placement, MetaFeature destination) =>
            _recorder.Selected(placement, destination, _connected);
    }
}
