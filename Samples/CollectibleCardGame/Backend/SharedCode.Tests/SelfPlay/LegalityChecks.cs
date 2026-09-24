using System.Collections.Generic;

namespace Game.Logic.Tests
{
    /// <summary>
    /// The rules-legality half of the invariant catalog (L1, L3–L5), checked against the authoritative state
    /// at the moment an action is offered and <em>before</em> the engine sees it.
    /// <para>
    /// It deliberately re-states the rules rather than calling <see cref="Legality"/>. A second opinion that
    /// asked the thing it is checking would agree with it about a shared bug, and the point of these is to
    /// disagree: the enumeration, the validation and this walk are three readings of the same rulings, and two
    /// of them being wrong in the same way is the case worth catching.
    /// </para>
    /// </summary>
    public static class LegalityChecks
    {
        public static string Check(MatchEngine engine, SeatView view, int seat, MatchIntent intent)
        {
            string member = CheckMembership(engine, view, seat, intent);
            if (member != null)
                return member;

            switch (intent)
            {
                case AttackIntent attack:
                    return CheckAttack(engine, seat, attack);

                case PlayCardIntent play:
                    return CheckPlay(engine, seat, play);

                default:
                    return null;
            }
        }

        /// <summary>
        /// The two things a play has to satisfy that membership cannot speak to. Membership compares against
        /// the set <see cref="Legality.EnumerateLegalIntents"/> produced, so for a play it is circular: an
        /// enumeration that offered an unaffordable card would have that card in the set, and the check would
        /// find it there and be satisfied. Affordability and the board cap are therefore re-derived here from
        /// the authoritative state and the content, with no call back into the thing under test.
        /// </summary>
        static string CheckPlay(MatchEngine engine, int seat, PlayCardIntent play)
        {
            CardInstance instance = engine.Rules.TryGetInstance(play.Card);
            if (instance == null)
                return $"L1: seat {seat} played {play.Card}, which is not a card in this match";
            if (instance.Owner != seat || !engine.InHand(seat, instance.Id))
                return $"L1: seat {seat} played {play.Card}, which is not in its hand";

            // The card and its rank come off CardLookup: a hand card is hidden, and its own registry entry
            // holds no identity. That the check has to ask this way is the point of the split.
            CardInfo  info  = CardLookup.Info(engine.Model, instance.Id);
            CardStats stats = SecretDerivations.Stats(engine.Model, instance.Id).Value;

            SeatState state = engine.Rules.Seat(seat);
            int       cost  = stats.Cost + WeatherModifiers.CostDelta(engine.Weather, info, state);
            if (cost < 0)
                cost = 0;

            if (cost > state.Mana)
                return $"L1: seat {seat} played {info.CardId} for {cost} with {state.Mana} mana";

            if (info.Type == CardType.Critter && state.Board.Count >= engine.Config.Global.MaxBoardCritters)
                return $"L1: seat {seat} played {info.CardId} onto a board already holding {state.Board.Count}";

            return CheckTarget(engine, seat, play.Target, "a play");
        }

        // ---------------------------------------------------------------- L1

        /// <summary>
        /// The action taken is one the rules offered. The mulligan and the peek are the two the rules can only
        /// offer as a shape rather than as a list — a replace set is any subset of a hand and a keep set any
        /// subset of what was revealed — so for those, membership means "the shape the rules described".
        /// </summary>
        static string CheckMembership(MatchEngine engine, SeatView view, int seat, MatchIntent intent)
        {
            switch (intent)
            {
                case MulliganIntent mulligan:
                    return CheckMulligan(engine, view, seat, mulligan);

                case EffectChoiceIntent choice:
                    return CheckChoice(engine, seat, choice);

                default:
                {
                    foreach (MatchIntent legal in view.LegalActions)
                    {
                        if (SameAction(legal, intent))
                            return null;
                    }

                    return $"L1: seat {seat}'s {SelfPlayHarness.Describe(intent)} is not in the legal set of {view.LegalActions.Count}";
                }
            }
        }

        static bool SameAction(MatchIntent a, MatchIntent b)
        {
            switch (a)
            {
                case PlayCardIntent playA:
                    return b is PlayCardIntent playB && playA.Card == playB.Card && playA.Target == playB.Target;

                case AttackIntent attackA:
                    return b is AttackIntent attackB && attackA.Attacker == attackB.Attacker && attackA.Target == attackB.Target;

                case EndTurnIntent _:
                    return b is EndTurnIntent;

                default:
                    return false;
            }
        }

