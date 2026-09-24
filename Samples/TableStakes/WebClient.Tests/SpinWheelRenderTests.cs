using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using WebClient.Components.Meta;
using WebClient.Components.Pages.Meta;

namespace WebClient.Tests;

/// <summary>
/// bUnit render tests for <see cref="SpinWheelPage"/> with fixture data: the odds sheet, and the closed state when
/// the config has no wheel table.
/// <para>
/// The fixture's wheel table is a hand-written copy of the one in the game config, so these tests check the page's
/// drawing, not the published config. <c>LiveServerSpinWheelTests</c> checks against a live server that the sheet
/// shows the published odds (<c>ThePublishedOddsAreOnTheSheet</c>) and that the closed state stays closed once a
/// session connects (<c>AWheelWithNoPublishedTableSurvivesASession</c>).
/// </para>
/// </summary>
[TestFixture]
public class SpinWheelRenderTests : BunitPageTest
{
    /// <summary>
    /// The odds sheet lists every outcome, including the empty one, with a percentage each, and the percentages sum
    /// to 100%. This test covers the sheet's markup and opening and closing it. <c>SpinWheelViewTests</c> covers how
    /// the sectors map to percentages.
    /// </summary>
    [Test]
    public void ThePrizeDetailsListEveryOutcomeAndSumToAHundred()
    {
        Setup("/events/spin", "spin");
        IRenderedComponent<SpinWheelPage> page = RenderComponent<SpinWheelPage>();

        // The sheet is not rendered until the player taps Prize details.
        Assert.That(page.FindAll("[data-testid=\"odds-sheet\"]"), Is.Empty);

        page.Find("[data-testid=\"prize-details\"]").Click();

        Assert.That(page.FindAll("[data-testid=\"odds-row\"]"), Has.Count.EqualTo(7));

        string[] percents = page.FindAll("[data-testid=\"odds-percent\"]")
            .Select(element => element.TextContent.Trim())
            .ToArray();
        Assert.That(percents, Is.EqualTo(new[] { "10%", "20%", "20%", "10%", "10%", "20%", "10%" }));
        Assert.That(page.Find("[data-testid=\"odds-total\"]").TextContent.Trim(), Is.EqualTo("100%"));

        // The empty outcome ("Nothing") has a row too. Without it, the listed percentages would not match the
        // player's real chances.
        string table = page.Find("[data-testid=\"odds-table\"]").TextContent;
        Assert.That(table, Does.Contain("1,000 Coins"));
        Assert.That(table, Does.Contain("30 Gems"));
        Assert.That(table, Does.Contain("1 Spin Token"));
        Assert.That(table, Does.Contain("Nothing"));

        // The note states the spin cost and that the result is decided before the animation plays.
        Assert.That(page.Find("[data-testid=\"odds-sheet\"]").TextContent, Does.Contain(
            "Each spin costs 1 spin token. The result is chosen when you spin; the animation only shows it."));

        page.Find("[data-testid=\"sheet-close\"]").Click();
        Assert.That(page.FindAll("[data-testid=\"odds-sheet\"]"), Is.Empty);
    }

    /// <summary>
    /// When the config has no usable wheel table, the page shows the closed state and no wheel. The sectors, the odds
    /// sheet and the teaser all come from that table, so drawing any of them would show prizes that do not exist.
    /// </summary>
    [Test]
    public void AWheelWithNoPublishedTableDrawsNoWheel()
    {
        Setup("/events/spin", "unpublished");
        IRenderedComponent<SpinWheelPage> page = RenderComponent<SpinWheelPage>();

        Assert.That(page.Find("[data-testid=\"state-unavailable\"]").TextContent,
            Does.Contain("The wheel is closed"));

        // No spin button, no token count, no Prize details button and no odds sheet.
        Assert.That(page.FindAll("[data-testid=\"spin\"]"), Is.Empty);
        Assert.That(page.FindAll("[data-testid=\"spin-available\"]"), Is.Empty);
        Assert.That(page.FindAll("[data-testid=\"prize-details\"]"), Is.Empty);
        Assert.That(page.FindAll("[data-testid=\"odds-sheet\"]"), Is.Empty);
    }

    /// <summary>
    /// Blazor keeps the page when a navigation changes only the query, so the <c>spinMs</c> knob must be read again
    /// when the parameters are set, not once when the page is created.
    /// </summary>
    [Test]
    public void ASpinDurationKnobChangedByAQueryOnlyNavigationTakesEffect()
    {
        Setup("/events/spin", "spin");
        NavigationManager nav = Services.GetRequiredService<NavigationManager>();

        nav.NavigateTo("http://localhost/events/spin?meta=spin&spinMs=0");
        IRenderedComponent<SpinWheelPage> page = RenderComponent<SpinWheelPage>();
        Assert.That(page.FindComponent<PrizeWheel>().Instance.SpinMs, Is.EqualTo(0));

        nav.NavigateTo("http://localhost/events/spin?meta=spin&spinMs=5000");
        page.SetParametersAndRender();
        Assert.That(page.FindComponent<PrizeWheel>().Instance.SpinMs, Is.EqualTo(5000));
    }
}
