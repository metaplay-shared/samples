using WebClient.Meta;

namespace WebClient.Tests;

/// <summary>
/// Tests for <see cref="FloatingTextTiming"/>, the lifetime and stack offset rules shared by the table page and
/// the <c>FloatingText</c> component. Both read the same constants, so their timings cannot drift apart.
/// </summary>
[TestFixture]
public class FloatingTextTimingTests
{
    /// <summary>
    /// With full motion and no delay, the lifetime equals the duration. The delay before the animation starts is
    /// added to the lifetime under either motion setting.
    /// </summary>
    [TestCase(0,   900, false, 900)]
    [TestCase(440, 900, false, 1340)]
    [TestCase(440, 900, true,  1040)]
    public void LifetimeIsTheDelayPlusTheWindow(int delayMs, int durationMs, bool reducedMotion, int expected)
    {
        Assert.That(FloatingTextTiming.LifetimeMs(delayMs, durationMs, reducedMotion: reducedMotion), Is.EqualTo(expected));
    }

    /// <summary>With reduced motion the label does not rise, and its lifetime is
    /// <see cref="FloatingTextTiming.ReducedMotionLifetimeMs"/>. The lifetime is shortened in C#, because the
    /// component, not the stylesheet, removes the label.</summary>
    [Test]
    public void ReducedMotionShortensTheHold()
    {
        Assert.That(FloatingTextTiming.LifetimeMs(0, 900, reducedMotion: true), Is.EqualTo(FloatingTextTiming.ReducedMotionLifetimeMs));
        Assert.That(FloatingTextTiming.ReducedMotionLifetimeMs, Is.LessThan(FloatingTextTiming.FullMotionLifetimeMs));
    }

    /// <summary>Each stack position adds <see cref="FloatingTextTiming.StackStepRem"/> to the offset, so labels on the
    /// same anchor do not overlap.</summary>
    [TestCase(0, 0.0)]
    [TestCase(1, 1.1)]
    [TestCase(2, 2.2)]
    public void StackOffsetIsOneStepPerLabel(int stackIndex, double expected)
    {
        Assert.That(FloatingTextTiming.StackOffsetRem(stackIndex), Is.EqualTo(expected));
    }
}
