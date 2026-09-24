using Metaplay.Core;
using Metaplay.Core.Config;
using Metaplay.Core.Model;
using System.Collections.Generic;

namespace Game.Logic
{
    /// <summary>Why a spin cannot happen. <see cref="None"/> is the spinnable case.</summary>
    [MetaSerializable]
    public enum SpinRefusal
    {
        None = 0,

        /// <summary>The player has no spin token. The screen points to the features that grant tokens.</summary>
        NoTokens = 1,

        /// <summary>
        /// A resolved result has not been seen yet. The wheel shows that result before it accepts another spin,
        /// so an unseen result is never overwritten.
        /// </summary>
        ResultPending = 2,

        /// <summary>The active table is missing or incomplete (see <see cref="WheelTableInfo.IsComplete"/>).</summary>
        ConfigUnavailable = 3,

        /// <summary>
        /// At least one sector would take a balance past its cap. Nothing is drawn and the token is kept. The
        /// check covers every sector, so the result never depends on which sector would have been drawn.
        /// </summary>
        WalletFull = 4,

        /// <summary>
        /// The requested ordinal is not the player's next one, for example on a retransmission, a stale browser
        /// tab or a second tap. Nothing is settled and the earlier result stays.
        /// </summary>
        StaleOrdinal = 5,

        /// <summary>
        /// Requests arrive faster than the player actor's rate limit allows. <b><see cref="SpinWheelPolicy"/>
        /// never returns this</b>, because the limit is about the connection, not the wheel. The actor sends a
        /// response instead of dropping the request, because the client keeps the Spin control locked until a
        /// response arrives.
        /// </summary>
        TooFast = 6,
    }

    /// <summary>
    /// The player's spin wheel status at one moment: whether they can spin, why not, and any result waiting
    /// to be shown. Computed, never stored.
    /// </summary>
    public readonly struct SpinWheelOutlook
    {
        /// <summary>The table a spin would use, or null when the config has no active table.</summary>
        public WheelTableInfo Table { get; }

        /// <summary>Why a spin is not possible now, or <see cref="SpinRefusal.None"/> when it is.</summary>
        public SpinRefusal Refusal { get; }

        /// <summary>How many spins the player's token balance pays for.</summary>
        public int SpinsAvailable { get; }

        /// <summary>The resolved result waiting to be shown, or null.</summary>
        public SpinReceipt PendingReceipt { get; }

        /// <summary>The ordinal of the next spin.</summary>
        public int NextOrdinal { get; }

        internal SpinWheelOutlook(WheelTableInfo table, SpinRefusal refusal, int spinsAvailable, SpinReceipt pendingReceipt, int nextOrdinal)
        {
            Table          = table;
            Refusal        = refusal;
            SpinsAvailable = spinsAvailable;
            PendingReceipt = pendingReceipt;
            NextOrdinal    = nextOrdinal;
        }

        /// <summary>Whether a spin is possible now.</summary>
        public bool CanSpin => Refusal == SpinRefusal.None;

        /// <summary>Whether a result must be shown before another spin is offered.</summary>
        public bool HasPendingReceipt => PendingReceipt != null;

        public override string ToString() =>
            CanSpin ? $"can spin ({SpinsAvailable} available, next ordinal {NextOrdinal})" : $"cannot spin: {Refusal}";
    }

    /// <summary>
    /// The spin wheel rules, as pure functions of the player's wheel state, wallet and the published table
    /// (<c>docs/spin-wheel.md</c>).
    /// <para>
    /// The server and the client both use these functions. Only the server <i>draws</i>: it picks the sector
    /// with a <see cref="RandomPCG"/> it owns and sends it in the action that commits the spin. Whether the
    /// player may spin is decided by the same function on both sides, so the screen and the server agree.
    /// </para>
    /// </summary>
    public static class SpinWheelPolicy
    {
        /// <summary>The cost of one spin.</summary>
        public static readonly CurrencyAmount SpinCost = CurrencyAmount.SpinTokens(1);

        /// <summary>The cause string passed to <see cref="AnalyticsCorrelationId.Create"/> for a spin.</summary>
        public const string CorrelationCause = "wheel_spin";

        /// <summary>
        /// The active wheel table, or null when the published config has none. Works on a config built in a test
        /// (<see cref="ConfigRefs.Resolve"/>).
        /// </summary>
        public static WheelTableInfo ActiveTable(SharedGameConfig config) =>
            ConfigRefs.Resolve(config?.Global?.ActiveWheelTable, config?.WheelTables);

        /// <summary>
        /// Draws one sector with equal probability for each sector, or returns -1 for a missing or incomplete
        /// table. This is the complete probability model: a reward on two sectors is twice as likely, which matches
        /// the wheel art and the displayed odds. There are no weights, no pity timer and no dependence on the player.
        /// In production <paramref name="rng"/> is held by the server and seeded at actor start, never from client
        /// input.
        /// </summary>
        public static int DrawSectorIndex(RandomPCG rng, WheelTableInfo table)
        {
            if (table == null || !table.IsComplete)
                return -1;
            return rng.NextInt(table.Sectors.Count);
        }

