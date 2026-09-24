using Metaplay.Core;
using NUnit.Framework;

namespace Game.Logic.Tests
{
    /// <summary>
    /// Tests for <see cref="ServerClockEstimate"/>, the client's estimate of the host's clock. Every deadline a table
    /// counts down to is a host timestamp, so without the estimate a wrong device clock could disable the
    /// player's hand while the host is still waiting for the move.
    /// </summary>
    [TestFixture]
    public class ServerClockEstimateTests
    {
        static readonly MetaTime DeviceClockStart = MetaTime.FromDateTime(new System.DateTime(2026, 8, 31, 12, 0, 0, System.DateTimeKind.Utc));

        /// <summary>
        /// How far ahead of the host the device clock runs in these tests. It is large so that a test that reads
        /// the device clock instead of the estimate fails.
        /// </summary>
        static readonly MetaDuration DeviceError = MetaDuration.FromSeconds(120);

        static MetaTime ServerTimeAt(MetaTime deviceTime) => deviceTime - DeviceError;

        [Test]
        public void WithNoSampleTheEstimateIsTheDeviceClock()
        {
            ServerClockEstimate clock = new ServerClockEstimate();

            Assert.That(clock.HasEstimate, Is.False);
            Assert.That(clock.EstimateNow(DeviceClockStart), Is.EqualTo(DeviceClockStart));
        }

        [Test]
        public void ASymmetricRoundTripEstimatesTheHostsClockExactly()
        {
            // The host answers exactly halfway through the round trip, so the estimate is exact and the
            // uncertainty is half the round trip.
            ServerClockEstimate clock = new ServerClockEstimate();
            MetaTime    sentAt     = DeviceClockStart;
            MetaTime    receivedAt = DeviceClockStart + MetaDuration.FromMilliseconds(100);
            MetaTime    serverTime = ServerTimeAt(DeviceClockStart + MetaDuration.FromMilliseconds(50));

            Assert.That(clock.TryTakeSample(sentAt, serverTime, receivedAt), Is.True);
            Assert.That(clock.Uncertainty, Is.EqualTo(MetaDuration.FromMilliseconds(50)));
            Assert.That(clock.EstimateNow(receivedAt), Is.EqualTo(ServerTimeAt(receivedAt)));

            // The device clock differs from the estimate by the whole device error.
            Assert.That(receivedAt - clock.EstimateNow(receivedAt), Is.EqualTo(DeviceError));
        }

        [TestCase(0)]
        [TestCase(40)]
        [TestCase(100)]
        [TestCase(160)]
        [TestCase(200)]
        public void TheErrorNeverExceedsHalfTheRoundTrip(int hostAnsweredAfterMs)
        {
            // The error comes only from the two legs of the round trip taking different times. The worst case is
            // one leg taking the whole round trip. Both extremes are checked because their errors have opposite
            // signs.
            ServerClockEstimate clock      = new ServerClockEstimate();
            MetaTime    sentAt     = DeviceClockStart;
            MetaTime    receivedAt = DeviceClockStart + MetaDuration.FromMilliseconds(200);
            MetaTime    serverTime = ServerTimeAt(DeviceClockStart + MetaDuration.FromMilliseconds(hostAnsweredAfterMs));

            Assert.That(clock.TryTakeSample(sentAt, serverTime, receivedAt), Is.True);

            MetaDuration error          = clock.EstimateNow(receivedAt) - ServerTimeAt(receivedAt);
            MetaDuration errorMagnitude = error > MetaDuration.Zero ? error : -error;

            Assert.That(errorMagnitude, Is.LessThanOrEqualTo(clock.Uncertainty));
            Assert.That(clock.Uncertainty, Is.EqualTo(MetaDuration.FromMilliseconds(100)));
        }

        [Test]
        public void ABetterMeasuredSampleReplacesAWorseOneAndAWorseOneIsIgnored()
        {
            ServerClockEstimate clock = new ServerClockEstimate();

            // A slow round trip first.
            Assert.That(clock.TryTakeSample(DeviceClockStart, ServerTimeAt(DeviceClockStart), DeviceClockStart + MetaDuration.FromMilliseconds(400)), Is.True);
            Assert.That(clock.Uncertainty, Is.EqualTo(MetaDuration.FromMilliseconds(200)));

            // A slower round trip has a larger uncertainty than the current sample, so it is ignored.
            MetaTime later = DeviceClockStart + MetaDuration.FromSeconds(5);
            Assert.That(clock.TryTakeSample(later, ServerTimeAt(later), later + MetaDuration.FromMilliseconds(900)), Is.False);
            Assert.That(clock.Uncertainty, Is.EqualTo(MetaDuration.FromMilliseconds(200)));

            // A faster round trip replaces the current sample.
            Assert.That(clock.TryTakeSample(later, ServerTimeAt(later), later + MetaDuration.FromMilliseconds(20)), Is.True);
            Assert.That(clock.Uncertainty, Is.EqualTo(MetaDuration.FromMilliseconds(10)));
        }

