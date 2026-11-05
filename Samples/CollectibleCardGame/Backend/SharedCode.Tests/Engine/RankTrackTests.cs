using NUnit.Framework;

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

        /// <summary>
        /// A card's rank lands on the critter it becomes. The deltas are cumulative: Ember Kit's track gives
        /// +0/+1 and then +1/+0, so the printed 10/5 becomes an 11/6 at the top; Old Mossback's +6/+6 grows the
        /// printed 35/40 body by sixteen percent.
        /// </summary>
        [TestCase("EmberKit", 1, 10, 5)]
        [TestCase("EmberKit", 5, 11, 6)]
        [TestCase("OldMossback", 5, 41, 46)]
        public void APlayedCritterCarriesItsRanksStats(string card, int rank, int attack, int health)
        {
            Scenario       scenario = Fresh();
            CardInstanceId played   = scenario.Seat(0).Hand(card, rank: rank);
            MatchEngine    engine   = scenario.Build();

            engine.Play(0, played);

            Assert.That(engine.Critter(played).Attack, Is.EqualTo(attack));
            Assert.That(engine.Critter(played).MaxHealth, Is.EqualTo(health));
            Assert.That(Config.Cards[CardId.FromString(card)].HasRankGrowth(rank), Is.EqualTo(rank > Config.Global.RankMin));
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
    }
}
