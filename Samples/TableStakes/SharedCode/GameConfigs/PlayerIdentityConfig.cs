using Metaplay.Core.Config;
using Metaplay.Core.Model;
using System.Collections.Generic;

namespace Game.Logic
{
    /// <summary>
    /// The vocabulary for generated player names: two word lists, a numeric suffix range, and a version number
    /// (<c>docs/player.md</c>, "Generated names").
    /// <para>
    /// The name rules are in <see cref="DisplayNamePolicy"/> in code, so a config publish cannot change them.
    /// Changing the lists does not rename existing players, because a name is generated once, at player creation,
    /// and stored in the player model.
    /// </para>
    /// </summary>
    [MetaSerializable]
    public class PlayerIdentityConfig : GameConfigKeyValue<PlayerIdentityConfig>
    {
        /// <summary>The words for the first part of a generated name.</summary>
        [MetaMember(1)] public List<string> Adjectives { get; private set; } = new List<string>();

        /// <summary>The words for the second part of a generated name.</summary>
        [MetaMember(2)] public List<string> Nouns { get; private set; } = new List<string>();

        /// <summary>
        /// The number of values the numeric suffix can take, counting from zero. The suffix is written with two
        /// digits.
        /// </summary>
        [MetaMember(3)] public int SuffixCount { get; private set; }

        /// <summary>
        /// The version of these word lists. The identity-initialized analytics event records it, so analytics can
        /// tell which vocabulary a player's name came from. It is also part of the generator's seed, so a new
        /// version changes which name a given player id maps to.
        /// </summary>
        [MetaMember(4)] public int GeneratorVersion { get; private set; }

        public PlayerIdentityConfig() { }

        public PlayerIdentityConfig(List<string> adjectives, List<string> nouns, int suffixCount, int generatorVersion)
        {
            Adjectives       = adjectives;
            Nouns            = nouns;
            SuffixCount      = suffixCount;
            GeneratorVersion = generatorVersion;
        }

        /// <summary>Whether both word lists are non-empty and <see cref="SuffixCount"/> is positive.</summary>
        public bool CanGenerate =>
            Adjectives != null && Adjectives.Count > 0 &&
            Nouns != null && Nouns.Count > 0 &&
            SuffixCount > 0;

        /// <summary>
        /// The number of distinct names this vocabulary can produce, or zero if <see cref="CanGenerate"/> is false.
        /// A <c>long</c> so the product cannot overflow.
        /// </summary>
        public long CombinationCount =>
            CanGenerate ? (long)Adjectives.Count * Nouns.Count * SuffixCount : 0;
    }
}
