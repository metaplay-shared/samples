using Metaplay.Core;
using NUnit.Framework;
using System.Collections.Generic;

namespace Game.Logic.Tests
{
    /// <summary> The ramp, the refill, the Acorn, and what the Weather does to a cost. </summary>
    [TestFixture]
    public class ManaTests
    {
        SharedGameConfig Config => TestGameConfig.Shared;

        MatchEngine Dealt(ulong seed, string _ = null)
        {
            MatchSetup setup = new MatchSetup(seed, Config, MatchTimings.Instant, TestDecks.Standard(Config), TestDecks.Alternate(Config));
            MatchEngine engine = MatchEngine.Create(setup);
            engine.Mulligan(0);
            engine.Mulligan(1);
            return engine;
        }

        [Test]
        public void FirstTurnHasExactlyOneMana()
        {
            MatchEngine engine = Dealt(seed: 3);
            SeatState   first  = engine.Rules.Seat(engine.Rules.SeatOnTurn);

            // Max mana starts at zero and the turn-1 ramp produces one: "start at 1" describes the first
            // turn's available mana, not a starting maximum the ramp then adds to.
            Assert.That(first.MaxMana, Is.EqualTo(1));
            Assert.That(first.Mana, Is.EqualTo(1));
        }

        [Test]
        public void MaxManaRampsByOneEachTurnWithNoCap()
        {
            Scenario scenario = new Scenario(Config);
            scenario.Seat(0).Mana(10, 10).DeckOf(10);
            scenario.Seat(1).Mana(10, 10).DeckOf(10);
            MatchEngine engine = scenario.OnTurn(0).Build();

            engine.PassTurn();
            engine.PassTurn();
            Assert.That(engine.Rules.Seat(0).MaxMana, Is.EqualTo(11), "one per own turn start");

            engine.PassTurn();
            engine.PassTurn();
            Assert.That(engine.Rules.Seat(0).MaxMana, Is.EqualTo(12), "and no cap at ten");
        }

        [Test]
        public void ManaRefillsToMaxAtStartOfTurn()
        {
            Scenario scenario = new Scenario(Config);
            scenario.Seat(0).Mana(0, 5).DeckOf(5);
            scenario.Seat(1).DeckOf(5);
            MatchEngine engine = scenario.OnTurn(0).Build();

            engine.PassTurn();
            engine.PassTurn();

            Assert.That(engine.Rules.Seat(0).Mana, Is.EqualTo(engine.Rules.Seat(0).MaxMana));
        }

        [Test]
        public void UnspentManaDoesNotCarryOver()
        {
            // Current well below maximum, so "refilled to maximum" and "kept what was left and added the
            // ramp" are different numbers. With nine unspent they would both be ten and the test would pass
            // either way.
            Scenario scenario = new Scenario(Config);
            scenario.Seat(0).Mana(2, 9).DeckOf(5);
            scenario.Seat(1).DeckOf(5);
            MatchEngine engine = scenario.OnTurn(0).Build();

            engine.PassTurn();
            engine.PassTurn();

            Assert.That(engine.Rules.Seat(0).MaxMana, Is.EqualTo(10));
            Assert.That(engine.Rules.Seat(0).Mana, Is.EqualTo(10), "the refill sets current to maximum rather than adding to it");
        }

        [Test]
        public void TheAcornGrantsOneCurrentManaOnly()
        {
            Scenario       scenario = new Scenario(Config);
            CardInstanceId acorn    = scenario.Seat(0).Hand("TheAcorn");
            scenario.Seat(0).Mana(1, 1).DeckOf(3);
            scenario.Seat(1).DeckOf(3);
            MatchEngine engine = scenario.OnTurn(0).Build();

            Assert.That(engine.Play(0, acorn), Is.EqualTo(MatchIntentResults.Success));
            Assert.That(engine.Rules.Seat(0).Mana, Is.EqualTo(2));
            Assert.That(engine.Rules.Seat(0).MaxMana, Is.EqualTo(1));
        }

        [Test]
        public void CurrentManaMayExceedMaxAfterTheAcorn()
        {
            Scenario       scenario = new Scenario(Config);
            CardInstanceId acorn    = scenario.Seat(0).Hand("TheAcorn");
            scenario.Seat(0).Mana(2, 2).DeckOf(3);
            scenario.Seat(1).DeckOf(3);
            MatchEngine engine = scenario.OnTurn(0).Build();

            engine.Play(0, acorn);

            Assert.That(engine.Rules.Seat(0).Mana, Is.GreaterThan(engine.Rules.Seat(0).MaxMana));
        }

