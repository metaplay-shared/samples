using Game.Logic;
using Game.Server.Player;
using Metaplay.Core;
using NUnit.Framework;

namespace Game.Server.Tests
{
    /// <summary>
    /// Tests <see cref="DailyRewardClaimInFlight"/>, which covers the time between enqueuing a daily reward claim and
    /// the claim action running.
    /// <para>
    /// The in-flight claim is <b>server-only</b> state: the player model does not know that an action was sent to the
    /// client and has not run yet. A browser test cannot reach these cases, because the time window is only
    /// milliseconds long.
    /// </para>
    /// </summary>
    [TestFixture]
    public class DailyRewardClaimInFlightTests
    {
        static PlayerDailyRewardClaimed ClaimFor(int activation) => new PlayerDailyRewardClaimed(activation, MetaTime.Epoch);

        [Test]
        public void AnIdleClaimHoldsNothingInFlight()
        {
            Assert.That(DailyRewardClaimInFlight.Idle.IsInFlight(activation: 20_522), Is.False);
            Assert.That(DailyRewardClaimInFlight.Idle.IsInFlight(activation: 0), Is.False,
                "activation zero is a real day and must not collide with the idle sentinel");
        }

        [Test]
        public void AnEnqueuedClaimIsInFlightUntilItRuns()
        {
            DailyRewardClaimInFlight inFlight = DailyRewardClaimInFlight.Enqueued(activation: 20_522);

            Assert.That(inFlight.IsInFlight(20_522), Is.True, "the second request of the same day found no claim in flight");
        }

        /// <summary>
        /// The in-flight claim ends when the claim action runs, whether the claim succeeded or failed. A failed claim, for
        /// example after a config publish lowered a wallet cap before the action ran, leaves the day unclaimed,
        /// so the player's next request that day must be processed.
        /// </summary>
        [Test]
        public void TheClaimRunningEndsTheBurstWhateverItsResult()
        {
            DailyRewardClaimInFlight inFlight = DailyRewardClaimInFlight.Enqueued(activation: 20_522);

            Assert.That(inFlight.After(ClaimFor(20_522)).IsInFlight(20_522), Is.False,
                "a claim that has run, paid or refused, still held the day");
        }

        /// <summary>
        /// Any other action leaves the claim pending.
        /// </summary>
        [Test]
        public void AnotherActionLeavesTheClaimInFlight()
        {
            DailyRewardClaimInFlight inFlight = DailyRewardClaimInFlight.Enqueued(activation: 20_522);

            Assert.That(inFlight.After(new PlayerTournamentJoined()).IsInFlight(20_522), Is.True);
        }

        [Test]
        public void AClaimInFlightDoesNotHoldAnotherDay()
        {
            DailyRewardClaimInFlight inFlight = DailyRewardClaimInFlight.Enqueued(activation: 20_522);

            Assert.That(inFlight.IsInFlight(20_523), Is.False);
        }
    }
}
