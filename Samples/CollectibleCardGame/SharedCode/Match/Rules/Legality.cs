using System.Collections.Generic;

namespace Game.Logic
{
    /// <summary>
    /// What a seat may do. <b>One implementation, one walk</b>, called by the rule actions' gates, by the bot
    /// policy when it picks a move, and by the client when it decides what to highlight.
    /// <para>
    /// The acting seat's hand is an <b>explicit argument</b> because the two callers hold it in different
    /// places and neither may reach into the other's: the server passes the seat's secret hand, a client
    /// passes the hand the private channel delivered. Everything else this reads is public and is on the
    /// model, so the two callers cannot disagree about it without the session ending.
    /// </para>
    /// <para>
    /// Everything here is a pure query. Nothing in this file mutates, and nothing in it reads the secret: the
    /// hand arrives as a parameter.
    /// </para>
    /// </summary>
    public static class Legality
    {
        // ---------------------------------------------------------------- playing a card

        /// <summary> Whether this seat may play this card from its hand at this target right now. </summary>
        public static MatchIntentResult CanPlayCard(MatchModel match, int seat, HandCard card, EffectTargetRef target)
        {
            if (!match.Content.Cards.TryGetValue(card.Card, out CardInfo info))
                return MatchIntentResults.UnknownInstance;

            CardInstance instance = match.Rules.TryGetInstance(card.Instance);
            if (instance == null)
                return MatchIntentResults.UnknownInstance;

            if (instance.Owner != seat || (instance.Place != CardPlace.Unseen && instance.Place != CardPlace.Hand))
                return MatchIntentResults.NotInYourHand;

            SeatState state = match.Rules.Seat(seat);
            if (ManaRules.CostToPlay(match.Content, match.Rules.Weather?.Ref, info, card.Rank, state.TricksCastThisTurn) > state.Mana)
                return MatchIntentResults.NotEnoughMana;

            if (info.Type == CardType.Critter && state.Board.Count >= match.Content.Global.MaxBoardCritters)
                return MatchIntentResults.BoardFull;

            return CheckChosenTarget(match, seat, info, target);
        }

        /// <summary>
        /// Whether the one cast-time choice a card asks for is a thing this seat may point at. The classes a
        /// card may ask for are <see cref="ChooseTargetKind"/>, and what each admits is
        /// <see cref="EffectVocabulary"/>'s to say — so the runtime address and the config vocabulary cannot
        /// drift apart about whether a Den is a legal answer.
        /// </summary>
        public static MatchIntentResult CheckChosenTarget(MatchModel match, int seat, CardInfo card, EffectTargetRef target)
        {
            ChooseTargetKind choose = card.ChooseTarget;

            if (choose == ChooseTargetKind.None)
                return target.IsNone ? MatchIntentResults.Success : MatchIntentResults.IllegalTarget;

            if (target.IsNone)
                return MatchIntentResults.IllegalTarget;

            if (target.IsDen)
            {
                if (!EffectVocabulary.ChoiceAdmitsDen(choose))
                    return MatchIntentResults.IllegalTarget;
                if (!MatchSeats.IsValid(target.DenSeat))
                    return MatchIntentResults.IllegalTarget;
                if (EffectVocabulary.ChoiceIsFriendlyOnly(choose) && target.DenSeat != seat)
                    return MatchIntentResults.IllegalTarget;

                return MatchIntentResults.Success;
            }

            CardInstance instance = match.Rules.TryGetInstance(target.Critter);
            if (instance == null)
                return MatchIntentResults.UnknownInstance;

            BoardCritter critter = match.Rules.Seat(instance.Owner).FindCritter(target.Critter);
            if (critter == null)
                return MatchIntentResults.IllegalTarget;

            bool isFriendly = instance.Owner == seat;

            if (EffectVocabulary.ChoiceIsFriendlyOnly(choose) && !isFriendly)
                return MatchIntentResults.IllegalTarget;
            if (choose == ChooseTargetKind.EnemyCritter && isFriendly)
                return MatchIntentResults.IllegalTarget;

            // Sneaky blocks targeting by the enemy only: a seat may always point at its own.
            if (!isFriendly && !KeywordRules.IsTargetableByEnemy(critter))
                return MatchIntentResults.TargetIsSneaky;

            return MatchIntentResults.Success;
        }

        // ---------------------------------------------------------------- attacking

