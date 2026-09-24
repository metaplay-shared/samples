using Metaplay.Core.Player;
using System.Collections.Generic;
using System.Globalization;

namespace Game.Logic
{
    /// <summary>
    /// The ids of the sample's player segments (<c>docs/offers.md</c>, "Player segments"). The conditions are
    /// config in <c>GameConfigSource/PlayerSegments.csv</c>. Offer groups, analytics and the Dashboard refer to a
    /// segment by id, so renaming one needs a migration, while its conditions can change with a config publish.
    /// There is no segment that matches every player: an offer for everyone uses an offer group with no targeting.
    /// </summary>
    public static class PlayerSegmentIds
    {
        /// <summary>
        /// A player in their first week who has bought nothing. The starter bundle is promoted to this segment.
        /// </summary>
        public static readonly PlayerSegmentId FirstWeekNonPurchaser = PlayerSegmentId.FromString("first_week_non_purchaser");

        /// <summary>
        /// A player who holds enough gems to afford a premium bundle. Segments choose which offer is promoted to
        /// a player. They do not change the price.
        /// </summary>
        public static readonly PlayerSegmentId GemFunded = PlayerSegmentId.FromString("gem_funded");

        /// <summary>
        /// A player who has played many games and bought nothing. It is based on games played, because the
        /// sample has no spend tiers or predicted value (<c>docs/offers.md</c>).
        /// </summary>
        public static readonly PlayerSegmentId EngagedNonPurchaser = PlayerSegmentId.FromString("engaged_non_purchaser");

        /// <summary>
        /// Every segment id the sample defines. The order has no meaning. When a player is in several segments,
        /// offer-group priority decides which offer is shown (<c>docs/offers.md</c>).
        /// </summary>
        public static readonly IReadOnlyList<PlayerSegmentId> All = new List<PlayerSegmentId>
        {
            FirstWeekNonPurchaser,
            GemFunded,
            EngagedNonPurchaser,
        };
    }

    /// <summary>
    /// Helpers that build segment conditions in code, for tests that need a segment without a config archive.
    /// <para>
    /// The published segments come from <c>GameConfigSource/PlayerSegments.csv</c> through the SDK's segment
    /// source item, not from here. These helpers call <see cref="PlayerPropertyRequirement.ParseFromStrings"/>,
    /// the same call that parses the sheet's <c>PropMin</c> and <c>PropMax</c> columns, so a condition built
    /// here behaves like the same values in the sheet.
    /// </para>
    /// </summary>
    public static class PlayerSegmentConditions
    {
        /// <summary>The property is at least <paramref name="min"/>, with no upper bound.</summary>
        public static PlayerPropertyRequirement AtLeast(PlayerPropertyId id, int min) =>
            PlayerPropertyRequirement.ParseFromStrings(id, Text(min), null);

        /// <summary>The property is between <paramref name="min"/> and <paramref name="max"/>, both included.</summary>
        public static PlayerPropertyRequirement Between(PlayerPropertyId id, int min, int max) =>
            PlayerPropertyRequirement.ParseFromStrings(id, Text(min), Text(max));

        /// <summary>The property is exactly <paramref name="value"/>.</summary>
        public static PlayerPropertyRequirement Exactly(PlayerPropertyId id, int value) =>
            PlayerPropertyRequirement.ParseFromStrings(id, Text(value), Text(value));

        /// <summary>The boolean property is <c>true</c>. Used to require that the player has not turned off personalized offers.</summary>
        public static PlayerPropertyRequirement IsTrue(PlayerPropertyId id) =>
            PlayerPropertyRequirement.ParseFromStrings(id, "true", null);

        /// <summary>A condition that holds when every one of <paramref name="requirements"/> holds.</summary>
        public static PlayerSegmentBasicCondition AllOf(params PlayerPropertyRequirement[] requirements) =>
            new PlayerSegmentBasicCondition(new List<PlayerPropertyRequirement>(requirements), requireAnySegment: null, requireAllSegments: null);

        static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);
    }
}
