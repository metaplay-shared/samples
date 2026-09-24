using System.Collections.Generic;

namespace Game.Logic.Tests
{
    /// <summary>
    /// The invariant catalog, walked after every accepted action and every engine-driven step of every
    /// self-play game — so a violation is caught at the position it happened rather than once at the end
    /// (<c>Docs/bots.md</c>, "The invariant catalog").
    /// <para>
    /// Each check reports a message rather than asserting, so the runner can attach the failing game's seed
    /// and action script to it before anything is thrown. The conservation half needs the state on both sides
    /// of a step, which is why this is an object with a <see cref="Capture"/> and a <see cref="Check"/> rather
    /// than a static predicate: it holds the "before" of the step being checked and nothing else.
    /// </para>
    /// <para>
    /// The engine's own determinism suite walks the same object. Two implementations of "what must always be
    /// true of a match" would disagree eventually, and the one that disagreed silently would be the one
    /// nobody was reading.
    /// </para>
    /// </summary>
    public sealed class InvariantWalk
    {
        readonly SharedGameConfig _config;
        readonly SelfPlayChecks   _checks;
        readonly bool             _dealtFromFullDecks;

        readonly int[] _denHp   = new int[MatchSeats.Count];
        readonly int[] _mana    = new int[MatchSeats.Count];
        readonly int[] _maxMana = new int[MatchSeats.Count];
        /// <summary>
        /// Whether a seat is currently holding mana that may legitimately sit above its maximum. It is
        /// cleared at that seat's <em>own</em> turn start, because the refill is what takes the grant back —
        /// unspent mana survives the opponent's whole turn in between.
        /// </summary>
        readonly bool[] _holdsGrantedMana = new bool[MatchSeats.Count];

        int[]  _damageBefore  = new int[64];
        bool[] _onBoardBefore = new bool[64];
        int[]  _damageDelta   = new int[64];
        int[]  _zoneSeen      = new int[64];
        /// <summary> Where each instance was before the step, so C6 can ask what moved and why. </summary>
        int[]  _zoneBefore    = new int[64];
        /// <summary> How many instances existed before the step; anything past it was minted during it. </summary>
        int    _knownBefore;

        int _highestMaxMana0;
        int _highestMaxMana1;

        /// <summary>
        /// <paramref name="dealtFromFullDecks"/> says whether the game under the walk started from a real deal
        /// rather than from a hand-built board. Only a dealt game can be held to the starting-deck census, and
        /// a scenario fixture that builds five cards is not a game that lost twenty.
        /// </summary>
        public InvariantWalk(SharedGameConfig config, SelfPlayChecks checks, bool dealtFromFullDecks = false)
        {
            _config             = config;
            _checks             = checks;
            _dealtFromFullDecks = dealtFromFullDecks;
        }

        /// <summary> The turn ceiling Tuckered Out implies, derived from content rather than pinned here. </summary>
        public int TurnCeiling => TuckeredOutTurnCeiling(_config.Global);

        /// <summary>
        /// How long a game can possibly last: the turns it takes to empty a deck when the *smaller* opening
        /// hand took the fewest cards out of it, plus the refused draws whose escalating damage adds up to a
        /// full Den, for both seats, and a turn of slack. Nothing about the real games comes near it — it is
        /// the bound that says a game ends because the rules end it rather than because a harness got bored
        /// (<c>Docs/game-design.md</c>, "Ending the game").
        /// </summary>
        public static int TuckeredOutTurnCeiling(GlobalConfig global)
        {
            int drawsToEmpty = global.DeckSize - global.OpeningHandFirstPlayer;

            int ticks = 0;
            int dealt = 0;
            while (dealt < global.DenStartingHp)
            {
                dealt += global.TuckeredOutFirstDamage + ticks * global.TuckeredOutIncrement;
                ticks++;
            }

            return (drawsToEmpty + ticks + 1) * MatchSeats.Count;
        }

        // ---------------------------------------------------------------- the before half

