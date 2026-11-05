using Metaplay.Core;
using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;

namespace Game.Logic.Tests
{
    /// <summary>
    /// The shipped content, as it comes out of the checked-in archive: rank growth stays proportional, and the
    /// catalogue exercises the engine's whole effect vocabulary.
    /// </summary>
    [TestFixture]
    public class GameConfigContentTests
    {
        static SharedGameConfig Config => TestGameConfig.Shared;

        #region The catalogue

        [Test]
        public void CritterGrowthIsProportionalAndPassiveCardsStayPassive()
        {
            foreach ((CardId id, CardInfo card) in Config.Cards)
            {
                if (card.Type != CardType.Critter || !card.Collectible || card.RankTrack.Ref.IsFlat)
                    continue;
                CardStats grown = card.GetStatsAtRank(Config.Global.RankMax);
                double target = (card.Attack + card.Health) * 0.16;
                int actual = grown.Attack + grown.Health - card.Attack - card.Health;
                Assert.That(System.Math.Abs(actual - target), Is.LessThanOrEqualTo(0.50001), id.Value);
                if (actual >= 4)
                    for (int rank = 2; rank <= 5; rank++)
                        Assert.That(card.RankTrack.Ref.GetStep(rank).IsEmpty, Is.False, $"{id} rank {rank}");
                if (card.Attack == 0)
                    Assert.That(grown.Attack, Is.Zero, id.Value);
            }
        }

        [Test]
        public void Catalogue_ExercisesEveryPrimitiveAndTrigger()
        {
            // The starter slice exists to cover the vocabulary, so a missing verb here means the content no
            // longer proves the engine's whole surface is reachable from config.
            HashSet<EffectOp> ops = Config.EffectSteps.Values.Select(step => step.Op).ToHashSet();
            Assert.That(ops, Is.EquivalentTo(EnumUtil.GetValues<EffectOp>()));

            foreach (CardTrigger trigger in EffectVocabulary.AllCardTriggers)
                Assert.That(Config.Cards.Values.Any(card => card.BindsTrigger(trigger)), Is.True, $"no card binds {trigger}");

            HashSet<WeatherTrigger> weatherTriggers = Config.Weathers.Values.Select(weather => weather.Trigger).ToHashSet();
            Assert.That(weatherTriggers, Is.EquivalentTo(EnumUtil.GetValues<WeatherTrigger>()));
            Assert.That(Config.Weathers.Values.Any(weather => weather.HasAura), Is.True);
            Assert.That(Config.Weathers.Values.Any(weather => weather.HasCostRule), Is.True);
        }

        #endregion
    }
}
