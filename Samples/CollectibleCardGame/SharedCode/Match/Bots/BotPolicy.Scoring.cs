using Metaplay.Core;
using System.Collections.Generic;

namespace Game.Logic
{
    public sealed partial class BotPolicy
    {
        // ---------------------------------------------------------------- weights
        //
        // Integers throughout, in hundredths of a mana of card value. No floating point anywhere: the policy
        // has to give the same answer on every machine that could ever be asked to re-derive a decision.

        const int Unit = BotProfile.ManaValueUnit;

        // ---------------------------------------------------------------- the health-domain weights
        //
        // Every weight in this block multiplies a health-domain quantity — a point of attack, a point of
        // damage, a point of hit points restored. They are the one part of this scorer that is NOT
        // magnitude-agnostic: the rest of the numbers below are flat bonuses, mana values or ratios, so
        // widening the stat domain by five multiplies exactly these terms by five and leaves the rest,
        // quietly re-tuning the whole policy. They were divided by the stat quantum when the domain was
        // widened (F2): a point of stats is a fifth of an old point and is worth a fifth as much.
        //
        // **Every such weight belongs here, named.** F2's first pass rebased five constants and left seven
        // inline multipliers at the old scale, which valued Snacktime, Zoomies and own-side healing five
        // times too heavily for a whole wave without a single test noticing — so an inline `* 30` on a stat
        // is not a style problem, it is the defect. If you add one, add it to this block and pin a decision
        // that turns on it (`PolicyTests`, "the health-domain weights").
        /// <summary> What a point of Den damage is worth when neither side is closing yet. </summary>
        const int FaceDamagePoint = 9;
        /// <summary> What a point of Den damage is worth on top of that when the race is nearly over. </summary>
        const int FaceDamageClosing = 12;
        /// <summary> What a point of damage on a critter that survives it is worth. </summary>
        const int ChipDamagePoint = 3;
        /// <summary> Stat mass, on top of a critter's mana value. </summary>
        const int StatPoint = 5;
        /// <summary> A point of attack specifically, which is worth a little more than a point of health. </summary>
        const int AttackPoint = 6;
        /// <summary> A point of Snacktime attack traded into a body: the damage is spent on the trade. </summary>
        const int SnacktimeTradePoint = 2;
        /// <summary> A point of Snacktime attack swung at a Den, where the lifegain rides the face damage. </summary>
        const int SnacktimeFacePoint = 3;
        /// <summary> A point of damage aimed at one's own Den. Never worth anything; the sign does the work. </summary>
        const int OwnDenDamagePoint = 12;
        /// <summary> A point restored to an own Den the enemy can already reach. </summary>
        const int OwnDenHealUnderThreatPoint = 12;
        /// <summary>
        /// A point of health restored on an own Den nothing is threatening, or on a critter.
        /// <para>
        /// The one weight in this block that did not survive the division cleanly: it was 22, and a fifth of
        /// that is 4.4. Rounded <b>down</b> to 4 rather than up to 5, because what the original 22 encoded
        /// was an ordering against <see cref="StatPoint"/>'s 25 — a point of health restored is worth less
        /// than a point of stat mass gained — and 5 would have made the two equal, losing the very
        /// relationship the number was chosen for.
        /// </para>
        /// </summary>
        const int HealPoint = 4;
        /// <summary> A point handed to the enemy Den, which is what healing it would do. </summary>
        const int EnemyDenHealPenaltyPoint = 8;
        /// <summary> Removing a body is worth a little more than the body itself. </summary>
        const int KillBonus = 50;
        /// <summary> Opening the Den, when there is a clock behind it. </summary>
        const int GuardClearBonus = 150;
        /// <summary> Having a Guard on the board at all, when it will actually shut the Den. </summary>
        const int GuardKeywordBonus = 50;
        /// <summary> Adding one while the Den is under a clock, which is the turn it is really buying. </summary>
        const int GuardUnderPressureBonus = 150;
        /// <summary> Trading away the Guard that is holding your own Den shut. </summary>
        const int GuardLossPenalty = 200;
        const int CardDrawPoint = 130;
        const int TurnManaPoint = 55;
        const int RampPoint = 160;
        const int SummonPoint = 120;
        const int GraveyardCopyPoint = 140;
        const int PeekPoint = 90;
        /// <summary> Spending the turn's mana, all else equal: an unspent acorn is usually a wasted turn. </summary>
        const int CurvePoint = 20;
        /// <summary> A clock nobody is on. Larger than any real one, and not an overflow hazard. </summary>
        const int NoClock = 99;

