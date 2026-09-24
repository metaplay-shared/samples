using Game.Logic;
using Game.Server.Match;
using Metaplay.Core;
using System.Collections.Generic;

namespace Game.Server.Matchmaking
{
    /// <summary>
    /// What a seat and a ticket are, taken from the account's model at one instant. Pure, so a fact the model
    /// carries reaches the queue only because this names it, and a test can hold the whole shape.
    /// </summary>
    public static class MatchmakingTicketPolicy
    {
        /// <summary>
        /// Freeze the seat an account plays a match with, practice or ranked: the deck list, the rank this
        /// account holds each card at, its padlocks and its rating, plus the deck's Power Score.
        /// </summary>
        public static (MatchSeatSetup Seat, int PowerScore) FreezeSeat(PlayerModel model, EntityId playerId, IReadOnlyList<CardId> deck, SharedGameConfig config)
        {
            List<CardId> cards      = new List<CardId>(deck);
            int          powerScore = DeckValidator.ComputePowerScore(cards, model.Collection);

            MetaDictionary<CardId, int> ranks = new MetaDictionary<CardId, int>();
            foreach (CardId cardId in cards)
                ranks[cardId] = model.Collection.TryGetValue(cardId, out int rank) ? rank : config.Global.RankMin;

            List<CardId> locks = new List<CardId>();
            foreach ((int _, CardId locked) in model.LockSlots)
            {
                if (locked != null)
                    locks.Add(locked);
            }

            MatchSeatSetup seat = new MatchSeatSetup(
                playerId, model.DisplayName, SeatOccupancy.Human, BotProfileId.Strongest,
                cards, ranks, locks, model.Record.Rating);

            return (seat, powerScore);
        }

        /// <summary> The ranked ticket for a frozen seat. </summary>
        public static MatchmakingTicket Freeze(PlayerModel model, DeckChoice deckChoice, MatchSeatSetup seat, int powerScore, MetaTime arrivedAt)
            => new MatchmakingTicket(
                seat,
                powerScore,
                model.Record.RankedMatchesPlayed,
                model.NewcomerShieldWaived,
                deckChoice,
                arrivedAt);
    }
}
