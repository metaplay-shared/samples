using Game.Logic;
using Metaplay.Core;
using System.Collections.Generic;

namespace Game.Client.Services;

/// <summary>
/// The board's view of the match, and the one place an intent leaves the client from. It observes the
/// replicated match through <see cref="IMatchModelClientListener"/>.
/// </summary>
public class MatchService : IMatchModelClientListener
{
    readonly MetaplayClientService _client;

    /// <summary> Raised when anything the board draws has changed. </summary>
    public event Action? OnMatchChanged;

    /// <summary> Raised when a board appears or goes away, which is when the page turns. </summary>
    public event Action? OnMatchAttachmentChanged;

    /// <summary>
    /// Counts attachments and detachments. A sub-client activation is the thing the first frame keys on
    /// (<c>Docs/client.md</c>, "the first frame"), and it is never the match's identity — the same
    /// match re-attached is a new epoch, because everything drawn from the old one has to go. The browser
    /// motion layer holds state no C# member can see, so it reads this to know when to drop it.
    /// </summary>
    public int AttachmentEpoch { get; private set; }

    public MatchService(MetaplayClientService client)
    {
        _client      = client;
        Tuning       = new BoardPresentationTuning();
        Presentation = new BoardPresentation(Tuning);
    }

    /// <summary> The beat queue and the trailing rules. </summary>
    public BoardPresentation Presentation { get; private set; }

    /// <summary> The animation knobs, forceable from the query string so a test can watch a beat. </summary>
    public BoardPresentationTuning Tuning { get; }

    MatchClientContext? _context;

    /// <summary> The replicated match, or null when this client is not at a table. </summary>
    public MatchModel? Model => _context?.CommittedModel;

    /// <summary> Whether there is a board to draw. </summary>
    public bool HasMatch => Model != null;

    /// <summary> This client's seat, or <see cref="MatchSeats.None"/>. </summary>
    public int LocalSeat { get; private set; } = MatchSeats.None;

    public int OpponentSeat => LocalSeat >= 0 ? MatchSeats.Other(LocalSeat) : MatchSeats.None;

    public SharedGameConfig? GameConfig => Model?.GameConfig as SharedGameConfig;

    /// <summary>
    /// The replicated rules state: the members the server's rules run on, checksummed, so a divergence ends
    /// the session instead of drawing two boards.
    /// </summary>
    public MatchRulesState? Rules => Model?.Rules;

    /// <summary> This seat's own hand as last applied. </summary>
    public List<HandCard>? OwnHand => Model?.OwnHand;

    /// <summary>
    /// The table's clock: the model's own, as the timeline delivers it, typed so it cannot be confused with the
    /// device's. With no table it is the device clock, which only the page's own presentation timers read.
    /// </summary>
    public AuthoritativeTime Now => new AuthoritativeTime(_context?.ModelNow ?? MetaTime.Now);

    // ---------------------------------------------------------------- attachment

    /// <summary>
    /// A whole state arrived with no prior frame: a fresh attach or a reconnect. The board is rendered
    /// directly, with no beats replayed — an animation of something the player did not miss is a flourish,
    /// and one of something they did is a lie.
    /// </summary>
    internal void OnMatchAttached(MatchClientContext context, EntityId playerId)
    {
        MatchModel model = context.CommittedModel;

        Presentation = new BoardPresentation(Tuning);
        Presentation.SnapTo(BoardVisualState.FromRules(model.Rules), model.History);

        ChangeAttachment(context, model.SeatIndexOfPlayer(playerId));
    }

    internal void OnMatchDetached() => ChangeAttachment(null, MatchSeats.None);

    void ChangeAttachment(MatchClientContext? context, int localSeat)
    {
        _context          = context;
        LocalSeat         = localSeat;
        _pendingRequestId = 0;
        _legalCache       = null;
        LastRefusal       = null;
        _sentRequestIds.Clear();

        AttachmentEpoch++;
        OnMatchAttachmentChanged?.Invoke();
        OnMatchChanged?.Invoke();
    }

    // ---------------------------------------------------------------- the model's own listener

    void IMatchModelClientListener.OnMatchEvent(MatchEvent ev)
    {
        // Queued rather than drawn: one beat per engine step, so a chain of Hello and Goodbye triggers reads
        // as a sequence of things that happened.
        _stepEvents.Add(ev);
    }

    readonly List<MatchEvent> _stepEvents = new List<MatchEvent>();

