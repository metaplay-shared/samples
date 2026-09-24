using Metaplay.Core;

namespace Game.Server
{
    /// <summary>
    /// A minimum interval between the client requests an actor processes, measured on <see cref="MetaTime"/> so it
    /// follows the server's debug time offset. The caller decides what a throttled request gets: the rename and daily
    /// reward handlers drop it, and <see cref="Player.WheelSpinThrottle"/> answers it with a refusal. Kept in memory
    /// only.
    /// </summary>
    public readonly struct RequestThrottle
    {
        /// <summary>The minimum time between two processed requests.</summary>
        public MetaDuration MinInterval { get; }

        /// <summary>The earliest time the next request is processed.</summary>
        public MetaTime NextAt { get; }

        RequestThrottle(MetaDuration minInterval, MetaTime nextAt)
        {
            MinInterval = minInterval;
            NextAt      = nextAt;
        }

        /// <summary>A throttle that has received no request yet.</summary>
        public static RequestThrottle Open(MetaDuration minInterval) => new RequestThrottle(minInterval, MetaTime.Epoch);

        /// <summary>Whether a request arriving at <paramref name="now"/> should be processed.</summary>
        public bool IsOpenAt(MetaTime now) => now >= NextAt;

        /// <summary>Returns the throttle after a request at <paramref name="now"/> was processed.</summary>
        public RequestThrottle AfterRequestAt(MetaTime now) => new RequestThrottle(MinInterval, now + MinInterval);

        public override string ToString() => $"open from {NextAt}";
    }
}
