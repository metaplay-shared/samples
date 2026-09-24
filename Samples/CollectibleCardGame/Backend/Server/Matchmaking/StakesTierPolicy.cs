using Game.Logic;
using System;

namespace Game.Server.Matchmaking
{
    /// <summary> One seat's inputs to the stakes tier, as a unit, so the two seats cannot be half-swapped. </summary>
    public readonly struct StakesSeatFacts
    {
        public readonly int  PowerScore;
        public readonly int  RankedMatchesPlayed;
        public readonly bool NewcomerShieldWaived;

        public StakesSeatFacts(int powerScore, int rankedMatchesPlayed, bool newcomerShieldWaived)
        {
            PowerScore           = powerScore;
            RankedMatchesPlayed  = rankedMatchesPlayed;
            NewcomerShieldWaived = newcomerShieldWaived;
        }

        /// <summary> The seat's inputs off the ticket it queued with, which is where all three were frozen. </summary>
        public static StakesSeatFacts OfTicket(MatchmakingTicket ticket)
            => new StakesSeatFacts(ticket.PowerScore, ticket.RankedMatchesPlayed, ticket.NewcomerShieldWaived);

        /// <summary>
        /// Whether the newcomer shield covers this seat. A waiver lifts this account's own shield and nothing
        /// else; the count it overrides is left exactly as the record has it.
        /// </summary>
        public bool IsShielded(GlobalConfig global)
            => !NewcomerShieldWaived && RankedMatchesPlayed < global.NewcomerShieldMatches;
    }

    /// <summary>
    /// What the match pays, decided once at formation from the two frozen tickets (<c>Docs/match.md</c>,
    /// "The Heist phase").
    /// </summary>
    public static class StakesTierPolicy
    {
        /// <summary>
        /// The tier for two humans, in precedence order. A bot table takes <see cref="StakesTier.Practice"/>
        /// at formation instead.
        /// <list type="number">
        /// <item><b>The newcomer shield beats the Power Score gap.</b> A shielded match's whole point is that
        /// no rank moves in either direction, which has to override what the gap alone would have implied
        /// rather than compose with it. A shield on either side shields the match, because what one player
        /// stands to win is what the other stands to lose.</item>
        /// <item><b>Otherwise the gap decides</b> Even against asymmetric, at game-design.md's threshold.</item>
        /// </list>
        /// <para>
        /// A waiver lifts one account's own shield; the other seat's shield still shields the match.
        /// </para>
        /// </summary>
        public static StakesTier ComputeTier(StakesSeatFacts seat0, StakesSeatFacts seat1, GlobalConfig global)
        {
            if (seat0.IsShielded(global) || seat1.IsShielded(global))
                return StakesTier.Shielded;

            int gap = seat0.PowerScore - seat1.PowerScore;
            if (Math.Abs(gap) <= global.PowerScoreGapThreshold)
                return StakesTier.Even;

            // Relative to seat 0, not the eventual winner, which formation cannot know. The Heist compares the
            // winner's seat against it (MatchHeistPolicy.SeatIsTheFavourite).
            return gap > 0 ? StakesTier.Favourite : StakesTier.Underdog;
        }
    }
}
