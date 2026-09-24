using Game.Logic;

namespace Game.Server.Matchmaking
{
    /// <summary>
    /// Where one account stands in a search. Held on the player actor as plain local state — not on the model,
    /// not persisted — because it is a fact about an in-flight request rather than about the account.
    /// </summary>
    public enum MatchmakingSearchPhase
    {
        /// <summary> Not searching. The only phase a new entry is accepted from. </summary>
        None = 0,
        /// <summary> A ticket is in the queue, and the player may still cancel. </summary>
        Searching = 1,
        /// <summary> This actor has committed to a seat. A cancel arriving now is refused. </summary>
        SeatReserved = 2,
    }

    /// <summary> Everything that can move the phase. </summary>
    public enum MatchmakingSearchEvent
    {
        /// <summary> The player's own entry action reached the authoritative run. </summary>
        Enqueued,
        /// <summary> The matchmaker is asking this actor to commit to a seat. </summary>
        SeatAsked,
        /// <summary> The player's own cancel action reached the authoritative run. </summary>
        Cancelled,
        /// <summary> The pairing dissolved and this seat answered, so its reservation is released. </summary>
        ReservationReleased,
        /// <summary> The pairing dissolved and this seat's answer was lost, so the seat is gone. </summary>
        SeatGone,
        /// <summary> A table exists and this account is seated at it. </summary>
        Formed,
        /// <summary> The player-side bound on this phase elapsed with nothing having happened. </summary>
        BoundExpired,
    }

    /// <summary> What the actor does toward the matchmaker or the client after a step, besides an end notice. </summary>
    public enum MatchmakingSearchOutbound
    {
        Nothing,
        /// <summary> The one status push, carrying the instant the player expects to be seated by. </summary>
        StatusUpdate,
        /// <summary> Accept the seat reservation the matchmaker asked for. </summary>
        AcceptSeat,
        /// <summary> Decline it. </summary>
        DeclineSeat,
    }

    /// <summary>
    /// One step: the phase afterwards, the one thing to do, and why the search ended when the client has to be
    /// told it did.
    /// </summary>
    public readonly struct MatchmakingSearchStep
    {
        public readonly MatchmakingSearchPhase    Phase;
        public readonly MatchmakingSearchOutbound Outbound;
        public readonly MatchmakingEndReason?     EndReason;

        public MatchmakingSearchStep(MatchmakingSearchPhase phase, MatchmakingSearchOutbound outbound, MatchmakingEndReason? endReason = null)
        {
            Phase     = phase;
            Outbound  = outbound;
            EndReason = endReason;
        }
    }

    /// <summary>
    /// The player side of the search, as a pure <c>(phase, event) → step</c> function.
    /// <para>
    /// <b>This is the whole cancel-versus-formation race, and it needs no locking</b>: the player's actor is the
    /// single arbiter, so whichever of the cancel and the seat ask reaches its mailbox first is the outcome
    /// (<c>Docs/matchmaking.md</c>, "A cancel and a formation cannot both win").
    /// </para>
    /// </summary>
    public static class MatchmakingSearchPhasePolicy
    {
        /// <summary>
        /// The step this event produces.
        /// </summary>
        /// <param name="isClientConnected">
        /// Whether a client is connected right now. Only the seat reservation reads it: a live connection, not
        /// a live session, which outlives its connection for about as long as a search takes.
        /// </param>
        /// <param name="isInMatch"> Whether this account is already at a table. Only the seat reservation reads it. </param>
        public static MatchmakingSearchStep Step(MatchmakingSearchPhase phase, MatchmakingSearchEvent ev, bool isClientConnected, bool isInMatch = false)
        {
            switch (ev)
            {
                case MatchmakingSearchEvent.Enqueued:
                    // One entry per player. A second one is refused, and told so.
                    return phase == MatchmakingSearchPhase.None
                        ? new MatchmakingSearchStep(MatchmakingSearchPhase.Searching, MatchmakingSearchOutbound.StatusUpdate)
                        : new MatchmakingSearchStep(phase, MatchmakingSearchOutbound.Nothing, MatchmakingEndReason.Refused);

                case MatchmakingSearchEvent.SeatAsked:
                    // Already committed to another formation: declined, phase unchanged. This also stops a pairing
                    // whose two tickets are the same account.
                    if (phase == MatchmakingSearchPhase.SeatReserved && isClientConnected && !isInMatch)
                        return new MatchmakingSearchStep(MatchmakingSearchPhase.SeatReserved, MatchmakingSearchOutbound.DeclineSeat);

                    // The cancel already won the race and was already answered, so this decline is silent.
                    if (phase == MatchmakingSearchPhase.None)
                        return new MatchmakingSearchStep(MatchmakingSearchPhase.None, MatchmakingSearchOutbound.DeclineSeat);

                    // Declined for a reason the player did not ask for; nothing re-queues a declined seat, so the
                    // client has to be told.
                    if (!isClientConnected || isInMatch)
                        return new MatchmakingSearchStep(MatchmakingSearchPhase.None, MatchmakingSearchOutbound.DeclineSeat, MatchmakingEndReason.SeatLost);

                    // The commit point.
                    return new MatchmakingSearchStep(MatchmakingSearchPhase.SeatReserved, MatchmakingSearchOutbound.AcceptSeat);

                case MatchmakingSearchEvent.Cancelled:
                    // After the seat is committed a cancel is refused silently; the association is inbound.
                    if (phase == MatchmakingSearchPhase.SeatReserved)
                        return new MatchmakingSearchStep(phase, MatchmakingSearchOutbound.Nothing);

                    // From None the client is asking to leave a queue this actor does not think it is in; it is
                    // answered anyway, or its dialog never closes.
                    return new MatchmakingSearchStep(MatchmakingSearchPhase.None, MatchmakingSearchOutbound.Nothing, MatchmakingEndReason.Cancelled);

                case MatchmakingSearchEvent.ReservationReleased:
                    // Back to searching under the original stamp. The dialog never stopped saying "searching".
                    return new MatchmakingSearchStep(MatchmakingSearchPhase.Searching, MatchmakingSearchOutbound.Nothing);

                case MatchmakingSearchEvent.SeatGone:
                    // A lost answer leaves a seat committed to a formation that dissolved. It pays one tap.
                    return phase == MatchmakingSearchPhase.None
                        ? new MatchmakingSearchStep(MatchmakingSearchPhase.None, MatchmakingSearchOutbound.Nothing)
                        : new MatchmakingSearchStep(MatchmakingSearchPhase.None, MatchmakingSearchOutbound.Nothing, MatchmakingEndReason.SeatLost);

                case MatchmakingSearchEvent.Formed:
                    // The board appearing is what dismisses the dialog.
                    return new MatchmakingSearchStep(MatchmakingSearchPhase.None, MatchmakingSearchOutbound.Nothing);

                case MatchmakingSearchEvent.BoundExpired:
                    // Both waiting states are bounded on the player's own actor, so a matchmaker restart becomes
                    // "tap Play again".
                    return phase == MatchmakingSearchPhase.None
                        ? new MatchmakingSearchStep(MatchmakingSearchPhase.None, MatchmakingSearchOutbound.Nothing)
                        : new MatchmakingSearchStep(MatchmakingSearchPhase.None, MatchmakingSearchOutbound.Nothing, MatchmakingEndReason.TimedOut);

                default:
                    return new MatchmakingSearchStep(phase, MatchmakingSearchOutbound.Nothing);
            }
        }
    }
}
