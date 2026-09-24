using Metaplay.Core;
using System.Collections.Generic;

namespace Game.Logic
{
    /// <summary> What the host must schedule next, read after every action it commits. </summary>
    public readonly struct MatchPendingWork
    {
        public readonly MatchPendingKind  Kind;
        /// <summary> The seat that owes something, or <see cref="MatchSeats.None"/> when both do or nobody does. </summary>
        public readonly int               Seat;
        public readonly MetaTime?         DeadlineAt;
        public readonly MatchDeadlineKind DeadlineKind;

        public MatchPendingWork(MatchPendingKind kind, int seat, MetaTime? deadlineAt, MatchDeadlineKind deadlineKind)
        {
            Kind         = kind;
            Seat         = seat;
            DeadlineAt   = deadlineAt;
            DeadlineKind = deadlineKind;
        }

        public override string ToString() => $"{Kind} seat{Seat}";
    }

    /// <summary>
    /// Something that chooses one action for a seat from what the rules allow. <see cref="BotPolicy"/> is the
    /// implementation; a lapsed turn deadline and a played-out table are the host's two uses of it.
    /// <para>
    /// A decision must be a pure function of (seat view, the source's own seed): the same position gives the
    /// same action, which is what makes a delayed decision safe to discard and re-derive
    /// (<c>Docs/bots.md</c>).
    /// </para>
    /// </summary>
    public interface IActionSource
    {
        /// <summary>
        /// The action to take, or null to end the turn. The view names the public members and has no name
        /// for the opposing hand or either deck order, which a reflection test pins.
        /// </summary>
        MatchIntent ChooseAction(SeatView view, int seat);
    }

    /// <summary>
    /// What the table is waiting for, folded out of the model. The host and a bot policy read the same
    /// answer.
    /// </summary>
    public static class MatchPending
    {
        public static MatchPendingWork Of(MatchModel match)
        {
            MatchRulesState rules  = match.Rules;
            MatchPacing     pacing = match.Pacing;

            if (rules.Phase == MatchPhase.Complete)
                return new MatchPendingWork(MatchPendingKind.Complete, MatchSeats.None, null, MatchDeadlineKind.Turn);

            if (rules.PendingChoice != null)
                return new MatchPendingWork(MatchPendingKind.AwaitingEffectChoice, rules.PendingChoice.Seat, pacing.DeadlineAt, pacing.DeadlineKind);

            if (rules.Phase == MatchPhase.Mulligan)
                return new MatchPendingWork(MatchPendingKind.AwaitingMulligan, MatchSeats.None, pacing.DeadlineAt, pacing.DeadlineKind);

            return new MatchPendingWork(MatchPendingKind.AwaitingIntent, rules.SeatOnTurn, pacing.DeadlineAt, pacing.DeadlineKind);
        }
    }
}
