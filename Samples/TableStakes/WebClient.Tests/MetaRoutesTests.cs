using WebClient.Meta;

namespace WebClient.Tests;

/// <summary>
/// <see cref="MetaRoutes"/>: which navigation destination a route belongs to, and where Back leads from a child
/// route.
/// <para>
/// Both answers depend only on the route, not on how the player reached it. This keeps deep links and page
/// refreshes working: a refreshed <c>/events/spin</c> still selects the Events tab, and its Back control goes to
/// Events instead of leaving the app.
/// </para>
/// </summary>
[TestFixture]
public class MetaRoutesTests
{
    [TestCase(MetaRoutes.Home,           NavDestination.Home)]
    [TestCase(MetaRoutes.Events,         NavDestination.Events)]
    [TestCase(MetaRoutes.DailyReward,    NavDestination.Events)]
    [TestCase(MetaRoutes.FirstWeekEvent, NavDestination.Events)]
    [TestCase(MetaRoutes.Missions,       NavDestination.Events)]
    [TestCase(MetaRoutes.SpinWheel,      NavDestination.Events)]
    [TestCase(MetaRoutes.WeeklyEvent,    NavDestination.Events)]
    [TestCase(MetaRoutes.Compete,        NavDestination.Compete)]
    [TestCase(MetaRoutes.Tournament,     NavDestination.Compete)]
    [TestCase(MetaRoutes.Shop,           NavDestination.Shop)]
    public void AChildRouteLightsItsParentDestination(string route, NavDestination expected)
    {
        Assert.That(MetaRoutes.DestinationOf(route), Is.EqualTo(expected));
    }

    /// <summary>
    /// Play is an action that starts matchmaking, not a screen, so it has no route and is never selected from the
    /// address.
    /// </summary>
    [Test]
    public void PlayHasNoRoute()
    {
        Assert.That(MetaRoutes.For(MetaFeature.Play), Is.Null);
        Assert.That(MetaRoutes.For(NavDestination.Play), Is.Null);
    }

    /// <summary>
    /// Profile and Cosmetics are reached from the HUD avatar and belong to no navigation destination, so no tab is
    /// selected while they are open.
    /// </summary>
    [TestCase(MetaRoutes.Profile)]
    [TestCase(MetaRoutes.Cosmetics)]
    public void ProfileLightsNoDestination(string route)
    {
        Assert.That(MetaRoutes.DestinationOf(route), Is.Null);
    }

    /// <summary>
    /// The destination ignores a query string, a fragment, a missing leading slash, a trailing slash and letter case.
    /// </summary>
    [TestCase("/events/spin?meta=fresh")]
    [TestCase("/events/spin#top")]
    [TestCase("events/spin")]
    [TestCase("/events/spin/")]
    [TestCase("/Events/Spin")]
    public void TheDestinationSurvivesHowTheUrlIsWritten(string route)
    {
        Assert.That(MetaRoutes.DestinationOf(route), Is.EqualTo(NavDestination.Events));
    }

    [TestCase(MetaRoutes.DailyReward,    MetaRoutes.Events)]
    [TestCase(MetaRoutes.FirstWeekEvent, MetaRoutes.Events)]
    [TestCase(MetaRoutes.SpinWheel,      MetaRoutes.Events)]
    [TestCase(MetaRoutes.Tournament,     MetaRoutes.Compete)]
    [TestCase(MetaRoutes.Cosmetics,      MetaRoutes.Profile)]
    [TestCase(MetaRoutes.Profile,        MetaRoutes.Home)]
    public void BackFromAChildGoesToItsParent(string route, string expected)
    {
        Assert.That(MetaRoutes.ParentOf(route), Is.EqualTo(expected));
    }

    /// <summary>A hub route has no parent, so its header shows a title and no Back control.</summary>
    [TestCase(MetaRoutes.Home)]
    [TestCase(MetaRoutes.Events)]
    [TestCase(MetaRoutes.Compete)]
    [TestCase(MetaRoutes.Shop)]
    public void AHubHasNoBack(string route)
    {
        Assert.That(MetaRoutes.ParentOf(route), Is.Null);
    }

    /// <summary>
    /// Every <see cref="MetaFeature"/> except Play has its own route, and no two features share a route or use
    /// Home. Play has no route (see <see cref="PlayHasNoRoute"/>).
    /// </summary>
    [Test]
    public void EveryFeatureHasItsOwnRoute()
    {
        MetaFeature[] features = Enum.GetValues<MetaFeature>()
            .Where(feature => feature != MetaFeature.Play)
            .ToArray();
        string[] routes = features.Select(feature => MetaRoutes.For(feature)!).ToArray();

        Assert.That(routes, Is.Unique);
        Assert.That(routes, Has.None.EqualTo(MetaRoutes.Home),
            "a feature route that is Home is a feature with no screen of its own");
    }

    /// <summary>
    /// Every feature is at most two taps from the navigation: its route is a destination itself, or its parent is a
    /// destination or Profile (which the HUD avatar opens). Play is skipped because it has no route.
    /// </summary>
    [Test]
    public void EveryFeatureIsWithinTwoTapsOfTheNavigation()
    {
        foreach (MetaFeature feature in Enum.GetValues<MetaFeature>())
        {
            string? route  = MetaRoutes.For(feature);
            if (route == null)
            {
                Assert.That(feature, Is.EqualTo(MetaFeature.Play),
                    "a feature with no route is a feature with no way in");
                continue;
            }

            string? parent = MetaRoutes.ParentOf(route);

            bool isItselfADestination = MetaRoutes.DestinationOf(route) != null && parent == null;
            bool parentIsReachable    = parent != null &&
                                        (MetaRoutes.DestinationOf(parent) != null || parent == MetaRoutes.Profile);

            Assert.That(isItselfADestination || parentIsReachable, Is.True,
                $"{feature} at {route} is more than two taps from the navigation");
        }
    }

    /// <summary>The table is the only game route, which the shell draws without the meta navigation.</summary>
    [Test]
    public void OnlyTheTableIsAGameRoute()
    {
        Assert.That(MetaRoutes.IsGameRoute(MetaRoutes.Table), Is.True);
        Assert.That(MetaRoutes.IsGameRoute("/table?beatMs=0"), Is.True);

        foreach (MetaFeature feature in Enum.GetValues<MetaFeature>())
        {
            if (feature == MetaFeature.Play)
                continue;

            Assert.That(MetaRoutes.IsGameRoute(MetaRoutes.For(feature)!), Is.False, $"{feature} is not the game");
        }

        Assert.That(MetaRoutes.IsGameRoute(MetaRoutes.Home), Is.False);
    }
}
