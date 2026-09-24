using Metaplay.Core;
using System.Collections.Generic;

namespace Game.Logic
{
    /// <summary>
    /// The effect queue and everything that drains it: enqueueing a trigger, resolving one step, the death
    /// sweep between steps, the Den check at the end, and the damage and healing primitives the interpreter
    /// reaches through <see cref="IEffectMutations"/>.
    /// <para>
    /// Every method here mutates <b>public</b> state only. The two places a resolution touches a hidden
    /// zone — a draw and a peek — are <see cref="ZoneOps.Draw"/> and <see cref="ChoiceRules"/>, and both
    /// pair a public decision with one named excursion into <see cref="SecretOps"/>.
    /// </para>
    /// </summary>
    public static class ResolutionRules
    {
        /// <summary>
        /// The most items one drain may resolve. This is a termination guard rather than a tunable — hitting
        /// it means content composed a cycle the vocabulary is not supposed to permit — so it is a constant
        /// here rather than a number in game config somebody could raise instead of fixing the content.
        /// </summary>
        public const int EffectQueueDrainCap = 256;

        /// <summary> How many passes the death sweep may take before the board must be stable. </summary>
        public const int MaxSweepPasses = 16;

        // ---------------------------------------------------------------- enqueueing

        /// <summary>
        /// Put one card's steps for one trigger onto the tail of the queue, in authored order. A rank track's
        /// amount delta applies to the literal base of the first step of the card's Hello, and nowhere else.
        /// <para>
        /// The card's identity is read off the <em>public</em> registry entry, which is populated for every
        /// instance a trigger can fire for: a card is on the board or in a graveyard when its triggers run,
        /// and both are public.
        /// </para>
        /// </summary>
        public static void EnqueueCardSteps(MatchModel match, CardInstance instance, CardTrigger trigger, int resolvingSeat, CardInstanceSnapshot source, EffectTargetRef chosenTarget, bool excludesSourceFromPlayedCount)
        {
            IReadOnlyList<MetaRef<EffectStepInfo>> steps = instance.Info.GetSteps(trigger);
            if (steps.Count == 0)
                return;

            int amountBonus = trigger == CardTrigger.Hello ? instance.Stats.EffectAmountDelta : 0;

            for (int ndx = 0; ndx < steps.Count; ndx++)
            {
                match.Rules.EffectQueue.Add(new EffectQueueItem(
                    steps[ndx],
                    resolvingSeat,
                    source,
                    chosenTarget,
                    ndx == 0 ? amountBonus : 0,
                    excludesSourceFromPlayedCount));
            }
        }

        /// <summary>
        /// Put the Weather's steps onto the tail, resolved as the seat the event belongs to — the seat whose
        /// turn it is, or the owner of the critter that died. That scoping is what makes one config row
        /// symmetric by construction.
        /// </summary>
        public static void EnqueueWeatherSteps(MatchModel match, WeatherTrigger trigger, int resolvingSeat)
        {
            WeatherInfo weather = match.Rules.Weather?.Ref;
            if (weather == null || weather.Trigger != trigger)
                return;

            foreach (MetaRef<EffectStepInfo> step in weather.Steps)
            {
                match.Rules.EffectQueue.Add(new EffectQueueItem(
                    step,
                    resolvingSeat,
                    CardInstanceSnapshot.None(resolvingSeat),
                    EffectTargetRef.None,
                    amountBonus: 0,
                    excludesSourceFromPlayedCount: false));
            }
        }

        /// <summary> The Weather goes first, then cards in entered-play order (<c>Docs/effects.md</c>). </summary>
        public static void EnqueueTurnTriggers(MatchModel match, int seat, CardTrigger cardTrigger, WeatherTrigger weatherTrigger)
        {
            EnqueueWeatherSteps(match, weatherTrigger, seat);

            foreach (BoardCritter critter in match.Rules.Seat(seat).Board)
            {
                CardInstance instance = match.Rules.Instance(critter.Id);
                EnqueueCardSteps(match, instance, cardTrigger, seat, CardInstanceSnapshot.OfCritter(critter, seat), EffectTargetRef.None, excludesSourceFromPlayedCount: false);
            }
        }

        // ---------------------------------------------------------------- the drain

        public static void ResolveQueue(MatchModel match, ResolutionContinuation continuation)
        {
            match.Rules.SetContinuation(continuation);
            ResumeResolution(match);
        }

