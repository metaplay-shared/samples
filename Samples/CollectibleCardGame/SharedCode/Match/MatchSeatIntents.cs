using Metaplay.Core.Model;

namespace Game.Logic
{
    /// <summary>
    /// Type codes for the seat-intent hierarchy. Its own registry, like the engine's event and intent codes.
    /// </summary>
    public static class MatchSeatIntentCodes
    {
        public const int Leave     = 1;
        public const int HeistPick = 3;
        public const int DebugWin  = 4;
    }

    /// <summary>
    /// What a seat asks the <em>table</em> to do, as opposed to what it asks the game to do. The split is
    /// deliberate: occupancy, cover and the wager are the actor's and not the engine's, so the engine's
    /// <see cref="MatchIntent"/> hierarchy stays about the rules and this one stays about the seat.
    /// </summary>
    [MetaSerializable]
    public abstract class MatchSeatIntent
    {
        protected MatchSeatIntent() { }
    }

    /// <summary> Ask the server to finish a developer player's match and open a real Heist. </summary>
    [MetaSerializableDerived(MatchSeatIntentCodes.DebugWin)]
    public class DebugWinMatchIntent : MatchSeatIntent { }

    /// <summary>
    /// Leave the table. Conceding is the same thing: a disconnect that skips grace. The game is still played
    /// out and the result is still real, which is what makes leaving cost exactly what staying would
    /// (<c>Docs/match.md</c>).
    /// </summary>
    [MetaSerializableDerived(MatchSeatIntentCodes.Leave)]
    public class LeaveMatchIntent : MatchSeatIntent
    {
        public LeaveMatchIntent() { }
    }


    /// <summary> The winner's Heist pick. </summary>
    [MetaSerializableDerived(MatchSeatIntentCodes.HeistPick)]
    public class HeistPickIntent : MatchSeatIntent
    {
        [MetaMember(1)] public CardId Card { get; private set; }

        public HeistPickIntent() { }

        public HeistPickIntent(CardId card)
        {
            Card = card;
        }
    }
}
