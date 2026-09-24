using NUnit.Framework;

namespace Game.Logic.Tests
{
    /// <summary>
    /// The two small string grammars the <c>EffectSteps</c> sheet authors amounts and filters in. These are
    /// parsed at config-build time, so a cell that does not parse must be reported rather than guessed at —
    /// these are the negative controls for that.
    /// </summary>
    [TestFixture]
    public class EffectGrammarTests
    {
        #region Amounts

        [TestCase("3", 3, EffectCounter.None, 0)]
        [TestCase("0", 0, EffectCounter.None, 0)]
        [TestCase("-1", -1, EffectCounter.None, 0)]
        [TestCase("Per:PlayedThisMatch", 0, EffectCounter.PlayedThisMatch, 1)]
        [TestCase("2+Per:FriendlyCritters", 2, EffectCounter.FriendlyCritters, 1)]
        [TestCase(" 1 + Per:EnemyCritters ", 1, EffectCounter.EnemyCritters, 1)]
        // A counter whose every unit is worth more than one, which is what the widened stat domain needs: the
        // bare form still means one per unit, so every cell authored before it keeps its meaning.
        [TestCase("5xPer:PlayedThisMatch", 0, EffectCounter.PlayedThisMatch, 5)]
        [TestCase("10xPer:FriendlyCritters", 0, EffectCounter.FriendlyCritters, 10)]
        [TestCase("3+5xPer:EnemyCritters", 3, EffectCounter.EnemyCritters, 5)]
        [TestCase(" 2 + 5x Per:FriendlyCritters ", 2, EffectCounter.FriendlyCritters, 5)]
        public void Amount_Parses(string text, int expectedLiteral, EffectCounter expectedCounter, int expectedPerUnit)
        {
            Assert.That(EffectAmount.TryParse(text, out EffectAmount amount, out string error), Is.True, error);
            Assert.That(amount.Literal, Is.EqualTo(expectedLiteral));
            Assert.That(amount.Counter, Is.EqualTo(expectedCounter));
            Assert.That(amount.PerUnit, Is.EqualTo(expectedPerUnit));
        }

        [TestCase("")]
        [TestCase("   ")]
        [TestCase("three")]
        [TestCase("Per:Vibes")]
        [TestCase("Per:None")]
        [TestCase("2+3")]
        [TestCase("2+Vibes")]
        [TestCase("Per:")]
        // The per-unit factor's own negative controls: it must be a positive whole number, and it means
        // nothing without a counter to scale.
        [TestCase("0xPer:FriendlyCritters")]
        [TestCase("-2xPer:FriendlyCritters")]
        [TestCase("xPer:FriendlyCritters")]
        [TestCase("5x")]
        [TestCase("5xFriendlyCritters")]
        public void Amount_RefusesNonsense(string text)
        {
            Assert.That(EffectAmount.TryParse(text, out EffectAmount _, out string error), Is.False, $"'{text}' was accepted");
            Assert.That(error, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void Amount_RoundTripsThroughItsOwnSyntax()
        {
            foreach (string text in new[]
                     {
                         "3", "-1", "Per:FriendlyCritters", "2+Per:PlayedThisMatch",
                         "5xPer:PlayedThisMatch", "3+5xPer:EnemyCritters",
                     })
            {
                Assert.That(EffectAmount.TryParse(text, out EffectAmount amount, out string error), Is.True, error);
                Assert.That(amount.ToString(), Is.EqualTo(text));
            }
        }

        /// <summary>
        /// The arithmetic every counted amount resolves through, over every step in the shipped catalogue
        /// that has a <c>Per:</c> term. There used to be two copies of this expression — the engine's and the
        /// bot's — with doc comments on both saying they must agree and nothing checking that they did; a
        /// reviewer dropped the per-unit factor from the bot's copy and all 525 tests passed, so a bot could
        /// have valued Nine-Tail Matriarch's Hello at one per Kitsune while the table resolved five. There is
        /// one copy now and this is what pins it.
        /// </summary>
        [Test]
        public void EveryCountedAmountInTheCatalogueIsItsLiteralPlusPerUnitTimesTheCount()
        {
            int counted = 0;

            foreach (EffectStepInfo step in TestGameConfig.Shared.EffectSteps.Values)
            {
                foreach (EffectAmount amount in new[] { step.Amount, step.Amount2 })
                {
                    if (amount == null || amount.IsLiteralOnly)
                        continue;

                    counted++;
                    Assert.That(amount.PerUnit, Is.GreaterThan(0), $"{step.StepId} counts {amount.Counter} at nothing per unit");

                    for (int count = 0; count <= 6; count++)
                    {
                        Assert.That(amount.ValueFrom(count), Is.EqualTo(amount.Literal + amount.PerUnit * count),
                            $"{step.StepId} at a count of {count}");

                        // And with a rank track's delta on the literal, which is the other term the engine
                        // and the bot both have to fold in the same place.
                        Assert.That(amount.ValueFrom(count, rankBonus: 2), Is.EqualTo(amount.Literal + 2 + amount.PerUnit * count),
                            $"{step.StepId} at a count of {count} with a rank bonus");
                    }
                }
            }

            // The pool has two scaling cards; a catalogue that had lost them would pass every assertion above
            // without asking the question.
            Assert.That(counted, Is.EqualTo(2), "the shipped catalogue's counted amounts are DenPerKitsune and HealDenPerFriendly");
        }

        #endregion

        #region Filters

        [Test]
        public void Filter_ParsesAClanClause()
        {
            Assert.That(EffectFilter.TryParse("Clan=Kitsune", out EffectFilter filter, out string error), Is.True, error);
            Assert.That(filter.Clan.KeyObject, Is.EqualTo(ClanId.FromString("Kitsune")));
            Assert.That(filter.Type, Is.EqualTo(CardTypeFilter.Any));
        }

        [Test]
        public void Filter_ParsesATypeClause()
        {
            Assert.That(EffectFilter.TryParse("Type=Trick", out EffectFilter filter, out string error), Is.True, error);
            Assert.That(filter.Clan, Is.Null);
            Assert.That(filter.Type, Is.EqualTo(CardTypeFilter.Trick));
        }

        [Test]
        public void Filter_ParsesBothClauses()
        {
            Assert.That(EffectFilter.TryParse("Clan=Moonlight;Type=Critter", out EffectFilter filter, out string error), Is.True, error);
            Assert.That(filter.Clan.KeyObject, Is.EqualTo(ClanId.FromString("Moonlight")));
            Assert.That(filter.Type, Is.EqualTo(CardTypeFilter.Critter));
        }

        [TestCase("")]
        [TestCase("Kitsune")]
        [TestCase("Colour=Red")]
        [TestCase("Type=Enchantment")]
        [TestCase("Type=Any")]
        [TestCase("Clan=Kitsune;Clan=Tidepool")]
        [TestCase("Type=Trick;Type=Critter")]
        [TestCase("Clan=Kitsune;")]
        [TestCase("Clan=")]
        public void Filter_RefusesNonsense(string text)
        {
            Assert.That(EffectFilter.TryParse(text, out EffectFilter _, out string error), Is.False, $"'{text}' was accepted");
            Assert.That(error, Is.Not.Null.And.Not.Empty);
        }

        #endregion
    }
}