        // ---------------------------------------------------------------- the position, read once

        /// <summary>
        /// What the scorer needs to know about the position, computed once per decision so that thirty
        /// candidate actions do not each re-derive the race.
        /// </summary>
        readonly struct Situation
        {
            public readonly SeatView       View;
            public readonly int            Seat;
            public readonly SeatState Own;
            public readonly SeatState Enemy;
            /// <summary> Turns for this seat's board to close the enemy Den, or <see cref="NoClock"/>. </summary>
            public readonly int            OwnClock;
            public readonly int            EnemyClock;
            public readonly bool           EnemyThreatensLethal;
            public readonly int            BiggestEnemyAttack;

            public Situation(SeatView view, int seat)
            {
                View   = view;
                Seat   = seat;
                Own    = view.Own;
                Enemy  = view.Opponent;

                // A Guard the attacker could legally have hit instead shuts the Den it stands in front of,
                // which lengthens the clock pointed at it by the turn spent getting through.
                bool enemyDenGuarded = HasGate(Enemy.Board);
                bool ownDenGuarded   = HasGate(Own.Board);

                int ownPressure   = TotalAttack(Own.Board);
                int enemyPressure = TotalAttack(Enemy.Board);

                OwnClock   = Clock(Enemy.DenHp, ownPressure, enemyDenGuarded);
                EnemyClock = Clock(Own.DenHp, enemyPressure, ownDenGuarded);

                EnemyThreatensLethal = !ownDenGuarded && enemyPressure >= Own.DenHp;
                BiggestEnemyAttack   = BiggestAttack(Enemy.Board);
            }

            /// <summary> Whether the other side closes first. Ties count as behind: the enemy acts next. </summary>
            public bool Behind => EnemyClock <= OwnClock;

            /// <summary> Whether this seat is one or two turns from lethal, which is when face damage wins. </summary>
            public bool Closing => OwnClock <= 2;

            static int Clock(int denHp, int pressure, bool guarded)
            {
                if (pressure <= 0)
                    return NoClock;

                int turns = (denHp + pressure - 1) / pressure;
                return guarded ? turns + 1 : turns;
            }
        }

        /// <summary>
        /// Whether a board holds a Guard that shuts its own Den. The ruling itself is
        /// <see cref="KeywordRules.GatesTheDen"/>, which every reading here goes through — including the one
        /// the engine has no equivalent of, a card still in <em>hand</em> with the Weather aura ORed in, which
        /// is why that predicate is stated over a keyword set rather than over a critter in play.
        /// </summary>
        static bool HasGate(List<BoardCritter> board)
        {
            for (int ndx = 0; ndx < board.Count; ndx++)
            {
                if (Gates(board[ndx]))
                    return true;
            }

            return false;
        }

        static bool Gates(BoardCritter critter) => KeywordRules.GatesTheDen(critter.Keywords);

        static int TotalAttack(List<BoardCritter> board)
        {
            int total = 0;
            for (int ndx = 0; ndx < board.Count; ndx++)
                total += board[ndx].Attack;
            return total;
        }

        static int BiggestAttack(List<BoardCritter> board)
        {
            int biggest = 0;
            for (int ndx = 0; ndx < board.Count; ndx++)
            {
                if (board[ndx].Attack > biggest)
                    biggest = board[ndx].Attack;
            }
            return biggest;
        }

        static bool Absorbs(BoardCritter critter, int amount)
            => amount > 0 && (critter.Keywords & KeywordFlags.Bubble) != 0 && critter.BubbleIntact;

        static bool CanAttackNow(BoardCritter critter)
            => !critter.IsSleepy && !critter.HasAttackedThisTurn && critter.Attack > 0;

        static BoardCritter Find(List<BoardCritter> board, CardInstanceId id)
        {
            for (int ndx = 0; ndx < board.Count; ndx++)
            {
                if (board[ndx].Id == id)
                    return board[ndx];
            }

            return null;
        }