        [Test]
        public void PermanentMaxManaSurvivesTheNextRefill()
        {
            Scenario       scenario = new Scenario(Config);
            CardInstanceId hoard    = scenario.Seat(0).Hand("AcornHoard");
            scenario.Seat(0).Mana(4, 4).DeckOf(4);
            scenario.Seat(1).DeckOf(4);
            MatchEngine engine = scenario.OnTurn(0).Build();

            engine.Play(0, hoard);
            Assert.That(engine.Rules.Seat(0).MaxMana, Is.EqualTo(5));
            Assert.That(engine.Rules.Seat(0).Mana, Is.EqualTo(2), "the maximum moved, current mana only paid the cost");

            engine.PassTurn();
            engine.PassTurn();

            Assert.That(engine.Rules.Seat(0).MaxMana, Is.EqualTo(6), "the permanent gain is still there under the next ramp");
            Assert.That(engine.Rules.Seat(0).Mana, Is.EqualTo(6), "and the refill makes all of it spendable");
        }

        [Test]
        public void PermanentManaRaisesTheMaximumWithoutGrantingItNow()
        {
            // "Gain +1 max mana permanently" is a ramp, payable from the next refill. Granting current mana
            // as well would make a 2-mana card refund half of itself the turn it is played.
            Scenario       scenario = new Scenario(Config);
            CardInstanceId hoard    = scenario.Seat(0).Hand("AcornHoard"); // 2 mana
            scenario.Seat(0).Mana(4, 4).DeckOf(6);
            scenario.Seat(1).DeckOf(6);
            MatchEngine engine = scenario.OnTurn(0).Build();

            engine.Play(0, hoard);

            Assert.That(engine.Rules.Seat(0).MaxMana, Is.EqualTo(5), "the maximum moved");
            Assert.That(engine.Rules.Seat(0).Mana, Is.EqualTo(2), "and current mana only paid the cost");

            engine.PassTurn();
            engine.PassTurn();

            Assert.That(engine.Rules.Seat(0).Mana, Is.EqualTo(6), "the gain arrives with the next refill");
        }

        [Test]
        public void APermanentRampEmitsAManaChangeThatMovedOnlyTheMaximum()
        {
            // The signature the client's feedback reads. A ramp is the one mana change
            // with nothing else on the board to show it, and what tells it apart from a refill or a spend is
            // that the maximum grew while the pool stood still — the event carries no delta of its own.
            Scenario       scenario = new Scenario(Config);
            CardInstanceId hoard    = scenario.Seat(0).Hand("AcornHoard"); // 2 mana, +1 max permanently
            scenario.Seat(0).Mana(4, 4).DeckOf(6);
            scenario.Seat(1).DeckOf(6);
            MatchEngine engine = scenario.OnTurn(0).Build();

            engine.Clear();
            engine.Play(0, hoard);

            List<ManaChangedEvent> changes = engine.EventsOf<ManaChangedEvent>();
            Assert.That(changes.Count, Is.EqualTo(2), "the cost, then the ramp");

            Assert.That(changes[0].Mana, Is.EqualTo(2), "the cost was paid out of the pool");
            Assert.That(changes[0].MaxMana, Is.EqualTo(4));

            Assert.That(changes[1].MaxMana, Is.EqualTo(5), "the ramp moved the maximum");
            Assert.That(changes[1].Mana, Is.EqualTo(changes[0].Mana), "and left the pool exactly where it was");
        }

        [Test]
        public void ATurnStartRefillMovesBothNumbers()
        {
            // The other side of the same reading: a refill is not a ramp to the eye, because both numbers
            // move — so the feed does not narrate it and the new acorn does not pop.
            Scenario scenario = new Scenario(Config);
            scenario.Seat(0).Mana(1, 4).DeckOf(6);
            scenario.Seat(1).DeckOf(6);
            MatchEngine engine = scenario.OnTurn(0).Build();

            engine.Clear();
            engine.PassTurn();
            engine.PassTurn();

            List<ManaChangedEvent> changes = engine.EventsOf<ManaChangedEvent>();
            Assert.That(changes, Is.Not.Empty);

            ManaChangedEvent refill = changes[changes.Count - 1];
            Assert.That(refill.MaxMana, Is.EqualTo(5));
            Assert.That(refill.Mana, Is.EqualTo(5), "the pool moved too, which is what makes it a refill");
        }

        [Test]
        public void TurnManaGrantsCurrentWithoutRaisingTheMaximum()
        {
            // The mirror of the ruling above: the same primitive with the other parameter moves the other
            // number. Slipstream is two mana for this turn only.
            Scenario       scenario = new Scenario(Config);
            CardInstanceId slip     = scenario.Seat(0).Hand("Slipstream"); // 1 mana, gain 2 this turn
            scenario.Seat(0).Mana(4, 4).DeckOf(6);
            scenario.Seat(1).DeckOf(6);
            MatchEngine engine = scenario.OnTurn(0).Build();

            engine.Play(0, slip);

            Assert.That(engine.Rules.Seat(0).Mana, Is.EqualTo(5), "four, less one, plus two");
            Assert.That(engine.Rules.Seat(0).MaxMana, Is.EqualTo(4), "and the maximum did not move");
        }