        /// <summary>
        /// Drain to empty, then do whatever the resolution still owed. Split from the drain itself because a
        /// peek holds the queue for its owner's choice and the rest of the turn has to wait for the answer,
        /// possibly from a different action.
        /// </summary>
        public static void ResumeResolution(MatchModel match)
        {
            if (!DrainQueue(match))
                return;

            ResolutionContinuation continuation = match.Rules.Continuation;
            match.Rules.SetContinuation(ResolutionContinuation.None);

            // The Den is checked once per resolution, after the queue drains — so a Den taken to zero and
            // healed back inside the same resolution survives.
            CheckDens(match);

            if (match.Rules.Phase == MatchPhase.Complete)
                return;

            switch (continuation)
            {
                case ResolutionContinuation.FinishAction:
                    // Nothing to do: the acting seat's turn deadline is already in force and stays as it is.
                    // How long the resolution stays on screen is the client's own beat.
                    break;

                case ResolutionContinuation.FinishEndTurn:
                    TurnRules.AdvanceToNextTurn(match);
                    break;

                case ResolutionContinuation.FinishStartTurn:
                    match.Pacing.ArmOrClear(MatchDeadlineKind.Turn, match.CurrentTime, match.Timings.TurnDeadline, match.Rules.SeatOnTurn);
                    break;
            }
        }

        /// <summary> True when the queue reached the end; false when it is held for a seat's choice. </summary>
        static bool DrainQueue(MatchModel match)
        {
            int                resolved    = 0;
            EffectSurface      effects     = new EffectSurface(match);
            IEffectInterpreter interpreter = match.Interpreter;

            while (true)
            {
                if (match.Rules.PendingChoice != null)
                    return false;

                if (match.Rules.EffectQueue.Count == 0)
                    return true;

                if (++resolved > EffectQueueDrainCap)
                {
                    // A failure, not a policy: reaching this means content composed a cycle the vocabulary is
                    // not supposed to permit, and continuing would hide it.
                    throw new MatchEngineException($"One resolution exceeded the drain cap of {EffectQueueDrainCap} steps");
                }

                EffectQueueItem item = match.Rules.EffectQueue[0];
                match.Rules.EffectQueue.RemoveAt(0);

                effects.Begin(item);
                interpreter.Resolve(item, effects, effects);
                effects.End();

                match.Emit(new EffectResolvedEvent(item.Info.StepId, item.ResolvingSeat, item.Source.Id));

                // State-based cleanup between items: the interpreter never has to check whether the thing it
                // just damaged should now be in a graveyard.
                SweepDeaths(match);
            }
        }

        /// <summary>
        /// Take the dead off the board and enqueue what their dying triggers, in canonical order — the active
        /// seat's board ascending, then the other seat's — repeated until the board is stable.
        /// </summary>
        public static void SweepDeaths(MatchModel match)
        {
            List<CardInstanceId> dead      = new List<CardInstanceId>();
            List<int>            deadSeats = new List<int>();

            for (int pass = 0; pass < MaxSweepPasses; pass++)
            {
                CombatRules.CollectDead(match.Rules, dead, deadSeats);
                if (dead.Count == 0)
                    return;

                // Snapshot and announce before anything leaves the board, so a Goodbye reads its critter as
                // it was at the moment it died.
                List<CardInstanceSnapshot> snapshots = new List<CardInstanceSnapshot>(dead.Count);
                for (int ndx = 0; ndx < dead.Count; ndx++)
                {
                    int          seat    = deadSeats[ndx];
                    BoardCritter critter = match.Rules.Seat(seat).FindCritter(dead[ndx]);
                    snapshots.Add(CardInstanceSnapshot.OfCritter(critter, seat));
                    match.Emit(new CritterDiedEvent(critter.Id, seat, critter.MaxHealth <= 0 ? CritterDeathCause.StatLoss : CritterDeathCause.LethalDamage));
                }

                for (int ndx = 0; ndx < dead.Count; ndx++)
                    ZoneOps.RemoveDeadCritter(match, deadSeats[ndx], dead[ndx]);

                // The Weather goes first, then the card's own Goodbye, per death in canonical order.
                for (int ndx = 0; ndx < dead.Count; ndx++)
                {
                    EnqueueWeatherSteps(match, WeatherTrigger.CritterDies, deadSeats[ndx]);
                    EnqueueCardSteps(match, match.Rules.Instance(dead[ndx]), CardTrigger.Goodbye, deadSeats[ndx], snapshots[ndx], EffectTargetRef.None, excludesSourceFromPlayedCount: false);
                }
            }

            throw new MatchEngineException($"The death sweep did not reach a stable board within {MaxSweepPasses} passes");
        }

        // ---------------------------------------------------------------- winning and losing

