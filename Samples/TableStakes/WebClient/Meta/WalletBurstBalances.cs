namespace WebClient.Meta;

/// <summary>
/// The HUD balances to show while a reward animation plays (<c>docs/meta-shell.md</c>, "The shared reward flow").
/// The model already contains the reward, so the displayed value is the model balance minus the part of the reward
/// that has not landed yet. It stores no earlier balance, so it cannot go stale and always ends at the model
/// balance. Currencies not in the reward show the model balance.
/// </summary>
public sealed record WalletBurstBalances(WalletBurstPlan Plan)
{
    /// <summary>
    /// The maximum animation time in milliseconds. After this, the HUD shows the model balance even if the
    /// animation did not report that it finished, for example because the tab was in the background.
    /// <para>
    /// The cap applies to the animation, not to the wait before it. The balance is held back from the grant until
    /// the player dismisses the reveal, however long that takes. Every way of closing the reveal releases the
    /// hold.
    /// </para>
    /// </summary>
    public const int MaxAnimationMs = 2_000;

    /// <summary>
    /// The animated balances for a paid reward, or null if nothing animates: the bundle is empty, has no
    /// currencies, or <paramref name="burstMs"/> is not positive.
    /// <para>
    /// It takes no wallet because <see cref="ValueAt"/> computes from the current wallet, so the order of the grant,
    /// the reveal and this call does not matter.
    /// </para>
    /// </summary>
    public static WalletBurstBalances? Begin(RewardView bundle, int burstMs = WalletBurstPlan.DefaultBurstMs)
    {
        WalletBurstPlan plan = WalletBurstPlan.For(bundle, burstMs);
        return plan.IsEmpty ? null : new WalletBurstBalances(plan);
    }

    /// <summary>The animation's duration in milliseconds, capped at <see cref="MaxAnimationMs"/>.</summary>
    public int DurationMs => Math.Min(Plan.DurationMs, MaxAnimationMs);

    /// <summary>
    /// The balance to show for <paramref name="kind"/>, given the model's wallet <paramref name="live"/>.
    /// <para>
    /// The value increases with the number of landed sprites, not with elapsed time, so the number matches the
    /// sprites.
    /// </para>
    /// </summary>
    public long ValueAt(CurrencyKind kind, WalletView live, int elapsedMs)
    {
        long liveAmount = live.AmountOf(kind);

        CurrencyBurst? burst = Plan.Of(kind);
        if (burst == null || burst.Sprites.Count == 0 || elapsedMs >= MaxAnimationMs)
            return liveAmount;

        int landedSprites = burst.LandedBy(elapsedMs);
        if (landedSprites >= burst.Sprites.Count)
            return liveAmount;

        // Clamp at zero because the reward amount can exceed the current balance, for example when the grant was
        // capped or the reveal replays after the player spent the currency.
        long amountBeforeReward = Math.Max(0, liveAmount - burst.Amount);
        return amountBeforeReward + (long)Math.Round((liveAmount - amountBeforeReward) * (landedSprites / (double)burst.Sprites.Count));
    }

    /// <summary>
    /// The fraction of one currency's sprites that have landed, from 0 to 1. The HUD chip's glow uses it, and it
    /// counts the same landings as <see cref="ValueAt"/>.
    /// <para>
    /// Returns 1 for a currency that is not in the reward.
    /// </para>
    /// </summary>
    public double ProgressOf(CurrencyKind kind, int elapsedMs)
    {
        CurrencyBurst? burst = Plan.Of(kind);
        if (burst == null || burst.Sprites.Count == 0)
            return 1;

        return Math.Clamp(burst.LandedBy(elapsedMs) / (double)burst.Sprites.Count, 0.0, 1.0);
    }
}