        /// <summary>
        /// The critter a target names and which side it is on, or null when it names nothing in play. One
        /// lookup rather than one per value function: five copies of "own board, then the other one" is five
        /// places for the answer to drift.
        /// </summary>
        static BoardCritter Locate(Situation situation, EffectTargetRef target, out bool friendly)
        {
            friendly = true;
            BoardCritter critter = Find(situation.Own.Board, target.Critter);
            if (critter != null)
                return critter;

            friendly = false;
            return Find(situation.Enemy.Board, target.Critter);
        }

        static HandCard? FindInHand(IReadOnlyList<HandCard> hand, CardInstanceId id)
        {
            for (int ndx = 0; ndx < hand.Count; ndx++)
            {
                if (hand[ndx].Instance == id)
                    return hand[ndx];
            }

            return null;
        }

        // ---------------------------------------------------------------- one action's score

        int Score(Situation situation, MatchIntent intent)
        {
            switch (intent)
            {
                // The floor. Anything that converts remaining mana or an idle critter into something scores
                // above it, so the turn ends only once nothing is left worth doing.
                case EndTurnIntent _:
                    return 0;

                case AttackIntent attack:
                    return ScoreAttack(situation, attack);

                case PlayCardIntent play:
                    return ScorePlay(situation, play);

                default:
                    return 0;
            }
        }

        // ---------------------------------------------------------------- combat

        int ScoreAttack(Situation situation, AttackIntent intent)
        {
            BoardCritter attacker = Find(situation.Own.Board, intent.Attacker);
            if (attacker == null)
                return 0;

            if (intent.Target.IsDen)
                return ScoreFaceAttack(situation, attacker);

            BoardCritter defender = Find(situation.Enemy.Board, intent.Target.Critter);
            if (defender == null)
                return 0;

            // Simultaneous, both amounts up front, exactly as the engine resolves it.
            bool defenderAbsorbs = Absorbs(defender, attacker.Attack);
            bool kills           = !defenderAbsorbs && attacker.Attack >= defender.CurrentHealth;
            bool attackerAbsorbs = Absorbs(attacker, defender.Attack);
            bool dies            = !attackerAbsorbs && defender.Attack >= attacker.CurrentHealth;

            int score;
            if (kills)
                score = CritterValue(situation, defender) + KillBonus;
            else
            {
                int chip = defenderAbsorbs ? 0 : Smaller(attacker.Attack, defender.CurrentHealth);
                score = chip * ChipDamagePoint;
            }

            if (dies)
                score -= CritterValue(situation, attacker);

            // Clearing a Guard is only worth extra when it is a precondition for damage that matters this turn
            // or next; otherwise it is an ordinary trade.
            if (kills && Gates(defender) && situation.OwnClock <= 3)
                score += GuardClearBonus;

            // The mirror: holding a Guard back is worth something exactly when the Den is under pressure. It is
            // folded into the trade rather than being a decision of its own, and only bites when the Guard
            // would actually die.
            if (dies && Gates(attacker) && situation.EnemyClock <= 3)
                score -= GuardLossPenalty;

            // Behind on the race, a body removed is worth more than the same swing sent at a Den you are not
            // going to reach in time.
            if (kills && situation.Behind)
                score += Unit;

            if ((attacker.Keywords & KeywordFlags.Snacktime) != 0)
                score += attacker.Attack * SnacktimeTradePoint;

            return score;
        }

        int ScoreFaceAttack(Situation situation, BoardCritter attacker)
        {
            int damage = Smaller(attacker.Attack, situation.Enemy.DenHp);
            int score  = damage * FaceDamagePoint;

            if (situation.Closing)
                score += damage * FaceDamageClosing;
            else if (situation.Behind)
                score -= damage * ChipDamagePoint;

            if ((attacker.Keywords & KeywordFlags.Snacktime) != 0)
                score += attacker.Attack * SnacktimeFacePoint;

            return score;
        }

        int CritterValue(Situation situation, BoardCritter critter)
        {
            CardInstance instance = situation.View.Rules.Instance(critter.Id);
            CardInfo     card     = instance.Info;
            CardStats    stats    = instance.Stats;

            int value = stats.Cost * Unit + (critter.Attack + critter.CurrentHealth) * StatPoint;

            if ((critter.Keywords & KeywordFlags.Guard) != 0)
                value += 50;
            if ((critter.Keywords & KeywordFlags.Sneaky) != 0)
                value += 40;
            if ((critter.Keywords & KeywordFlags.Bubble) != 0 && critter.BubbleIntact)
                value += 50;
            if ((critter.Keywords & KeywordFlags.Snacktime) != 0)
                value += 30;

            return value;
        }

