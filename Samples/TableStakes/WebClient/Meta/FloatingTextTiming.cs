namespace WebClient.Meta;

/// <summary>
/// Timing and layout constants for floating combat text labels. The page and the label component both read
/// these values so that the page's trigger window and the label's lifetime stay equal.
/// <para>
/// Under reduced motion the stylesheet does not animate the label, so C# removes it after
/// <see cref="ReducedMotionLifetimeMs"/> (see <see cref="WebClient.Integration.MotionPreference"/>).
/// </para>
/// </summary>
public static class FloatingTextTiming
{
    /// <summary>The label's lifetime in milliseconds at full motion, covering pop-in, rise and fade.</summary>
    public const int FullMotionLifetimeMs = 900;

    /// <summary>The label's lifetime in milliseconds under reduced motion, where it does not rise or fade.</summary>
    public const int ReducedMotionLifetimeMs = 600;

    /// <summary>How far a label rises over its lifetime, in rem.</summary>
    public const double RiseRem = 2.5;

    /// <summary>How far up a stacked label sits above the one before it, in rem.</summary>
    public const double StackStepRem = 1.1;

    /// <summary>
    /// The time in milliseconds from mounting the label to removing it: <paramref name="delayMs"/> plus
    /// <paramref name="durationMs"/>, or plus <see cref="ReducedMotionLifetimeMs"/> under reduced motion.
    /// </summary>
    public static int LifetimeMs(int delayMs, int durationMs, bool reducedMotion)
        => delayMs + (reducedMotion ? ReducedMotionLifetimeMs : durationMs);

    /// <summary>The stylesheet's <c>--ts-float-stack</c> offset for the label at this position in a stack.</summary>
    public static double StackOffsetRem(int stack) => stack * StackStepRem;
}