        /// <summary>
        /// Whether every sector of <paramref name="table"/> could be paid into <paramref name="wallet"/>
        /// without exceeding a cap.
        /// <para>
        /// It is checked <b>before</b> the draw, so a player near a cap is refused up front instead of losing a
        /// token on a spin the wallet then refuses. Because every sector is checked, the result never depends on
        /// which sector would have been drawn.
        /// </para>
        /// </summary>
        public static bool EveryPrizeFits(PlayerWalletModel wallet, WalletCaps caps, WheelTableInfo table)
        {
            if (wallet == null || table == null || !table.IsComplete)
                return false;

            EconomyContentId              contentId = ContentIdOf(table);
            IReadOnlyList<CurrencyAmount> price     = SpinPrice();
            foreach (WheelSectorInfo sector in table.Sectors)
            {
                if (sector?.Reward == null)
                    return false;

                // Settle the whole exchange, not only the grant, so the check matches the real settlement,
                // which applies the token spend first.
                WalletSettlement settlement = wallet.Settle(SpinTransaction(sector, contentId, price), caps);
                if (settlement.Refusal == WalletRefusal.CapExceeded || settlement.Refusal == WalletRefusal.Malformed)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// The wallet transaction for one spin: one token spent and one sector's reward granted as one exchange, so
        /// the player is never charged without receiving the prize. The spend uses
        /// <see cref="EconomyReason.WheelSpinCost"/> and the grant <see cref="EconomyReason.WheelPrize"/>, so
        /// analytics can filter the wheel's token spending separately (<c>docs/economy.md</c>).
        /// </summary>
        public static WalletTransaction SpinTransaction(WheelTableInfo table, WheelSectorInfo sector) =>
            SpinTransaction(sector, ContentIdOf(table), SpinPrice());

        /// <summary>
        /// <see cref="SpinTransaction(WheelTableInfo, WheelSectorInfo)"/> with the table's content id and the price
        /// list passed in, so a check over every sector builds them once. The transaction only reads
        /// <paramref name="price"/>.
        /// </summary>
        static WalletTransaction SpinTransaction(WheelSectorInfo sector, EconomyContentId contentId, IReadOnlyList<CurrencyAmount> price) =>
            WalletTransaction.Exchange(
                EconomyFeature.SpinWheel,
                EconomyReason.WheelPrize,
                price,
                sector?.Reward,
                contentId,
                EconomyReason.WheelSpinCost);

        static EconomyContentId ContentIdOf(WheelTableInfo table) => EconomyContentId.FromString(table?.Id?.Value);

        static IReadOnlyList<CurrencyAmount> SpinPrice() => new List<CurrencyAmount> { SpinCost };

        /// <summary>
        /// Returns the player's spin wheel status from their wheel state, wallet and the published table.
        /// <para>
        /// <paramref name="expectedOrdinal"/> is the ordinal a spin request expects. Pass <b>null</b>, not zero,
        /// when checking status without a request, for example to draw the Spin button. Null skips the ordinal
        /// check. If zero meant "skip", a client could skip the check by sending zero. Ordinals start at one, so
        /// a request with zero is refused as stale.
        /// </para>
        /// </summary>
        public static SpinWheelOutlook OutlookFor(SpinWheelState state, PlayerWalletModel wallet, SharedGameConfig config, int? expectedOrdinal = null)
        {
            state ??= new SpinWheelState();

            WheelTableInfo table          = ActiveTable(config);
            SpinReceipt    pending        = state.PendingReceipt;
            int            nextOrdinal    = state.NextOrdinal;
            int            spinsAvailable = wallet == null ? 0 : wallet.AmountOf(CurrencyType.SpinTokens) / SpinCost.Amount;

            SpinRefusal refusal = RefusalFor(state, wallet, config, table, expectedOrdinal);
            return new SpinWheelOutlook(table, refusal, spinsAvailable, pending, nextOrdinal);
        }

        static SpinRefusal RefusalFor(SpinWheelState state, PlayerWalletModel wallet, SharedGameConfig config, WheelTableInfo table, int? expectedOrdinal)
        {
            if (table == null || !table.IsComplete)
                return SpinRefusal.ConfigUnavailable;

            // Check first, because a new spin would overwrite an unseen result. A duplicate request also stops
            // here and gets the pending receipt.
            if (state.HasPendingReceipt)
                return SpinRefusal.ResultPending;

            // A wrong ordinal means a retransmission or a stale browser tab. Checked after the pending check, so
            // a replay of the spin that just resolved gets ResultPending, not StaleOrdinal. A null ordinal skips
            // this check.
            if (expectedOrdinal.HasValue && expectedOrdinal.Value != state.NextOrdinal)
                return SpinRefusal.StaleOrdinal;

            if (wallet == null || !wallet.CanAfford(SpinCost))
                return SpinRefusal.NoTokens;

            if (!EveryPrizeFits(wallet, WalletCaps.From(config.Global), table))
                return SpinRefusal.WalletFull;

            return SpinRefusal.None;
        }

        /// <summary>
        /// The currency of the first sector, in table order, whose prize would exceed its cap, or
        /// <see cref="CurrencyType.None"/> when no sector would.
        /// <para>
        /// The "wallet full" screen uses it to tell the player <i>which</i> balance to spend down.
        /// </para>
        /// </summary>
        public static CurrencyType OverflowingCurrency(PlayerWalletModel wallet, WalletCaps caps, WheelTableInfo table)
        {
            if (wallet == null || table == null || !table.IsComplete)
                return CurrencyType.None;

            EconomyContentId              contentId = ContentIdOf(table);
            IReadOnlyList<CurrencyAmount> price     = SpinPrice();
            foreach (WheelSectorInfo sector in table.Sectors)
            {
                if (sector?.Reward == null)
                    continue;

                WalletSettlement settlement = wallet.Settle(SpinTransaction(sector, contentId, price), caps);
                if (settlement.Refusal == WalletRefusal.CapExceeded)
                    return settlement.RefusedCurrency;
            }

            return CurrencyType.None;
        }
    }
}
