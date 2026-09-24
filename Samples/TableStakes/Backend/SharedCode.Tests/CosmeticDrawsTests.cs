using Metaplay.Core;
using NUnit.Framework;

namespace Game.Logic.Tests
{
    /// <summary>
    /// Tests <see cref="CosmeticDraws"/>, which picks the cosmetics a bot wears. Both the seasonal tournament's
    /// generated entrants and a table's bots use it, so the rate at which bots wear an item is the same in both.
    /// <para>
    /// This fixture tests the draw rates and the price bands. Each caller's seeding is tested separately:
    /// <see cref="MatchSeatIdentityTests"/> for a table and <c>CosmeticsTests</c> for a tournament division.
    /// </para>
    /// </summary>
    [TestFixture]
    public class CosmeticDrawsTests
    {
        /// <summary>
        /// The number of draws, large enough that the bounds below test the distribution rather than one seed.
        /// Each draw uses a fresh random stream, as both callers do (one stream per seat per slot).
        /// </summary>
        const int NumDraws = 4_000;

        static RandomPCG Stream(int index) => RandomPCG.CreateFromSeed(0x9E37_79B9_7F4A_7C15UL ^ (ulong)index);

        /// <summary>
        /// Checks the name-effect distribution: most bots have plain text, some wear a coin-priced effect, and
        /// fewer wear a gem-priced effect.
        /// </summary>
        [Test]
        public void MostBotsWearNoNameEffect_AndThePremiumBandIsTheRarest()
        {
            SharedGameConfig config = TestGameConfig.Build();

            int premium = 0;
            int basic   = 0;
            int plain   = 0;

            for (int index = 0; index < NumDraws; index++)
            {
                CosmeticId worn = CosmeticDraws.RolledNameEffect(config, Stream(index));
                if (worn == null)
                {
                    plain += 1;
                    continue;
                }

                if (config.Cosmetics[worn].Price.Currency == CurrencyType.Gems)
                    premium += 1;
                else
                    basic += 1;
            }

            Assert.Multiple(() =>
            {
                Assert.That(plain, Is.GreaterThan(80 * NumDraws / 100), "most bots are supposed to be in plain text");
                Assert.That(premium, Is.GreaterThan(0), $"no bot drew a premium effect over {NumDraws} rolls");
                Assert.That(basic, Is.GreaterThan(0), $"no bot drew a basic effect over {NumDraws} rolls");
                Assert.That(premium, Is.LessThan(basic), "the premium band is supposed to be the rarer of the two");

                // The upper bounds are loose around the expected shares from CosmeticDraws.RolledNameEffect, but
                // tight enough to catch a band drawn far too often.
                Assert.That(premium, Is.LessThanOrEqualTo(5 * NumDraws / 100), $"{premium} of {NumDraws} wear premium, above the 5% bound");
                Assert.That(basic, Is.LessThanOrEqualTo(15 * NumDraws / 100), $"{basic} of {NumDraws} wear basic, above the 15% bound");
            });
        }

        /// <summary>
        /// Checks that a bot only wears purchasable items of the requested kind, so a bot never wears a
        /// non-purchasable reward item such as the champion frame.
        /// </summary>
        [Test]
        public void EveryDrawnItemIsPurchasableAndInTheSlotItWasDrawnFor()
        {
            SharedGameConfig config = TestGameConfig.Build();

            for (int index = 0; index < NumDraws; index++)
            {
                CosmeticId avatar = CosmeticDraws.PurchasableAvatar(config, Stream(index));
                Assert.That(config.Cosmetics[avatar].Kind, Is.EqualTo(CosmeticKind.Avatar));
                Assert.That(config.Cosmetics[avatar].IsPurchasable, Is.True);

                CosmeticId effect = CosmeticDraws.RolledNameEffect(config, Stream(index));
                if (effect == null)
                    continue;

                Assert.That(config.Cosmetics[effect].Kind, Is.EqualTo(CosmeticKind.NameEffect));
                Assert.That(config.Cosmetics[effect].IsPurchasable, Is.True);
            }
        }

        /// <summary>
        /// Checks that an empty cosmetics catalogue yields null draws instead of throwing. The client draws its
        /// default for a slot with no cosmetic.
        /// </summary>
        [Test]
        public void AnEmptyCatalogueDressesNobodyAndDoesNotThrow()
        {
            SharedGameConfig empty = TestGameConfig.Build();
            TestGameConfig.SetEntry(empty, "Cosmetics", Metaplay.Core.Config.GameConfigLibrary<CosmeticId, CosmeticInfo>.CreateSolo(new System.Collections.Generic.List<CosmeticInfo>()));

            Assert.That(CosmeticDraws.PurchasableAvatar(empty, Stream(0)), Is.Null);
            Assert.That(CosmeticDraws.RolledNameEffect(empty, Stream(0)), Is.Null);

            // A null config also yields null draws.
            Assert.That(CosmeticDraws.PurchasableAvatar(null, Stream(0)), Is.Null);
            Assert.That(CosmeticDraws.RolledNameEffect(null, Stream(0)), Is.Null);
        }
    }
}
