using Metaplay.Core;
using Metaplay.Core.Config;
using Metaplay.Core.Model;
using System.Collections.Generic;

namespace Game.Logic
{
    /// <summary> Identifier for a <see cref="StarterDeckInfo"/> row. </summary>
    [MetaSerializable]
    public class StarterDeckId : StringId<StarterDeckId>
    {
    }

    /// <summary>
    /// One config-authored starter deck: a name, a line of copy, and a card list every account can play from
    /// the moment it exists. A starter deck is content rather than a code path — the account owns every card
    /// in it because the starter grant hands out the whole starter collection and the config build refuses a
    /// starter deck that names anything else.
    /// <para>
    /// A starter deck is a card list and nothing more. It carries no ranks and no power level of its own, so
    /// it is seated at whatever ranks its player's collection holds and its Power Score is that player's.
    /// </para>
    /// </summary>
    [MetaSerializable]
    public class StarterDeckInfo : IGameConfigData<StarterDeckId>
    {
        [MetaMember(1)] public StarterDeckId           StarterDeckId { get; private set; }
        [MetaMember(2)] public string                  DisplayName   { get; private set; }
        /// <summary> One line, shown beside the deck in the picker. </summary>
        [MetaMember(3)] public string                  Description   { get; private set; }
        /// <summary> Exactly DeckSize cards, in authored order. A MetaRef, so an unknown id fails the build. </summary>
        [MetaMember(4)] public List<MetaRef<CardInfo>> Cards         { get; private set; } = new List<MetaRef<CardInfo>>();

        public StarterDeckId ConfigKey => StarterDeckId;

        public StarterDeckInfo() { }

        public StarterDeckInfo(StarterDeckId starterDeckId, string displayName, string description, List<MetaRef<CardInfo>> cards)
        {
            StarterDeckId = starterDeckId;
            DisplayName   = displayName;
            Description   = description;
            Cards         = cards ?? new List<MetaRef<CardInfo>>();
        }

        /// <summary>
        /// The list as every consumer takes it. A fresh list per call: a config item is immutable, so there is
        /// nowhere to memoize this that would not be a mutation of shared state, and the call sites are one
        /// per match entry and one per picker render.
        /// <para>
        /// A blank row inside a deck block parses as a default element, so an entry can be null. The list is
        /// handed on with the null in it, which is what makes the config build refuse it as an unknown card
        /// rather than silently shipping a 24-card deck.
        /// </para>
        /// </summary>
        public List<CardId> ToCardIds()
        {
            List<CardId> ids = new List<CardId>(Cards.Count);
            foreach (MetaRef<CardInfo> card in Cards)
                ids.Add(card?.MaybeRef?.CardId);
            return ids;
        }
    }
}
