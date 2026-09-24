using Game.Logic;
using Metaplay.Core;

namespace Game.Server.Match
{
    /// <summary>
    /// The stamps the table is waiting on that live in the actor rather than in the model: a bot's think delay
    /// and a retry of an unacknowledged result. Neither belongs on a checksummed timeline. They are taken off
    /// <see cref="MetaTime.Now"/>, which on the actor is the clock the model's ticks follow, so the fold
    /// compares them with the model's stamps directly.
    /// </summary>
    public readonly struct HostPendingWork
    {
        /// <summary> When a bot seat's armed decision is due, or null when none is waiting. </summary>
        public readonly MetaTime? BotDelayAt;
        /// <summary> When to re-offer an unacknowledged result, or null when nothing is outstanding. </summary>
        public readonly MetaTime? ResultRetryAt;

        public HostPendingWork(MetaTime? botDelayAt, MetaTime? resultRetryAt)
        {
            BotDelayAt    = botDelayAt;
            ResultRetryAt = resultRetryAt;
        }

        public static readonly HostPendingWork None = new HostPendingWork(null, null);
    }

    /// <summary>
    /// When the table next needs attention, as one pure fold over its five sources: the deadline, the join
    /// window, grace, the bot delay and the result retry. Each is a way to leave a table waiting on something
    /// nobody will offer again.
    /// </summary>
    public static class MatchDeadlinePolicy
    {
        /// <summary>
        /// The earliest stamp the table is waiting on, or null when it is waiting on nothing: no deadline, no
        /// grace, no host-held work, and, for a finished table, every seated account having acknowledged.
        /// </summary>
        public static MetaTime? NextAttentionAt(MatchModel model, HostPendingWork host)
        {
            MatchPacing pacing = model.Pacing;
            MetaTime?   next   = null;

            // A deadline its seat is not governed by is held out here, where the wake schedule comes from, or
            // the fold would keep answering a stamp whose dispatch is refused.
            if (DeadlineIsInForce(model))
                next = Earlier(next, pacing.DeadlineAt);

            // The join window stamp is never cleared, so it is offered only while it can still do something.
            if (JoinWindowIsInForce(model))
                next = Earlier(next, pacing.JoinWindowEndsAt);

            foreach (MetaTime? grace in pacing.GraceEndsAt)
                next = Earlier(next, grace);

            next = Earlier(next, host.BotDelayAt);

            // A finished table is waiting on a result its accounts have not folded in. The actor's retry stamp
            // is the only thing that keeps it in the fold.
            if (OwesDelivery(model))
                next = Earlier(next, host.ResultRetryAt);

            return next;
        }

        /// <summary>
        /// Whether this table still owes its accounts an errand: a <b>result</b> to apply, or, for a table that
        /// never started, a <b>pointer to clear</b>. Keyed on the record rather than the phase, because the
        /// outcome goes on the model before the phase moves.
        /// <para>
        /// <b>Not while the Heist is running.</b> A result delivered mid-pick has no picks on it, and the
        /// account-side gate is keyed on the match id alone, so the picks could never be delivered afterwards.
        /// </para>
        /// </summary>
        public static bool OwesDelivery(MatchModel model)
        {
            if (model.Phase == MatchTablePhase.HeistPick)
                return false;

            return (model.Result != null || model.Phase == MatchTablePhase.Abandoned) && !model.AllSeatsAcked;
        }

        /// <summary>
        /// Whether the game has decided, from <b>both</b> the outcome record and the engine's phase. Inside a
        /// timer handler whose auto-play finishes the game, the record is still null until the post-action step
        /// writes it, and only <see cref="MatchPhase.Complete"/> says the game is over.
        /// </summary>
        public static bool HasDecided(MatchModel model)
            => model.Result != null || model.Rules?.Phase == MatchPhase.Complete;

