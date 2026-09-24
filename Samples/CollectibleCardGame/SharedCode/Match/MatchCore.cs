using Metaplay.Core.Model;
using System;

namespace Game.Logic
{
    /// <summary>
    /// The rules' own phases. There is no Heist phase and no Ended here — those belong to the match actor
    /// that drives the table (<c>Docs/match.md</c>); mapping one onto the other is the host's job.
    /// </summary>
    [MetaSerializable]
    public enum MatchPhase
    {
        Mulligan = 0,
        Playing  = 1,
        Complete = 2,
    }

    /// <summary> Who won, or that nobody did. </summary>
    [MetaSerializable]
    public enum MatchOutcome
    {
        Seat0Wins = 0,
        Seat1Wins = 1,
        Draw      = 2,
    }

    /// <summary> How the game ended. Both Dens reaching zero in one resolution is its own cause, not a Den at zero. </summary>
    [MetaSerializable]
    public enum MatchEndCause
    {
        DenAtZero      = 0,
        BothDensAtZero = 1,
        DeveloperWin  = 2,
    }

    /// <summary>
    /// The deadlines the host arms and expires. The model holds their absolute stamps and never evaluates one:
    /// the host says which lapsed (<c>Docs/rules.md</c>, "Deadlines are data, not decisions").
    /// </summary>
    [MetaSerializable]
    public enum MatchDeadlineKind
    {
        /// <summary> The shared mulligan deadline. A seat that never submitted keeps its dealt hand. </summary>
        Mulligan     = 0,
        /// <summary> The seat on turn ran out of time: the rest of its turn is played out and then ended. </summary>
        Turn         = 1,
        /// <summary> The owner of a held resolution did not choose: the deterministic default is applied. </summary>
        EffectChoice = 2,
        /// <summary>
        /// The winner has not made a Heist pick: the auto-default is applied to that slot. Armed by
        /// <see cref="MatchArmHeistDeadline"/> with a carried stamp; the rules never arm it.
        /// </summary>
        HeistPick    = 3,
    }

    /// <summary> What the host must schedule next. </summary>
    public enum MatchPendingKind
    {
        /// <summary> Both seats may submit a mulligan, in either order. </summary>
        AwaitingMulligan     = 0,
        /// <summary> The seat on turn may act. </summary>
        AwaitingIntent       = 1,
        /// <summary> A resolution is paused for one seat's peek choice. Exclusivity survives the pause. </summary>
        AwaitingEffectChoice = 3,
        Complete             = 4,
    }

    /// <summary>
    /// The table is hard two-seat, by design. A seat is 0 or 1 and
    /// nothing else: the engine cannot tell a human from a bot and must not be able to.
    /// </summary>
    public static class MatchSeats
    {
        public const int Count = 2;
        public const int None  = -1;

        public static int Other(int seat) => 1 - seat;

        public static bool IsValid(int seat) => seat == 0 || seat == 1;
    }

    /// <summary>
    /// A rules invariant the rules could not uphold. This is never a refusal a player can cause: every
    /// player-reachable failure is a <see cref="MatchIntentResult"/>. Reaching this means content or a host
    /// drove the table somewhere the rules do not define, and the match cannot continue.
    /// <para>
    /// It is fatal on both sides: the SDK treats an exception inside an action's <c>Execute</c> as
    /// unrecoverable.
    /// </para>
    /// </summary>
    public class MatchEngineException : Exception
    {
        public MatchEngineException(string message) : base(message) { }
    }
}
