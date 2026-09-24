using Metaplay.Core;
using NUnit.Framework;

namespace Game.Logic.Tests
{
    /// <summary>
    /// Legality settles every race: an intent is judged against the state it arrives at, so the loser of a race
    /// is refused because the card has left the hand, the critter has attacked or the turn has passed.
    /// </summary>
    [TestFixture]
    public class RaceTests
    {
        SharedGameConfig Config => TestGameConfig.Shared;

        [Test]
        public void TheSameCardPlayedTwiceIsAcceptedOnce()
        {
            Scenario       scenario = new Scenario(Config);
            CardInstanceId mouse    = scenario.Seat(0).Hand("MeadowMouse");
            scenario.Seat(0).Mana(9, 9).DeckOf(4);
            scenario.Seat(1).DeckOf(4);
            MatchEngine engine = scenario.OnTurn(0).Build();

            PlayCardIntent intent = new PlayCardIntent(mouse, EffectTargetRef.None);

            Assert.That(engine.Submit(0, intent), Is.EqualTo(MatchIntentResults.Success));
            Assert.That(engine.Submit(0, intent), Is.EqualTo(MatchIntentResults.NotInYourHand), "the card has left the hand");
            Assert.That(engine.Rules.Seat(0).Board.Count, Is.EqualTo(1), "a double tap plays one card");
        }

        [Test]
        public void TheSameAttackDeclaredTwiceIsAcceptedOnce()
        {
            Scenario       scenario = new Scenario(Config);
            CardInstanceId frog     = scenario.Seat(0).Board("PondFrog");
            scenario.Seat(0).DeckOf(4);
            scenario.Seat(1).DeckOf(4);
            MatchEngine engine = scenario.OnTurn(0).Build();
            int         denHp  = engine.Rules.Seat(1).DenHp;

            AttackIntent intent = new AttackIntent(frog, EffectTargetRef.Den(1));

            Assert.That(engine.Submit(0, intent), Is.EqualTo(MatchIntentResults.Success));
            Assert.That(engine.Submit(0, intent), Is.EqualTo(MatchIntentResults.CritterAlreadyAttacked));
            Assert.That(denHp - engine.Rules.Seat(1).DenHp, Is.EqualTo(engine.Critter(frog).Attack), "one hit, not two");
        }

        [Test]
        public void EndTurnSentTwiceEndsOneTurn()
        {
            Scenario scenario = new Scenario(Config);
            scenario.Seat(0).DeckOf(4);
            scenario.Seat(1).DeckOf(4);
            MatchEngine engine = scenario.OnTurn(0).Build();
            int         turn   = engine.Turn;

            Assert.That(engine.Submit(0, new EndTurnIntent()), Is.EqualTo(MatchIntentResults.Success));
            Assert.That(engine.Submit(0, new EndTurnIntent()), Is.EqualTo(MatchIntentResults.NotYourTurn));
            Assert.That(engine.Turn, Is.EqualTo(turn + 1));
        }

        [Test]
        public void AnIntentThatArrivesAfterTheTurnPassedIsNotYourTurn()
        {
            Scenario       scenario = new Scenario(Config);
            CardInstanceId mouse    = scenario.Seat(0).Hand("MeadowMouse");
            scenario.Seat(0).Mana(9, 9).DeckOf(4);
            scenario.Seat(1).DeckOf(4);
            MatchEngine engine = scenario.OnTurn(0).Build();

            engine.PassTurn();

            Assert.That(engine.Submit(0, new PlayCardIntent(mouse, EffectTargetRef.None)), Is.EqualTo(MatchIntentResults.NotYourTurn));
        }

        [Test]
        public void ActedThisTurnResetsAtTheTurnBoundary()
        {
            Scenario       scenario = new Scenario(Config);
            CardInstanceId mouse    = scenario.Seat(0).Hand("MeadowMouse");
            scenario.Seat(0).Mana(9, 9).DeckOf(4);
            scenario.Seat(1).DeckOf(4);
            MatchEngine engine = scenario.OnTurn(0).Build();

            Assert.That(engine.Rules.ActedThisTurn, Is.False);
            engine.Play(0, mouse);
            Assert.That(engine.Rules.ActedThisTurn, Is.True);

            engine.PassTurn();
            Assert.That(engine.Rules.ActedThisTurn, Is.False);
        }

        [Test]
        public void TheActionCountMovesByOneForEveryAcceptedSeatActionAndNothingElse()
        {
            MatchSetup  setup  = new MatchSetup(808, Config, MatchTimings.Instant, TestDecks.Standard(Config), TestDecks.Alternate(Config));
            MatchEngine engine = MatchEngine.Create(setup);

            Assert.That(engine.ActionCount, Is.EqualTo(0));
            engine.Mulligan(0);
            engine.Mulligan(1);
            Assert.That(engine.ActionCount, Is.EqualTo(2), "a mulligan is a seat action");

            for (int step = 0; step < 20000 && engine.Phase != MatchPhase.Complete; step++)
            {
                int before = engine.ActionCount;

                if (engine.Pending.Kind == MatchPendingKind.AwaitingEffectChoice)
                {
                    // The host's defaulted answer is the same action a seat's answer is, and counts the same.
                    engine.ExpireDeadline(MatchDeadlineKind.EffectChoice, null);
                    Assert.That(engine.ActionCount, Is.EqualTo(before + 1));
                    continue;
                }

                int         seat   = engine.Rules.SeatOnTurn;
                MatchIntent intent = TestBots.Strongest.ChooseAction(engine.BuildSeatView(seat), seat)
                                     ?? new EndTurnIntent();
                Assert.That(engine.Submit(seat, intent), Is.EqualTo(MatchIntentResults.Success));
                Assert.That(engine.ActionCount, Is.EqualTo(before + 1));

                // A refusal moves nothing.
                Assert.That(engine.Submit(MatchSeats.Other(engine.Rules.SeatOnTurn), new EndTurnIntent()).IsSuccess, Is.False);
                Assert.That(engine.ActionCount, Is.EqualTo(before + 1));
            }

            Assert.That(engine.Phase, Is.EqualTo(MatchPhase.Complete));
        }
    }
}
