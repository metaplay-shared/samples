using Metaplay.Core;
using Metaplay.Core.Model;
using Metaplay.Core.Serialization;
using System.Collections.Generic;

namespace Game.Logic.Tests
{
    /// <summary>
    /// A host for one <see cref="MatchModel"/>, as a test drives one.
    /// <para>
    /// <b>Production has no such type.</b> The match actor submits actions and reads the model; this exists so
    /// that the rules are testable with no server, no client and no network (<c>Docs/rules.md</c>).
    /// It prepares each intent and stages the action it produced, as the actor does, with the actor's own
    /// concerns (refusing a client, timers) left out.
    /// </para>
    /// <para>
    /// <b>The model's clock is driven by ticks</b>, as it is on the server: a new model starts at
    /// <see cref="MetaTime.Epoch"/>, <see cref="AdvanceTime"/> ticks it forward, and a lapsed deadline first
    /// ticks the model up to its stamp. Every tick is mirrored to the follower.
    /// </para>
    /// </summary>
    public sealed class MatchEngine
    {
        readonly MatchModel        _model;
        readonly List<MatchEvent>  _events  = new List<MatchEvent>();
        readonly List<MatchAction> _actions = new List<MatchAction>();

        int         _lastEventStart;
        int         _lastActionStart;
        /// <summary> How far into the model's own history this driver has read. Not reset by <see cref="Clear"/>. </summary>
        int         _historyCursor;
        readonly bool[] _handChangedInLastCall = new bool[MatchSeats.Count];

        /// <summary> What the last call queued for a seat's own eyes, taken from the model's outbox. </summary>
        readonly List<(int Seat, MatchAddressedAction Action)> _undelivered = new List<(int, MatchAddressedAction)>();

        /// <summary> Per seat, whether a held peek has already been addressed to it. The host's own bookkeeping. </summary>
        readonly bool[] _mirroredPendingChoice = new bool[MatchSeats.Count];

        MatchEngine(MatchModel model)
        {
            _model = model;
        }

        /// <summary>
        /// Deal a new game. The bot seed defaults to zero because most suites do not care; the ones that do
        /// pass one, and the deal seed and the bot seed are independent exactly as they are on the server.
        /// </summary>
        public static MatchEngine Create(MatchSetup setup, ulong botSeed = 0)
        {
            MatchModel model = new MatchModel
            {
                GameConfig       = setup.Config,
                Timings          = setup.Timings,
                Phase            = MatchTablePhase.Playing,
                ResultAcked      = new List<bool> { false, false },
                HeistEligibility = new List<List<HeistEligibleCard>> { new List<HeistEligibleCard>(), new List<HeistEligibleCard>() },
                History          = new List<MatchEvent>(),
                Seats            = new List<MatchSeat>
                {
                    new MatchSeat(EntityId.None, "Seat0", SeatOccupancy.Bot, BotProfileId.Strongest),
                    new MatchSeat(EntityId.None, "Seat1", SeatOccupancy.Bot, BotProfileId.Strongest),
                },
            };

            Deal.Create(model, setup, botSeed);

            MatchEngine engine = new MatchEngine(model);
            engine.Record();
            engine._undelivered.Clear();   // the dealt hands are the baseline a subscriber is given, not changes
            return engine;
        }

        /// <summary>
        /// Resolve this match's effect steps with a different interpreter — a stub, which is the only way to
        /// reach the failure modes real content cannot produce. The hook is on the model, so this is a
        /// one-line setter rather than a constructor argument.
        /// </summary>
        public MatchEngine WithInterpreter(IEffectInterpreter interpreter)
        {
            _model.Interpreter = interpreter;
            return this;
        }

        /// <summary> Drive a game that already exists. What <c>Restore</c> was, minus the restoring. </summary>
        public static MatchEngine Wrap(MatchModel model)
        {
            MatchEngine engine = new MatchEngine(model);
            engine.Record();
            engine._undelivered.Clear();   // as above: what is already in hand is the baseline
            return engine;
        }

        // ---------------------------------------------------------------- reading

        public MatchModel       Model  => _model;
        public MatchRulesState  Rules  => _model.Rules;
        public MatchPacing      Pacing => _model.Pacing;
        /// <summary> The whole model. What <c>engine.State</c> named when the state was a separate object. </summary>
        public MatchModel       State  => _model;

