using Game.Logic;
using Game.Server.Match;
using Metaplay.Core;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.Server.Tests
{
    /// <summary>
    /// The bot opponent's deck. <c>matchmaking.md</c> asks for a config-authored deck chosen to sit in the
    /// band the human queued in, because the point is that a practice game is a real game rather than a
    /// formality — so the list has to be legal by the same shared validator the deckbuilder runs, its Power
    /// Score has to land near the human's, and it must not simply mirror the deck the human picked.
    /// <para>
    /// The legality assertion overlaps the config build's own rule on purpose: this one guards the selector.
    /// </para>
    /// </summary>
    [TestFixture]
    public class BotDeckTests
    {
        SharedGameConfig Config => TestGameConfig.Shared;

        /// <summary> The clan-limited clans a card list draws on, as every consumer computes them. </summary>
        HashSet<ClanId> ClansOf(IReadOnlyList<CardId> cards)
        {
            HashSet<ClanId> clans = new HashSet<ClanId>();
            foreach (CardId cardId in cards)
            {
                ClanInfo clan = Config.Cards[cardId].Clan.Ref;
                if (clan.CountsTowardClanLimit)
                    clans.Add(clan.ClanId);
            }

            return clans;
        }

        /// <summary>
        /// The ten two-clan decks a human can bring, taken from the config rather than hard-coded: every pair
        /// of the five clan-limited clans, each padded out to a legal deck with Wanderers.
        /// </summary>
        List<List<CardId>> EveryHumanClanPair()
        {
            List<ClanId> limited = Config.Clans.Values
                .Where(clan => clan.CountsTowardClanLimit)
                .Select(clan => clan.ClanId)
                .ToList();

            List<CardId> wanderers = Config.Cards.Values
                .Where(card => card.Collectible && !card.Clan.Ref.CountsTowardClanLimit)
                .Select(card => card.CardId)
                .ToList();

            List<List<CardId>> decks = new List<List<CardId>>();
            for (int i = 0; i < limited.Count; i++)
            {
                for (int j = i + 1; j < limited.Count; j++)
                {
                    List<CardId> deck = Config.Cards.Values
                        .Where(card => card.Collectible
                                    && (card.Clan.Ref.ClanId == limited[i] || card.Clan.Ref.ClanId == limited[j]))
                        .Select(card => card.CardId)
                        .ToList();

                    foreach (CardId cardId in wanderers)
                    {
                        if (deck.Count >= Config.Global.DeckSize)
                            break;
                        deck.Add(cardId);
                    }

                    Assert.That(DeckValidator.Validate(deck, Config).IsValid, Is.True, "the fixture built an illegal human deck");
                    decks.Add(deck);
                }
            }

            Assert.That(decks.Count, Is.EqualTo(10), "five clan-limited clans make ten pairs");
            return decks;
        }

        [Test]
        public void EveryDeckTheSelectorCanReturnIsLegalByTheSharedValidator()
        {
            foreach (StarterDeckInfo deck in Config.StarterDecks.Values)
            {
                List<CardId>         cards  = deck.ToCardIds();
                DeckValidationResult result = DeckValidator.Validate(cards, Config);

                Assert.That(result.IsValid, Is.True, $"{deck.StarterDeckId} is illegal: {result}");
                Assert.That(cards.Count, Is.EqualTo(Config.Global.DeckSize), deck.StarterDeckId.Value);
            }
        }

        [Test]
        public void TheBotsDeckIsTheSameListEveryTime()
        {
            // Deterministic on purpose: a practice opponent that differed between two runs of the same test
            // would make every board assertion a coin flip.
            foreach (List<CardId> humanDeck in EveryHumanClanPair())
            {
                StarterDeckInfo first  = BotDecks.DeckForOpponent(Config, humanDeck, salt: 3);
                StarterDeckInfo second = BotDecks.DeckForOpponent(Config, humanDeck, salt: 3);

                Assert.That(second.StarterDeckId, Is.EqualTo(first.StarterDeckId));
            }
        }

        [Test]
        public void TheBotAvoidsTheHumansClanPair()
        {
            // The property the disjointness rule rests on: for every one of the ten two-clan human decks, at
            // least one starter deck is clan-disjoint, so the fallback to the whole list is unreachable.
            foreach (List<CardId> humanDeck in EveryHumanClanPair())
            {
                HashSet<ClanId> humanClans = ClansOf(humanDeck);

                foreach (BotProfileId profile in Enum.GetValues<BotProfileId>())
                {
                    StarterDeckInfo bot = BotDecks.DeckForOpponent(Config, humanDeck, (int)profile);

                    Assert.That(ClansOf(bot.ToCardIds()).Overlaps(humanClans), Is.False,
                        $"a human on {string.Join("+", humanClans.Select(clan => clan.Value))} met {bot.StarterDeckId}");
                }
            }
        }

        [Test]
        public void EveryProfileDrawsAValidDeck()
        {
            // Catches a modulo sign or an off-by-one: five profiles over ten human pairs, every answer real.
            foreach (List<CardId> humanDeck in EveryHumanClanPair())
            {
                foreach (BotProfileId profile in Enum.GetValues<BotProfileId>())
                {
                    StarterDeckInfo bot = BotDecks.DeckForOpponent(Config, humanDeck, (int)profile);

                    Assert.That(bot, Is.Not.Null);
                    Assert.That(DeckValidator.Validate(bot.ToCardIds(), Config).IsValid, Is.True, bot.StarterDeckId.Value);
                }
            }
        }

        [Test]
        public void TheDifficultyChangesTheOpponentsDeck()
        {
            // Otherwise the salt is wired to nothing: the deck would be a function of the human's clans alone
            // and picking a different difficulty would change only how the same list is played.
            //
            // DELIBERATELY WEAK, and do not strengthen it into "every pair sees two decks" — that is false for
            // the shipped content and correctly so. Three of the ten human pairs have exactly one clan-disjoint
            // starter deck, so for those the salt has nothing to choose between and every difficulty draws the
            // same list: Kitsune+Mossback → PorchlightPack, Kitsune+Sunny → RiverbankPatience, and
            // Mossback+Moonlight → FireAndFoam. Determinism and never-a-mirror are what matter there, and
            // TheBotAvoidsTheHumansClanPair covers all ten pairs against all five profiles.
            List<string> pairsSeeingOneDeck = new List<string>();

            foreach (List<CardId> humanDeck in EveryHumanClanPair())
            {
                HashSet<StarterDeckId> seen = Enum.GetValues<BotProfileId>()
                    .Select(profile => BotDecks.DeckForOpponent(Config, humanDeck, (int)profile).StarterDeckId)
                    .ToHashSet();

                if (seen.Count == 1)
                    pairsSeeingOneDeck.Add(string.Join("+", ClansOf(humanDeck).Select(clan => clan.Value)));
            }

            Assert.That(pairsSeeingOneDeck.Count, Is.LessThan(10),
                "no human clan pair sees more than one opponent deck, so the salt is wired to nothing");

            // Pinned so a content edit that removed the last choice from a pair shows up here rather than as a
            // quietly weaker guarantee.
            Assert.That(pairsSeeingOneDeck.Count, Is.EqualTo(3),
                $"the single-candidate pairs changed: {string.Join(", ", pairsSeeingOneDeck)}");
        }

        [Test]
        public void TheBotsRankLandsItsPowerScoreNearTheHumans()
        {
            List<CardId> deck = Config.StarterDecks.Values.First().ToCardIds();

            foreach (int humanScore in new[] { 25, 40, 63, 90, 125 })
            {
                int rank = BotDecks.RankForPowerScore(Config, humanScore);
                int botScore = DeckValidator.ComputePowerScore(deck, BotDecks.UniformRanks(deck, rank));

                // A Power Score is the sum of a deck's ranks, so a uniform rank can only land within half a
                // deck's worth of the target — which is the closest a uniform rank can get.
                Assert.That(Math.Abs(botScore - humanScore), Is.LessThanOrEqualTo(Config.Global.DeckSize / 2 + 1),
                    $"a human at {humanScore} met a bot at {botScore}");
            }
        }

        [Test]
        public void TheBotsRankStaysInsideTheRankTracksRange()
        {
            Assert.That(BotDecks.RankForPowerScore(Config, 0), Is.EqualTo(Config.Global.RankMin));
            Assert.That(BotDecks.RankForPowerScore(Config, 100_000), Is.EqualTo(Config.Global.RankMax));
        }

        [Test]
        public void EveryBotProfileHasAName()
        {
            // The names read as machines on purpose: the computer-player mark carries the honesty on its own,
            // but a mark on a name that reads like a person's is a weaker signal than a mark on one that does
            // not.
            foreach (BotProfileId id in Enum.GetValues<BotProfileId>())
                Assert.That(BotDecks.NameFor(id), Is.Not.Empty);
        }
    }
}
