using NUnit.Framework;

namespace Game.Logic.Tests
{
    /// <summary>
    /// The whole invariant catalog — state sanity, conservation, legality and termination — over one bulk seeded
    /// run of every profile pairing, including the two random-legal ones, with every action also run against a
    /// follower and the two models compared after each one. The categories' negative controls live beside their
    /// rules in <c>StateSanityTests</c>, <c>ConservationTests</c>, <c>LegalityTests</c> and
    /// <c>TerminationTests</c>.
    /// </summary>
    [TestFixture]
    public class InvariantBatchTests
    {
        static SharedGameConfig Config => TestGameConfig.Shared;

        static SelfPlayGameSpec Spec(int index, MatchTimings timings)
            => new SelfPlayGameSpec
            {
                Config           = Config,
                Seat0            = SelfPlaySeats.Mix[index % SelfPlaySeats.Mix.Length].Seat0,
                Seat1            = SelfPlaySeats.Mix[index % SelfPlaySeats.Mix.Length].Seat1,
                Pairing          = SelfPlaySeats.Mix[index % SelfPlaySeats.Mix.Length].Name,
                Checks           = SelfPlayChecks.All,
                Timings          = timings,
                MirrorOnFollower = true,
            };

        [Test]
        public void EveryStepOfEveryGameKeepsEveryInvariant()
        {
            SelfPlayBatch batch = SelfPlayHarness.RunBatch(
                "invariants",
                SelfPlayStreams.Invariants,
                SelfPlayRun.Games(standard: 80, deep: 17000),
                index => Spec(index, MatchTimings.Instant));

            Assert.That(batch.Count, Is.GreaterThan(0));
            Assert.That(batch.LongestGame(), Is.LessThanOrEqualTo(InvariantWalk.TuckeredOutTurnCeiling(Config.Global)));

            // Both results actually occur. The per-game shape check already ran inside the batch; what it
            // cannot notice is a harness that produced the same outcome every time for a reason of its own.
            Assert.That(batch.Wins(0), Is.GreaterThan(0), "seat 0 never won a game");
            Assert.That(batch.Wins(1), Is.GreaterThan(0), "seat 1 never won a game");
        }

        [Test]
        public void EveryStepOfEveryPacedGameKeepsEveryInvariant()
        {
            // The same games, paced. The clock changes no rule, but it changes the stamps: at zero timings
            // every deadline is null, so a follower deriving one from (payload, public state) is never actually
            // put to the test. This batch is where it is — a follower that reached a different stamp fails the
            // mirror.
            SelfPlayBatch batch = SelfPlayHarness.RunBatch(
                "invariants, paced",
                SelfPlayStreams.Invariants,
                SelfPlayRun.Games(standard: 20, deep: 1600),
                index => Spec(index, MatchTimings.Default));

            Assert.That(batch.Count, Is.GreaterThan(0));
            Assert.That(batch.LongestGame(), Is.LessThanOrEqualTo(InvariantWalk.TuckeredOutTurnCeiling(Config.Global)),
                "pacing must not move the Tuckered Out ceiling");
        }
    }
}
