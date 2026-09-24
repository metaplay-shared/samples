using WebClient.Meta;

namespace WebClient.Tests;

/// <summary>
/// Tests for <see cref="Countdown"/>, the shared timer format and <see cref="CountdownPhase"/> rules.
/// <para>
/// A player sees the same deadline on Home, on a hub card and on the feature's own page, so every surface must
/// format it the same way. An expired countdown must show its end state instead of zero or a negative time.
/// </para>
/// </summary>
[TestFixture]
public class CountdownTests
{
    [TestCase(0, 0, 0, 45, "45s")]
    [TestCase(0, 0, 1, 0,  "1m")]
    [TestCase(0, 0, 47, 0, "47m")]
    [TestCase(0, 0, 59, 59, "59m")]
    [TestCase(0, 1, 0, 0,  "1h 0m")]
    [TestCase(0, 14, 32, 0, "14h 32m")]
    [TestCase(0, 23, 59, 59, "23h 59m")]
    [TestCase(1, 0, 0, 0,  "1d 0h")]
    [TestCase(2, 14, 0, 0, "2d 14h")]
    public void TimeIsWrittenAtTheCoarsestUnitThatStillDecidesSomething(int d, int h, int m, int s, string expected)
    {
        Assert.That(Countdown.Format(new TimeSpan(d, h, m, s)), Is.EqualTo(expected));
    }

    /// <summary>
    /// An expired deadline shows its end state, so this string is not rendered. The format still clamps to zero
    /// so that a surface that does render it never shows a negative time.
    /// </summary>
    [Test]
    public void TimeThatHasRunOutNeverGoesNegative()
    {
        Assert.That(Countdown.Format(TimeSpan.FromMinutes(-3)), Is.EqualTo("0s"));
        Assert.That(Countdown.Format(TimeSpan.Zero), Is.EqualTo("0s"));
    }

    [Test]
    public void ThePhasesAreOrderedByHowMuchTimeIsLeft()
    {
        Assert.That(Countdown.PhaseOf(TimeSpan.FromDays(3)),     Is.EqualTo(CountdownPhase.Normal));
        Assert.That(Countdown.PhaseOf(TimeSpan.FromHours(23)),   Is.EqualTo(CountdownPhase.EndingSoon));
        Assert.That(Countdown.PhaseOf(TimeSpan.Zero),            Is.EqualTo(CountdownPhase.Expired));
        Assert.That(Countdown.PhaseOf(TimeSpan.FromSeconds(-1)), Is.EqualTo(CountdownPhase.Expired));
    }

    /// <summary>
    /// A missing deadline has the phase <see cref="CountdownPhase.None"/>, not <see cref="CountdownPhase.Expired"/>.
    /// A tournament the player has not entered reports no deadline, and treating that as expired would show the
    /// season as ended next to the invitation to join.
    /// </summary>
    [Test]
    public void AnAbsentDeadlineIsNotAnExpiredOne()
    {
        Assert.That(Countdown.PhaseOf((TimeSpan?)null, Countdown.EndingSoonThreshold),
            Is.EqualTo(CountdownPhase.None));

        Assert.That(Countdown.PhaseOf((TimeSpan?)TimeSpan.Zero, Countdown.EndingSoonThreshold),
            Is.EqualTo(CountdownPhase.Expired));

        Assert.That(Countdown.PhaseOf((TimeSpan?)TimeSpan.FromHours(2), Countdown.EndingSoonThreshold),
            Is.EqualTo(CountdownPhase.EndingSoon));
    }

    /// <summary>A remaining time exactly equal to the threshold is already ending soon.</summary>
    [Test]
    public void TheEndingSoonThresholdIsInclusive()
    {
        Assert.That(Countdown.PhaseOf(Countdown.EndingSoonThreshold), Is.EqualTo(CountdownPhase.EndingSoon));
        Assert.That(Countdown.PhaseOf(Countdown.EndingSoonThreshold + TimeSpan.FromSeconds(1)),
            Is.EqualTo(CountdownPhase.Normal));
    }

    /// <summary>
    /// The ending-soon threshold is passed per countdown, because the same remaining time is nearly over for a
    /// week-long event and barely started for a one-day goal.
    /// </summary>
    [Test]
    public void EndingSoonIsJudgedAgainstTheClocksOwnWindow()
    {
        TimeSpan twentyHours = TimeSpan.FromHours(20);

        Assert.That(Countdown.PhaseOf(twentyHours, TimeSpan.FromHours(24)), Is.EqualTo(CountdownPhase.EndingSoon),
            "a week-long event with twenty hours left is nearly over");

        Assert.That(Countdown.PhaseOf(twentyHours, TimeSpan.FromHours(1)), Is.EqualTo(CountdownPhase.Normal),
            "a daily goal with twenty hours left has barely started");
    }

    /// <summary>A countdown with a zero threshold is never ending soon, however little time is left.</summary>
    [Test]
    public void AClockWithNoUrgencyNeverEndsSoon()
    {
        Assert.That(Countdown.PhaseOf(TimeSpan.FromSeconds(1), TimeSpan.Zero), Is.EqualTo(CountdownPhase.Normal));
        Assert.That(Countdown.PhaseOf(TimeSpan.Zero, TimeSpan.Zero), Is.EqualTo(CountdownPhase.Expired));
    }

    /// <summary>
    /// A timer is redrawn as often as its smallest visible unit changes. Near one minute it must already tick in
    /// seconds, or "1m" would stay on screen while the deadline ran out.
    /// </summary>
    [Test]
    public void ATimerIsRedrawnOnlyAsOftenAsItsSmallestVisibleUnitMoves()
    {
        Assert.That(Countdown.RefreshIntervalFor(TimeSpan.FromDays(2)),     Is.EqualTo(TimeSpan.FromMinutes(1)));
        Assert.That(Countdown.RefreshIntervalFor(TimeSpan.FromMinutes(47)), Is.EqualTo(TimeSpan.FromMinutes(1)));
        Assert.That(Countdown.RefreshIntervalFor(TimeSpan.FromSeconds(90)), Is.EqualTo(TimeSpan.FromSeconds(1)),
            "the display is about to switch to seconds, so it is already ticking in seconds");
        Assert.That(Countdown.RefreshIntervalFor(TimeSpan.FromSeconds(30)), Is.EqualTo(TimeSpan.FromSeconds(1)));
    }
}
