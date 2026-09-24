using Metaplay.Core;
using Metaplay.Core.Model;
using System;
using System.Collections.Generic;

namespace Game.Logic
{
    public static partial class ActionResults
    {
        /// <summary>The player cannot pay the price. The shop screen shows this to the player.</summary>
        public static readonly MetaActionResult InsufficientFunds = new MetaActionResult(nameof(InsufficientFunds));

        /// <summary>A grant would take a balance past its configured cap, so no part of the transaction was applied.</summary>
        public static readonly MetaActionResult WalletCapExceeded = new MetaActionResult(nameof(WalletCapExceeded));

        /// <summary>The transaction is invalid, for example a zero amount, no currency, or one currency listed twice.</summary>
        public static readonly MetaActionResult MalformedTransaction = new MetaActionResult(nameof(MalformedTransaction));
    }

    /// <summary>
    /// The feature that changed a balance. One value per feature that grants or spends currency, so analytics
    /// can group changes by feature without mapping each <see cref="EconomyReason"/>.
    /// </summary>
    [MetaSerializable]
    public enum EconomyFeature
    {
        None = 0,

        /// <summary>The economy itself, used for the starting wallet.</summary>
        Economy = 1,

        DailyReward    = 2,
        FirstWeekEvent = 3,
        Missions       = 4,
        SpinWheel      = 5,
        Cosmetics      = 6,
        Offers         = 7,

        /// <summary>A validated development-mode purchase.</summary>
        Iap = 8,

        Tournament  = 9,
        WeeklyEvent = 10,
    }

    /// <summary>
    /// Why a balance changed. Every economy transaction event carries one (<c>docs/economy.md</c>).
    /// <para>
    /// It is an enum instead of a string so that analytics can filter on a fixed set of values. A feature that
    /// needs a new reason adds a value here.
    /// </para>
    /// </summary>
    [MetaSerializable]
    public enum EconomyReason
    {
        None = 0,

        /// <summary>The balances a new player is created with.</summary>
        StartingWallet = 1,

        DailyReward     = 2,
        FirstWeekReward = 3,
        MissionReward   = 4,

        /// <summary>The token spent on a spin.</summary>
        WheelSpinCost = 5,

        /// <summary>The prize the wheel landed on.</summary>
        WheelPrize = 6,

        CosmeticPurchase  = 7,
        OfferPurchase     = 8,
        IapGrant          = 9,
        TournamentReward  = 10,
        WeeklyEventReward = 11,
    }

    /// <summary>The direction of a currency change. A source and a sink are never combined into one event.</summary>
    [MetaSerializable]
    public enum CurrencyFlow
    {
        None = 0,

        /// <summary>The player gained this currency.</summary>
        Source = 1,

        /// <summary>The player spent this currency.</summary>
        Sink = 2,
    }

    /// <summary>
    /// The config ID a transaction relates to, such as a cosmetic, offer, mission or wheel table, as one type
    /// the wallet can carry for any feature.
    /// <para>
    /// It must hold a <b>config</b> ID and never player data. It is the only free-form string field on an economy
    /// event, and the analytics rules forbid free text in event payloads.
    /// </para>
    /// </summary>
    [MetaSerializable]
    public class EconomyContentId : StringId<EconomyContentId> { }

    /// <summary>
    /// The currency changes one action requests: what it spends, what it grants, and the feature, reason and
    /// content ID recorded in analytics. It changes nothing by itself: <see cref="PlayerWalletModel.Settle"/>
    /// computes the result or refuses it.
    /// <para>
    /// Spends and grants are separate lists, and one currency may appear in both, because netting a spin's token
    /// cost against a token prize would hide the spin from the log.
    /// </para>
    /// </summary>
    public sealed class WalletTransaction
    {
        static readonly IReadOnlyList<CurrencyAmount> NoAmounts = new List<CurrencyAmount>();

        /// <summary>The feature that changes the balance. Supplied by the action, never inferred.</summary>
        public EconomyFeature Feature { get; }

