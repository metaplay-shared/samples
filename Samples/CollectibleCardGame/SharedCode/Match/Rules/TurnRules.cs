using Metaplay.Core;
using System.Collections.Generic;

namespace Game.Logic
{
    /// <summary>
    /// The turn: its start-of-turn package, playing a card, ending it, and the reserve bank the turn deadline
    /// is extended out of. Plus the refusal checks the rule actions gate on, which is why the ordered prefix
    /// every intent shares is written once here (<see cref="CheckCommon"/>).
    /// </summary>
    public static class TurnRules
    {
        // ---------------------------------------------------------------- the refusal gate

        /// <summary>
        /// Everything that can refuse <em>any</em> intent, in a fixed order. There is no staleness check: an
        /// intent is judged against the state it arrives at, so the loser of a race is refused by legality — the
        /// card has left the hand, the critter has attacked, the turn has passed — and nothing can be played
        /// twice (<c>Docs/match.md</c>, "Legality settles every race").
        /// <para>
        /// Every check reads public state and the payload only, which is what lets the client's own legality
        /// walk reach the same verdict from the same code.
        /// </para>
        /// </summary>
        public static MatchIntentResult CheckCommon(MatchModel match, int seat, bool isTheChoiceItself)
        {
            MatchRulesState rules = match.Rules;

            if (!MatchSeats.IsValid(seat))
                return MatchIntentResults.IllegalTarget;

            if (rules.Phase == MatchPhase.Complete)
                return MatchIntentResults.WrongPhase;

            // A held resolution is exclusive: nobody acts inside it, and the one intent that answers it is
            // the choice itself.
            if (rules.PendingChoice != null && !isTheChoiceItself)
                return MatchIntentResults.ResolutionHeld;

            return MatchIntentResults.Success;
        }

        /// <summary> The prefix, plus "it is the playing phase and this seat's turn". </summary>
        public static MatchIntentResult CheckOnTurn(MatchModel match, int seat)
        {
            MatchIntentResult common = CheckCommon(match, seat, isTheChoiceItself: false);
            if (!common.IsSuccess)
                return common;

            if (match.Rules.Phase != MatchPhase.Playing)
                return MatchIntentResults.WrongPhase;

            if (match.Rules.SeatOnTurn != seat)
                return MatchIntentResults.NotYourTurn;

            return MatchIntentResults.Success;
        }

        /// <summary> Whether this seat may declare this attack right now. </summary>
        public static MatchIntentResult CheckAttack(MatchModel match, int seat, CardInstanceId attacker, EffectTargetRef target)
        {
            MatchIntentResult onTurn = CheckOnTurn(match, seat);
            if (!onTurn.IsSuccess)
                return onTurn;

            return Legality.CanAttack(match, seat, attacker, target);
        }

        /// <summary> Ending the turn is always legal for the seat on turn. </summary>
        public static MatchIntentResult CheckEndTurn(MatchModel match, int seat)
            => CheckOnTurn(match, seat);

        // ---------------------------------------------------------------- start of turn

        /// <summary>
        /// The start-of-turn package: ramp, refill, reset the per-turn counters, draw, wake, then resolve
        /// whatever that triggered. All rules-driven, and all inside the action that ended the previous turn.
        /// </summary>
        public static void StartTurn(MatchModel match)
        {
            int       seat  = match.Rules.SeatOnTurn;
            SeatState state = match.Rules.Seat(seat);

            match.Emit(new TurnStartedEvent(seat, match.Rules.Turn));

            ManaRules.RampAndRefill(match.Content.Global, state);
            match.Emit(new ManaChangedEvent(seat, state.Mana, state.MaxMana));

            state.SetTricksCastThisTurn(0);

            for (int ndx = 0; ndx < match.Content.Global.DrawsPerTurn; ndx++)
                ZoneOps.Draw(match, seat);

            WakeCritters(match, seat);

            ResolutionRules.EnqueueTurnTriggers(match, seat, CardTrigger.TurnStart, WeatherTrigger.TurnStart);
            ResolutionRules.ResolveQueue(match, ResolutionContinuation.FinishStartTurn);
        }

        /// <summary> Waking clears both being sleepy and having attacked. </summary>
        public static void WakeCritters(MatchModel match, int seat)
        {
            SeatState            state = match.Rules.Seat(seat);
            List<CardInstanceId> woke  = new List<CardInstanceId>();

            foreach (BoardCritter critter in state.Board)
            {
                if (critter.IsSleepy || critter.HasAttackedThisTurn)
                    woke.Add(critter.Id);

                critter.SetSleepy(false);
                critter.SetHasAttacked(false);
            }

            if (woke.Count > 0)
                match.Emit(new CrittersWokeEvent(seat, woke));
        }

        // ---------------------------------------------------------------- end of turn

        public static void EndTurn(MatchModel match)
        {
            int seat = match.Rules.SeatOnTurn;

            // The event comes after the end-of-turn effects have resolved, not before them: a client that
            // closed the turn on this event would otherwise close it over a resolution still in flight. It is
            // emitted in AdvanceToNextTurn, which is where the drain lands.
            ResolutionRules.EnqueueTurnTriggers(match, seat, CardTrigger.TurnEnd, WeatherTrigger.TurnEnd);
            ResolutionRules.ResolveQueue(match, ResolutionContinuation.FinishEndTurn);
        }

