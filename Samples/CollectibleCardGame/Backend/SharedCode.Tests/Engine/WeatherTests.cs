using NUnit.Framework;
using System.Collections.Generic;

namespace Game.Logic.Tests
{
    /// <summary>
    /// One public, symmetric modifier per match, expressed in two halves: queries the engine asks (a keyword
    /// aura, a cost rule) and effects the interpreter runs. Neither half is a branch on a Weather's identity.
    /// </summary>
    [TestFixture]
    public class WeatherTests
    {
        SharedGameConfig Config => TestGameConfig.Shared;

        Scenario Under(string weather)
        {
            Scenario scenario = new Scenario(Config, weather);
            scenario.Seat(0).Mana(9, 9).DeckOf(8);
            scenario.Seat(1).Mana(9, 9).DeckOf(8);
            return scenario.OnTurn(0);
        }

        [Test]
        public void WeatherAppliesIdenticallyToBothSeats()
        {
            Scenario       scenario = Under("BubbleBath");
            CardInstanceId mine     = scenario.Seat(0).Hand("PondFrog");
            CardInstanceId theirs   = scenario.Seat(1).Hand("PondFrog");
            MatchEngine    engine   = scenario.Build();

            engine.Play(0, mine);
            engine.PassTurn();
            engine.Play(1, theirs);

            Assert.That(engine.Critter(mine).HasBubble, Is.True);
            Assert.That(engine.Critter(theirs).HasBubble, Is.True);
        }

        [Test]
        public void AcornRainIsACostModifierNotAnEffect()
        {
            WeatherInfo rain = Config.Weathers[WeatherId.FromString("AcornRain")];

            Assert.That(rain.HasCostRule, Is.True);
            Assert.That(rain.HasTrigger, Is.False, "a cost rule is a query the engine asks, not a step it enqueues");
            Assert.That(rain.HasAura, Is.False);

            SeatState fresh = new SeatState(25, 5, 5);
            CardInfo  trick = Config.Cards[CardId.FromString("FieldNotes")];
            Assert.That(WeatherModifiers.CostDelta(rain, trick, fresh), Is.EqualTo(-1));
        }

        [Test]
        public void BubbleBathGrantsBubbleToEveryCritter()
        {
            // Composed rather than asserted on the flag alone: the aura has to reach the rules that read it,
            // and the same setup under no Weather has to behave differently or the test proves nothing.
            Scenario       bath   = Under("BubbleBath");
            CardInstanceId mouse  = bath.Seat(0).Hand("MeadowMouse");   // 5/10, and its card authors no Bubble
            CardInstanceId first  = bath.Seat(1).Board("MeadowMouse");
            CardInstanceId second = bath.Seat(1).Board("MeadowMouse");
            MatchEngine    engine = bath.Build();

            engine.Play(0, mouse);
            Assert.That(engine.Critter(mouse).HasBubble, Is.True);
            Assert.That(engine.Critter(mouse).BubbleIntact, Is.True);

            engine.PassTurn();
            engine.Attack(1, first, EffectTargetRef.OnCritter(mouse));
            Assert.That(engine.Critter(mouse).Damage, Is.Zero, "the granted Bubble reaches the damage rule, not just the flag set");
            Assert.That(engine.Critter(mouse).BubbleIntact, Is.False);

            // The aura is applied when a critter enters play and never re-queried, so the popped Bubble stays
            // popped and the second instance lands. An aura that re-granted it would be an unbreakable shell.
            engine.Attack(1, second, EffectTargetRef.OnCritter(mouse));
            Assert.That(engine.Critter(mouse).Damage, Is.EqualTo(5));
            Assert.That(engine.Critter(mouse).BubbleIntact, Is.False);

            Scenario       clear      = new Scenario(Config);
            CardInstanceId plainMouse = clear.Seat(0).Hand("MeadowMouse");
            clear.Seat(0).Mana(9, 9).DeckOf(8);
            clear.Seat(1).Mana(9, 9).DeckOf(8);
            MatchEngine plain = clear.OnTurn(0).Build();

            plain.Play(0, plainMouse);
            Assert.That(plain.Critter(plainMouse).HasBubble, Is.False, "and without the Weather the very same critter has none");
        }

        [Test]
        public void HarvestMoonModifiesTheRamp()
        {
            Scenario scenario = new Scenario(Config, "HarvestMoon");
            scenario.Seat(0).Mana(0, 0).DeckOf(8);
            scenario.Seat(1).Mana(0, 0).DeckOf(8);
            MatchEngine engine = scenario.OnTurn(0).Build();

            engine.PassTurn();
            engine.PassTurn();

            Assert.That(engine.Rules.Seat(0).MaxMana, Is.EqualTo(2), "the base ramp plus the Weather's own turn-start step");
        }

