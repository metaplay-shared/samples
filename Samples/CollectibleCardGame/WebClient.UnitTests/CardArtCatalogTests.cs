using WebClient.Services;
using Game.Logic;

namespace WebClient.UnitTests;

public sealed class CardArtCatalogTests
{
    [Test]
    public void UnknownOverTheAirCardUsesACompleteClanFallback()
    {
        CardArtSet art = CardArtCatalog.Resolve(CardId.FromString("FutureCard"), "Tidepool", CardType.Trick);

        Assert.Multiple(() =>
        {
            Assert.That(art.IllustrationSrc, Is.Null);
            Assert.That(art.BackdropSrc, Is.EqualTo("art/cards/background-tidal-eye.webp"));
            Assert.That(art.Treatment, Is.EqualTo(CardArtTreatment.Fallback));
            Assert.That(art.CompositionClass, Is.EqualTo("card-art-fallback-trick"));
            Assert.That(art.IsFallback, Is.True);
        });
    }

    [TestCase("Kitsune", "split-flame")]
    [TestCase("Tidepool", "tidal-eye")]
    [TestCase("Mossback", "rising-rings")]
    [TestCase("Sunny", "hearth-knot")]
    [TestCase("Moonlight", "returning-crescent")]
    [TestCase("Wanderer", "waystar")]
    [TestCase("AClanAddedLater", "waystar")]
    public void ClansMapToNameIndependentSymbols(string clanId, string expected)
    {
        Assert.That(CardArtCatalog.ClanSymbolSlug(clanId), Is.EqualTo(expected));
    }
}
