using System.Collections.Generic;

namespace Game.Logic
{
    /// <summary> One card in a deck as the host resolved it: the catalogue card and the rank its owner holds it at. </summary>
    public readonly struct MatchDeckCard
    {
        public readonly CardId Card;
        public readonly int    Rank;

        public MatchDeckCard(CardId card, int rank)
        {
            Card = card;
            Rank = rank;
        }

        public override string ToString() => $"{Card} r{Rank}";
    }

    /// <summary>
    /// The whole of what the host decides before a game exists: the seed, the content, the pacing parameters,
    /// and the two decks already resolved against their owners' collections. Not serialized — a match is
    /// restored from its <see cref="MatchModel"/>, not from its setup.
    /// <para>
    /// <b>The seed is part of the secret.</b> It must come from a cryptographic source
    /// (<c>RandomNumberGenerator</c>) at the host: a seed a client can bracket hands over both hands and both
    /// deck orders while the transmission invariant reads as perfectly satisfied
    /// (<c>Docs/hidden-information.md</c>). <c>RandomPCG.CreateNew()</c> is not that source.
    /// </para>
    /// </summary>
    public sealed class MatchSetup
    {
        public readonly ulong                        Seed;
        public readonly SharedGameConfig             Config;
        public readonly MatchTimings                 Timings;
        /// <summary> Seat 0's deck in <em>authored</em> order. Instance identities are minted over this, then it is shuffled. </summary>
        public readonly IReadOnlyList<MatchDeckCard> Seat0Deck;
        public readonly IReadOnlyList<MatchDeckCard> Seat1Deck;

        public MatchSetup(ulong seed, SharedGameConfig config, MatchTimings timings, IReadOnlyList<MatchDeckCard> seat0Deck, IReadOnlyList<MatchDeckCard> seat1Deck)
        {
            Seed      = seed;
            Config    = config;
            Timings   = timings;
            Seat0Deck = seat0Deck;
            Seat1Deck = seat1Deck;
        }

        public IReadOnlyList<MatchDeckCard> Deck(int seat) => seat == 0 ? Seat0Deck : Seat1Deck;
    }
}
