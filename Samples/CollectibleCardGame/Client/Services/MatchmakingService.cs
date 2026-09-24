using Game.Logic;
using Metaplay.Core;
using Metaplay.Unity;

namespace Game.Client.Services;

/// <summary>
/// The client's half of the ranked queue: the search state, the one stamp the server sent, and the two
/// actions. It owns no model — there is no entity a client subscribes to for "my queue status" — so its input
/// is two messages on the SDK's top-level dispatcher, bound when the client is created.
/// </summary>
public class MatchmakingService
{
    readonly MetaplayClientService _client;
    readonly MatchService _match;

    /// <summary> Raised when the dialog would draw something different. </summary>
    public event Action? OnSearchChanged;

    public MatchmakingService(MetaplayClientService client, MatchService match)
    {
        _client = client;
        _match  = match;

        // A board appearing is what dismisses the dialog. There is deliberately no "you have been matched"
        // message: a formed match reaches its client the same way a practice one does, so the dialog goes
        // away because there is something to play rather than because it was told.
        _match.OnMatchAttachmentChanged += OnMatchAttachmentChanged;
    }

    /// <summary> Where the dialog is. </summary>
    public MatchmakingDialogState State { get; private set; } = MatchmakingDialogState.Hidden;

    /// <summary>
    /// The server's own bound on this wait on the device clock — the push's remaining time added to the moment
    /// it landed — or null until it lands.
    /// </summary>
    public MetaTime? ByInstant { get; private set; }

    /// <summary> Why the last search ended with no board, or null. Cleared by the next entry. </summary>
    public MatchmakingEndReason? LastEndReason { get; private set; }

    /// <summary> Whether the dialog is up at all. </summary>
    public bool IsSearching => State != MatchmakingDialogState.Hidden;

    /// <summary> The headline, the countdown and whether Cancel is offered are all <c>MatchmakingCopy</c>'s. </summary>
    public string Headline => MatchmakingCopy.Headline(State);

    public string Countdown => MatchmakingCopy.Countdown(ByInstant, MetaTime.Now);

    public bool ShowsCancel => MatchmakingCopy.ShowsCancel(State);

    /// <summary> The sentence the last ended search left behind, or empty. </summary>
    public string EndedNotice => LastEndReason.HasValue ? MatchmakingCopy.EndedNotice(LastEndReason.Value) : "";

    // ---------------------------------------------------------------- the session's own wiring

    /// <summary> Bind the two directed messages this account can receive about its own queue entry. </summary>
    public void BindListeners()
    {
        MetaplaySDK.MessageDispatcher.AddListener<MatchmakingStatusUpdate>(OnStatusUpdate);
        MetaplaySDK.MessageDispatcher.AddListener<MatchmakingEnded>(OnEnded);
    }

    void OnStatusUpdate(MatchmakingStatusUpdate message)
    {
        // The one push, and it is what turns the optimistic dialog into a real one: until it lands, the
        // client does not know the entry committed at all.
        _searchEpoch++;
        State     = MatchmakingDialogState.Searching;
        ByInstant = MetaTime.Now + message.Remaining;

        Changed();
    }

    void OnEnded(MatchmakingEnded message)
    {
        _searchEpoch++;
        State         = MatchmakingDialogState.Hidden;
        ByInstant     = null;
        LastEndReason = message.Reason;

        Changed();
    }

    void OnMatchAttachmentChanged()
    {
        if (!_match.HasMatch || State == MatchmakingDialogState.Hidden)
            return;

        _searchEpoch++;
        State         = MatchmakingDialogState.Hidden;
        ByInstant     = null;
        LastEndReason = null;

        Changed();
    }

    // ---------------------------------------------------------------- the two taps

    /// <summary>
    /// How long the optimistic dialog waits for the server to say anything at all. It is the client's own
    /// bound, the one the server's cannot reach: a request lost on an already-dead connection would otherwise
    /// leave the dialog up with a Cancel that goes exactly where the request went.
    /// </summary>
    public const int CommitGuardMs = 5000;

    /// <summary>
    /// Which entry is current. Bumped by every state change, and carried by the optimistic guard, so a guard
    /// armed for a search that has since been answered no-ops instead of closing the one after it.
    /// </summary>
    int _searchEpoch;

    /// <summary>
    /// Enter the queue. The dialog goes up on the tap rather than on the answer — the wait is a dialog over
    /// whatever the player tapped Play on, and nothing is torn down for a wait of under a minute.
    /// </summary>
    public async Task EnterRankedQueueAsync(DeckChoice deck)
    {
        if (State != MatchmakingDialogState.Hidden)
            return;

        int epoch = ++_searchEpoch;

        State         = MatchmakingDialogState.Searching;
        ByInstant     = null;
        LastEndReason = null;
        Changed();

        _client.ExecuteAction(new PlayerEnqueueForRankedMatch(deck));

        await Task.Delay(CommitGuardMs);

        if (epoch != _searchEpoch)
            return;

        // Nothing came back — not the status push and not a refusal. Treat it locally as the search having
        // timed out rather than leaving a dialog up over a request that never arrived anywhere.
        State         = MatchmakingDialogState.Hidden;
        ByInstant     = null;
        LastEndReason = MatchmakingEndReason.TimedOut;
        Changed();
    }

    /// <summary>
    /// Leave the queue. The dialog dismisses on <c>MatchmakingEnded</c> rather than on the tap, so a cancel
    /// that loses the race to a seat reservation simply never shows the dismiss: the board appears over it
    /// instead, and the attachment is what takes the dialog down.
    /// </summary>
    public void Cancel()
    {
        if (State != MatchmakingDialogState.Searching)
            return;

        State = MatchmakingDialogState.Cancelling;
        Changed();

        _client.ExecuteAction(new PlayerCancelMatchmaking());
    }

    /// <summary> The player has read the notice the last ended search left. </summary>
    public void DismissNotice()
    {
        if (!LastEndReason.HasValue)
            return;

        LastEndReason = null;
        Changed();
    }

    /// <summary>
    /// Lift this account's own newcomer shield. <b>Development-only</b>: the shield covers an account's first
    /// <c>Global.NewcomerShieldMatches</c> ranked matches and a shield on either seat shields the match, so
    /// this is the only way a pair of fresh accounts reaches a stakes tier that moves a rank at all. The
    /// action behind it is refused outside a development environment, and the affordance that calls it is
    /// query-string gated.
    /// <para>
    /// It applies from the next queue entry, because the flag is frozen onto the ticket at enqueue.
    /// </para>
    /// </summary>
    public void DevWaiveNewcomerShield() => _client.ExecuteAction(new PlayerDevWaiveNewcomerShield());

    void Changed() => OnSearchChanged?.Invoke();
}
