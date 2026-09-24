namespace WebClient.Meta;

/// <summary>
/// Chooses the icon drawn on one sector of the prize wheel.
/// <para>
/// A blank sector shows a skull and a spin-again sector shows a circular arrow. Every other sector shows its
/// currency's icon, the same icon that the wallet and the reward previews use.
/// </para>
/// </summary>
public static class WheelSectorArt
{
    /// <summary>
    /// The <c>MetaIcon</c> id for one sector. The tier is checked before the currency because a spin-again sector
    /// pays in spin tokens but should show the respin icon.
    /// </summary>
    public static string IconIdFor(WheelSectorView sector) =>
        sector.Currency is null
            ? "skull"
            : sector.Tier == WheelTier.SpinAgain
                ? "respin"
                : RewardViewItem.IconIdFor(sector.Currency.Value);
}