        public void Capture(MatchEngine engine)
        {
            EnsureCapacity(engine.Rules.Instances.Count);

            for (int seat = 0; seat < MatchSeats.Count; seat++)
            {
                SeatState state = engine.Rules.Seat(seat);
                _denHp[seat]   = state.DenHp;
                _mana[seat]    = state.Mana;
                _maxMana[seat] = state.MaxMana;
            }

            for (int ndx = 0; ndx < _onBoardBefore.Length; ndx++)
                _onBoardBefore[ndx] = false;

            for (int seat = 0; seat < MatchSeats.Count; seat++)
            {
                foreach (BoardCritter critter in engine.Rules.Seat(seat).Board)
                {
                    _onBoardBefore[critter.Id.Value] = true;
                    _damageBefore[critter.Id.Value]  = critter.Damage;
                }
            }

            _knownBefore = engine.Rules.Instances.Count;
            for (int ndx = 0; ndx < _knownBefore; ndx++)
                _zoneBefore[ndx] = (int)AuthorityZones.Of(engine.Model, engine.Rules.Instances[ndx]);
        }

        void EnsureCapacity(int instances)
        {
            if (_damageBefore.Length >= instances)
                return;

            int size = _damageBefore.Length;
            while (size < instances)
                size *= 2;

            System.Array.Resize(ref _damageBefore, size);
            System.Array.Resize(ref _onBoardBefore, size);
            System.Array.Resize(ref _damageDelta, size);
            System.Array.Resize(ref _zoneSeen, size);
            System.Array.Resize(ref _zoneBefore, size);
        }

        // ---------------------------------------------------------------- the after half

        /// <summary> Null when everything held; the first violation's message otherwise. </summary>
        public string Check(MatchEngine engine, IReadOnlyList<MatchEvent> events)
        {
            EnsureCapacity(engine.Rules.Instances.Count);
            NoteTemporaryMana(engine, events);

            if ((_checks & SelfPlayChecks.StateSanity) != 0)
            {
                string sanity = CheckStateSanity(engine);
                if (sanity != null)
                    return sanity;
            }

            if ((_checks & SelfPlayChecks.Conservation) != 0)
            {
                string conservation = CheckConservation(engine, events);
                if (conservation != null)
                    return conservation;
            }

            if ((_checks & SelfPlayChecks.Termination) != 0)
            {
                string termination = CheckTermination(engine);
                if (termination != null)
                    return termination;
            }

            return null;
        }

        /// <summary>
        /// Mana above the maximum is legal only for the turn a card or the Weather granted it in, so the
        /// grant has to be on the record before the cap is judged. It is read off the resolved-step events
        /// rather than off the card, which keeps this honest for a Weather's grant too.
        /// </summary>
        void NoteTemporaryMana(MatchEngine engine, IReadOnlyList<MatchEvent> events)
        {
            // In order: a turn start clears the seat's grant, and a grant inside that same start-of-turn
            // package sets it again.
            for (int ndx = 0; ndx < events.Count; ndx++)
            {
                switch (events[ndx])
                {
                    case TurnStartedEvent started:
                        _holdsGrantedMana[started.Seat] = false;
                        break;

                    case EffectResolvedEvent resolved:
                        if (_config.EffectSteps.TryGetValue(resolved.Step, out EffectStepInfo step)
                            && step.Op == EffectOp.GainMana
                            && step.Duration == ManaDuration.Turn)
                        {
                            _holdsGrantedMana[resolved.ResolvingSeat] = true;
                        }
                        break;
                }
            }
        }

        // ---------------------------------------------------------------- state sanity (S1-S8)