    void IMatchModelClientListener.OnBoardChanged()
    {
        // Every rules action and every addressed hand operation fires this, so it is the one place the cached
        // legal set can go stale.
        _legalCache = null;

        MatchModel? model = Model;
        if (model != null && _stepEvents.Count > 0)
            Presentation.EnqueueStep(
                model.Rules.ActionCount,
                _stepEvents,
                BoardVisualState.FromRules(model.Rules));

        _stepEvents.Clear();

        if (model != null && (IsLocalSeatOnTurn || IsMulliganOpen || model.Rules.PendingChoice?.Seat == LocalSeat))
            Presentation.PrepareForDecision(Now);

        // The input lock lasts one round trip: any board change means the server has spoken. A refusal that
        // lands after the release is still shown (OnIntentRefused).
        _pendingRequestId = 0;

        OnMatchChanged?.Invoke();
    }

    void IMatchModelClientListener.OnSeatsChanged() => OnMatchChanged?.Invoke();

    void IMatchModelClientListener.OnPacingChanged() => OnMatchChanged?.Invoke();

    void IMatchModelClientListener.OnPhaseChanged(MatchTablePhase phase)
    {
        // A refusal belongs to the move that was refused, and the game is over: the Heist screen reads the
        // same field, so the last refused card of the match would otherwise open it saying "the table refused
        // that move" beside a pick nobody has made yet.
        if (phase != MatchTablePhase.Playing)
            LastRefusal = null;

        _legalCache = null;

        OnMatchChanged?.Invoke();
    }

    void IMatchModelClientListener.OnResult(MatchOutcomeRecord result) => OnMatchChanged?.Invoke();

    void IMatchModelClientListener.OnResultAcked(int seat) => OnMatchChanged?.Invoke();

    // ---------------------------------------------------------------- directed messages

    /// <summary> The last refusal, so the board can put the card down and say what happened. </summary>
    public MatchIntentRefused? LastRefusal { get; private set; }

    internal void OnIntentRefused(MatchIntentRefused refusal)
    {
        // Shown if it answers anything this client sent, not only if it answers whatever is in flight right
        // now: a board change can release the lock before the refusal lands, and a refusal nobody renders is
        // a move the player watched fail with no explanation.
        if (!_sentRequestIds.Contains(refusal.RequestId))
            return;

        LastRefusal = refusal;

        if (refusal.RequestId == _pendingRequestId)
            _pendingRequestId = 0;

        OnMatchChanged?.Invoke();
    }

    // ---------------------------------------------------------------- sending

    int _pendingRequestId;

    /// <summary>
    /// The request ids this client has sent recently, so a refusal can be recognised as ours even when the
    /// input lock has already been released by an intervening board change. Without it a refusal that lost
    /// that race is dropped on the floor: the player is told nothing about a move the table rejected, and the
    /// board keeps whatever it was showing. Bounded, because only the recent ones can still be answered.
    /// </summary>
    readonly Queue<int> _sentRequestIds = new Queue<int>();

    void RememberSentRequest(int requestId)
    {
        _sentRequestIds.Enqueue(requestId);

        while (_sentRequestIds.Count > 8)
            _sentRequestIds.Dequeue();
    }
    int _nextRequestId = 1;

    /// <summary>
    /// Whether an intent is in flight. <b>One intent at a time</b>, and the reason is legality rather than
    /// protocol: the second action's legality depends on the first's result — mana spent, a critter dead, a
    /// board slot taken — so a queued second action would be a choice offered against a board the server has
    /// already moved past.
    /// </summary>
    public bool IsInputLocked => _pendingRequestId != 0;

    /// <summary> Offer one rules intent. Returns false when the input is locked or there is no table. </summary>
    public bool Submit(MatchIntent intent)
    {
        if (_context == null || IsInputLocked || intent == null)
            return false;

        _pendingRequestId = _nextRequestId++;
        Send(new MatchIntentMessage(intent, _pendingRequestId), _pendingRequestId);
        return true;
    }

    /// <summary>
    /// Offer one rules intent behind an End turn sent first, so it reaches the table after the turn has passed:
    /// the race a turn deadline lapsing under a press produces. The board's <c>?stale=1</c> hook, and the one
    /// route a test has to a real refusal (<c>MatchBoardPage.Submit</c> says why). The End turn goes out with
    /// its own request id, so whichever of the two is refused is shown.
    /// </summary>
    public bool SubmitAfterTurnPassed(MatchIntent intent)
    {
        if (_context == null || IsInputLocked || intent == null)
            return false;

        int endTurnRequestId = _nextRequestId++;
        Send(new MatchIntentMessage(new EndTurnIntent(), endTurnRequestId), endTurnRequestId);

        _pendingRequestId = _nextRequestId++;
        Send(new MatchIntentMessage(intent, _pendingRequestId), _pendingRequestId);
        return true;
    }

    /// <summary>
    /// Offer one seat intent: leaving, or the winner's Heist pick. Remembered so a refusal can be shown; does
    /// not take the input lock, which is for rules actions only.
    /// </summary>
    public bool SubmitSeatIntent(MatchSeatIntent intent)
    {
        if (_context == null || intent == null)
            return false;

        int requestId = _nextRequestId++;
        Send(new MatchSeatIntentMessage(intent, requestId), requestId);
        return true;
    }

