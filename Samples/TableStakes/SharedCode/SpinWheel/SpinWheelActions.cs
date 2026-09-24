using Metaplay.Core;
using Metaplay.Core.Model;

namespace Game.Logic
{
    public static partial class ActionCodes
    {
        // Spin wheel actions. SharedCode/Player/PlayerActions.cs lists every player action code, so check it
        // before taking a new code.
        public const int PlayerWheelSpinResolved     = 5010;
        public const int PlayerAcknowledgeWheelSpin  = 5011;
    }

    public static partial class ActionResults
    {
        /// <summary>A resolved result has not been seen yet. Refusing here makes a duplicate spin request harmless.</summary>
        public static readonly MetaActionResult WheelResultPending = new MetaActionResult(nameof(WheelResultPending));

        /// <summary>The config has no complete wheel table (<see cref="WheelTableInfo.IsComplete"/>). Nothing is spent.</summary>
        public static readonly MetaActionResult WheelUnavailable = new MetaActionResult(nameof(WheelUnavailable));

        /// <summary>The spin ordinal is not the player's next one, for example on a replay or a second tap.</summary>
        public static readonly MetaActionResult WheelStaleOrdinal = new MetaActionResult(nameof(WheelStaleOrdinal));

        /// <summary>At least one sector would exceed a balance cap, so nothing was drawn and no token was spent.</summary>
        public static readonly MetaActionResult WheelWalletFull = new MetaActionResult(nameof(WheelWalletFull));

        /// <summary>No result is waiting to be acknowledged. A repeated Done is refused and changes nothing.</summary>
        public static readonly MetaActionResult WheelNothingToAcknowledge = new MetaActionResult(nameof(WheelNothingToAcknowledge));
    }

    /// <summary>
    /// Resolves one spin: spends the token, pays the sector the server drew, and stores the receipt. It is a
    /// synchronized server action because wallet balances are checksummed, so both sides must change them at the
    /// same tick. The payload carries only what only the server knows: the drawn sector, the spin it was drawn for,
    /// and the time. The reward and receipt are computed from replicated state and the published table.
    /// A re-delivery of a pending spin is refused before the wallet changes. <see cref="Ordinal"/> must equal the
    /// player's next ordinal, so a client that holds several draws can settle only the first one to run. Without
    /// that check it could keep the draws it liked (<c>docs/spin-wheel.md</c>, "Why a client cannot choose its prize").
    /// </summary>
    [ModelAction(ActionCodes.PlayerWheelSpinResolved)]
    public class PlayerWheelSpinResolved : PlayerSynchronizedServerAction
    {
        /// <summary>
        /// The spin this draw was made for: the player's resolved spin count plus one when the server drew.
        /// The action resolves this spin or nothing.
        /// </summary>
        public int Ordinal { get; private set; }

        /// <summary>The sector the server drew, as a zero-based index in the active table's sector order.</summary>
        public int SectorIndex { get; private set; }

        /// <summary>The server time of the draw. Stored on the receipt. Never taken from the client.</summary>
        public MetaTime DrawnAt { get; private set; }

        public PlayerWheelSpinResolved() { }

        public PlayerWheelSpinResolved(int ordinal, int sectorIndex, MetaTime drawnAt)
        {
            Ordinal     = ordinal;
            SectorIndex = sectorIndex;
            DrawnAt     = drawnAt;
        }

        public override MetaActionResult Execute(PlayerModel player, bool commit)
        {
            SpinWheelOutlook outlook = SpinWheelPolicy.OutlookFor(player.SpinWheel, player.Wallet, player.GameConfig);

            switch (outlook.Refusal)
            {
                case SpinRefusal.ResultPending:     return ActionResults.WheelResultPending;
                case SpinRefusal.ConfigUnavailable: return ActionResults.WheelUnavailable;
                case SpinRefusal.WalletFull:        return ActionResults.WheelWalletFull;
                case SpinRefusal.NoTokens:          return ActionResults.InsufficientFunds;
            }

            // Check the ordinal after the pending check, so a re-delivery of the pending spin gets
            // WheelResultPending, and before the wallet changes, so a stale draw costs nothing.
            if (Ordinal != outlook.NextOrdinal)
                return ActionResults.WheelStaleOrdinal;

            WheelSectorInfo sector = outlook.Table.SectorAt(SectorIndex);
            if (sector == null)
                return ActionResults.WheelUnavailable;

            // One correlation ID for the whole spin, computed from the server's timestamp so the client and the
            // server create the same ID. The token spend, the prize grant and the spin event carry it, and the
            // receipt stores it so an operator can find the matching wallet events.
            AnalyticsCorrelationId correlation = AnalyticsCorrelationId.Create(player.PlayerId, DrawnAt, SpinWheelPolicy.CorrelationCause);

            // Apply the wallet change on both passes and before any other write, so a refusal leaves the token
            // unspent, the ordinal unchanged and no receipt. The token spend and prize grant are one exchange,
            // so both apply or neither does.
            MetaActionResult result = player.ApplyWallet(
                SpinWheelPolicy.SpinTransaction(outlook.Table, sector), commit, correlation, out WalletSettlement settlement);

            if (result != MetaActionResult.Success)
                return result;

            if (commit)
            {
                SpinReceipt receipt = new SpinReceipt(
                    outlook.NextOrdinal, outlook.Table.Id, sector.Id, SectorIndex, sector.Reward, DrawnAt, correlation);

                // ApplySpinResult is the only place a spin result is written. It throws if a result is pending or
                // the ordinal is wrong, so "an unseen result is never overwritten" does not depend on this method.
                player.ApplySpinResult(receipt);

                player.EventStream.Event(new PlayerEventWheelSpinResolved(
                    outlook.Table.Id, sector.Id, SectorIndex + 1, sector.Tier,
                    sector.Currency, sector.Amount,
                    receipt.Ordinal,
                    settlement.WalletAfter.SpinTokens,
                    correlation));
            }

            return MetaActionResult.Success;
        }

        public override string ToString() => $"resolve wheel spin on sector {SectorIndex}";
    }

    /// <summary>
    /// Marks the pending receipt as seen. The client sends it for Done and as the first step of Spin again.
    /// It only updates <see cref="SpinWheelState.LastAcknowledgedOrdinal"/>: the reward was granted when the spin
    /// resolved. It is a client action, so it runs after the resolve action on the same timeline. It takes no
    /// parameters and acknowledges the pending receipt in the player state, so a client cannot name a spin that
    /// did not happen.
    /// </summary>
    [ModelAction(ActionCodes.PlayerAcknowledgeWheelSpin)]
    public class PlayerAcknowledgeWheelSpin : PlayerAction
    {
        public PlayerAcknowledgeWheelSpin() { }

        public override MetaActionResult Execute(PlayerModel player, bool commit)
        {
            if (!player.SpinWheel.HasPendingReceipt)
                return ActionResults.WheelNothingToAcknowledge;

            if (commit)
                player.AcknowledgeSpinResult();

            return MetaActionResult.Success;
        }

        public override string ToString() => "acknowledge the pending wheel result";
    }
}
