namespace Game.Logic
{
    /// <summary>
    /// What the client-side match screens are told when the replicated match model changes. Dispatched from
    /// the match actions' <c>Execute</c>, which runs on every client's copy of the model — so every hook here
    /// is a notification about public state, never a carrier of it.
    /// <para>
    /// This is the <em>match model's</em> listener and is separate from
    /// <see cref="IPlayerModelClientListener"/>'s handles: the two models change for different reasons and a
    /// screen that draws the board has no business being repainted by a deck rename. On the server every
    /// implementation is the empty one.
    /// </para>
    /// </summary>
    public interface IMatchModelClientListener
    {
        /// <summary>
        /// One thing that happened, in the order it happened. The board queues these as beats; the event feed
        /// reads the tail of the model's own history.
        /// </summary>
        void OnMatchEvent(MatchEvent ev);

        /// <summary> A step landed: the public board and the pacing stamps have been replaced. </summary>
        void OnBoardChanged();

        /// <summary> Who is seated, whether they are here, and whether a bot is covering. </summary>
        void OnSeatsChanged();

        /// <summary> A clock moved with no engine step behind it — grace armed, reserve spent, join window. </summary>
        void OnPacingChanged();

        /// <summary> The table moved to a new phase. </summary>
        void OnPhaseChanged(MatchTablePhase phase);

        /// <summary> The game is over and the outcome is on the model. </summary>
        void OnResult(MatchOutcomeRecord result);

        /// <summary> One seat's account has acknowledged the result. </summary>
        void OnResultAcked(int seat);
    }

    /// <summary> The listener a model with nobody watching it dispatches to. Always the server's. </summary>
    public class EmptyMatchModelClientListener : IMatchModelClientListener
    {
        public static readonly EmptyMatchModelClientListener Instance = new EmptyMatchModelClientListener();

        public void OnMatchEvent(MatchEvent ev) { }
        public void OnBoardChanged() { }
        public void OnSeatsChanged() { }
        public void OnPacingChanged() { }
        public void OnPhaseChanged(MatchTablePhase phase) { }
        public void OnResult(MatchOutcomeRecord result) { }
        public void OnResultAcked(int seat) { }
    }
}
