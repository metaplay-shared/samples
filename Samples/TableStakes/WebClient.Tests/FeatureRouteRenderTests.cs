using Bunit;
using WebClient.Components.Pages.Meta;

namespace WebClient.Tests;

/// <summary>
/// bUnit render tests that each meta feature page renders its own title. The route-to-page map is written by hand,
/// so a changed <c>@page</c> directive is not caught here. <c>ShellPageTests</c> covers loading each route in a
/// browser.
/// </summary>
[TestFixture]
public class FeatureRouteRenderTests : BunitPageTest
{
    [TestCase("/events",             typeof(EventsPage),      "Events")]
    [TestCase("/events/daily",       typeof(DailyRewardPage), "Daily Reward")]
    [TestCase("/events/first-week",  typeof(FirstWeekPage),   "First-Week Event")]
    [TestCase("/events/missions",    typeof(MissionsPage),    "Missions")]
    [TestCase("/events/spin",        typeof(SpinWheelPage),   "Spin Wheel")]
    [TestCase("/events/weekly",      typeof(WeeklyEventPage), "Weekly Event")]
    [TestCase("/compete",            typeof(CompetePage),         "Compete")]
    [TestCase("/compete/tournament", typeof(CompetePage),         "Compete")]
    [TestCase("/shop",               typeof(ShopPage),            "Shop")]
    [TestCase("/profile",            typeof(ProfilePage),         "Profile")]
    [TestCase("/profile/cosmetics",  typeof(CosmeticsPage),       "Cosmetics")]
    public void EachFeatureRouteRendersItsPageTitle(string route, Type componentType, string title)
    {
        Setup(route);

        IRenderedFragment page = RenderPage(componentType);

        Assert.That(page.Find("[data-testid=\"page-title\"]").TextContent, Does.Contain(title));
    }

    /// <summary>
    /// Renders a component whose type is known only at run time. <c>RenderComponent&lt;T&gt;</c> needs a
    /// compile-time type, which a <c>[TestCase]</c> argument cannot provide.
    /// </summary>
    private IRenderedFragment RenderPage(Type componentType) =>
        Render(builder =>
        {
            builder.OpenComponent(0, componentType);
            builder.CloseComponent();
        });
}
