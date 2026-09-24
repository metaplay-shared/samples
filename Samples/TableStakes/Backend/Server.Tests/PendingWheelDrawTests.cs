using Game.Server.Player;
using Metaplay.Core;
using NUnit.Framework;
using System;

namespace Game.Server.Tests
{
    /// <summary>
    /// Tests <see cref="PendingWheelDraw"/> without a live server. A normal browser runs the payout action within
    /// milliseconds, so the states that only a delaying modified client reaches are hard to hit end to end.
    /// </summary>
    [TestFixture]
    public class PendingWheelDrawTests
    {
        static readonly MetaTime Noon = MetaTime.FromDateTime(new DateTime(2026, 3, 10, 12, 0, 0, DateTimeKind.Utc));

        /// <summary>
        /// A restarted actor starts with <see cref="PendingWheelDraw.Idle"/>. This is safe because the SDK persists
        /// an enqueued synchronized action with the model and runs it after the restart, so the pending draw
        /// still resolves for the spin it was drawn for.
        /// </summary>
        [Test]
        public void AnIdleGateHoldsNothing()
        {
            Assert.That(PendingWheelDraw.Idle.HasDrawFor(1), Is.False);
            Assert.That(PendingWheelDraw.Idle.HasDrawFor(0), Is.False);
            Assert.That(PendingWheelDraw.Idle.HasDrawFor(-1), Is.False,
                "the idle sentinel must not collide with any ordinal, its own included");
            Assert.That(PendingWheelDraw.Idle.MayResend(Noon), Is.True, "there is nothing to send, so nothing may be withheld");
        }

        /// <summary>
        /// The pending draw holds only the spin it was drawn for, so it does not affect other spins. After the spin
        /// resolves, the player's ordinal advances, so the next request is for a spin the pending draw does not hold and
        /// gets a new draw. The pending draw does not need to expire for this.
        /// </summary>
        [Test]
        public void ADifferentSpinIsNotTheOneThatWasDrawn()
        {
            PendingWheelDraw pending = PendingWheelDraw.Enqueued(ordinal: 4, sectorIndex: 7, now: Noon);

            Assert.That(pending.HasDrawFor(5), Is.False, "the spin after the one that just resolved was refused");
            Assert.That(pending.HasDrawFor(3), Is.False);
        }

        /// <summary>
        /// The pending draw keeps the drawn sector, so a spin request while the draw is pending gets the same sector
        /// instead of a new one. If the action fails, for example because a config publish lowered a wallet cap
        /// before it ran, the ordinal does not change. The pending draw keeps the draw indefinitely, so the next request
        /// gets the same sector. A pending draw with a deadline would allow a second draw for a client that delays running
        /// the action.
        /// </summary>
        [Test]
        public void AnUnresolvedDrawIsStillTheSameDrawLongAfterwards()
        {
            PendingWheelDraw pending = PendingWheelDraw.Enqueued(ordinal: 4, sectorIndex: 7, now: Noon);

            Assert.That(pending.HasDrawFor(4), Is.True, "a second request found nothing held, so the wheel would be drawn a second time for one spin");
            Assert.That(pending.SectorIndex, Is.EqualTo(7));

            MetaTime muchLater = Noon + MetaDuration.FromMinutes(5);
            Assert.That(pending.Resent(muchLater).HasDrawFor(4), Is.True);
            Assert.That(pending.Resent(muchLater).SectorIndex, Is.EqualTo(7), "sending the draw again changed what it says");
        }

        /// <summary>
        /// <see cref="PendingWheelDraw.ResendInterval"/> limits only how often the same draw is sent again, so
        /// repeated presses of Spin cannot queue up copies of one action.
        /// </summary>
        [Test]
        public void TheHeldDrawGoesOutAgainOnlyOnTheInterval()
        {
            PendingWheelDraw pending = PendingWheelDraw.Enqueued(ordinal: 4, sectorIndex: 7, now: Noon);

            Assert.That(pending.MayResend(Noon), Is.False);
            Assert.That(pending.MayResend(Noon + PendingWheelDraw.ResendInterval - MetaDuration.FromMilliseconds(1)), Is.False);
            Assert.That(pending.MayResend(Noon + PendingWheelDraw.ResendInterval), Is.True);

            // Sending the draw again restarts the interval.
            PendingWheelDraw resent = pending.Resent(Noon + PendingWheelDraw.ResendInterval);
            Assert.That(resent.MayResend(Noon + PendingWheelDraw.ResendInterval), Is.False);
            Assert.That(resent.MayResend(Noon + PendingWheelDraw.ResendInterval + PendingWheelDraw.ResendInterval), Is.True);
        }
    }
}
