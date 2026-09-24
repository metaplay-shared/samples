using Metaplay.Core;
using Metaplay.Core.Model;

namespace Game.Logic
{
    /// <summary>
    /// One resolved spin, kept so the client can show the result again after a disconnect during the reveal
    /// (<c>docs/spin-wheel.md</c>, "Interrupted-spin recovery"). It is a record, not a claim: the reward was
    /// granted when the spin resolved, and no code pays anything based on a receipt. It stores the table, sector
    /// and reward it resolved with, so a config published before the reveal does not change what the player sees.
    /// </summary>
    [MetaSerializable]
    public class SpinReceipt
    {
        /// <summary>The player's spin number, counting from one. Only increases.</summary>
        [MetaMember(1)] public int Ordinal { get; private set; }

        /// <summary>The published wheel table the sector was drawn from.</summary>
        [MetaMember(2)] public WheelTableId Table { get; private set; }

        /// <summary>The stable ID of the drawn sector.</summary>
        [MetaMember(3)] public WheelSectorId SectorId { get; private set; }

        /// <summary>The zero-based index of the drawn sector, so the client can point the wheel at it again.</summary>
        [MetaMember(4)] public int SectorIndex { get; private set; }

        /// <summary>The reward that was paid.</summary>
        [MetaMember(5)] public RewardBundle Reward { get; private set; }

        /// <summary>The server time when the spin resolved.</summary>
        [MetaMember(6)] public MetaTime ResolvedAt { get; private set; }

        /// <summary>
        /// The correlation ID of the spin's two currency events and its spin event, so an operator can find
        /// them from the receipt without searching by time.
        /// </summary>
        [MetaMember(7)] public AnalyticsCorrelationId Correlation { get; private set; }

        public SpinReceipt() { }

        public SpinReceipt(
            int                    ordinal,
            WheelTableId           table,
            WheelSectorId          sectorId,
            int                    sectorIndex,
            RewardBundle           reward,
            MetaTime               resolvedAt,
            AnalyticsCorrelationId correlation)
        {
            Ordinal     = ordinal;
            Table       = table;
            SectorId    = sectorId;
            SectorIndex = sectorIndex;
            Reward      = reward;
            ResolvedAt  = resolvedAt;
            Correlation = correlation;
        }

        public override string ToString() => $"spin {Ordinal}: {SectorId} (sector {SectorIndex + 1} of {Table}) paid {Reward}";
    }

    /// <summary>
    /// One player's spin wheel state (<c>docs/spin-wheel.md</c>). A resolved but unacknowledged result is shown
    /// before another spin is accepted, so a player who disconnected during the reveal still sees the reward.
    /// <para>
    /// Only <see cref="Apply"/> and <see cref="Acknowledge"/> write this state. Both are <c>internal</c> and each
    /// has one <see cref="PlayerModel"/> caller. The <see cref="Apply"/> caller throws if a result is pending, so
    /// a result cannot be overwritten before it is seen. The wheel has no cooldown: only the token balance limits
    /// spins.
    /// </para>
    /// </summary>
    [MetaSerializable]
    public class SpinWheelState
    {
        /// <summary>
        /// How many spins the player has resolved in total. Only increases. A spin request names the ordinal it
        /// expects (this value plus one), so a replayed request is refused as stale.
        /// </summary>
        [MetaMember(1)] public int TotalResolvedSpins { get; private set; }

        /// <summary>The last resolved spin, or null for a player who has never spun.</summary>
        [MetaMember(2)] public SpinReceipt LastResolvedSpin { get; private set; }

        /// <summary>
        /// The highest ordinal the player acknowledged with Done or Spin again. Zero when the player has never
        /// acknowledged a spin, because ordinals start at one.
        /// </summary>
        [MetaMember(3)] public int LastAcknowledgedOrdinal { get; private set; }

        public SpinWheelState() { }

        /// <summary>Whether this player has ever spun.</summary>
        public bool HasSpun => TotalResolvedSpins > 0;

        /// <summary>
        /// Whether a resolved result is waiting to be shown. While true, the screen shows that result and the
        /// server refuses another spin, so an unseen receipt is never overwritten.
        /// </summary>
        public bool HasPendingReceipt => LastResolvedSpin != null && LastResolvedSpin.Ordinal > LastAcknowledgedOrdinal;

        /// <summary>The receipt waiting to be shown, or null when there is none.</summary>
        public SpinReceipt PendingReceipt => HasPendingReceipt ? LastResolvedSpin : null;

        /// <summary>The ordinal the next spin would produce.</summary>
        public int NextOrdinal => TotalResolvedSpins + 1;

        /// <summary>
        /// Records one committed spin. See the class summary for who may call it.
        /// </summary>
        internal void Apply(SpinReceipt receipt)
        {
            TotalResolvedSpins = receipt.Ordinal;
            LastResolvedSpin   = receipt;
        }

        /// <summary>
        /// Marks the last result as seen. Changes only <see cref="LastAcknowledgedOrdinal"/>.
        /// </summary>
        internal void Acknowledge()
        {
            if (LastResolvedSpin != null)
                LastAcknowledgedOrdinal = LastResolvedSpin.Ordinal;
        }

        public override string ToString() =>
            !HasSpun ? "never spun"
                     : $"{TotalResolvedSpins} spins, last {LastResolvedSpin}, acknowledged {LastAcknowledgedOrdinal}";
    }
}
