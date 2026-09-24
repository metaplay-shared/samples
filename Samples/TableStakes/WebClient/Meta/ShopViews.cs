namespace WebClient.Meta;

/// <summary>The kind of price an offer has. The two kinds are presented differently.</summary>
public enum OfferPriceKind
{
    /// <summary>Priced in an in-game currency and paid from the player's wallet.</summary>
    Currency,

    /// <summary>
    /// Stands in for a real-money price. This sample charges no real money, so the card and the purchase result
    /// are clearly marked as a demo.
    /// </summary>
    Demo,
}

/// <summary>An offer's price. <see cref="Currency"/> and <see cref="Amount"/> are ignored for a demo price.</summary>
public sealed record OfferPrice(OfferPriceKind Kind, CurrencyKind Currency, long Amount, string DemoText)
{
    public static OfferPrice In(CurrencyKind currency, long amount) =>
        new OfferPrice(OfferPriceKind.Currency, currency, amount, "");

    public static OfferPrice Demo(string text) =>
        new OfferPrice(OfferPriceKind.Demo, CurrencyKind.Gems, 0, text);
}

/// <summary>Whether an offer can be bought right now, and if not, why.</summary>
public enum OfferAvailability
{
    Available,

    /// <summary>Already bought, and cannot be bought again.</summary>
    Purchased,

    /// <summary>The stock or per-player limit is used up. The card says when it returns.</summary>
    SoldOut,

    /// <summary>The offer's time window has closed.</summary>
    Expired,

    /// <summary>Requires something the player has not done. The card says what.</summary>
    Locked,

    /// <summary>Not offered to this player's segment.</summary>
    Unavailable,
}

/// <summary>One offer, with the data for both the featured card and a catalogue tile.</summary>
/// <param name="BlockedBy">
/// The currency whose balance this offer's contents would push over its cap, or null if the purchase fits. The
/// card uses it to tell the player which currency to spend first (<c>docs/offers.md</c>, "Wallet-priced offers").
/// </param>
public sealed record OfferView(
    string            Id,
    string            Title,
    string            Tagline,
    RewardView        Contents,
    OfferPrice        Price,
    TimeSpan?         Remaining,
    OfferAvailability Availability,
    string            LockReason,
    int?              BonusPercent,
    bool              IsTargeted,
    string            IconId,
    CurrencyKind?     BlockedBy = null)
{
    public bool CanBuy => Availability == OfferAvailability.Available;

    /// <summary>
    /// The line under the title on a card that also shows a lock chip. It is empty when the offer is locked and
    /// <see cref="LockReason"/> equals <see cref="Tagline"/>, so the card does not show the same sentence twice.
    /// Surfaces without a lock chip show <see cref="Tagline"/> instead.
    /// </summary>
    public string Note => Availability == OfferAvailability.Locked && LockReason == Tagline ? "" : Tagline;

    /// <summary>
    /// How much more of the price currency the player needs, or zero if they can pay. Demo prices return zero.
    /// <para>
    /// The card uses this to show that the offer is unaffordable instead of offering Buy. The server would refuse
    /// the purchase with <c>CannotAfford</c>, and retrying could not succeed (<c>docs/economy.md</c>,
    /// "ApplyWallet").
    /// </para>
    /// </summary>
    public long ShortfallAgainst(WalletView wallet) =>
        Price.Kind == OfferPriceKind.Currency
            ? Math.Max(0, Price.Amount - wallet.AmountOf(Price.Currency))
            : 0;

    /// <summary>
    /// The label above the card title. "Just for you" is shown only when <see cref="IsTargeted"/> is true, that
    /// is, when the offer was chosen for the player's segment.
    /// </summary>
    public string Eyebrow => IsTargeted ? "Just for you" : "Featured offer";
}

/// <summary>
/// The shop: one featured offer and a catalogue. The shop never opens automatically (docs/meta-shell.md).
/// </summary>
public sealed record ShopView(
    ActivityState            State,
    OfferView?               Featured,
    IReadOnlyList<OfferView> Catalogue) : IFeatureView
{
    public MetaFeature Feature          => MetaFeature.Shop;
    public double?     Progress         => null;
    public TimeSpan    EndingSoonWithin => Countdown.EndingSoonThreshold;

    /// <summary>The time until the soonest expiry among offers that can be bought, or null if none expires.</summary>
    public TimeSpan? UntilDeadline
    {
        get
        {
            IEnumerable<TimeSpan> clocks = All()
                .Where(o => o.CanBuy && o.Remaining.HasValue)
                .Select(o => o.Remaining!.Value);

            return clocks.Any() ? clocks.Min() : null;
        }
    }

    public IEnumerable<OfferView> All()
    {
        if (Featured != null)
            yield return Featured;
        foreach (OfferView offer in Catalogue)
            yield return offer;
    }

    /// <summary>
    /// Targeted, buyable offers the player has not seen. <see cref="BadgePolicy"/> badges the shop when this is
    /// non-empty, so the badge clears once the player views the offers.
    /// </summary>
    public IEnumerable<OfferView> UnseenTargeted(IReadOnlyCollection<string> seenOfferIds) =>
        All().Where(o => o.IsTargeted && o.CanBuy && !seenOfferIds.Contains(o.Id));
}
