using Game.Client.Services;
using Game.Logic;

namespace Game.Client.Tests;

/// <summary> Every shipped config identity, including tokens, has a real illustration in the client. </summary>
public sealed class CollectionArtCoverageTests
{
    [Test]
    public void EveryConfiguredCardHasAnExistingIllustrationWithTheCorrectTreatment()
    {
        string root = Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "../../../.."));
        string[] rows = File.ReadAllLines(Path.Combine(root, "GameConfigSource/Cards.csv"));
        int count = 0;
        foreach (string row in rows.Skip(1).Where(row => !string.IsNullOrWhiteSpace(row)))
        {
            // Identity, display name, clan and type are the leading unquoted fields in the source sheet.
            string[] cells = row.Split(',');
            CardType type = Enum.Parse<CardType>(cells[3]);
            CardArtSet art = CardArtCatalog.Resolve(CardId.FromString(cells[0]), cells[2], type);
            Assert.Multiple(() =>
            {
                Assert.That(art.IsFallback, Is.False, cells[0]);
                Assert.That(art.Treatment, Is.EqualTo(type == CardType.Critter
                    ? CardArtTreatment.CritterCutout : CardArtTreatment.FullBleed), cells[0]);
                Assert.That(File.Exists(Path.Combine(root, "Client/wwwroot", art.BackdropSrc)), Is.True,
                    $"{cells[0]} backdrop: {art.BackdropSrc}");
                if (type == CardType.Critter)
                {
                    Assert.That(art.IllustrationSrc, Is.Not.Null, cells[0]);
                    Assert.That(File.Exists(Path.Combine(root, "Client/wwwroot", art.IllustrationSrc!)), Is.True,
                        $"{cells[0]} illustration: {art.IllustrationSrc}");
                }
            });
            count++;
        }
        Assert.That(count, Is.EqualTo(67), "The coverage check must include collectible and token cards.");
    }
}