        public SharedGameConfig Config      => _model.Content;
        public MatchTimings     Timings     => _model.Timings;
        public MatchPhase       Phase       => _model.Rules.Phase;
        public int              Turn        => _model.Rules.Turn;
        public int              ActionCount => _model.Rules.ActionCount;
        /// <summary> The held peek choice's id, or zero when none is held — an answer that names no choice. </summary>
        public int              ChoiceId    => _model.Rules.PendingChoice?.Id ?? 0;
        /// <summary> Null until <see cref="MatchPhase.Complete"/>. </summary>
        public MatchResult      Result      => _model.Rules.Result;
        public WeatherInfo      Weather     => _model.Rules.Weather?.Ref;

        /// <summary> The model's clock. </summary>
        public MetaTime         Now      => _model.CurrentTime;

        /// <summary> What the host must schedule next. </summary>
        public MatchPendingWork Pending => MatchPending.Of(_model);

        /// <summary>
        /// Every event recorded since the last <see cref="Clear"/>. It is the model's own history, sliced —
        /// the recorded stream and the event feed are one list, so there is nothing here that a client would
        /// not also have.
        /// </summary>
        public List<MatchEvent> Events => _events;

        /// <summary> The events the most recent call recorded, in order. </summary>
        public List<MatchEvent> LastEvents => _events.GetRange(_lastEventStart, _events.Count - _lastEventStart);

        /// <summary> Every action committed since the last <see cref="Clear"/>, in order. </summary>
        public IReadOnlyList<MatchAction> Actions => _actions;

        /// <summary> The actions the most recent call committed. What the follower mirror re-runs. </summary>
        public IReadOnlyList<MatchAction> LastActions => _actions.GetRange(_lastActionStart, _actions.Count - _lastActionStart);

        /// <summary>
        /// Whether the most recent call changed this seat's hand — the question the publisher's hand-changed
        /// flags answered, asked of the public revision counter the actor diffs.
        /// </summary>
        public bool HandChangedInLastCall(int seat) => _handChangedInLastCall[seat];

        public void Clear()
        {
            _events.Clear();
            _actions.Clear();
            _lastEventStart  = 0;
            _lastActionStart = 0;
        }

        public List<T> EventsOf<T>() where T : MatchEvent
        {
            List<T> found = new List<T>();
            foreach (MatchEvent ev in _events)
            {
                if (ev is T typed)
                    found.Add(typed);
            }
            return found;
        }

        public T LastEventOf<T>() where T : MatchEvent
        {
            for (int ndx = _events.Count - 1; ndx >= 0; ndx--)
            {
                if (_events[ndx] is T typed)
                    return typed;
            }
            return null;
        }

        public bool HasEventOf<T>() where T : MatchEvent => LastEventOf<T>() != null;

        /// <summary>
        /// A hash of the rules half of the state. <see cref="MatchPacing"/> is a separate member and is simply
        /// not passed in, so "timing stamps are outside the hash" is structural rather than a list somebody
        /// has to maintain.
        /// </summary>
        public uint ComputeRulesHash()
        {
            byte[] bytes = MetaSerialization.SerializeTagged(Rules, MetaSerializationFlags.IncludeAll, logicVersion: null);
            return MurmurHash.MurmurHash2(bytes);
        }

        public List<HandCard>    BuildHandView(int seat)  => SecretOps.HandOf(_model, seat);
        public PendingChoiceView BuildPeekView(int seat)  => HandViews.BuildPendingChoice(_model, seat);
        public SeatView          BuildSeatView(int seat)  => SeatView.Build(_model, seat);
        public List<MatchIntent> EnumerateLegalIntents(int seat) => Legality.EnumerateForSeat(_model, seat);
        public List<CardId>      DeriveUnseenPool(int seat) => SecretDerivations.DeriveUnseenPool(_model, seat);
        public MetaDuration      ReserveRemaining(int seat) => TurnRules.ReserveRemaining(_model, seat);
        public BoardCritter      FindCritterAnywhere(CardInstanceId id) => ResolutionRules.FindCritterAnywhere(Rules, id);

        // ---------------------------------------------------------------- the mirror