        string CheckStateSanity(MatchEngine engine)
        {
            GlobalConfig global = _config.Global;

            for (int seat = 0; seat < MatchSeats.Count; seat++)
            {
                SeatState state = engine.Rules.Seat(seat);

                if (engine.SecretHand(seat).Count > global.MaxHandSize)
                    return $"S1: seat {seat} holds {engine.SecretHand(seat).Count} cards, over the hand limit of {global.MaxHandSize}";

                if (state.Board.Count > global.MaxBoardCritters)
                    return $"S2: seat {seat} has {state.Board.Count} critters, over the board limit of {global.MaxBoardCritters}";

                if (state.DenHp < 0)
                    return $"S3: seat {seat}'s Den is at {state.DenHp}";

                if (state.Mana < 0)
                    return $"S4: seat {seat} has {state.Mana} mana";

                if (state.Mana > state.MaxMana && !_holdsGrantedMana[seat])
                    return $"S4: seat {seat} has {state.Mana} of {state.MaxMana} mana with no grant on record since its last refill";

                int highest = seat == 0 ? _highestMaxMana0 : _highestMaxMana1;
                if (state.MaxMana < highest)
                    return $"S5: seat {seat}'s maximum mana fell from {highest} to {state.MaxMana}";

                if (seat == 0)
                    _highestMaxMana0 = state.MaxMana;
                else
                    _highestMaxMana1 = state.MaxMana;

                foreach (BoardCritter critter in state.Board)
                {
                    if (critter.Attack < 0 || critter.MaxHealth < 0)
                        return $"S7: seat {seat}'s {critter.Id} is {critter.Attack}/{critter.MaxHealth}";

                    // The drain is over by the time an observation is taken, so a critter at or below zero
                    // health should already have been swept off the board.
                    if (engine.Rules.PendingChoice == null && critter.CurrentHealth <= 0)
                        return $"S7: seat {seat}'s {critter.Id} survived a drained resolution at {critter.CurrentHealth} health";
                }

                // The pool is maintained incrementally; this is what the field is checked against.
                List<CardId> derived = engine.DeriveUnseenPool(seat);
                if (derived.Count != state.UnseenPool.Count)
                    return $"S8: seat {seat}'s unseen pool holds {state.UnseenPool.Count} cards, derived {derived.Count}";

                for (int ndx = 0; ndx < derived.Count; ndx++)
                {
                    if (derived[ndx] != state.UnseenPool[ndx])
                        return $"S8: seat {seat}'s unseen pool drifted from its derivation at index {ndx}";
                }
            }

            // The registry is index-keyed, which is what makes two live cards sharing an identity impossible —
            // provided the index and the identity have not come apart.
            for (int ndx = 0; ndx < engine.Rules.Instances.Count; ndx++)
            {
                if (engine.Rules.Instances[ndx].Id.Value != ndx)
                    return $"S6: registry slot {ndx} holds {engine.Rules.Instances[ndx].Id}";
            }

            return null;
        }

        // ---------------------------------------------------------------- conservation (C1-C5)

        string CheckConservation(MatchEngine engine, IReadOnlyList<MatchEvent> events)
        {
            string zones = CheckZones(engine);
            if (zones != null)
                return zones;

            string mana = CheckMana(engine, events);
            if (mana != null)
                return mana;

            string damage = CheckDamage(engine, events);
            if (damage != null)
                return damage;

            return CheckTransitions(engine, events);
        }

