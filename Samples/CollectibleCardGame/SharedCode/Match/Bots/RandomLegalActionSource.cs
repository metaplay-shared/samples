using Metaplay.Core;
using System.Collections.Generic;

namespace Game.Logic
{
    /// <summary>
    /// The sharpest instrument in the project for the rules engine: it draws uniformly from the legal set and
    /// does not run the scoring procedure at all (<c>Docs/bots.md</c>, "Personalities"). That is
    /// deliberate — its whole job is to reach the states a correct-looking scorer never walks into, so it must
    /// not depend on the scorer being correct.
    /// <para>
    /// A test instrument, not a personality a player meets. Like <see cref="BotPolicy"/> it is a pure function
    /// of (seat view, seed): the stream is derived from the decision's own coordinates rather than carried
    /// across calls, so the same state replays to the same action and self-play's determinism invariant
    /// holds for it too.
    /// </para>
    /// </summary>
    public sealed class RandomLegalActionSource : IActionSource
    {
        readonly ulong _seed;

        /// <summary>
        /// The config is taken and not kept: every question this source asks is of the seat view and the
        /// shared rules, and none of the catalogue. The harness builds every action source through one
        /// factory shape, and that shape passes it.
        /// </summary>
        public RandomLegalActionSource(SharedGameConfig config, ulong seed)
        {
            _seed = seed;
        }

        public MatchIntent ChooseAction(SeatView view, int seat)
        {
            int actionCount = view.Rules.ActionCount;

            if (view.PendingChoice != null)
                return ChooseKept(view, seat, actionCount);

            if (view.Rules.Phase == MatchPhase.Mulligan)
                return ChooseMulligan(view, seat, actionCount);

            IReadOnlyList<MatchIntent> legal = view.LegalActions;
            if (legal.Count == 0)
                return null;

            MatchIntent picked = legal[Stream(seat, BotDecisionKind.MainPhase, actionCount).NextInt(legal.Count)];
            return picked is EndTurnIntent ? null : picked;
        }

        MatchIntent ChooseMulligan(SeatView view, int seat, int actionCount)
        {
            if (view.LegalActions.Count == 0)
                return null;

            RandomPCG            rng     = Stream(seat, BotDecisionKind.Mulligan, actionCount);
            List<CardInstanceId> replace = new List<CardInstanceId>();

            foreach (HandCard card in view.Hand)
            {
                // Every card in hand during the mulligan is from the opening deal, so any may go back.
                if (rng.NextBool())
                    replace.Add(card.Instance);
            }

            return new MulliganIntent(replace);
        }

        MatchIntent ChooseKept(SeatView view, int seat, int actionCount)
        {
            PendingChoiceView pending = view.PendingChoice;
            RandomPCG         rng     = Stream(seat, BotDecisionKind.EffectChoice, actionCount);
            List<int>         keep    = new List<int>();

            for (int ndx = 0; ndx < pending.Revealed.Count; ndx++)
            {
                if (keep.Count >= pending.KeepCount)
                    break;
                if (rng.NextBool())
                    keep.Add(ndx);
            }

            return new EffectChoiceIntent(view.Rules.PendingChoice.Id, keep);
        }

        /// <summary>
        /// One stream per decision. A peek is answered at the same action count as the decision that would
        /// follow it, and <see cref="BotDecisionKind.EffectChoice"/> is what keeps the two from drawing from the
        /// same place.
        /// </summary>
        RandomPCG Stream(int seat, BotDecisionKind kind, int actionCount)
            => BotSubstreams.For(_seed, seat, kind, actionCount);
    }
}
