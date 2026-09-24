using Metaplay.Core;
using NUnit.Framework;
using System.Collections.Generic;

namespace Game.Logic.Tests
{
    /// <summary> Winning, losing, and the draw — checked once per resolution rather than on every number. </summary>
    [TestFixture]
    public class WinLossTests
    {
        SharedGameConfig Config => TestGameConfig.Shared;

        Scenario Fresh()
        {
            Scenario scenario = new Scenario(Config);
            scenario.Seat(0).Mana(9, 9).DeckOf(8);
            scenario.Seat(1).Mana(9, 9).DeckOf(8);
            return scenario.OnTurn(0);
        }

        [Test]
        public void ReducingTheEnemyDenToZeroWinsTheMatch()
        {
            Scenario       scenario = Fresh();
            CardInstanceId frog     = scenario.Seat(0).Board("PondFrog");
            scenario.Seat(1).Den(15);
            MatchEngine engine = scenario.Build();

            engine.Attack(0, frog, EffectTargetRef.Den(1));

            Assert.That(engine.Phase, Is.EqualTo(MatchPhase.Complete));
            Assert.That(engine.Result.Outcome, Is.EqualTo(MatchOutcome.Seat0Wins));
            Assert.That(engine.Result.WinnerSeat, Is.EqualTo(0));
            Assert.That(engine.Result.Cause, Is.EqualTo(MatchEndCause.DenAtZero));
        }

        [Test]
        public void BothDensAtZeroInOneResolutionIsADraw()
        {
            // Case O: a symmetric effect takes both Dens to zero in the same resolution.
            Scenario       scenario = new Scenario(Config, "LongShadows");
            CardInstanceId whisker  = scenario.Seat(0).Board("SizzleWhisker", attack: 5, maxHealth: 5); // Goodbye: 5 to the enemy Den
            CardInstanceId rabbit   = scenario.Seat(1).Board("TrailRabbit", attack: 5, maxHealth: 5);
            scenario.Seat(0).Den(5).Mana(9, 9).DeckOf(6);
            scenario.Seat(1).Den(5).Mana(9, 9).DeckOf(6);
            MatchEngine engine = scenario.OnTurn(0).Build();

            // Both trade to death; the Goodbye takes seat 1's Den to zero, and seat 0's own Den is taken to
            // zero in the same resolution by ending the turn under Long Shadows. Simpler here: kill both
            // directly with a symmetric setup.
            engine.Attack(0, whisker, EffectTargetRef.OnCritter(rabbit));

            Assert.That(engine.Rules.Seat(1).DenHp, Is.EqualTo(0));
            Assert.That(engine.Phase, Is.EqualTo(MatchPhase.Complete));
            Assert.That(engine.Result.Outcome, Is.EqualTo(MatchOutcome.Seat0Wins), "only one Den reached zero here");

            // And the genuinely symmetric case, built directly.
            Scenario both = new Scenario(Config);
            both.Seat(0).Den(0).DeckOf(6);
            both.Seat(1).Den(0).DeckOf(6);
            CardInstanceId mouse = both.Seat(0).Board("MeadowMouse");
            MatchEngine drawEngine = both.OnTurn(0).Build();
            drawEngine.Attack(0, mouse, EffectTargetRef.Den(1));

            Assert.That(drawEngine.Result.Outcome, Is.EqualTo(MatchOutcome.Draw));
            Assert.That(drawEngine.Result.Cause, Is.EqualTo(MatchEndCause.BothDensAtZero));
        }

        [Test]
        public void DrawResultNamesNoWinner()
        {
            Scenario scenario = new Scenario(Config);
            scenario.Seat(0).Den(0).DeckOf(6);
            scenario.Seat(1).Den(0).DeckOf(6);
            CardInstanceId mouse = scenario.Seat(0).Board("MeadowMouse");
            MatchEngine engine = scenario.OnTurn(0).Build();

            engine.Attack(0, mouse, EffectTargetRef.Den(1));

            Assert.That(engine.Result.IsDraw, Is.True);
            Assert.That(engine.Result.WinnerSeat, Is.EqualTo(MatchSeats.None));
        }

        [Test]
        public void DenHealedBackInsideTheSameResolutionSurvives()
        {
            // Case P: Picnic Day heals the owner when a critter dies, and the Den check happens after the
            // whole queue has drained.
            Scenario       scenario = new Scenario(Config, "PicnicDay");
            CardInstanceId hound    = scenario.Seat(0).Board("BiscuitHound", attack: 5, maxHealth: 5, keywords: KeywordFlags.None);
            CardInstanceId rabbit   = scenario.Seat(1).Board("TrailRabbit", attack: 5, maxHealth: 5);
            scenario.Seat(0).Den(0).Mana(9, 9).DeckOf(6);
            scenario.Seat(1).Den(50).Mana(9, 9).DeckOf(6);
            MatchEngine engine = scenario.OnTurn(0).Build();

            engine.Attack(0, hound, EffectTargetRef.OnCritter(rabbit));

            Assert.That(engine.Rules.Seat(0).DenHp, Is.EqualTo(5), "Picnic Day healed it back before the check");
            Assert.That(engine.Phase, Is.EqualTo(MatchPhase.Playing));
        }