        // ---------------------------------------------------------------- playing a card

        int ScorePlay(Situation situation, PlayCardIntent intent)
        {
            HandCard? found = FindInHand(situation.View.Hand, intent.Card);
            if (found == null)
                return 0;

            HandCard  held  = found.Value;
            CardInfo  card  = Card(held.Card);
            CardStats stats = card.GetStatsAtRank(held.Rank);

            int score = 0;

            if (card.Type == CardType.Critter)
            {
                score += stats.Cost * Unit + (stats.Attack + stats.Health) * StatPoint;

                KeywordFlags keywords = card.GetKeywordFlags() | WeatherModifiers.AuraKeywords(WeatherOf(situation));

                // A Guard is worth paying for only if it will actually shut the Den, and a Sneaky one does not
                // — so a Weather whose aura grants Sneaky, or a Smoke Bomb already on the body, takes the
                // gate away without touching the keyword. Paying the body-that-buys-a-turn price for a Guard
                // that buys no turn is how a bot walks into a lost race believing it is stalling.
                if (KeywordRules.GatesTheDen(keywords))
                {
                    score += GuardKeywordBonus;
                    if (situation.EnemyClock <= 3)
                        score += GuardUnderPressureBonus;
                }

                if ((keywords & KeywordFlags.Zoomies) != 0)
                    score += stats.Attack * AttackPoint;
                if ((keywords & KeywordFlags.Sneaky) != 0)
                    score += 40;
                if ((keywords & KeywordFlags.Bubble) != 0)
                    score += 50;
                if ((keywords & KeywordFlags.Snacktime) != 0)
                    score += 30;
            }

            score += ScoreHello(situation, card, stats.EffectAmountDelta, intent.Target, held.Instance);

            // Tempo, as a tie-break rather than as a driver: between two lines of the same value, take the one
            // that converts more of the turn's mana into board.
            score += CostOf(situation.View, situation.Seat, held) * CurvePoint;

            // Removal is not auto-cast the instant it is legal. Held until the target is worth the card, or
            // until the board makes waiting a luxury. Halving has to move the score *down*, which on a play
            // that already scores negative means leaving it alone rather than halving the penalty.
            if (card.Type == CardType.Trick
                && score > 0
                && !RemovalIsWorthCastingNow(situation, card, stats.EffectAmountDelta, intent.Target))
            {
                score /= 2;
            }

            return score;
        }

        static WeatherInfo WeatherOf(Situation situation) => situation.View.Weather;

        /// <summary>
        /// What the card's Hello is worth in this position. A trick's whole body is its Hello, and a critter's
        /// arrival effect is scored on top of the body it comes attached to.
        /// </summary>
        int ScoreHello(Situation situation, CardInfo card, int amountDelta, EffectTargetRef chosen, CardInstanceId self)
        {
            IReadOnlyList<MetaRef<EffectStepInfo>> steps = card.Hello;
            int total = 0;

            for (int ndx = 0; ndx < steps.Count; ndx++)
            {
                EffectStepInfo step = steps[ndx].Ref;
                // The rank track scales the first Hello step's primary amount, and nothing else.
                int amount  = EvaluateAmount(situation, step, step.Amount, ndx == 0 ? amountDelta : 0);
                int amount2 = EvaluateAmount(situation, step, step.Amount2, 0);
                total += ScoreStep(situation, step, amount, amount2, chosen, self);
            }

            return total;
        }

