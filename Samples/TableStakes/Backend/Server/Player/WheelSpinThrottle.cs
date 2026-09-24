using Game.Logic;
using Metaplay.Core;

namespace Game.Server.Player
{
    /// <summary>
    /// Rate limit for spin messages from one client. <see cref="PendingWheelDraw"/> and the model's guards already make
    /// a repeated request harmless, so the throttle only limits the policy checks and replies a client can cause.
    /// <para>
    /// <b>A throttled request is answered with a refusal, never dropped.</b> A real request can fall inside the
    /// window (with reduced motion, a player can spin, press Done and spin again in under a second), and only a
    /// payout or a refusal unlocks the client's Spin button. The daily reward can drop a throttled claim because a
    /// claim can succeed only once a day.
    /// </para>
    /// </summary>
    public readonly struct WheelSpinThrottle
    {
        /// <summary>
        /// The minimum interval between spin messages that this actor processes. It is short enough that a player
        /// spinning again does not hit it, and long enough to limit an automated client.
        /// </summary>
        public static readonly MetaDuration MinInterval = MetaDuration.FromMilliseconds(250);

        readonly RequestThrottle _throttle;

        /// <summary>The earliest time the next request is processed.</summary>
        public MetaTime NextAt => _throttle.NextAt;

        WheelSpinThrottle(RequestThrottle throttle)
        {
            _throttle = throttle;
        }

        /// <summary>No request received yet. The state of a newly started actor.</summary>
        public static readonly WheelSpinThrottle Open = new WheelSpinThrottle(RequestThrottle.Open(MinInterval));

        /// <summary>
        /// Returns <see cref="SpinRefusal.None"/> if a request arriving at <paramref name="now"/> should be
        /// processed, or <see cref="SpinRefusal.TooFast"/> if it should be answered with that refusal. There is no
        /// result that drops the request.
        /// </summary>
        public SpinRefusal RefusalAt(MetaTime now) => _throttle.IsOpenAt(now) ? SpinRefusal.None : SpinRefusal.TooFast;

        /// <summary>Returns the throttle after a request at <paramref name="now"/> was processed.</summary>
        public WheelSpinThrottle AfterRequestAt(MetaTime now) => new WheelSpinThrottle(_throttle.AfterRequestAt(now));

        public override string ToString() => _throttle.ToString();
    }
}
