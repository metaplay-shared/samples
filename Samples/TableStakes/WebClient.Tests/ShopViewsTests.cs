using WebClient.Meta;

namespace WebClient.Tests;

/// <summary>
/// <see cref="OfferView"/> logic that needs no browser or server: the shortfall against the wallet, the eyebrow
/// label, and when the description note is hidden.
/// <para>
/// The live tests only reach the states that the sample's catalogue and starting wallet produce. These tests
/// cover states they cannot, such as an exactly affordable price, an empty wallet, and a lock reason that
/// differs from the offer's description.
/// </para>
/// </summary>
[TestFixture]
public class ShopViewsTests
{
    private static OfferView Offer(
        OfferPrice        price,
        OfferAvailability availability = OfferAvailability.Available,
        string            tagline      = "Five spins and a pile of coins, paid for in gems.",
        string            lockReason   = "",
        bool              isTargeted   = false) =>
        new OfferView(
            Id:           "lucky-spin-bundle",
            Title:        "Lucky Spin Bundle",
            Tagline:      tagline,
            Contents:     RewardView.Of(RewardViewItem.Of(CurrencyKind.Coins, 1500)),
            Price:        price,
            Remaining:    null,
            Availability: availability,
            LockReason:   lockReason,
            BonusPercent: null,
            IsTargeted:   isTargeted,
            IconId:       "wheel");

    /// <summary>
    /// <see cref="OfferView.ShortfallAgainst"/> lets the card show "cannot afford" before the player taps. A balance
    /// equal to the price has no shortfall, because the server allows a purchase that spends the balance to zero.
    /// With an empty wallet the shortfall is the whole price: showing the offer as unaffordable is safer than showing
    /// a Buy button for a purchase the server would refuse. Only the balance in the price's currency counts.
    /// </summary>
    [TestCase(0L,      100L, 0L,      400L)]
    [TestCase(0L,      499L, 0L,      1L)]
    [TestCase(0L,      500L, 0L,      0L)]
    [TestCase(0L,      900L, 0L,      0L)]
    [TestCase(0L,      0L,   0L,      500L)]
    [TestCase(99_999L, 0L,   99_999L, 500L)]
    public void AWalletPriceIsShortByWhatTheBalanceDoesNotCover(long coins, long gems, long spinTokens, long expected)
    {
        OfferView offer = Offer(OfferPrice.In(CurrencyKind.Gems, 500));

        Assert.That(offer.ShortfallAgainst(new WalletView(coins, gems, spinTokens)), Is.EqualTo(expected));
    }

    /// <summary>
    /// A demo (real-money) price has no shortfall. It is not paid from the wallet: the SDK validates the purchase
    /// and the server grants the contents.
    /// </summary>
    [Test]
    public void ADemoPriceIsNeverShort()
    {
        Assert.That(Offer(OfferPrice.Demo("$4.99")).ShortfallAgainst(WalletView.Empty), Is.Zero);
    }

    /// <summary>
    /// <see cref="OfferView.Note"/> is empty only when the offer is locked and its lock reason is the same text as
    /// its tagline, so the card does not print the same sentence twice. A different lock reason keeps the tagline.
    /// </summary>
    [Test]
    public void TheNoteStepsAsideOnlyForAChipRepeatingIt()
    {
        const string Tagline = "Unlocked once your Starter Pack has run its course.";

        Assert.That(Offer(OfferPrice.In(CurrencyKind.Coins, 750), OfferAvailability.Locked,
                          tagline: Tagline, lockReason: Tagline).Note,
            Is.Empty);

        Assert.That(Offer(OfferPrice.In(CurrencyKind.Coins, 750), OfferAvailability.Locked,
                          tagline: "Five spins and a pile of coins.", lockReason: Tagline).Note,
            Is.EqualTo("Five spins and a pile of coins."));

        // Only a locked offer shows the lock chip, so in every other state the note keeps the tagline.
        foreach (OfferAvailability availability in new[]
                 {
                     OfferAvailability.Available, OfferAvailability.Purchased,
                     OfferAvailability.SoldOut, OfferAvailability.Expired, OfferAvailability.Unavailable,
                 })
        {
            Assert.That(Offer(OfferPrice.In(CurrencyKind.Coins, 750), availability,
                              tagline: Tagline, lockReason: Tagline).Note,
                Is.EqualTo(Tagline), availability.ToString());
        }
    }

    /// <summary>
    /// Only a targeted offer gets the "Just for you" eyebrow. The shop's featured card and Home's teaser both read
    /// <see cref="OfferView.Eyebrow"/>, so they always show the same label.
    /// </summary>
    [Test]
    public void OnlyATargetedOfferClaimsToBeForThisPlayer()
    {
        Assert.That(Offer(OfferPrice.In(CurrencyKind.Gems, 500), isTargeted: true).Eyebrow,
            Is.EqualTo("Just for you"));
        Assert.That(Offer(OfferPrice.In(CurrencyKind.Gems, 500), isTargeted: false).Eyebrow,
            Is.EqualTo("Featured offer"));
    }
}