        int ScoreStep(Situation situation, EffectStepInfo step, int amount, int amount2, EffectTargetRef chosen, CardInstanceId self)
        {
            switch (step.Op)
            {
                case EffectOp.Damage:
                {
                    int total = 0;
                    foreach (EffectTargetRef target in Expand(situation, step.Target, chosen, self))
                        total += DamageValue(situation, target, amount);
                    return total;
                }

                case EffectOp.Heal:
                {
                    int total = 0;
                    foreach (EffectTargetRef target in Expand(situation, step.Target, chosen, self))
                        total += HealValue(situation, target, amount);
                    return total;
                }

                case EffectOp.Draw:
                    return amount * CardDrawPoint;

                case EffectOp.GainMana:
                    return amount * (step.Duration == ManaDuration.Permanent ? RampPoint : TurnManaPoint);

                case EffectOp.Buff:
                {
                    int total = 0;
                    foreach (EffectTargetRef target in Expand(situation, step.Target, chosen, self))
                        total += BuffValue(situation, target, amount, amount2, self);
                    return total;
                }

                case EffectOp.GrantKeyword:
                {
                    KeywordFlags flag  = step.Keyword?.Ref.EngineFlag ?? KeywordFlags.None;
                    int          total = 0;
                    foreach (EffectTargetRef target in Expand(situation, step.Target, chosen, self))
                        total += KeywordValue(situation, target, flag);
                    return total;
                }

                case EffectOp.Summon:
                {
                    int room = _config.Global.MaxBoardCritters - situation.Own.Board.Count;
                    return Smaller(amount, room < 0 ? 0 : room) * SummonPoint;
                }

                case EffectOp.Bounce:
                {
                    int total = 0;
                    foreach (EffectTargetRef target in Expand(situation, step.Target, chosen, self))
                        total += BounceValue(situation, target);
                    return total;
                }

                case EffectOp.CopyFromGraveyard:
                    return GraveyardHasAMatch(situation, step) ? GraveyardCopyPoint : 0;

                case EffectOp.Peek:
                    return situation.Own.DeckCount > 0 ? PeekPoint : 0;

                default:
                    return 0;
            }
        }

        /// <summary>
        /// What a step's target kind addresses in this position, in the same canonical order the interpreter
        /// expands it in. A <see cref="EffectTargetKind.Self"/> on a card being played names a body that is not
        /// on the board yet; the value functions recognise it by the identity and value the raw deltas.
        /// </summary>
        static List<EffectTargetRef> Expand(Situation situation, EffectTargetKind kind, EffectTargetRef chosen, CardInstanceId self)
        {
            List<EffectTargetRef> targets = new List<EffectTargetRef>();
            int                   enemy   = MatchSeats.Other(situation.Seat);

            switch (kind)
            {
                case EffectTargetKind.Chosen:
                    if (!chosen.IsNone)
                        targets.Add(chosen);
                    break;

                case EffectTargetKind.Self:
                    targets.Add(EffectTargetRef.OnCritter(self));
                    break;

                case EffectTargetKind.OwnDen:
                    targets.Add(EffectTargetRef.Den(situation.Seat));
                    break;

                case EffectTargetKind.EnemyDen:
                    targets.Add(EffectTargetRef.Den(enemy));
                    break;

                case EffectTargetKind.AllFriendly:
                    AddBoard(targets, situation.Own.Board);
                    break;

                case EffectTargetKind.AllEnemy:
                    AddBoard(targets, situation.Enemy.Board);
                    break;

                case EffectTargetKind.AllCritters:
                    AddBoard(targets, situation.Own.Board);
                    AddBoard(targets, situation.Enemy.Board);
                    break;
            }

            return targets;
        }

        static void AddBoard(List<EffectTargetRef> targets, List<BoardCritter> board)
        {
            for (int ndx = 0; ndx < board.Count; ndx++)
                targets.Add(EffectTargetRef.OnCritter(board[ndx].Id));
        }

        /// <summary>
        /// A step's value at resolution: its literal, whatever the rank track added to it, and its counter over
        /// public state, each unit worth the step's per-unit factor. The counters are exact rather than
        /// estimated — every one of them reads something both players can see, which is what makes a scaling
        /// card's number computable from a seat view at all. The arithmetic itself is
        /// <see cref="EffectAmount.ValueFrom"/>, shared with <c>StepInterpreter.Evaluate</c>, so the two
        /// cannot drift; only the counter reading is this method's own.
        /// </summary>
        int EvaluateAmount(Situation situation, EffectStepInfo step, EffectAmount amount, int rankBonus)
        {
            if (amount == null)
                return 0;

            int count = 0;

            switch (amount.Counter)
            {
                case EffectCounter.PlayedThisMatch:
                    // The card being scored is not on the played list yet, which is exactly what "each other
                    // card you have played" counts.
                    count = CountMatchingCards(situation.Own.PlayedThisMatch, step.Filter);
                    break;

                case EffectCounter.FriendlyCritters:
                    count = CountMatching(situation.View.Rules, situation.Own.Board, step.Filter);
                    break;

                case EffectCounter.EnemyCritters:
                    count = CountMatching(situation.View.Rules, situation.Enemy.Board, step.Filter);
                    break;
            }

            int value = amount.ValueFrom(count, rankBonus);

            return value < 0 && step.Op != EffectOp.Buff ? 0 : value;
        }

