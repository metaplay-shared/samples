namespace Game.Logic
{
    /// <summary>
    /// The half of a Weather that <em>modifies</em>: a keyword aura and a cost rule. These are queries the
    /// engine asks while computing keywords and costs, never a branch on a Weather's identity — which is the
    /// test of whether the decomposition is right, since adding a Weather to the config pool must require no
    /// engine change (<c>Docs/rules.md</c>).
    /// <para>
    /// The other half — a Weather bound to a turn boundary or to any critter dying — is an effect like any
    /// other and goes through the interpreter, so it is not here.
    /// </para>
    /// </summary>
    public static class WeatherModifiers
    {
        /// <summary>
        /// The keywords every critter in play has under this Weather. Applied when a critter enters play
        /// rather than queried afterwards: there is exactly one Weather per match and it never changes, and
        /// baking it in is what lets a Bubble Bath Bubble stay popped once it has absorbed something — an
        /// aura that re-granted it would make the Weather an unbreakable shell rather than one free hit.
        /// </summary>
        public static KeywordFlags AuraKeywords(WeatherInfo weather)
        {
            if (weather == null || !weather.HasAura)
                return KeywordFlags.None;

            return weather.AuraKeyword.Ref.EngineFlag;
        }

        /// <summary>
        /// What this Weather adds to or takes off a card's cost for this seat right now. Acorn Rain's "first
        /// trick each player casts on their turn" reads the seat's own per-turn counter.
        /// </summary>
        public static int CostDelta(WeatherInfo weather, CardInfo card, SeatState seat)
            => CostDelta(weather, card, seat.TricksCastThisTurn);

        /// <summary>
        /// The same rule over the one number it actually reads, so a caller holding the counter but not the
        /// seat — the hand fan costing a card, the bot scorer costing a candidate — asks it without a second
        /// implementation.
        /// </summary>
        public static int CostDelta(WeatherInfo weather, CardInfo card, int tricksCastThisTurn)
        {
            if (weather == null || !weather.HasCostRule)
                return 0;

            switch (weather.CostScope)
            {
                case WeatherCostScope.FirstTrickPerTurn:
                    return card.Type == CardType.Trick && tricksCastThisTurn == 0 ? weather.CostDelta : 0;

                case WeatherCostScope.AllTricks:
                    return card.Type == CardType.Trick ? weather.CostDelta : 0;

                case WeatherCostScope.AllCritters:
                    return card.Type == CardType.Critter ? weather.CostDelta : 0;

                default:
                    return 0;
            }
        }
    }
}
