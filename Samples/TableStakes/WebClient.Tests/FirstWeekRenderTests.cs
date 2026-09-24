using AngleSharp.Dom;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using WebClient.Components.Pages;
using WebClient.Components.Pages.Meta;
using WebClient.Components.Shell;
using WebClientBase.Configuration;

namespace WebClient.Tests;

/// <summary>
/// bUnit render tests for the <c>first-week-reward</c> fixture scenario, in which an earned reward is still
/// unclaimed after its day ended. The tests check the day tiles, the Events hub card, Home's next-up card and the
/// Events tab badge. That the state survives a live session arriving is tested in <c>LiveServerFirstWeekTests</c>.
/// </summary>
[TestFixture]
public class FirstWeekRenderTests : BunitPageTest
{
    [Test]
    public void TheOwedRewardWeekDrawsItsTiles()
    {
        Setup("/events/first-week", "first-week-reward");
        IRenderedComponent<FirstWeekPage> page = RenderComponent<FirstWeekPage>();

        // Day two ended with its reward unclaimed, so the tile still shows the reward and its Claim button.
        IElement dayTwo = DayTile(page, 2);
        Assert.That(dayTwo.QuerySelector("[data-testid=\"first-week-day-state\"]")!.TextContent,
            Is.EqualTo("Reward ready"));
        Assert.That(dayTwo.QuerySelectorAll("[data-testid=\"first-week-day-claim\"]"), Has.Exactly(1).Items);

        // Day three ended with its goals unfinished, so the tile shows it as missed, with no Claim button.
        IElement dayThree = DayTile(page, 3);
        Assert.That(dayThree.QuerySelector("[data-testid=\"first-week-day-state\"]")!.TextContent,
            Is.EqualTo("Missed · This day has ended"));
        Assert.That(dayThree.QuerySelectorAll("[data-testid=\"first-week-day-claim\"]"), Is.Empty);
    }

    [Test]
    public void TheHubCardOffersTheClaim()
    {
        Setup("/events", "first-week-reward");
        IRenderedComponent<EventsPage> page = RenderComponent<EventsPage>();

        // The card counts the unclaimed reward from an earlier day, so its button says Claim.
        IElement card = page.Find("[data-testid=\"feature-firstweekevent\"]");
        Assert.That(card.TextContent, Does.Contain("Claim"));
    }

    [Test]
    public void HomePromotesTheOwedReward()
    {
        Setup("/", "first-week-reward");
        IRenderedComponent<Home> page = RenderComponent<Home>();

        Assert.That(page.Find("[data-testid=\"next-up-headline\"]").TextContent,
            Is.EqualTo("A first-week reward is waiting"));
    }

    /// <summary>
    /// The Events tab carries a badge for the unclaimed reward. <see cref="PrimaryNav"/> is not part of any page,
    /// so it is rendered on its own. <see cref="BadgePolicyTests"/> covers which states earn a badge.
    /// </summary>
    [Test]
    public void TheEventsTabCarriesABadge()
    {
        Setup("/events", "first-week-reward");
        IRenderedComponent<PrimaryNav> nav = RenderComponent<PrimaryNav>();

        Assert.That(nav.Find("[data-testid=\"nav-events\"]").QuerySelectorAll("[data-testid=\"badge\"]"),
            Has.Exactly(1).Items);
    }

    /// <summary>Returns the tile for day <paramref name="day"/>.</summary>
    private static IElement DayTile(IRenderedComponent<FirstWeekPage> page, int day) =>
        page.FindAll("[data-testid=\"first-week-day\"]")
            .Single(el => el.TextContent.Contains($"Day {day} ·", StringComparison.Ordinal));
}