        int CountMatching(List<CardId> cards, EffectFilter filter)
        {
            if (filter == null)
                return cards.Count;

            int count = 0;
            for (int ndx = 0; ndx < cards.Count; ndx++)
            {
                if (filter.Matches(Card(cards[ndx])))
                    count++;
            }

            return count;
        }

        /// <summary> How many critters on this board the filter admits. Each one's card comes off the public registry. </summary>
        static int CountMatching(MatchRulesState rules, List<BoardCritter> board, EffectFilter filter)
        {
            if (filter == null)
                return board.Count;

            int count = 0;
            for (int ndx = 0; ndx < board.Count; ndx++)
            {
                if (filter.Matches(rules.Instance(board[ndx].Id).Info))
                    count++;
            }

            return count;
        }

        /// <summary> The same count over a list of cards rather than of critters in play. </summary>
        int CountMatchingCards(List<CardId> cards, EffectFilter filter)
        {
            if (filter == null)
                return cards.Count;

            int count = 0;
            for (int ndx = 0; ndx < cards.Count; ndx++)
            {
                if (filter.Matches(Card(cards[ndx])))
                    count++;
            }

            return count;
        }

        bool GraveyardHasAMatch(Situation situation, EffectStepInfo step)
        {
            List<CardInstanceId> graveyard = step.Target == EffectTargetKind.EnemyGraveyard ? situation.Enemy.Graveyard : situation.Own.Graveyard;
            for (int ndx = 0; ndx < graveyard.Count; ndx++)
            {
                if (step.Filter == null || step.Filter.Matches(situation.View.Rules.Instance(graveyard[ndx]).Info))
                    return true;
            }

            return false;
        }

        // ---------------------------------------------------------------- what an effect is worth

        int DamageValue(Situation situation, EffectTargetRef target, int amount)
        {
            if (amount <= 0)
                return 0;

            if (target.IsDen)
            {
                if (target.DenSeat == situation.Seat)
                    return -amount * OwnDenDamagePoint;

                int landed  = Smaller(amount, situation.Enemy.DenHp);
                int atTheDen = landed * FaceDamagePoint;
                if (situation.Closing)
                    atTheDen += landed * FaceDamageClosing;
                return atTheDen;
            }

            BoardCritter critter = Locate(situation, target, out bool friendly);

            if (critter == null)
                return 0;

            bool absorbs = Absorbs(critter, amount);
            int  value;
            if (!absorbs && amount >= critter.CurrentHealth)
                value = CritterValue(situation, critter) + KillBonus;
            else
                value = absorbs ? 0 : Smaller(amount, critter.CurrentHealth) * ChipDamagePoint;

            return friendly ? -value : value;
        }

        int HealValue(Situation situation, EffectTargetRef target, int amount)
        {
            if (amount <= 0)
                return 0;

            if (target.IsDen)
            {
                bool own    = target.DenSeat == situation.Seat;
                int  denHp  = own ? situation.Own.DenHp : situation.Enemy.DenHp;
                int  healed = Smaller(amount, _config.Global.DenStartingHp - denHp);
                if (healed <= 0)
                    return 0;

                // Health on a Den nobody is threatening is worth much less than health on one that is about to
                // be at zero.
                return own
                    ? healed * (situation.EnemyThreatensLethal ? OwnDenHealUnderThreatPoint : HealPoint)
                    : -healed * EnemyDenHealPenaltyPoint;
            }

            BoardCritter critter = Locate(situation, target, out bool friendly);

            if (critter == null)
                return 0;

            int healedOnCritter = Smaller(amount, critter.Damage);
            int value           = healedOnCritter * HealPoint;

            // Aimed at what is about to die: a heal that lifts a critter out of range of the biggest swing on
            // the other board is worth a body, not a couple of points.
            if (critter.CurrentHealth <= situation.BiggestEnemyAttack && critter.CurrentHealth + healedOnCritter > situation.BiggestEnemyAttack)
                value += 120;

            return friendly ? value : -value;
        }

