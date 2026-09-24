using Metaplay.Core.Model;
using Metaplay.Core.Player;
using System.Collections.Generic;

namespace Game.Logic
{
    /// <summary>
    /// Create a deck, or overwrite one that exists. One action for both, because the two differ only in where
    /// the id comes from: a create takes the next server-assigned id, an overwrite names an id the player's own
    /// model already holds. Deck ids are never client-chosen.
    /// <para>
    /// The client runs the identical validation before offering Save, so a refusal here means the client's copy
    /// of the model was stale — a save from a second tab, most plausibly. The deckbuilder shows the reason
    /// rather than discarding the edit.
    /// </para>
    /// </summary>
    [ModelAction(ActionCodes.PlayerSaveDeck)]
    public class PlayerSaveDeck : PlayerAction
    {
        /// <summary> The deck to overwrite, or null to create a new one. </summary>
        public int?          DeckId { get; private set; }
        public string        Name   { get; private set; }
        public List<CardId>  Cards  { get; private set; }

        public PlayerSaveDeck() { }

        public PlayerSaveDeck(int? deckId, string name, IReadOnlyList<CardId> cards)
        {
            DeckId = deckId;
            Name   = name;
            Cards  = cards != null ? new List<CardId>(cards) : new List<CardId>();
        }

        public override MetaActionResult Execute(PlayerModel player, bool commit)
        {
            // An id the client invented is refused before anything else looks at it.
            if (DeckId.HasValue && !player.Decks.ContainsKey(DeckId.Value))
                return ActionResults.UnknownDeck;

            // Trimmed before it is measured, so a trailing space is never the reason a name is refused, and
            // both sides agree on the string that gets stored.
            string name = Name?.Trim();
            if (string.IsNullOrEmpty(name) || name.Length > PlayerDeck.MaxNameLength)
                return ActionResults.InvalidDeckName;

            // Only a create can push the account over the cap; an overwrite replaces a deck that is already
            // counted.
            if (!DeckId.HasValue && player.Decks.Count >= PlayerModel.MaxSavedDecks)
                return ActionResults.TooManySavedDecks;

            DeckValidationResult legality = DeckValidator.ValidateForPlayer(Cards, player.GameConfig, player.Collection);
            if (!legality.IsValid)
                return ActionResults.ForDeckError(legality.Error);

            if (commit)
            {
                int deckId = DeckId ?? player.NextDeckId;
                player.Decks[deckId] = new PlayerDeck(name, Cards);

                if (!DeckId.HasValue)
                    player.NextDeckId++;

                player.ClientListener.Collection?.GenericPropertyChanged(nameof(PlayerModel.Decks));
            }

            return MetaActionResult.Success;
        }
    }

    /// <summary>
    /// Rename a saved deck without resubmitting its cards. The deck list offers this on its own, so renaming
    /// does not mean reopening the editor and re-sending 25 card ids to change one string.
    /// </summary>
    [ModelAction(ActionCodes.PlayerRenameDeck)]
    public class PlayerRenameDeck : PlayerAction
    {
        public int    DeckId { get; private set; }
        public string Name   { get; private set; }

        public PlayerRenameDeck() { }

        public PlayerRenameDeck(int deckId, string name)
        {
            DeckId = deckId;
            Name   = name;
        }

        public override MetaActionResult Execute(PlayerModel player, bool commit)
        {
            if (!player.Decks.TryGetValue(DeckId, out PlayerDeck deck))
                return ActionResults.UnknownDeck;

            string name = Name?.Trim();
            if (string.IsNullOrEmpty(name) || name.Length > PlayerDeck.MaxNameLength)
                return ActionResults.InvalidDeckName;

            if (commit)
            {
                deck.Name = name;
                player.ClientListener.Collection?.GenericPropertyChanged(nameof(PlayerModel.Decks));
            }

            return MetaActionResult.Success;
        }
    }

    /// <summary>
    /// Delete a saved deck. An account with no decks at all is a valid state — the deckbuilder's empty state is
    /// exactly what a player who deletes their last deck sees next — so there is no "keep one" rule.
    /// <para>
    /// A deck frozen inside a live queue or match snapshot is not touched by this: the stakes were fixed from a
    /// snapshot taken at enqueue, and nothing the account does afterwards moves a wager already agreed.
    /// </para>
    /// </summary>
    [ModelAction(ActionCodes.PlayerDeleteDeck)]
    public class PlayerDeleteDeck : PlayerAction
    {
        public int DeckId { get; private set; }

        public PlayerDeleteDeck() { }
        public PlayerDeleteDeck(int deckId) { DeckId = deckId; }

        public override MetaActionResult Execute(PlayerModel player, bool commit)
        {
            if (!player.Decks.ContainsKey(DeckId))
                return ActionResults.UnknownDeck;

            if (commit)
            {
                player.Decks.Remove(DeckId);
                player.ClientListener.Collection?.GenericPropertyChanged(nameof(PlayerModel.Decks));
            }

            return MetaActionResult.Success;
        }
    }
}
