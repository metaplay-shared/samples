using System.Collections.Generic;

namespace Game.Logic
{
    /// <summary>
    /// The mulligan: each seat swaps any part of its opening hand once, then the first turn opens.
    /// <para>
    /// A seat's swap happens when it submits. Which cards it named never reaches a payload; how many rides
    /// it, which is why <see cref="MulliganResolvedEvent"/> carries a count only. The swap draws from the
    /// seeded stream, so the order the two seats answer in decides the order they consume it — harmless,
    /// because the seed is secret and gives neither player a lever, and the timeline fixes that order for
    /// every replay.
    /// </para>
    /// </summary>
    public static class MulliganRules
    {
        /// <summary>
        /// The one check on a mulligan, run by <c>MulliganIntent.Prepare</c> on the server: the phase, this
        /// seat not having answered yet, and then every named card distinct and in this seat's hand. While the
        /// phase is open the hand holds only the opening deal, so any card in it may go back.
        /// </summary>
        public static MatchIntentResult CheckSubmit(MatchModel match, int seat, List<CardInstanceId> replace)
        {
            MatchIntentResult common = TurnRules.CheckCommon(match, seat, isTheChoiceItself: false);
            if (!common.IsSuccess)
                return common;

            if (match.Rules.Phase != MatchPhase.Mulligan)
                return MatchIntentResults.WrongPhase;

            if (match.Rules.Seat(seat).HasMulliganed)
                return MatchIntentResults.AlreadyMulliganed;

            if (replace == null)
                return MatchIntentResults.Success;

            for (int ndx = 0; ndx < replace.Count; ndx++)
            {
                for (int other = 0; other < ndx; other++)
                {
                    if (replace[other] == replace[ndx])
                        return MatchIntentResults.IllegalTarget;
                }

                if (!SecretOps.IsInHand(match, seat, replace[ndx]))
                    return MatchIntentResults.NotInYourHand;
            }

            return MatchIntentResults.Success;
        }

        /// <summary>
        /// Record one seat's answer, whose swap has already happened. When both have answered the first turn
        /// opens in the same action, because there is nothing left to wait for.
        /// </summary>
        public static void Submit(MatchModel match, int seat, int replaceCount)
        {
            SeatState state = match.Rules.Seat(seat);
            state.SetMulliganed();

            // The count only: which cards went back is secret, and the pool is unchanged either way.
            match.Emit(new MulliganResolvedEvent(seat, replaceCount, state.HandCount, state.DeckCount));

            if (match.Rules.Seat(0).HasMulliganed && match.Rules.Seat(1).HasMulliganed)
                Resolve(match);
        }

        /// <summary>
        /// End the phase: both seats answered, or the shared deadline lapsed. A seat that never submitted keeps
        /// its hand, and says so with a zero count. Then the second seat's compensation card, which is not a
        /// card the mulligan could ever have put back, and the first turn.
        /// </summary>
        public static void Resolve(MatchModel match)
        {
            for (int seat = 0; seat < MatchSeats.Count; seat++)
            {
                SeatState state = match.Rules.Seat(seat);
                if (!state.HasMulliganed)
                    match.Emit(new MulliganResolvedEvent(seat, 0, state.HandCount, state.DeckCount));
            }

            GrantSecondPlayerBonus(match, MatchSeats.Other(match.Rules.FirstSeat));

            match.Rules.SetPhase(MatchPhase.Playing);
            match.Rules.SetTurn(1);
            match.Rules.SetSeatOnTurn(match.Rules.FirstSeat);
            match.Pacing.ClearDeadline();

            TurnRules.StartTurn(match);
        }

        /// <summary>
        /// Mint the compensation card into the second seat's hand. It is public from this moment and never in
        /// the unseen pool: it was never in a deck, so it does not have to leave one. Everything the
        /// grant reads is public — the card comes from config, the seat from the first-seat decision — so a
        /// follower mints the same instance with the same identity without being told what it is.
        /// <para>
        /// It does <b>not</b> go through <see cref="ZoneOps.AddToHand"/>, so the full-hand rule — a card
        /// that will not fit goes to the bottom of the owner's deck — deliberately does not apply: a seat
        /// compensated for going second into a deck it cannot reach has not been compensated, and the Acorn
        /// was never in a deck to be returned to. That the hand always has room for it is therefore a
        /// content invariant rather than a runtime one, and <c>ContentValidator</c> is where it is enforced:
        /// it refuses a <c>MaxHandSize</c> that does not cover the second opening hand plus this card.
        /// </para>
        /// </summary>
        static void GrantSecondPlayerBonus(MatchModel match, int seat)
        {
            CardInfo bonus = match.Content.Global.SecondPlayerBonusCard?.Ref;
            if (bonus == null)
                return;

            CardInstance instance = ZoneOps.Mint(match, bonus, match.Content.Global.RankMin, seat, CardPlace.Hand, isPublic: true, fromStartingDeck: false);

            SeatState state = match.Rules.Seat(seat);
            SecretOps.AppendToHand(match, seat, instance.Id);
            state.SetCounts(state.HandCount + 1, state.DeckCount);
            match.Emit(new CardAddedToHandEvent(seat, state.HandCount, instance.Id, instance.CardId));
        }
    }
}
