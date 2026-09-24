using System.Collections.Generic;

namespace Game.Logic
{
    /// <summary>
    /// The launch interpreter: one method per primitive in <see cref="EffectOp"/>, and no knowledge of any
    /// specific card. A card is a config entry binding sequences of these to triggers, so adding a card is a
    /// config change and adding a verb is a change here (<c>Docs/effects.md</c>).
    /// <para>
    /// It reads the board through <see cref="IEffectContext"/> and changes it only through
    /// <see cref="IEffectMutations"/>. Everything a step's parameters are allowed to be was already proven by
    /// the config build, so this reads them rather than re-deriving their legality.
    /// </para>
    /// </summary>
    public sealed class StepInterpreter : IEffectInterpreter
    {
        public static readonly StepInterpreter Instance = new StepInterpreter();

        StepInterpreter() { }

        public void Resolve(EffectQueueItem item, IEffectContext ctx, IEffectMutations mut)
        {
            EffectStepInfo step = item.Info;
            int            seat = item.ResolvingSeat;

            switch (step.Op)
            {
                case EffectOp.Damage:
                    foreach (EffectTargetRef target in ExpandTargets(item, ctx))
                        mut.DealDamage(target, EvaluatePrimary(step.Amount, item, ctx));
                    break;

                case EffectOp.Heal:
                    foreach (EffectTargetRef target in ExpandTargets(item, ctx))
                        mut.Heal(target, EvaluatePrimary(step.Amount, item, ctx));
                    break;

                case EffectOp.Draw:
                    mut.Draw(seat, EvaluatePrimary(step.Amount, item, ctx));
                    break;

                case EffectOp.GainMana:
                    mut.GainMana(seat, EvaluatePrimary(step.Amount, item, ctx), step.Duration);
                    break;

                case EffectOp.Buff:
                {
                    int attackDelta    = EvaluatePrimary(step.Amount, item, ctx);
                    int maxHealthDelta = EvaluateSecondary(step.Amount2, item, ctx);
                    foreach (EffectTargetRef target in ExpandTargets(item, ctx))
                    {
                        if (target.IsCritter)
                            mut.Buff(target.Critter, attackDelta, maxHealthDelta);
                    }
                    break;
                }

                case EffectOp.GrantKeyword:
                {
                    KeywordFlags flag = step.Keyword?.Ref.EngineFlag ?? KeywordFlags.None;
                    foreach (EffectTargetRef target in ExpandTargets(item, ctx))
                    {
                        if (target.IsCritter)
                            mut.GrantKeywords(target.Critter, flag);
                    }
                    break;
                }

                case EffectOp.Summon:
                    if (step.Card != null)
                        mut.Summon(seat, step.Card.Ref, EvaluatePrimary(step.Amount, item, ctx));
                    break;

                case EffectOp.Bounce:
                    // Snapshot the expansion before bouncing anything: the boards shrink as this runs.
                    foreach (EffectTargetRef target in new List<EffectTargetRef>(ExpandTargets(item, ctx)))
                    {
                        if (target.IsCritter)
                            mut.Bounce(target.Critter);
                    }
                    break;

                case EffectOp.CopyFromGraveyard:
                {
                    CardInfo picked = SelectFromGraveyard(step, seat, ctx);
                    if (picked != null)
                    {
                        // A copy out of a public graveyard by a stated rule: both players could have computed
                        // which card it is, so it arrives public.
                        mut.AddCopyToHand(seat, picked);
                    }
                    break;
                }

                case EffectOp.Peek:
                    mut.PeekDeck(seat, EvaluatePrimary(step.Amount, item, ctx), EvaluateSecondary(step.Amount2, item, ctx));
                    break;
            }
        }

        // ---------------------------------------------------------------- targets

        /// <summary>
        /// What a step's <see cref="EffectTargetKind"/> addresses right now, in the canonical order the
        /// context offers: the resolving owner's board first, then the opposing one. Never a dictionary, a
        /// hash set, or a LINQ ordering over one.
        /// </summary>
        static List<EffectTargetRef> ExpandTargets(EffectQueueItem item, IEffectContext ctx)
        {
            List<EffectTargetRef> targets = new List<EffectTargetRef>();

            int seat  = item.ResolvingSeat;
            int enemy = MatchSeats.Other(seat);

            switch (item.Info.Target)
            {
                case EffectTargetKind.Chosen:
                    if (!item.ChosenTarget.IsNone)
                        targets.Add(item.ChosenTarget);
                    break;

                case EffectTargetKind.Self:
                    if (item.Source.WasOnBoard && ctx.CritterAt(item.Source.Id) != null)
                        targets.Add(EffectTargetRef.OnCritter(item.Source.Id));
                    break;

                case EffectTargetKind.OwnDen:
                    targets.Add(EffectTargetRef.Den(seat));
                    break;

                case EffectTargetKind.EnemyDen:
                    targets.Add(EffectTargetRef.Den(enemy));
                    break;

                case EffectTargetKind.AllFriendly:
                    AddBoard(targets, ctx.Board(seat));
                    break;

                case EffectTargetKind.AllEnemy:
                    AddBoard(targets, ctx.Board(enemy));
                    break;

                case EffectTargetKind.AllCritters:
                    AddBoard(targets, ctx.Board(seat));
                    AddBoard(targets, ctx.Board(enemy));
                    break;
            }

            return targets;
        }

