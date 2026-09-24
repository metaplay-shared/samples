using Metaplay.Core.Analytics;
using Metaplay.Core.Model;
using Metaplay.Core.Player;
using System.Collections.Generic;

namespace Game.Logic
{
    /// <summary>
    /// One committed change to one currency balance.
    /// <para>
    /// Each change is its own event, never netted, so a spin that wins back the token it cost still appears in
    /// the log. All events from one action carry the action's correlation ID (<c>docs/analytics.md</c>,
    /// "Correlation id"). The committing action supplies the feature and the reason.
    /// </para>
    /// </summary>
    [AnalyticsEvent(AnalyticsEventCodes.EconomyTransaction, displayName: "Economy transaction", docString: "One currency balance moved, in one direction, with the balance before and after it.")]
    [AnalyticsAlias("economy_transaction")]
    [AnalyticsEventKeywords(AnalyticsKeywords.Economy)]
    public class PlayerEventEconomyTransaction : PlayerEventBase
    {
        /// <summary>
        /// Adds <c>Sink</c> for a spend or <c>Source</c> for a grant, based on <see cref="Flow"/>, so the
        /// Dashboard's keyword filters separate spends from grants.
        /// <para>
        /// The keyword must be set per instance. Declaring both keywords on the class would put both on every
        /// event, and the filters would match everything.
        /// </para>
        /// </summary>
        public override IEnumerable<string> KeywordsForEventInstance =>
            new[] { Flow == CurrencyFlow.Sink ? AnalyticsKeywords.Sink : AnalyticsKeywords.Source };

        [MetaMember(1)] public CurrencyType Currency { get; private set; }

        /// <summary>Whether the player gained or spent the currency.</summary>
        [MetaMember(2)] public CurrencyFlow Flow { get; private set; }

        /// <summary>The amount, always positive. <see cref="Flow"/> gives the direction.</summary>
        [MetaMember(3)] public int Amount { get; private set; }

        [MetaMember(4)] public int BalanceBefore { get; private set; }
        [MetaMember(5)] public int BalanceAfter  { get; private set; }

        /// <summary>Why the balance changed.</summary>
        [MetaMember(6)] public EconomyReason Reason { get; private set; }

        /// <summary>The feature that changed the balance.</summary>
        [MetaMember(7)] public EconomyFeature Feature { get; private set; }

        /// <summary>The config ID the change relates to, such as a cosmetic, offer or mission, or null.</summary>
        [MetaMember(8)] public EconomyContentId ContentId { get; private set; }

        /// <summary>The same ID as every other event from the same action.</summary>
        [MetaMember(9)] public AnalyticsCorrelationId Correlation { get; private set; }

        public override string EventDescription =>
            Flow == CurrencyFlow.Sink
                ? $"Spent {Amount} {Currency} on {Reason}, leaving {BalanceAfter}."
                : $"Earned {Amount} {Currency} from {Reason}, reaching {BalanceAfter}.";

        public PlayerEventEconomyTransaction() { }

        public PlayerEventEconomyTransaction(
            CurrencyType           currency,
            CurrencyFlow           flow,
            int                    amount,
            int                    balanceBefore,
            int                    balanceAfter,
            EconomyReason          reason,
            EconomyFeature         feature,
            EconomyContentId       contentId,
            AnalyticsCorrelationId correlation)
        {
            Currency      = currency;
            Flow          = flow;
            Amount        = amount;
            BalanceBefore = balanceBefore;
            BalanceAfter  = balanceAfter;
            Reason        = reason;
            Feature       = feature;
            ContentId     = contentId;
            Correlation   = correlation;
        }
    }

    /// <summary>
    /// The player could not afford a spend, so it was refused and no balance changed.
    /// <para>
    /// Insufficient funds is a normal product outcome, not an error, and it would not appear in a log that only
    /// records successful changes. Grants refused by a cap and malformed transactions are <b>not</b> reported
    /// here, because they are bugs and would distort the rejection rate.
    /// </para>
    /// </summary>
    [AnalyticsEvent(AnalyticsEventCodes.EconomySpendRejected, displayName: "Economy spend rejected", docString: "The player could not afford a spend, so nothing was charged and nothing was granted.")]
    [AnalyticsAlias("economy_spend_rejected")]
    [AnalyticsEventKeywords(AnalyticsKeywords.Economy, AnalyticsKeywords.Rejected)]
    public class PlayerEventEconomySpendRejected : PlayerEventBase
    {
        /// <summary>The currency that was short. For a multi-currency price, the first currency that was short.</summary>
        [MetaMember(1)] public CurrencyType Currency { get; private set; }

        /// <summary>The amount of <see cref="Currency"/> the spend required.</summary>
        [MetaMember(2)] public int Requested { get; private set; }

        /// <summary>The player's balance of <see cref="Currency"/> at the time of the spend.</summary>
        [MetaMember(3)] public int Balance { get; private set; }

        [MetaMember(4)] public EconomyReason  Reason  { get; private set; }
        [MetaMember(5)] public EconomyFeature Feature { get; private set; }

        [MetaMember(6)] public EconomyContentId ContentId { get; private set; }

        [MetaMember(7)] public AnalyticsCorrelationId Correlation { get; private set; }

        public override string EventDescription =>
            $"Could not afford {Requested} {Currency} for {Reason} with {Balance} in hand.";

        public PlayerEventEconomySpendRejected() { }

        public PlayerEventEconomySpendRejected(
            CurrencyType           currency,
            int                    requested,
            int                    balance,
            EconomyReason          reason,
            EconomyFeature         feature,
            EconomyContentId       contentId,
            AnalyticsCorrelationId correlation)
        {
            Currency    = currency;
            Requested   = requested;
            Balance     = balance;
            Reason      = reason;
            Feature     = feature;
            ContentId   = contentId;
            Correlation = correlation;
        }
    }
}
