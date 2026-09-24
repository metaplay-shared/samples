using Metaplay.Core;
using System.Collections.Generic;

namespace Game.Logic
{
    /// <summary>
    /// The one interactive resolution: a peek at the top of a seat's own deck, keeping part of it. It holds
    /// the effect queue for that seat alone, and nobody else may act meanwhile — the queue is held, not
    /// abandoned (<c>Docs/effects.md</c>, "The one interactive resolution").
    /// <para>
    /// <em>What</em> was revealed is secret and lives in <see cref="SeatSecrets"/>. The counts, and which
    /// indices the seat kept, are public and live on <see cref="PendingEffectChoice"/> and in the action's
    /// payload.
    /// </para>
    /// </summary>
    public static class ChoiceRules
    {
        /// <summary>
        /// Hold the queue for one seat's choice over the top of its own deck. The revealed cards stay where
        /// they are — they moved nowhere and are not public, so they must not touch the unseen pool — and
        /// travel to their owner as <see cref="MatchOwnPeekRevealed"/>.
        /// <para>
        /// How many can be revealed is <see cref="SeatState.DeckCount"/>, which is public, so both sides
        /// hold the same <see cref="PendingEffectChoice"/>. Which cards they are is the server's alone.
        /// </para>
        /// </summary>
        public static void PeekDeck(MatchModel match, int seat, int lookCount, int keepCount)
        {
            int deckCount = match.Rules.Seat(seat).DeckCount;
            int count     = lookCount < deckCount ? lookCount : deckCount;

            if (count <= 0)
                return;

            int keep = keepCount < count ? keepCount : count;

            SecretOps.WritePeekRevealed(match, seat, SecretOps.RevealTopOfDeck(match, seat, count));

            match.Rules.SetPendingChoice(new PendingEffectChoice(match.Rules.RaiseChoice(), seat, count, keep));
            match.Emit(new EffectChoiceRequestedEvent(seat, count, keep));

            match.Pacing.SuspendDeadline();
            ArmChoiceDeadline(match);
        }

        /// <summary>
        /// Give the held resolution a deadline. A host that arms turn deadlines but configured no choice
        /// deadline inherits the one already in force rather than having it cleared out from under it: an
        /// <see cref="MatchPendingKind.AwaitingEffectChoice"/> with nothing to expire it is a table nobody
        /// can ever unstick, and the one interactive resolution must never be able to do that.
        /// </summary>
        static void ArmChoiceDeadline(MatchModel match)
        {
            int          seat     = match.Rules.PendingChoice.Seat;
            MetaDuration duration = match.Timings.EffectChoiceDeadline;

            if (duration > MetaDuration.Zero)
            {
                match.Pacing.ArmDeadline(MatchDeadlineKind.EffectChoice, match.CurrentTime + duration, seat);
                return;
            }

            if (match.Pacing.DeadlineAt.HasValue)
            {
                match.Pacing.ArmDeadline(MatchDeadlineKind.EffectChoice, match.Pacing.DeadlineAt.Value, seat);
                return;
            }

            // Nothing was in force to inherit, which is the all-zero timings a test or the self-play runner
            // uses. There is no host timer to stall.
            match.Pacing.ClearDeadline();
        }

        /// <summary>
        /// The public half of the choice gate: a resolution is held, on this seat, it is the choice the answer
        /// names, and the answer keeps no more than it may.
        /// </summary>
        public static MatchIntentResult CheckApply(MatchModel match, int seat, int choiceId, List<int> keep)
        {
            MatchIntentResult common = TurnRules.CheckCommon(match, seat, isTheChoiceItself: true);
            if (!common.IsSuccess)
                return common;

            PendingEffectChoice pending = match.Rules.PendingChoice;
            if (pending == null)
                return MatchIntentResults.NoChoicePending;

            if (pending.Seat != seat)
                return MatchIntentResults.NotYourTurn;

            // Indices mean something only within one reveal, so an answer to an earlier one is refused rather
            // than applied to this one.
            if (pending.Id != choiceId)
                return MatchIntentResults.StaleChoice;

            if (keep == null || keep.Count > pending.KeepCount)
                return MatchIntentResults.InvalidChoice;

            // An answer names indices in the reveal, and how many were revealed is public — so the whole of
            // this check is public, and the secret is never asked whether the answer is a legal one.
            for (int ndx = 0; ndx < keep.Count; ndx++)
            {
                if (keep[ndx] < 0 || keep[ndx] >= pending.RevealedCount)
                    return MatchIntentResults.InvalidChoice;

                for (int other = 0; other < ndx; other++)
                {
                    if (keep[other] == keep[ndx])
                        return MatchIntentResults.InvalidChoice;
                }
            }

            return MatchIntentResults.Success;
        }

        /// <summary>
        /// Apply the choice and resume the resolution.
        /// <para>
        /// <b>The public loop decides how many kept cards fit and the secret side decides which they are.</b>
        /// That order is what keeps a hidden input out of a public mutation: every branch below reads
        /// <see cref="SeatState.HandCount"/> and the payload's kept count, and the one number that crosses
        /// back into the secret is <c>keptToHand</c>, which this loop computed. A kept card that finds no
        /// room goes to the deck bottom exactly as a bounce would, and because a hand only grows, the
        /// kept cards that overflow are the trailing ones in reveal order.
        /// </para>
        /// </summary>
        public static void Apply(MatchModel match, int seat, List<int> keep, bool wasDefaulted)
        {
            SeatState state      = match.Rules.Seat(seat);
            int       keptToHand = 0;

            for (int ndx = 0; ndx < keep.Count; ndx++)
            {
                if (state.HandCount >= match.Content.Global.MaxHandSize)
                {
                    // No room: it goes to the bottom with the ones the seat declined. The counts do not move,
                    // because the card was in the deck and stays in it.
                    match.Emit(new DrawOverflowedEvent(seat, state.HandCount, state.DeckCount));
                    continue;
                }

                state.SetCounts(state.HandCount + 1, state.DeckCount - 1);
                keptToHand++;
                match.Emit(new CardAddedToHandEvent(seat, state.HandCount, CardInstanceId.None, null));
            }

            SecretOps.ApplyPeekChoice(match, seat, keptToHand, keep);
            SecretOps.ClearPeekRevealed(match, seat);

            match.Rules.SetPendingChoice(null);
            match.Emit(new EffectChoiceResolvedEvent(seat, keep.Count, wasDefaulted));

            // Exactly the deadline the pause displaced, at exactly the stamp it had. Re-arming a fresh turn
            // deadline here would hand the seat a whole second turn, repeatable by bouncing the peeker.
            match.Pacing.ResumeSuspendedDeadline();

            ResolutionRules.ResumeResolution(match);
        }
    }
}
