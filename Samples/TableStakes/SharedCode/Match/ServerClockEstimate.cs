using Metaplay.Core;

namespace Game.Logic
{
    /// <summary>
    /// The client's estimate of the server clock, from round trips the client times itself. The host writes deadlines
    /// from its own clock, and a device clock is often off by minutes. A device clock that is ahead disables the hand
    /// while the host still waits, and one that is behind shows a deadline running after the host acted on it.
    /// The SDK's own estimator, <c>ServerClockEstimator</c>, is internal. The public
    /// <c>Connection.ServerOptions.ServerTime</c> is set only at session start and lags by the handshake, login and
    /// config download time. The estimate assumes the request and the reply each took half the round trip, so its
    /// error is at most half the round trip (<see cref="Uncertainty"/>).
    /// </summary>
    public sealed class ServerClockEstimate
    {
        /// <summary>
        /// How long the current sample is protected from replacement by a sample with a larger error bound. After
        /// this, any valid sample replaces it, so the estimate follows a device clock that was changed or drifted.
        /// <para>
        /// A stale estimate is still used. Clock drift over this interval is small, while falling back to the
        /// device clock could reintroduce an error of minutes and would move authoritative time by that error in
        /// one step.
        /// </para>
        /// </summary>
        public static readonly MetaDuration StaleAfter = MetaDuration.FromSeconds(60);

        /// <summary>
        /// How often the client measures a new round trip. It is much shorter than <see cref="StaleAfter"/>, so
        /// the estimate becomes stale only after several samples in a row fail, and the slow first sample is
        /// replaced soon after connecting.
        /// </summary>
        public static readonly MetaDuration ResampleInterval = MetaDuration.FromSeconds(20);

        /// <summary>
        /// The longest round trip <see cref="TryTakeSample"/> accepts. A longer one may be a slow network or a device
        /// clock that jumped forward, and the two cannot be told apart. Either way the error bound, half the
        /// round trip, would be too large to be useful for move deadlines. The next sample follows within
        /// <see cref="ResampleInterval"/>.
        /// </summary>
        public static readonly MetaDuration MaxPlausibleRoundTrip = MetaDuration.FromSeconds(30);

        /// <summary>
        /// The instance the client uses for all server time readings. There is one per app because every
        /// countdown the client shows is against the same server.
        /// </summary>
        public static ServerClockEstimate Shared { get; } = new ServerClockEstimate();

        MetaDuration _offset;
        MetaDuration _uncertainty;
        MetaTime     _takenAt;
        bool         _hasEstimate;

        /// <summary>Whether any round trip has been measured yet.</summary>
        public bool HasEstimate => _hasEstimate;

        /// <summary>The amount to add to the device clock to get the server clock. Zero until the first sample.</summary>
        public MetaDuration Offset => _offset;

        /// <summary>The maximum error of <see cref="Offset"/>: half the sample's round trip.</summary>
        public MetaDuration Uncertainty => _uncertainty;

        /// <summary>The estimated server time for the device time <paramref name="deviceNow"/>.</summary>
        public MetaTime EstimateNow(MetaTime deviceNow) => deviceNow + _offset;

        /// <summary>
        /// Whether the current sample is older than <see cref="StaleAfter"/>. A stale estimate is still used, but
        /// any valid sample replaces it.
        /// </summary>
        public bool IsStaleAt(MetaTime deviceNow) => _hasEstimate && deviceNow - _takenAt >= StaleAfter;

        /// <summary>
        /// Whether to measure a new round trip now: always before the first sample, then every
        /// <see cref="ResampleInterval"/>. Also true when the device clock moved back before the last sample time.
        /// </summary>
        public bool WantsSampleAt(MetaTime deviceNow)
            => !_hasEstimate || deviceNow < _takenAt || deviceNow - _takenAt >= ResampleInterval;

        /// <summary>
        /// Record a round trip: the request was sent at device time <paramref name="sentAt"/>, the server reported
        /// <paramref name="serverTime"/>, and the reply arrived at device time <paramref name="receivedAt"/>.
        /// </summary>
        /// <returns>Whether this sample replaced the current estimate.</returns>
        public bool TryTakeSample(MetaTime sentAt, MetaTime serverTime, MetaTime receivedAt)
        {
            // A device clock change during the round trip shows up as a negative or implausibly long round trip,
            // and the sample's offset would be wrong by the size of the change. Drop such samples.
            if (receivedAt < sentAt)
                return false;

            MetaDuration roundTrip = receivedAt - sentAt;
            if (roundTrip > MaxPlausibleRoundTrip)
                return false;

            MetaDuration uncertainty = MetaDuration.FromMilliseconds(roundTrip.Milliseconds / 2);

            bool isStale = IsStaleAt(receivedAt);
            if (_hasEstimate && !isStale && uncertainty > _uncertainty)
                return false;

            // The server answered about half a round trip before receivedAt.
            _offset      = (serverTime + uncertainty) - receivedAt;
            _uncertainty = uncertainty;
            _takenAt     = receivedAt;
            _hasEstimate = true;
            return true;
        }

        /// <summary>Discard the estimate, so a new session measures its own round trips.</summary>
        public void Reset()
        {
            _offset      = MetaDuration.Zero;
            _uncertainty = MetaDuration.Zero;
            _takenAt     = MetaTime.Epoch;
            _hasEstimate = false;
        }

        public override string ToString()
            => _hasEstimate ? $"server clock {_offset} ahead (±{_uncertainty})" : "server clock unmeasured";
    }
}