        /// <summary>
        /// Whether the join window still has anybody to wait for: a human seat that has not arrived and is not
        /// on grace. A seat that arrived and dropped is on grace, which is the rule for a player who was here.
        /// </summary>
        public static bool JoinWindowIsInForce(MatchModel model)
        {
            if (!model.Pacing.JoinWindowEndsAt.HasValue || model.IsTerminal)
                return false;

            for (int seat = 0; seat < model.Seats.Count; seat++)
            {
                if (SeatHasNotArrived(model, seat))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Whether this seat is a human the table is still waiting on for the first time: owned by a person,
        /// not connected, not covered, and not on grace.
        /// </summary>
        public static bool SeatHasNotArrived(MatchModel model, int seat)
        {
            MatchSeat roster = model.Seats[seat];

            return roster.Occupancy == SeatOccupancy.Human
                   && !roster.IsConnected
                   && !model.Pacing.Grace(seat).HasValue;
        }

        /// <summary>
        /// Whether any seat is owned by a person. A table of two bots satisfies "nobody arrived" vacuously; the
        /// matchmaker never forms one, so this is a guard rather than a case.
        /// </summary>
        public static bool HasAHumanSeat(MatchModel model)
        {
            foreach (MatchSeat seat in model.Seats)
            {
                if (seat.PlayerId.IsValid)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Whether the table never started: it has a human seat, and every one of them has not arrived. The one
        /// condition under which a table is <c>Abandoned</c>.
        /// </summary>
        public static bool NeverStarted(MatchModel model)
        {
            if (!HasAHumanSeat(model))
                return false;

            for (int seat = 0; seat < model.Seats.Count; seat++)
            {
                if (model.Seats[seat].PlayerId.IsValid && !SeatHasNotArrived(model, seat))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Whether a table that has <b>lost everyone</b> is played out to a real result: leaving costs what
        /// staying would (<c>Docs/match.md</c>). Two clauses guard what a call site must not have to:
        /// <list type="bullet">
        /// <item><b>A live join window is not an empty table.</b> A seat that has not arrived yet looks, seat by
        /// seat, like one whose player left.</item>
        /// <item><b>A table of two formation bots has lost nobody.</b></item>
        /// </list>
        /// </summary>
        public static bool ShouldPlayOut(MatchModel model)
        {
            // Nothing left to play. Both facts, because inside a timer handler the record trails the phase.
            if (model.IsTerminal || HasDecided(model))
                return false;

            if (!HasAHumanSeat(model))
                return false;

            foreach (MatchSeat seat in model.Seats)
            {
                if (seat.IsConnectedHuman)
                    return false;
            }

            // Still owed grace by somebody? Then nobody is gone yet.
            for (int seat = 0; seat < model.Seats.Count; seat++)
            {
                if (model.Pacing.Grace(seat).HasValue)
                    return false;
            }

            return !JoinWindowIsInForce(model);
        }

        /// <summary>
        /// Whether the deadline in force may be dispatched: it exists, and its seat is one the deadline governs
        /// rather than one grace is holding (<see cref="MatchSeatPolicy.TurnDeadlineGoverns"/>). The mulligan's
        /// shared deadline is in force only while neither seated human is away, so it cannot resolve both hands
        /// the moment one player comes back.
        /// </summary>
        public static bool DeadlineIsInForce(MatchModel model)
        {
            MatchPacing pacing = model.Pacing;
            if (!pacing.DeadlineAt.HasValue)
                return false;

            if (pacing.DeadlineSeat == MatchSeats.None)
            {
                foreach (MatchSeat seat in model.Seats)
                {
                    if (MatchSeatPolicy.IsAwayHuman(seat))
                        return false;
                }

                return true;
            }

            return MatchSeatPolicy.TurnDeadlineGoverns(model.Seats[pacing.DeadlineSeat]);
        }

        static MetaTime? Earlier(MetaTime? current, MetaTime? candidate)
        {
            if (!candidate.HasValue)
                return current;
            if (!current.HasValue)
                return candidate;

            return candidate.Value < current.Value ? candidate : current;
        }
    }
}
