using Game.Logic;
using Metaplay.Core;
using System.Collections.Generic;

namespace Game.Client.Services;

/// <summary>
/// The table's clock: the model's own, advanced by the server's ticks. It is what the server's deadlines are
/// stamped on, so it is what decides what the server will accept.
/// <para>
/// It is a type of its own, and <see cref="PresentedTime"/> is another, because the two must never be mixed in
/// one answer and a judgement call about that is a judgement call somebody eventually gets wrong
/// (<c>Docs/client.md</c>, "Two clocks, and one rule").
/// </para>
/// </summary>
public readonly struct AuthoritativeTime
{
    public readonly MetaTime Value;

    public AuthoritativeTime(MetaTime value)
    {
        Value = value;
    }

    public override string ToString() => Value.ToString();
}

/// <summary> Where the board has been animated to: what a player is actually looking at. </summary>
public readonly struct PresentedTime
{
    public readonly MetaTime Value;

    public PresentedTime(MetaTime value)
    {
        Value = value;
    }

    public override string ToString() => Value.ToString();
}

/// <summary> How long beats run and how hard the queue compresses. Forceable from the query string. </summary>
public sealed class BoardPresentationTuning
{
    /// <summary> One beat's length at an empty queue. </summary>
    public MetaDuration BeatLength { get; set; } = MetaDuration.FromMilliseconds(560);

    /// <summary>
    /// How many queued beats halve a beat's length. The queue compresses as it grows: an opponent playing
    /// quickly can queue beats faster than they play them.
    /// </summary>
    public int CompressAfter { get; set; } = 8;

    /// <summary>
    /// Collapse every beat to its end state. What the player asked for when they asked for reduced motion —
    /// and what a test forces when it wants the board's end state rather than its animation. It changes the
    /// presentation and never the doctrine: state still only moves on a server update, and the terminal panel
    /// is still withheld until the finish has been shown, even when showing it takes one frame.
    /// </summary>
    public bool ReducedMotion { get; set; }
}

/// <summary>
/// The beat queue and the trailing rule. The board animates <em>towards</em> each update rather than jumping
/// to it, so that a number which changes visibly is caused by something a player saw
/// (<c>Docs/client.md</c>, "Pacing the board").
/// <para>
/// Everything here is pure: it is handed an authoritative clock reading and answers what should be on screen.
/// That is what lets the trailing rules — the ones a live board could only be observed to get wrong — be
/// unit-tested away from the browser.
/// </para>
/// </summary>
public sealed class BoardPresentation
{
    /// <summary> One beat: the event it presents and the action count the board reaches by finishing its step. </summary>
    readonly struct QueuedBeat
    {
        public readonly MatchEvent    Event;
        public readonly IReadOnlyList<MatchEvent> Events;
        public readonly int           ActionCount;
        public readonly BoardVisualState? StateAfterStep;
        public readonly bool EndsStep;

        public QueuedBeat(MatchEvent primaryEvent, IReadOnlyList<MatchEvent> events, int actionCount,
            BoardVisualState? stateAfterStep, bool endsStep)
        {
            Event          = primaryEvent;
            Events         = events;
            ActionCount    = actionCount;
            StateAfterStep = stateAfterStep;
            EndsStep       = endsStep;
        }
    }

    readonly List<QueuedBeat> _queue = new List<QueuedBeat>();
    readonly List<MatchEvent> _history = new List<MatchEvent>();
    readonly List<MatchEvent> _receivedHistory = new List<MatchEvent>();
    readonly BoardPresentationTuning _tuning;

    MatchEvent?   _running;
    IReadOnlyList<MatchEvent>? _runningEvents;
    BoardVisualCritter? _enteringCritter;
    int           _runningActionCount;
    MetaTime      _runningStartedAt;
    MetaTime      _runningEndsAt;
    MetaDuration  _runningDuration;
    int           _runningSequence;
    int           _nextSequence;
    BoardVisualState? _runningStateAfterStep;
    bool          _runningEndsStep;
    bool          _runningSuppressImpact;
    CardInstanceId _activeAttackSource = CardInstanceId.None;
    AuthoritativeTime _lastNow;
    MetaTime? _catchUpBy;
    bool          _isRunning;

    public BoardPresentation(BoardPresentationTuning tuning)
    {
        _tuning = tuning;
    }