        /// <summary>
        /// When set, every action this driver commits is also run against a follower and the two are
        /// compared. The harness wires it up after the deal; nothing else knows it is there.
        /// </summary>
        public FollowerMirror Mirror { get; set; }

        // ---------------------------------------------------------------- driving

        /// <summary>
        /// Offer one seat's intent, as the actor does: prepare it, then refuse or commit the action it produced.
        /// </summary>
        public MatchIntentResult Submit(int seat, MatchIntent intent)
        {
            BeginCall();

            if (intent == null)
                return MatchIntentResults.IllegalTarget;

            MatchIntentResult gate = intent.Prepare(_model, seat, out MatchAction action);
            if (!gate.IsSuccess)
                return gate;

            return Stage(action);
        }

        /// <summary>
        /// Tick the model's clock forward by at least <paramref name="by"/>, as the server's timeline does
        /// while nothing else happens. The follower ticks with it.
        /// </summary>
        public void AdvanceTime(MetaDuration by) => AdvanceTo(Now + by);

        /// <summary> Tick until the model's clock has reached <paramref name="target"/>. </summary>
        void AdvanceTo(MetaTime target)
        {
            while (_model.CurrentTime < target)
            {
                _model.Tick(checksumCtx: null);
                Mirror?.Tick();
            }
        }

        /// <summary>
        /// Whether a deadline of this kind is the one actually in force. This driver needs it and the match
        /// actor does not: the actor reads the kind off the model at the moment the stamp matches, so it
        /// dispatches whatever the deadline has become, which makes the same guard there a comparison of a
        /// field against itself. This driver is handed a kind by its caller, so it has to ask.
        /// <para>
        /// A model with no deadline armed answers <b>true</b>: there is nothing in force to have replaced the
        /// caller's kind, and a fixture that walks into a state directly must not be refused for it.
        /// </para>
        /// </summary>
        bool DeadlineIsInForce(MatchDeadlineKind kind)
            => !_model.Pacing.DeadlineAt.HasValue || _model.Pacing.DeadlineKind == kind;

        /// <summary>
        /// One of the host's timers ran out. The routing table the actor has, with the same division of
        /// labour: the reserve rule is a shared query, so this routes rather than deciding. The model's clock
        /// is first ticked up to the stamp, which is where a real lapse happens.
        /// </summary>
        public MatchDeadlineOutcome ExpireDeadline(MatchDeadlineKind kind, IActionSource autoPlay)
        {
            BeginCall();

            if (Phase == MatchPhase.Complete)
                return MatchDeadlineOutcome.Ignored;

            // A timer armed for a deadline that has since been replaced is a stale callback, not a lapse.
            if (!DeadlineIsInForce(kind))
                return MatchDeadlineOutcome.Ignored;

            if (_model.Pacing.DeadlineAt.HasValue)
                AdvanceTo(_model.Pacing.DeadlineAt.Value);

            switch (kind)
            {
                case MatchDeadlineKind.Mulligan:
                    if (Phase != MatchPhase.Mulligan)
                        return MatchDeadlineOutcome.Ignored;
                    return TrySubmit(new MatchMulliganResolve()).IsSuccess ? MatchDeadlineOutcome.Applied : MatchDeadlineOutcome.Ignored;

                case MatchDeadlineKind.EffectChoice:
                    if (Rules.PendingChoice == null)
                        return MatchDeadlineOutcome.Ignored;
                    return ApplyDefaultChoice() ? MatchDeadlineOutcome.Applied : MatchDeadlineOutcome.Ignored;

                case MatchDeadlineKind.Turn:
                    if (Phase != MatchPhase.Playing)
                        return MatchDeadlineOutcome.Ignored;

                    if (TurnRules.CanExtendFromReserve(_model, Rules.SeatOnTurn))
                    {
                        TrySubmit(new MatchExtendReserve(Rules.SeatOnTurn));
                        return MatchDeadlineOutcome.ExtendedFromReserve;
                    }

                    PlayOutTurn(autoPlay ?? TestBots.Strongest);
                    return MatchDeadlineOutcome.Applied;

                default:
                    return MatchDeadlineOutcome.Ignored;
            }
        }

