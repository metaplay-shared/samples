using System;

namespace Game.Logic.Tests
{
    /// <summary>
    /// How big a self-play run is and where its seeds come from.
    /// <para>
    /// The master seed is a checked-in literal and never the clock. That is a reproducibility choice rather
    /// than the deal seed's cryptographic-randomness requirement
    /// (<c>Docs/hidden-information.md</c>): a test needs to fail the same way twice, not to keep a
    /// secret.
    /// </para>
    /// <para>
    /// The count scales with the same switch the engine's determinism suites use, so there is one thing to set
    /// when a change needs the long check and no suite that quietly sits it out.
    /// </para>
    /// </summary>
    public static class SelfPlayRun
    {
        /// <summary> The one seed everything else is derived from. Changing it re-rolls every game in the suite. </summary>
        public const ulong MasterSeed = 0x5CA1AB1E5EEDF00Dul;

        public static bool IsDeep => Environment.GetEnvironmentVariable("STICKYPAWS_DETERMINISM_DEEP") == "1";

        /// <summary> The standard count on an ordinary run, the deep one when the switch is set. </summary>
        public static int Games(int standard, int deep) => IsDeep ? deep : standard;

        /// <summary>
        /// The seed for game <paramref name="index"/> of one stream. It is a function of the index alone, so
        /// game 137 gets the same seed however many games ran before it — which is what makes the first games
        /// of a deep run exactly the standard run's games plus more, rather than a different sample.
        /// <para>
        /// The stream number keeps two fixtures from playing the same games: a suite that only ever saw one
        /// sample of the deal would be a hundred games' worth of wall clock buying one game's worth of
        /// coverage.
        /// </para>
        /// </summary>
        public static ulong GameSeed(int stream, int index)
        {
            ulong key = Mix(MasterSeed ^ ((ulong)(uint)stream * 0x9E3779B97F4A7C15ul));
            return Mix(key + (ulong)(uint)index);
        }

        static ulong Mix(ulong value)
        {
            ulong z = value + 0x9E3779B97F4A7C15ul;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9ul;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBul;
            return z ^ (z >> 31);
        }
    }

    /// <summary>
    /// One stream of games per fixture, so no two suites replay the same sample. The numbers are addresses,
    /// not an ordering — renumbering one re-rolls that suite's games and nothing else.
    /// </summary>
    public static class SelfPlayStreams
    {
        public const int StateSanity           = 1;
        public const int Conservation          = 2;
        public const int Legality              = 3;
        public const int DecisionDeterminism   = 9;
        public const int Termination           = 4;
        public const int Indistinguishability  = 5;
        public const int NegativeControls      = 6;
        public const int Profiles              = 7;
        public const int WiringSmokeTest       = 8;
        // 10 RETIRED: ViewLegality. There is one legality walk, so there is no second one to compare it to.
        // 11 RETIRED: TargetSuggestion. Attacks pick their target by hand, so there is no suggestion to sweep for.
        public const int StarterDeckBalance   = 12;
    }

    /// <summary>
    /// Which of the invariant catalog's four categories a run asserts. They are separable so a failure names
    /// the category that caught it, not "self-play broke".
    /// </summary>
    [Flags]
    public enum SelfPlayChecks
    {
        None         = 0,
        StateSanity  = 1 << 0,
        Conservation = 1 << 1,
        Legality     = 1 << 2,
        Termination  = 1 << 3,
        All          = StateSanity | Conservation | Legality | Termination,
    }
}