    /// <summary> The rules' action count the board has been animated to: the last step it finished showing. </summary>
    public int PresentedActionCount { get; private set; }

    /// <summary> The beat on screen right now, or null. </summary>
    public MatchEvent? RunningBeat => _isRunning ? _running : null;

    /// <summary> The visual state after every beat which has finished, never the authoritative future state. </summary>
    public BoardVisualState? VisualState { get; private set; }

    /// <summary> History revealed with the action on screen, excluding the queued future. </summary>
    public IReadOnlyList<MatchEvent> PresentedHistory => _history;

    /// <summary> Animation-ready metadata for the beat on screen right now, or null. </summary>
    public BoardBeat? CurrentBeat
        => _isRunning && _running != null
            ? BoardBeat.FromEvent(
                _running,
                _runningActionCount,
                _runningDuration,
                RunningProgress(),
                _runningSequence,
                _runningSuppressImpact,
                _runningEvents,
                _enteringCritter)
            : null;

    public int QueueLength => _queue.Count;

    /// <summary>
    /// Whether the board is behind the server. While it trails, the ring and the turn indicator are withheld:
    /// a ring draining for a turn the player has not been shown yet is worse than no ring, and a trailing
    /// board must never invite input it cannot honour.
    /// </summary>
    public bool IsTrailing => _isRunning || _queue.Count > 0;

    /// <summary> Accelerate queued effects before a decision, without interrupting the active animation. </summary>
    public void PrepareForDecision(AuthoritativeTime now)
    {
        // A deliberately lengthened preview/test beat remains observable.
        if (_tuning.BeatLength > MetaDuration.FromMilliseconds(560))
            return;
        MetaTime by = now.Value + MetaDuration.FromSeconds(2);
        if (!_catchUpBy.HasValue || by < _catchUpBy.Value)
            _catchUpBy = by;
    }

    /// <summary>
    /// A whole state arrived with no prior frame — a fresh subscribe or a reconnect. It is rendered directly,
    /// with no beats replayed: an animation of something the player did not miss is a flourish, and one of
    /// something they did is a lie.
    /// </summary>
    public void SnapTo(int actionCount)
    {
        _queue.Clear();
        _catchUpBy = null;
        _isRunning        = false;
        _running          = null;
        _runningEvents = null;
        _enteringCritter = null;
        _history.Clear();
        _history.AddRange(_receivedHistory);
        _runningStateAfterStep = null;
        _runningEndsStep = false;
        _runningSuppressImpact = false;
        _activeAttackSource = CardInstanceId.None;
        _runningSequence = 0;
        _nextSequence = 0;
        PresentedActionCount = actionCount;
        VisualState          = null;
    }

    /// <summary> Snap a fresh subscribe or reconnect to its complete public visual state. </summary>
    public void SnapTo(BoardVisualState state)
    {
        SnapTo(state.ActionCount);
        VisualState = state.Clone();
    }

    public void SnapTo(BoardVisualState state, IReadOnlyList<MatchEvent> history)
    {
        _receivedHistory.Clear();
        _receivedHistory.AddRange(history);
        SnapTo(state);
    }

    /// <summary>
    /// One published step's events. A play's resource and entry bookkeeping share its flight; Hello and
    /// Goodbye triggers retain their own beats so the chain reads as a sequence of things that happened.
    /// </summary>
    public void EnqueueStep(int actionCount, IReadOnlyList<MatchEvent> events)
        => EnqueueStep(actionCount, events, null);

