using System.Collections.Generic;

namespace Game.Logic.Tests
{
    /// <summary>
    /// What the two seats bring, rotated across a run.
    /// <para>
    /// Two fixed decks at rank 1 would be twenty thousand games over the same forty cards: ten of the
    /// catalogue's collectibles would never be dealt, and every rank track in the game would be read at the
    /// one rank where it does nothing. A sweep that large owes more than that — the bugs it exists to find
    /// live in the cards a hand-written test did not think to name.
    /// </para>
    /// <para>
    /// The rotation is a function of the game index alone, so a failing game still reproduces from its seed
    /// and its index and nothing else.
    /// </para>
    /// <para>
    /// <b>This is still the rotation, and deliberately not the config-authored starter decks.</b> Those cover
    /// every collectible between them, so they could make the first claim — but they have no rank dimension
    /// at all, and switching to them would silently drop rank coverage from every invariant suite in the
    /// project. The starter decks are measured on their own, over fixed ranks, by
    /// <c>StarterDeckBalanceTests</c>; that suite names its own decks through
    /// <c>SelfPlayGameSpec.Deck0</c>/<c>Deck1</c> rather than changing what this one deals, which also keeps
    /// the recorded first-seat readings comparable across measurements.
    /// </para>
    /// </summary>
    public static class SelfPlayDecks
    {
        /// <summary>
        /// The clan pairs, ordered so that every clan-limited clan takes the <em>first</em> slot at least
        /// once. A deck is the fifteen Wanderers plus its two clans truncated to the deck size, so the clan in
        /// the second slot loses two of its six — which means a clan that is never first is a clan two of
        /// whose cards are never dealt.
        /// </summary>
        static readonly string[][] ClanPairs =
        {
            new[] { "Kitsune",   "Tidepool"  },
            new[] { "Mossback",  "Sunny"     },
            new[] { "Moonlight", "Kitsune"   },
            new[] { "Sunny",     "Moonlight" },
            new[] { "Tidepool",  "Mossback"  },
        };

        /// <summary> Ranks worth playing at: the floor, the two the tracks step at, and the ceiling. </summary>
        static readonly int[] Ranks = { 1, 3, 5, 1, 4, 2 };

        public static int PairCount => ClanPairs.Length;

        /// <summary> One seat's deck for game <paramref name="index"/>. The two seats never draw the same pair. </summary>
        public static List<MatchDeckCard> For(SharedGameConfig config, int index, int seat)
        {
            int pair = (index + (seat == 0 ? 0 : 1 + index % (ClanPairs.Length - 1))) % ClanPairs.Length;
            int rank = Ranks[(index * MatchSeats.Count + seat) % Ranks.Length];

            return Build(config, rank, ClanPairs[pair]);
        }

        /// <summary> The rank a seat's deck is held at in game <paramref name="index"/>. </summary>
        public static int RankFor(int index, int seat) => Ranks[(index * MatchSeats.Count + seat) % Ranks.Length];

        static List<MatchDeckCard> Build(SharedGameConfig config, int rank, string[] clans)
        {
            List<string> order = new List<string> { "Wanderer" };
            order.AddRange(clans);

            List<MatchDeckCard> deck = new List<MatchDeckCard>();
            foreach (string clan in order)
            {
                List<CardInfo> inClan = new List<CardInfo>();
                foreach (CardInfo card in config.Cards.Values)
                {
                    if (card.Collectible && card.Clan.Ref.ClanId.Value == clan)
                        inClan.Add(card);
                }

                inClan.Sort((a, b) => CardInfo.CompareCanonical(a.CardId, b.CardId));

                foreach (CardInfo card in inClan)
                {
                    if (deck.Count >= config.Global.DeckSize)
                        break;
                    deck.Add(new MatchDeckCard(card.CardId, rank));
                }
            }

            return deck;
        }

        /// <summary> Every card the rotation ever deals, across one full turn of it. </summary>
        public static List<CardId> Covered(SharedGameConfig config)
        {
            List<CardId> seen = new List<CardId>();

            for (int index = 0; index < PairCount * Ranks.Length; index++)
            {
                for (int seat = 0; seat < MatchSeats.Count; seat++)
                {
                    foreach (MatchDeckCard card in For(config, index, seat))
                    {
                        if (!seen.Contains(card.Card))
                            seen.Add(card.Card);
                    }
                }
            }

            return seen;
        }

        /// <summary> Every rank the rotation ever deals at. </summary>
        public static List<int> RanksCovered()
        {
            List<int> seen = new List<int>();
            foreach (int rank in Ranks)
            {
                if (!seen.Contains(rank))
                    seen.Add(rank);
            }

            return seen;
        }
    }
}