        [Test]
        public void AStaleSampleIsReplacedHoweverItMeasured()
        {
            // If a precise sample could never be replaced, the estimate would go wrong once the device clock is
            // corrected or drifts. After ServerClockEstimate.StaleAfter, any sample replaces it regardless of its round trip.
            ServerClockEstimate clock = new ServerClockEstimate();
            Assert.That(clock.TryTakeSample(DeviceClockStart, ServerTimeAt(DeviceClockStart), DeviceClockStart + MetaDuration.FromMilliseconds(10)), Is.True);

            MetaTime justInside = DeviceClockStart + ServerClockEstimate.StaleAfter - MetaDuration.FromSeconds(1);
            Assert.That(clock.TryTakeSample(justInside, ServerTimeAt(justInside), justInside + MetaDuration.FromMilliseconds(400)), Is.False);

            MetaTime pastWindow = DeviceClockStart + ServerClockEstimate.StaleAfter + MetaDuration.FromSeconds(1);
            Assert.That(clock.TryTakeSample(pastWindow, ServerTimeAt(pastWindow), pastWindow + MetaDuration.FromMilliseconds(400)), Is.True);
            Assert.That(clock.Uncertainty, Is.EqualTo(MetaDuration.FromMilliseconds(200)));
        }

        [Test]
        public void ADeviceClockThatJumpedBackMidFlightIsNotAPerfectlyMeasuredSample()
        {
            // A negative round trip means the device clock moved between the two timestamps, so the sample is
            // dropped. Clamping it to a zero round trip would claim zero uncertainty, and that sample would then
            // beat every real measurement until it went stale.
            ServerClockEstimate clock      = new ServerClockEstimate();
            MetaTime    sentAt     = DeviceClockStart + MetaDuration.FromSeconds(30);
            MetaTime    receivedAt = DeviceClockStart;

            Assert.That(clock.TryTakeSample(sentAt, ServerTimeAt(receivedAt), receivedAt), Is.False);
            Assert.That(clock.HasEstimate, Is.False);
            Assert.That(clock.EstimateNow(receivedAt), Is.EqualTo(receivedAt), "a dropped sample still moved the estimate");
        }

        [Test]
        public void ADeviceClockThatJumpedForwardMidFlightIsDropped()
        {
            // A round trip longer than ServerClockEstimate.MaxPlausibleRoundTrip means the device clock jumped forward.
            // The sample's uncertainty would be huge, but its offset would be wrong by the whole jump, so it is
            // dropped.
            ServerClockEstimate clock      = new ServerClockEstimate();
            MetaTime    sentAt     = DeviceClockStart;
            MetaTime    receivedAt = DeviceClockStart + MetaDuration.FromSeconds(300);

            Assert.That(clock.TryTakeSample(sentAt, ServerTimeAt(sentAt), receivedAt), Is.False);
            Assert.That(clock.HasEstimate, Is.False);
        }

        [Test]
        public void AJumpedSampleDoesNotDisplaceTheEstimateInForce()
        {
            ServerClockEstimate clock = new ServerClockEstimate();
            Assert.That(clock.TryTakeSample(DeviceClockStart, ServerTimeAt(DeviceClockStart), DeviceClockStart + MetaDuration.FromMilliseconds(200)), Is.True);
            MetaDuration measured = clock.Offset;

            MetaTime later = DeviceClockStart + MetaDuration.FromSeconds(5);
            Assert.That(clock.TryTakeSample(later, ServerTimeAt(later), later - MetaDuration.FromSeconds(10)), Is.False);
            Assert.That(clock.TryTakeSample(later, ServerTimeAt(later), later + MetaDuration.FromSeconds(300)), Is.False);

            Assert.That(clock.Offset, Is.EqualTo(measured), "a sample the round trip cannot vouch for replaced a measured one");
            Assert.That(clock.Uncertainty, Is.EqualTo(MetaDuration.FromMilliseconds(100)));
        }

        [Test]
        public void ARoundTripAtTheLimitIsStillMeasured()
        {
            // MaxPlausibleRoundTrip rejects clock jumps, not slow round trips. A cold boot under parallel load can
            // take tens of seconds, and a sample measured over one is imprecise but still valid.
            ServerClockEstimate clock      = new ServerClockEstimate();
            MetaTime            receivedAt = DeviceClockStart + ServerClockEstimate.MaxPlausibleRoundTrip;

            Assert.That(clock.TryTakeSample(DeviceClockStart, ServerTimeAt(DeviceClockStart), receivedAt), Is.True);
            Assert.That(clock.Uncertainty, Is.EqualTo(MetaDuration.FromMilliseconds(ServerClockEstimate.MaxPlausibleRoundTrip.Milliseconds / 2)));
        }

