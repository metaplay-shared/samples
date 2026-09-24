using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;

namespace Game.Logic.Tests
{
    /// <summary>
    /// The deck legality policy: size, singleton, collectible-only, at most two clan-limited clans. Pure
    /// shared code, so the deckbuilder and the server refuse exactly the same lists.
    /// </summary>
    [TestFixture]
    public class DeckValidatorTests
    {
        static SharedGameConfig Config => TestGameConfig.Shared;

        /// <summary> A legal deck: 25 distinct collectible cards from one clan plus the Wanderers. </summary>
        static List<CardId> LegalDeck(string clan = "Kitsune", string secondClan = "Tidepool")
        {
            List<CardId> deck = Config.Cards.Values
                .Where(card => card.Collectible)
                .Where(card => IsClan(card, clan) || IsClan(card, secondClan) || IsClan(card, "Wanderer"))
                .OrderBy(card => card.CardId.Value, System.StringComparer.Ordinal)
                .Take(Config.Global.DeckSize)
                .Select(card => card.CardId)
                .ToList();

            Assert.That(deck.Count, Is.EqualTo(Config.Global.DeckSize), "the fixture itself must be a full deck");
            return deck;
        }

        static bool IsClan(CardInfo card, string clanId) => card.Clan.Ref.ClanId.Value == clanId;

        static CardId Id(string cardId) => CardId.FromString(cardId);

        [Test]
        public void Accepts_ATwoClanSingletonDeck()
        {
            Assert.That(DeckValidator.Validate(LegalDeck(), Config).IsValid, Is.True);
        }

        [Test]
        public void Refuses_ADeckOfTheWrongSize()
        {
            List<CardId> tooFew = LegalDeck();
            tooFew.RemoveAt(0);

            Assert.That(DeckValidator.Validate(tooFew, Config).Error, Is.EqualTo(DeckValidationError.WrongSize));
        }

        [Test]
        public void Refuses_ADuplicateCard()
        {
            List<CardId> deck = LegalDeck();
            deck[1] = deck[0];

            DeckValidationResult result = DeckValidator.Validate(deck, Config);

            Assert.That(result.Error, Is.EqualTo(DeckValidationError.DuplicateCard));
            Assert.That(result.OffendingCard, Is.EqualTo(deck[0]));
        }

        [Test]
        public void Refuses_ACardTheCatalogueDoesNotHave()
        {
            List<CardId> deck = LegalDeck();
            deck[0] = Id("NoSuchCard");

            DeckValidationResult result = DeckValidator.Validate(deck, Config);

            Assert.That(result.Error, Is.EqualTo(DeckValidationError.UnknownCard));
            Assert.That(result.OffendingCard, Is.EqualTo(Id("NoSuchCard")));
        }

        [Test]
        public void Refuses_ANonCollectibleCard()
        {
            // The Acorn is granted at the deal and tokens are summoned; neither is a card you may build with.
            List<CardId> deck = LegalDeck();
            deck[0] = Id("TheAcorn");

            DeckValidationResult result = DeckValidator.Validate(deck, Config);

            Assert.That(result.Error, Is.EqualTo(DeckValidationError.NotCollectible));
            Assert.That(result.OffendingCard, Is.EqualTo(Id("TheAcorn")));
        }

        [Test]
        public void Refuses_AThirdClan()
        {
            List<CardId> deck = Config.Cards.Values
                .Where(card => card.Collectible)
                .Where(card => IsClan(card, "Kitsune") || IsClan(card, "Tidepool") || IsClan(card, "Mossback") || IsClan(card, "Wanderer"))
                .OrderBy(card => card.CardId.Value, System.StringComparer.Ordinal)
                .Take(Config.Global.DeckSize)
                .Select(card => card.CardId)
                .ToList();

            Assert.That(deck.Count, Is.EqualTo(Config.Global.DeckSize));
            Assert.That(DeckValidator.Validate(deck, Config).Error, Is.EqualTo(DeckValidationError.TooManyClans));
        }

        [Test]
        public void Accepts_WanderersOnTopOfTwoClans()
        {
            // Wanderers are the glue: they never spend a clan slot, which is what makes a two-clan deck
            // buildable out of a small pool.
            List<CardId> deck = LegalDeck();
            int clanCount = deck
                .Select(cardId => Config.Cards[cardId].Clan.Ref)
                .Where(clan => clan.CountsTowardClanLimit)
                .Select(clan => clan.ClanId)
                .Distinct()
                .Count();

            Assert.That(clanCount, Is.EqualTo(2));
            Assert.That(deck.Any(cardId => !Config.Cards[cardId].Clan.Ref.CountsTowardClanLimit), Is.True);
            Assert.That(DeckValidator.Validate(deck, Config).IsValid, Is.True);
        }
    }
}
