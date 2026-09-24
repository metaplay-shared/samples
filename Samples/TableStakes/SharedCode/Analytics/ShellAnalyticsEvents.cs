using Metaplay.Core.Analytics;
using Metaplay.Core.Model;
using Metaplay.Core.Player;

namespace Game.Logic
{
    /// <summary>
    /// Every screen the shell can report, as a closed set.
    /// <para>
    /// An enum is used instead of the route string because a route can vary in query strings, trailing slashes
    /// and IDs, which would split one screen into many values. A route with no member here is not reported, so
    /// adding a route does not add a value to the analytics data by accident.
    /// </para>
    /// </summary>
    [MetaSerializable]
    public enum ShellScreen
    {
        /// <summary>Never emitted. A route with no member of its own is not reported.</summary>
        Unknown = 0,

        Home           = 1,
        Events         = 2,
        DailyReward    = 3,
        FirstWeekEvent = 4,
        Missions       = 5,
        SpinWheel      = 6,
        WeeklyEvent    = 7,

        /// <summary>
        /// <b>Retired.</b> The <c>/play</c> lobby screen. No route maps to it and nothing emits it
        /// (<c>docs/meta-shell.md</c>, "Screens and routes").
        /// <para>
        /// Existing event logs contain this value, so its number must never be reused for another screen.
        /// </para>
        /// </summary>
        Play           = 8,

        Compete        = 9,

        /// <summary>
        /// <b>Retired.</b> The League tab of the Compete screen. No route maps to it and nothing emits it
        /// (<c>docs/seasonal-tournament.md</c>).
        /// <para>
        /// Existing event logs contain this value, so its number must never be reused for another screen.
        /// </para>
        /// </summary>
        League         = 10,

        Tournament     = 11,
        Shop           = 12,
        Profile        = 13,
        Cosmetics      = 14,
        Table          = 15,
    }

    /// <summary>
    /// Which kind of promoted entry the player selected. Only surfaces that <i>recommend</i> something are
    /// listed. Nav tabs and Back buttons are plain navigation, which <see cref="PlayerEventScreenViewed"/>
    /// already records.
    /// </summary>
    [MetaSerializable]
    public enum PromotedEntryPlacement
    {
        Unknown = 0,

        /// <summary>The single next-action card on Home, which recommends one action to the player.</summary>
        NextActionCard = 1,

        /// <summary>A feature card on a hub or on Home's shortcut row.</summary>
        FeatureCard = 2,

        /// <summary>A teaser on Home: the live activity or the offer strip.</summary>
        Teaser = 3,
    }

    /// <summary>
    /// A top-level route became the screen the player is looking at.
    /// <para>
    /// One of the two events the client may submit (<c>docs/analytics.md</c>). It is
    /// <b>non-authoritative</b>: it changes no state, it may be lost, and the game never reads it. It lets a
    /// person reading one player's event log see which screen the player was on when other events happened.
    /// </para>
    /// </summary>
    [AnalyticsEvent(AnalyticsEventCodes.ScreenViewed, displayName: "Screen viewed", docString: "A top-level route became the player's visible screen. Client-reported and non-authoritative.")]
    [AnalyticsAlias("screen_viewed")]
    [AnalyticsEventKeywords(AnalyticsKeywords.Navigation)]
    public class PlayerEventScreenViewed : PlayerEventBase
    {
        [MetaMember(1)] public ShellScreen Screen { get; private set; }

        public override string EventDescription => $"Opened the {Screen} screen.";

        public PlayerEventScreenViewed() { }

        public PlayerEventScreenViewed(ShellScreen screen)
        {
            Screen = screen;
        }
    }

    /// <summary>
    /// The player selected a promoted entry: the next-action card, a feature card or a teaser.
    /// <para>
    /// The second of the two events the client may submit. It records <i>which surface</i> led the player to a
    /// screen. Screen views alone cannot show this: a player who opens the spin wheel from Home's card and one
    /// who opens it from the nav tab produce the same screen views.
    /// </para>
    /// </summary>
    [AnalyticsEvent(AnalyticsEventCodes.PromotedEntrySelected, displayName: "Promoted entry selected", docString: "The player selected a promoted entry: the next-action card, a feature card or a teaser. Client-reported and non-authoritative.")]
    [AnalyticsAlias("promoted_entry_selected")]
    [AnalyticsEventKeywords(AnalyticsKeywords.Navigation)]
    public class PlayerEventPromotedEntrySelected : PlayerEventBase
    {
        [MetaMember(1)] public PromotedEntryPlacement Placement   { get; private set; }

        /// <summary>The screen the entry was shown on.</summary>
        [MetaMember(2)] public ShellScreen            From        { get; private set; }

        /// <summary>The screen the entry opened.</summary>
        [MetaMember(3)] public ShellScreen            Destination { get; private set; }

        public override string EventDescription => $"Took the {Placement} on {From} through to {Destination}.";

        public PlayerEventPromotedEntrySelected() { }

        public PlayerEventPromotedEntrySelected(PromotedEntryPlacement placement, ShellScreen from, ShellScreen destination)
        {
            Placement   = placement;
            From        = from;
            Destination = destination;
        }
    }
}