        /// <summary>
        /// A Den at zero ends the game; both at zero in one resolution is a draw with no winner and therefore
        /// no Heist. Checked once per resolution rather than the instant a number changes, which is a ruling
        /// and not an implementation detail.
        /// </summary>
        public static void CheckDens(MatchModel match)
        {
            MatchRulesState rules = match.Rules;

            if (rules.Phase == MatchPhase.Complete)
                return;

            bool seat0Dead = rules.Seat(0).DenHp <= 0;
            bool seat1Dead = rules.Seat(1).DenHp <= 0;

            if (!seat0Dead && !seat1Dead)
                return;

            MatchOutcome  outcome;
            int           winner;
            MatchEndCause cause;

            if (seat0Dead && seat1Dead)
            {
                outcome = MatchOutcome.Draw;
                winner  = MatchSeats.None;
                cause   = MatchEndCause.BothDensAtZero;
            }
            else if (seat0Dead)
            {
                outcome = MatchOutcome.Seat1Wins;
                winner  = 1;
                cause   = MatchEndCause.DenAtZero;
            }
            else
            {
                outcome = MatchOutcome.Seat0Wins;
                winner  = 0;
                cause   = MatchEndCause.DenAtZero;
            }

            MatchResult result = new MatchResult(
                outcome,
                winner,
                new List<HeistEligibleCard>(rules.Seat(0).PlayedFromOwnDeck),
                new List<HeistEligibleCard>(rules.Seat(1).PlayedFromOwnDeck),
                rules.Turn,
                cause);

            EndGame(match, result);
        }

        /// <summary> Record the result and stop the game: nothing is queued, held or timed after this. </summary>
        public static void EndGame(MatchModel match, MatchResult result)
        {
            MatchRulesState rules = match.Rules;
            rules.SetResult(result);
            rules.SetPhase(MatchPhase.Complete);
            rules.EffectQueue.Clear();
            rules.SetPendingChoice(null);
            rules.SetContinuation(ResolutionContinuation.None);
            match.Pacing.ClearDeadline();
            match.Emit(new MatchEndedEvent(result));
        }

        // ---------------------------------------------------------------- damage and healing

        /// <summary>
        /// One damage instance. A Bubble ignores the whole first instance above zero and then pops;
        /// a step whose target no longer exists resolves as a no-op, never an error.
        /// <para>
        /// Returns how much this critter <em>dealt</em>, which is the swing it landed on something — zero
        /// when the target was already gone. A Bubble that swallowed it does not make it zero: the swing
        /// happened and the whole board saw it, which is what Snacktime and the Sneaky reveal key off.
        /// </para>
        /// </summary>
        public static int ApplyDamage(MatchModel match, EffectTargetRef target, int amount, CardInstanceId source)
        {
            if (amount <= 0)
                return 0;

            if (target.IsDen)
            {
                if (!MatchSeats.IsValid(target.DenSeat))
                    return 0;

                match.Emit(new DamageDealtEvent(source, target, amount, false));
                DamageDen(match, target.DenSeat, amount, source);
                return amount;
            }

            BoardCritter critter = FindCritterAnywhere(match.Rules, target.Critter);
            if (critter == null)
                return 0;

            if (KeywordRules.BubbleAbsorbs(critter, amount))
            {
                critter.PopBubble();
                match.Emit(new DamageDealtEvent(source, target, amount, true));
                match.Emit(new BubblePoppedEvent(critter.Id));
                return amount;
            }

            critter.AddDamage(amount);
            match.Emit(new DamageDealtEvent(source, target, amount, false));
            return amount;
        }

        public static void DamageDen(MatchModel match, int seat, int amount, CardInstanceId source)
        {
            if (amount <= 0)
                return;

            SeatState state = match.Rules.Seat(seat);
            int       hp    = state.DenHp - amount;
            if (hp < 0)
                hp = 0;

            state.SetDenHp(hp);
            match.Emit(new DenDamagedEvent(seat, amount, hp));
        }

        /// <summary> Healing a Den caps at its starting hit points; healing a critter at its maximum. </summary>
        public static void ApplyHeal(MatchModel match, EffectTargetRef target, int amount)
        {
            if (amount <= 0)
                return;

            if (target.IsDen)
            {
                if (MatchSeats.IsValid(target.DenSeat))
                    HealDen(match, target.DenSeat, amount);
                return;
            }

            BoardCritter critter = FindCritterAnywhere(match.Rules, target.Critter);
            if (critter == null)
                return;

            int healed = HealableOnCritter(critter.Damage, amount);
            if (healed <= 0)
                return;

            critter.RemoveDamage(healed);
            match.Emit(new HealedEvent(target, healed, critter.CurrentHealth));
        }

        public static void HealDen(MatchModel match, int seat, int amount)
        {
            SeatState state  = match.Rules.Seat(seat);
            int       healed = HealableOnDen(state.DenHp, amount, match.Content.Global.DenStartingHp);
            if (healed <= 0)
                return;

            state.SetDenHp(state.DenHp + healed);
            match.Emit(new HealedEvent(EffectTargetRef.Den(seat), healed, state.DenHp));
        }

