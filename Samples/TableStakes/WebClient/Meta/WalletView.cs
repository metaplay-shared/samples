namespace WebClient.Meta;

/// <summary>The currencies in this sample. The HUD shows a balance for each.</summary>
public enum CurrencyKind
{
    Coins,
    Gems,
    SpinTokens,
}

/// <summary>
/// The player's currency balances as the HUD shows them. Balances are formatted with
/// <see cref="Currencies.Format"/>.
/// </summary>
public sealed record WalletView(long Coins, long Gems, long SpinTokens)
{
    public static readonly WalletView Empty = new WalletView(0, 0, 0);

    public long AmountOf(CurrencyKind kind) => kind switch
    {
        CurrencyKind.Coins      => Coins,
        CurrencyKind.Gems       => Gems,
        CurrencyKind.SpinTokens => SpinTokens,
        _                       => 0,
    };

    public WalletView With(CurrencyKind kind, long amount) => kind switch
    {
        CurrencyKind.Coins      => this with { Coins = amount },
        CurrencyKind.Gems       => this with { Gems = amount },
        CurrencyKind.SpinTokens => this with { SpinTokens = amount },
        _                       => this,
    };

    /// <summary>Whether the balance of <paramref name="kind"/> covers <paramref name="price"/>.</summary>
    public bool CanAfford(CurrencyKind kind, long price) => AmountOf(kind) >= price;
}

/// <summary>Currency names and number formatting, shared by the HUD, prices and rewards.</summary>
public static class Currencies
{
    /// <summary>
    /// Maps a shared-code <c>CurrencyType</c> to the shell's <see cref="CurrencyKind"/>, or returns null for a
    /// value the shell does not show.
    /// <para>
    /// <c>None</c> and any currency not listed here return null instead of a default currency, so a new
    /// <c>CurrencyType</c> is left out of the UI until it is added here, instead of being shown as coins. The Shop and
    /// the wheel read null as "no balance is full", so a new currency must be added here in the same change.
    /// <c>WalletViewAndRewardBundleTests</c> fails until it is.
    /// </para>
    /// </summary>
    public static CurrencyKind? KindOf(Game.Logic.CurrencyType currency) => currency switch
    {
        Game.Logic.CurrencyType.Coins      => CurrencyKind.Coins,
        Game.Logic.CurrencyType.Gems       => CurrencyKind.Gems,
        Game.Logic.CurrencyType.SpinTokens => CurrencyKind.SpinTokens,
        _                                  => null,
    };

    public static string NameOf(CurrencyKind kind) => kind switch
    {
        CurrencyKind.Coins      => "Coins",
        CurrencyKind.Gems       => "Gems",
        CurrencyKind.SpinTokens => "Spin Tokens",
        _                       => "",
    };

    /// <summary>
    /// The short currency name for the HUD, which shares its row with the player's name. Reward previews and
    /// prices use <see cref="NameOf(CurrencyKind)"/>.
    /// </summary>
    public static string ShortNameOf(CurrencyKind kind) => kind switch
    {
        CurrencyKind.Coins      => "Coins",
        CurrencyKind.Gems       => "Gems",
        CurrencyKind.SpinTokens => "Spins",
        _                       => "",
    };

    /// <summary>
    /// The currency name for <paramref name="amount"/>: singular when it is 1, such as "1 Spin Token", plural
    /// otherwise.
    /// </summary>
    public static string NameOf(CurrencyKind kind, long amount) => amount == 1
        ? kind switch
        {
            CurrencyKind.Coins      => "Coin",
            CurrencyKind.Gems       => "Gem",
            CurrencyKind.SpinTokens => "Spin Token",
            _                       => "",
        }
        : NameOf(kind);

    /// <summary>
    /// Formats an amount with thousands separators, such as "24,350", instead of abbreviating it, so the player can
    /// compare a balance with a full price.
    /// </summary>
    public static string Format(long amount) => amount.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
}
