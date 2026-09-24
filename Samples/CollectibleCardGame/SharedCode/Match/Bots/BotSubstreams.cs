using Metaplay.Core;

namespace Game.Logic
{
    /// <summary>
    /// Where a profile's seeded imperfection comes from. Every decision that may depart from the top-ranked
    /// action draws from <em>its own</em> stream, derived as a stable function of the decision's coordinates:
    /// the match seed, the seat, the kind of decision, and the action count it was taken at.
    /// <para>
    /// A per-decision substream rather than one running generator is what makes a mistake reproducible in
    /// isolation. With one generator, an unrelated effect adding a card to the legal set would shift every
    /// later draw and the "same game, same mistakes" property would hold only for games nothing else touched.
    /// </para>
    /// <para>
    /// This is the <b>only</b> place the policy consumes randomness. Tie-breaks are seedless by construction
    /// (they take the rules' own canonical enumeration order), so a profile that makes no mistakes never
    /// constructs a stream at all.
    /// </para>
    /// <para>
    /// <b>The derivation is deliberately lossy</b>, as defence in depth: every other input to the key is
    /// public, and folding the mixed key in half leaves each key about four billion preimages, so a recovered
    /// key does not recover the seed.
    /// </para>
    /// </summary>
    public static class BotSubstreams
    {
        /// <summary>
        /// Domain separation, so a bot substream and any other consumer of the same seed cannot collide even
        /// before the fold.
        /// </summary>
        const ulong BotDomain = 0xB0757EED5EED5EEDul;

        /// <summary> The stream for one decision. The same coordinates always give the same stream. </summary>
        public static RandomPCG For(ulong matchSeed, int seat, BotDecisionKind kind, int actionCount)
        {
            ulong key = Mix(matchSeed ^ BotDomain);
            key = Mix(key ^ (ulong)(uint)seat);
            key = Mix(key ^ (ulong)(uint)(int)kind);
            key = Mix(key ^ (ulong)(uint)actionCount);

            return RandomPCG.CreateFromSeed(Fold(key));
        }

        /// <summary>
        /// Throw half the key away. The mix on its own is a bijection over 64 bits, so it hides nothing from
        /// anyone who can run it backwards: with the seat, the kind and the action count all public, one key would
        /// name one seed. Folding is what makes the step one-way, and a bot's mistake rolls have no use for
        /// the entropy it costs.
        /// </summary>
        static ulong Fold(ulong key) => (key ^ (key >> 32)) & 0xFFFFFFFFul;

        /// <summary>
        /// The splitmix64 finalizer. A cheap avalanche is all it has to be — two neighbouring keys give
        /// unrelated streams — and it is <see cref="Fold"/> rather than this that keeps the seed in.
        /// </summary>
        static ulong Mix(ulong value)
        {
            ulong z = value + 0x9E3779B97F4A7C15ul;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9ul;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBul;
            return z ^ (z >> 31);
        }
    }
}
