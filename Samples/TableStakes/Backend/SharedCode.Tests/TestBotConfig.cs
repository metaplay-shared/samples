using Game.Logic;
using System.Collections.Generic;

namespace Game.Logic.Tests
{
    /// <summary>
    /// The computer players the unit tests play against: the published opponents, two test-only strengths that
    /// cannot be published, and a roster of names.
    /// <para>
    /// The published opponents copy the rows in <c>GameConfigSource/BotProfiles.csv</c>, because this test assembly
    /// cannot reference the server assembly that loads them. The server test
    /// <c>TheShippedArchiveCarriesTheComputerPlayers</c> fails when the copy and the published config differ.
    /// </para>
    /// </summary>
    public static class TestBotConfig
    {
        public static readonly BotProfileInfo SharpRow  = new BotProfileInfo(BotProfileId.FromString("sharp"),  "Sharp",  mistakeChancePercent: 0,  longThinkChancePercent: 15);
        public static readonly BotProfileInfo SteadyRow = new BotProfileInfo(BotProfileId.FromString("steady"), "Steady", mistakeChancePercent: 10, longThinkChancePercent: 20);
        public static readonly BotProfileInfo CasualRow = new BotProfileInfo(BotProfileId.FromString("casual"), "Casual", mistakeChancePercent: 25, longThinkChancePercent: 25);

        /// <summary>The strongest published opponent: the heuristic bot with no deliberate mistakes.</summary>
        public static readonly BotProfile Sharp = SharpRow.ToProfile();

        public static readonly BotProfile Steady = SteadyRow.ToProfile();
        public static readonly BotProfile Casual = CasualRow.ToProfile();

        /// <summary>
        /// Plays a uniformly random legal card. Test-only: a published strength always uses the heuristic
        /// decision mode.
        /// </summary>
        public static readonly BotProfile TestRandom =
            new BotProfile(BotProfileId.FromString("testRandom"), BotDecisionMode.RandomLegal, mistakeChancePercent: 0, longThinkChancePercent: 0);

        /// <summary>Always plays the lowest legal card, so each deal plays out the same way every time.</summary>
        public static readonly BotProfile TestDeterministic =
            new BotProfile(BotProfileId.FromString("testDeterministic"), BotDecisionMode.LowestLegal, mistakeChancePercent: 0, longThinkChancePercent: 0);

        /// <summary>The published opponents as config rows, which a bot seat draws from when a table forms.</summary>
        public static List<BotProfileInfo> ProfileRows() => new List<BotProfileInfo> { SharpRow, SteadyRow, CasualRow };

        /// <summary>The published opponents as the <see cref="BotProfile"/> values a table holds.</summary>
        public static readonly IReadOnlyList<BotProfile> Opponents = new List<BotProfile> { Sharp, Steady, Casual };

        /// <summary>Every strength the tests play with, including the test-only ones.</summary>
        public static readonly IReadOnlyList<BotProfile> All =
            new List<BotProfile> { Sharp, Steady, Casual, TestRandom, TestDeterministic };

        /// <summary>
        /// The names a table draws its computer players from, copied from the shipped config for the same reason as
        /// the strengths. The list is longer than a table's bot seats because names are drawn without replacement.
        /// </summary>
        public static readonly IReadOnlyList<string> Names = new List<string>
        {
            "Abacus", "Bitwise", "Cogwheel", "Dynamo", "Ember-9", "Flipflop", "Gearbox", "Hexdump",
            "Ironside", "Klaxon", "Lodestone", "Magnetron", "Nullbyte", "Overclock", "Pinwheel",
            "Quicksilver", "Ratchet", "Sprocket", "Turnstile", "Voltaic",
        };

        /// <summary><see cref="Names"/> as config rows.</summary>
        public static List<BotNameInfo> NameRows()
        {
            List<BotNameInfo> rows = new List<BotNameInfo>(Names.Count);
            foreach (string name in Names)
                rows.Add(new BotNameInfo(BotNameId.FromString(name.Replace("-", "").ToLowerInvariant()), name));
            return rows;
        }

        /// <summary><see cref="Names"/> as the roster that a table and the display name check read.</summary>
        public static readonly BotNameRoster Roster = BotNameRoster.FromNames(Names);
    }
}
