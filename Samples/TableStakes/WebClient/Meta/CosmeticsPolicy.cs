using Game.Logic;
using Metaplay.Core.Model;

namespace WebClient.Meta;

/// <summary>
/// Client-side cosmetics rules: mapping a catalogue id to its CSS style token, choosing an item's
/// <see cref="CosmeticOwnership"/> state, composing the preview identity, and wording refusals.
/// <para>
/// The server does not use this class. Whether a purchase succeeds is decided by the server's wallet
/// settlement, not here (<c>docs/cosmetics.md</c>).
/// </para>
/// </summary>
public static class CosmeticsPolicy
{
    /// <summary>
    /// The CSS style token for a catalogue id, or an empty string if <paramref name="id"/> is null or not in the
    /// config.
    /// <para>
    /// The model stores catalogue ids, and screens build CSS classes from style tokens. The two are different
    /// strings, so a screen that used the catalogue id directly would produce a class that matches no CSS rule
    /// (<c>docs/cosmetics.md</c>).
    /// </para>
    /// </summary>
    public static string StyleTokenOf(SharedGameConfig? config, CosmeticId? id)
    {
        if (config?.Cosmetics == null || id == null)
            return "";

        return config.Cosmetics.TryGetValue(id, out CosmeticInfo? info) ? info.StyleToken : "";
    }

    /// <summary>
    /// Converts a model <see cref="PlayerPublicIdentity"/> into an <see cref="IdentityView"/>, translating the worn
    /// cosmetic ids into style tokens.
    /// <para>
    /// Level and competition fields are left empty. Callers that show a tournament standing fill them in
    /// separately.
    /// </para>
    /// </summary>
    public static IdentityView ViewOf(SharedGameConfig? config, PlayerPublicIdentity? identity) =>
        new IdentityView(
            DisplayName:     identity?.DisplayName ?? "",
            AvatarToken:     StyleTokenOf(config, identity?.AvatarId),
            FrameToken:      StyleTokenOf(config, identity?.FrameId),
            NameEffectToken: StyleTokenOf(config, identity?.NameEffectId),
            Level:           0,
            CompetitionName: "",
            CompetitionRank: 0);

    /// <summary>
    /// The ownership state of one item.
    /// <para>
    /// An item that is not for sale is <see cref="CosmeticOwnership.Locked"/> when it has an unlock requirement,
    /// and <see cref="CosmeticOwnership.Unavailable"/> otherwise. A retired catalogue item is not purchasable and
    /// has no unlock requirement, so it becomes <see cref="CosmeticOwnership.Unavailable"/>.
    /// </para>
    /// </summary>
    public static CosmeticOwnership OwnershipOf(bool isOwned, bool isEquipped, bool isPurchasable, bool canAfford, string unlockRequirement)
    {
        if (isEquipped)
            return CosmeticOwnership.Equipped;
        if (isOwned)
            return CosmeticOwnership.Owned;
        if (isPurchasable)
            return canAfford ? CosmeticOwnership.Affordable : CosmeticOwnership.Unaffordable;

        return string.IsNullOrEmpty(unlockRequirement) ? CosmeticOwnership.Unavailable : CosmeticOwnership.Locked;
    }

    /// <summary>
    /// The identity shown on the preview card: <paramref name="identity"/> with <paramref name="previewed"/> in its
    /// slot. The preview shows the whole identity so that it matches how the item looks in the standings
    /// (<c>docs/cosmetics.md</c>).
    /// </summary>
    /// <remarks>
    /// Slots are set to the item's style token, not its catalogue id, because <see cref="IdentityView"/> fields
    /// hold style tokens (see <see cref="StyleTokenOf"/>).
    /// </remarks>
    public static IdentityView Preview(IdentityView identity, CosmeticItem previewed) => previewed.Slot switch
    {
        CosmeticSlot.Avatar => identity with { AvatarToken = previewed.StyleToken },
        CosmeticSlot.Frame  => identity with { FrameToken = previewed.StyleToken },
        _                   => identity with { NameEffectToken = previewed.StyleToken },
    };

    /// <summary>
    /// The message to show for a refused cosmetics action, or null for success or a null result. The
    /// insufficient-funds message computes the shortfall from the item price and <paramref name="wallet"/>, which
    /// is unchanged because a refused action spends nothing.
    /// <para>
    /// A null result means there is no session (fixtures or offline mode), so there is nothing to report. Results
    /// not listed here, such as SDK system errors, get a generic message.
    /// </para>
    /// </summary>
    public static string? RefusalMessage(MetaActionResult? result, CosmeticItem item, WalletView wallet)
    {
        if (result == null || result == MetaActionResult.Success)
            return null;

        if (result == ActionResults.InsufficientFunds)
        {
            long shortfall = item.ShortfallAgainst(wallet);
            return $"Not enough {Currencies.NameOf(item.PriceCurrency)} — "
                   + $"you need {Currencies.Format(shortfall)} more {Currencies.NameOf(item.PriceCurrency, shortfall)}.";
        }

        if (result == ActionResults.NoSuchCosmetic)
            return "That item isn't in the catalogue any more. Nothing was spent.";
        if (result == ActionResults.CosmeticNotPurchasable)
            return "This one isn't for sale — it's earned, not bought.";
        if (result == ActionResults.CosmeticAlreadyOwned)
            return "You already own this one.";
        if (result == ActionResults.CosmeticNotOwned)
            return "You don't own that one yet.";
        if (result == ActionResults.CosmeticAlreadyEquipped)
            return "You're already wearing it.";

        return "That did not go through. Please try again.";
    }
}
