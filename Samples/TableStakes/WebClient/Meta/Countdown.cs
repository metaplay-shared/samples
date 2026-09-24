using Metaplay.Core;

namespace WebClient.Meta;

/// <summary>
/// The phase of a countdown. Surfaces show each phase with text and shape, not only colour
/// (docs/meta-shell.md, "MetaSnapshot").
/// <para>
/// <see cref="None"/> is declared first so that the default value means "no deadline".
/// </para>
/// </summary>
public enum CountdownPhase
{
    /// <summary>
    /// There is no deadline, so the surface shows no timer. This is different from <see cref="Expired"/>. For
    /// example, a player who has not entered the tournament has no season deadline.
    /// </summary>
    None,

    /// <summary>The deadline is not close. Shown without emphasis.</summary>
    Normal,

    /// <summary>The deadline is close. Shown with its own label and mark, not only a colour.</summary>
    EndingSoon,

    /// <summary>The deadline has passed. The surface shows an end state and no timer.</summary>
    Expired,
}

/// <summary>
/// Countdown phase and formatting rules, shared by every surface in the shell so that the same deadline reads the
/// same on Home, on a hub card and on the feature's own screen.
/// </summary>
public static class Countdown
{
    /// <summary>
    /// The default ending-soon threshold, for countdowns that do not pass their own.
    /// <para>
    /// Features with windows of different lengths pass their own threshold, because the same remaining time can be
    /// nearly over for a daily goal and early for a weekly event.
    /// </para>
    /// </summary>
    public static readonly TimeSpan EndingSoonThreshold = TimeSpan.FromHours(24);

    public static CountdownPhase PhaseOf(TimeSpan remaining) => PhaseOf(remaining, EndingSoonThreshold);

    /// <summary><paramref name="remaining"/> as a <see cref="TimeSpan"/>, or zero if it is negative.</summary>
    public static TimeSpan NonNegative(MetaDuration remaining) => MetaDuration.Max(remaining, MetaDuration.Zero).ToTimeSpan();

    /// <summary>
    /// The phase of a countdown that may have no deadline. A null <paramref name="remaining"/> returns
    /// <see cref="CountdownPhase.None"/>, not <see cref="CountdownPhase.Expired"/>, so that a missing deadline is
    /// not shown as ended.
    /// </summary>
    public static CountdownPhase PhaseOf(TimeSpan? remaining, TimeSpan endingSoonWithin) =>
        remaining is TimeSpan left ? PhaseOf(left, endingSoonWithin) : CountdownPhase.None;

    /// <summary>The phase of a countdown with its own ending-soon threshold.</summary>
    public static CountdownPhase PhaseOf(TimeSpan remaining, TimeSpan endingSoonWithin)
    {
        if (remaining <= TimeSpan.Zero)
            return CountdownPhase.Expired;
        if (remaining <= endingSoonWithin)
            return CountdownPhase.EndingSoon;
        return CountdownPhase.Normal;
    }

    /// <summary>
    /// Formats the remaining time with two units at most: days and hours, hours and minutes, minutes only, or
    /// seconds only.
    /// <para>
    /// Zero and negative durations return "0s". Callers should not show a timer when <see cref="PhaseOf"/> returns
    /// <see cref="CountdownPhase.Expired"/>.
    /// </para>
    /// </summary>
    public static string Format(TimeSpan remaining)
    {
        if (remaining <= TimeSpan.Zero)
            return "0s";

        if (remaining.TotalDays >= 1)
            return $"{(int)remaining.TotalDays}d {remaining.Hours}h";

        if (remaining.TotalHours >= 1)
            return $"{remaining.Hours}h {remaining.Minutes}m";

        if (remaining.TotalMinutes >= 1)
            return $"{remaining.Minutes}m";

        return $"{remaining.Seconds}s";
    }

    /// <summary>
    /// How often a countdown showing <paramref name="remaining"/> must be redrawn to stay correct. Countdowns that
    /// show minutes redraw once a minute instead of every second.
    /// </summary>
    public static TimeSpan RefreshIntervalFor(TimeSpan remaining)
    {
        // Format switches to seconds below one minute. Start ticking every second before that, because a timer that
        // ticks once a minute could reach the switch up to a minute late.
        if (remaining <= TimeSpan.FromMinutes(2))
            return TimeSpan.FromSeconds(1);

        return TimeSpan.FromMinutes(1);
    }
}
