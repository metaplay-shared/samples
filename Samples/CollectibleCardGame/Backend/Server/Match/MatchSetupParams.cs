using Game.Logic;
using Metaplay.Core;
using Metaplay.Core.Model;
using Metaplay.Server.MultiplayerEntity.InternalMessages;
using System.Collections.Generic;

namespace Game.Server.Match
{
    /// <summary>
    /// Everything the table is born with: who sits where, what each seat is playing, and what the match pays.
    /// The decks and ranks are a <b>snapshot</b> taken at formation, never re-read from the accounts.
    /// </summary>
    [MetaSerializableDerived(1)]
    public class MatchSetupParams : IMultiplayerEntitySetupParams
    {
        /// <summary> Exactly two, index == seat. </summary>
        [MetaMember(1)] public List<MatchSeatSetup> Seats  { get; private set; }
        [MetaMember(2)] public MatchStakes          Stakes { get; private set; }

        MatchSetupParams() { }

        public MatchSetupParams(List<MatchSeatSetup> seats, MatchStakes stakes)
        {
            Seats  = seats;
            Stakes = stakes;
        }
    }

    /// <summary> One seat as formation resolved it. A ranked ticket carries this seat from enqueue. </summary>
    [MetaSerializable]
    public class MatchSeatSetup
    {
        /// <summary> The owning account, or <see cref="EntityId.None"/> for a pure bot seat. </summary>
        [MetaMember(1)] public EntityId                    PlayerId    { get; private set; }
        [MetaMember(2)] public string                      DisplayName { get; private set; }
        [MetaMember(3)] public SeatOccupancy               Occupancy   { get; private set; }
        [MetaMember(4)] public BotProfileId                BotProfile  { get; private set; }
        /// <summary> The deck list in authored order. Instance identities are minted over this, then it is shuffled. </summary>
        [MetaMember(5)] public List<CardId>                Deck        { get; private set; }
        /// <summary> The rank the owner held each of those cards at, frozen with the list. </summary>
        [MetaMember(6)] public MetaDictionary<CardId, int> Ranks       { get; private set; }
        /// <summary> The cards the owner had locked at enqueue. Frozen with the deck, for the same reason. </summary>
        [MetaMember(7)] public List<CardId>                LockedCards { get; private set; }

        /// <summary>
        /// The rating this seat entered on, so the table can compute what the result is worth. It stops at the
        /// actor and never reaches the replicated model. A bot seat carries its opponent's rating.
        /// </summary>
        [MetaMember(8)] public int                         Rating      { get; private set; }

        MatchSeatSetup() { }

        public MatchSeatSetup(EntityId playerId, string displayName, SeatOccupancy occupancy, BotProfileId botProfile, List<CardId> deck, MetaDictionary<CardId, int> ranks, List<CardId> lockedCards, int rating)
        {
            PlayerId    = playerId;
            DisplayName = displayName;
            Occupancy   = occupancy;
            BotProfile  = botProfile;
            Deck        = deck;
            Ranks       = ranks;
            LockedCards = lockedCards;
            Rating      = rating;
        }

        /// <summary> The deck as the engine takes it: each card paired with the rank its owner holds it at. </summary>
        public List<MatchDeckCard> ResolveDeck(SharedGameConfig config)
        {
            List<MatchDeckCard> resolved = new List<MatchDeckCard>(Deck.Count);
            foreach (CardId cardId in Deck)
            {
                int rank = Ranks != null && Ranks.TryGetValue(cardId, out int owned) ? owned : config.Global.RankMin;
                resolved.Add(new MatchDeckCard(cardId, rank));
            }

            return resolved;
        }
    }
}
