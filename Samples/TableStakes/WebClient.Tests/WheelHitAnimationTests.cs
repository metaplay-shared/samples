using WebClient.Meta;

namespace WebClient.Tests;

/// <summary>
/// <see cref="WheelHitAnimation.Of"/>: which landing animation the wheel plays for a sector's tier. No browser or server is
/// needed.
/// </summary>
[TestFixture]
public class WheelHitAnimationTests
{
    /// <summary>
    /// The empty sector gets the Blank animation (a tremble), not a celebration. A spin-again sector pays in spin
    /// tokens, but it gets the Respin animation, not the Reward one: the tier is checked before the currency. Every
    /// paying tier, including the top prize, gets the Reward animation.
    /// </summary>
    [TestCase(WheelTier.Nothing,   WheelHitKind.Nothing)]
    [TestCase(WheelTier.SpinAgain, WheelHitKind.SpinAgain)]
    [TestCase(WheelTier.Common,    WheelHitKind.Reward)]
    [TestCase(WheelTier.Uncommon,  WheelHitKind.Reward)]
    [TestCase(WheelTier.Rare,      WheelHitKind.Reward)]
    [TestCase(WheelTier.Premium,   WheelHitKind.Reward)]
    public void EachTierPlaysItsAnimation(WheelTier tier, WheelHitKind expected)
    {
        Assert.That(WheelHitAnimation.Of(tier), Is.EqualTo(expected));
    }
}