        /// <summary>
        /// Apply one <b>table</b> action — the actor's own state rather than the game's: the seats, the phase,
        /// the result and its ack, a seat's grace, the host's durations, the outage push.
        /// <para>
        /// The actor issues these itself and no intent produces one, so without this the test driver could
        /// never put one in front of a follower — which is exactly how seven of the fifteen actions came to
        /// have no mirrored path at all. It goes through the same dry-run-then-commit gate every other action
        /// takes, so it cannot be used to write a state the actor could not.
        /// </para>
        /// </summary>
        public MatchIntentResult SubmitTableAction(MatchHostAction action)
        {
            BeginCall();
            return TrySubmit(action);
        }


        /// <summary>
        /// The table was not there for a while, so nothing that was waiting on a clock spent it. An
        /// <em>action</em> rather than a direct write, because the pacing stamps are public and therefore
        /// checksummed — which is also what lets a follower learn the pushed stamps by running the same body.
        /// </summary>
        public void PushClocksForward(MetaDuration by)
        {
            BeginCall();

            if (by <= MetaDuration.Zero)
                return;

            TrySubmit(new MatchPushClocks(by));
        }

        /// <summary>
        /// Play the whole rest of the game with no beats and no deadlines. A deserted table and a fast-forward
        /// both use this on the server, and it is the same loop.
        /// </summary>
        public void PlayOutRemainder(IActionSource policy)
        {
            BeginCall();

            IActionSource source = policy ?? TestBots.Strongest;

            const int MaxIterations = 100000;
            int       iterations    = 0;

            while (Phase != MatchPhase.Complete)
            {
                if (++iterations > MaxIterations)
                    throw new MatchEngineException($"PlayOutRemainder did not terminate within {MaxIterations} steps");

                if (Rules.PendingChoice != null)
                {
                    if (!ApplyDefaultChoice())
                        throw new MatchEngineException("PlayOutRemainder cannot finish the game: the held choice would not resolve");
                    continue;
                }

                if (Phase == MatchPhase.Mulligan)
                {
                    if (!TrySubmit(new MatchMulliganResolve()).IsSuccess)
                        throw new MatchEngineException("PlayOutRemainder cannot finish the game: the mulligan would not resolve");
                    continue;
                }

                if (!TakeOneMove(source))
                    throw new MatchEngineException("PlayOutRemainder is stuck: neither the policy's move nor ending the turn applied");
            }
        }

        /// <summary> The rest of one turn, then its end. What a lapsed turn deadline does. </summary>
        void PlayOutTurn(IActionSource source)
        {
            int turn = Turn;

            const int MaxActionsPerTurn = 1000;
            for (int guard = 0; guard < MaxActionsPerTurn; guard++)
            {
                if (Phase != MatchPhase.Playing || Turn != turn)
                    return;

                if (Rules.PendingChoice != null)
                {
                    ApplyDefaultChoice();
                    continue;
                }

                if (!TakeOneMove(source))
                    throw new MatchEngineException("A lapsed turn deadline is stuck: neither the policy's move nor ending the turn applied");
            }

            throw new MatchEngineException($"A lapsed turn deadline played {MaxActionsPerTurn} actions without ending turn {turn}");
        }

        /// <summary>
        /// Ask the policy for one move and apply it, falling back to ending the turn — which is always legal
        /// for the seat on turn, so the game still moves when a policy offers something illegal.
        /// </summary>
        bool TakeOneMove(IActionSource source)
        {
            int         seat   = Rules.SeatOnTurn;
            MatchIntent intent = source.ChooseAction(BuildSeatView(seat), seat) ?? new EndTurnIntent();

            if (SubmitInner(seat, intent).IsSuccess)
                return true;

            return SubmitInner(seat, new EndTurnIntent()).IsSuccess;
        }

        /// <summary> The default peek answer, as the intent that seat would have sent. </summary>
        bool ApplyDefaultChoice()
            => SubmitInner(Rules.PendingChoice.Seat, EffectChoiceIntent.Default(_model, Rules.PendingChoice.Seat)).IsSuccess;

        /// <summary> Submit without resetting the per-call markers, for the loops that submit several. </summary>
        MatchIntentResult SubmitInner(int seat, MatchIntent intent)
        {
            MatchIntentResult gate = intent.Prepare(_model, seat, out MatchAction action);
            if (!gate.IsSuccess)
                return gate;

            return Stage(action);
        }

