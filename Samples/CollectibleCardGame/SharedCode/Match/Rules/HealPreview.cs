using Metaplay.Core;
using System.Collections.Generic;

namespace Game.Logic
{
    /// <summary>
    /// What a heal card in hand would actually restore to a target the player is hovering over choosing. A
    /// heal is capped — a Den at its starting hit points takes nothing, a critter takes only the damage it
    /// carries — so a trick played at full health is legal, costs mana and does nothing visible. This is what
    /// the board shows on each legal target instead.
    /// <para>
    /// It is a <em>query</em>, not a rule: nothing here mutates anything, and the cap arithmetic is
    /// <see cref="ResolutionRules.HealableOnDen"/> and <see cref="ResolutionRules.HealableOnCritter"/> — the
    /// same two functions the resolution runs — so the number a player is shown and the number they get cannot
    /// disagree. Public state and the card in hand are its only inputs.
    /// </para>
    /// </summary>
    public static class HealPreview
    {
        /// <summary>
        /// How much this card, played from hand at this rank, would restore to this target. Null when there is
        /// nothing to preview: the card heals no chosen target, the target is gone, or the amount is not
        /// knowable ahead of the resolution (see <see cref="ChosenHealAmount"/>).
        /// </summary>
        public static int? ForTarget(MatchRulesState rules, GlobalConfig global, CardInfo card, int rank, EffectTargetRef target)
        {
            if (rules == null || global == null)
                return null;

            int? amount = ChosenHealAmount(card, rank);
            if (amount == null)
                return null;

            if (target.IsDen)
            {
                if (!MatchSeats.IsValid(target.DenSeat))
                    return null;

                return AtLeastNothing(ResolutionRules.HealableOnDen(rules.Seat(target.DenSeat).DenHp, amount.Value, global.DenStartingHp));
            }

            BoardCritter critter = ResolutionRules.FindCritterAnywhere(rules, target.Critter);
            if (critter == null)
                return null;

            return AtLeastNothing(ResolutionRules.HealableOnCritter(critter.Damage, amount.Value));
        }

        /// <summary>
        /// What the card's chosen-target heal adds up to at this rank: the literal amounts of its Hello's
        /// <c>Heal</c> steps aimed at the chosen target, plus whatever the rank track adds — which the engine
        /// applies to the first step of a card's Hello and nowhere else.
        /// <para>
        /// Null when a step's amount is not a plain number. A <c>Per:</c> counter is a board read the
        /// resolution does when it dequeues the step, and a preview that guessed at it could be wrong; no card
        /// in the pool has one on a chosen heal, and the board simply shows no number if one ever does.
        /// </para>
        /// </summary>
        public static int? ChosenHealAmount(CardInfo card, int rank)
        {
            if (card == null)
                return null;

            IReadOnlyList<MetaRef<EffectStepInfo>> steps = card.GetSteps(CardTrigger.Hello);
            if (steps.Count == 0)
                return null;

            int  bonus = card.GetStatsAtRank(rank).EffectAmountDelta;
            int  total = 0;
            bool heals = false;

            for (int ndx = 0; ndx < steps.Count; ndx++)
            {
                EffectStepInfo step = steps[ndx].Ref;
                if (step.Op != EffectOp.Heal || step.Target != EffectTargetKind.Chosen)
                    continue;

                if (step.Amount == null || !step.Amount.IsLiteralOnly)
                    return null;

                total += step.Amount.Literal + (ndx == 0 ? bonus : 0);
                heals  = true;
            }

            return heals ? total : (int?)null;
        }

        static int AtLeastNothing(int amount) => amount < 0 ? 0 : amount;
    }
}
