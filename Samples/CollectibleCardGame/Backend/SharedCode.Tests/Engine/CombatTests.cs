using NUnit.Framework;

namespace Game.Logic.Tests
{
    /// <summary>
    /// The exchange, worked with game-design.md's own cards. Everything simultaneous has two of everything, so the
    /// order is fixed rather than emergent: both damages computed, both applied, then anything dies.
    /// </summary>
    [TestFixture]
    public class CombatTests
    {
        SharedGameConfig Config => TestGameConfig.Shared;

        Scenario Fresh()
        {
            Scenario scenario = new Scenario(Config);
            scenario.Seat(0).Mana(9, 9).DeckOf(6);
            scenario.Seat(1).Mana(9, 9).DeckOf(6);
            return scenario.OnTurn(0);
        }

        [Test]
        public void AnAwakeCritterMayAttackOncePerTurn()
        {
            Scenario       scenario = Fresh();
            CardInstanceId frog     = scenario.Seat(0).Board("PondFrog");
            scenario.Seat(1).Board("MeadowMouse");
            MatchEngine engine = scenario.Build();

            Assert.That(engine.Attack(0, frog, EffectTargetRef.Den(1)), Is.EqualTo(MatchIntentResults.Success));
            Assert.That(engine.Attack(0, frog, EffectTargetRef.Den(1)), Is.EqualTo(MatchIntentResults.CritterAlreadyAttacked));
        }

        [Test]
        public void SleepyCritterIsRefusedCritterAsleep()
        {
            Scenario       scenario = Fresh();
            CardInstanceId frog     = scenario.Seat(0).Board("PondFrog", sleepy: true);
            MatchEngine    engine   = scenario.Build();

            Assert.That(engine.Attack(0, frog, EffectTargetRef.Den(1)), Is.EqualTo(MatchIntentResults.CritterAsleep));
        }

        [Test]
        public void CritterEntersPlaySleepy()
        {
            Scenario       scenario = Fresh();
            CardInstanceId hand     = scenario.Seat(0).Hand("PondFrog");
            MatchEngine    engine   = scenario.Build();

            engine.Play(0, hand);
            Assert.That(engine.Critter(hand).IsSleepy, Is.True);
            Assert.That(engine.Attack(0, hand, EffectTargetRef.Den(1)), Is.EqualTo(MatchIntentResults.CritterAsleep));
        }

        [Test]
        public void AttackerAndDefenderBothDealDamage()
        {
            // Case A: Ember Kit 10/5 Zoomies into Sunbeam Retriever 10/15 Guard.
            Scenario       scenario  = Fresh();
            CardInstanceId kit       = scenario.Seat(0).Board("EmberKit");
            CardInstanceId retriever = scenario.Seat(1).Board("SunbeamRetriever");
            MatchEngine    engine    = scenario.Build();

            engine.Attack(0, kit, EffectTargetRef.OnCritter(retriever));

            Assert.That(engine.Critter(kit), Is.Null, "the 10/5 took ten and died");
            Assert.That(engine.Critter(retriever).Damage, Is.EqualTo(10));
            Assert.That(engine.Critter(retriever).CurrentHealth, Is.EqualTo(5), "it survives, hurt");
        }

        [Test]
        public void DamageOnCrittersPersistsAcrossTurns()
        {
            Scenario       scenario  = Fresh();
            CardInstanceId kit       = scenario.Seat(0).Board("EmberKit");
            CardInstanceId retriever = scenario.Seat(1).Board("SunbeamRetriever");
            MatchEngine    engine    = scenario.Build();

            engine.Attack(0, kit, EffectTargetRef.OnCritter(retriever));
            engine.PassTurn();
            engine.PassTurn();

            Assert.That(engine.Critter(retriever).Damage, Is.EqualTo(10), "healing exists but a turn boundary is not it");
        }

        [Test]
        public void AttackingTheDenTakesNoDamageBack()
        {
            Scenario       scenario = Fresh();
            CardInstanceId frog     = scenario.Seat(0).Board("PondFrog");
            MatchEngine    engine   = scenario.Build();

            engine.Attack(0, frog, EffectTargetRef.Den(1));

            Assert.That(engine.Rules.Seat(1).DenHp, Is.EqualTo(Config.Global.DenStartingHp - 15));
            Assert.That(engine.Critter(frog).Damage, Is.EqualTo(0), "the Den never counterattacks");
        }

        [Test]
        public void MutuallyLethalTradeKillsBothCritters()
        {
            // Case B: 10/5 into 10/5. Both damages are applied before any death check, so both die.
            Scenario       scenario = Fresh();
            CardInstanceId kit      = scenario.Seat(0).Board("EmberKit");
            CardInstanceId rabbit   = scenario.Seat(1).Board("TrailRabbit");
            MatchEngine    engine   = scenario.Build();

            engine.Attack(0, kit, EffectTargetRef.OnCritter(rabbit));

            Assert.That(engine.Critter(kit), Is.Null);
            Assert.That(engine.Critter(rabbit), Is.Null);
            Assert.That(engine.ZoneOf(kit), Is.EqualTo(AuthorityZone.Graveyard));
            Assert.That(engine.ZoneOf(rabbit), Is.EqualTo(AuthorityZone.Graveyard));
        }