    /// <summary>
    /// Queue a published step together with its authoritative public end state. Events advance a detached
    /// visual projection one at a time; the final event reconciles it exactly to this snapshot.
    /// </summary>
    public void EnqueueStep(int actionCount, IReadOnlyList<MatchEvent> events, BoardVisualState? stateAfterStep)
    {
        if (events == null || events.Count == 0)
        {
            if (stateAfterStep != null)
                SnapTo(stateAfterStep);
            return;
        }

        _receivedHistory.AddRange(events);
        for (int ndx = 0; ndx < events.Count; ndx++)
        {
            MatchEvent primaryEvent = events[ndx];
            List<MatchEvent> group = new List<MatchEvent> { events[ndx] };
            if (events[ndx] is CardPlayedEvent played)
            {
                bool includedSpend = false;
                while (ndx + 1 < events.Count && IsPlayBookkeeping(events[ndx + 1], played))
                {
                    // Only the initial cost belongs to the flight; later mana changes are card effects.
                    if (events[ndx + 1] is ManaChangedEvent)
                    {
                        if (includedSpend)
                            break;
                        includedSpend = true;
                    }
                    group.Add(events[++ndx]);
                }
            }
            else if (primaryEvent is DamageDealtEvent damage && damage.Target.IsDen
                && !damage.AbsorbedByBubble && ndx + 1 < events.Count
                && events[ndx + 1] is DenDamagedEvent den
                && den.Seat == damage.Target.DenSeat && den.Amount == damage.Amount)
            {
                // The damage report and resulting Den health describe one impact, not two hits.
                group.Add(events[++ndx]);
                primaryEvent = den;
            }
            else if (primaryEvent is DamageDealtEvent blocked && blocked.Target.IsCritter
                && blocked.AbsorbedByBubble && ndx + 1 < events.Count
                && events[ndx + 1] is BubblePoppedEvent bubble && bubble.Instance == blocked.Target.Critter)
            {
                // The shield absorbs the hit during this beat and disappears when that same beat completes.
                group.Add(events[++ndx]);
            }
            // Completion markers have no independent visual change; keep their history and snapshot with
            // the preceding effect instead of charging an extra animation beat for bookkeeping.
            while (ndx + 1 < events.Count && events[ndx + 1] is EffectResolvedEvent)
                group.Add(events[++ndx]);
            BoardVisualState? finalState = ndx == events.Count - 1 ? stateAfterStep?.Clone() : null;
            bool endsStep = ndx == events.Count - 1;
            _queue.Add(new QueuedBeat(primaryEvent, group, actionCount, finalState, endsStep));
        }
    }

    static bool IsPlayBookkeeping(MatchEvent ev, CardPlayedEvent played)
        => ev switch
        {
            ManaChangedEvent mana => mana.Seat == played.Seat,
            CritterEnteredPlayEvent entered => entered.Instance == played.Instance,
            StatsChangedEvent stats => stats.Instance == played.Instance,
            KeywordsChangedEvent keywords => keywords.Instance == played.Instance,
            UnseenPoolChangedEvent => true,
            _ => false,
        };

    /// <summary>
    /// Advance the presentation to <paramref name="now"/>. A beat is bounded by its own <em>span</em> and not
    /// only by its end: a reading from before the window began can only mean the clock moved after the window
    /// was stamped, and the beat is then over rather than waiting to be caught up with — ending an animation
    /// early is a dropped frame, failing to end it is a stuck board.
    /// </summary>
    public void Update(AuthoritativeTime now)
    {
        _lastNow = now;
        if (_tuning.ReducedMotion)
        {
            // Apply every event and reveal the complete history in one frame, including steps that carry
            // no reconciliation snapshot. Nothing is meant to be watched happening with reduced motion.
            while (_queue.Count > 0)
            {
                if (_isRunning)
                    Finish();
                StartNext(now);
            }

            if (_isRunning)
                Finish();

            return;
        }

        if (_isRunning && !IsWithinWindow(now.Value))
            Finish();

        while (!_isRunning && _queue.Count > 0)
        {
            StartNext(now);
            // A marker can arrive alone or before a visible event. Commit it without yielding a timed
            // beat, then start the next real animation at this same clock reading.
            if (_running is EffectResolvedEvent)
                Finish();
        }
        if (!IsTrailing)
            _catchUpBy = null;
    }

    bool IsWithinWindow(MetaTime now) => now >= _runningStartedAt && now < _runningEndsAt;

