using Metaplay.Core.Analytics;
using Metaplay.Core.Model;
using Metaplay.Core.Player;

namespace Game.Logic
{
    /// <summary>
    /// A spin resolved on one sector. Emitted once per committed spin: a duplicate or replayed request is refused
    /// by the once-per-ordinal check and emits nothing, and acknowledging a result emits nothing.
    /// <para>
    /// It does not record the random seed, the generator state or the client's animation angle, so it cannot be
    /// used to predict the next draw. The currency changes are separate <c>economy_transaction</c> events with the
    /// same correlation ID, a token spend and a prize grant that are never netted.
    /// </para>
    /// </summary>
    [AnalyticsEvent(AnalyticsEventCodes.WheelSpinResolved, displayName: "Wheel spin resolved", docString: "The player spent a spin token and the wheel resolved on one sector.")]
    [AnalyticsAlias("wheel_spin_resolved")]
    [AnalyticsEventKeywords(AnalyticsKeywords.SpinWheel, AnalyticsKeywords.Source, AnalyticsKeywords.Sink)]
    public class PlayerEventWheelSpinResolved : PlayerEventBase
    {
        /// <summary>The published wheel table the sector was drawn from.</summary>
        [MetaMember(1)] public WheelTableId Table { get; private set; }

        /// <summary>The stable ID of the drawn sector. Join on this, not on <see cref="Sector"/>, which is only its position.</summary>
        [MetaMember(2)] public WheelSectorId Segment { get; private set; }

        /// <summary>The sector's 1-based position on the wheel, up to <see cref="WheelTableInfo.NumSectors"/>.</summary>
        [MetaMember(3)] public int Sector { get; private set; }

        /// <summary>The prize tier, which decides how the client celebrates the result. Included for grouping in analytics.</summary>
        [MetaMember(4)] public WheelPrizeTier Tier { get; private set; }

        /// <summary>The currency and amount the sector paid. A sector pays exactly one currency.</summary>
        [MetaMember(5)] public CurrencyType RewardCurrency { get; private set; }
        [MetaMember(6)] public int          RewardAmount   { get; private set; }

        /// <summary>The player's spin number, counting from one. Only increases.</summary>
        [MetaMember(7)] public int SpinOrdinal { get; private set; }

        /// <summary>The spin token balance after the spin, including the spent token and any token won.</summary>
        [MetaMember(8)] public int SpinTokensAfter { get; private set; }

        /// <summary>The same ID as the two currency events of this spin, so they can be joined.</summary>
        [MetaMember(9)] public AnalyticsCorrelationId Correlation { get; private set; }

        public override string EventDescription =>
            $"Spin {SpinOrdinal} landed on sector {Sector} and paid {RewardAmount} {RewardCurrency}"
            + (Tier == WheelPrizeTier.SpinAgain ? ", which is another spin" : "")
            + ".";

        public PlayerEventWheelSpinResolved() { }

        public PlayerEventWheelSpinResolved(
            WheelTableId           table,
            WheelSectorId         segment,
            int                    sector,
            WheelPrizeTier         tier,
            CurrencyType           rewardCurrency,
            int                    rewardAmount,
            int                    spinOrdinal,
            int                    spinTokensAfter,
            AnalyticsCorrelationId correlation)
        {
            Table           = table;
            Segment         = segment;
            Sector          = sector;
            Tier            = tier;
            RewardCurrency  = rewardCurrency;
            RewardAmount    = rewardAmount;
            SpinOrdinal     = spinOrdinal;
            SpinTokensAfter = spinTokensAfter;
            Correlation     = correlation;
        }
    }
}
