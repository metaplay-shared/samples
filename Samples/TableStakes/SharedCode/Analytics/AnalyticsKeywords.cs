using System;
using System.Collections.Generic;

namespace Game.Logic
{
    /// <summary>
    /// The name an analytics event is referred to by outside C#, such as in specs and metric definitions. An
    /// alias must never change. <c>AnalyticsEventCodes.Registry</c> records each event's alias so that a change
    /// is detected.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class AnalyticsAliasAttribute : Attribute
    {
        public string Alias { get; }

        public AnalyticsAliasAttribute(string alias)
        {
            Alias = alias;
        }
    }

    /// <summary>
    /// The complete set of keywords for filtering this game's analytics events in the LiveOps Dashboard.
    /// <para>
    /// Every custom event carries <b>exactly one domain</b> keyword and <b>at most two</b> qualifiers. The set
    /// is small and fixed so the Dashboard filter list stays usable. Do not create keywords from config IDs,
    /// currency names, screens or player data. Put that detail in the event's payload fields instead.
    /// </para>
    /// </summary>
    public static class AnalyticsKeywords
    {
        // ---- Domains: exactly one per event. ----

        public const string Gameplay     = "Gameplay";
        public const string Identity     = "Identity";
        public const string Navigation   = "Navigation";
        public const string Economy      = "Economy";
        public const string Reward       = "Reward";
        public const string SpinWheel    = "SpinWheel";
        public const string Mission      = "Mission";
        public const string Cosmetic     = "Cosmetic";
        public const string Offer        = "Offer";
        public const string IAP          = "IAP";
        public const string LiveOpsEvent = "LiveOpsEvent";
        public const string League       = "League";
        public const string Tournament   = "Tournament";

        // ---- Qualifiers: at most two per event. ----

        public const string Source       = "Source";
        public const string Sink         = "Sink";
        public const string Claim        = "Claim";
        public const string Purchase     = "Purchase";
        public const string Progression  = "Progression";
        public const string Rejected     = "Rejected";
        public const string Session      = "Session";
        public const string Connectivity = "Connectivity";

        public static readonly IReadOnlyList<string> Domains = new[]
        {
            Gameplay, Identity, Navigation, Economy, Reward, SpinWheel, Mission,
            Cosmetic, Offer, IAP, LiveOpsEvent, League, Tournament,
        };

        public static readonly IReadOnlyList<string> Qualifiers = new[]
        {
            Source, Sink, Claim, Purchase, Progression, Rejected, Session, Connectivity,
        };

        /// <summary>The most keywords one event may carry: one domain and two qualifiers.</summary>
        public const int MaxPerEvent = 3;
    }
}