    void StartNext(AuthoritativeTime now)
    {
        QueuedBeat next = _queue[0];
        _queue.RemoveAt(0);

        _running          = next.Event;
        _runningEvents = next.Events;
        _history.AddRange(next.Events);
        _enteringCritter = null;
        if (VisualState != null && next.Event is CardPlayedEvent played)
        {
            BoardVisualState arrival = VisualState;
            foreach (MatchEvent ev in next.Events)
                arrival = BoardVisualReducer.Apply(arrival, ev);
            _enteringCritter = arrival.FindCritter(played.Instance);
            // Spending accompanies the lift; the hand and board reconcile when the flight completes.
            foreach (MatchEvent ev in next.Events)
            {
                if (ev is ManaChangedEvent)
                    VisualState = BoardVisualReducer.Apply(VisualState, ev);
            }
        }
        _runningActionCount = next.ActionCount;
        _runningStartedAt = now.Value;
        _runningDuration  = EffectiveBeatLength();
        // A play with no board body needs time to reveal its face. Queue compression may shorten effects,
        // but must not turn a trick into an unreadable flash. Explicit zero/reduced-motion tuning stays instant.
        if (!_tuning.ReducedMotion && next.Event is CardPlayedEvent && _enteringCritter == null)
            _runningDuration = MetaDuration.FromMilliseconds(Math.Max(_runningDuration.Milliseconds,
                Math.Min(400, _tuning.BeatLength.Milliseconds)));
        _runningEndsAt    = now.Value + _runningDuration;
        _runningStateAfterStep = next.StateAfterStep;
        _runningEndsStep = next.EndsStep;
        _runningSuppressImpact = false;
        if (next.Event is AttackDeclaredEvent attack)
        {
            _activeAttackSource = attack.Attacker;
        }
        else if (next.Event is DamageDealtEvent damage
                 && damage.Target.IsCritter
                 && damage.Target.Critter == _activeAttackSource)
        {
            _runningSuppressImpact = true;
            _activeAttackSource = CardInstanceId.None;
        }
        else if (next.Event is DenDamagedEvent)
        {
            _activeAttackSource = CardInstanceId.None;
        }
        else if (next.Event is TurnStartedEvent or TurnEndedEvent or CardPlayedEvent or EffectChoiceRequestedEvent
                 or MatchEndedEvent)
        {
            _activeAttackSource = CardInstanceId.None;
        }
        _runningSequence = ++_nextSequence;
        _isRunning        = true;
    }

    void Finish()
    {
        if (VisualState != null && _runningEvents != null)
        {
            foreach (MatchEvent ev in _runningEvents)
                VisualState = BoardVisualReducer.Apply(VisualState, ev);
        }

        if (_runningStateAfterStep != null)
            VisualState       = _runningStateAfterStep;

        if (_runningEndsStep)
            PresentedActionCount = _runningActionCount;

        _isRunning        = false;
        _running          = null;
        _runningEvents = null;
        _enteringCritter = null;
        _runningStateAfterStep = null;
        _runningEndsStep = false;
        _runningSuppressImpact = false;
        _runningSequence = 0;
    }

    float RunningProgress()
    {
        if (!_isRunning || _runningDuration.Milliseconds <= 0)
            return 1f;

        double elapsed = (_lastNow.Value - _runningStartedAt).Milliseconds;
        double duration = _runningDuration.Milliseconds;
        return (float)System.Math.Clamp(elapsed / duration, 0d, 1d);
    }

    /// <summary> How long the next beat runs: shorter the further behind the board is. </summary>
    public MetaDuration EffectiveBeatLength()
    {
        if (_tuning.ReducedMotion)
            return MetaDuration.Zero;

        long baseMs  = _tuning.BeatLength.Milliseconds;
        int  compress = _tuning.CompressAfter > 0 ? _tuning.CompressAfter : 1;

        // Halved once the queue reaches CompressAfter, a third at twice that, and so on.
        long duration = baseMs * compress / (compress + _queue.Count);
        if (_catchUpBy.HasValue)
        {
            long remaining = (_catchUpBy.Value - _lastNow.Value).Milliseconds;
            duration = Math.Min(duration, Math.Max(16, remaining / (_queue.Count + 1)));
        }
        return MetaDuration.FromMilliseconds(duration);
    }

    /// <summary>
    /// Whether to draw the deadline ring. A ring is only ever drawn for a deadline someone enforces: "no
    /// deadline in force" is a state the board carries rather than a deadline of length zero, and the ring is
    /// withheld entirely while the board trails.
    /// </summary>
    public bool ShowsDeadlineRing(AuthoritativeTime now, MetaTime? deadlineAt)
    {
        if (IsTrailing || !deadlineAt.HasValue)
            return false;

        return deadlineAt.Value > now.Value;
    }

    /// <summary> Whether to say whose turn it is. Withheld while trailing, for the ring's reason. </summary>
    public bool ShowsTurnIndicator => !IsTrailing;

    /// <summary>
    /// Whether the result may be shown. The terminal phase is withheld until the finish has played out: the
    /// lethal blow lands, the Den's hearts run out, the board settles — <em>then</em> the panel comes up.
    /// </summary>
    public bool ShowsTerminalPanel(bool hasResult) => hasResult && !IsTrailing;

}