        /// <summary>
        /// C1 and C2. The counts have to add up <em>and</em> every identity has to be findable in exactly the
        /// zone its own record names — a swap that preserved the counts while duplicating one card and losing
        /// another would pass the first check and fail the second.
        /// </summary>
        string CheckZones(MatchEngine engine)
        {
            for (int ndx = 0; ndx < _zoneSeen.Length; ndx++)
                _zoneSeen[ndx] = -1;

            for (int seat = 0; seat < MatchSeats.Count; seat++)
            {
                SeatState   state  = engine.Rules.Seat(seat);
                SeatSecrets secret = engine.Model.SecretSeat(seat);

                // C1b. The stored public counts are what a follower branches on and what the opponent's card
                // backs are drawn from, and nothing derives them any more — every zone op maintains them by
                // hand. So the invariant the whole stored-count decision needs is checked here, at every step
                // of every game: the numbers equal the lists they stand for.
                if (secret.Hand.Count != state.HandCount)
                    return $"C1b: seat {seat} holds {secret.Hand.Count} cards but its HandCount says {state.HandCount}";
                if (secret.Deck.Count != state.DeckCount)
                    return $"C1b: seat {seat} has {secret.Deck.Count} cards in its deck but its DeckCount says {state.DeckCount}";

                string deck = MarkZone(secret.Deck, AuthorityZone.Deck, seat);
                if (deck != null)
                    return deck;

                string hand = MarkZone(secret.Hand, AuthorityZone.Hand, seat);
                if (hand != null)
                    return hand;

                string graveyard = MarkZone(state.Graveyard, AuthorityZone.Graveyard, seat);
                if (graveyard != null)
                    return graveyard;

                foreach (BoardCritter critter in state.Board)
                {
                    string board = MarkOne(critter.Id, AuthorityZone.Board, seat);
                    if (board != null)
                        return board;
                }

                int inZones     = secret.Deck.Count + secret.Hand.Count + state.Board.Count + state.Graveyard.Count;
                int owned       = 0;
                int fromOwnDeck = 0;
                foreach (CardInstance instance in engine.Rules.Instances)
                {
                    if (instance.Owner != seat)
                        continue;
                    if (AuthorityZones.Of(engine.Model, instance) != AuthorityZone.Limbo)
                        owned++;
                    if (instance.FromStartingDeck)
                        fromOwnDeck++;
                }

                if (inZones != owned)
                    return $"C1: seat {seat} has {inZones} cards across its zones but owns {owned}";

                // Nothing creates or destroys a card that came out of a starting deck. Tokens, copies and the
                // second player's Acorn are the only instances that ever appear, and none of them is one.
                if (_dealtFromFullDecks && fromOwnDeck != _config.Global.DeckSize)
                    return $"C1: seat {seat} accounts for {fromOwnDeck} of its {_config.Global.DeckSize} starting-deck cards";
            }

            foreach (CardInstance instance in engine.Rules.Instances)
            {
                AuthorityZone zone = AuthorityZones.Of(engine.Model, instance);
                if (zone == AuthorityZone.Limbo)
                    continue;

                if (_zoneSeen[instance.Id.Value] < 0)
                    return $"C2: {instance.Id} says it is in the {zone} but is in no zone list";

                if ((AuthorityZone)_zoneSeen[instance.Id.Value] != zone)
                    return $"C2: {instance.Id} says it is in the {zone} but was found in the {(AuthorityZone)_zoneSeen[instance.Id.Value]}";
            }

            return null;
        }

        string MarkZone(List<CardInstanceId> zone, AuthorityZone kind, int seat)
        {
            foreach (CardInstanceId id in zone)
            {
                string duplicate = MarkOne(id, kind, seat);
                if (duplicate != null)
                    return duplicate;
            }

            return null;
        }

        string MarkOne(CardInstanceId id, AuthorityZone kind, int seat)
        {
            if (_zoneSeen[id.Value] >= 0)
                return $"C2: {id} is in the {(AuthorityZone)_zoneSeen[id.Value]} and the {kind} at once";

            _zoneSeen[id.Value] = (int)kind;
            return null;
        }

        /// <summary>
        /// C3 and C4. Every mana movement in the engine emits an event, so the delta across a step is
        /// reconstructible from the step itself: the costs of what was played, the start-of-turn ramp, and any
        /// grant a resolved step made.
        /// </summary>
        string CheckMana(MatchEngine engine, IReadOnlyList<MatchEvent> events)
        {
            for (int seat = 0; seat < MatchSeats.Count; seat++)
            {
                int  spent           = 0;
                bool turnStarted     = false;
                bool gainedForTurn   = false;
                bool gainedPermanent = false;

                for (int ndx = 0; ndx < events.Count; ndx++)
                {
                    switch (events[ndx])
                    {
                        case CardPlayedEvent played when played.Seat == seat:
                            spent += played.ManaSpent;
                            break;

                        case TurnStartedEvent started when started.Seat == seat:
                            turnStarted = true;
                            break;

                        case EffectResolvedEvent resolved when resolved.ResolvingSeat == seat:
                            // Permanent raises the maximum and leaves current mana alone; Turn does the
                            // opposite. They relax different halves of the accounting.
                            if (_config.EffectSteps.TryGetValue(resolved.Step, out EffectStepInfo step) && step.Op == EffectOp.GainMana)
                            {
                                if (step.Duration == ManaDuration.Permanent)
                                    gainedPermanent = true;
                                else
                                    gainedForTurn = true;
                            }
                            break;
                    }
                }

                SeatState state = engine.Rules.Seat(seat);

                if (turnStarted)
                {
                    int ramped = _maxMana[seat] + _config.Global.ManaGainPerTurn;

                    if (!gainedPermanent && state.MaxMana != ramped)
                        return $"C4: seat {seat}'s maximum mana went {_maxMana[seat]} to {state.MaxMana}, not the ramp's {ramped}";
                    if (gainedPermanent && state.MaxMana < ramped)
                        return $"C4: seat {seat}'s maximum mana went {_maxMana[seat]} to {state.MaxMana}, below the ramp's {ramped}";

                    // The refill sets current to the maximum it had at the refill, and unspent mana does not
                    // carry. A permanent grant later in the same step raises the maximum only, so the
                    // refill is measured against the ramp rather than against where the maximum ended up.
                    if (!gainedForTurn && state.Mana != ramped - spent)
                        return $"C4: seat {seat} refilled to {state.Mana}, not to {ramped - spent}";

                    continue;
                }

                if (state.MaxMana != _maxMana[seat] && !gainedPermanent)
                    return $"C4: seat {seat}'s maximum mana moved from {_maxMana[seat]} to {state.MaxMana} outside a turn start";

                if (!gainedForTurn && state.Mana != _mana[seat] - spent)
                    return $"C3: seat {seat}'s mana went {_mana[seat]} to {state.Mana}, but {spent} was the whole cost of what it played";

                if (gainedForTurn && state.Mana < _mana[seat] - spent)
                    return $"C3: seat {seat}'s mana went {_mana[seat]} to {state.Mana}, below the {_mana[seat] - spent} its plays left it";
            }

            return null;
        }

