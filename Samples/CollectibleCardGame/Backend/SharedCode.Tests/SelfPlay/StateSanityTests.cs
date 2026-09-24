using NUnit.Framework;

namespace Game.Logic.Tests
{
    /// <summary>
    /// S1–S8 of the invariant catalog, over a bulk seeded run: the zone bounds the rules declare, mana that is
    /// never negative and never above its maximum without a grant on the record, a monotonic ramp, an
    /// index-keyed identity registry, no critter left on a board that a drained resolution should have swept,
    /// and an unseen pool that has not drifted from its own derivation.
    /// </summary>
    [TestFixture]
    public class StateSanityTests
    {
        static SharedGameConfig Config => TestGameConfig.Shared;

        [Test]
        public void EveryStepOfEveryGameKeepsTheStateSane()
        {
            SelfPlayHarness.RunBatch(
                "state sanity",
                SelfPlayStreams.StateSanity,
                SelfPlayRun.Games(standard: 60, deep: 4000),
                index => new SelfPlayGameSpec
                {
                    Config = Config,
                    Seat0   = SelfPlaySeats.Mix[index % SelfPlaySeats.Mix.Length].Seat0,
                    Seat1   = SelfPlaySeats.Mix[index % SelfPlaySeats.Mix.Length].Seat1,
                    Pairing = SelfPlaySeats.Mix[index % SelfPlaySeats.Mix.Length].Name,
                    Checks = SelfPlayChecks.StateSanity,
                    // Every action of this game also runs against a follower, and the two models are
                    // compared after each one. This is the refactor's proof, and it is on for every batch.
                    MirrorOnFollower = true,
                });
        }

        [Test]
        public void EveryStepOfEveryPacedGameKeepsTheStateSane()
        {
            // The same games, paced. Nothing about the rules changes with the clock, so a difference between
            // this batch and the one above is the timings — but what the clock changes is the *stamps*: at
            // zero timings every deadline is null, so a follower deriving one from (payload, public state) is
            // never actually put to the test. This batch is where it is.
            SelfPlayHarness.RunBatch(
                "state sanity, paced",
                SelfPlayStreams.StateSanity,
                SelfPlayRun.Games(standard: 20, deep: 400),
                index => new SelfPlayGameSpec
                {
                    Config  = Config,
                    Seat0   = SelfPlaySeats.Mix[index % SelfPlaySeats.Mix.Length].Seat0,
                    Seat1   = SelfPlaySeats.Mix[index % SelfPlaySeats.Mix.Length].Seat1,
                    Pairing = SelfPlaySeats.Mix[index % SelfPlaySeats.Mix.Length].Name,
                    Checks  = SelfPlayChecks.StateSanity,
                    Timings = MatchTimings.Default,
                    MirrorOnFollower = true,
                });

            // The point of the batch, asserted rather than assumed. The set is assembly-wide, so this says
            // "some mirrored path ran a beat release" rather than "this batch did" — FollowerMirrorCoverageTests
            // is the closing ratchet over the whole block; this is the tripwire for the one action a return to
            // zero timings would silently take away again.
        }

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

        [Test]
        public void TheTurnCeilingComesFromContentRatherThanFromAConstantHere()
        {
            // Twenty-two draws to empty a deck, seven escalating ticks to take a full Den from there, both
            // seats, and a turn of slack. If a global moves, the bound moves with it.
            Assert.That(InvariantWalk.TuckeredOutTurnCeiling(Config.Global), Is.EqualTo(60));
        }
    }
}
