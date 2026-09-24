using Game.Logic;
using Metaplay.Core.Model;

namespace Game.Server.Player
{
    /// <summary>
    /// Tracks whether a daily reward claim has been enqueued for an activation and has not run yet. Enqueuing a
    /// <b>synchronized</b> server action does not change the actor's model until the client runs it, so without
    /// this record a second request would enqueue a second action and tell the client "granted" twice. The action
    /// itself refuses a day that is already paid, so this record is not what prevents a double payout.
    /// </summary>
    public readonly struct DailyRewardClaimInFlight
    {
        /// <summary>The activation a claim was enqueued for, or -1 while nothing is in flight.</summary>
        public int Activation { get; }

        DailyRewardClaimInFlight(int activation)
        {
            Activation = activation;
        }

        /// <summary>No claim pending. The state of a newly started actor.</summary>
        public static readonly DailyRewardClaimInFlight Idle = new DailyRewardClaimInFlight(-1);

        /// <summary>Returns the in-flight record for a claim enqueued for <paramref name="activation"/>.</summary>
        public static DailyRewardClaimInFlight Enqueued(int activation) => new DailyRewardClaimInFlight(activation);

        /// <summary>
        /// Returns the in-flight record after the actor executed <paramref name="action"/>. A daily reward claim action resets
        /// it to <see cref="Idle"/> even when the claim failed, because a failed claim (for example after a config
        /// publish lowered a wallet cap) leaves the day unclaimed. The actor calls this from the SDK's
        /// <c>OnAfterAction</c>, which runs for a synchronized action whether the client or the server ran it.
        /// </summary>
        public DailyRewardClaimInFlight After(ModelAction action) => action is PlayerDailyRewardClaimed ? Idle : this;

        /// <summary>Whether the claim enqueued for <paramref name="activation"/> is still waiting to run.</summary>
        public bool IsInFlight(int activation) => Activation >= 0 && Activation == activation;

        public override string ToString() => Activation < 0 ? "idle" : $"activation {Activation} enqueued";
    }
}
