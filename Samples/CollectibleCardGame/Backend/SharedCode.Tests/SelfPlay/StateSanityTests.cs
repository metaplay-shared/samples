using NUnit.Framework;

namespace Game.Logic.Tests
{
    /// <summary>
    /// S1–S8 of the invariant catalog: the zone bounds the rules declare, mana that is never negative and never
    /// above its maximum without a grant on the record, a monotonic ramp, an index-keyed identity registry, no
    /// critter left on a board that a drained resolution should have swept, and an unseen pool that has not
    /// drifted from its own derivation. <c>InvariantBatchTests</c> runs the walk over the bulk seeded run; this
    /// is its negative control.
    /// </summary>
    [TestFixture]
    public class StateSanityTests
    {
        static SharedGameConfig Config => TestGameConfig.Shared;

        [Test]
        public void TheWalkCatchesAStateItShouldReject()
        {
            // "Nothing was wrong" is also what a walk that checked nothing would report. A hand over the limit
            // is the cheapest thing to plant and it exercises the same reporting path every other check uses.
            Scenario scenario = new Scenario(Config);
            for (int ndx = 0; ndx < Config.Global.MaxHandSize + 1; ndx++)
                scenario.Seat(0).Hand("MeadowMouse");
            scenario.Seat(0).DeckOf(4);
            scenario.Seat(1).DeckOf(4);

            MatchEngine   engine = scenario.OnTurn(0).Build();
            InvariantWalk walk   = new InvariantWalk(Config, SelfPlayChecks.StateSanity);
            walk.Capture(engine);

            Assert.That(walk.Check(engine, new MatchEvent[0]), Does.StartWith("S1"));
        }
    }
}
