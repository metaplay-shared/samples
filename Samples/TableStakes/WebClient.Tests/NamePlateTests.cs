using WebClient.Meta;

namespace WebClient.Tests;

/// <summary>
/// <see cref="NamePlate"/>: the font size step the Profile screen uses for a name. The name plate has a fixed width
/// and a generated name is one word with no spaces, so longer names need a smaller size or they break mid-word.
/// <para>
/// The tests cover the length boundaries, which a screenshot cannot distinguish from a wrong size.
/// </para>
/// </summary>
[TestFixture]
public class NamePlateTests
{
    /// <summary>
    /// Short names use the default (largest) size, which is the base rule and needs no modifier class. Longer names
    /// step down once, and generated adjective-noun-number names at or near
    /// <see cref="Game.Logic.DisplayNamePolicy.MaxLength"/> take the smallest step. The plate renders with an empty
    /// name before the session arrives, and an empty name uses the default size.
    /// </summary>
    [TestCase("",                 NameFit.Roomy,   null)]
    [TestCase("Ace",              NameFit.Roomy,   null)]
    [TestCase("Avery",            NameFit.Roomy,   null)]
    [TestCase("GoldFox17",        NameFit.Roomy,   null)]
    [TestCase("VelvetAce4",       NameFit.Tight,   "m-nameplate--tight")]
    [TestCase("GoldenFox178",     NameFit.Tight,   "m-nameplate--tight")]
    [TestCase("CrimsonSaloon00",  NameFit.Cramped, "m-nameplate--cramped")]
    [TestCase("MidnightQueen123", NameFit.Cramped, "m-nameplate--cramped")]
    public void ALongerNameTakesASmallerStep(string name, NameFit expectedFit, string? expectedClass)
    {
        Assert.That(NamePlate.Fit(name), Is.EqualTo(expectedFit));
        Assert.That(NamePlate.ClassFor(name), Is.EqualTo(expectedClass));
    }

    /// <summary>
    /// Length is counted in user-perceived characters (grapheme clusters), not UTF-16 code units, which is also how
    /// the name length limit is enforced (docs/player.md, "Name rules"). An accent stored as a separate combining
    /// mark does not add to the length.
    /// </summary>
    [Test]
    public void CombiningMarksDoNotCountAgainstTheName()
    {
        // "Joséé" with both accents stored as separate combining marks: seven code units, five characters.
        string decomposed = "Joséé";

        Assert.That(decomposed, Has.Length.EqualTo(7));
        Assert.That(NamePlate.Fit(decomposed), Is.EqualTo(NameFit.Roomy));
    }

    /// <summary>
    /// A name at <see cref="Game.Logic.DisplayNamePolicy.MaxLength"/> gets the smallest size. If the maximum length
    /// grows, this test checks that <see cref="NamePlate.CrampedFrom"/> still covers it.
    /// </summary>
    [Test]
    public void TheLongestPermittedNameIsCovered()
    {
        Assert.That(NamePlate.CrampedFrom, Is.LessThanOrEqualTo(Game.Logic.DisplayNamePolicy.MaxLength));
        Assert.That(NamePlate.Fit(new string('W', Game.Logic.DisplayNamePolicy.MaxLength)),
            Is.EqualTo(NameFit.Cramped));
    }
}
