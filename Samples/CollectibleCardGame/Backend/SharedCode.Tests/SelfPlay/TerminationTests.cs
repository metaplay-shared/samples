using NUnit.Framework;

namespace Game.Logic.Tests
{
    /// <summary>
    /// T1–T3: every game ends, ends inside the ceiling Tuckered Out implies rather than inside whatever bound
    /// the harness happens to enforce, drains its effect queue rather than looping, and ends in exactly one of
    /// three shapes.
    /// </summary>
    [TestFixture]
    public class TerminationTests
    {
        static SharedGameConfig Config => TestGameConfig.Shared;

        [Test]
        public void EveryGameEndsInsideTheTuckeredOutCeiling()
        {
            SelfPlayBatch batch = SelfPlayHarness.RunBatch(
                "termination",
                SelfPlayStreams.Termination,
                SelfPlayRun.Games(standard: 60, deep: 4000),
                index => new SelfPlayGameSpec
                {
                    Config = Config,
                    Seat0   = SelfPlaySeats.Mix[index % SelfPlaySeats.Mix.Length].Seat0,
                    Seat1   = SelfPlaySeats.Mix[index % SelfPlaySeats.Mix.Length].Seat1,
                    Pairing = SelfPlaySeats.Mix[index % SelfPlaySeats.Mix.Length].Name,
                    Checks = SelfPlayChecks.Termination,
                    // Every action of this game also runs against a follower, and the two models are
                    // compared after each one. This is the refactor's proof, and it is on for every batch.
                    MirrorOnFollower = true,
                });

            Assert.That(batch.Count, Is.GreaterThan(0));
            Assert.That(batch.LongestGame(), Is.LessThanOrEqualTo(InvariantWalk.TuckeredOutTurnCeiling(Config.Global)));

            // Both results actually occur. The per-game shape check already ran inside the batch; what it
            // cannot notice is a harness that produced the same outcome every time for a reason of its own.
            Assert.That(batch.Wins(0), Is.GreaterThan(0), "seat 0 never won a game");
            Assert.That(batch.Wins(1), Is.GreaterThan(0), "seat 1 never won a game");
        }

        [Test]
        public void EveryPacedGameEndsInsideTheTuckeredOutCeiling()
        {
            // The same games, paced. The clock changes no rule, but it changes the *stamps*: at zero timings
            // every deadline is null, so a follower deriving one from (payload, public state) is never
            // actually put to the test. This batch is where it is — a follower that reached a different
            // stamp fails the mirror.
            SelfPlayBatch batch = SelfPlayHarness.RunBatch(
                "termination, paced",
                SelfPlayStreams.Termination,
                SelfPlayRun.Games(standard: 20, deep: 400),
                index => new SelfPlayGameSpec
                {
                    Config  = Config,
                    Seat0   = SelfPlaySeats.Mix[index % SelfPlaySeats.Mix.Length].Seat0,
                    Seat1   = SelfPlaySeats.Mix[index % SelfPlaySeats.Mix.Length].Seat1,
                    Pairing = SelfPlaySeats.Mix[index % SelfPlaySeats.Mix.Length].Name,
                    Checks  = SelfPlayChecks.Termination,
                    Timings = MatchTimings.Default,
                    MirrorOnFollower = true,
                });

            Assert.That(batch.Count, Is.GreaterThan(0));
            Assert.That(batch.LongestGame(), Is.LessThanOrEqualTo(InvariantWalk.TuckeredOutTurnCeiling(Config.Global)),
                "pacing must not move the Tuckered Out ceiling");
        }

        [Test]
        public void TheTerminalStateCheckRejectsAGameThatIsNotOver()
        {
            Scenario scenario = new Scenario(Config);
            scenario.Seat(0).DeckOf(4);
            scenario.Seat(1).DeckOf(4);
            MatchEngine engine = scenario.OnTurn(0).Build();

            Assert.That(InvariantWalk.CheckTerminalState(engine), Does.StartWith("T1"));
        }

        [Test]
        public void TheWalkCatchesAGamePastTheTurnCeiling()
        {
            // T1's branch is unreachable in a real game by construction — that is the point of the ceiling —
            // so the only way to know it would fire is to put a game past it.
            Scenario scenario = new Scenario(Config);
            scenario.Seat(0).DeckOf(4);
            scenario.Seat(1).DeckOf(4);
            MatchEngine engine = scenario.Turn(InvariantWalk.TuckeredOutTurnCeiling(Config.Global) + 1).OnTurn(0).Build();

            InvariantWalk walk = new InvariantWalk(Config, SelfPlayChecks.Termination);
            walk.Capture(engine);

            Assert.That(walk.Check(engine, new MatchEvent[0]), Does.StartWith("T1"));
        }

        [Test]
        public void TheWalkCatchesAResolutionThatNeverDrained()
        {
            // A queue left standing with nothing holding it is what a trigger that re-queues itself would look
            // like from outside, so the walk has to notice one.
            Scenario scenario = new Scenario(Config);
            scenario.Seat(0).DeckOf(4);
            scenario.Seat(1).DeckOf(4);
            MatchEngine engine = scenario.OnTurn(0).Build();

            engine.Rules.EffectQueue.Add(new EffectQueueItem(
                Metaplay.Core.MetaRef<EffectStepInfo>.FromItem(Config.EffectSteps[EffectStepId.FromString("DrawOne")]),
                resolvingSeat: 0,
                CardInstanceSnapshot.None(0),
                EffectTargetRef.None,
                amountBonus: 0,
                excludesSourceFromPlayedCount: false));

            InvariantWalk walk = new InvariantWalk(Config, SelfPlayChecks.Termination);
            walk.Capture(engine);

            Assert.That(walk.Check(engine, new MatchEvent[0]), Does.StartWith("T2"));
        }
    }
}
