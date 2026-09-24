using Metaplay.Core;

namespace Game.Logic
{
    /// <summary>
    /// Derives one independent seed or random stream per numbered item (a seat, a version) from a shared seed.
    /// <para>
    /// Each kind of draw uses its own odd <c>key</c> constant, so two draws for the same item are not correlated.
    /// The key is multiplied by <c>n</c>, so for <c>n</c> of zero every key gives the shared seed itself. Callers
    /// whose items are numbered from zero and need distinct draws add one. <see cref="BotProfiles"/> and
    /// <see cref="BotPolicy"/> mix their seeds differently, and changing them to this formula would change their
    /// draws.
    /// </para>
    /// </summary>
    public static class SeedStreams
    {
        /// <summary>The seed for item <paramref name="n"/> of the draw keyed <paramref name="key"/>.</summary>
        public static ulong Seed(ulong seed, int n, ulong key) => seed ^ ((ulong)(uint)n * key);

        /// <summary>A random stream seeded with <see cref="Seed"/>.</summary>
        public static RandomPCG Stream(ulong seed, int n, ulong key) => RandomPCG.CreateFromSeed(Seed(seed, n, key));
    }
}