        [Test]
        public void AFreshSampleIsWantedOnceTheResampleIntervalHasPassed()
        {
            // With one sample per session, that sample would come from the cold boot, which has the slowest round
            // trip of the session, and nothing would replace it.
            ServerClockEstimate clock = new ServerClockEstimate();
            Assert.That(clock.WantsSampleAt(DeviceClockStart), Is.True, "an unmeasured clock does not ask for a sample");

            MetaTime receivedAt = DeviceClockStart + MetaDuration.FromMilliseconds(100);
            Assert.That(clock.TryTakeSample(DeviceClockStart, ServerTimeAt(DeviceClockStart), receivedAt), Is.True);

            Assert.That(clock.WantsSampleAt(receivedAt), Is.False);
            Assert.That(clock.WantsSampleAt(receivedAt + ServerClockEstimate.ResampleInterval - MetaDuration.FromSeconds(1)), Is.False);
            Assert.That(clock.WantsSampleAt(receivedAt + ServerClockEstimate.ResampleInterval), Is.True);

            // A device clock corrected backwards leaves the sample in the future, which also requests a new sample.
            Assert.That(clock.WantsSampleAt(receivedAt - MetaDuration.FromSeconds(1)), Is.True);
        }

        [Test]
        public void TheEstimateGoesStaleWellAfterItWouldHaveBeenResampled()
        {
            // Once the estimate is stale, any sample replaces it. StaleAfter must be well past ResampleInterval so
            // that the estimate goes stale only after several resamples in a row have failed.
            Assert.That(ServerClockEstimate.StaleAfter, Is.GreaterThan(ServerClockEstimate.ResampleInterval + ServerClockEstimate.ResampleInterval));

            ServerClockEstimate clock = new ServerClockEstimate();
            Assert.That(clock.IsStaleAt(DeviceClockStart), Is.False, "an unmeasured clock reports itself stale");

            MetaTime receivedAt = DeviceClockStart + MetaDuration.FromMilliseconds(100);
            Assert.That(clock.TryTakeSample(DeviceClockStart, ServerTimeAt(DeviceClockStart), receivedAt), Is.True);

            Assert.That(clock.IsStaleAt(receivedAt + ServerClockEstimate.StaleAfter - MetaDuration.FromSeconds(1)), Is.False);
            Assert.That(clock.IsStaleAt(receivedAt + ServerClockEstimate.StaleAfter), Is.True);

            // A stale estimate is still used. Falling back to the device clock would reintroduce the device error
            // and shift every countdown on screen.
            Assert.That(clock.EstimateNow(receivedAt + ServerClockEstimate.StaleAfter),
                Is.EqualTo(ServerTimeAt(receivedAt + ServerClockEstimate.StaleAfter) - MetaDuration.FromMilliseconds(50)));
        }

        [Test]
        public void ResetForgetsTheEstimate()
        {
            ServerClockEstimate clock = new ServerClockEstimate();
            Assert.That(clock.TryTakeSample(DeviceClockStart, ServerTimeAt(DeviceClockStart), DeviceClockStart + MetaDuration.FromMilliseconds(10)), Is.True);

            clock.Reset();

            Assert.That(clock.HasEstimate, Is.False);
            Assert.That(clock.Offset, Is.EqualTo(MetaDuration.Zero));
            Assert.That(clock.EstimateNow(DeviceClockStart), Is.EqualTo(DeviceClockStart));
        }

        [Test]
        [NonParallelizable]
        public void ABeatStampedFromAuthoritativeTimeStillPlaysOnADeviceClockThatIsWrong()
        {
            // The table's trick-resolve beat is timestamped with AuthoritativeTime and must be checked against
            // AuthoritativeTime. On a device clock that runs fast, a device-clock reading would treat the beat as
            // over before its first frame (see MatchTableTime.IsBeatPlaying for what depends on the beat).
            try
            {
                MetaTime deviceNow = MetaTime.Now;
                Assert.That(ServerClockEstimate.Shared.TryTakeSample(deviceNow, deviceNow - DeviceError, deviceNow), Is.True);

                MetaTime startedAt = AuthoritativeTime.Now.Timestamp;
                MetaTime endsAt    = startedAt + MetaDuration.FromSeconds(30);

                Assert.That(MatchTableTime.IsBeatPlaying(startedAt, endsAt, AuthoritativeTime.Now), Is.True);

                // Guard for the test itself: the device clock must put the beat's end in the past. Otherwise the
                // check above would pass even when it reads the device clock.
                Assert.That(endsAt > MetaTime.Now, Is.False, "the device clock is not far enough out for this test to mean anything");
            }
            finally
            {
                ServerClockEstimate.Shared.Reset();
            }
        }

        [Test]
        [NonParallelizable]
        public void AuthoritativeTimeReadsTheSharedEstimate()
        {
            // AuthoritativeTime reads ServerClockEstimate.Shared, so the table checks move deadlines against the host's
            // clock, not the device's.
            try
            {
                MetaTime now = MetaTime.Now;
                Assert.That(ServerClockEstimate.Shared.TryTakeSample(now, now + MetaDuration.FromSeconds(30), now), Is.True);

                MetaDuration ahead = AuthoritativeTime.Now.Timestamp - MetaTime.Now;
                Assert.That(ahead, Is.GreaterThanOrEqualTo(MetaDuration.FromSeconds(29)));
                Assert.That(ahead, Is.LessThanOrEqualTo(MetaDuration.FromSeconds(31)));
            }
            finally
            {
                ServerClockEstimate.Shared.Reset();
            }
        }
    }
}
