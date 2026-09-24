using Metaplay.Core.Analytics;
using Metaplay.Core.League;
using Metaplay.Core.Player;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.Logic
{
    /// <summary>
    /// Adds this game's Dashboard keywords to analytics events that the <b>SDK</b> emits, so one keyword filter
    /// matches both the game's events and the SDK's events on the same topic (<c>docs/analytics.md</c>).
    /// <para>
    /// The keywords are appended. An SDK event keeps any keywords it already declares.
    /// </para>
    /// </summary>
    public class GameAnalyticsEventCustomizations : AnalyticsEventCustomizations
    {
        /// <summary>
        /// The game keywords to add to each SDK event type. An SDK event type with no entry keeps only the
        /// keywords the SDK gave it.
        /// </summary>
        public static readonly IReadOnlyDictionary<Type, string[]> KeywordsBySdkEvent = new Dictionary<Type, string[]>
        {
            // The SDK emits PlayerEventNameChanged only for renames made from the LiveOps Dashboard. A player's
            // own rename goes through this game's handler, which emits PlayerEventNameChangeAccepted instead, so
            // each rename produces one row. Both events carry the Identity keyword.
            [typeof(PlayerEventNameChanged)]                    = new[] { AnalyticsKeywords.Identity },

            // Session and connection events. The game emits no events of its own for these.
            [typeof(PlayerEventClientConnected)]                = new[] { AnalyticsKeywords.Session, AnalyticsKeywords.Connectivity },
            [typeof(PlayerEventClientDisconnected)]             = new[] { AnalyticsKeywords.Session, AnalyticsKeywords.Connectivity },

            // In-app purchase and in-game offer purchase events.
            [typeof(PlayerEventInAppValidationStarted)]         = new[] { AnalyticsKeywords.IAP },
            [typeof(PlayerEventInAppValidationComplete)]        = new[] { AnalyticsKeywords.IAP },
            [typeof(PlayerEventInAppPurchased)]                 = new[] { AnalyticsKeywords.IAP, AnalyticsKeywords.Purchase },
            [typeof(PlayerEventInGameCurrencyOfferPurchased)]   = new[] { AnalyticsKeywords.Offer, AnalyticsKeywords.Economy },

            // LiveOps event lifecycle. The weekly event is a LiveOps event.
            [typeof(PlayerEventLiveOpsEventAdded)]              = new[] { AnalyticsKeywords.LiveOpsEvent },
            [typeof(PlayerEventLiveOpsEventPhaseChanged)]       = new[] { AnalyticsKeywords.LiveOpsEvent },
            [typeof(PlayerEventLiveOpsEventAudienceMembershipChanged)] = new[] { AnalyticsKeywords.LiveOpsEvent },

            // League division events. The seasonal tournament uses SDK league divisions.
            [typeof(DivisionEventCreated)]                      = new[] { AnalyticsKeywords.League },
            [typeof(DivisionEventParticipantJoined)]            = new[] { AnalyticsKeywords.League },
        };

        public override OverrideableProps CustomizeAnalyticsEventSpec(Type eventType, OverrideableProps props)
        {
            if (!KeywordsBySdkEvent.TryGetValue(eventType, out string[] added))
                return props;

            GetAnalyticsEventKeywordsFunc existing = props.KeywordsFunc;

            props.KeywordsFunc = existing == null
                ? (GetAnalyticsEventKeywordsFunc)(_ => added)
                : (ev => existing(ev).Concat(added).Distinct());

            return props;
        }
    }
}