        [Test]
        public void SimultaneousDeathsResolveActiveSeatFirst()
        {
            // Built twice, with the identities minted in the opposite order the second time, so "active seat
            // first" cannot be confounded with "lower instance id first".
            Scenario           a                 = Fresh();
            CardInstanceId     kitA              = a.Seat(0).Board("EmberKit");
            CardInstanceId     rabbitA           = a.Seat(1).Board("TrailRabbit");
            MatchEngine        engineA           = a.Build();

            engineA.Attack(0, kitA, EffectTargetRef.OnCritter(rabbitA));

            System.Collections.Generic.List<CritterDiedEvent> deathsA = engineA.EventsOf<CritterDiedEvent>();
            Assert.That(deathsA.Count, Is.EqualTo(2));
            Assert.That(deathsA[0].Instance, Is.EqualTo(kitA), "the active seat's board is swept first");
            Assert.That(deathsA[1].Instance, Is.EqualTo(rabbitA));

            Scenario           b                  = Fresh();
            CardInstanceId     rabbitB            = b.Seat(1).Board("TrailRabbit");   // lower identity
            CardInstanceId     kitB               = b.Seat(0).Board("EmberKit");      // higher identity
            MatchEngine        engineB            = b.Build();

            Assert.That(kitB.Value, Is.GreaterThan(rabbitB.Value));
            engineB.Attack(0, kitB, EffectTargetRef.OnCritter(rabbitB));

            System.Collections.Generic.List<CritterDiedEvent> deathsB = engineB.EventsOf<CritterDiedEvent>();
            Assert.That(deathsB.Count, Is.EqualTo(2));
            Assert.That(deathsB[0].Instance, Is.EqualTo(kitB), "still the active seat's, even though its identity is higher");
            Assert.That(deathsB[1].Instance, Is.EqualTo(rabbitB));
        }

        [Test]
        public void DyingAttackerStillDealtItsDamage()
        {
            Scenario       scenario  = Fresh();
            CardInstanceId kit       = scenario.Seat(0).Board("EmberKit");
            CardInstanceId retriever = scenario.Seat(1).Board("SunbeamRetriever");
            MatchEngine    engine    = scenario.Build();

            engine.Attack(0, kit, EffectTargetRef.OnCritter(retriever));

            Assert.That(engine.Critter(kit), Is.Null);
            Assert.That(engine.Critter(retriever).Damage, Is.EqualTo(10), "the exchange is simultaneous: its damage landed");
        }

        [Test]
        public void DefenderMayStillAttackOnItsOwnTurn()
        {
            Scenario       scenario  = Fresh();
            CardInstanceId kit       = scenario.Seat(0).Board("EmberKit");
            CardInstanceId retriever = scenario.Seat(1).Board("SunbeamRetriever");
            MatchEngine    engine    = scenario.Build();

            engine.Attack(0, kit, EffectTargetRef.OnCritter(retriever));
            engine.PassTurn();

            // Counterattacking does not spend the defender's own attack.
            Assert.That(engine.Attack(1, retriever, EffectTargetRef.Den(0)), Is.EqualTo(MatchIntentResults.Success));
        }

        [Test]
        public void ZeroAttackCritterIsRefused()
        {
            Scenario       scenario = Fresh();
            CardInstanceId snail    = scenario.Seat(0).Board("GardenSnail"); // 0/15 Guard
            scenario.Seat(1).Board("MeadowMouse");
            MatchEngine engine = scenario.Build();

            Assert.That(engine.Attack(0, snail, EffectTargetRef.Den(1)), Is.EqualTo(MatchIntentResults.CritterHasNoAttack));
        }

        [Test]
        public void AttackingAFriendlyCritterIsRefused()
        {
            Scenario       scenario = Fresh();
            CardInstanceId frog     = scenario.Seat(0).Board("PondFrog");
            CardInstanceId mouse    = scenario.Seat(0).Board("MeadowMouse");
            MatchEngine    engine   = scenario.Build();

            Assert.That(engine.Attack(0, frog, EffectTargetRef.OnCritter(mouse)), Is.EqualTo(MatchIntentResults.IllegalTarget));
        }

        [Test]
        public void BoardOrderDoesNotAffectAnyOutcome()
        {
            // There is no positioning in v1: board order is a stable iteration order and nothing else.
            Scenario a = new Scenario(Config);
            a.Seat(0).Mana(9, 9).DeckOf(6);
            a.Seat(1).Mana(9, 9).DeckOf(6);
            CardInstanceId frogA     = a.Seat(0).Board("PondFrog");
            a.Seat(1).Board("MeadowMouse");
            CardInstanceId tortoiseA = a.Seat(1).Board("WiseTortoise");
            MatchEngine engineA = a.OnTurn(0).Build();

            Scenario b = new Scenario(Config);
            b.Seat(0).Mana(9, 9).DeckOf(6);
            b.Seat(1).Mana(9, 9).DeckOf(6);
            CardInstanceId frogB     = b.Seat(0).Board("PondFrog");
            CardInstanceId tortoiseB = b.Seat(1).Board("WiseTortoise");
            b.Seat(1).Board("MeadowMouse");
            MatchEngine engineB = b.OnTurn(0).Build();

            engineA.Attack(0, frogA, EffectTargetRef.OnCritter(tortoiseA));
            engineB.Attack(0, frogB, EffectTargetRef.OnCritter(tortoiseB));

            Assert.That(engineB.Critter(tortoiseB).Damage, Is.EqualTo(engineA.Critter(tortoiseA).Damage));
            Assert.That(engineB.Critter(frogB).Damage, Is.EqualTo(engineA.Critter(frogA).Damage));
        }
    }
}