        /// <summary>
        /// C5. Health only ever moves through damage and healing, and both announce themselves, so a critter's
        /// damage across a step must be exactly what the step said happened to it. A critter that entered or
        /// left play inside the step has no before-and-after pair and is not checked here; C2 is what covers
        /// its move between zones.
        /// </summary>
        string CheckDamage(MatchEngine engine, IReadOnlyList<MatchEvent> events)
        {
            for (int ndx = 0; ndx < _damageDelta.Length; ndx++)
                _damageDelta[ndx] = 0;

            int[]  denAfterEvent = { -1, -1 };
            bool[] denTouched    = { false, false };

            for (int ndx = 0; ndx < events.Count; ndx++)
            {
                switch (events[ndx])
                {
                    case DamageDealtEvent damage:
                        if (damage.Target.IsCritter && !damage.AbsorbedByBubble && damage.Target.Critter.Value < _damageDelta.Length)
                            _damageDelta[damage.Target.Critter.Value] += damage.Amount;
                        break;

                    case HealedEvent healed:
                        if (healed.Target.IsCritter && healed.Target.Critter.Value < _damageDelta.Length)
                            _damageDelta[healed.Target.Critter.Value] -= healed.Amount;
                        else if (healed.Target.IsDen && MatchSeats.IsValid(healed.Target.DenSeat))
                        {
                            denAfterEvent[healed.Target.DenSeat] = healed.NewValue;
                            denTouched[healed.Target.DenSeat]    = true;
                        }
                        break;

                    case DenDamagedEvent denDamage:
                        denAfterEvent[denDamage.Seat] = denDamage.NewHp;
                        denTouched[denDamage.Seat]    = true;
                        break;
                }
            }

            for (int seat = 0; seat < MatchSeats.Count; seat++)
            {
                SeatState state = engine.Rules.Seat(seat);

                // A Den is only ever written by the two calls that announce the number they wrote, so the last
                // word on the record is what the Den must hold.
                if (denTouched[seat])
                {
                    if (state.DenHp != denAfterEvent[seat])
                        return $"C5: seat {seat}'s Den is at {state.DenHp}, but the last event about it said {denAfterEvent[seat]}";
                }
                else if (state.DenHp != _denHp[seat])
                    return $"C5: seat {seat}'s Den went {_denHp[seat]} to {state.DenHp} with nothing on the record";

                foreach (BoardCritter critter in state.Board)
                {
                    int id = critter.Id.Value;
                    if (!_onBoardBefore[id])
                        continue;

                    // Not clamped at zero on purpose: healing is capped at the damage present before it is
                    // announced, so a step whose events add up to negative damage is a step that over-claimed
                    // a heal, and clamping would hide exactly that.
                    int expected = _damageBefore[id] + _damageDelta[id];

                    if (critter.Damage != expected)
                        return $"C5: {critter.Id} carries {critter.Damage} damage, but the step accounts for {expected}";
                }
            }

            return null;
        }

