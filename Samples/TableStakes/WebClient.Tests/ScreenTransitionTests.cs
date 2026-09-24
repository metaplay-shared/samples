using WebClient.Meta;

namespace WebClient.Tests;

/// <summary>
/// Tests for <see cref="ScreenTransitions.Between"/>, which chooses the direction a screen slides in from when
/// the shell navigates between two routes.
/// <para>
/// <c>ScreenTransitions.Between</c> is a pure function of two routes, so it needs no browser.
/// <c>ShellPageTests</c> checks that the shell plays the transition and scrolls the new screen to its top.
/// </para>
/// </summary>
[TestFixture]
public class ScreenTransitionTests
{
    [Test]
    public void TheFirstScreenOfASessionHasNowhereToHaveComeFrom()
    {
        Assert.That(ScreenTransitions.Between(null, MetaRoutes.Home), Is.EqualTo(ScreenTransition.Arrive));
    }

    [TestCase(MetaRoutes.Events,  MetaRoutes.Missions)]
    [TestCase(MetaRoutes.Compete, MetaRoutes.Tournament)]
    [TestCase(MetaRoutes.Home,    MetaRoutes.Profile)]    // Profile is a child of Home, not of a navigation bar destination.
    public void OpeningAChildOfTheScreenShowingGoesForward(string from, string to)
    {
        Assert.That(ScreenTransitions.Between(from, to), Is.EqualTo(ScreenTransition.Forward));
    }

    /// <summary>
    /// Navigating to any ancestor, not only the direct parent, goes back. The Back button goes up one level,
    /// but a screen two levels down could link to Home.
    /// </summary>
    [TestCase(MetaRoutes.Missions,  MetaRoutes.Events)]
    [TestCase(MetaRoutes.Cosmetics, MetaRoutes.Profile)]
    [TestCase(MetaRoutes.Cosmetics, MetaRoutes.Home)]
    public void ReturningToAParentOrPastItGoesBack(string from, string to)
    {
        Assert.That(ScreenTransitions.Between(from, to), Is.EqualTo(ScreenTransition.Back));
    }

    /// <summary>
    /// Between navigation bar destinations, the direction follows the bar's left-to-right order. A destination to
    /// the right of the current one goes forward, whichever screen of the current destination is showing.
    /// </summary>
    [TestCase(MetaRoutes.Home,     MetaRoutes.Shop,    ScreenTransition.Forward)]
    [TestCase(MetaRoutes.Shop,     MetaRoutes.Home,    ScreenTransition.Back)]
    [TestCase(MetaRoutes.Events,   MetaRoutes.Compete, ScreenTransition.Forward)]
    [TestCase(MetaRoutes.Missions, MetaRoutes.Home,    ScreenTransition.Back)]    // From a child of Events to Home, which is to the left of Events on the bar.
    public void TheNavigationBarsOwnOrderDecidesWhichWayATabGoes(string from, string to, ScreenTransition expected)
    {
        Assert.That(ScreenTransitions.Between(from, to), Is.EqualTo(expected));
    }

    /// <summary>
    /// When neither the parent chain nor the navigation bar relates two screens, moving to a deeper screen goes
    /// forward and moving to a shallower one goes back. The Shop's link to Cosmetics, a child of Profile, is an
    /// example.
    /// </summary>
    [TestCase(MetaRoutes.Shop,      MetaRoutes.Cosmetics, ScreenTransition.Forward)]
    [TestCase(MetaRoutes.Cosmetics, MetaRoutes.Shop,      ScreenTransition.Back)]
    public void AStepIntoADeeperScreenGoesForwardEvenAcrossHubs(string from, string to, ScreenTransition expected)
    {
        Assert.That(ScreenTransitions.Between(from, to), Is.EqualTo(expected));
    }

    /// <summary>
    /// Two unrelated screens at the same depth have no direction, so the new screen arrives in place. Profile
    /// belongs to no navigation bar destination, and two children of one hub have no order between them.
    /// </summary>
    [TestCase(MetaRoutes.Profile,  MetaRoutes.Shop)]
    [TestCase(MetaRoutes.Missions, MetaRoutes.SpinWheel)]
    public void ScreensWithNoAxisBetweenThemArriveInPlace(string from, string to)
    {
        Assert.That(ScreenTransitions.Between(from, to), Is.EqualTo(ScreenTransition.Arrive));
    }

    /// <summary>
    /// Navigation to or from the table arrives in place, because the table has no shell chrome. When leaving the
    /// table, the shell layout is rebuilt and has no previous route, which also gives Arrive.
    /// </summary>
    [TestCase(MetaRoutes.Table, MetaRoutes.Home)]
    [TestCase(MetaRoutes.Shop,  MetaRoutes.Table)]
    public void TheGameIsNotAStepAlongTheShellsAxis(string from, string to)
    {
        Assert.That(ScreenTransitions.Between(from, to), Is.EqualTo(ScreenTransition.Arrive));
    }

    /// <summary>
    /// A navigation that does not change the screen, such as a changed <c>?meta=</c> query string, plays no
    /// transition. A transition would also scroll the screen back to its top.
    /// </summary>
    [TestCase(MetaRoutes.Events, MetaRoutes.Events)]
    [TestCase("/events",         "/events?meta=first-week-day-3")]
    [TestCase("/Events/",        "/events")]
    public void ANavigationThatChangedNoScreenMovesNothing(string from, string to)
    {
        Assert.That(ScreenTransitions.Between(from, to), Is.EqualTo(ScreenTransition.None));
    }
}
