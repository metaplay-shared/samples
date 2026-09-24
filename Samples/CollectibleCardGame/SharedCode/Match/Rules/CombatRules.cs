using System.Collections.Generic;

namespace Game.Logic
{
    /// <summary>
    /// Combat: the order things are collected in, and the exchange itself.
    /// </summary>
    public static class CombatRules
    {
        /// <summary>
        /// Collect the critters that are dead, in canonical order: the active seat's board ascending, then the
        /// other seat's. Anything else makes "who died first" depend on which local variable the
        /// implementation happened to write to first — a determinism bug that only shows up once a Goodbye
        /// starts mattering.
        /// </summary>
        public static void CollectDead(MatchRulesState rules, List<CardInstanceId> deadOut, List<int> deadSeatsOut)
        {
            deadOut.Clear();
            deadSeatsOut.Clear();

            int active = rules.SeatOnTurn;
            CollectDeadOnBoard(rules.Seat(active), active, deadOut, deadSeatsOut);

            int other = MatchSeats.Other(active);
            CollectDeadOnBoard(rules.Seat(other), other, deadOut, deadSeatsOut);
        }

        static void CollectDeadOnBoard(SeatState state, int seat, List<CardInstanceId> deadOut, List<int> deadSeatsOut)
        {
            IReadOnlyList<BoardCritter> board = state.Board;
            for (int ndx = 0; ndx < board.Count; ndx++)
            {
                if (board[ndx].IsDead)
                {
                    deadOut.Add(board[ndx].Id);
                    deadSeatsOut.Add(seat);
                }
            }
        }

        // ---------------------------------------------------------------- the exchange

        /// <summary>
        /// One attack, in a fixed order with no exceptions. Both damages are computed before either is
        /// applied and both are applied before anything is checked for death, so "who died first" never
        /// depends on which local variable the implementation happened to write to first.
        /// <para>
        /// The keyword interactions that fall out of simultaneity — a dying attacker whose damage still
        /// landed and still healed, a Bubble that pops without absorbing a second hit, a Sneaky critter that
        /// stops being sneaky the instant it connects — are consequences of this ordering rather than special
        /// cases in it.
        /// </para>
        /// <para>
        /// Every value it reads is public: two boards, their critters' numbers and their keywords. Nothing
        /// about an attack is hidden, which is why combat needed no split at all.
        /// </para>
        /// </summary>
        public static void ApplyAttack(MatchModel match, int seat, CardInstanceId attackerId, EffectTargetRef target)
        {
            int          enemySeat = MatchSeats.Other(seat);
            BoardCritter attacker  = match.Rules.Seat(seat).FindCritter(attackerId);

            attacker.SetHasAttacked(true);
            match.Emit(new AttackDeclaredEvent(attacker.Id, target));

            // 1. Both amounts up front. The Den never counterattacks.
            BoardCritter defender     = target.IsDen ? null : match.Rules.Seat(enemySeat).FindCritter(target.Critter);
            int          fromAttacker = attacker.Attack;
            int          fromDefender = defender != null ? defender.Attack : 0;

            // 2. Both applications, before any death check.
            ResolutionRules.ApplyDamage(match, target, fromAttacker, attacker.Id);
            if (defender != null)
                ResolutionRules.ApplyDamage(match, EffectTargetRef.OnCritter(attacker.Id), fromDefender, defender.Id);

            // 3. Snacktime, including on a participant that is about to die: its damage still landed.
            ApplySnacktime(match, attacker, seat, fromAttacker);
            if (defender != null)
                ApplySnacktime(match, defender, enemySeat, fromDefender);

            // 4. Sneaky drops after the exchange, so a Sneaky attacker still took the counterattack.
            RevealIfSneaky(match, attacker, fromAttacker);
            if (defender != null)
                RevealIfSneaky(match, defender, fromDefender);

            // 5. Deaths, then the attacker's own Attack trigger, which fires even if it died attacking.
            ResolutionRules.SweepDeaths(match);
            CardInstance attackerInstance = match.Rules.Instance(attacker.Id);
            ResolutionRules.EnqueueCardSteps(match, attackerInstance, CardTrigger.OnAttack, seat, CardInstanceSnapshot.OfCritter(attacker, seat), EffectTargetRef.None, excludesSourceFromPlayedCount: false);

            ResolutionRules.ResolveQueue(match, ResolutionContinuation.FinishAction);
        }

        public static void ApplySnacktime(MatchModel match, BoardCritter dealer, int owner, int amountDealt)
        {
            int heal = KeywordRules.SnacktimeHeal(dealer, amountDealt);
            if (heal > 0)
                ResolutionRules.HealDen(match, owner, heal);
        }

        public static void RevealIfSneaky(MatchModel match, BoardCritter critter, int amountDealt)
        {
            if (KeywordRules.RevealsSneaky(critter, amountDealt))
                RevealSneaky(match, critter);
        }

        public static void RevealSneaky(MatchModel match, BoardCritter critter)
        {
            critter.RemoveKeywords(KeywordFlags.Sneaky);
            match.Emit(new SneakyRevealedEvent(critter.Id));
            match.Emit(new KeywordsChangedEvent(critter.Id, critter.Keywords));
        }
    }
}