        /// <summary>
        /// How much of <paramref name="amount"/> a Den at <paramref name="denHp"/> can actually take: healing
        /// a Den caps at its starting hit points. Its own function because
        /// <see cref="HealPreview"/> answers the player's "what would this do" with the same arithmetic the
        /// resolution runs, so the two cannot disagree.
        /// </summary>
        public static int HealableOnDen(int denHp, int amount, int cap)
            => denHp + amount > cap ? cap - denHp : amount;

        /// <summary> How much of <paramref name="amount"/> a critter carrying this much damage can take. </summary>
        public static int HealableOnCritter(int damage, int amount)
            => amount < damage ? amount : damage;

        /// <summary> The critter with this identity on either board, or null. </summary>
        public static BoardCritter FindCritterAnywhere(MatchRulesState rules, CardInstanceId id)
        {
            if (!id.IsValid)
                return null;

            for (int seat = 0; seat < MatchSeats.Count; seat++)
            {
                BoardCritter critter = rules.Seat(seat).FindCritter(id);
                if (critter != null)
                    return critter;
            }

            return null;
        }

        // ---------------------------------------------------------------- effect mutations

        public static void SummonCritters(MatchModel match, int seat, CardInfo card, int count)
        {
            SeatState state    = match.Rules.Seat(seat);
            int       capacity = match.Content.Global.MaxBoardCritters - state.Board.Count;
            int       summoned = count < capacity ? count : capacity;

            for (int ndx = 0; ndx < summoned; ndx++)
            {
                // Public from creation: a token was never in anybody's unseen pool, so making it public must
                // not take a card out of one. Public also means a follower mints the same instance from the
                // same config at the same registry index.
                CardInstance instance = ZoneOps.Mint(match, card, match.Content.Global.RankMin, seat, CardPlace.Limbo, isPublic: true, fromStartingDeck: false);
                ZoneOps.ToBoard(match, instance);
            }

            if (summoned < count)
                match.Emit(new SummonFizzledEvent(seat, card.CardId, count - summoned, SummonFizzleReason.BoardFull));
        }

        public static void BuffCritter(MatchModel match, CardInstanceId id, int attackDelta, int maxHealthDelta)
        {
            BoardCritter critter = FindCritterAnywhere(match.Rules, id);
            if (critter == null)
                return;

            critter.Buff(attackDelta, maxHealthDelta);
            match.Emit(new StatsChangedEvent(critter.Id, critter.Attack, critter.MaxHealth, critter.Damage));
        }

        public static void GrantKeywordsTo(MatchModel match, CardInstanceId id, KeywordFlags keywords)
        {
            BoardCritter critter = FindCritterAnywhere(match.Rules, id);
            if (critter == null || keywords == KeywordFlags.None)
                return;

            KeywordFlags before = critter.Keywords;
            critter.AddKeywords(keywords);

            if ((keywords & KeywordFlags.Bubble) != 0 && (before & KeywordFlags.Bubble) == 0)
                critter.RestoreBubble();

            if (critter.Keywords != before)
                match.Emit(new KeywordsChangedEvent(critter.Id, critter.Keywords));
        }

        public static void GainMana(MatchModel match, int seat, int amount, ManaDuration duration)
        {
            if (amount == 0)
                return;

            SeatState state = match.Rules.Seat(seat);

            // Temporary and permanent mana are the same primitive with a parameter, and the parameter decides
            // which number moves. Permanent raises the maximum and nothing else — "gain +1 max mana
            // permanently" is a ramp, payable from the next refill, not a ramp plus an acorn today. Turn
            // grants current mana only, which may take it above the maximum.
            if (duration == ManaDuration.Permanent)
            {
                state.SetMaxMana(state.MaxMana + amount);
            }
            else
            {
                int mana = state.Mana + amount;
                state.SetMana(mana < 0 ? 0 : mana);
            }

            match.Emit(new ManaChangedEvent(seat, state.Mana, state.MaxMana));
        }

        public static void DrawCards(MatchModel match, int seat, int count)
        {
            for (int ndx = 0; ndx < count; ndx++)
                ZoneOps.Draw(match, seat);
        }

        public static void BounceCritter(MatchModel match, CardInstanceId id) => ZoneOps.Bounce(match, id);

        /// <summary>
        /// A fresh instance of a catalogue card, straight into a hand. A copy out of a public graveyard is
        /// public from creation — both players could have computed which card it is — and a copy is never in
        /// anybody's unseen pool, so making it public must not take a card out of one.
        /// </summary>
        public static void AddCopyToHand(MatchModel match, int seat, CardInfo card)
        {
            CardInstance instance = ZoneOps.Mint(match, card, match.Content.Global.RankMin, seat, CardPlace.Limbo, isPublic: true, fromStartingDeck: false);
            ZoneOps.AddToHand(match, instance);
        }
    }
}