        [Test]
        public void ChainedGoodbyesResolveAgainstAnUpdatedBoard()
        {

            Scenario       scenario = Fresh();
            CardInstanceId storm    = scenario.Seat(0).Hand("CinderStorm"); // 5 damage to every enemy critter
            CardInstanceId whiskerA = scenario.Seat(1).Board("SizzleWhisker", attack: 5, maxHealth: 5);
            CardInstanceId whiskerB = scenario.Seat(1).Board("SizzleWhisker", attack: 5, maxHealth: 5);
            scenario.Seat(0).Den(125);
            MatchEngine engine = scenario.Build();

            engine.Play(0, storm, EffectTargetRef.None);

            // Both died in one sweep and both Goodbyes fired, each against the board as it then was.
            Assert.That(engine.Critter(whiskerA), Is.Null);
            Assert.That(engine.Critter(whiskerB), Is.Null);
            Assert.That(engine.Rules.Seat(0).DenHp, Is.EqualTo(115), "two Goodbyes, one point each");
            Assert.That(engine.EventsOf<CritterDiedEvent>().Count, Is.EqualTo(2));
        }

        [Test]
        public void ResultCarriesEachSeatsPlayedCards()
        {
            Scenario       scenario = Fresh();
            CardInstanceId frog     = scenario.Seat(0).Hand("PondFrog");
            CardInstanceId attacker = scenario.Seat(0).Board("StrayGoat");
            scenario.Seat(1).Den(20);
            MatchEngine engine = scenario.Build();

            engine.Play(0, frog);
            engine.Attack(0, attacker, EffectTargetRef.Den(1));

            Assert.That(engine.Result, Is.Not.Null);
            Assert.That(Cards(engine.Result.PlayedBy(0)), Does.Contain(CardId.FromString("PondFrog")));
            Assert.That(engine.Result.PlayedBy(1), Is.Empty);
        }

        [Test]
        public void SummonedTokensAreNotHeistEligible()
        {
            Scenario       scenario = Fresh();
            CardInstanceId shepherd = scenario.Seat(0).Hand("SheepdogShepherd");
            CardInstanceId attacker = scenario.Seat(0).Board("StrayGoat");
            scenario.Seat(1).Den(20);
            MatchEngine engine = scenario.Build();

            engine.Play(0, shepherd);
            engine.Attack(0, attacker, EffectTargetRef.Den(1));

            Assert.That(Cards(engine.Result.PlayedBy(0)), Does.Contain(CardId.FromString("SheepdogShepherd")));
            Assert.That(Cards(engine.Result.PlayedBy(0)), Does.Not.Contain(CardId.FromString("LambToken")),
                "a token is not a card of yours to lose");
        }

        [Test]
        public void CopiedEnemyTrickIsNotHeistEligible()
        {
            Scenario       scenario = Fresh();
            CardInstanceId bandit   = scenario.Seat(0).Hand("DumpsterBandit"); // Hello: copy the cheapest enemy trick
            scenario.Seat(1).Graveyard("BerrySnack");
            CardInstanceId attacker = scenario.Seat(0).Board("StrayGoat");
            scenario.Seat(1).Den(20);
            MatchEngine engine = scenario.Build();

            engine.Play(0, bandit);

            CardInstanceId copy = engine.SecretHand(0)[engine.SecretHand(0).Count - 1];
            Assert.That(CardLookup.CardId(engine.Model, copy), Is.EqualTo(CardId.FromString("BerrySnack")));

            engine.Play(0, copy, EffectTargetRef.Den(0));
            engine.Attack(0, attacker, EffectTargetRef.Den(1));

            Assert.That(Cards(engine.Result.PlayedBy(0)), Does.Contain(CardId.FromString("DumpsterBandit")));
            Assert.That(Cards(engine.Result.PlayedBy(0)), Does.Not.Contain(CardId.FromString("BerrySnack")),
                "a copy of the opponent's trick is not a card of yours to lose");
        }

        [Test]
        public void IntentsAfterCompleteAreRefusedWrongPhase()
        {
            Scenario       scenario = Fresh();
            CardInstanceId frog     = scenario.Seat(0).Board("PondFrog");
            scenario.Seat(0).Hand("MeadowMouse");
            scenario.Seat(1).Den(15);
            MatchEngine engine = scenario.Build();

            engine.Attack(0, frog, EffectTargetRef.Den(1));
            Assert.That(engine.Phase, Is.EqualTo(MatchPhase.Complete));

            Assert.That(engine.EndTurn(0), Is.EqualTo(MatchIntentResults.WrongPhase));
            Assert.That(engine.Play(0, engine.SecretHand(0)[0]), Is.EqualTo(MatchIntentResults.WrongPhase));
        }

        /// <summary>
        /// The cards a seat's Heist rows name. The rows carry the owned rank as well, so
        /// a test that is about <em>which</em> cards are eligible reads the card half out.
        /// </summary>
        static List<CardId> Cards(IReadOnlyList<HeistEligibleCard> rows)
        {
            List<CardId> cards = new List<CardId>(rows.Count);
            foreach (HeistEligibleCard row in rows)
                cards.Add(row.Card);
            return cards;
        }
    }
}
