using Metaplay.Core;
using System.Collections.Generic;

namespace Game.Logic
{
    public sealed partial class BotPolicy
    {
        /// <summary>
        /// The one action to take when this turn can put the enemy Den at zero, or null when it cannot.
        /// <para>
        /// It builds a plan — the Den-facing damage this turn's mana can buy, the Guards that have to come down
        /// first, and the swings that are left over — and then returns the first step of it. Nothing is carried
        /// between calls: every action changes the board, so the condition is simply re-derived on the next
        /// call, which is what lets a greedy policy find a multi-action lethal with no planning state.
        /// </para>
        /// <para>
        /// It only ever <em>under</em>-counts. A plan that over-counted would commit the turn to a line that
        /// does not finish, which is worse than missing the lethal — so a source of damage this cannot compute
        /// exactly is left out of the plan rather than estimated into it.
        /// </para>
        /// </summary>
        MatchIntent FindLethalAction(SeatView view, int seat, IReadOnlyList<MatchIntent> legal)
        {
            SeatState own   = view.Own;
            SeatState enemy = view.Opponent;

            if (enemy.DenHp <= 0)
                return null;

            // 1. Den-facing damage from plays. Only legal ones count, so affordability and targeting stay the
            //    rules' business rather than a second opinion about them.
            List<LethalBurn> burns = new List<LethalBurn>();
            for (int ndx = 0; ndx < legal.Count; ndx++)
            {
                if (!(legal[ndx] is PlayCardIntent play))
                    continue;

                HandCard? found = FindInHand(view.Hand, play.Card);
                if (found == null)
                    continue;

                HandCard held = found.Value;

                int damage = DenDamageOnPlay(view, seat, Card(held.Card), held.Rank, play.Target);
                if (damage > 0)
                    burns.Add(new LethalBurn(ndx, damage, CostOf(view, seat, held), Card(held.Card).GetStatsAtRank(held.Rank).Cost, held.Instance));
            }

            // Most damage first, cheapest first within that, and the enumeration order last: a seedless
            // ordering, so two seats in the same position plan the same turn.
            burns.Sort(LethalBurn.Compare);

            int                  mana      = own.Mana;
            int                  burnTotal = 0;
            int                  firstBurn = -1;
            List<CardInstanceId> spent     = new List<CardInstanceId>();

            for (int ndx = 0; ndx < burns.Count; ndx++)
            {
                LethalBurn burn = burns[ndx];
                // The legal set holds one intent per (card, target) pair, and a card is played once.
                if (spent.Contains(burn.Card))
                    continue;

                // The cost the seat view reports is what this card costs as the *next* thing played, and a
                // Weather that discounts the first trick of a turn will not discount the second. Only the
                // first card in the plan may take that price; everything after it pays the higher of the two,
                // so a plan is never declared affordable when it is not.
                int cost = spent.Count == 0 ? burn.Cost : (burn.Cost > burn.UndiscountedCost ? burn.Cost : burn.UndiscountedCost);
                if (cost > mana)
                    continue;

                mana      -= cost;
                burnTotal += burn.Damage;
                spent.Add(burn.Card);

                if (firstBurn < 0)
                    firstBurn = burn.LegalIndex;
            }

            // 2. The critters that can still swing, smallest first, so the Guards are cleared with the least
            //    attack that does the job and the biggest bodies are left pointing at the Den.
            List<BoardCritter> ready = new List<BoardCritter>();
            for (int ndx = 0; ndx < own.Board.Count; ndx++)
            {
                if (CanAttackNow(own.Board[ndx]))
                    ready.Add(own.Board[ndx]);
            }

            ready.Sort(CompareBySwingCost);

            // 3. Every Guard that shuts the Den has to die before any of it reaches the Den: two Guards are
            //    one gate and two bodies.
            List<BoardCritter> gates = new List<BoardCritter>();
            for (int ndx = 0; ndx < enemy.Board.Count; ndx++)
            {
                if (Gates(enemy.Board[ndx]))
                    gates.Add(enemy.Board[ndx]);
            }

            BoardCritter clearAttacker = null;
            BoardCritter clearTarget   = null;

            for (int gateNdx = 0; gateNdx < gates.Count; gateNdx++)
            {
                BoardCritter gate    = gates[gateNdx];
                int              needed  = gate.CurrentHealth;
                bool             bubble  = (gate.Keywords & KeywordFlags.Bubble) != 0 && gate.BubbleIntact;
                int              dealt   = 0;
                int              used    = 0;

                while (used < ready.Count && dealt < needed)
                {
                    if (bubble)
                    {
                        // The Bubble swallows the whole first instance, so it costs a body and buys nothing.
                        bubble = false;
                        used++;
                        continue;
                    }

                    dealt += ready[used].Attack;
                    used++;
                }

                if (dealt < needed)
                    return LethalByBurnAlone(legal, firstBurn, burnTotal, RequiredDamage(view, enemy.DenHp, deathsInPlan: 0));

                if (clearAttacker == null)
                {
                    clearAttacker = ready[0];
                    clearTarget   = gate;
                }

                ready.RemoveRange(0, used);
            }

            // 4. What is left points at the Den. An attacker's own OnAttack Den damage lands whichever way it
            //    swung, so the ones spent on Guards contribute too — but only that, never a Goodbye's, which
            //    depends on dying.
            int reachable = burnTotal;
            for (int ndx = 0; ndx < ready.Count; ndx++)
                reachable += ready[ndx].Attack;

            for (int ndx = 0; ndx < own.Board.Count; ndx++)
            {
                if (CanAttackNow(own.Board[ndx]))
                    reachable += DenDamageOnAttack(view.Rules.Instance(own.Board[ndx].Id).Info);
            }

            // Every Guard in the plan dies, and under a Weather that heals a Den when a critter dies, each of
            // those deaths gives the Den back some of what the swing is about to take. It resolves before the
            // Den is checked, so it is part of what has to be got through rather than a rounding error.
            if (reachable < RequiredDamage(view, enemy.DenHp, gates.Count))
                return null;

            // The plan is lethal. Its first step: the burn, then the Guard that is in the way, then the swing.
            if (firstBurn >= 0)
                return legal[firstBurn];

            if (clearAttacker != null)
                return FindAttack(legal, clearAttacker.Id, EffectTargetRef.OnCritter(clearTarget.Id));

            for (int ndx = ready.Count - 1; ndx >= 0; ndx--)
            {
                MatchIntent swing = FindAttack(legal, ready[ndx].Id, EffectTargetRef.Den(MatchSeats.Other(seat)));
                if (swing != null)
                    return swing;
            }

            return null;
        }

