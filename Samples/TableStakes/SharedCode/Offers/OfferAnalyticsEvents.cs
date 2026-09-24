using Metaplay.Core.Analytics;
using Metaplay.Core.Model;
using Metaplay.Core.Player;

namespace Game.Logic
{
    /// <summary>
    /// The player turned segment-targeted featured offers on or off (<c>docs/offers.md</c>, "Segment
    /// targeting"). It has no correlation ID, because changing the preference moves no currency.
    /// <para>
    /// The game has <b>no</b> event of its own for a wallet-priced purchase. The SDK's
    /// <c>PlayerEventInGameCurrencyOfferPurchased</c>, emitted by <c>PlayerPurchaseInGameCurrencyMetaOffer</c>,
    /// already records the offer, group and placement, and <c>docs/analytics.md</c> forbids duplicating an SDK
    /// event.
    /// </para>
    /// </summary>
    [AnalyticsEvent(AnalyticsEventCodes.PersonalizedOffersPreferenceChanged, displayName: "Personalized offers preference changed", docString: "The player turned segmented featured offers on or off.")]
    [AnalyticsAlias("personalized_offers_preference_changed")]
    [AnalyticsEventKeywords(AnalyticsKeywords.Offer)]
    public class PlayerEventPersonalizedOffersPreferenceChanged : PlayerEventBase
    {
        [MetaMember(1)] public bool Enabled { get; private set; }

        public override string EventDescription => Enabled ? "Turned personalized offers on." : "Turned personalized offers off.";

        public PlayerEventPersonalizedOffersPreferenceChanged() { }

        public PlayerEventPersonalizedOffersPreferenceChanged(bool enabled)
        {
            Enabled = enabled;
        }
    }
}
