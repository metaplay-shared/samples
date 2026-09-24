using Metaplay.Core;
using Metaplay.Core.Analytics;
using Metaplay.Core.Model;
using Metaplay.Core.Player;

namespace Game.Logic
{
    /// <summary>
    /// The player bought a cosmetic with wallet currency. Emitted with the same correlation ID as the
    /// <c>economy_transaction</c> event for the spend, so the two can be joined.
    /// <para>
    /// <b>Every ID here comes from the catalogue.</b> The action looks up the ID it received and emits the
    /// catalogue item's own ID, so client-supplied text never reaches an analytics field
    /// (<c>docs/cosmetics.md</c>).
    /// </para>
    /// </summary>
    [AnalyticsEvent(AnalyticsEventCodes.CosmeticPurchased, displayName: "Cosmetic purchased", docString: "The player bought a cosmetic, and the price was taken from their wallet under the same correlation key.")]
    [AnalyticsAlias("cosmetic_purchased")]
    [AnalyticsEventKeywords(AnalyticsKeywords.Cosmetic, AnalyticsKeywords.Sink, AnalyticsKeywords.Purchase)]
    public class PlayerEventCosmeticPurchased : PlayerEventBase
    {
        /// <summary>The same ID as the wallet transaction event for this purchase.</summary>
        [MetaMember(1)] public AnalyticsCorrelationId Correlation { get; private set; }

        [MetaMember(2)] public CosmeticId   Cosmetic { get; private set; }
        [MetaMember(3)] public CosmeticKind Slot     { get; private set; }

        [MetaMember(4)] public CurrencyType Currency { get; private set; }

        /// <summary>The price paid. An <c>int</c> because the analytics export does not support unsigned integers.</summary>
        [MetaMember(5)] public int Price { get; private set; }

        /// <summary>The balance left in that currency after the purchase settled.</summary>
        [MetaMember(6)] public int BalanceAfter { get; private set; }

        /// <summary>How many cosmetics the player owns after the purchase, including this one.</summary>
        [MetaMember(7)] public int OwnedCount { get; private set; }

        public override string EventDescription => $"Bought {Cosmetic} for {Price} {Currency}; {OwnedCount} owned.";

        public PlayerEventCosmeticPurchased() { }

        public PlayerEventCosmeticPurchased(
            AnalyticsCorrelationId correlation,
            CosmeticId             cosmetic,
            CosmeticKind           slot,
            CurrencyType           currency,
            int                    price,
            int                    balanceAfter,
            int                    ownedCount)
        {
            Correlation  = correlation;
            Cosmetic     = cosmetic;
            Slot         = slot;
            Currency     = currency;
            Price        = price;
            BalanceAfter = balanceAfter;
            OwnedCount   = ownedCount;
        }
    }

    /// <summary>
    /// The player changed the cosmetic equipped in one slot. Emitted both by a purchase, which equips the
    /// bought item, and by equipping an owned item. <see cref="OnPurchase"/> tells the two apart, so one event
    /// type answers what players choose to wear.
    /// <para>
    /// It grants nothing and changes no balance, so it has no correlation ID. The spend is recorded on
    /// <see cref="PlayerEventCosmeticPurchased"/>.
    /// </para>
    /// </summary>
    [AnalyticsEvent(AnalyticsEventCodes.CosmeticEquipped, displayName: "Cosmetic equipped", docString: "The player changed what they are wearing in one cosmetic slot.")]
    [AnalyticsAlias("cosmetic_equipped")]
    [AnalyticsEventKeywords(AnalyticsKeywords.Cosmetic, AnalyticsKeywords.Progression)]
    public class PlayerEventCosmeticEquipped : PlayerEventBase
    {
        [MetaMember(1)] public CosmeticId   Cosmetic { get; private set; }
        [MetaMember(2)] public CosmeticKind Slot     { get; private set; }

        /// <summary>The item previously equipped in the slot, or null if the slot was empty.</summary>
        [MetaMember(3)] public CosmeticId Replaced { get; private set; }

        /// <summary>True when a purchase equipped the item, false when the player equipped an owned item.</summary>
        [MetaMember(4)] public bool OnPurchase { get; private set; }

        public override string EventDescription =>
            $"Equipped {Cosmetic} in the {Slot} slot, replacing {Replaced?.Value ?? "nothing"}{(OnPurchase ? ", on purchase" : "")}.";

        public PlayerEventCosmeticEquipped() { }

        public PlayerEventCosmeticEquipped(CosmeticId cosmetic, CosmeticKind slot, CosmeticId replaced, bool onPurchase)
        {
            Cosmetic   = cosmetic;
            Slot       = slot;
            Replaced   = replaced;
            OnPurchase = onPurchase;
        }
    }
}
