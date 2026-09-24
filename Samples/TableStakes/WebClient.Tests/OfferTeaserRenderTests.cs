using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using WebClient.Components.Meta;
using WebClient.Meta;
using WebClient.Meta.Fixtures;

namespace WebClient.Tests;

/// <summary>
/// The <see cref="OfferTeaser"/> component on Home: it draws the offer with the price above the View button in the
/// aside column, and reports a View tap through <c>OnOpen</c>. These tests check the markup structure that the
/// stylesheet attaches to. Widths and text wrapping are the stylesheet's and are not tested here.
/// </summary>
[TestFixture]
public class OfferTeaserRenderTests : BunitPageTest
{
    /// <summary>The default fixture's featured offer, which has a demo (real-money) price.</summary>
    private static OfferView FeaturedOffer =>
        MetaFixtures.Build(MetaFixtures.Default, TimeSpan.Zero).Shop.Featured
        ?? throw new InvalidOperationException("The default fixture has no featured offer.");

    /// <summary>The default fixture's first catalogue offer, which has a wallet (gem) price.</summary>
    private static OfferView WalletPricedOffer =>
        MetaFixtures.Build(MetaFixtures.Default, TimeSpan.Zero).Shop.Catalogue[0];

    /// <summary>
    /// The teaser draws the eyebrow, title and tagline, and puts the price chip before the View button inside
    /// <c>.m-teaser__aside</c>, which the stylesheet stacks vertically.
    /// </summary>
    [Test]
    public void TheAsideStacksThePriceAboveTheViewButton()
    {
        Setup("/shop");
        IRenderedComponent<OfferTeaser> component = RenderComponent<OfferTeaser>(
            parameters => parameters.Add(p => p.Offer, FeaturedOffer));

        IElement aside = component.Find(".m-teaser__aside");

        string asideHtml = aside.InnerHtml;
        int price  = asideHtml.IndexOf("data-testid=\"offer-teaser-price\"", StringComparison.Ordinal);
        int action = asideHtml.IndexOf("data-testid=\"offer-teaser-action\"", StringComparison.Ordinal);

        Assert.Multiple(() =>
        {
            Assert.That(component.Find("[data-testid=\"offer-teaser\"]"), Is.Not.Null, "the teaser card");
            Assert.That(component.Find("[data-testid=\"offer-teaser-eyebrow\"]").TextContent, Is.EqualTo("Just for you"));
            Assert.That(component.Find(".m-card__title").TextContent, Is.EqualTo("Gem Booster Pack"));
            Assert.That(component.Find(".m-card__note").TextContent, Is.EqualTo("Power up your game."));
            Assert.That(price, Is.GreaterThanOrEqualTo(0), "the price chip is drawn in the aside");
            Assert.That(action, Is.GreaterThan(price), "the View button is drawn after the price");
            Assert.That(component.Find("[data-testid=\"offer-teaser-price\"]").TextContent, Is.EqualTo("Demo $4.99"));
            Assert.That(component.Find("[data-testid=\"offer-teaser-action\"]").TextContent, Is.EqualTo("View"));
        });
    }

    /// <summary>
    /// A wallet price chip shows the currency icon next to the amount, as the shop tiles do, instead of a bare
    /// number. A demo price has no currency and shows its text instead.
    /// </summary>
    [Test]
    public void WalletPriceChipNamesItsCurrencyWithTheIconBesideTheAmount()
    {
        Setup("/shop");
        IRenderedComponent<OfferTeaser> component = RenderComponent<OfferTeaser>(
            parameters => parameters.Add(p => p.Offer, WalletPricedOffer));

        IElement chip = component.Find("[data-testid=\"offer-teaser-price\"]");

        Assert.Multiple(() =>
        {
            Assert.That(chip.QuerySelector(".m-icon"), Is.Not.Null, "the currency icon is inside the chip");
            Assert.That(chip.TextContent, Does.Contain("500"), "the formatted amount is beside the icon");
            Assert.That(chip.TextContent, Does.Not.Contain("Demo"), "a wallet price is not the demo string");
        });
    }

    /// <summary>
    /// The component does not navigate itself. It reports the tap to <c>OnOpen</c>, and Home decides where to go.
    /// </summary>
    [Test]
    public void TappingViewInvokesOnOpenOnce()
    {
        Setup("/shop");
        int opens = 0;
        IRenderedComponent<OfferTeaser> component = RenderComponent<OfferTeaser>(parameters => parameters
            .Add(p => p.Offer, FeaturedOffer)
            .Add(p => p.OnOpen, EventCallback.Factory.Create(this, () => opens++)));

        component.Find("[data-testid=\"offer-teaser-action\"]").Click();

        Assert.That(opens, Is.EqualTo(1));
    }
}