        /// <summary>
        /// The reason recorded on the grant rows. Also used for the spend rows when
        /// <see cref="SpendReason"/> was not given separately.
        /// </summary>
        public EconomyReason Reason { get; }

        /// <summary>
        /// The reason recorded on the spend rows. Equal to <see cref="Reason"/> unless the transaction was
        /// created with a separate spend reason.
        /// <para>
        /// An exchange can need two reasons. A spin spends a token with reason
        /// <see cref="EconomyReason.WheelSpinCost"/> and grants a prize with reason
        /// <see cref="EconomyReason.WheelPrize"/>. If both rows used the prize reason, filtering on
        /// <see cref="EconomyReason.WheelSpinCost"/> would find nothing (<c>docs/economy.md</c>).
        /// </para>
        /// </summary>
        public EconomyReason SpendReason { get; }

        /// <summary>The config ID the transaction relates to, or null.</summary>
        public EconomyContentId ContentId { get; }

        /// <summary>What the player pays, at most one entry per currency. Applied before the grants.</summary>
        public IReadOnlyList<CurrencyAmount> Spends { get; }

        /// <summary>What the player receives, at most one entry per currency.</summary>
        public IReadOnlyList<CurrencyAmount> Grants { get; }

        /// <summary>
        /// Whether the grants may take a balance above its cap. True only for <see cref="PaidGrant"/>, because the
        /// player has already paid for it and a refusal cannot be undone.
        /// </summary>
        public bool MayExceedCaps { get; }

        WalletTransaction(
            EconomyFeature               feature,
            EconomyReason                reason,
            EconomyReason                spendReason,
            EconomyContentId             contentId,
            IReadOnlyList<CurrencyAmount> spends,
            IReadOnlyList<CurrencyAmount> grants,
            bool                          mayExceedCaps = false)
        {
            Feature     = feature;
            Reason      = reason;
            SpendReason = spendReason == EconomyReason.None ? reason : spendReason;
            ContentId   = contentId;
            Spends      = spends;
            Grants        = grants;
            MayExceedCaps = mayExceedCaps;
        }

        /// <summary>A grant with no spend.</summary>
        public static WalletTransaction Grant(EconomyFeature feature, EconomyReason reason, RewardBundle reward, EconomyContentId contentId = null) =>
            new WalletTransaction(feature, reason, reason, contentId, NoAmounts, reward?.Amounts);

        /// <summary>
        /// A grant for an in-app purchase that the platform has already charged. It is never refused for a cap: the
        /// SDK records the purchase whatever the wallet does, so a refusal would take the player's money and grant
        /// nothing. A balance it takes above its cap stays there, and ordinary grants of that currency are refused
        /// until the player spends below the cap.
        /// </summary>
        public static WalletTransaction PaidGrant(EconomyFeature feature, EconomyReason reason, RewardBundle reward, EconomyContentId contentId = null) =>
            new WalletTransaction(feature, reason, reason, contentId, NoAmounts, reward?.Amounts, mayExceedCaps: true);

        /// <summary>A spend of one currency with no grant.</summary>
        public static WalletTransaction Spend(EconomyFeature feature, EconomyReason reason, CurrencyAmount price, EconomyContentId contentId = null) =>
            new WalletTransaction(feature, reason, reason, contentId, price == null ? null : new List<CurrencyAmount> { price }, NoAmounts);

        /// <summary>A spend of several currencies with no grant. All are applied, or none.</summary>
        public static WalletTransaction Spend(EconomyFeature feature, EconomyReason reason, IReadOnlyList<CurrencyAmount> price, EconomyContentId contentId = null) =>
            new WalletTransaction(feature, reason, reason, contentId, price, NoAmounts);

        /// <summary>
        /// A price and what it buys, settled together, so the player is never charged without receiving the
        /// grant. Used for offers and spins.
        /// <para>
        /// <paramref name="spendReason"/> is the reason recorded on the spend rows. When omitted, both spend and
        /// grant rows use <paramref name="reason"/>.
        /// </para>
        /// </summary>
        public static WalletTransaction Exchange(EconomyFeature feature, EconomyReason reason, IReadOnlyList<CurrencyAmount> price, RewardBundle reward, EconomyContentId contentId = null, EconomyReason spendReason = EconomyReason.None) =>
            new WalletTransaction(feature, reason, spendReason, contentId, price, reward?.Amounts);

