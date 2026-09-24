using Metaplay.Core.Model;

namespace WebClient.Meta;

/// <summary>
/// One awarded item: a currency amount, or a named non-currency item such as a cosmetic or a chest.
/// <para>
/// Currencies and items share one type because every consumer previews and reveals them together in one list.
/// </para>
/// </summary>
public sealed record RewardViewItem(CurrencyKind? Currency, long Amount, string Label, string IconId)
{
    public static RewardViewItem Of(CurrencyKind currency, long amount) =>
        new RewardViewItem(currency, amount, Currencies.NameOf(currency, amount), IconIdFor(currency));

    public static RewardViewItem Item(string label, string iconId, long amount = 1) =>
        new RewardViewItem(null, amount, label, iconId);

    /// <summary>Whether this item animates to a wallet balance during the reveal. True only for currencies.</summary>
    public bool TravelsToWallet => Currency.HasValue;

    public static string IconIdFor(CurrencyKind currency) => currency switch
    {
        CurrencyKind.Coins      => "coin",
        CurrencyKind.Gems       => "gem",
        CurrencyKind.SpinTokens => "spin-token",
        _                       => "chest",
    };
}

/// <summary>
/// A list of items awarded or previewed together. The reveal plays the items in list order, which matches the
/// preview order.
/// </summary>
public sealed record RewardView(IReadOnlyList<RewardViewItem> Items)
{
    public static readonly RewardView Empty = new RewardView(Array.Empty<RewardViewItem>());

    public static RewardView Of(params RewardViewItem[] items) => new RewardView(items);

    /// <summary>
    /// Converts a game config <see cref="Game.Logic.RewardBundle"/> into the shell's reward bundle. All callers use
    /// this method so that every screen shows a config reward the same way.
    /// <para>
    /// Amounts with an unknown currency or a non-positive amount are skipped. Config validation already rejects
    /// both (<c>Game.Logic.RewardBundle.Validate</c>), and the checks here make sure an unknown currency is left
    /// out of a reveal instead of being shown as a different currency.
    /// </para>
    /// </summary>
    /// <param name="extra">An item to show after the currencies, such as a cosmetic reward, or null.</param>
    public static RewardView From(Game.Logic.RewardBundle? bundle, RewardViewItem? extra = null)
    {
        List<RewardViewItem> items = new List<RewardViewItem>();
        if (bundle?.Amounts != null)
        {
            foreach (Game.Logic.CurrencyAmount amount in bundle.Amounts)
            {
                if (Currencies.KindOf(amount.Currency) is CurrencyKind kind && amount.Amount > 0)
                    items.Add(RewardViewItem.Of(kind, amount.Amount));
            }
        }

        if (extra != null)
            items.Add(extra);

        return items.Count == 0 ? Empty : new RewardView(items);
    }

    public bool IsEmpty => Items.Count == 0;

    /// <summary>
    /// The bundle as text, for example "330 Coins + 1 Spin Token".
    /// <para>
    /// The reward reveal compares this string to detect that the bundle changed while the panel is open. Button
    /// labels do not use it.
    /// </para>
    /// </summary>
    public string Describe() =>
        string.Join(" + ", Items.Select(item => $"{Currencies.Format(item.Amount)} {item.Label}"));

    /// <summary>
    /// Returns <paramref name="wallet"/> with this bundle's currencies added. Non-currency items are skipped.
    /// </summary>
    public WalletView ApplyTo(WalletView wallet)
    {
        WalletView result = wallet;
        foreach (RewardViewItem item in Items)
        {
            if (item.Currency is CurrencyKind kind)
                result = result.With(kind, result.AmountOf(kind) + item.Amount);
        }
        return result;
    }
}

/// <summary>
/// The stage of a reward reveal. The stages let the client resume a reveal after a reconnect.
/// <para>
/// In <see cref="Granted"/> the player has been paid but the reveal may not have finished. After a reconnect the
/// client must replay the reveal without granting again. Treating an unfinished reveal as an unfinished grant
/// would either pay twice or tell a paid player they got nothing (docs/meta-shell.md, "The shared reward flow").
/// </para>
/// </summary>
public enum RewardRevealStage
{
    /// <summary>Nothing to show.</summary>
    Idle,

    /// <summary>The grant action is in flight. Nothing is revealed until the grant succeeds.</summary>
    Granting,

    /// <summary>The grant succeeded. The items are known and the reveal can run or run again.</summary>
    Granted,

    /// <summary>
    /// The reveal has finished playing and the balance is showing. Nothing in the client sets this stage, and
    /// <c>RewardReveal</c> draws it exactly as it draws <see cref="Granted"/>.
    /// </summary>
    Revealed,

    /// <summary>The grant failed and nothing was awarded. The player can retry.</summary>
    Failed,
}

public static class RewardRevealStages
{
    /// <summary>
    /// The reveal stage after a claim action returned <paramref name="result"/>: Granted when the action succeeded,
    /// Failed when it was refused. A null result means there is no session (offline mode and fixture scenarios),
    /// and the reveal runs as Granted without changing any state.
    /// </summary>
    public static RewardRevealStage AfterClaim(MetaActionResult? result) =>
        result == null || result.IsSuccess ? RewardRevealStage.Granted : RewardRevealStage.Failed;
}
