using AngleSharp.Dom;
using Bunit;
using Game.Logic;
using WebClient.Components.TableUI;

namespace WebClient.Tests;

/// <summary>
/// The markup that the <c>Foil</c> parameter adds to a playing card. The table sets <c>Foil</c> only for a trump
/// played to the centre. The stylesheet and the E2E tests select on this markup.
/// </summary>
[TestFixture]
public class PlayingCardRenderTests : BunitPageTest
{
    /// <summary>A foiled card has the foil overlay with its sheen span, the <c>data-foil</c> attribute, and "trump" in
    /// its accessible name.</summary>
    [Test]
    public void AFoiledCardCarriesTheFoilTheStateAndTheTrumpName()
    {
        IRenderedComponent<PlayingCard> cut = RenderQueenOfHearts(foil: true);

        IElement card = cut.Find(".ts-card--foil[data-foil=true]");

        Assert.That(card.GetAttribute("aria-label"), Is.EqualTo("queen of hearts, trump"));
        Assert.That(cut.Find(".ts-card__foil .ts-card__sheen"), Is.Not.Null,
            "the sheen is the foil's inner span; the stylesheet hangs its idle loop off it");
    }

    /// <summary>A card without foil has no overlay, no <c>data-foil</c> attribute, no foil class, and its plain
    /// accessible name.</summary>
    [Test]
    public void AnUnfoiledCardRendersNeitherTheOverlayNorTheState()
    {
        IRenderedComponent<PlayingCard> cut = RenderQueenOfHearts(foil: false);

        Assert.That(cut.FindAll(".ts-card__foil"), Is.Empty);
        Assert.That(cut.Find(".ts-card").GetAttribute("data-foil"), Is.Null);
        Assert.That(cut.Find(".ts-card").GetAttribute("aria-label"), Is.EqualTo("queen of hearts"));
        Assert.That(cut.Find(".ts-card").ClassList, Does.Not.Contain("ts-card--foil"));
    }

    private IRenderedComponent<PlayingCard> RenderQueenOfHearts(bool foil) =>
        RenderComponent<PlayingCard>(parameters => parameters
            .Add(p => p.Value, new Card(Suit.Hearts, Rank.Queen))
            .Add(p => p.Foil, foil));
}
