using Metaplay.Core.Model;
using System.Collections.Generic;

namespace Game.Logic
{
    /// <summary>
    /// What a match pays. Fixed at formation from facts both accounts already had, and public from the
    /// pre-match screen onward, so nobody discovers after winning that the Heist pays less than they assumed
    /// (<c>Docs/match.md</c>, "The Heist phase").
    /// </summary>
    [MetaSerializable]
    public enum StakesTier
    {
        /// <summary> Two comparable decks. A rank moves. </summary>
        Even = 0,
        /// <summary> The stronger deck won: no ranks move, so there is no phase to enter. </summary>
        Favourite = 1,
        /// <summary> The weaker deck won: the upset pays double. </summary>
        Underdog = 2,
        /// <summary> No human opponent: rating moves, ranks never do. </summary>
        Practice = 3,
        /// <summary> Either player is inside the newcomer shield: no ranks move, in either direction. </summary>
        Shielded = 4,
    }

    /// <summary>
    /// The wager, frozen at formation. The matchmaker produces the same record from the queue; this
    /// type stays here so that is a different constructor rather than a parallel type.
    /// <para>
    /// The locked sets are frozen with the decks, which is the whole security property of locks: a player who
    /// is losing cannot reach into their collection at turn nine and padlock the card they are about to be
    /// robbed of.
    /// </para>
    /// </summary>
    [MetaSerializable]
    public class MatchStakes
    {
        [MetaMember(1)] public bool               IsRanked     { get; set; }
        [MetaMember(2)] public StakesTier         Tier         { get; set; }
        /// <summary> Per seat, the deck's Power Score as <c>DeckValidator.ComputePowerScore</c> read it. </summary>
        [MetaMember(3)] public List<int>          PowerScores  { get; set; }
        /// <summary> Per seat, the cards its owner had locked at enqueue. Open information, both sides. </summary>
        [MetaMember(4)] public List<List<CardId>> LockedCards  { get; set; }

        public MatchStakes() { }

        public MatchStakes(bool isRanked, StakesTier tier, List<int> powerScores, List<List<CardId>> lockedCards)
        {
            IsRanked     = isRanked;
            Tier         = tier;
            PowerScores  = powerScores;
            LockedCards  = lockedCards;
        }

        /// <summary> The practice stakes: no queue, no ranks, no Heist (<c>Docs/matchmaking.md</c>). </summary>
        public static MatchStakes Practice(List<int> powerScores, List<List<CardId>> lockedCards)
            => new MatchStakes(isRanked: false, StakesTier.Practice, powerScores, lockedCards);

        public IReadOnlyList<CardId> Locked(int seat) => LockedCards[seat];
    }
}