        /// <summary> Whether this seat may declare this attack right now. Reads nothing but public state. </summary>
        public static MatchIntentResult CanAttack(MatchModel match, int seat, CardInstanceId attackerId, EffectTargetRef target)
        {
            MatchRulesState rules    = match.Rules;
            BoardCritter    attacker = rules.Seat(seat).FindCritter(attackerId);
            if (attacker == null)
                return MatchIntentResults.NotYourCritter;

            if (attacker.IsSleepy)
                return MatchIntentResults.CritterAsleep;

            if (attacker.HasAttackedThisTurn)
                return MatchIntentResults.CritterAlreadyAttacked;

            // A critter with nothing to swing has no attack to declare.
            if (attacker.Attack <= 0)
                return MatchIntentResults.CritterHasNoAttack;

            int enemySeat = MatchSeats.Other(seat);

            if (target.IsDen)
            {
                if (target.DenSeat != enemySeat)
                    return MatchIntentResults.IllegalTarget;
                if (KeywordRules.GuardGate(rules.Seat(enemySeat)))
                    return MatchIntentResults.DenProtectedByGuard;

                return MatchIntentResults.Success;
            }

            if (!target.IsCritter)
                return MatchIntentResults.IllegalTarget;

            CardInstance instance = rules.TryGetInstance(target.Critter);
            if (instance == null)
                return MatchIntentResults.UnknownInstance;

            // You may not attack your own.
            if (instance.Owner != enemySeat)
                return MatchIntentResults.IllegalTarget;

            BoardCritter defender = rules.Seat(enemySeat).FindCritter(target.Critter);
            if (defender == null)
                return MatchIntentResults.IllegalTarget;

            if (!KeywordRules.IsTargetableByEnemy(defender))
                return MatchIntentResults.TargetIsSneaky;

            return MatchIntentResults.Success;
        }

        // ---------------------------------------------------------------- the legal set

        /// <summary>
        /// Every action this seat may take right now, in one canonical order: the mulligan while that phase is
        /// open; otherwise plays in hand order with their legal targets, then attacks in board order with
        /// their legal targets, then ending the turn. The order is part of the contract — a policy that picks
        /// from this list is deterministic without having to sort anything itself.
        /// </summary>
        public static List<MatchIntent> EnumerateLegalIntents(MatchModel match, int seat, IReadOnlyList<HandCard> hand)
        {
            List<MatchIntent> intents = new List<MatchIntent>();
            MatchRulesState   rules   = match.Rules;

            if (rules.Phase == MatchPhase.Complete)
                return intents;

            if (rules.PendingChoice != null)
                return intents;

            if (rules.Phase == MatchPhase.Mulligan)
            {
                if (!rules.Seat(seat).HasMulliganed)
                    intents.Add(new MulliganIntent(new List<CardInstanceId>()));
                return intents;
            }

            if (rules.SeatOnTurn != seat)
                return intents;

            SeatState state     = rules.Seat(seat);
            int       enemySeat = MatchSeats.Other(seat);
            SeatState enemy     = rules.Seat(enemySeat);

            if (hand != null)
            {
                foreach (HandCard card in hand)
                {
                    if (!match.Content.Cards.TryGetValue(card.Card, out CardInfo info))
                        continue;

                    if (info.ChooseTarget == ChooseTargetKind.None)
                    {
                        if (CanPlayCard(match, seat, card, EffectTargetRef.None).IsSuccess)
                            intents.Add(new PlayCardIntent(card.Instance, EffectTargetRef.None));
                        continue;
                    }

                    foreach (EffectTargetRef candidate in EnumerateTargets(state, enemy, seat, enemySeat))
                    {
                        if (CanPlayCard(match, seat, card, candidate).IsSuccess)
                            intents.Add(new PlayCardIntent(card.Instance, candidate));
                    }
                }
            }

            foreach (BoardCritter attacker in state.Board)
            {
                foreach (BoardCritter defender in enemy.Board)
                {
                    EffectTargetRef target = EffectTargetRef.OnCritter(defender.Id);
                    if (CanAttack(match, seat, attacker.Id, target).IsSuccess)
                        intents.Add(new AttackIntent(attacker.Id, target));
                }

                EffectTargetRef den = EffectTargetRef.Den(enemySeat);
                if (CanAttack(match, seat, attacker.Id, den).IsSuccess)
                    intents.Add(new AttackIntent(attacker.Id, den));
            }

            intents.Add(new EndTurnIntent());
            return intents;
        }

        /// <summary>
        /// The server-side call: the legal set for one seat, with that seat's hand read out of the secret.
        /// <para>
        /// A follower that calls this gets an <b>empty list</b> rather than a wrong answer, because
        /// <see cref="SecretOps.HandOf"/> answers empty there. That is the right failure: "nothing is legal"
        /// is visibly useless, where a list computed over a hand this side cannot see would be silently
        /// short.
        /// </para>
        /// </summary>
        public static List<MatchIntent> EnumerateForSeat(MatchModel match, int seat)
            => EnumerateLegalIntents(match, seat, SecretOps.HandOf(match, seat));

        /// <summary> Every address a chosen target could name, in canonical order: own board, enemy board, own Den, enemy Den. </summary>
        static IEnumerable<EffectTargetRef> EnumerateTargets(SeatState own, SeatState enemy, int seat, int enemySeat)
        {
            foreach (BoardCritter critter in own.Board)
                yield return EffectTargetRef.OnCritter(critter.Id);

            foreach (BoardCritter critter in enemy.Board)
                yield return EffectTargetRef.OnCritter(critter.Id);

            yield return EffectTargetRef.Den(seat);
            yield return EffectTargetRef.Den(enemySeat);
        }
    }
}
