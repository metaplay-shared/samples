namespace Game.Logic
{
    /// <summary>
    /// What a card costs and what a turn's mana does. One implementation, called by the engine when it
    /// validates a play and by the client when it decides what to highlight — the same function on both sides
    /// is what the legality promise rests on (<c>Docs/hidden-information.md</c>).
    /// </summary>
    public static class ManaRules
    {
        /// <summary>
        /// What a card in a seat's own hand costs it right now. The one place a held card's price is derived,
        /// so the bot, the board and the rules cannot disagree about it — and the reason no delivered payload
        /// carries a cost: every input here is public and moves under the holder's feet as the Weather turns
        /// and tricks are cast.
        /// </summary>
        public static int CostToPlay(SharedGameConfig config, MatchRulesState rules, int seat, HandCard card)
        {
            if (!config.Cards.TryGetValue(card.Card, out CardInfo info))
                return 0;

            return CostToPlay(config, rules.Weather?.Ref, info, card.Rank, rules.Seat(seat).TricksCastThisTurn);
        }

        /// <summary>
        /// What playing a card at a rank costs: the printed cost at that rank plus the Weather's cost rule,
        /// floored at zero.
        /// </summary>
        public static int CostToPlay(SharedGameConfig config, WeatherInfo weather, CardInfo card, int rank, int tricksCastThisTurn)
        {
            int cost = card.GetStatsAtRank(rank).Cost + WeatherModifiers.CostDelta(weather, card, tricksCastThisTurn);
            return cost < 0 ? 0 : cost;
        }

        /// <summary>
        /// The start-of-turn ramp and refill. Maximum mana starts at zero and grows by one at each of a seat's
        /// own turn starts, so a seat's first turn has exactly one; there is no cap, and unspent mana does not
        /// carry because the refill sets current to maximum.
        /// </summary>
        public static void RampAndRefill(GlobalConfig global, SeatState seat)
        {
            seat.SetMaxMana(seat.MaxMana + global.ManaGainPerTurn);
            seat.SetMana(seat.MaxMana);
        }
    }
}
