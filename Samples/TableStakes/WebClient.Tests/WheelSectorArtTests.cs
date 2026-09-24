using WebClient.Meta;

namespace WebClient.Tests;

/// <summary>
/// <see cref="WheelSectorArt.IconIdFor"/>: the icon drawn on each wheel sector. No browser or server is needed.
/// </summary>
[TestFixture]
public class WheelSectorArtTests
{
    /// <summary>
    /// The empty sector shows the skull icon. A spin-again sector pays in spin tokens, but it shows the respin icon,
    /// matching its label: the tier is checked before the currency. A paying sector shows its currency's icon, the
    /// same icon the wallet uses, and the jackpot is a coin prize drawn golden, not a currency of its own.
    /// </summary>
    [TestCase(null,                    0L,     WheelTier.Nothing,   "skull")]
    [TestCase(CurrencyKind.SpinTokens, 1L,     WheelTier.SpinAgain, "respin")]
    [TestCase(CurrencyKind.Coins,      100L,   WheelTier.Common,    "coin")]
    [TestCase(CurrencyKind.Gems,       30L,    WheelTier.Premium,   "gem")]
    [TestCase(CurrencyKind.Coins,      1_000L, WheelTier.Rare,      "coin")]
    public void EachSectorCarriesItsIcon(CurrencyKind? currency, long amount, WheelTier tier, string expected)
    {
        Assert.That(WheelSectorArt.IconIdFor(new WheelSectorView(0, currency, amount, tier)), Is.EqualTo(expected));
    }
}
