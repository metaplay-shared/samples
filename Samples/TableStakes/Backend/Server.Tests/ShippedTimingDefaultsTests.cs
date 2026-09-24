using Game.Logic;
using Game.Server.Match;
using Game.Server.Matchmaking;
using Metaplay.Core;
using NUnit.Framework;
using System;

namespace Game.Server.Tests
{
    /// <summary>
    /// Checks the server's default timing values as literal numbers.
    /// <para>
    /// E2E tests that depend on a default duration hard-code it or derive a bound from it, because they cannot read
    /// it from the server. If a default changed, those tests would silently test something else, so these tests
    /// fail instead. The E2E harness may shorten only the durations in <c>SANCTIONED_PACING_KEYS</c>
    /// (<c>tools/run-e2e.py</c>), and a duration checked here must not be on that list.
    /// </para>
    /// </summary>
    [TestFixture]
    public class ShippedTimingDefaultsTests
    {
        /// <summary>
        /// The matchmaking fill wait. Two players who press Play within one fill wait are placed at the same
        /// table, so E2E tests that seat two players together depend on it, and the live-server fixtures run one at
        /// a time because they share the queue.
        /// <para>
        /// The test checks <see cref="MatchmakingOptions.FillWaitDuration"/>, which the matchmaker and the
        /// player's actor read, rather than the <see cref="TimeSpan"/> option, so it also covers the conversion.
        /// </para>
        /// </summary>
        [Test]
        public void TheQueueFillsATableAfterFiveSeconds()
        {
            Assert.That(new MatchmakingOptions().FillWaitDuration, Is.EqualTo(MetaDuration.FromSeconds(5)));
        }

        /// <summary>
        /// The pause after the last card of a trick. The client's trick animation is limited to this pause
        /// (<c>MatchClocks.GetBeatEndsAt</c>), so it is the longest time a player sees a completed trick.
        /// </summary>
        [Test]
        public void TheResolvePauseIsOneAndAHalfSeconds()
        {
            Assert.That(MatchTimings.Default.ResolvePause, Is.EqualTo(MetaDuration.FromMilliseconds(1500)));
        }

        /// <summary>
        /// How long a connected seat has to play before its card is auto-played. Tests that need the table to stay
        /// unchanged while they check something rely on this time.
        /// </summary>
        [Test]
        public void TheMoveDeadlineIsTwentySeconds()
        {
            Assert.That(MatchTimings.Default.MoveDeadline, Is.EqualTo(MetaDuration.FromSeconds(20)));
        }

        /// <summary>
        /// How long a disconnected seat is held before a bot covers it. A test that reloads the page during a game
        /// must reconnect within this time. The E2E suite has a hard-coded copy of this value
        /// (<c>WebClient.Tests/PlaywrightPageTest.cs</c>).
        /// </summary>
        [Test]
        public void TheDisconnectGraceIsTwentySeconds()
        {
            Assert.That(MatchTimings.Default.DisconnectGrace, Is.EqualTo(MetaDuration.FromSeconds(20)));
        }

        /// <summary>
        /// An unconfigured server passes the shared-code defaults checked above to the match engine. Without this
        /// test, a different default in <see cref="MatchOptions"/> would make the tests above check values the
        /// server does not use.
        /// </summary>
        [Test]
        public void AnUnconfiguredServerRunsTheSharedDefaults()
        {
            MatchTimings timings = new MatchOptions().ToMatchTimings();

            Assert.That(timings.ResolvePause, Is.EqualTo(MatchTimings.Default.ResolvePause));
            Assert.That(timings.MoveDeadline, Is.EqualTo(MatchTimings.Default.MoveDeadline));
            Assert.That(timings.DisconnectGrace, Is.EqualTo(MatchTimings.Default.DisconnectGrace));
        }

        /// <summary>
        /// The default matchmaking timeouts are in the required order, and loading options with a value out of
        /// order throws, which stops the server from starting (see <see cref="MatchmakingOptions"/>).
        /// <para>
        /// This covers an operator override, which the default value tests cannot see. A wrong order causes no
        /// error at run time, only a player who is told the search failed and is then seated.
        /// </para>
        /// </summary>
        [Test]
        public void TheShippedMatchmakingTimeoutsAreInOrder_AndAnInvertedOverrideIsRefused()
        {
            Assert.DoesNotThrowAsync(() => new MatchmakingOptions().OnLoadedAsync());

            // A seat reservation timeout shorter than the longest formation: the player's actor would release the
            // seat while the matchmaker still fills it.
            AssertRefused(With(new MatchmakingOptions(), nameof(MatchmakingOptions.SeatReservationTimeout), TimeSpan.FromSeconds(1)).OnLoadedAsync,
                nameof(MatchmakingOptions.SeatReservationTimeout));

            // A fill wait so long that the search times out before the reservation ask can arrive.
            AssertRefused(With(new MatchmakingOptions(), nameof(MatchmakingOptions.FillWait), TimeSpan.FromSeconds(58)).OnLoadedAsync,
                nameof(MatchmakingOptions.SearchTimeout));
        }

        /// <summary>
        /// A longer fill wait is a valid tuning for a low-population deployment, because the search timeout still
        /// covers the wait and the reservation ask.
        /// </summary>
        [Test]
        public void ALongerFillWaitIsAccepted()
        {
            Assert.DoesNotThrowAsync(() => With(new MatchmakingOptions(), nameof(MatchmakingOptions.FillWait), TimeSpan.FromSeconds(20)).OnLoadedAsync());
        }

        /// <summary>
        /// The table's actor must outlive the disconnect grace and the join window, or it shuts down before they
        /// end and the table waits in the database mid-game.
        /// </summary>
        [Test]
        public void TheShippedMatchLingerOutlivesItsTimers_AndAShorterOneIsRefused()
        {
            Assert.DoesNotThrowAsync(() => new MatchOptions().OnLoadedAsync());

            AssertRefused(With(new MatchOptions(), nameof(MatchOptions.DisconnectGrace), TimeSpan.FromSeconds(60)).OnLoadedAsync,
                nameof(MatchOptions.DisconnectGrace));
            AssertRefused(With(new MatchOptions(), nameof(MatchOptions.JoinWindow), TimeSpan.FromSeconds(60)).OnLoadedAsync,
                nameof(MatchOptions.JoinWindow));
        }

        /// <summary>Sets one option by reflection, because the setters are private to the options binder.</summary>
        static T With<T>(T options, string property, TimeSpan value)
        {
            typeof(T).GetProperty(property)!.SetValue(options, value);
            return options;
        }

        static void AssertRefused(AsyncTestDelegate load, string namedOption)
        {
            InvalidOperationException refusal = Assert.ThrowsAsync<InvalidOperationException>(load);

            Assert.That(refusal.Message, Does.Contain(namedOption), "the refusal does not name the option that has to change");
        }
    }
}
