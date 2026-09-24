using Metaplay.Core;
using System.Collections.Generic;
using static System.FormattableString;

namespace Game.Logic
{
    /// <summary>
    /// A generated name, the vocabulary version it came from, and whether the fallback was used. The
    /// identity-initialized analytics event carries all three.
    /// </summary>
    public readonly struct GeneratedDisplayName
    {
        /// <summary>The name to store on the model. Always a name <see cref="DisplayNamePolicy"/> accepts.</summary>
        public string Name { get; }

        /// <summary>The <see cref="PlayerIdentityConfig.GeneratorVersion"/> of the vocabulary used, or zero when no vocabulary was usable.</summary>
        public int GeneratorVersion { get; }

        /// <summary>Whether <see cref="Name"/> is the fallback name because the vocabulary could not produce a valid name.</summary>
        public bool UsedFallback { get; }

        public GeneratedDisplayName(string name, int generatorVersion, bool usedFallback)
        {
            Name             = name;
            GeneratorVersion = generatorVersion;
            UsedFallback     = usedFallback;
        }

        public override string ToString() => UsedFallback ? $"{Name} (fallback)" : $"{Name} (v{GeneratorVersion})";
    }

    /// <summary>
    /// Generates the name a new player gets at creation, so the game needs no naming step (<c>docs/player.md</c>,
    /// "Generated names"). The result depends only on the player's entity ID and the vocabulary's generator version,
    /// so it can run in the player model's initializer, and the identity-initialized event recomputes it at first
    /// login instead of storing it in the model. Names are not unique. The SDK does not require it, and the game has
    /// no search or friend list that would.
    /// </summary>
    public static class DisplayNameGenerator
    {
        /// <summary>
        /// The prefix of a fallback name, which is followed by a space and a number. Generated names never
        /// contain a space, so a fallback name cannot equal a generated one.
        /// </summary>
        public const string FallbackPrefix = "Guest";

        /// <summary>The number of distinct fallback names. The suffix is written with four digits.</summary>
        public const int FallbackSuffixCount = 10_000;

        /// <summary>
        /// The minimum number of distinct names the published vocabulary must produce, checked by the config
        /// build. It keeps a player unlikely to meet another player with the same name at a table.
        /// <para>
        /// This is a product requirement, not a safety margin. Lowering it should be a deliberate decision.
        /// </para>
        /// </summary>
        public const long MinCombinationCount = 230_400;

        /// <summary>
        /// The fallback name for <paramref name="playerId"/>, used when the vocabulary cannot produce a valid name.
        /// <para>
        /// The server actor passes it to the SDK as the new model's name, and the model falls back to it if the
        /// vocabulary is unusable. Both use this one function, so they give the same name for the same player.
        /// </para>
        /// </summary>
        public static string FallbackName(EntityId playerId) =>
            Invariant($"{FallbackPrefix} {SeededRandom(playerId, generatorVersion: 0).NextInt(FallbackSuffixCount):D4}");

        /// <summary>
        /// Generates the player's name: an adjective, a noun and a numeric suffix from <paramref name="config"/>, or
        /// <paramref name="fallback"/> if that fails. The name is checked with <see cref="DisplayNamePolicy.Validate"/>,
        /// which can fail only for an archive the config build did not validate.
        /// </summary>
        /// <param name="config">The published config, or null. Generated names never match reserved bot names.</param>
        /// <param name="playerId">The player's entity ID. Together with the generator version, it determines the name.</param>
        /// <param name="fallback">
        /// Normally <see cref="FallbackName"/>, which is also used when this value is invalid.
        /// </param>
        public static GeneratedDisplayName Generate(SharedGameConfig config, EntityId playerId, string fallback)
        {
            PlayerIdentityConfig vocabulary       = config?.PlayerIdentity;
            BotNameRoster        reservedBotNames = BotConfig.ReservedNames(config);

            // Only the two fallback returns need it, and a valid vocabulary never takes them.
            string SafeFallback() =>
                DisplayNamePolicy.Validate(fallback, reservedBotNames) == DisplayNameRefusal.None
                    ? DisplayNamePolicy.ToStoredForm(fallback)
                    : FallbackName(playerId);

            if (vocabulary == null || !vocabulary.CanGenerate)
                return new GeneratedDisplayName(SafeFallback(), generatorVersion: 0, usedFallback: true);

            RandomPCG random    = SeededRandom(playerId, vocabulary.GeneratorVersion);
            string    adjective = random.Choice(vocabulary.Adjectives);
            string    noun      = random.Choice(vocabulary.Nouns);
            int       suffix    = random.NextInt(vocabulary.SuffixCount);

            string name = adjective + noun + Suffix(suffix);

            if (DisplayNamePolicy.Validate(name, reservedBotNames) != DisplayNameRefusal.None)
                return new GeneratedDisplayName(SafeFallback(), vocabulary.GeneratorVersion, usedFallback: true);

            return new GeneratedDisplayName(name, vocabulary.GeneratorVersion, usedFallback: false);
        }

        /// <summary>
        /// Every name the vocabulary can produce, in a stable order. The config build validates each one
        /// (<c>docs/game-config.md</c>).
        /// </summary>
        public static IEnumerable<string> AllCombinations(PlayerIdentityConfig config)
        {
            if (config == null || !config.CanGenerate)
                yield break;

            // Enumerate every combination rather than a sample. A sample would be sufficient only if the suffix
            // length and the reserved bot names followed rules that nothing enforces, for example that no bot
            // name ends in digits.
            foreach (string adjective in config.Adjectives)
            {
                foreach (string noun in config.Nouns)
                {
                    string stem = adjective + noun;
                    for (int suffix = 0; suffix < config.SuffixCount; suffix++)
                        yield return stem + Suffix(suffix);
                }
            }
        }

        /// <summary>
        /// The two-digit zero-padded suffix, read from a precomputed table. Formatting the suffix for every
        /// combination would be most of the cost of <see cref="AllCombinations"/>.
        /// </summary>
        static string Suffix(int value) =>
            value >= 0 && value < TwoDigits.Length ? TwoDigits[value] : Invariant($"{value:D2}");

        static readonly string[] TwoDigits = BuildTwoDigits();

        static string[] BuildTwoDigits()
        {
            string[] table = new string[100];
            for (int value = 0; value < table.Length; value++)
                table[value] = Invariant($"{value:D2}");
            return table;
        }

        /// <summary>
        /// The generator's random source, seeded from the player's entity ID and the vocabulary's generator
        /// version. A new version therefore changes which name a given ID maps to.
        /// </summary>
        static RandomPCG SeededRandom(EntityId playerId, int generatorVersion)
        {
            // Multiplying by the 64-bit golden-ratio constant spreads a small version number across all bits of
            // the seed, rather than changing only the low bits.
            const ulong VersionMix = 0x9E3779B97F4A7C15UL;
            return SeedStreams.Stream(playerId.Value, generatorVersion, VersionMix);
        }
    }
}
