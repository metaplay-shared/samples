using Metaplay.Core;
using NUnit.Framework;

namespace Game.Logic.Tests
{
    /// <summary>
    /// Tests for <see cref="MatchmakingWaitText"/>: when the matchmaking dialog shows, whether Cancel is enabled,
    /// and the text and countdown it shows. The logic is pure, so it is tested here rather than in a browser
    /// (<c>docs/matchmaking.md</c>, "What the client sees").
    /// </summary>
    [TestFixture]
    public class MatchmakingWaitTextTests
    {
        /// <summary>
        /// The dialog is up for both waits and for nothing else. It stays open across the seat commit instead of
        /// closing and reopening. A refused request is not a wait: the dialog closes and the menu reports the
        /// refusal, rather than showing a wait for a queue the player is not in.
        /// </summary>
        [TestCase(MatchmakingStatus.Searching,    true)]
        [TestCase(MatchmakingStatus.SeatReserved, true)]
        [TestCase(MatchmakingStatus.NotSearching, false)]
        [TestCase(MatchmakingStatus.Unavailable,  false)]
        public void TheDialogIsUpForBothWaitsAndForNothingElse(MatchmakingStatus status, bool expectedWaiting)
        {
            Assert.That(MatchmakingWaitText.IsWaiting(status), Is.EqualTo(expectedWaiting));
        }

        [Test]
        public void CancelIsOfferedOnlyWhileThereIsSomethingToCancel()
        {
            Assert.That(MatchmakingWaitText.CanCancel(MatchmakingStatus.Searching), Is.True);

            // The seat is committed to a forming table and the player actor would refuse a Cancel, so the
            // Cancel button is disabled (docs/matchmaking.md, "Cancel").
            Assert.That(MatchmakingWaitText.CanCancel(MatchmakingStatus.SeatReserved), Is.False);
        }

        [Test]
        public void TheDetailSaysWhatIsHappeningWhetherOrNotThereIsStillANumberToShow()
        {
            // Waiting on the queue, with a countdown running.
            Assert.That(MatchmakingWaitText.Detail(MetaDuration.FromSeconds(3)), Does.Contain("Computer players"));

            // The countdown has run out and the table is still forming. The detail line says so instead of going
            // blank or starting a new countdown.
            Assert.That(MatchmakingWaitText.Detail(MetaDuration.Zero), Is.EqualTo("Any moment now…"));
        }

        [Test]
        public void ThereIsNoCountdownBeforeTheServerHasAnsweredOrAfterTheWaitHasRunOut()
        {
            // The dialog opens on the tap and the server's deadline arrives a round trip later. Until then the
            // dial shows no number, rather than one the client made up.
            Assert.That(MatchmakingWaitText.HasCountdown(MetaDuration.Zero), Is.False);
            Assert.That(MatchmakingWaitText.HasCountdown(MetaDuration.FromSeconds(-2)), Is.False);

            Assert.That(MatchmakingWaitText.HasCountdown(MetaDuration.FromMilliseconds(1)), Is.True);
        }

        /// <summary>
        /// The countdown rounds up, so a wait with any fraction of a second left shows 1 and never reads 0. The
        /// dial is hidden when the wait is over, so it never needs to show 0.
        /// <para>
        /// The remaining time is the server's deadline minus the client's clock. A badly skewed clock or a corrupt
        /// message can make it too large for an int, so <see cref="MatchmakingWaitText.SecondsShown"/> clamps it before
        /// narrowing to int. The worst case is a number that reads too high rather than one that wraps negative.
        /// The clamp is far above any real fill wait and leaves ordinary values unchanged.
        /// </para>
        /// </summary>
        [TestCase(5000L,            5)]
        [TestCase(4001L,            5)]
        [TestCase(4000L,            4)]
        [TestCase(1L,               1)]
        [TestCase(0L,               0)]
        [TestCase(-3000L,           0)]
        [TestCase(30_000L,          30)]
        [TestCase(315_360_000_000L, MatchmakingWaitText.MaxSecondsShown)]
        [TestCase(long.MaxValue,    MatchmakingWaitText.MaxSecondsShown)]
        public void TheCountdownRoundsUpAndIsClampedRatherThanOverflowed(long remainingMs, int expectedSeconds)
        {
            Assert.That(MatchmakingWaitText.SecondsShown(MetaDuration.FromMilliseconds(remainingMs)), Is.EqualTo(expectedSeconds));
        }
    }
}
