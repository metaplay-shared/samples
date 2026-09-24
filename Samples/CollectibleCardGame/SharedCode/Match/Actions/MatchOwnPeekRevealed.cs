using Metaplay.Core.Model;

namespace Game.Logic
{
    /// <summary>
    /// What a held peek is showing one seat, delivered to that seat alone.
    /// <para>
    /// That a resolution is held, on which seat, over how many cards, and how many may be kept is all public
    /// and already on the timeline — <c>Rules.PendingChoice</c>. Only <em>which cards</em> were revealed is
    /// secret, and that is this payload. It rides the timeline for the same reason the hand's changes do: the
    /// question must not reach the seat before the operation that posed it.
    /// </para>
    /// </summary>
    [ModelAction(ActionCodes.MatchOwnPeekRevealed)]
    public class MatchOwnPeekRevealed : MatchAddressedAction
    {
        /// <summary> The revealed cards, top of deck first, or null when the resolution has ended. </summary>
        public PendingChoiceView Revealed { get; private set; }

        public MatchOwnPeekRevealed() { }

        public MatchOwnPeekRevealed(PendingChoiceView revealed)
        {
            Revealed = revealed;
        }

        protected override MetaActionResult Execute(MatchModel match)
        {
            match.OwnPeek = Revealed;
            match.ClientListener.OnBoardChanged();
            return MetaActionResult.Success;
        }
    }
}
