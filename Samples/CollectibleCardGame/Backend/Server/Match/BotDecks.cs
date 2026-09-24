using Game.Logic;
using Metaplay.Core;
using System.Collections.Generic;

namespace Game.Server.Match
{
    /// <summary>
    /// The deck a bot seat brings, and the ranks it holds it at: one of the starter decks, off the human's
    /// clan pair where the pool allows, at ranks that put its Power Score near the human's
    /// (<c>Docs/matchmaking.md</c>). Practice opponents and the fill-wait bot both come through here.
    /// </summary>
    public static class BotDecks
    {
        /// <summary>
        /// The starter deck a bot seat brings: config-authored, deterministic, and on a clan pair the human is
        /// not playing where the pool allows. An empty starter-deck pool is a config build error.
        /// </summary>
        /// <param name="salt">
        /// A stable, non-negative per-entry integer: the bot profile. Deterministic, so tests see the same
        /// opponent every run.
        /// </param>
        public static StarterDeckInfo DeckForOpponent(SharedGameConfig config, IReadOnlyList<CardId> humanDeck, int salt)
        {
            HashSet<ClanId> humanClans = ClanLimitedClansOf(config, humanDeck);

            List<StarterDeckInfo> candidates = new List<StarterDeckInfo>();
            foreach (StarterDeckInfo deck in config.StarterDecks.Values)
            {
                if (!ClanLimitedClansOf(config, deck.ToCardIds()).Overlaps(humanClans))
                    candidates.Add(deck);
            }

            // Unreachable for the shipped content (BotDeckTests pins it), but a narrower pool still seats a bot.
            if (candidates.Count == 0)
            {
                foreach (StarterDeckInfo deck in config.StarterDecks.Values)
                    candidates.Add(deck);
            }

            return candidates[salt % candidates.Count];
        }

        /// <summary> The clan-limited clans a card list draws on. Wanderers never count, as everywhere else. </summary>
        static HashSet<ClanId> ClanLimitedClansOf(SharedGameConfig config, IReadOnlyList<CardId> cards)
        {
            HashSet<ClanId> clans = new HashSet<ClanId>();
            foreach (CardId cardId in cards)
            {
                if (cardId == null || !config.Cards.TryGetValue(cardId, out CardInfo card))
                    continue;

                ClanInfo clan = card.Clan?.MaybeRef;
                if (clan != null && clan.CountsTowardClanLimit)
                    clans.Add(clan.ClanId);
            }

            return clans;
        }

        /// <summary>
        /// The rank to hold every card of the bot's deck at, so its Power Score lands as close to
        /// <paramref name="targetPowerScore"/> as a uniform rank can get. A Power Score is the sum of a deck's
        /// ranks, so a uniform rank of <c>score / size</c> is the answer, clamped to the rank track's range.
        /// </summary>
        public static int RankForPowerScore(SharedGameConfig config, int targetPowerScore)
        {
            GlobalConfig global = config.Global;
            int          size   = global.DeckSize > 0 ? global.DeckSize : 1;

            // Rounded rather than floored, so a human halfway between two ranks meets the nearer bot.
            int rank = (targetPowerScore + size / 2) / size;

            if (rank < global.RankMin)
                return global.RankMin;
            if (rank > global.RankMax)
                return global.RankMax;

            return rank;
        }

        /// <summary> Every card of <paramref name="deck"/> held at one rank. </summary>
        public static MetaDictionary<CardId, int> UniformRanks(IReadOnlyList<CardId> deck, int rank)
        {
            MetaDictionary<CardId, int> ranks = new MetaDictionary<CardId, int>();
            foreach (CardId cardId in deck)
                ranks[cardId] = rank;

            return ranks;
        }

        /// <summary>
        /// The names a bot seat may wear, which read as machines on purpose (<c>Docs/bots.md</c>,
        /// "Labelled, not disguised").
        /// </summary>
        static readonly string[] BotNames =
        {
            "Clockwork Cub",
            "Tin Tanuki",
            "Brass Badger",
            "Cogwheel Coyote",
            "Windup Weasel",
        };

        /// <summary> A bot name, chosen from the roster by the profile it is playing at. </summary>
        public static string NameFor(BotProfileId profile)
            => BotNames[(int)profile % BotNames.Length];
    }
}