    void Send(MetaMessage message, int requestId)
    {
        LastRefusal = null;
        RememberSentRequest(requestId);
        _context!.Send(message);
        OnMatchChanged?.Invoke();
    }

    // ---------------------------------------------------------------- the frame

    /// <summary>
    /// Advance the presentation. Driven by the board page while it is on screen: the
    /// shell's own frame pump deliberately raises only what nothing else can see, and a board with a
    /// countdown on it is the one screen that genuinely needs a repaint loop.
    /// </summary>
    public void Tick()
    {
        if (_context == null)
            return;

        Presentation.Update(Now);

        // The clock is sampled every frame, but the board renders only when its beat identity, queue,
        // input state or countdown second changes. CSS and JavaScript animate between those renders.
        string signature = FrameSignature();
        if (signature == _lastFrameSignature)
            return;

        _lastFrameSignature = signature;
        OnMatchChanged?.Invoke();
    }

    string? _lastFrameSignature;

    /// <summary> Everything a frame can change on its own, in one comparable value. </summary>
    string FrameSignature()
    {
        MetaTime? deadline = Model?.Pacing?.DeadlineAt;
        long remaining = deadline.HasValue ? (deadline.Value - Now.Value).Milliseconds / 1000 : -1;

        return $"{Presentation.IsTrailing}|{Presentation.QueueLength}|{Presentation.CurrentBeat?.Sequence}|{Presentation.PresentedActionCount}|{remaining}|{IsInputLocked}";
    }

    // ---------------------------------------------------------------- legality, for the highlights

    /// <summary>
    /// Every action this seat may take right now: the same legality walk the server's own gate runs, over the
    /// replicated model and this seat's hand, which addressed timeline operations keep current. Cached until
    /// the board next changes, because the board reads it several times a render.
    /// </summary>
    public List<MatchIntent> LegalIntents()
    {
        MatchRulesState?  rules  = Rules;
        SharedGameConfig? config = GameConfig;

        if (rules == null || config == null || LocalSeat < 0)
            return EmptyIntents;

        return _legalCache ??= Legality.EnumerateLegalIntents(Model!, LocalSeat, OwnHand);
    }

    static readonly List<MatchIntent> EmptyIntents = new List<MatchIntent>();

    List<MatchIntent>? _legalCache;

    /// <summary> Every target this card may be played at, or an empty list when it asks for none. </summary>
    public List<EffectTargetRef> TargetsFor(CardInstanceId card)
    {
        List<EffectTargetRef> targets = new List<EffectTargetRef>();
        foreach (MatchIntent intent in LegalIntents())
        {
            if (intent is PlayCardIntent play && play.Card == card && !play.Target.IsNone)
                targets.Add(play.Target);
        }

        return targets;
    }

    /// <summary> Whether the local seat is the one the table is waiting on. </summary>
    public bool IsLocalSeatOnTurn
    {
        get
        {
            MatchRulesState? rules = Rules;
            return rules != null && LocalSeat >= 0 && rules.Phase == MatchPhase.Playing && rules.SeatOnTurn == LocalSeat;
        }
    }

    /// <summary> Whether this seat still owes a mulligan. </summary>
    public bool IsMulliganOpen
    {
        get
        {
            MatchRulesState? rules = Rules;
            return rules != null && LocalSeat >= 0 && rules.Phase == MatchPhase.Mulligan && !rules.Seat(LocalSeat).HasMulliganed;
        }
    }

    /// <summary> What a held peek is showing this seat, or null. </summary>
    public PendingChoiceView? PendingChoice => Model?.OwnPeek;

    // ---------------------------------------------------------------- entry

    /// <summary>
    /// Enter a practice match. Practice does not touch the queue — it mints a bot table on the spot — so this
    /// is the mode rather than a test hook. Home raises no screen change: the page turns exactly once, at the
    /// moment there is a board to draw.
    /// </summary>
    public void StartPracticeMatch(DeckChoice deck, BotProfileId profile)
        => _client.ExecuteAction(new PlayerStartPracticeMatch(deck, profile));

    // ---------------------------------------------------------------- the table that went away

    /// <summary>
    /// Whether a match this account was in stopped existing and the player has not been told yet.
    /// <para>
    /// It reads through the player model because the pointer that named the table is <c>ServerOnly</c>; the
    /// root observer is what repaints on it.
    /// </para>
    /// </summary>
    public bool MatchGoneUnseen => _client.PlayerModel?.MatchGoneUnseen ?? false;

    /// <summary> The player has read the notice. </summary>
    public void DismissMatchGone() => _client.ExecuteAction(new PlayerDismissMatchGone());
}
