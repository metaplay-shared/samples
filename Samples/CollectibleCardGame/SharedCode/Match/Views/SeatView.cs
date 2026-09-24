using System.Collections.Generic;

namespace Game.Logic
{
    /// <summary>
    /// Everything one seat may reason from: the public rules state, the pacing, the timings, the roster, the
    /// stakes, that seat's own hand, and the legal actions the shared rules produce.
    /// <para>
    /// <b>It names the public members individually and never the model root</b>, which also carries
    /// <see cref="MatchModel.Secret"/>. A reflection test refuses a secret member on the view
    /// (<c>Docs/bots.md</c>).
    /// </para>
    /// </summary>
    public sealed class SeatView
    {
        public readonly int                        Seat;
        public readonly MatchRulesState            Rules;
        public readonly MatchPacing                Pacing;
        public readonly MatchTimings               Timings;
        public readonly IReadOnlyList<MatchSeat>   Seats;
        public readonly MatchStakes                Stakes;
        /// <summary> This seat's own hand. </summary>
        public readonly IReadOnlyList<HandCard>    Hand;
        /// <summary> What a held peek is showing this seat, or null. Separate from the hand: those cards are in the deck. </summary>
        public readonly PendingChoiceView          PendingChoice;
        /// <summary> Every action this seat may take right now, in the rules' own canonical enumeration order. </summary>
        public readonly IReadOnlyList<MatchIntent> LegalActions;
        /// <summary> What the host must answer next, so a policy can see a held peek without reading one. </summary>
        public readonly MatchPendingWork           Pending;

        public SeatView(int seat, MatchRulesState rules, MatchPacing pacing, MatchTimings timings, IReadOnlyList<MatchSeat> seats, MatchStakes stakes, IReadOnlyList<HandCard> hand, PendingChoiceView pendingChoice, IReadOnlyList<MatchIntent> legalActions, MatchPendingWork pending)
        {
            Seat         = seat;
            Rules        = rules;
            Pacing       = pacing;
            Timings      = timings;
            Seats        = seats;
            Stakes       = stakes;
            Hand          = hand;
            PendingChoice = pendingChoice;
            LegalActions = legalActions;
            Pending      = pending;
        }

        /// <summary>
        /// What a bot reasons over, built from the model. The <em>model</em> is not kept: only the public
        /// members it names, one by one.
        /// </summary>
        public static SeatView Build(MatchModel match, int seat)
            => new SeatView(
                seat,
                match.Rules,
                match.Pacing,
                match.Timings,
                match.Seats,
                match.Stakes,
                SecretOps.HandOf(match, seat),
                HandViews.BuildPendingChoice(match, seat),
                Legality.EnumerateForSeat(match, seat),
                MatchPending.Of(match));

        public SeatState Own      => Rules.Seat(Seat);
        public SeatState Opponent => Rules.Seat(MatchSeats.Other(Seat));

        /// <summary> The Weather in force, or null. </summary>
        public WeatherInfo Weather => Rules.Weather?.Ref;
    }
}
