using AngleSharp.Dom;
using Bunit;
using WebClient.Components.Pages.Meta;

namespace WebClient.Tests;

/// <summary>
/// bUnit render tests for the daily reward page and its Events hub card in the closed states. That the state
/// survives a live session arriving is tested in <c>LiveServerDailyRewardTests</c>.
/// </summary>
[TestFixture]
public class DailyRewardRenderTests : BunitPageTest
{
    [TestCase("daily-closed", "state-ready",       "No reward open right now")]
    [TestCase("daily-ended",  "state-expired",     "The daily reward has ended")]
    [TestCase("unpublished",  "state-unavailable", "No daily reward right now")]
    public void AClosedDailyRewardDrawsItsOwnState(string scenario, string block, string title)
    {
        Setup("/events/daily", scenario);
        IRenderedComponent<DailyRewardPage> page = RenderComponent<DailyRewardPage>();

        Assert.That(page.Find($"[data-testid=\"{block}\"]").TextContent, Does.Contain(title));

        // A closed reward shows neither the claim button nor the claimed text.
        Assert.That(page.FindAll("[data-testid=\"daily-claim\"]"), Is.Empty);
        Assert.That(page.FindAll("[data-testid=\"daily-claimed\"]"), Is.Empty);
    }

    [TestCase("daily-closed", "Nothing open right now")]
    [TestCase("daily-ended",  "The cycle has ended")]
    [TestCase("unpublished",  "Nothing scheduled")]
    public void TheHubCardSaysWhatTheScreenSays(string scenario, string note)
    {
        Setup("/events", scenario);
        IRenderedComponent<EventsPage> page = RenderComponent<EventsPage>();

        IElement card = page.Find("[data-testid=\"feature-dailyreward\"]");
        Assert.That(card.TextContent, Does.Contain(note));
        Assert.That(card.TextContent, Does.Not.Contain("Claimed — come back tomorrow"));
    }
}
