using Game.Logic;
using System.Collections.Generic;

namespace Game.Server.Matchmaking
{
    /// <summary>
    /// The profile of the bot the fill wait hands out (<c>Docs/matchmaking.md</c>, "The fill wait and
    /// the bot fallback"). Its deck is <see cref="Game.Server.Match.BotDecks"/>'.
    /// </summary>
    public static class FallbackBotPolicy
    {
        /// <summary>
        /// Which personality the fallback draws, from the waiter's rating; the player does not pick
        /// (<c>Docs/bots.md</c>, "Personalities"). Three buckets over the profiles that make mistakes,
        /// split around the rating seed at the schedule's widest finite rating band.
        /// </summary>
        public static BotProfileId ProfileFor(int waiterRating, GlobalConfig global, MatchmakingBandSchedule schedule)
        {
            int band = WidestFiniteRatingBand(schedule);

            if (waiterRating < global.InitialRating - band)
                return BotProfileId.Sloppy;
            if (waiterRating > global.InitialRating + band)
                return BotProfileId.Practiced;

            return BotProfileId.Casual;
        }

        /// <summary> The widest rating band the schedule bounds at all, or the first step's when none does. </summary>
        static int WidestFiniteRatingBand(MatchmakingBandSchedule schedule)
        {
            int widest = 0;
            foreach (MatchmakingBandStep step in schedule.Steps)
            {
                if (step.RatingBand.HasValue && step.RatingBand.Value > widest)
                    widest = step.RatingBand.Value;
            }

            return widest;
        }
    }
}