        static string CheckMulligan(MatchEngine engine, SeatView view, int seat, MulliganIntent mulligan)
        {
            bool offered = false;
            foreach (MatchIntent legal in view.LegalActions)
                offered |= legal is MulliganIntent;

            if (!offered)
                return $"L1: seat {seat} mulliganed with no mulligan in the legal set";

            List<CardInstanceId> hand = engine.SecretHand(seat);
            List<CardInstanceId> seen = new List<CardInstanceId>();

            foreach (CardInstanceId id in mulligan.Replace)
            {
                if (!hand.Contains(id))
                    return $"L1: seat {seat} asked to replace {id}, which is not in its hand";
                if (seen.Contains(id))
                    return $"L1: seat {seat} asked to replace {id} twice";
                // Compensation was never in a deck and never goes into one.
                if (!engine.Rules.Instance(id).FromStartingDeck)
                    return $"L1: seat {seat} asked to replace {id}, which never came out of its deck";

                seen.Add(id);
            }

            return null;
        }

        static string CheckChoice(MatchEngine engine, int seat, EffectChoiceIntent choice)
        {
            PendingEffectChoice pending = engine.Rules.PendingChoice;
            if (pending == null)
                return $"L1: seat {seat} answered a choice nobody asked for";
            if (pending.Seat != seat)
                return $"L1: seat {seat} answered a choice held on seat {pending.Seat}";
            if (choice.ChoiceId != pending.Id)
                return $"L1: seat {seat} answered choice {choice.ChoiceId} while choice {pending.Id} is held";
            if (choice.Keep.Count > pending.KeepCount)
                return $"L1: seat {seat} kept {choice.Keep.Count} of a possible {pending.KeepCount}";

            List<int> seen = new List<int>();
            foreach (int index in choice.Keep)
            {
                if (index < 0 || index >= pending.RevealedCount)
                    return $"L1: seat {seat} kept index {index}, which was not revealed to it";
                if (seen.Contains(index))
                    return $"L1: seat {seat} kept index {index} twice";

                seen.Add(index);
            }

            return null;
        }

        // ---------------------------------------------------------------- L3, L4, L5

        static string CheckAttack(MatchEngine engine, int seat, AttackIntent attack)
        {
            int          enemySeat = MatchSeats.Other(seat);
            BoardCritter attacker  = engine.Rules.Seat(seat).FindCritter(attack.Attacker);

            if (attacker == null)
                return $"L1: seat {seat} attacked with {attack.Attacker}, which is not on its board";

            // L4. A critter attacks once, awake, and only if it has something to swing.
            if (attacker.IsSleepy)
                return $"L4: seat {seat}'s {attacker.Id} attacked while sleepy";
            if (attacker.HasAttackedThisTurn)
                return $"L4: seat {seat}'s {attacker.Id} attacked twice in one turn";
            if (attacker.Attack <= 0)
                return $"L4: seat {seat}'s {attacker.Id} attacked with no attack";

            if (attack.Target.IsDen)
            {
                if (attack.Target.DenSeat != enemySeat)
                    return $"L3: seat {seat} attacked the Den of seat {attack.Target.DenSeat}";

                // L3. Any Guard the attacker could legally have hit instead shuts the Den; a Sneaky one could
                // not have been hit, so it does not gate.
                foreach (BoardCritter defender in engine.Rules.Seat(enemySeat).Board)
                {
                    if ((defender.Keywords & KeywordFlags.Guard) != 0 && (defender.Keywords & KeywordFlags.Sneaky) == 0)
                        return $"L3: seat {seat} reached the Den past {defender.Id}, a Guard";
                }

                return null;
            }

            return CheckTarget(engine, seat, attack.Target, "an attack");
        }

        /// <summary> L5. Nothing the enemy does may name a Sneaky critter that has not yet dealt damage. </summary>
        static string CheckTarget(MatchEngine engine, int seat, EffectTargetRef target, string what)
        {
            if (!target.IsCritter)
                return null;

            CardInstance instance = engine.Rules.TryGetInstance(target.Critter);
            if (instance == null)
                return $"L1: {what} by seat {seat} names {target.Critter}, which is not a card in this match";

            if (instance.Owner == seat)
                return null;

            BoardCritter critter = engine.Rules.Seat(instance.Owner).FindCritter(target.Critter);
            if (critter == null)
                return null;

            if ((critter.Keywords & KeywordFlags.Sneaky) != 0)
                return $"L5: {what} by seat {seat} names {critter.Id}, which is Sneaky";

            return null;
        }
    }
}
