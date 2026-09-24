namespace WebClient.Meta;

/// <summary>The hit animation the wheel plays after it stops, chosen by the landed sector's outcome.</summary>
public enum WheelHitKind
{
    Reward,
    SpinAgain,
    Nothing,
}

/// <summary>
/// Chooses the hit animation that plays between the end of the wheel spin and the reward reveal
/// (docs/spin-wheel.md).
/// <para>
/// The animation is chosen from the sector's <see cref="WheelTier"/>. The spin receipt and the drawn sector use the
/// same tier, so the animation always matches what was paid.
/// </para>
/// </summary>
public static class WheelHitAnimation
{
    /// <summary>
    /// How long the hit animation plays, in milliseconds. The page waits for this duration instead of the CSS
    /// animation's end event, like <c>SpinWheelPage.TurnMs</c> does for the spin. Under reduced motion the page
    /// shortens the wait in C#.
    /// </summary>
    public const int DefaultHitMs = 520;

    /// <summary>
    /// The hit animation for a sector's tier. <see cref="WheelTier.Nothing"/> and <see cref="WheelTier.SpinAgain"/>
    /// have their own animations. Every paying tier, including <see cref="WheelTier.Premium"/>, plays
    /// <see cref="WheelHitKind.Reward"/>.
    /// </summary>
    public static WheelHitKind Of(WheelTier tier) => tier switch
    {
        WheelTier.Nothing   => WheelHitKind.Nothing,
        WheelTier.SpinAgain => WheelHitKind.SpinAgain,
        _                   => WheelHitKind.Reward,
    };
}