        /// <summary> Offer one host action to the model, checked first the way the actor checks it. </summary>
        MatchIntentResult TrySubmit(MatchHostAction action)
        {
            if (action == null)
                return MatchIntentResults.IllegalTarget;

            MatchIntentResult gate = action.ServerPrepare(_model);
            if (!gate.IsSuccess)
                return gate;

            return Stage(action);
        }

        /// <summary> Put an already-checked action on the model, mirroring it the way a follower would run it. </summary>
        MatchIntentResult Stage(MatchAction action)
        {
            int historyBefore = _model.History.Count;
            action.InvokeExecute(_model, commit: true);
            _actions.Add(action);
            Record();

            // The follower runs the same action and the two models are compared, every action of every game.
            // The events handed over are this action's own, which is the second axis of the comparison: a
            // state that agrees while an event names a different card is a leak the checksum cannot see.
            Mirror?.Apply(action, _model, _model.History.GetRange(historyBefore, _model.History.Count - historyBefore));

            DeliverHandChanges();

            return MatchIntentResults.Success;
        }

        void BeginCall()
        {
            _lastEventStart  = _events.Count;
            _lastActionStart = _actions.Count;

            for (int seat = 0; seat < MatchSeats.Count; seat++)
                _handChangedInLastCall[seat] = false;
        }

        /// <summary>
        /// What the host does after an action: address each seat's hand changes to that seat. The mirror runs
        /// them the way that seat's client would, which is what proves an addressed action moves nothing
        /// public — and then that replaying them still reproduces the real hand.
        /// </summary>
        void DeliverHandChanges()
        {
            if (Mirror == null)
            {
                _undelivered.Clear();
                return;
            }

            // Only the seat this mirror is a client of. The SDK addresses an operation to one member and hands
            // everyone else a no-op, so delivering the other seat's operations here would model a delivery
            // that cannot happen.
            foreach ((int seat, MatchAddressedAction action) in _undelivered)
            {
                if (seat == FollowerMirror.MirroredSeat)
                    Mirror.Apply(action, _model, new List<MatchEvent>());
            }

            _undelivered.Clear();

            // A peek moves no card, so its reveal is addressed on its own — and only when the public pending
            // state actually changed, rather than once per action taken while the question stands.
            for (int seat = 0; seat < MatchSeats.Count; seat++)
            {
                PendingEffectChoice pending   = _model.Rules.PendingChoice;
                bool                hasChoice = pending != null && pending.Seat == seat;

                if (hasChoice == _mirroredPendingChoice[seat])
                    continue;

                _mirroredPendingChoice[seat] = hasChoice;

                if (seat != FollowerMirror.MirroredSeat)
                    continue;

                Mirror.Apply(
                    new MatchOwnPeekRevealed(hasChoice ? HandViews.BuildPendingChoice(_model, seat) : null),
                    _model,
                    new List<MatchEvent>());
            }

            string divergence = Mirror.OwnHandDivergence(_model);
            if (divergence != null)
                throw new FollowerDivergence($"the follower's own hand no longer matches the authority's: {divergence}");
        }

        /// <summary> Take whatever the model's history gained, and note whose hand moved. </summary>
        void Record()
        {
            for (; _historyCursor < _model.History.Count; _historyCursor++)
                _events.Add(_model.History[_historyCursor]);

            // Taken, not read — exactly as the host does before it addresses each queued operation to its
            // seat. A call that moved a hand is a call that owes that seat a delivery.
            foreach ((int seat, MatchAddressedAction action) in _model.Outbox)
            {
                _handChangedInLastCall[seat] = true;
                _undelivered.Add((seat, action));
            }

            _model.Outbox.Clear();
        }
    }

    /// <summary> What the engine did with a lapsed deadline. </summary>
    public enum MatchDeadlineOutcome
    {
        /// <summary> Nothing to do: the deadline named is not the one in force. </summary>
        Ignored             = 0,
        /// <summary> The rules consequence happened. The host re-reads <see cref="MatchPending"/>. </summary>
        Applied             = 1,
        /// <summary>
        /// The seat drew on its reserve instead. The turn is not over; the host re-arms from the new
        /// <see cref="MatchPendingWork.DeadlineAt"/> and this does not count as a lapse.
        /// </summary>
        ExtendedFromReserve = 2,
    }
}
