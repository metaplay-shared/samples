using Metaplay.Core.Model;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace Game.Logic
{
    /// <summary>
    /// Which deck an entry means: one of this account's saved decks, or one of the config-authored starter
    /// decks. Exactly one of the two, which is what makes it a choice rather than two optional fields — every
    /// consumer resolves it through <see cref="DeckChoiceResolver"/>, so there is one definition of "the deck
    /// this entry means" for the same reason <see cref="DeckValidator"/> is the one definition of legal.
    /// <para>
    /// The starter half is an id rather than a <c>MetaRef</c> because this value is persisted on the player
    /// model: an unresolvable <c>MetaRef</c> in a persisted model is an unloadable row, where an id naming a
    /// deck the config no longer carries is a lookup that misses and falls back.
    /// </para>
    /// <para>
    /// <b>Immutable once built</b>, which is why <see cref="PlayerNoteDeckPlayed"/> stores the action's own
    /// payload object on the model rather than copying it.
    /// </para>
    /// </summary>
    [MetaSerializable]
    public class DeckChoice
    {
        /// <summary> The saved deck's id, or 0 when this is a starter deck. Deck ids start at 1. </summary>
        [MetaMember(1)] public int           SavedDeckId   { get; private set; }
        /// <summary> The starter deck's id, or null when this is a saved deck. </summary>
        [MetaMember(2)] public StarterDeckId StarterDeckId { get; private set; }

        public DeckChoice() { }

        public static DeckChoice Saved(int deckId)         => new DeckChoice { SavedDeckId = deckId };
        public static DeckChoice Starter(StarterDeckId id) => new DeckChoice { StarterDeckId = id };

        public bool IsStarter => StarterDeckId != null;
        public bool IsSaved   => StarterDeckId == null && SavedDeckId != 0;

        /// <summary>
        /// Neither half set, or both: a payload no picker produces. A deserialized payload from an old or
        /// hostile client can be anything, so this is checked by the consumers rather than enforced by a
        /// factory that would have to throw inside an action's <c>Execute</c>.
        /// </summary>
        public bool IsWellFormed => (StarterDeckId != null) ^ (SavedDeckId != 0);

        public override string ToString() => ToKey();

        /// <summary>
        /// The picker's option value. One definition, shared with <see cref="TryParseKey"/>, so the two
        /// halves of the round trip cannot drift apart.
        /// </summary>
        public string ToKey() => IsStarter ? $"starter:{StarterDeckId}" : $"saved:{SavedDeckId.ToString(CultureInfo.InvariantCulture)}";

        /// <summary>
        /// The inverse of <see cref="ToKey"/>. False for a key with no recognisable shape — no prefix, an
        /// empty or non-numeric saved id, a saved id of zero.
        /// <para>
        /// <b>Parsing is syntax; <see cref="DeckChoiceResolver"/> is semantics.</b> A <c>StarterDeckId</c> has
        /// no grammar, so <c>"starter:&lt;anything&gt;"</c> parses true and then resolves to
        /// <see cref="DeckChoiceError.UnknownStarterDeck"/>, which keeps "is this a key" free of a config lookup.
        /// </para>
        /// </summary>
        public static bool TryParseKey(string key, out DeckChoice choice)
        {
            choice = null;

            if (string.IsNullOrEmpty(key))
                return false;

            if (key.StartsWith("starter:", StringComparison.Ordinal))
            {
                string id = key.Substring("starter:".Length);
                if (id.Length == 0)
                    return false;

                choice = Starter(StarterDeckId.FromString(id));
                return true;
            }

            if (key.StartsWith("saved:", StringComparison.Ordinal))
            {
                if (!int.TryParse(key.Substring("saved:".Length), NumberStyles.None, CultureInfo.InvariantCulture, out int deckId) || deckId == 0)
                    return false;

                choice = Saved(deckId);
                return true;
            }

            return false;
        }
    }

    /// <summary> Why a deck choice does not name a playable deck. </summary>
    public enum DeckChoiceError
    {
        None = 0,
        /// <summary> Neither half set, or both. </summary>
        Malformed,
        /// <summary> A saved-deck id this account's decks no longer hold. </summary>
        UnknownSavedDeck,
        /// <summary> A starter-deck id the config no longer carries. </summary>
        UnknownStarterDeck,
    }

    /// <summary> The cards a choice means, or why it means none. </summary>
    public readonly struct DeckChoiceResult
    {
        public readonly DeckChoiceError       Error;
        /// <summary> The deck's cards, in authored order. Null unless <see cref="Error"/> is None. </summary>
        public readonly IReadOnlyList<CardId> Cards;

        DeckChoiceResult(DeckChoiceError error, IReadOnlyList<CardId> cards)
        {
            Error = error;
            Cards = cards;
        }

        public bool IsValid => Error == DeckChoiceError.None;

        public static DeckChoiceResult Resolved(IReadOnlyList<CardId> cards) => new DeckChoiceResult(DeckChoiceError.None, cards);
        public static DeckChoiceResult Failed(DeckChoiceError error)         => new DeckChoiceResult(error, null);

        public override string ToString() => IsValid ? $"{Cards.Count.ToString(CultureInfo.InvariantCulture)} cards" : Error.ToString();
    }

    /// <summary>
    /// Turns a <see cref="DeckChoice"/> into a card list. The only place either kind of deck becomes cards,
    /// which is what keeps the two entry actions and the two actor paths agreeing about what an entry meant.
    /// <para>
    /// Ownership and legality are deliberately not checked here: those are <see cref="DeckValidator"/>'s, and
    /// every caller runs it on the result. Keeping the two questions separable is the same split that lets the
    /// config build check a starter deck with no player in scope.
    /// </para>
    /// </summary>
    public static class DeckChoiceResolver
    {
        /// <summary> The cards this choice means, for this account. </summary>
        public static DeckChoiceResult Resolve(DeckChoice choice, PlayerModel player)
        {
            if (choice == null || !choice.IsWellFormed)
                return DeckChoiceResult.Failed(DeckChoiceError.Malformed);

            if (choice.IsStarter)
                return ResolveStarter(choice.StarterDeckId, player.GameConfig);

            if (!player.Decks.TryGetValue(choice.SavedDeckId, out PlayerDeck deck))
                return DeckChoiceResult.Failed(DeckChoiceError.UnknownSavedDeck);

            return DeckChoiceResult.Resolved(deck.Cards);
        }

        /// <summary> The config half alone, with no player in scope. </summary>
        static DeckChoiceResult ResolveStarter(StarterDeckId id, SharedGameConfig config)
        {
            if (id == null)
                return DeckChoiceResult.Failed(DeckChoiceError.Malformed);

            if (!config.StarterDecks.TryGetValue(id, out StarterDeckInfo deck))
                return DeckChoiceResult.Failed(DeckChoiceError.UnknownStarterDeck);

            return DeckChoiceResult.Resolved(deck.ToCardIds());
        }
    }
}
