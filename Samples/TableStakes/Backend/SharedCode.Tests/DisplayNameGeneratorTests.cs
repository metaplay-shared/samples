using Metaplay.Core;
using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;

namespace Game.Logic.Tests
{
    /// <summary>
    /// Tests <see cref="DisplayNameGenerator"/>, which gives a new player a display name. The name depends only on
    /// the player id and the config, so the identity event can be rebuilt at first login. Every name the generator
    /// can produce passes <see cref="DisplayNamePolicy.Validate"/>, so a player is never given a name they could
    /// not type themselves.
    /// </summary>
    [TestFixture]
    public class DisplayNameGeneratorTests
    {
        static EntityId Player(int index) => EntityId.Create(EntityKindCore.Player, (ulong)index);

        /// <summary>Builds a vocabulary of <paramref name="words"/> generated adjectives and nouns.</summary>
        static PlayerIdentityConfig Vocabulary(int words = 48, int suffixCount = 100, int version = 1)
        {
            List<string> adjectives = new List<string>();
            List<string> nouns      = new List<string>();
            for (int index = 0; index < words; index++)
            {
                string tail = $"{(char)('a' + index / 26)}{(char)('a' + index % 26)}";
                adjectives.Add("Adj" + tail);
                nouns.Add("Nou" + tail);
            }
            return new PlayerIdentityConfig(adjectives, nouns, suffixCount, version);
        }

        /// <summary>
        /// Builds a test config with <paramref name="vocabulary"/>. The generator reads both the vocabulary and the
        /// bot names it must not give a player, so it takes a whole config.
        /// </summary>
        static SharedGameConfig ConfigWith(PlayerIdentityConfig vocabulary) => TestGameConfig.Build(identity: vocabulary);

        #region Determinism

        [Test]
        public void TheSamePlayerIsNamedTheSameWayEveryTime()
        {
            // Determinism lets the first login rebuild the identity-initialized event instead of storing the
            // generated name in an extra model member.
            SharedGameConfig config = ConfigWith(Vocabulary());

            for (int index = 1; index <= 50; index++)
            {
                GeneratedDisplayName first  = DisplayNameGenerator.Generate(config, Player(index), "Guest 0001");
                GeneratedDisplayName second = DisplayNameGenerator.Generate(config, Player(index), "Guest 0001");

                Assert.That(second.Name, Is.EqualTo(first.Name));
                Assert.That(second.UsedFallback, Is.EqualTo(first.UsedFallback));
                Assert.That(second.GeneratorVersion, Is.EqualTo(first.GeneratorVersion));
            }
        }

        [Test]
        public void DifferentPlayersGetDifferentNames()
        {
            // Some collisions are expected. The test catches a generator that ignores the player id in its seed.
            SharedGameConfig config = ConfigWith(Vocabulary());

            HashSet<string> names = new HashSet<string>();
            for (int index = 1; index <= 200; index++)
                names.Add(DisplayNameGenerator.Generate(config, Player(index), "Guest 0001").Name);

            Assert.That(names, Has.Count.GreaterThan(190), "200 consecutive player ids produced too few distinct names");
        }

        [Test]
        public void RepublishingTheVocabularyUnderANewVersionReshufflesTheMapping()
        {
            // The generator version is part of the seed, so publishing a new version assigns new names instead of
            // keeping each player on the same word indexes.
            List<string> underOne = new List<string>();
            List<string> underTwo = new List<string>();
            for (int index = 1; index <= 40; index++)
            {
                underOne.Add(DisplayNameGenerator.Generate(ConfigWith(Vocabulary(version: 1)), Player(index), "Guest 0001").Name);
                underTwo.Add(DisplayNameGenerator.Generate(ConfigWith(Vocabulary(version: 2)), Player(index), "Guest 0001").Name);
            }

            Assert.That(underTwo, Is.Not.EqualTo(underOne));
        }

        [Test]
        public void TheFallbackIsAFunctionOfThePlayerToo()
        {
            // The player actor and the player model both compute the fallback name, at different times, so both
            // must get the same name.
            for (int index = 1; index <= 50; index++)
            {
                string once = DisplayNameGenerator.FallbackName(Player(index));

                Assert.That(DisplayNameGenerator.FallbackName(Player(index)), Is.EqualTo(once));
                Assert.That(DisplayNamePolicy.Validate(once, TestBotConfig.Roster), Is.EqualTo(DisplayNameRefusal.None));
            }
        }