        /// <summary> Lethal with no attack in it at all, which is the only kind an unclearable Guard leaves. </summary>
        static MatchIntent LethalByBurnAlone(IReadOnlyList<MatchIntent> legal, int firstBurn, int burnTotal, int required)
            => firstBurn >= 0 && burnTotal >= required ? legal[firstBurn] : null;

        /// <summary>
        /// How much has to reach the enemy Den for it to be at zero when the Den is next checked: its hit
        /// points, plus whatever the Weather gives it back for each enemy critter the plan kills on the way.
        /// </summary>
        int RequiredDamage(SeatView view, int denHp, int deathsInPlan)
        {
            if (deathsInPlan <= 0)
                return denHp;

            WeatherInfo weather = WeatherOf(new Situation(view, view.Seat));
            if (weather == null || weather.Trigger != WeatherTrigger.CritterDies)
                return denHp;

            int perDeath = 0;
            foreach (MetaRef<EffectStepInfo> step in weather.Steps)
            {
                // Resolved as the dying critter's owner, so an OwnDen heal is the enemy's Den healing.
                if (step.Ref.Op == EffectOp.Heal && step.Ref.Target == EffectTargetKind.OwnDen && step.Ref.Amount != null && step.Ref.Amount.IsLiteralOnly)
                    perDeath += step.Ref.Amount.Literal;
            }

            return denHp + deathsInPlan * perDeath;
        }

