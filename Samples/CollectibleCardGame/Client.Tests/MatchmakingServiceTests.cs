using Game.Client.Services;
using Game.Logic;
using Metaplay.Core;
using System;

namespace Game.Client.Tests;

/// <summary>
/// The searching dialog's copy and its countdown, away from the browser. The design asks for exactly this:
/// "what the dialog says for a given status, and how the countdown rounds, is a pure function and is
/// unit-tested as one" (<c>Docs/matchmaking.md</c>).
/// </summary>
[TestFixture]
public class MatchmakingServiceTests
{
    static readonly MetaTime Now = MetaTime.FromDateTime(new DateTime(2026, 9, 9, 12, 0, 0, DateTimeKind.Utc));

    static MetaTime In(double seconds) => Now + MetaDuration.FromMilliseconds((long)(seconds * 1000));

    [Test]
    public void TheHeadlineSaysWhichOfTheTwoWaitsIsRunning()
    {
        Assert.That(MatchmakingCopy.Headline(MatchmakingDialogState.Searching), Is.Not.Empty);
        Assert.That(MatchmakingCopy.Headline(MatchmakingDialogState.Cancelling), Is.Not.Empty);
        Assert.That(MatchmakingCopy.Headline(MatchmakingDialogState.Cancelling),
            Is.Not.EqualTo(MatchmakingCopy.Headline(MatchmakingDialogState.Searching)));
        Assert.That(MatchmakingCopy.Headline(MatchmakingDialogState.Hidden), Is.Empty);
    }

    [Test]
    public void TheCountdownRoundsUpSoItNeverReadsZeroWhileThereIsTimeLeft()
    {
        Assert.That(MatchmakingCopy.Countdown(In(45), Now), Does.Contain("45"));
        Assert.That(MatchmakingCopy.Countdown(In(44.2), Now), Does.Contain("45"));
        Assert.That(MatchmakingCopy.Countdown(In(0.4), Now), Does.Contain("1 second"));
        Assert.That(MatchmakingCopy.Countdown(In(0.4), Now), Does.Not.Contain("seconds"));
    }

    [Test]
    public void PastZeroItSaysAnyMomentNowRatherThanANegativeNumber()
    {
        // The stamp is an upper bound rather than a prediction, so the copy is written to finish early
        // gracefully: never a negative number, and never a restarted one.
        Assert.That(MatchmakingCopy.Countdown(In(0), Now), Is.EqualTo(MatchmakingCopy.AnyMomentNow));
        Assert.That(MatchmakingCopy.Countdown(In(-1), Now), Is.EqualTo(MatchmakingCopy.AnyMomentNow));
        Assert.That(MatchmakingCopy.Countdown(In(-600), Now), Is.EqualTo(MatchmakingCopy.AnyMomentNow));
    }

    [Test]
    public void TheCountdownOnlyEverFalls()
    {
        // The stamp is fixed for the life of the ticket, so what the player watches is monotone. A restart
        // would mean the client had invented a number.
        MetaTime byInstant = In(45);
        long previous = long.MaxValue;

        for (int elapsed = 0; elapsed <= 50; elapsed++)
        {
            string copy = MatchmakingCopy.Countdown(byInstant, In(elapsed));
            long seconds = copy == MatchmakingCopy.AnyMomentNow ? 0 : long.Parse(copy.Split(' ')[1]);

            Assert.That(seconds, Is.LessThanOrEqualTo(previous), $"the countdown went up at {elapsed}s");
            previous = seconds;
        }

        Assert.That(previous, Is.Zero, "and it lands on 'any moment now'");
    }

    [Test]
    public void NoStampMeansNoCountdownAtAll()
    {
        // Between the optimistic tap and the server's one status push there is no number to show, and
        // inventing one is the thing the design is most explicit about not doing.
        Assert.That(MatchmakingCopy.Countdown(null, Now), Is.Empty);
    }

    [Test]
    public void CancelIsOfferedOnlyWhileTheSearchIsStillCancellable()
    {
        Assert.That(MatchmakingCopy.ShowsCancel(MatchmakingDialogState.Searching), Is.True);
        Assert.That(MatchmakingCopy.ShowsCancel(MatchmakingDialogState.Cancelling), Is.False,
            "tapping it twice does nothing new");
        Assert.That(MatchmakingCopy.ShowsCancel(MatchmakingDialogState.Hidden), Is.False);
    }

    [Test]
    public void EveryEndReasonHasSomethingToSay_ExceptTheOneThePlayerAskedFor()
    {
        // A dialog that vanished with nothing said is what a player reads as a button that did nothing — but
        // a cancel is a thing they did on purpose and needs no explanation.
        Assert.That(MatchmakingCopy.EndedNotice(MatchmakingEndReason.Cancelled), Is.Empty);

        foreach (MatchmakingEndReason reason in Enum.GetValues<MatchmakingEndReason>())
        {
            if (reason == MatchmakingEndReason.Cancelled)
                continue;

            Assert.That(MatchmakingCopy.EndedNotice(reason), Is.Not.Empty, $"{reason} says nothing");
        }
    }

    [Test]
    public void NothingTheDialogSaysNamesAPopulationCountOrABand()
    {
        // The rule the design is most emphatic about: a count invites the player to model the queue and read a
        // small number as a dead game, and a visibly widening band reads as "the match you are being offered
        // is getting worse the longer you wait". The copy is asserted rather than trusted because it is the
        // kind of thing a later edit adds back as a kindness.
        string[] everything =
        {
            MatchmakingCopy.Headline(MatchmakingDialogState.Searching),
            MatchmakingCopy.Headline(MatchmakingDialogState.Cancelling),
            MatchmakingCopy.Countdown(In(30), Now),
            MatchmakingCopy.AnyMomentNow,
            MatchmakingCopy.EndedNotice(MatchmakingEndReason.TimedOut),
            MatchmakingCopy.EndedNotice(MatchmakingEndReason.SeatLost),
            MatchmakingCopy.EndedNotice(MatchmakingEndReason.Refused),
        };

        foreach (string copy in everything)
        {
            foreach (string forbidden in new[] { "waiting", "queue of", "rating", "band", "players online" })
                Assert.That(copy.ToLowerInvariant(), Does.Not.Contain(forbidden), $"'{copy}' names {forbidden}");
        }
    }
}
