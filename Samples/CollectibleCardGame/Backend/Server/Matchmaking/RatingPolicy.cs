using Game.Logic;
using System;

namespace Game.Server.Matchmaking
{
    /// <summary>
    /// How much one ranked result moves one account's rating: fixed-K Elo. The queue reads the rating only
    /// as an ordering, so this can be replaced without the band policy changing.
    /// </summary>
    public static class RatingPolicy
    {
        /// <summary>
        /// This seat's rating change. A draw scores a half. Rounded away from zero, so a real movement is never
        /// rounded to nothing.
        /// </summary>
        public static int Delta(int ownRating, int opponentRating, MatchAccountOutcome outcome, int kFactor)
        {
            double expected = 1.0 / (1.0 + Math.Pow(10.0, (opponentRating - ownRating) / 400.0));
            double scored   = outcome switch
            {
                MatchAccountOutcome.Win  => 1.0,
                MatchAccountOutcome.Loss => 0.0,
                _                        => 0.5,
            };

            double delta = kFactor * (scored - expected);

            return (int)Math.Round(delta, MidpointRounding.AwayFromZero);
        }

    }
}
