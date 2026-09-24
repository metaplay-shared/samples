using NUnit.Framework;
using System.Reflection;

namespace Game.Logic.Tests
{
    /// <summary>
    /// A rank track is stat deltas applied when an instance is created. Nothing downstream of instance
    /// creation knows ranks exist — a rank-5 critter is simply a critter with different numbers.
    /// </summary>
    [TestFixture]
    public class RankTrackTests
    {
        SharedGameConfig Config => TestGameConfig.Shared;

        Scenario Fresh()
        {
            Scenario scenario = new Scenario(Config);
            scenario.Seat(0).Mana(9, 9).DeckOf(6);
            scenario.Seat(1).Mana(9, 9).DeckOf(6);
            return scenario.OnTurn(0);
        }

        [TestCase(1, 15)]
        [TestCase(2, 16)]
        [TestCase(3, 16)]
        [TestCase(4, 17)]
        [TestCase(5, 17)]
        public void GardenSnailGainsHealthAtTwoAndFour(int rank, int health)
        {
            CardStats stats = Config.Cards[CardId.FromString("GardenSnail")].GetStatsAtRank(rank);
            Assert.That(stats.Attack, Is.Zero);
            Assert.That(stats.Health, Is.EqualTo(health));
        }

        [Test]
        public void NewStepsAccumulateEveryStat()
        {
            RankTrackInfo copy = new RankTrackInfo(RankTrackId.FromString("Test"),
                rank2: new RankTrackStep(1, 2, -1, 3), rank4: new RankTrackStep(2, 1, -1, 4));
            Assert.That(copy.IsFlat, Is.False);
            Assert.That(copy.ScalesEffectAmount, Is.True);
            Assert.That(copy.GetCumulativeDeltas(1).IsEmpty, Is.True);
            Assert.That(copy.GetCumulativeDeltas(3).AmountDelta, Is.EqualTo(3));
            RankTrackStep total = copy.GetCumulativeDeltas(5);
            Assert.That(total.AttackDelta, Is.EqualTo(3));
            Assert.That(total.HealthDelta, Is.EqualTo(3));
            Assert.That(total.CostDelta, Is.EqualTo(-2));
            Assert.That(total.AmountDelta, Is.EqualTo(7));
        }

        [Test]
        public void RankOneUsesPrintedStats()
        {
            Scenario       scenario = Fresh();
            CardInstanceId kit      = scenario.Seat(0).Hand("EmberKit", rank: 1);
            MatchEngine    engine   = scenario.Build();

            engine.Play(0, kit);

            Assert.That(engine.Critter(kit).Attack, Is.EqualTo(10));
            Assert.That(engine.Critter(kit).MaxHealth, Is.EqualTo(5));
        }

        [Test]
        public void RankFiveEmberKitGainsOnePointInEach()
        {
            // Case M: Rank 2 gives +0/+1 and rank 4 gives +1/+0, and the deltas are cumulative. A
            // point is a fifth of the stat quantum, so a 10/5 becomes an 11/6 — about 13% over its rank-1
            // self, which is the guardrail the widened domain exists to make reachable (F2).
            Scenario       scenario = Fresh();
            CardInstanceId kit      = scenario.Seat(0).Hand("EmberKit", rank: 5);
            MatchEngine    engine   = scenario.Build();

            engine.Play(0, kit);

            Assert.That(engine.Critter(kit).Attack, Is.EqualTo(11));
            Assert.That(engine.Critter(kit).MaxHealth, Is.EqualTo(6));
        }

        [Test]
        public void RankFiveOldMossbackGainsSixPointsInEach()
        {
            // The track is authored as deltas: +6/+6 grows the printed 35/40 body by 16 percent.
            Scenario       scenario = Fresh();
            CardInstanceId mossback = scenario.Seat(0).Board("OldMossback", rank: 5);
            MatchEngine    engine   = scenario.Build();

            Assert.That(engine.Critter(mossback).Attack, Is.EqualTo(41));
            Assert.That(engine.Critter(mossback).MaxHealth, Is.EqualTo(46));
        }

        [Test]
        public void RankThreeUndertowCostsThree()
        {
            Scenario       scenario = Fresh();
            CardInstanceId undertow = scenario.Seat(0).Hand("Undertow", rank: 3);
            CardInstanceId victim   = scenario.Seat(0).Board("MeadowMouse");
            MatchEngine    engine   = scenario.Build();

            int before = engine.Rules.Seat(0).Mana;
            engine.Play(0, undertow, EffectTargetRef.OnCritter(victim));

            Assert.That(before - engine.Rules.Seat(0).Mana, Is.EqualTo(3), "a trick that gets cheaper rather than bigger");
        }

        [Test]
        public void RankThreeWarmBiscuitRestoresTwentyTwo()
        {
            // A track that scales an amount rather than a body: the delta lands on the literal base of the
            // card's first Hello step, so a heal of 20 becomes 22.
            Scenario       scenario = Fresh();
            CardInstanceId biscuit  = scenario.Seat(0).Hand("WarmBiscuit", rank: 3);
            scenario.Seat(0).Den(90);
            MatchEngine engine = scenario.Build();

            engine.Play(0, biscuit, EffectTargetRef.Den(0));

            Assert.That(engine.Rules.Seat(0).DenHp, Is.EqualTo(112));
        }

        [Test]
        public void PlayedCardEventCarriesItsRank()
        {

            Scenario       scenario = Fresh();
            CardInstanceId kit      = scenario.Seat(0).Hand("EmberKit", rank: 4);
            MatchEngine    engine   = scenario.Build();

            engine.Play(0, kit, EffectTargetRef.None);

            Assert.That(engine.LastEventOf<CardPlayedEvent>().Rank, Is.EqualTo(4),
                "the numbers are public the moment the card is played");
        }

        [Test]
        public void MaxHealthBuffDoesNotRestoreHealth()
        {
            // Case N: a +10/+10 buff on a 15/20 carrying 15 damage becomes a 25/30 still carrying 15, so
            // its current health is 15 rather than the 5 it had.
            Scenario       scenario = Fresh();
            CardInstanceId hedgehog = scenario.Seat(0).Board("PricklyHedgehog", damage: 15); // 15/20
            CardInstanceId honey    = scenario.Seat(0).Hand("HoneyFeast");                   // +10/+10
            MatchEngine    engine   = scenario.Build();

            engine.Play(0, honey, EffectTargetRef.OnCritter(hedgehog));

            BoardCritter critter = engine.Critter(hedgehog);
            Assert.That(critter.MaxHealth, Is.EqualTo(30));
            Assert.That(critter.Damage, Is.EqualTo(15), "damage is stored separately, so buffing the maximum does not heal");
            Assert.That(critter.CurrentHealth, Is.EqualTo(15));
        }

        [Test]
        public void NoRuleReadsRankAfterInstanceCreation()
        {
            // A reflection guard: what is in play is a BoardCritter, and it has no rank to read.
            foreach (PropertyInfo property in typeof(BoardCritter).GetProperties())
                Assert.That(property.Name, Is.Not.EqualTo("Rank"), "a critter in play is numbers, not a rank");

            foreach (MethodInfo method in typeof(Legality).GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
                Assert.That(method.Name, Does.Not.Contain("Rank"), $"{method.Name} looks like a rank rule");
        }
    }
}