        /// <summary>
        /// C6. Every card that moved between zones, and every card that came into existence, has to be
        /// accounted for by something the step announced.
        /// <para>
        /// This is the card-shaped analogue of the mana and damage laws, and the reason it is not redundant
        /// with C1 and C2: those two ask whether the state agrees with <em>itself</em>, and a card that slid
        /// from a deck into a hand with no draw on the record agrees with itself perfectly. The client rebuilds
        /// its board from the event stream, so a move nobody announced is a move the client will never make.
        /// </para>
        /// <para>
        /// Two kinds of justification, because the engine has two kinds of announcement. Events that name an
        /// instance are matched to that instance; events that only carry a count — a draw, an overflow, a
        /// mulligan's replacements — are credits against that seat, consumed one per move, so two cards moving
        /// on one draw event is still caught.
        /// </para>
        /// </summary>
        string CheckTransitions(MatchEngine engine, IReadOnlyList<MatchEvent> events)
        {
            List<CardInstanceId> enteredPlay = new List<CardInstanceId>();
            List<CardInstanceId> died        = new List<CardInstanceId>();
            List<CardInstanceId> bounced     = new List<CardInstanceId>();
            List<CardInstanceId> played      = new List<CardInstanceId>();
            List<CardInstanceId> namedToHand = new List<CardInstanceId>();

            int[] drawCredits    = new int[MatchSeats.Count];
            int[] toHandCredits  = new int[MatchSeats.Count];
            int[] toDeckCredits  = new int[MatchSeats.Count];
            // One replacement is two moves — the card put back and the card drawn for it — so the two
            // directions get their own credit rather than sharing one and running out halfway.
            int[] mulliganBack   = new int[MatchSeats.Count];
            int[] mulliganRedraw = new int[MatchSeats.Count];

            for (int ndx = 0; ndx < events.Count; ndx++)
            {
                switch (events[ndx])
                {
                    case CritterEnteredPlayEvent entered: enteredPlay.Add(entered.Instance); break;
                    case CritterDiedEvent        dead:    died.Add(dead.Instance);           break;
                    case CardBouncedEvent        bounce:  bounced.Add(bounce.Instance);      break;
                    case CardPlayedEvent         play:    played.Add(play.Instance);         break;

                    case CardDrawnEvent drawn:
                        drawCredits[drawn.Seat]++;
                        break;

                    case CardAddedToHandEvent added:
                        // The identity travels only when the card is already public; a peeked card arrives
                        // with none, which is what the seat credit is for.
                        if (added.Instance.IsValid)
                            namedToHand.Add(added.Instance);
                        else
                            toHandCredits[added.Seat]++;
                        break;

                    case DrawOverflowedEvent overflowed:
                        toDeckCredits[overflowed.Seat]++;
                        break;

                    case MulliganResolvedEvent mulligan:
                        mulliganBack[mulligan.Seat]   += mulligan.ReplacedCount;
                        mulliganRedraw[mulligan.Seat] += mulligan.ReplacedCount;
                        break;
                }
            }

            for (int ndx = 0; ndx < engine.Rules.Instances.Count; ndx++)
            {
                CardInstance  instance = engine.Rules.Instances[ndx];
                AuthorityZone after    = AuthorityZones.Of(engine.Model, instance);
                // Anything minted during the step started nowhere, which is what Limbo means here.
                AuthorityZone     before   = ndx < _knownBefore ? (AuthorityZone)_zoneBefore[ndx] : AuthorityZone.Limbo;

                if (before == after)
                    continue;

                if (Justified(instance, before, after, enteredPlay, died, bounced, played, namedToHand,
                        drawCredits, toHandCredits, toDeckCredits, mulliganBack, mulliganRedraw))
                {
                    continue;
                }

                return $"C6: {instance.Id} moved from the {before} to the {after} with nothing on the record";
            }

            return null;
        }

