using Game.Logic;

namespace Game.Client.Services;

/// <summary>
/// Turns a <see cref="DeckValidationResult"/> into the one line the deckbuilder shows. The rule numbers come
/// from game config rather than from constants here, so a balance pass that changes the deck size or the clan
/// limit needs no client change.
/// </summary>
public static class DeckLegalityText
{
    public static string Describe(DeckValidationResult result, SharedGameConfig config)
    {
        string cardName = result.OffendingCard != null && config.Cards.TryGetValue(result.OffendingCard, out CardInfo card)
            ? card.DisplayName
            : result.OffendingCard?.Value ?? "that card";

        return result.Error switch
        {
            DeckValidationError.None           => "Legal deck.",
            DeckValidationError.WrongSize      => $"A deck is exactly {config.Global.DeckSize} cards.",
            DeckValidationError.DuplicateCard  => $"{cardName} is already in the deck — one copy of each.",
            DeckValidationError.UnknownCard    => $"{cardName} is not a card.",
            DeckValidationError.NotCollectible => $"{cardName} cannot be put in a deck.",
            DeckValidationError.TooManyClans   => $"A deck draws on at most {config.Global.MaxClansPerDeck} clans. Wanderers are free.",
            DeckValidationError.NotOwned       => $"You do not own {cardName}.",
            _                                  => "That deck is not legal.",
        };
    }
}
