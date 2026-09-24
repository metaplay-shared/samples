using Metaplay.Core;
using Metaplay.Core.Serialization;
using NUnit.Framework;

namespace Game.Logic.Tests
{
    /// <summary>
    /// The engine's state is serializable so its host can persist it in a <c>ServerOnly</c> member and restore
    /// a match mid-game after a restart. Nothing here is about the network: the engine never persists anything
    /// itself.
    /// </summary>
    [TestFixture]
    public class EngineSerializationTests
    {
        SharedGameConfig Config => TestGameConfig.Shared;

        /// <summary> A game several turns in, with a board, a graveyard and a pending choice. </summary>
        MatchEngine MidGame(ulong seed = 909)
        {
            MatchSetup  setup  = new MatchSetup(seed, Config, MatchTimings.Default, TestDecks.Standard(Config), TestDecks.Alternate(Config));
            MatchEngine engine = MatchEngine.Create(setup);
            engine.Mulligan(0);
            engine.Mulligan(1);

            for (int step = 0; step < 60 && engine.Phase == MatchPhase.Playing; step++)
            {

                if (engine.Pending.Kind == MatchPendingKind.AwaitingEffectChoice)
                    break;

                int         seat   = engine.Rules.SeatOnTurn;
                MatchIntent intent = TestBots.Strongest.ChooseAction(engine.BuildSeatView(seat), seat)
                                     ?? new EndTurnIntent();
                engine.Submit(seat, intent);
            }

            return engine;
        }

        /// <summary>
        /// The model through the database's own mask. <c>IncludeAll</c> keeps everything, secret included,
        /// which is what makes a restart resumable.
        /// </summary>
        static MatchModel RoundTrip(MatchModel model, SharedGameConfig config)
        {
            byte[]     bytes    = MetaSerialization.SerializeTagged(model, MetaSerializationFlags.IncludeAll, logicVersion: null);
            MatchModel restored = MetaSerialization.DeserializeTagged<MatchModel>(bytes, MetaSerializationFlags.IncludeAll, config, logicVersion: null);
            restored.GameConfig = config;
            return restored;
        }

        [Test]
        public void MidGameStateSurvivesTheSerializationRoundTrip()
        {
            MatchEngine engine   = MidGame();
            MatchModel  restored = RoundTrip(engine.State, Config);
            MatchEngine revived  = MatchEngine.Wrap(restored);

            Assert.That(revived.ComputeRulesHash(), Is.EqualTo(engine.ComputeRulesHash()));
            Assert.That((revived.Turn, revived.ActionCount), Is.EqualTo((engine.Turn, engine.ActionCount)));
            Assert.That(revived.Rules.SeatOnTurn, Is.EqualTo(engine.Rules.SeatOnTurn));
            Assert.That(revived.Weather.WeatherId, Is.EqualTo(engine.Weather.WeatherId));

            for (int seat = 0; seat < MatchSeats.Count; seat++)
            {
                Assert.That(revived.Rules.Seat(seat).DenHp, Is.EqualTo(engine.Rules.Seat(seat).DenHp));
                Assert.That(revived.SecretHand(seat), Is.EqualTo(engine.SecretHand(seat)));
                Assert.That(revived.SecretDeck(seat), Is.EqualTo(engine.SecretDeck(seat)));
                Assert.That(revived.Rules.Seat(seat).UnseenPool, Is.EqualTo(engine.Rules.Seat(seat).UnseenPool));
                Assert.That(revived.Rules.Seat(seat).Board.Count, Is.EqualTo(engine.Rules.Seat(seat).Board.Count));
            }
        }

        [Test]
        public void RestoredEngineContinuesTheGameIdentically()
        {
            MatchEngine original = MidGame(seed: 4321);
            MatchEngine revived  = MatchEngine.Wrap(RoundTrip(original.State, Config));

            original.PlayOutRemainder(TestBots.Strongest);
            revived.PlayOutRemainder(TestBots.Strongest);

            Assert.That(revived.ComputeRulesHash(), Is.EqualTo(original.ComputeRulesHash()),
                "the generator's position rode along with everything else");
            Assert.That(revived.Result.Outcome, Is.EqualTo(original.Result.Outcome));
        }

        [Test]
        public void AHeldResolutionSurvivesTheRoundTrip()
        {
            Scenario scenario = new Scenario(Config);
            scenario.Seat(0).Hand("PebbleCollector");
            scenario.Seat(0).DeckOf(6, "GreyOwl");
            scenario.Seat(0).Mana(9, 9);
            scenario.Seat(1).Mana(9, 9).DeckOf(6);
            MatchEngine engine = scenario.OnTurn(0).Build();

            engine.Play(0, engine.SecretHand(0)[0]);
            Assert.That(engine.Pending.Kind, Is.EqualTo(MatchPendingKind.AwaitingEffectChoice));

            MatchEngine revived = MatchEngine.Wrap(RoundTrip(engine.State, Config));

            Assert.That(revived.Pending.Kind, Is.EqualTo(MatchPendingKind.AwaitingEffectChoice));
            Assert.That(SecretOps.PeekRevealed(revived.Model, revived.Rules.PendingChoice.Seat), Is.EqualTo(engine.Revealed(engine.Rules.PendingChoice.Seat)));
            Assert.That(revived.Rules.Continuation, Is.EqualTo(engine.Rules.Continuation), "what the resolution still owes rode along too");

            revived.ChooseKept(0, SecretOps.PeekRevealed(revived.Model, revived.Rules.PendingChoice.Seat)[0]);
            Assert.That(revived.Pending.Kind, Is.EqualTo(MatchPendingKind.AwaitingIntent));
        }

        [Test]
        public void TurnReserveSpentSurvivesTheSerializationRoundTrip()
        {
            Scenario scenario = new Scenario(Config);
            scenario.Seat(0).Hand("MeadowMouse");
            scenario.Seat(0).Mana(9, 9).DeckOf(6);
            scenario.Seat(1).DeckOf(6);
            MatchEngine engine = scenario.OnTurn(0).Build(MatchTimings.Default);

            engine.PassTurn();
            engine.PassTurn();
            engine.Play(0, engine.SecretHand(0)[0]);
            engine.ExpireDeadline(MatchDeadlineKind.Turn, TestBots.Strongest);

            Assert.That(engine.Rules.TurnReserveSpent[0], Is.EqualTo(MatchTimings.Default.TurnReserveExtension));

            MatchEngine revived = MatchEngine.Wrap(RoundTrip(engine.State, Config));

            Assert.That(revived.Rules.TurnReserveSpent[0], Is.EqualTo(engine.Rules.TurnReserveSpent[0]));
            Assert.That(revived.ReserveRemaining(0), Is.EqualTo(engine.ReserveRemaining(0)));
            Assert.That(revived.ComputeRulesHash(), Is.EqualTo(engine.ComputeRulesHash()));
        }

        [Test]
        public void PacingStampsSurviveTheRoundTripButAreOutsideTheHash()
        {
            MatchEngine engine = MidGame();
            MatchEngine paced  = MatchEngine.Wrap(RoundTrip(engine.State, Config));

            Assert.That(paced.Pending.DeadlineAt, Is.EqualTo(engine.Pending.DeadlineAt), "the host re-arms every timer from the absolute stamps");

            // …and the stamps are structurally not in the hash: it is taken over the rules half alone.
            Assert.That(typeof(MatchModel).GetProperty("Pacing"), Is.Not.Null);
            Assert.That(typeof(MatchRulesState).GetProperty("Pacing"), Is.Null);
        }
    }
}
