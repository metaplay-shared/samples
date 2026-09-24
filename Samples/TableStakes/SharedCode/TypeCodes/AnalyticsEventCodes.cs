using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.Logic
{
    /// <summary>
    /// The part of the product that owns an analytics code range. Each range has exactly one owner.
    /// </summary>
    public enum AnalyticsOwner
    {
        /// <summary>The card game: matchmaking, the table and the match result.</summary>
        Gameplay,

        /// <summary>The events the client is allowed to submit.</summary>
        ClientObservation,

        /// <summary>Events and schema migrations that belong to no single feature.</summary>
        Foundations,

        Identity,
        Economy,

        /// <summary>Daily rewards and the first-week event, which grant rewards on a schedule.</summary>
        Rewards,

        SpinWheel,
        Missions,
        Cosmetics,

        /// <summary>Offers and the demo purchase flow.</summary>
        Offers,

        WeeklyEvent,

        /// <summary>The seasonal tournament and any future competitive feature.</summary>
        Competition,

        Segments,

        /// <summary>Not yet assigned. Assign a range by editing this file, not inside a feature change.</summary>
        Unallocated,

        /// <summary>Reserved for events migrated from elsewhere. New features do not allocate here.</summary>
        Legacy,
    }

    /// <summary>Whether a registered code is in use, reserved for an unbuilt feature, or retired.</summary>
    public enum AnalyticsEventStatus
    {
        /// <summary>The type exists and the game emits it.</summary>
        Live,

        /// <summary>The code, alias and type name are reserved, but the type does not exist yet.</summary>
        Reserved,

        /// <summary>The type was removed. The code and alias stay in the registry and are never reused.</summary>
        Retired,
    }

    /// <summary>
    /// The registry of this game's <c>[AnalyticsEvent]</c> type codes. It splits the codes the SDK leaves to the
    /// game (<see cref="FirstGameCode"/> to <see cref="LastGameCode"/>) into per-feature <see cref="Ranges"/>, so
    /// features developed at the same time cannot pick the same code. Every allocated code has a row in
    /// <see cref="Registry"/>, even when its type is declared elsewhere, so this file alone shows whether a code is
    /// free. A code may be <see cref="AnalyticsEventStatus.Reserved"/> before its type exists. The naming and
    /// retirement rules (never reuse a code or alias, never change an alias, no dynamic values in names) are in
    /// <c>docs/analytics.md</c>, "Codes and the registry". <c>MessageCodes.cs</c> follows the same rules for codes.
    /// </summary>
    public static class AnalyticsEventCodes
    {
        /// <summary>The first code the SDK leaves to the game.</summary>
        public const int FirstGameCode = 1;

        /// <summary>The last code the SDK leaves to the game.</summary>
        public const int LastGameCode = 999;

        // ---- 1-49: the card game. Types live in SharedCode/Player/PlayerAnalyticsEvents.cs. ----

        /// <summary>This player was seated at a table.</summary>
        public const int MatchStarted = 10;

        /// <summary>A table this player sat at reached a terminal phase.</summary>
        public const int MatchFinished = 11;

        /// <summary>This player's seat stopped being played by them, and why.</summary>
        public const int MatchSeatLost = 12;

        /// <summary>A matchmaking search ended, however it ended.</summary>
        public const int MatchmakingWait = 13;

        // 1-9 and 14-49: free.

        // ---- 50-99: client observations. Types live in SharedCode/Analytics/ShellAnalyticsEvents.cs. ----
        //
        // The client may submit only the events in this range (docs/analytics.md). Adding one requires changing
        // that document first.

        /// <summary>A top-level route became the committed visible screen.</summary>
        public const int ScreenViewed = 50;

        /// <summary>The player selected a promoted entry: the next-action card, a feature card or a teaser.</summary>
        public const int PromotedEntrySelected = 51;

        // 52-99: free.

        // ---- 100-149: shared foundations and migrations. None allocated. ----

        // ---- 150-199: identity and the player profile. ----

        /// <summary>A generated identity was initialized for a new player.</summary>
        public const int IdentityInitialized = 150;

        /// <summary>A rename was refused, and why.</summary>
        public const int NameChangeRejected = 151;

        /// <summary>
        /// The server accepted a player's rename. The event records no name text. The SDK's own name-change
        /// event covers renames made from the LiveOps Dashboard.
        /// </summary>
        public const int NameChanged = 152;

        // 153-199: free.

        // ---- 200-249: economy and the wallet. ----

        /// <summary>One committed change to one currency balance.</summary>
        public const int EconomyTransaction = 200;

        /// <summary>A spend was refused for a product-relevant reason: the player could not afford it.</summary>
        public const int EconomySpendRejected = 201;

        // 202-249: free.

        // ---- 250-299: daily rewards and the first-week event. ----

        /// <summary>A daily login reward was claimed, with the streak and cycle position it landed on.</summary>
        public const int DailyRewardClaimed = 250;

        /// <summary>A first-week day's match goal was met inside its window.</summary>
        public const int FirstWeekDayCompleted = 251;

        /// <summary>A completed first-week day's reward was paid.</summary>
        public const int FirstWeekRewardClaimed = 252;

        // 253-299: free.

        // ---- 300-324: the spin wheel. ----

        /// <summary>A spin resolved, with the outcome the player was shown.</summary>
        public const int WheelSpinResolved = 300;

        // 301-324: free.

        // ---- 325-349: missions. ----

        /// <summary>A mission's target was crossed for the first time.</summary>
        public const int MissionCompleted = 325;

        /// <summary>A finished mission's reward was paid.</summary>
        public const int MissionRewardClaimed = 326;

        // 327-349: free.

        // ---- 350-399: cosmetics and the collection. Types live in SharedCode/Cosmetics/CosmeticAnalyticsEvents.cs. ----

        /// <summary>A cosmetic was bought, and the wallet paid for it.</summary>
        public const int CosmeticPurchased = 350;

        /// <summary>What the player is now wearing in one slot.</summary>
        public const int CosmeticEquipped = 351;

        // 352-399: free.

        // ---- 400-449: offers and the demo purchase flow. Types live in SharedCode/Offers/OfferAnalyticsEvents.cs.
        // A wallet-priced purchase has no code here, because the SDK's PlayerPurchaseInGameCurrencyMetaOffer
        // emits PlayerEventInGameCurrencyOfferPurchased, and docs/analytics.md forbids duplicating it. ----

        /// <summary>The personalized offers preference was turned on or off.</summary>
        public const int PersonalizedOffersPreferenceChanged = 400;

        // 401-449: free.

        // ---- 450-499: the weekly LiveOps event. Types live in SharedCode/WeeklyEvent/WeeklyEventAnalyticsEvents.cs.
        // The SDK's LiveOps lifecycle events (added, phase changed, audience membership changed) already record
        // a week's start, phases and targeting, and this game adds its keywords to them
        // (GameAnalyticsEventCustomizations), so the game only adds events for the target and the claim. ----

        /// <summary>A weekly event's points target was crossed, so its reward became claimable.</summary>
        public const int WeeklyEventTargetReached = 450;

        /// <summary>A weekly event's reward was paid.</summary>
        public const int WeeklyEventRewardClaimed = 451;

        // 452-499: free.
        // ---- 500-549: the seasonal tournament. Types live in SharedCode/Tournament/TournamentAnalyticsEvents.cs. ----

        /// <summary>The player entered a season's tournament group.</summary>
        public const int TournamentJoined = 500;

        /// <summary>One completed match used one of the player's scored attempts.</summary>
        public const int TournamentMatchCounted = 501;

        /// <summary>A participation milestone was claimed.</summary>
        public const int TournamentMilestoneClaimed = 502;

        /// <summary>A season the player took part in concluded, and where they placed.</summary>
        public const int TournamentResolved = 503;

        /// <summary>A placement reward was claimed.</summary>
        public const int TournamentPlacementRewardClaimed = 504;

        // 505-549: free.
        // ---- 550-574: player segments. None allocated. ----
        // ---- 575-899: unallocated. Reserve a range by editing this file. ----
        // ---- 900-999: legacy and migration only. No new feature allocates here. ----

        // ---- Retired: none. ----
        //
        // To retire an event, set its Registry row to AnalyticsEventStatus.Retired and keep its code and alias.
        // Keep the event class deserializable, and marked obsolete, while old events can exist in a player's
        // event log. Schema evolution rules are in docs/analytics.md, "Codes and the registry".

        /// <summary>A range of codes and its owner.</summary>
        public sealed class Range
        {
            public int            First { get; }
            public int            Last  { get; }
            public AnalyticsOwner Owner { get; }

            public Range(int first, int last, AnalyticsOwner owner)
            {
                First = first;
                Last  = last;
                Owner = owner;
            }

            public bool Contains(int code) => code >= First && code <= Last;
        }

        /// <summary>One allocated code with its alias, type name, owner and status.</summary>
        public sealed class Entry
        {
            public int                  Code     { get; }
            public string               Alias    { get; }
            public string               TypeName { get; }
            public AnalyticsOwner       Owner    { get; }
            public AnalyticsEventStatus Status   { get; }

            /// <summary>
            /// Whether the alias must equal <see cref="AliasFor"/> of the type name. True by default, which
            /// catches typos. Set it to false when the type was renamed, because the alias must not change.
            /// </summary>
            public bool AliasFollowsTypeName { get; }

            public Entry(int code, string alias, string typeName, AnalyticsOwner owner, AnalyticsEventStatus status, bool aliasFollowsTypeName = true)
            {
                Code                 = code;
                Alias                = alias;
                TypeName             = typeName;
                Owner                = owner;
                Status               = status;
                AliasFollowsTypeName = aliasFollowsTypeName;
            }
        }

        /// <summary>
        /// The code ranges. They cover <see cref="FirstGameCode"/> to <see cref="LastGameCode"/> without gaps or
        /// overlaps, so every code has exactly one owner.
        /// <para>
        /// <b>Two intentional differences from the analytics spec.</b> The spec puts shared foundations at 1-49
        /// and the card game at 100-149. The card game's codes were already in use at 10-13 and codes are never
        /// reassigned, so the two ranges are swapped here. The spec has no range for player segments, so
        /// 550-574 is taken from the spec's unallocated range.
        /// </para>
        /// </summary>
        public static readonly IReadOnlyList<Range> Ranges = new[]
        {
            new Range(  1,  49, AnalyticsOwner.Gameplay),
            new Range( 50,  99, AnalyticsOwner.ClientObservation),
            new Range(100, 149, AnalyticsOwner.Foundations),
            new Range(150, 199, AnalyticsOwner.Identity),
            new Range(200, 249, AnalyticsOwner.Economy),
            new Range(250, 299, AnalyticsOwner.Rewards),
            new Range(300, 324, AnalyticsOwner.SpinWheel),
            new Range(325, 349, AnalyticsOwner.Missions),
            new Range(350, 399, AnalyticsOwner.Cosmetics),
            new Range(400, 449, AnalyticsOwner.Offers),
            new Range(450, 499, AnalyticsOwner.WeeklyEvent),
            new Range(500, 549, AnalyticsOwner.Competition),
            new Range(550, 574, AnalyticsOwner.Segments),
            new Range(575, 899, AnalyticsOwner.Unallocated),
            new Range(900, 999, AnalyticsOwner.Legacy),
        };

        /// <summary>
        /// Every allocated code, live or reserved. The contract tests fail for an event type that is not listed
        /// here, and for an entry marked <see cref="AnalyticsEventStatus.Live"/> that has no type.
        /// </summary>
        public static readonly IReadOnlyList<Entry> Registry = new[]
        {
            new Entry(MatchStarted,          "match_started",           nameof(PlayerEventMatchStarted),          AnalyticsOwner.Gameplay,          AnalyticsEventStatus.Live),
            new Entry(MatchFinished,         "match_finished",          nameof(PlayerEventMatchFinished),         AnalyticsOwner.Gameplay,          AnalyticsEventStatus.Live),
            new Entry(MatchSeatLost,         "match_seat_lost",         nameof(PlayerEventMatchSeatLost),         AnalyticsOwner.Gameplay,          AnalyticsEventStatus.Live),
            new Entry(MatchmakingWait,       "matchmaking_wait",        nameof(PlayerEventMatchmakingWait),       AnalyticsOwner.Gameplay,          AnalyticsEventStatus.Live),

            new Entry(ScreenViewed,          "screen_viewed",           nameof(PlayerEventScreenViewed),          AnalyticsOwner.ClientObservation, AnalyticsEventStatus.Live),
            new Entry(PromotedEntrySelected, "promoted_entry_selected", nameof(PlayerEventPromotedEntrySelected), AnalyticsOwner.ClientObservation, AnalyticsEventStatus.Live),

            // These three use the aliases the analytics spec reserved, which start with "player_" while the type
            // names do not. The alias must not change, so these rows opt out of the type-name check.
            new Entry(IdentityInitialized,   "player_identity_initialized", nameof(PlayerEventIdentityInitialized), AnalyticsOwner.Identity,       AnalyticsEventStatus.Live,     aliasFollowsTypeName: false),
            new Entry(NameChangeRejected,    "player_name_change_rejected", nameof(PlayerEventNameChangeRejected),  AnalyticsOwner.Identity,       AnalyticsEventStatus.Live,     aliasFollowsTypeName: false),
            new Entry(NameChanged,           "player_name_changed",         nameof(PlayerEventNameChangeAccepted),  AnalyticsOwner.Identity,       AnalyticsEventStatus.Live,     aliasFollowsTypeName: false),

            new Entry(EconomyTransaction,    "economy_transaction",     nameof(PlayerEventEconomyTransaction),    AnalyticsOwner.Economy,           AnalyticsEventStatus.Live),
            new Entry(EconomySpendRejected,  "economy_spend_rejected",  nameof(PlayerEventEconomySpendRejected),  AnalyticsOwner.Economy,           AnalyticsEventStatus.Live),

            new Entry(DailyRewardClaimed,     "daily_reward_claimed",      nameof(PlayerEventDailyRewardClaimed),      AnalyticsOwner.Rewards,          AnalyticsEventStatus.Live),
            new Entry(FirstWeekDayCompleted,  "first_week_day_completed",  nameof(PlayerEventFirstWeekDayCompleted),  AnalyticsOwner.Rewards,          AnalyticsEventStatus.Live),
            new Entry(FirstWeekRewardClaimed, "first_week_reward_claimed", nameof(PlayerEventFirstWeekRewardClaimed), AnalyticsOwner.Rewards,          AnalyticsEventStatus.Live),

            new Entry(WheelSpinResolved,     "wheel_spin_resolved",     nameof(PlayerEventWheelSpinResolved),     AnalyticsOwner.SpinWheel,         AnalyticsEventStatus.Live),

            new Entry(PersonalizedOffersPreferenceChanged, "personalized_offers_preference_changed", nameof(PlayerEventPersonalizedOffersPreferenceChanged), AnalyticsOwner.Offers, AnalyticsEventStatus.Live),

            new Entry(CosmeticPurchased,     "cosmetic_purchased",      nameof(PlayerEventCosmeticPurchased),     AnalyticsOwner.Cosmetics,         AnalyticsEventStatus.Live),
            new Entry(CosmeticEquipped,      "cosmetic_equipped",       nameof(PlayerEventCosmeticEquipped),      AnalyticsOwner.Cosmetics,         AnalyticsEventStatus.Live),

            new Entry(MissionCompleted,      "mission_completed",       nameof(PlayerEventMissionCompleted),      AnalyticsOwner.Missions,          AnalyticsEventStatus.Live),
            new Entry(MissionRewardClaimed,  "mission_reward_claimed",  nameof(PlayerEventMissionRewardClaimed),  AnalyticsOwner.Missions,          AnalyticsEventStatus.Live),

            new Entry(WeeklyEventTargetReached, "weekly_event_target_reached", nameof(PlayerEventWeeklyEventTargetReached), AnalyticsOwner.WeeklyEvent, AnalyticsEventStatus.Live),
            new Entry(WeeklyEventRewardClaimed, "weekly_event_reward_claimed", nameof(PlayerEventWeeklyEventRewardClaimed), AnalyticsOwner.WeeklyEvent, AnalyticsEventStatus.Live),

            new Entry(TournamentJoined,                 "tournament_joined",                  nameof(PlayerEventTournamentJoined),                 AnalyticsOwner.Competition, AnalyticsEventStatus.Live),
            new Entry(TournamentMatchCounted,           "tournament_match_counted",           nameof(PlayerEventTournamentMatchCounted),           AnalyticsOwner.Competition, AnalyticsEventStatus.Live),
            new Entry(TournamentMilestoneClaimed,       "tournament_milestone_claimed",       nameof(PlayerEventTournamentMilestoneClaimed),       AnalyticsOwner.Competition, AnalyticsEventStatus.Live),
            new Entry(TournamentResolved,               "tournament_resolved",                nameof(PlayerEventTournamentResolved),               AnalyticsOwner.Competition, AnalyticsEventStatus.Live),
            new Entry(TournamentPlacementRewardClaimed, "tournament_placement_reward_claimed", nameof(PlayerEventTournamentPlacementRewardClaimed), AnalyticsOwner.Competition, AnalyticsEventStatus.Live),
        };

        /// <summary>The range a code falls in, or null if it is outside the codes the SDK leaves to the game.</summary>
        public static Range RangeOf(int code) => Ranges.FirstOrDefault(range => range.Contains(code));

        /// <summary>The registry entry for a code, or null if the code is free.</summary>
        public static Entry EntryOf(int code) => Registry.FirstOrDefault(entry => entry.Code == code);

        /// <summary>
        /// Returns the default alias for a type name: the name without its <c>PlayerEvent</c> prefix, converted
        /// to snake_case.
        /// </summary>
        public static string AliasFor(string typeName)
        {
            if (typeName == null)
                throw new ArgumentNullException(nameof(typeName));

            const string prefix = "PlayerEvent";
            string       stem   = typeName.StartsWith(prefix, StringComparison.Ordinal) ? typeName.Substring(prefix.Length) : typeName;

            List<char> alias = new List<char>(stem.Length * 2);
            for (int index = 0; index < stem.Length; index++)
            {
                char c = stem[index];
                if (char.IsUpper(c) && index > 0)
                    alias.Add('_');
                alias.Add(char.ToLowerInvariant(c));
            }

            return new string(alias.ToArray());
        }
    }
}
