using Metaplay.Core.Model;
using System.Collections.Generic;

namespace Game.Logic
{
    /// <summary>
    /// One deck the player has saved: a name and the card list, exactly as it was when the save was accepted.
    /// The list is stored as authored rather than as a set, so the deckbuilder reopens a deck in the order the
    /// player built it.
    /// <para>
    /// A saved deck is always legal against the rules and the collection at the moment it was saved
    /// (<see cref="PlayerSaveDeck"/> refuses anything else). It is not re-validated afterwards: a card whose
    /// rank moved is still the same card, and ownership cannot be lost — the Heist moves ranks, never cards
    /// out of a collection.
    /// </para>
    /// </summary>
    [MetaSerializable]
    public class PlayerDeck
    {
        [MetaMember(1)] public string       Name  { get; set; }
        [MetaMember(2)] public List<CardId> Cards { get; set; } = new List<CardId>();

        /// <summary> Longest deck name the game accepts. Enforced by the actions, on both sides. </summary>
        public const int MaxNameLength = 24;

        public PlayerDeck() { }

        public PlayerDeck(string name, IReadOnlyList<CardId> cards)
        {
            Name  = name;
            Cards = new List<CardId>(cards);
        }
    }

    /// <summary>
    /// What the account has done. Everything here is written by the match and the Heist.
    /// </summary>
    [MetaSerializable]
    public class PlayerRecord
    {
        [MetaMember(1)] public int RankedWins           { get; set; }
        [MetaMember(2)] public int RankedLosses         { get; set; }
        [MetaMember(3)] public int RankedDraws          { get; set; }
        /// <summary> Lifetime ranked matches. The newcomer shield's "first N ranked matches" reads this. </summary>
        [MetaMember(4)] public int RankedMatchesPlayed  { get; set; }
        /// <summary> Lifetime practice and friendly matches. Counted, never a threshold for anything. </summary>
        [MetaMember(5)] public int UnrankedMatchesPlayed { get; set; }

        /// <summary>
        /// The pairing axis the ranked queue bands on. Seeded to <c>Global.InitialRating</c> at account
        /// creation and moved by every ranked result, including a fallback-bot match — a ranked tap that the
        /// queue could not find a human for still moves rating, and never ranks
        /// (<c>Docs/matchmaking.md</c>).
        /// <para>
        /// The global leaderboard orders this same rating, with ranked wins breaking ties. There are no
        /// seasons or decay. The queue reads this as an opaque ordering.
        /// </para>
        /// <para>
        /// <b>The floor at zero is deliberate and it costs the zero-sum.</b> The delta the table computes is
        /// exactly antisymmetric, so what one seat gains the other loses — until the loser is holding less than
        /// the delta, when the winner still gains the whole of it and rating is minted. It is bounded (one
        /// account's worth, once, near the bottom of the ordering) and it buys the simpler property that a run
        /// of losses cannot fall out of the ordering.
        /// </para>
        /// </summary>
        [MetaMember(6)] public int Rating { get; set; }

        /// <summary> Every ranked match with a decided or drawn outcome. </summary>
        public int RankedDecided => RankedWins + RankedLosses + RankedDraws;
    }
}