        int BuffValue(Situation situation, EffectTargetRef target, int attackDelta, int healthDelta, CardInstanceId self)
        {
            if (!target.IsCritter)
                return 0;

            BoardCritter critter = Locate(situation, target, out bool friendly);

            // The card being played is not on a board yet. Its own growth is worth its raw deltas.
            if (critter == null)
                return target.Critter == self ? attackDelta * AttackPoint + healthDelta * StatPoint : 0;

            int value = attackDelta * AttackPoint + healthDelta * StatPoint;

            // A buff aimed at something that can still swing this turn is worth the extra damage too.
            if (friendly && CanAttackNow(critter))
                value += attackDelta * StatPoint;

            bool wouldDie = healthDelta < 0 && critter.MaxHealth + healthDelta - critter.Damage <= 0;

            if (friendly)
                return wouldDie ? value - CritterValue(situation, critter) : value;

            // Stat theft on an enemy: the deltas are negative, so their negation is what it is worth.
            return wouldDie ? -value + CritterValue(situation, critter) + KillBonus : -value;
        }

        int KeywordValue(Situation situation, EffectTargetRef target, KeywordFlags flag)
        {
            if (!target.IsCritter || flag == KeywordFlags.None)
                return 0;

            BoardCritter critter = Locate(situation, target, out bool friendly);

            if (critter == null)
                return 0;

            // Nothing to grant: it already has it.
            if ((critter.Keywords & flag) != 0)
                return 0;

            int value;
            switch (flag)
            {
                // Granting Guard to something the enemy cannot attack anyway grants no gate, so it is
                // worth what the keyword is worth only when the critter would end up gating.
                case KeywordFlags.Guard:     value = KeywordRules.GatesTheDen(critter.Keywords | flag) ? 120 : 0; break;
                case KeywordFlags.Bubble:    value = 80;  break;
                case KeywordFlags.Sneaky:    value = 60;  break;
                case KeywordFlags.Zoomies:   value = critter.IsSleepy ? critter.Attack * AttackPoint : 20; break;
                case KeywordFlags.Snacktime: value = 40;  break;
                default:                     value = 0;   break;
            }

            return friendly ? value : -value;
        }

        int BounceValue(Situation situation, EffectTargetRef target)
        {
            if (!target.IsCritter)
                return 0;

            BoardCritter critter = Locate(situation, target, out bool friendly);
            if (critter == null)
                return 0;

            // Bouncing your own is tempo lost rather than a card lost — it comes back. Bouncing theirs is most
            // of a removal, since they have to spend the mana on it again.
            return friendly ? -CritterValue(situation, critter) / 2 : CritterValue(situation, critter) * 3 / 4;
        }

        /// <summary>
        /// Whether a removal trick should be spent now rather than held. The rule is deliberately narrow, since
        /// a one-ply greedy heuristic cannot tell "a better target arrives next turn" from "this is the target":
        /// spend it when it actually kills something worth the card, or when the board opposite already
        /// threatens lethal and there is no next turn to save it for.
        /// </summary>
        bool RemovalIsWorthCastingNow(Situation situation, CardInfo card, int amountDelta, EffectTargetRef chosen)
        {
            if (!chosen.IsCritter)
                return true;

            BoardCritter victim = Find(situation.Enemy.Board, chosen.Critter);
            if (victim == null)
                return true;

            IReadOnlyList<MetaRef<EffectStepInfo>> steps = card.Hello;
            int damage = 0;
            for (int ndx = 0; ndx < steps.Count; ndx++)
            {
                EffectStepInfo step = steps[ndx].Ref;
                if (step.Op == EffectOp.Damage && step.Target == EffectTargetKind.Chosen)
                    damage += EvaluateAmount(situation, step, step.Amount, ndx == 0 ? amountDelta : 0);
            }

            // Not a damage-removal trick at all: nothing to hold.
            if (damage <= 0)
                return true;

            if (situation.EnemyThreatensLethal)
                return true;

            bool kills = !Absorbs(victim, damage) && damage >= victim.CurrentHealth;
            return kills && situation.View.Rules.Instance(victim.Id).Stats.Cost >= 3;
        }

        static int Smaller(int a, int b) => a < b ? a : b;
    }
}
