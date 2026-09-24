using AngleSharp.Dom;
using Bunit;
using WebClient.Components.TableUI;

namespace WebClient.Tests;

/// <summary>
/// bUnit render tests for the <c>FloatingText</c> component: the markup the stylesheet selects on, the CSS custom
/// properties for duration and stack position, and removal of the label when its lifetime ends. A label left in
/// the DOM after its animation would stay invisible over the table.
/// </summary>
[TestFixture]
public class FloatingTextRenderTests : BunitPageTest
{
    /// <summary>
    /// The trump variant renders its modifier class, a matching <c>data-variant</c>, and the label text. The duration
    /// is passed to the stylesheet in the <c>--ts-float-ms</c> custom property, which the animation reads.
    /// </summary>
    [Test]
    public void TheTrumpVariantCarriesItsClassVariantTextAndWindow()
    {
        IRenderedComponent<FloatingText> cut = RenderComponent<FloatingText>(
            parameters => parameters
                .Add(p => p.Text, "TRUMP CARD!")
                .Add(p => p.Variant, FloatingTextVariant.Trump));

        IElement label = cut.Find(".ts-float--trump[data-variant=Trump] .ts-float__text");

        Assert.That(label.TextContent, Is.EqualTo("TRUMP CARD!"));
        Assert.That(cut.Find(".ts-float").GetAttribute("style"), Does.Contain("--ts-float-ms:900ms"),
            "the default window is the stylesheet's default too, so it is declared rather than implied");
    }

    /// <summary>
    /// The point variant is the default. It is hidden from assistive technology, because the score it celebrates is
    /// already shown and announced elsewhere.
    /// </summary>
    [Test]
    public void ThePointVariantIsTheDefaultAndIsHiddenFromAssistiveTechnology()
    {
        IRenderedComponent<FloatingText> cut = RenderComponent<FloatingText>(
            parameters => parameters.Add(p => p.Text, "+1 point!"));

        IElement label = cut.Find(".ts-float--point[data-variant=Point]");

        Assert.That(label.GetAttribute("aria-hidden"), Is.EqualTo("true"));
        Assert.That(label.GetAttribute("data-testid"), Is.EqualTo("floating-text"));
        Assert.That(label.TextContent, Is.EqualTo("+1 point!"));
    }

    /// <summary>The stack position is passed in the <c>--ts-float-stack</c> custom property, in rem, so labels on
    /// the same anchor do not overlap.</summary>
    [Test]
    public void AStackPositionTravelsAsTheStylesheetsCustomProperty()
    {
        IRenderedComponent<FloatingText> cut = RenderComponent<FloatingText>(
            parameters => parameters
                .Add(p => p.Text, "+1 point!")
                .Add(p => p.StackIndex, 2));

        Assert.That(cut.Find(".ts-float").GetAttribute("style"), Does.Contain("--ts-float-stack:2.2rem"));
    }

    /// <summary>
    /// The component renders nothing once its lifetime ends. The test uses a short duration so it does not wait
    /// for the default one.
    /// </summary>
    [Test]
    public void TheLabelRemovesItselfWhenItsLifetimeIsOut()
    {
        IRenderedComponent<FloatingText> cut = RenderComponent<FloatingText>(
            parameters => parameters
                .Add(p => p.Text, "+1 point!")
                .Add(p => p.DurationMs, 20));

        Assert.That(cut.Markup, Is.Not.Empty);

        cut.WaitForState(() => string.IsNullOrEmpty(cut.Markup), timeout: TimeSpan.FromSeconds(3));
    }
}
