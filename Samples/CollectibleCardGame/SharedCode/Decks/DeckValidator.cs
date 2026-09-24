using Metaplay.Core;
using System.Collections.Generic;

namespace Game.Logic
{
    /// <summary> Why a deck list is not legal. </summary>
    public enum DeckValidationError
    {
        None = 0,
        /// <summary> The list does not hold exactly <c>DeckSize</c> cards. </summary>
        WrongSize,
        /// <summary> Singleton is violated: the same card appears twice. </summary>
        DuplicateCard,
        /// <summary> The list names a card that is not in the catalogue. </summary>
        UnknownCard,
        /// <summary> The list names a token or the second-player bonus card. </summary>
        NotCollectible,
        /// <summary> The list draws on more clan-limited clans than a deck may. </summary>
        TooManyClans,
        /// <summary> The list names a card this player's collection does not hold. </summary>
        NotOwned,
    }

    /// <summary> The outcome of checking one deck list, naming the card that caused a refusal. </summary>
    public readonly struct DeckValidationResult
    {
        public readonly DeckValidationError Error;
        /// <summary> The card the refusal is about, when the error is about one card. </summary>
        public readonly CardId              OffendingCard;

        DeckValidationResult(DeckValidationError error, CardId offendingCard)
        {
            Error         = error;
            OffendingCard = offendingCard;
        }

        public bool IsValid => Error == DeckValidationError.None;

        public static readonly DeckValidationResult Valid = new DeckValidationResult(DeckValidationError.None, null);

        public static DeckValidationResult Failed(DeckValidationError error, CardId offendingCard = null)
            => new DeckValidationResult(error, offendingCard);

        public override string ToString()
            => IsValid ? "Valid" : (OffendingCard != null ? $"{Error} ({OffendingCard})" : Error.ToString());
    }

    /// <summary>
    /// Whether a deck list is legal by the game's rules alone: exactly <c>DeckSize</c> cards, singleton, all
    /// collectible, drawing on at most <c>MaxClansPerDeck</c> clan-limited clans. Wanderers never count
    /// against the clan limit, which is what lets them glue any two clans together.
    /// <para>
    /// This is a pure shared-code policy, so the deckbuilder can refuse the same lists the server does. It
    /// deliberately knows nothing about who owns what: whether the submitting player actually has these cards
    /// is an ownership question the server answers on top of this one. Lock state is not deck validation
    /// either — locks freeze at enqueue, with the deck.
    /// </para>
    /// <para>
    /// A deck of only Wanderers is legal here, and merely unbuildable while the pool is small. That is a
    /// decision, not an oversight: the lower clan bound buys nothing the pool does not already enforce.
    /// </para>
    /// </summary>
    public static class DeckValidator
    {
        public static DeckValidationResult Validate(IReadOnlyList<CardId> cards, SharedGameConfig gameConfig)
            => Validate(cards, gameConfig.Cards, gameConfig.Global);

        /// <summary>
        /// The same rules, stated over a card catalogue and the globals rather than a whole config. This is
        /// the implementation and the overload above delegates to it, so the config build can check a deck
        /// against a pool it is still assembling without a second definition of legal. A config library is
        /// itself an <c>IReadOnlyDictionary</c>, so the delegation allocates nothing.
        /// </summary>
        public static DeckValidationResult Validate(
            IReadOnlyList<CardId> cards,
            IReadOnlyDictionary<CardId, CardInfo> catalogue,
            GlobalConfig global)
        {
            if (cards == null)
                return DeckValidationResult.Failed(DeckValidationError.WrongSize);

            if (cards.Count != global.DeckSize)
                return DeckValidationResult.Failed(DeckValidationError.WrongSize);

            HashSet<CardId> seen  = new HashSet<CardId>();
            HashSet<ClanId> clans = new HashSet<ClanId>();

            foreach (CardId cardId in cards)
            {
                if (cardId == null)
                    return DeckValidationResult.Failed(DeckValidationError.UnknownCard);

                if (!seen.Add(cardId))
                    return DeckValidationResult.Failed(DeckValidationError.DuplicateCard, cardId);

                if (!catalogue.TryGetValue(cardId, out CardInfo card))
                    return DeckValidationResult.Failed(DeckValidationError.UnknownCard, cardId);

                if (!card.Collectible)
                    return DeckValidationResult.Failed(DeckValidationError.NotCollectible, cardId);

                ClanInfo clan = card.Clan.Ref;
                if (clan.CountsTowardClanLimit)
                    clans.Add(clan.ClanId);
            }

            if (clans.Count > global.MaxClansPerDeck)
                return DeckValidationResult.Failed(DeckValidationError.TooManyClans);

            return DeckValidationResult.Valid;
        }

        /// <summary>
        /// Whether a deck list is legal <em>for one player</em>: everything <see cref="Validate"/> checks, plus
        /// the ownership rule the rules alone cannot state. This is what the save action runs and what the
        /// deckbuilder predicts with, so the two can never disagree about a list.
        /// <para>
        /// Ownership is a wrapper rather than a sixth rule inside <see cref="Validate"/> so the config-only
        /// half stays callable with no player in scope — which is what lets the config build prove the starter
        /// collection can assemble a legal deck before any account exists.
        /// </para>
        /// </summary>
        public static DeckValidationResult ValidateForPlayer(
            IReadOnlyList<CardId> cards,
            SharedGameConfig gameConfig,
            MetaDictionary<CardId, int> collection)
        {
            DeckValidationResult shape = Validate(cards, gameConfig);
            if (!shape.IsValid)
                return shape;

            foreach (CardId cardId in cards)
            {
                if (collection == null || !collection.ContainsKey(cardId))
                    return DeckValidationResult.Failed(DeckValidationError.NotOwned, cardId);
            }

            return DeckValidationResult.Valid;
        }

        /// <summary>
        /// A deck's Power Score: the sum of its cards' ranks (<c>Docs/game-design.md</c>, "Ranks"). Matchmaking
        /// pairs on this number and the stakes tier is set by the gap between two of them, so the deckbuilder
        /// reads the same implementation those will — there is one definition and it is this one.
        /// <para>
        /// A card the collection does not hold contributes nothing, so a partially built list scores what it
        /// actually has rather than throwing. A legal deck can never contain one.
        /// </para>
        /// </summary>
        public static int ComputePowerScore(IReadOnlyList<CardId> cards, MetaDictionary<CardId, int> collection)
        {
            if (cards == null || collection == null)
                return 0;

            int total = 0;
            foreach (CardId cardId in cards)
            {
                if (cardId != null && collection.TryGetValue(cardId, out int rank))
                    total += rank;
            }

            return total;
        }
    }
}
