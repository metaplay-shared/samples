namespace Game.Logic
{
    /// <summary>
    /// Client-side observer for changes to the root player model (the parts owned by no subsystem). Implemented
    /// by a client service; shared code dispatches through <see cref="IPlayerModelClientListener.Root"/>.
    /// </summary>
    public interface IRootPlayerModelChangeObserver
    {
        // Used when there is no specific handling for the change
        void GenericPropertyChanged(string propertyName);

        // Example of specific handling
        // void OnPlayerLevelChanged(int playerLevel)
    }

    /// <summary>
    /// Client-side observer for the account's collection: the card-to-rank map, saved decks and lock slots.
    /// Split from <see cref="IRootPlayerModelChangeObserver"/> rather than folded into it so a screen that
    /// only draws the collection is not told about every unrelated account change — and the Heist is what
    /// makes that pay off, because a rank arriving from a match has to reach the collection screens and
    /// nothing else.
    /// </summary>
    public interface ICollectionChangeObserver
    {
        // Used when there is no specific handling for the change
        void GenericPropertyChanged(string propertyName);

        // Example of specific handling
        // void OnDeckSaved(int deckId)
    }

    /// <summary>
    /// Hooks the server-side PlayerActor can implement to react to player actions (e.g. to talk to other
    /// entities or trigger side effects). Server listeners must never mutate model state.
    /// </summary>
    public interface IPlayerModelServerListener
    {
        /// <summary> Triggered by <see cref="PlayerSetDisplayName"/> after the display name changes. </summary>
        void OnDisplayNameChanged(string displayName);

        void OnRankedRecordChanged();

        /// <summary>
        /// Triggered by <see cref="PlayerStartPracticeMatch"/> once the deck has been accepted. The actor
        /// mints the table and points the account at it; the action itself cannot, because creating an entity
        /// is an external side effect and an action's <c>Execute</c> must stay a pure state change.
        /// </summary>
        void StartPracticeMatch(DeckChoice deck, BotProfileId botProfile);

        /// <summary>
        /// Triggered by <see cref="PlayerEnqueueForRankedMatch"/> once the deck has been accepted. The actor
        /// freezes the deck, its ranks, its lock set and this account's rating onto a ticket and hands it to
        /// the matchmaker — the freeze is the queue's whole honesty, and none of it is re-read afterwards.
        /// </summary>
        void EnqueueForRankedMatch(DeckChoice deck);

        /// <summary>
        /// Triggered by <see cref="PlayerCancelMatchmaking"/>. The actor decides whether there is anything to
        /// cancel: a cancel that arrives after the seat reservation committed is refused there, because the
        /// actor is the single arbiter of that race and the client cannot see the phase.
        /// </summary>
        void CancelMatchmaking();
    }

    /// <summary>
    /// The client's model-change observers, one typed handle per subsystem, each implemented by the client
    /// service that owns that part of the UI. Every handle is null when nobody observes — always on the
    /// server, and per-handle on the client — so shared code null-checks on every dispatch:
    /// <c>player.ClientListener.Root?.GenericPropertyChanged(...)</c>.
    /// <para>
    /// Subsystems add sibling handles as they land (e.g. an <c>IWalletChangeObserver Wallet { get; }</c>),
    /// which costs the Empty implementation below one line per subsystem rather than one per event.
    /// </para>
    /// </summary>
    public interface IPlayerModelClientListener
    {
        IRootPlayerModelChangeObserver Root { get; }
        ICollectionChangeObserver Collection { get; }
    }

    public class EmptyPlayerModelServerListener : IPlayerModelServerListener
    {
        public static readonly EmptyPlayerModelServerListener Instance = new EmptyPlayerModelServerListener();

        public void OnDisplayNameChanged(string displayName) { }

        public void OnRankedRecordChanged() { }

        public void StartPracticeMatch(DeckChoice deck, BotProfileId botProfile) { }

        public void EnqueueForRankedMatch(DeckChoice deck) { }

        public void CancelMatchmaking() { }
    }

    public class EmptyPlayerModelClientListener : IPlayerModelClientListener
    {
        public static readonly EmptyPlayerModelClientListener Instance = new EmptyPlayerModelClientListener();

        public IRootPlayerModelChangeObserver Root => null;
        public ICollectionChangeObserver Collection => null;
    }
}