        /// <summary>A single-currency price and what it buys.</summary>
        public static WalletTransaction Exchange(EconomyFeature feature, EconomyReason reason, CurrencyAmount price, RewardBundle reward, EconomyContentId contentId = null, EconomyReason spendReason = EconomyReason.None) =>
            new WalletTransaction(feature, reason, spendReason, contentId, price == null ? null : new List<CurrencyAmount> { price }, reward?.Amounts);

        /// <summary>
        /// Whether the transaction is valid: the feature and reason are set, every amount has a currency and is
        /// positive, and neither the spends nor the grants list a currency twice.
        /// <para>
        /// A zero amount makes the transaction invalid instead of being skipped. The config build checks that
        /// every price and reward is positive, so a zero at runtime is a bug in the caller.
        /// </para>
        /// </summary>
        public bool IsWellFormed =>
            Feature != EconomyFeature.None &&
            Reason != EconomyReason.None &&
            IsWellFormedSide(Spends) &&
            IsWellFormedSide(Grants);

        static bool IsWellFormedSide(IReadOnlyList<CurrencyAmount> amounts)
        {
            if (amounts == null)
                return false;

            HashSet<CurrencyType> seen = new HashSet<CurrencyType>();
            foreach (CurrencyAmount amount in amounts)
            {
                if (amount == null || amount.Currency == CurrencyType.None || amount.Amount <= 0)
                    return false;
                if (!seen.Add(amount.Currency))
                    return false;
            }

            return true;
        }

        public override string ToString() =>
            $"{Reason} ({Feature}): -[{string.Join(", ", Spends ?? NoAmounts)}]{(SpendReason == Reason ? "" : $" as {SpendReason}")} +[{string.Join(", ", Grants ?? NoAmounts)}]";
    }

    /// <summary>
    /// One change to one currency balance, in one direction. Each row becomes one
    /// <c>economy_transaction</c> analytics event.
    /// </summary>
    public sealed class WalletTransactionRow
    {
        public CurrencyType Currency      { get; }
        public CurrencyFlow Flow          { get; }
        public int          Amount        { get; }
        public int          BalanceBefore { get; }
        public int          BalanceAfter  { get; }

        internal WalletTransactionRow(CurrencyType currency, CurrencyFlow flow, int amount, int balanceBefore, int balanceAfter)
        {
            Currency      = currency;
            Flow          = flow;
            Amount        = amount;
            BalanceBefore = balanceBefore;
            BalanceAfter  = balanceAfter;
        }

        public override string ToString() => $"{Flow} {Amount} {Currency} ({BalanceBefore} -> {BalanceAfter})";
    }

    /// <summary>Why a settlement refused. <see cref="None"/> is the successful case.</summary>
    public enum WalletRefusal
    {
        None = 0,

        /// <summary>The player cannot pay. The only refusal that is a normal outcome rather than a bug.</summary>
        InsufficientFunds = 1,

        /// <summary>
        /// A grant would exceed a balance cap. The whole transaction is refused, never reduced to fit. A
        /// <see cref="WalletTransaction.PaidGrant"/> is refused this way only when the balance would not fit in an
        /// <c>int</c>.
        /// </summary>
        CapExceeded = 2,

        /// <summary>The transaction is invalid (see <see cref="WalletTransaction.IsWellFormed"/>).</summary>
        Malformed = 3,
    }

    /// <summary>
    /// The result of settling one transaction, computed before any balance changes: either a refusal or a complete
    /// replacement wallet.
    /// <para>
    /// No method accepts a settlement to apply it.
    /// <see cref="PlayerModel.ApplyWallet(WalletTransaction, bool, AnalyticsCorrelationId)"/> takes a
    /// transaction and settles it itself, so a settlement computed against an older wallet can never be applied.
    /// </para>
    /// </summary>
    public sealed class WalletSettlement
    {
        static readonly IReadOnlyList<WalletTransactionRow> NoRows = new List<WalletTransactionRow>();

