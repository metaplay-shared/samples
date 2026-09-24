using Metaplay.Core;
using System;
using System.Collections.Generic;

namespace Game.Logic
{
    /// <summary>
    /// The reserved names that bot seats are given, read from the <c>BotNames</c> game config library.
    /// <para>
    /// A table draws bot names without replacement, so no table seats two bots with the same name. A player cannot
    /// take a roster name, so a human cannot pose as a bot (<c>docs/player.md</c>, "Name rules").
    /// <see cref="FromNames"/> normalizes every name once because the config build calls <see cref="IsReserved"/> for
    /// every name the player name generator can produce.
    /// </para>
    /// </summary>
    public sealed class BotNameRoster
    {
        /// <summary>
        /// A roster with no names, for a caller that cannot read game config. It reserves no name, so
        /// <see cref="IsReserved"/> on it always returns false. Do not use it where game config is available.
        /// </summary>
        public static readonly BotNameRoster Empty = new BotNameRoster(new List<string>());

        readonly List<string> _names;

        /// <summary>The names in <see cref="DisplayNamePolicy.ToComparisonKey"/> form.</summary>
        readonly HashSet<string> _comparisonKeys;

        BotNameRoster(List<string> names)
        {
            _names          = names;
            _comparisonKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (string name in names)
                _comparisonKeys.Add(DisplayNamePolicy.ToComparisonKey(name));
        }

        /// <summary>
        /// Build a roster from config names, skipping null and empty ones. The order must be stable because
        /// <see cref="Draw"/> shuffles it with a seed. A config archive keeps a library's rows in authored order.
        /// </summary>
        public static BotNameRoster FromNames(IEnumerable<string> names)
        {
            List<string> collected = new List<string>();
            if (names != null)
            {
                foreach (string name in names)
                {
                    if (!string.IsNullOrEmpty(name))
                        collected.Add(name);
                }
            }
            return collected.Count == 0 ? Empty : new BotNameRoster(collected);
        }

        /// <summary>How many names the roster holds.</summary>
        public int Count => _names.Count;

        /// <summary>
        /// Draw <paramref name="count"/> distinct names for one table by shuffling a copy of the roster.
        /// Throws <see cref="ArgumentOutOfRangeException"/> when <paramref name="count"/> is negative or larger
        /// than <see cref="Count"/>.
        /// </summary>
        /// <param name="seed">The table's seed. The same seed and roster always draw the same names.</param>
        public List<string> Draw(ulong seed, int count)
        {
            if (count < 0 || count > _names.Count)
                throw new ArgumentOutOfRangeException(nameof(count), count, $"A draw takes between 0 and {_names.Count} names");

            string[] pool = _names.ToArray();
            RandomPCG.CreateFromSeed(seed).ShuffleInPlace(pool);

            List<string> drawn = new List<string>(count);
            for (int index = 0; index < count; index++)
                drawn.Add(pool[index]);
            return drawn;
        }

        /// <summary>
        /// Whether a proposed display name matches a name on the roster. Both sides are compared in
        /// <see cref="DisplayNamePolicy.ToComparisonKey"/> form, which removes case and permitted punctuation, so
        /// "COG WHEEL", "Cog-wheel" and "cogwheel" are the same name. A name that normalizes to empty is never reserved.
        /// </summary>
        public bool IsReserved(string name) => IsReservedComparisonKey(DisplayNamePolicy.ToComparisonKey(name));

        /// <summary>
        /// <see cref="IsReserved"/> for a name already in <see cref="DisplayNamePolicy.ToComparisonKey"/> form, for a
        /// caller that also needs the key.
        /// </summary>
        public bool IsReservedComparisonKey(string comparisonKey) =>
            comparisonKey.Length > 0 && _comparisonKeys.Contains(comparisonKey);
    }
}