        static bool Justified(
            CardInstance instance, AuthorityZone before, AuthorityZone after,
            List<CardInstanceId> enteredPlay, List<CardInstanceId> died, List<CardInstanceId> bounced,
            List<CardInstanceId> played, List<CardInstanceId> namedToHand,
            int[] drawCredits, int[] toHandCredits, int[] toDeckCredits, int[] mulliganBack, int[] mulliganRedraw)
        {
            int            seat = instance.Owner;
            CardInstanceId id   = instance.Id;

            switch (after)
            {
                case AuthorityZone.Board:
                    return enteredPlay.Remove(id);

                case AuthorityZone.Graveyard:
                    // A death names its critter; a trick reaches its graveyard as the card that was played.
                    return died.Remove(id) || played.Remove(id);

                case AuthorityZone.Hand:
                    if (bounced.Remove(id) || namedToHand.Remove(id))
                        return true;
                    if (before == AuthorityZone.Deck && drawCredits[seat] > 0)
                    {
                        drawCredits[seat]--;
                        return true;
                    }
                    if (toHandCredits[seat] > 0)
                    {
                        toHandCredits[seat]--;
                        return true;
                    }
                    if (before == AuthorityZone.Deck && mulliganRedraw[seat] > 0)
                    {
                        mulliganRedraw[seat]--;
                        return true;
                    }
                    return false;

                case AuthorityZone.Deck:
                    // A hand that was full sends the card to the bottom instead, and the mulligan puts back
                    // whatever it was asked to.
                    if (bounced.Remove(id) && toDeckCredits[seat] > 0)
                    {
                        toDeckCredits[seat]--;
                        return true;
                    }
                    if (toDeckCredits[seat] > 0)
                    {
                        toDeckCredits[seat]--;
                        return true;
                    }
                    if (before == AuthorityZone.Hand && mulliganBack[seat] > 0)
                    {
                        mulliganBack[seat]--;
                        return true;
                    }
                    return false;

                default:
                    return false;
            }
        }

        // ---------------------------------------------------------------- termination (T1, T2)

        string CheckTermination(MatchEngine engine)
        {
            if (engine.Turn > TurnCeiling)
                return $"T1: the game reached turn {engine.Turn}, past the Tuckered Out ceiling of {TurnCeiling}";

            // The queue is held only for a seat's peek choice. Anywhere else, a resolution that has been
            // handed back to the host has drained.
            if (engine.Rules.PendingChoice == null)
            {
                if (engine.Rules.EffectQueue.Count != 0)
                    return $"T2: {engine.Rules.EffectQueue.Count} effect steps are queued with no resolution held";

                if (engine.Rules.Continuation != ResolutionContinuation.None)
                    return $"T2: a resolution still owes {engine.Rules.Continuation} with an empty queue";
            }

            return null;
        }

        // ---------------------------------------------------------------- termination (T3)

        /// <summary> The end state is one of exactly three shapes, and never anything else. </summary>
        public static string CheckTerminalState(MatchEngine engine)
        {
            if (engine.Phase != MatchPhase.Complete)
                return "T1: the game did not terminate";

            MatchResult result = engine.Result;
            if (result == null)
                return "T3: the game is complete with no result";

            int den0 = engine.Rules.Seat(0).DenHp;
            int den1 = engine.Rules.Seat(1).DenHp;

            switch (result.Outcome)
            {
                case MatchOutcome.Seat0Wins:
                    if (!(den1 == 0 && den0 > 0))
                        return $"T3: seat 0 won with Dens at {den0} and {den1}";
                    if (result.WinnerSeat != 0 || result.Cause != MatchEndCause.DenAtZero)
                        return $"T3: seat 0 won as winner {result.WinnerSeat} by {result.Cause}";
                    return null;

                case MatchOutcome.Seat1Wins:
                    if (!(den0 == 0 && den1 > 0))
                        return $"T3: seat 1 won with Dens at {den0} and {den1}";
                    if (result.WinnerSeat != 1 || result.Cause != MatchEndCause.DenAtZero)
                        return $"T3: seat 1 won as winner {result.WinnerSeat} by {result.Cause}";
                    return null;

                case MatchOutcome.Draw:
                    if (!(den0 == 0 && den1 == 0))
                        return $"T3: a draw with Dens at {den0} and {den1}";
                    if (result.WinnerSeat != MatchSeats.None || result.Cause != MatchEndCause.BothDensAtZero)
                        return $"T3: a draw with winner {result.WinnerSeat} by {result.Cause}";
                    return null;

                default:
                    return $"T3: the outcome is {result.Outcome}";
            }
        }
    }
}