        /// <summary> Hand the board over and run the next seat's start-of-turn package. </summary>
        public static void AdvanceToNextTurn(MatchModel match)
        {
            match.Emit(new TurnEndedEvent(match.Rules.SeatOnTurn, match.Rules.Turn));

            match.Rules.SetTurn(match.Rules.Turn + 1);
            match.Rules.SetSeatOnTurn(MatchSeats.Other(match.Rules.SeatOnTurn));
            match.Pacing.ClearDeadline();
            StartTurn(match);
        }

        // ---------------------------------------------------------------- playing a card

        /// <summary>
        /// Play one card from a hand. This is the only rule that turns a hidden identity into a public one,
        /// and it does it from the action's own payload: <paramref name="revealedCard"/> and
        /// <paramref name="revealedRank"/> are what the server read out of the secret before it authored the
        /// action, so a follower reveals the same card without ever having known it
        /// (<c>Docs/hidden-information.md</c>).
        /// </summary>
        public static void PlayCard(MatchModel match, int seat, CardInstanceId cardId, CardId revealedCard, int revealedRank, EffectTargetRef target)
        {
            SeatState    state    = match.Rules.Seat(seat);
            CardInstance instance = match.Rules.Instance(cardId);

            // The identity, from the payload. After this the registry holds the answer on both sides.
            ZoneOps.Reveal(match, instance, revealedCard, revealedRank);

            CardInfo card = instance.Info;
            int      cost = ManaRules.CostToPlay(match.Content, match.Rules.Weather?.Ref, card, instance.Rank, state.TricksCastThisTurn);
            state.SetMana(state.Mana - cost);

            if (card.Type == CardType.Trick)
                state.SetTricksCastThisTurn(state.TricksCastThisTurn + 1);

            match.Emit(new CardPlayedEvent(seat, instance.Id, instance.CardId, instance.Rank, target, cost));
            match.Emit(new ManaChangedEvent(seat, state.Mana, state.MaxMana));

            // Out of the hand. The card was in a hidden hand or a public one; the count is the same either
            // way and the secret list is SecretOps' to keep in step.
            SecretOps.RemoveFromHiddenZones(match, instance.Id);
            state.SetCounts(state.HandCount - 1, state.DeckCount);
            instance.SetPlace(CardPlace.Limbo);

            // The card is public from the moment it is played, and it is on the seat's played lists from the
            // same moment — which is what the Heist reads and what the public counters count.
            ZoneOps.MakePublic(match, instance);
            state.PlayedThisMatch.Add(instance.CardId);
            if (instance.FromStartingDeck)
                state.PlayedFromOwnDeck.Add(new HeistEligibleCard(instance.CardId, instance.Rank));

            CardInstanceSnapshot source;
            if (card.Type == CardType.Critter)
            {
                BoardCritter critter = ZoneOps.ToBoard(match, instance);
                source = CardInstanceSnapshot.OfCritter(critter, seat);
            }
            else
            {
                // A trick's whole body is its Hello, resolved on cast, after which the card goes to the
                // graveyard.
                source = CardInstanceSnapshot.OfCard(instance.Id, seat);
                ZoneOps.ToGraveyard(match, instance);
            }

            ResolutionRules.EnqueueCardSteps(match, instance, CardTrigger.Hello, seat, source, target, excludesSourceFromPlayedCount: true);
            ResolutionRules.ResolveQueue(match, ResolutionContinuation.FinishAction);
        }

        // ---------------------------------------------------------------- the reserve

        /// <summary>
        /// What is left of one seat's turn-reserve bank. Derived rather than stored: the bank's size is a
        /// public timing and the spend is public rules state, so a second stored mirror would be a second
        /// answer.
        /// </summary>
        public static MetaDuration ReserveRemaining(MatchModel match, int seat)
        {
            MetaDuration remaining = match.Timings.TurnReserveBank - match.Rules.TurnReserveSpent[seat];
            return remaining < MetaDuration.Zero ? MetaDuration.Zero : remaining;
        }

        /// <summary>
        /// Whether this seat may draw one extension out of its reserve. The reserve is only drawn by a seat
        /// that is <em>doing</em> something: a seat that has sat still for the whole deadline gets no
        /// extension, because the reserve exists for the player deep in a puzzle rather than as a second
        /// helping of somebody else's patience (<c>Docs/match.md</c>).
        /// <para>
        /// A pure query over public state, so the host routes on it rather than deciding for itself: a host
        /// that decided would be a second implementation of the gating rule, and two of those disagree.
        /// </para>
        /// </summary>
        public static bool CanExtendFromReserve(MatchModel match, int seat)
        {
            if (match.Rules.Phase != MatchPhase.Playing)
                return false;

            if (!match.Rules.ActedThisTurn)
                return false;

            MetaDuration extension = match.Timings.TurnReserveExtension;
            return extension > MetaDuration.Zero && ReserveRemaining(match, seat) >= extension;
        }

        public static void ExtendFromReserve(MatchModel match, int seat)
        {
            MetaDuration extension = match.Timings.TurnReserveExtension;
            match.Rules.SpendTurnReserve(seat, extension);

            if (match.Pacing.DeadlineAt.HasValue)
                match.Pacing.PushDeadline(extension);
            else
                match.Pacing.ArmDeadline(MatchDeadlineKind.Turn, match.CurrentTime + extension, seat);
        }
    }
}
