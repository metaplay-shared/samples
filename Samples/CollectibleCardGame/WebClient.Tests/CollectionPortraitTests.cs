using Microsoft.Playwright;

namespace WebClient.Tests;

/// <summary> Catalog illustrations reach the collection and detail overlay without changing their controls. </summary>
[TestFixture]
public sealed class CollectionPortraitTests : OfflineTestBase
{
    [TestCase("BuriedAcorn", "FullBleed")]
    [TestCase("EmberKit", "CritterCutout")]
    public async Task KnownCardUsesLoadedIllustrationInGridAndDetail(string cardId, string treatment)
    {
        await GotoOfflineAsync("/collection", "collection-grid");
        ILocator tile = Card(cardId);
        ILocator portrait = tile.Locator(".card-face");
        await portrait.ScrollIntoViewIfNeededAsync();

        await Expect(portrait).ToHaveAttributeAsync("data-art-fallback", "false");
        await Expect(portrait.Locator(".card-face-cutout")).ToHaveCountAsync(treatment == "CritterCutout" ? 1 : 0);
        await Expect(portrait.Locator(".card-portrait-fallback")).ToHaveCountAsync(0);
        await portrait.EvaluateAsync("element => Promise.all([...element.querySelectorAll('img')].map(image => image.decode()))");

        await tile.ClickAsync();
        ILocator detail = Page.GetByTestId("card-detail").Locator(".card-face");
        await Expect(detail).ToBeVisibleAsync();

        await Expect(detail.Locator(".card-face-cutout")).ToHaveCountAsync(treatment == "CritterCutout" ? 1 : 0);
        await detail.EvaluateAsync("element => Promise.all([...element.querySelectorAll('img')].map(image => image.decode()))");
        await detail.EvaluateAsync(
            """
            async element => {
                const animations = [];
                for (let ancestor = element; ancestor; ancestor = ancestor.parentElement)
                    animations.push(...ancestor.getAnimations());
                await Promise.all(animations.filter(animation => animation.effect.getTiming().iterations !== Infinity)
                    .map(animation => animation.finished.catch(() => {})));
            }
            """);
        Assert.That(await detail.EvaluateAsync<int>("element => element.offsetWidth"), Is.GreaterThanOrEqualTo(150),
            "the portrait's layout width must be independent of the dialog's entrance transform");
        LocatorBoundingBoxResult box = (await detail.BoundingBoxAsync())!;
        Assert.That(box.Width, Is.GreaterThanOrEqualTo(150), "detail artwork must remain large enough to read");
    }
}