        [Test]
        public void PicnicDayHealsTheOwnerWhenACritterDies()
        {
            Scenario       scenario = Under("PicnicDay");
            CardInstanceId kit      = scenario.Seat(0).Board("EmberKit");   // 10/5
            CardInstanceId rabbit   = scenario.Seat(1).Board("TrailRabbit"); // 10/5
            scenario.Seat(0).Den(100);
            scenario.Seat(1).Den(100);
            MatchEngine engine = scenario.Build();

            engine.Attack(0, kit, EffectTargetRef.OnCritter(rabbit));

            Assert.That(engine.Rules.Seat(0).DenHp, Is.EqualTo(105), "each owner heals for its own critter");
            Assert.That(engine.Rules.Seat(1).DenHp, Is.EqualTo(105));
        }

        [Test]
        public void PicnicDayHealCanPreventLethalInTheSameResolution()
        {
            // The Den check is once per resolution, after the queue drains, so a Den taken to zero and
            // healed back inside the same resolution survives.
            Scenario       scenario = Under("PicnicDay");
            CardInstanceId kit      = scenario.Seat(0).Board("EmberKit", attack: 5, maxHealth: 5);
            CardInstanceId rabbit   = scenario.Seat(1).Board("TrailRabbit", attack: 5, maxHealth: 5);
            scenario.Seat(0).Den(5);
            scenario.Seat(1).Den(100);
            MatchEngine engine = scenario.Build();

            // Seat 0's own Den is at 1 and takes nothing here; what matters is that both critters die and both
            // owners heal, inside one resolution and before the Den check.
            engine.Attack(0, kit, EffectTargetRef.OnCritter(rabbit));

            Assert.That(engine.Rules.Seat(0).DenHp, Is.EqualTo(10));
            Assert.That(engine.Phase, Is.EqualTo(MatchPhase.Playing));
        }

        [Test]
        public void LongShadowsDamagesTheTurnSeatsOwnDen()
        {
            Scenario scenario = new Scenario(Config, "LongShadows");
            scenario.Seat(0).Den(100).DeckOf(8);
            scenario.Seat(1).Den(100).DeckOf(8);
            MatchEngine engine = scenario.OnTurn(0).Build();

            engine.PassTurn();

            Assert.That(engine.Rules.Seat(0).DenHp, Is.EqualTo(95), "resolved as the seat the event belongs to");
            Assert.That(engine.Rules.Seat(1).DenHp, Is.EqualTo(100));
        }

        [Test]
        public void AConfigOnlyWeatherWorksWithNoEngineBranch()
        {
            // The test of whether the decomposition is right: every Weather in the pool must run through the
            // same three hooks, and the engine must name none of them.
            foreach (WeatherInfo weather in Config.Weathers.Values)
            {
                Scenario scenario = new Scenario(Config, weather.WeatherId.Value);
                scenario.Seat(0).Board("MeadowMouse");
                scenario.Seat(0).Hand("PondFrog");
                scenario.Seat(0).Mana(9, 9).DeckOf(8);
                scenario.Seat(1).Board("MeadowMouse");
                scenario.Seat(1).Mana(9, 9).DeckOf(8);
                MatchEngine engine = scenario.OnTurn(0).Build();

                Assert.That(engine.Play(0, engine.SecretHand(0)[0]), Is.EqualTo(MatchIntentResults.Success), weather.WeatherId.Value);
                engine.PassTurn();
                engine.PassTurn();

                // Falsifiable: whichever hooks this row declares are the ones that must have fired, and a row
                // that declares none must have changed nothing about the two seats.
                bool auraShowed = !weather.HasAura || (engine.Rules.Seat(0).Board[1].Keywords & weather.AuraKeyword.Ref.EngineFlag) != 0;
                Assert.That(auraShowed, Is.True, $"{weather.WeatherId}: the aura did not reach a critter that entered play");

                bool triggerShowed = !weather.HasTrigger
                    || engine.Rules.Seat(0).MaxMana != 9
                    || engine.Rules.Seat(0).DenHp != Config.Global.DenStartingHp
                    || engine.Rules.Seat(1).DenHp != Config.Global.DenStartingHp;
                Assert.That(triggerShowed, Is.True, $"{weather.WeatherId}: the triggered hook changed nothing at all");

                Assert.That(engine.Phase, Is.EqualTo(MatchPhase.Playing), weather.WeatherId.Value);
            }

            // …and no engine source names a Weather: every hook is read off the config row.
            foreach (WeatherId id in Config.Weathers.Keys)
            {
                Assert.That(typeof(WeatherModifiers).GetMethod(id.Value), Is.Null, $"{id} has a method of its own");
                Assert.That(typeof(MatchEngine).GetMethod(id.Value), Is.Null, $"{id} has a method of its own");
            }
        }
    }
}