        #endregion

        #region Shape

        [Test]
        public void AGeneratedNameIsAdjectiveNounAndTwoDigits()
        {
            SharedGameConfig config = ConfigWith(Vocabulary());

            for (int index = 1; index <= 100; index++)
            {
                string name = DisplayNameGenerator.Generate(config, Player(index), "Guest 0001").Name;

                Assert.That(name, Does.Match("^Adj[a-z]{2}Nou[a-z]{2}[0-9]{2}$"), $"'{name}' is not adjective + noun + two digits");
            }
        }

        [Test]
        public void EveryCombinationOfAVocabularyIsANameTheServerWouldAccept()
        {
            // The config build runs the same check. Server.Tests runs it over the shipped vocabulary.
            SharedGameConfig config = ConfigWith(Vocabulary());

            // Collect the refused names and assert once, because an assertion per combination is slow.
            List<string> refused = DisplayNameGenerator.AllCombinations(config.PlayerIdentity)
                .Where(candidate => DisplayNamePolicy.Validate(candidate, TestBotConfig.Roster) != DisplayNameRefusal.None)
                .ToList();
            Assert.That(refused, Is.Empty, "these generated names would be refused from a player");

            // AllCombinations yields every combination, not a sample.
            Assert.That(DisplayNameGenerator.AllCombinations(config.PlayerIdentity).Count(), Is.EqualTo(48 * 48 * 100));
        }

        #endregion

        #region The fallback

        [Test]
        public void NoVocabularyMeansTheFallback()
        {
            GeneratedDisplayName generated = DisplayNameGenerator.Generate(null, Player(7), "Guest 0042");

            Assert.That(generated.Name, Is.EqualTo("Guest 0042"));
            Assert.That(generated.UsedFallback, Is.True);
            Assert.That(generated.GeneratorVersion, Is.Zero);
        }

        [Test]
        public void AnEmptyVocabularyMeansTheFallback()
        {
            GeneratedDisplayName generated = DisplayNameGenerator.Generate(ConfigWith(new PlayerIdentityConfig()), Player(7), "Guest 0042");

            Assert.That(generated.UsedFallback, Is.True);
        }

        [Test]
        public void AVocabularyThatWouldProduceARefusedNameFallsBack()
        {
            // The config build refuses such a vocabulary. The generator checks each name anyway, so a config that
            // bypassed the build still cannot give a player an invalid name.
            PlayerIdentityConfig tooLong = new PlayerIdentityConfig(
                new List<string> { new string('A', 10) },
                new List<string> { new string('b', 10) },
                suffixCount: 1,
                generatorVersion: 3);

            GeneratedDisplayName generated = DisplayNameGenerator.Generate(ConfigWith(tooLong), Player(7), "Guest 0042");

            Assert.That(generated.Name, Is.EqualTo("Guest 0042"));
            Assert.That(generated.UsedFallback, Is.True);
            Assert.That(generated.GeneratorVersion, Is.EqualTo(3), "the version still says which vocabulary failed");
        }

        [Test]
        public void AnUnusableFallbackIsReplacedRatherThanStored()
        {
            // The player actor always passes a valid fallback. The generator still validates it, and replaces an
            // invalid one with FallbackName.
            GeneratedDisplayName generated = DisplayNameGenerator.Generate(null, Player(7), "<script>");

            Assert.That(DisplayNamePolicy.Validate(generated.Name, TestBotConfig.Roster), Is.EqualTo(DisplayNameRefusal.None));
            Assert.That(generated.Name, Does.StartWith(DisplayNameGenerator.FallbackPrefix));
        }

        [Test]
        public void AllCombinationsOfNothingIsNothing()
        {
            Assert.That(DisplayNameGenerator.AllCombinations(null), Is.Empty);
            Assert.That(DisplayNameGenerator.AllCombinations(new PlayerIdentityConfig()), Is.Empty);
        }

        #endregion
    }
}