        public WalletTransaction Transaction { get; }

        /// <summary>Why the transaction was refused, or <see cref="WalletRefusal.None"/> when it succeeded.</summary>
        public WalletRefusal Refusal { get; }

        /// <summary>The wallet the transaction was settled against.</summary>
        public PlayerWalletModel WalletBefore { get; }

        /// <summary>The resulting wallet, or null when the transaction was refused.</summary>
        public PlayerWalletModel WalletAfter { get; }

        /// <summary>One row per currency change, spends first. Empty on a refusal or when nothing changes.</summary>
        public IReadOnlyList<WalletTransactionRow> Rows { get; }

        /// <summary>The currency that was short or would exceed its cap. Set only on a refusal.</summary>
        public CurrencyType RefusedCurrency { get; }

        /// <summary>The requested amount of <see cref="RefusedCurrency"/>. Set only on a refusal.</summary>
        public int RefusedAmount { get; }

        /// <summary>The player's balance of <see cref="RefusedCurrency"/> at the refusal. Set only on a refusal.</summary>
        public int RefusedBalance { get; }

        WalletSettlement(
            WalletTransaction                   transaction,
            WalletRefusal                       refusal,
            PlayerWalletModel                   walletBefore,
            PlayerWalletModel                   walletAfter,
            IReadOnlyList<WalletTransactionRow> rows,
            CurrencyType                        refusedCurrency,
            int                                 refusedAmount,
            int                                 refusedBalance)
        {
            Transaction     = transaction;
            Refusal         = refusal;
            WalletBefore    = walletBefore;
            WalletAfter     = walletAfter;
            Rows            = rows ?? NoRows;
            RefusedCurrency = refusedCurrency;
            RefusedAmount   = refusedAmount;
            RefusedBalance  = refusedBalance;
        }

        internal static WalletSettlement Settled(WalletTransaction transaction, PlayerWalletModel walletBefore, PlayerWalletModel walletAfter, IReadOnlyList<WalletTransactionRow> rows) =>
            new WalletSettlement(transaction, WalletRefusal.None, walletBefore, walletAfter, rows, CurrencyType.None, 0, 0);

        internal static WalletSettlement Refused(WalletTransaction transaction, WalletRefusal refusal, PlayerWalletModel walletBefore, CurrencyType currency, int amount, int balance) =>
            new WalletSettlement(transaction, refusal, walletBefore, null, NoRows, currency, amount, balance);

        public bool IsSuccess => Refusal == WalletRefusal.None;

        /// <summary>Whether any balance changes. A settlement with no changes emits no analytics events.</summary>
        public bool MovesBalance => Rows.Count > 0;

        /// <summary>
        /// Whether the refusal should be shown to the player. Only insufficient funds is. A cap refusal or a
        /// malformed transaction is a bug, not something the player can act on.
        /// </summary>
        public bool IsPlayerFacingRefusal => Refusal == WalletRefusal.InsufficientFunds;

        /// <summary>
        /// The action result for this settlement's refusal.
        /// <para>
        /// <b>It is internal on purpose.</b> Actions get their result from
        /// <see cref="PlayerModel.ApplyWallet(WalletTransaction, bool, AnalyticsCorrelationId)"/>, which also
        /// records the refusal, so an action cannot return a refusal without it being recorded.
        /// </para>
        /// </summary>
        internal MetaActionResult ActionResult
        {
            get
            {
                switch (Refusal)
                {
                    case WalletRefusal.InsufficientFunds:   return ActionResults.InsufficientFunds;
                    case WalletRefusal.CapExceeded:         return ActionResults.WalletCapExceeded;
                    case WalletRefusal.Malformed:           return ActionResults.MalformedTransaction;
                    default:                                return MetaActionResult.Success;
                }
            }
        }

        public override string ToString() =>
            IsSuccess ? $"settled {Transaction} into {WalletAfter}"
                      : $"refused {Transaction}: {Refusal} ({RefusedAmount} {RefusedCurrency}, balance {RefusedBalance})";
    }
}
