using Metaplay.Core;
using Metaplay.Core.Model;
using System.Collections.Generic;

namespace Game.Logic
{
    /// <summary>
    /// Where the table's clocks are, as <b>absolute stamps on the model's clock</b>. Every countdown the
    /// client renders and every timer the actor arms compares the model's current time against a stamp in
    /// here (<c>Docs/protocol.md</c>, "The model's clock").
    /// <para>
    /// A null stamp means <b>not in force</b>, never "already lapsed". A zero-length deadline arms nothing and
    /// the client draws no ring — a ring that drains to nothing and is then not acted on is a lie
    /// (<c>Docs/client.md</c>).
    /// </para>
    /// <para>
    /// This is state, not a projection: the rule action that arms a deadline writes it here, from the model's
    /// current time plus a duration off <see cref="MatchModel.Timings"/>, so a follower re-executing that
    /// action on the same tick reaches the same stamp. It is <em>outside</em>
    /// <see cref="MatchRulesState"/> and therefore outside the rules hash, which is what lets the determinism
    /// suite ask "the same game at zero timings and at real ones" of a model whose stamps legitimately
    /// differ.
    /// </para>
    /// </summary>
    [MetaSerializable]
    public class MatchPacing
    {
        /// <summary> When the deadline in force runs out, or null for none. </summary>
        [MetaMember(1)] public MetaTime?         DeadlineAt   { get; private set; }
        [MetaMember(2)] public MatchDeadlineKind DeadlineKind { get; private set; }
        /// <summary> Whose deadline it is, or <see cref="MatchSeats.None"/> for the shared mulligan and for none. </summary>
        [MetaMember(3)] public int               DeadlineSeat { get; private set; }
        /// <summary> When the first turn stops waiting for a human seat that has not subscribed. </summary>
        [MetaMember(6)] public MetaTime?         JoinWindowEndsAt { get; private set; }
        /// <summary> Per seat, when its grace lapses, or null while it is not on grace. </summary>
        [MetaMember(7)] public List<MetaTime?>   GraceEndsAt      { get; private set; }
        /// <summary>
        /// The deadline a held resolution displaced, kept so it can be put back exactly as it was. A peek
        /// pause is the acting seat's own thinking time, so it neither grants free turn time nor stops the
        /// turn clock — and re-arming a fresh turn deadline afterwards would hand out a whole second turn.
        /// </summary>
        [MetaMember(9)]  public MetaTime?         SuspendedDeadlineAt   { get; private set; }
        [MetaMember(10)] public MatchDeadlineKind SuspendedDeadlineKind { get; private set; }

        public MatchPacing()
        {
            DeadlineSeat = MatchSeats.None;
            GraceEndsAt  = new List<MetaTime?> { null, null };
        }

        public MetaTime? Grace(int seat) => GraceEndsAt != null && seat < GraceEndsAt.Count ? GraceEndsAt[seat] : null;

        // \note Public rather than internal because the rule actions that call these live in a different
        //       folder of the same assembly. Every one of them is called from inside an action's Execute or
        //       from Deal.Create, which is the one pre-timeline entry point.

        /// <summary> Arm a deadline: its kind, its absolute stamp, and the seat that owes it. </summary>
        public void ArmDeadline(MatchDeadlineKind kind, MetaTime at, int seat)
        {
            DeadlineAt   = at;
            DeadlineKind = kind;
            DeadlineSeat = seat;
        }

        /// <summary>
        /// Arm a deadline <paramref name="duration"/> after <paramref name="now"/>, or clear it when the
        /// duration is zero: a zero duration means <b>not in force</b>, not already lapsed.
        /// </summary>
        public void ArmOrClear(MatchDeadlineKind kind, MetaTime now, MetaDuration duration, int seat)
        {
            if (duration <= MetaDuration.Zero)
                ClearDeadline();
            else
                ArmDeadline(kind, now + duration, seat);
        }

        public void ClearDeadline()
        {
            DeadlineAt   = null;
            DeadlineKind = MatchDeadlineKind.Turn;
            DeadlineSeat = MatchSeats.None;
        }

        public void PushDeadline(MetaDuration by)
        {
            if (DeadlineAt.HasValue)
                DeadlineAt = DeadlineAt.Value + by;
        }

        /// <summary>
        /// Push every absolute stamp forward by the same amount. Used when the table was <em>not there</em>
        /// for a while: a stamp is only a promise about how much time a seat gets to act, and time in which
        /// the table could not be reached is not time a seat spent.
        /// </summary>
        public void PushAllClocks(MetaDuration by)
        {
            PushDeadline(by);

            if (SuspendedDeadlineAt.HasValue)
                SuspendedDeadlineAt = SuspendedDeadlineAt.Value + by;
        }

        /// <summary> Put the current deadline aside for the duration of a held resolution. </summary>
        public void SuspendDeadline()
        {
            SuspendedDeadlineAt   = DeadlineAt;
            SuspendedDeadlineKind = DeadlineKind;
        }

        /// <summary> Put the displaced deadline back at the same stamp it had, and forget it. </summary>
        public void ResumeSuspendedDeadline()
        {
            DeadlineAt            = SuspendedDeadlineAt;
            DeadlineKind          = SuspendedDeadlineKind;
            SuspendedDeadlineAt   = null;
            SuspendedDeadlineKind = MatchDeadlineKind.Turn;
        }

        /// <summary>
        /// The join window's stamp, armed when the table is set up and cleared by nothing — the predicate
        /// that reads it stops naming it instead. Writable so the attention fold's own tests can put a table
        /// in that state.
        /// </summary>
        public void ArmJoinWindow(MetaTime? endsAt) => JoinWindowEndsAt = endsAt;

        /// <summary> One seat's grace stamp. <see cref="MatchSetGrace"/> is the only caller. </summary>
        public void SetGrace(int seat, MetaTime? endsAt) => GraceEndsAt[seat] = endsAt;
    }
}