        static MatchIntent FindAttack(IReadOnlyList<MatchIntent> legal, CardInstanceId attacker, EffectTargetRef target)
        {
            for (int ndx = 0; ndx < legal.Count; ndx++)
            {
                if (legal[ndx] is AttackIntent attack && attack.Attacker == attacker && attack.Target == target)
                    return attack;
            }

            return null;
        }

        /// <summary> Smallest swing first, identity last, so the ordering is total and seedless. </summary>
        static int CompareBySwingCost(BoardCritter a, BoardCritter b)
        {
            int byAttack = a.Attack.CompareTo(b.Attack);
            if (byAttack != 0)
                return byAttack;

            return a.Id.CompareTo(b.Id);
        }

        /// <summary> How much of this card's Hello lands on the enemy Den if it is played at this target. </summary>
        int DenDamageOnPlay(SeatView view, int seat, CardInfo card, int rank, EffectTargetRef chosen)
        {
            Situation situation  = new Situation(view, seat);
            int       amountDelta = card.GetStatsAtRank(rank).EffectAmountDelta;
            int       enemySeat   = MatchSeats.Other(seat);

            IReadOnlyList<MetaRef<EffectStepInfo>> steps = card.Hello;
            int damage = 0;

            for (int ndx = 0; ndx < steps.Count; ndx++)
            {
                EffectStepInfo step = steps[ndx].Ref;
                if (step.Op != EffectOp.Damage)
                    continue;

                int amount = EvaluateAmount(situation, step, step.Amount, ndx == 0 ? amountDelta : 0);
                foreach (EffectTargetRef target in Expand(situation, step.Target, chosen, CardInstanceId.None))
                {
                    if (target.IsDen && target.DenSeat == enemySeat)
                        damage += amount;
                }
            }

            return damage;
        }

        /// <summary>
        /// How much of a critter's OnAttack lands on the enemy Den. Only the literal, Den-addressed part: a
        /// counter would be exact too, but a rank track never scales an OnAttack step, so there is nothing else
        /// to add.
        /// </summary>
        static int DenDamageOnAttack(CardInfo card)
        {
            IReadOnlyList<MetaRef<EffectStepInfo>> steps = card.OnAttack;
            int damage = 0;

            for (int ndx = 0; ndx < steps.Count; ndx++)
            {
                EffectStepInfo step = steps[ndx].Ref;
                if (step.Op == EffectOp.Damage && step.Target == EffectTargetKind.EnemyDen && step.Amount != null && step.Amount.IsLiteralOnly)
                    damage += step.Amount.Literal;
            }

            return damage;
        }

        /// <summary> One affordable play that puts damage on the enemy Den. </summary>
        readonly struct LethalBurn
        {
            public readonly int            LegalIndex;
            public readonly int            Damage;
            /// <summary> What it costs as the next card played, Weather applied. </summary>
            public readonly int            Cost;
            /// <summary> Its printed cost at its rank, which is what it costs once a per-turn discount is spent. </summary>
            public readonly int            UndiscountedCost;
            public readonly CardInstanceId Card;

            public LethalBurn(int legalIndex, int damage, int cost, int undiscountedCost, CardInstanceId card)
            {
                LegalIndex       = legalIndex;
                Damage           = damage;
                Cost             = cost;
                UndiscountedCost = undiscountedCost;
                Card             = card;
            }

            public static int Compare(LethalBurn a, LethalBurn b)
            {
                int byDamage = b.Damage.CompareTo(a.Damage);
                if (byDamage != 0)
                    return byDamage;

                int byCost = a.Cost.CompareTo(b.Cost);
                if (byCost != 0)
                    return byCost;

                return a.LegalIndex.CompareTo(b.LegalIndex);
            }
        }
    }
}
