using NUnit.Framework;

namespace Game.Logic.Tests
{
    /// <summary>
    /// T1–T3: every game ends, ends inside the ceiling Tuckered Out implies rather than inside whatever bound
    /// the harness happens to enforce, drains its effect queue rather than looping, and ends in exactly one of
    /// three shapes. <c>InvariantBatchTests</c> runs the walk over the bulk seeded run; these are its negative
    /// controls.
    /// </summary>
    [TestFixture]
    public class TerminationTests
    {
        static SharedGameConfig Config => TestGameConfig.Shared;

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