        [Test]
        public void HarvestMoonsExtraRampIsUsableFromTheNextTurn()
        {
            // The Weather's ramp is a turn-start effect, so it resolves after that turn's refill: the extra
            // maximum is spendable from the next turn. Special-casing the Weather to dodge that would
            // be exactly the branch on a Weather's identity the decomposition exists to avoid.
            Scenario scenario = new Scenario(Config, weatherId: "HarvestMoon");
            scenario.Seat(0).Mana(0, 0).DeckOf(8);
            scenario.Seat(1).Mana(0, 0).DeckOf(8);
            MatchEngine engine = scenario.OnTurn(0).Build();

            engine.PassTurn();
            engine.PassTurn();
            Assert.That(engine.Rules.Seat(0).MaxMana, Is.EqualTo(2));
            Assert.That(engine.Rules.Seat(0).Mana, Is.EqualTo(1), "the extra maximum arrived after the refill");

            engine.PassTurn();
            engine.PassTurn();
            Assert.That(engine.Rules.Seat(0).MaxMana, Is.EqualTo(4));
            Assert.That(engine.Rules.Seat(0).Mana, Is.EqualTo(3), "and is spendable from the turn after");
        }

        [Test]
        public void HarvestMoonRampsByTwoPerTurn()
        {
            Scenario scenario = new Scenario(Config, weatherId: "HarvestMoon");
            scenario.Seat(0).Mana(0, 0).DeckOf(6);
            scenario.Seat(1).DeckOf(6);
            MatchEngine engine = scenario.OnTurn(0).Build();

            engine.PassTurn();
            engine.PassTurn();

            // One from the ramp, one from the Weather's own turn-start effect.
            Assert.That(engine.Rules.Seat(0).MaxMana, Is.EqualTo(2));
        }

        [Test]
        public void PlayingACardSpendsItsCost()
        {
            Scenario       scenario = new Scenario(Config);
            CardInstanceId frog     = scenario.Seat(0).Hand("PondFrog"); // 3 mana
            scenario.Seat(0).Mana(5, 5).DeckOf(3);
            scenario.Seat(1).DeckOf(3);
            MatchEngine engine = scenario.OnTurn(0).Build();

            engine.Play(0, frog);

            Assert.That(engine.Rules.Seat(0).Mana, Is.EqualTo(2));
        }

        [Test]
        public void PlayingWithoutManaIsRefusedNotEnoughMana()
        {
            Scenario       scenario = new Scenario(Config);
            CardInstanceId frog     = scenario.Seat(0).Hand("PondFrog");
            scenario.Seat(0).Mana(2, 2).DeckOf(3);
            scenario.Seat(1).DeckOf(3);
            MatchEngine engine = scenario.OnTurn(0).Build();

            Assert.That(engine.Play(0, frog), Is.EqualTo(MatchIntentResults.NotEnoughMana));
        }

        [Test]
        public void AcornRainDiscountsOnlyTheFirstTrickEachTurn()
        {
            Scenario       scenario = new Scenario(Config, weatherId: "AcornRain");
            CardInstanceId notes    = scenario.Seat(0).Hand("FieldNotes");  // 2 mana trick
            CardInstanceId biscuit  = scenario.Seat(0).Hand("BerrySnack");  // 1 mana trick
            scenario.Seat(0).Mana(9, 9).DeckOf(6);
            scenario.Seat(1).DeckOf(6);
            MatchEngine engine = scenario.OnTurn(0).Build();

            engine.Play(0, notes);
            Assert.That(engine.Rules.Seat(0).Mana, Is.EqualTo(8), "a 2-mana trick cost one less");

            int beforeSecond = engine.Rules.Seat(0).Mana;
            engine.Play(0, biscuit, EffectTargetRef.Den(0));
            Assert.That(engine.Rules.Seat(0).Mana, Is.EqualTo(beforeSecond - 1), "the second trick pays full price");
        }

        [Test]
        public void AcornRainCounterResetsAtStartOfTurn()
        {
            Scenario scenario = new Scenario(Config, weatherId: "AcornRain");
            scenario.Seat(0).Tricks(3).DeckOf(6);
            scenario.Seat(1).DeckOf(6);
            MatchEngine engine = scenario.OnTurn(0).Build();

            engine.PassTurn();
            engine.PassTurn();

            Assert.That(engine.Rules.Seat(0).TricksCastThisTurn, Is.EqualTo(0));
        }

        [Test]
        public void DiscountedCostFloorsAtZero()
        {
            Scenario       scenario = new Scenario(Config, weatherId: "AcornRain");
            CardInstanceId acorn    = scenario.Seat(0).Hand("TheAcorn"); // already costs 0
            scenario.Seat(0).Mana(0, 0).DeckOf(3);
            scenario.Seat(1).DeckOf(3);
            MatchEngine engine = scenario.OnTurn(0).Build();

            Assert.That(engine.Play(0, acorn), Is.EqualTo(MatchIntentResults.Success), "a discount below zero is a cost of zero, not a refund");
            Assert.That(engine.Rules.Seat(0).Mana, Is.EqualTo(1));
        }
    }
}
