using Metaplay.Core;
using Metaplay.Core.Model;

namespace Game.Logic
{
    /// <summary>
    /// Why a bot took over a player's seat. The reasons are kept separate because the result rules treat a
    /// deliberate leave differently, and nothing else can tell it apart from a disconnect
    /// (<c>docs/match.md</c>, "When players stop playing").
    /// </summary>
    [MetaSerializable]
    public enum MatchSeatLossReason
    {
        /// <summary>The seat has no owner, or the owner is still playing it.</summary>
        None = 0,

        /// <summary>The player was connected but missed enough move deadlines that a bot covered the seat.</summary>
        DeadlineLapse = 1,

        /// <summary>The player disconnected and their grace timer ended.</summary>
        Disconnect = 2,

        /// <summary>The player used the Leave control.</summary>
        DeliberateLeave = 3,
    }

    /// <summary>
    /// One human seat's result, captured when the match enters <see cref="MatchPhase.Ended"/>. It is not computed
    /// later because <see cref="FinishedByPlayer"/> depends on state that changes afterwards (restoring a table clears
    /// every connected flag), so the result stays the same however late it is delivered. <see cref="Delivered"/> is
    /// persisted with the table, so a restored table knows which seats still need their result
    /// (<c>docs/player.md</c>, "Delivery from the match").
    /// </summary>
    [MetaSerializable]
    public class MatchSeatResult
    {
        [MetaMember(1)] public int Seat { get; private set; }

        /// <summary>The player this result belongs to. Always a valid id, because seats without an owner get no result.</summary>
        [MetaMember(2)] public EntityId PlayerId { get; private set; }

        /// <summary>Position in the final standings, 0 for the winner.</summary>
        [MetaMember(3)] public int Position { get; private set; }

        [MetaMember(4)] public int TricksWon { get; private set; }

        /// <summary>How many other seats had an owner who subscribed to the table at least once.</summary>
        [MetaMember(5)] public int HumanOpponents { get; private set; }

        /// <summary>Whether the player was still playing the seat, not covered by a bot and connected, when the match ended.</summary>
        [MetaMember(6)] public bool FinishedByPlayer { get; private set; }

        /// <summary>Why the owner stopped playing this seat, or <see cref="MatchSeatLossReason.None"/>.</summary>
        [MetaMember(7)] public MatchSeatLossReason SeatLossReason { get; private set; }

        /// <summary>
        /// Whether the player has acknowledged the result. The table keeps sending it until they do.
        /// </summary>
        [MetaMember(8)] public bool Delivered { get; set; }

        public MatchSeatResult() { }

        public MatchSeatResult(int seat, EntityId playerId, int position, int tricksWon, int humanOpponents, bool finishedByPlayer, MatchSeatLossReason seatLossReason)
        {
            Seat             = seat;
            PlayerId         = playerId;
            Position         = position;
            TricksWon        = tricksWon;
            HumanOpponents   = humanOpponents;
            FinishedByPlayer = finishedByPlayer;
            SeatLossReason   = seatLossReason;
        }

        /// <summary>Whether this seat won: first place in <see cref="MatchRules.ComputeStandings"/>, after tie-breaks.</summary>
        public bool IsWin => Position == 0;

        public override string ToString() => $"seat {Seat} ({PlayerId}): #{Position + 1}, {TricksWon} tricks, delivered {Delivered}";
    }
}
