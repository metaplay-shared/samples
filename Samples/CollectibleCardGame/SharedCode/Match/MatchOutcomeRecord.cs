using Metaplay.Core;
using Metaplay.Core.Model;
using System.Collections.Generic;

namespace Game.Logic
{
    /// <summary>
    /// The table's own phases, which are not the engine's. The engine walks
    /// <c>Mulligan → Playing → Complete</c> and knows nothing about wagers; the table walks
    /// <c>Playing → HeistPick → Ended</c>, or <c>Abandoned</c> (<c>Docs/match.md</c>, "Phases").
    /// The mapping is one-way: engine <c>Complete</c> makes the table compute a result and move on, and
    /// everything before that — the mulligan included — is table <see cref="Playing"/>.
    /// <para>
    /// Named apart from the engine's <see cref="MatchPhase"/> because both live in this assembly and the two
    /// state machines are at different levels. The client reads the engine phase off the public board to know
    /// whether to draw the mulligan overlay.
    /// </para>
    /// </summary>
    [MetaSerializable]
    public enum MatchTablePhase
    {
        /// <summary> The game is being played, mulligan included. </summary>
        Playing = 0,
        /// <summary> The winner is picking: the table runs the Heist itself, as a phase of its own. </summary>
        HeistPick = 1,
        /// <summary> The result-bearing finish: the outcome is computed and owed to both accounts. </summary>
        Ended = 2,
        /// <summary>
        /// The result-less finish, and deliberately narrow: a match is abandoned only if it never started.
        /// Only an expired join window enters this.
        /// </summary>
        Abandoned = 3,
    }

    /// <summary> Which errand a terminal table owes an account. </summary>
    [MetaSerializable]
    public enum MatchTerminalKind
    {
        /// <summary> A real result: record it, then release the player. </summary>
        Result = 0,
        /// <summary>
        /// A table that died with no result. Nothing to record, but the pointer still has to be cleared or
        /// the account is locked out of matchmaking forever.
        /// </summary>
        Abandoned = 1,
    }

    /// <summary>
    /// How a match came out <em>for one account</em>. The table speaks in seats and the record speaks in wins,
    /// so the seat-to-account translation happens once, where the seat is still in scope.
    /// </summary>
    [MetaSerializable]
    public enum MatchAccountOutcome
    {
        Win = 0,
        Loss = 1,
        Draw = 2,
    }

    /// <summary>
    /// What the Heist moved. <b>Non-null exactly when the phase ran</b>: a tier that moves no ranks — a
    /// favourite's win, a shielded match, practice, a draw — leaves it null rather than carrying a record of
    /// nothing, and that one fact is what routes the client to the Heist screen or to the plain result.
    /// <para>
    /// The picks are a <em>list</em> because the underdog tier takes two, and how many are owed is not stored:
    /// it is a pure function of the frozen tier and which seat won, all public
    /// (<see cref="MatchHeistPolicy.PicksOwedForMatch"/>), and a stored copy would be a second answer to it.
    /// </para>
    /// </summary>
    [MetaSerializable]
    public class HeistResult
    {
        /// <summary>
        /// The cards the winner has taken, in the order they were picked. Empty while the phase is still
        /// waiting on a pick, and shorter than the tier owes when the eligible list ran out first.
        /// </summary>
        [MaxCollectionSize(2)]
        [MetaMember(2)] public List<CardId> Picks { get; set; } = new List<CardId>();

        /// <summary>
        /// Whether any of the picks was the deterministic default rather than a choice. Shown on both clients:
        /// the default is a function of state both players watched, so saying so is informational rather than
        /// a disclosure (<c>Docs/match.md</c>, "The deadline defaults in the absent player's own
        /// interest").
        /// </summary>
        [MetaMember(3)] public bool AnyPickAutoDefaulted { get; set; }

        public HeistResult() { }

        public HeistResult(List<CardId> picks, bool anyPickAutoDefaulted)
        {
            Picks                = picks;
            AnyPickAutoDefaulted = anyPickAutoDefaulted;
        }
    }

    /// <summary>
    /// The outcome as the accounts are told it. Kept apart from the engine's <c>MatchResult</c>: that one is
    /// what the rules produced, this one is what the table is owed — the seat that won, whether anybody was
    /// still at the table, and what the Heist moved.
    /// </summary>
    [MetaSerializable]
    public class MatchOutcomeRecord
    {
        [MetaMember(1)] public MatchOutcome   Outcome    { get; set; }
        /// <summary> The winning seat, or <see cref="MatchSeats.None"/> on a draw. </summary>
        [MetaMember(2)] public int            WinnerSeat { get; set; }
        [MetaMember(3)] public int            FinalTurn  { get; set; }
        [MetaMember(4)] public MatchEndCause  Cause      { get; set; }
        /// <summary> Per seat, whether its owner was still at the table at the finish. Recorded, not counted. </summary>
        [MetaMember(5)] public List<bool>     WasPresentAtFinish { get; set; }
        /// <summary> Whether ranks were ever at stake, copied off the stakes so the record stands alone. </summary>
        [MetaMember(6)] public bool           WasRanked  { get; set; }
        /// <summary> What the Heist moved, or null when no phase ran. See <see cref="HeistResult"/>. </summary>
        [MetaMember(7)] public HeistResult    Heist      { get; set; }
        /// <summary>
        /// When the game decided. Set once, from the stamp the actor read when it built this record, and
        /// carried on the action's payload so followers write the same value.
        /// </summary>
        [MetaMember(8)] public MetaTime       DecidedAt  { get; set; }

        public MatchOutcomeRecord() { }

        public MatchOutcomeRecord(MatchOutcome outcome, int winnerSeat, int finalTurn, MatchEndCause cause, List<bool> wasPresentAtFinish, bool wasRanked, MetaTime decidedAt)
        {
            Outcome            = outcome;
            WinnerSeat         = winnerSeat;
            FinalTurn          = finalTurn;
            Cause              = cause;
            WasPresentAtFinish = wasPresentAtFinish;
            WasRanked          = wasRanked;
            DecidedAt          = decidedAt;
        }

        public bool IsDraw => Outcome == MatchOutcome.Draw;

        /// <summary> How the match came out for the account that owned this seat. </summary>
        public MatchAccountOutcome OutcomeForSeat(int seat)
        {
            if (IsDraw)
                return MatchAccountOutcome.Draw;

            return WinnerSeat == seat ? MatchAccountOutcome.Win : MatchAccountOutcome.Loss;
        }
    }
}
