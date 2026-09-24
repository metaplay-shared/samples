using Metaplay.Core.Model;
using System;
using System.Collections.Generic;

namespace Game.Logic
{
    /// <summary>
    /// The four suits of a standard deck. The numeric values define the suit order used by
    /// <see cref="Card.CompareTo"/> and <see cref="Deck.CreateOrdered"/>. In play, no suit outranks another
    /// except the trump suit chosen at the deal.
    /// </summary>
    [MetaSerializable]
    public enum Suit
    {
        Clubs    = 0,
        Diamonds = 1,
        Hearts   = 2,
        Spades   = 3,
    }

    /// <summary>
    /// Card ranks, two through ace. A higher numeric value is a higher card, and ace is always high.
    /// </summary>
    [MetaSerializable]
    public enum Rank
    {
        Two   = 2,
        Three = 3,
        Four  = 4,
        Five  = 5,
        Six   = 6,
        Seven = 7,
        Eight = 8,
        Nine  = 9,
        Ten   = 10,
        Jack  = 11,
        Queen = 12,
        King  = 13,
        Ace   = 14,
    }

    /// <summary>
    /// One playing card, identified by its suit and rank. A move names the card it plays, not a position in the
    /// hand (see <c>docs/match.md</c>).
    /// </summary>
    [MetaSerializable]
    public struct Card : IEquatable<Card>, IComparable<Card>
    {
        [MetaMember(1)] public Suit Suit { get; private set; }
        [MetaMember(2)] public Rank Rank { get; private set; }

        public Card(Suit suit, Rank rank)
        {
            Suit = suit;
            Rank = rank;
        }

        /// <summary>
        /// Orders cards by suit, then by rank ascending. Used for deterministic sorting and display. It has no
        /// meaning in the rules of play.
        /// </summary>
        public int CompareTo(Card other)
        {
            if (Suit != other.Suit)
                return ((int)Suit).CompareTo((int)other.Suit);
            return ((int)Rank).CompareTo((int)other.Rank);
        }

        public bool Equals(Card other) => Suit == other.Suit && Rank == other.Rank;
        public override bool Equals(object obj) => obj is Card other && Equals(other);
        public override int GetHashCode() => ((int)Suit * 16) + (int)Rank;

        public static bool operator ==(Card a, Card b) => a.Equals(b);
        public static bool operator !=(Card a, Card b) => !a.Equals(b);

        public override string ToString() => $"{RankSymbol(Rank)}{SuitSymbol(Suit)}";

        /// <summary>
        /// The rank's short label, such as "10" or "J". Public so the client's card faces use the same labels as
        /// <see cref="ToString"/>.
        /// </summary>
        public static string RankSymbol(Rank rank)
        {
            switch (rank)
            {
                case Rank.Ten:   return "10";
                case Rank.Jack:  return "J";
                case Rank.Queen: return "Q";
                case Rank.King:  return "K";
                case Rank.Ace:   return "A";
                default:         return ((int)rank).ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        static string SuitSymbol(Suit suit)
        {
            switch (suit)
            {
                case Suit.Clubs:    return "C";
                case Suit.Diamonds: return "D";
                case Suit.Hearts:   return "H";
                default:            return "S";
            }
        }
    }

    /// <summary>
    /// The standard 52-card deck the game is dealt from.
    /// </summary>
    public static class Deck
    {
        public const int NumCards = 52;

        // Private because these arrays define the deck order that the seeded shuffle permutes. A caller that
        // changed them would make the same seed deal different hands on different hosts.
        static readonly Suit[] Suits = new Suit[] { Suit.Clubs, Suit.Diamonds, Suit.Hearts, Suit.Spades };

        static readonly Rank[] Ranks = new Rank[]
        {
            Rank.Two, Rank.Three, Rank.Four, Rank.Five, Rank.Six, Rank.Seven, Rank.Eight,
            Rank.Nine, Rank.Ten, Rank.Jack, Rank.Queen, Rank.King, Rank.Ace,
        };

        /// <summary>
        /// A new list of all cards, suit by suit and low rank to high. The seeded shuffle permutes this order, so
        /// changing it changes the deal every seed produces.
        /// </summary>
        public static List<Card> CreateOrdered()
        {
            List<Card> cards = new List<Card>(NumCards);
            foreach (Suit suit in Suits)
            {
                foreach (Rank rank in Ranks)
                    cards.Add(new Card(suit, rank));
            }
            return cards;
        }
    }
}
