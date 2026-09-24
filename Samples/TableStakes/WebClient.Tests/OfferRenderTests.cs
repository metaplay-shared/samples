using Bunit;
using WebClient.Components.Meta;
using WebClient.Meta;

namespace WebClient.Tests;

/// <summary>
/// The <see cref="OfferCard"/> card for a wallet-priced offer the player cannot buy. There are two such states, each
/// with its own message: the balance is too small to pay the price, or a balance is too full to receive the
/// offer's contents. Neither state draws a Buy button.
/// <para>
/// The server charges the price and grants the contents in one transaction and refuses the purchase in both
/// states (<c>docs/offers.md</c>, "Wallet-priced offers"), so a Buy button would only lead to a failure that a
/// retry cannot fix.
/// </para>
/// </summary>
[TestFixture]
public class OfferRenderTests : BunitPageTest
{
    /// <summary>
    /// A gem-priced offer that grants spin tokens. A non-null <paramref name="blockedBy"/> marks that balance as full.
    /// </summary>
    private static OfferView Bundle(CurrencyKind? blockedBy = null) =>
        new OfferView(
            Id:           "lucky-spin-bundle",
            Title:        "Lucky Spin Bundle",
            Tagline:      "Five spins and a pile of coins, paid for in gems.",
            Contents:     RewardView.Of(RewardViewItem.Of(CurrencyKind.SpinTokens, 5)),
            Price:        OfferPrice.In(CurrencyKind.Gems, 500),
            Remaining:    null,
            Availability: OfferAvailability.Available,
            LockReason:   "",
            BonusPercent: null,
            IsTargeted:   false,
            IconId:       "wheel",
            BlockedBy:    blockedBy);

    /// <summary>A wallet with exactly the gems the <see cref="Bundle"/> price needs.</summary>
    private static readonly WalletView Funded = new WalletView(0, 500, 0);

    private IRenderedComponent<OfferCard> Render(OfferView item, WalletView wallet)
    {
        Setup("/shop");
        return RenderComponent<OfferCard>(parameters => parameters
            .Add(p => p.Offer, item)
            .Add(p => p.Wallet, wallet));
    }

    /// <summary>Baseline: when the wallet covers the price and no balance is full, the card draws Buy.</summary>
    [Test]
    public void AnOfferThatWouldSettleDrawsBuy()
    {
        IRenderedComponent<OfferCard> card = Render(Bundle(), Funded);

        Assert.Multiple(() =>
        {
            Assert.That(card.FindAll("[data-testid=\"offer-buy\"]"), Has.Count.EqualTo(1));
            Assert.That(card.FindAll("[data-testid=\"offer-wallet-full\"]"), Is.Empty);
            Assert.That(card.FindAll("[data-testid=\"offer-unaffordable\"]"), Is.Empty);
        });
    }

    /// <summary>
    /// The wallet covers the price, but granting the contents would take a balance past its cap, so the server
    /// would refuse the purchase. The card names the full balance instead of saying "Not enough Gems".
    /// </summary>
    [Test]
    public void AnOfferWhoseGrantWouldPassACapNamesTheFullBalanceAndDrawsNoBuy()
    {
        IRenderedComponent<OfferCard> card = Render(Bundle(CurrencyKind.SpinTokens), Funded);

        Assert.Multiple(() =>
        {
            Assert.That(card.FindAll("[data-testid=\"offer-buy\"]"), Is.Empty, "a Buy the server would refuse");
            Assert.That(card.Find("[data-testid=\"offer-wallet-full\"]").TextContent.Trim(), Is.EqualTo("Spin Tokens full"));
            Assert.That(card.Find("[data-testid=\"offer-wallet-full-note\"]").TextContent, Does.Contain("Spin Tokens balance is full"));
            Assert.That(card.FindAll("[data-testid=\"offer-unaffordable\"]"), Is.Empty, "the player can afford the price");
            Assert.That(card.Find("[data-testid=\"offer-price\"]").TextContent, Does.Contain("500"), "the price stays on the card");
        });
    }

    /// <summary>
    /// When the wallet is both short and full, the card shows the shortfall. The server checks the price before the
    /// grant, so this is the same reason the server would give.
    /// </summary>
    [Test]
    public void AShortfallIsTheStateShownWhenTheWalletIsBothShortAndFull()
    {
        IRenderedComponent<OfferCard> card = Render(Bundle(CurrencyKind.SpinTokens), WalletView.Empty);

        Assert.Multiple(() =>
        {
            Assert.That(card.FindAll("[data-testid=\"offer-buy\"]"), Is.Empty);
            Assert.That(card.Find("[data-testid=\"offer-unaffordable\"]").TextContent, Does.Contain("Not enough Gems"));
            Assert.That(card.FindAll("[data-testid=\"offer-wallet-full\"]"), Is.Empty);
        });
    }
}
