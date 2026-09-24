using AngleSharp.Dom;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using WebClient.Components.Pages.Meta;

namespace WebClient.Tests;

/// <summary>
/// bUnit render tests for the Events hub: Daily Reward and the First-Week Event are separate cards with their own
/// names and routes. Navigation through the real router is tested in <c>ShellPageTests</c>.
/// </summary>
[TestFixture]
public class EventsHubRenderTests : BunitPageTest
{
    [Test]
    public void DailyRewardAndTheFirstWeekEventAreDistinctCardsWithTheirOwnRoutes()
    {
        Setup("/events");

        IRenderedComponent<EventsPage> page = RenderComponent<EventsPage>();

        IElement dailyCard = page.Find("[data-testid=\"feature-dailyreward\"]");
        IElement firstWeekCard = page.Find("[data-testid=\"feature-firstweekevent\"]");

        // Each card uses only its own feature's name.
        Assert.That(dailyCard.TextContent, Does.Contain("Daily reward"));
        Assert.That(dailyCard.TextContent, Does.Not.Contain("First-week event"));
        Assert.That(firstWeekCard.TextContent, Does.Contain("First-week event"));
        Assert.That(firstWeekCard.TextContent, Does.Not.Contain("Daily reward"));

        // A hub card is not itself a control and contains exactly one button, its action button.
        Assert.That(dailyCard.TagName, Is.EqualTo("DIV"), "the daily reward card must not be a control");
        Assert.That(dailyCard.QuerySelectorAll("button").Length, Is.EqualTo(1));
        Assert.That(firstWeekCard.QuerySelectorAll("button").Length, Is.EqualTo(1));

        // Each card's button navigates to that feature's page.
        FakeNavigationManager nav = (FakeNavigationManager)Services.GetRequiredService<NavigationManager>();

        page.Find("[data-testid=\"feature-dailyreward-action\"]").Click();
        Assert.That(nav.Uri, Does.EndWith("/events/daily"));

        page.Find("[data-testid=\"feature-firstweekevent-action\"]").Click();
        Assert.That(nav.Uri, Does.EndWith("/events/first-week"));
    }
}