        static void AddBoard(List<EffectTargetRef> targets, IReadOnlyList<BoardCritter> board)
        {
            for (int ndx = 0; ndx < board.Count; ndx++)
                targets.Add(EffectTargetRef.OnCritter(board[ndx].Id));
        }

        // ---------------------------------------------------------------- amounts

        /// <summary>
        /// The step's primary amount. A rank track scales one number, and this is the number it scales: the
        /// literal base of the amount, plus whatever the track added to it.
        /// </summary>
        static int EvaluatePrimary(EffectAmount amount, EffectQueueItem item, IEffectContext ctx)
            => Evaluate(amount, item.AmountBonus, item, ctx);

        /// <summary>
        /// The step's second amount — a Buff's health delta, a Peek's keep count. A rank track never touches
        /// it: "restore one more" must not also mean "look at one more".
        /// </summary>
        static int EvaluateSecondary(EffectAmount amount, EffectQueueItem item, IEffectContext ctx)
            => Evaluate(amount, 0, item, ctx);

        /// <summary>
        /// A step's value at the moment it is dequeued: its literal, whatever its rank track added to that
        /// literal, and its counter over public state, each unit of which is worth the step's per-unit
        /// factor. Counting public state is what keeps scaling cards honest — the number a card resolves for
        /// is one both players can read off the board.
        /// </summary>
        static int Evaluate(EffectAmount amount, int rankBonus, EffectQueueItem item, IEffectContext ctx)
        {
            if (amount == null)
                return 0;

            int count = 0;

            switch (amount.Counter)
            {
                case EffectCounter.PlayedThisMatch:
                    count = CountPlayed(item, ctx);
                    break;

                case EffectCounter.FriendlyCritters:
                    count = CountBoard(ctx.Board(item.ResolvingSeat), item.Info.Filter, ctx);
                    break;

                case EffectCounter.EnemyCritters:
                    count = CountBoard(ctx.Board(MatchSeats.Other(item.ResolvingSeat)), item.Info.Filter, ctx);
                    break;
            }

            int value = amount.ValueFrom(count, rankBonus);

            return value < 0 && item.Info.Op != EffectOp.Buff ? 0 : value;
        }

        static int CountPlayed(EffectQueueItem item, IEffectContext ctx)
        {
            EffectFilter          filter = item.Info.Filter;
            IReadOnlyList<CardId> played = ctx.PlayedThisMatch(item.ResolvingSeat);

            int count = 0;
            for (int ndx = 0; ndx < played.Count; ndx++)
            {
                if (filter == null || (ctx.Config.Cards.TryGetValue(played[ndx], out CardInfo card) && filter.Matches(card)))
                    count++;
            }

            // The card whose Hello this is went onto the played list when it was played, and "each other card
            // you have played" does not count itself.
            if (item.ExcludesSourceFromPlayedCount)
            {
                CardInfo source = ctx.CardOf(item.Source.Id);
                if (source != null && (filter == null || filter.Matches(source)))
                    count--;
            }

            return count < 0 ? 0 : count;
        }

        static int CountBoard(IReadOnlyList<BoardCritter> board, EffectFilter filter, IEffectContext ctx)
        {
            if (filter == null)
                return board.Count;

            int count = 0;
            for (int ndx = 0; ndx < board.Count; ndx++)
            {
                CardInfo card = ctx.CardOf(board[ndx].Id);
                if (card != null && filter.Matches(card))
                    count++;
            }

            return count;
        }

        // ---------------------------------------------------------------- graveyard selection

        /// <summary>
        /// The cheapest or costliest matching card in a graveyard, with ties broken on canonical card order —
        /// a pure function of public state that both players could have computed, which is what "no random
        /// effects" means for a card that selects something itself.
        /// </summary>
        static CardInfo SelectFromGraveyard(EffectStepInfo step, int seat, IEffectContext ctx)
        {
            int graveyardSeat = step.Target == EffectTargetKind.EnemyGraveyard ? MatchSeats.Other(seat) : seat;

            IReadOnlyList<CardInstanceId> graveyard = ctx.Graveyard(graveyardSeat);
            CardInfo                      best      = null;

            for (int ndx = 0; ndx < graveyard.Count; ndx++)
            {
                CardInfo candidate = ctx.CardOf(graveyard[ndx]);
                if (candidate == null)
                    continue;
                if (step.Filter != null && !step.Filter.Matches(candidate))
                    continue;

                if (best == null || IsBetter(candidate, best, step.Selector))
                    best = candidate;
            }

            return best;
        }

        static bool IsBetter(CardInfo candidate, CardInfo incumbent, GraveyardSelector selector)
        {
            int byCost = candidate.Cost.CompareTo(incumbent.Cost);
            if (byCost != 0)
                return selector == GraveyardSelector.Costliest ? byCost > 0 : byCost < 0;

            return CardInfo.CompareCanonical(candidate.CardId, incumbent.CardId) < 0;
        }
    }
}
