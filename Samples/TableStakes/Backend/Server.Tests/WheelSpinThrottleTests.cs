using Game.Logic;
using Game.Server.Player;
using Metaplay.Core;
using NUnit.Framework;
using System;

namespace Game.Server.Tests
{
    /// <summary>
    /// Tests the actor's rate limit on spin messages (<see cref="WheelSpinThrottle"/>), in particular that
    /// <b>every request gets an answer</b>.
    /// <para>
    /// The client locks its Spin button when pressed and unlocks it only on a payout or a refusal. A throttle
    /// that dropped a request would leave the button showing "Spinning…" until the page was reloaded. Unlike a
    /// daily reward, a spin can be repeated right away, so a real player can hit the throttle.
    /// </para>
    /// </summary>
    [TestFixture]
    public class WheelSpinThrottleTests
    {
        static readonly MetaTime Noon = MetaTime.FromDateTime(new DateTime(2026, 3, 10, 12, 0, 0, DateTimeKind.Utc));

        [Test]
        public void AnOpenThrottleActsOnTheRequest()
        {
            Assert.That(WheelSpinThrottle.Open.RefusalAt(Noon), Is.EqualTo(SpinRefusal.None));
        }

        /// <summary>
        /// A rapid second request is refused, not dropped. The refusal unlocks the client's Spin button.
        /// </summary>
        [Test]
        public void ARapidSecondRequestIsRefusedRatherThanDropped()
        {
            WheelSpinThrottle throttle = WheelSpinThrottle.Open.AfterRequestAt(Noon);

            Assert.That(throttle.RefusalAt(Noon), Is.EqualTo(SpinRefusal.TooFast));
            Assert.That(throttle.RefusalAt(Noon + MetaDuration.FromMilliseconds(1)), Is.EqualTo(SpinRefusal.TooFast));
        }

        /// <summary>
        /// The throttle reopens after <see cref="WheelSpinThrottle.MinInterval"/>, which is shorter than a player
        /// needs to read a result, press Done and press Spin again, even with reduced motion.
        /// </summary>
        [Test]
        public void TheWindowReopensAndIsShorterThanARealSpinAgain()
        {
            WheelSpinThrottle throttle = WheelSpinThrottle.Open.AfterRequestAt(Noon);

            Assert.That(throttle.RefusalAt(Noon + WheelSpinThrottle.MinInterval), Is.EqualTo(SpinRefusal.None));
            Assert.That(WheelSpinThrottle.MinInterval, Is.LessThanOrEqualTo(MetaDuration.FromMilliseconds(500)),
                "a window a real Spin again can land inside puts the throttle on the feature's happy path");
        }

        /// <summary>Each processed request restarts the interval from the time of that request.</summary>
        [Test]
        public void TakingARequestMovesTheWindowOnFromThatRequest()
        {
            MetaTime          later    = Noon + MetaDuration.FromSeconds(30);
            WheelSpinThrottle throttle = WheelSpinThrottle.Open.AfterRequestAt(Noon).AfterRequestAt(later);

            Assert.That(throttle.RefusalAt(later + MetaDuration.FromMilliseconds(1)), Is.EqualTo(SpinRefusal.TooFast));
            Assert.That(throttle.RefusalAt(later + WheelSpinThrottle.MinInterval), Is.EqualTo(SpinRefusal.None));
        }
    }
}
