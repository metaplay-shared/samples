namespace Game.Logic
{
    /// <summary>
    /// Where the player stands in a search. The player actor keeps it in memory, not in the model, because it
    /// describes an in-memory queue entry in the matchmaker. A persisted "searching" flag could outlive that entry
    /// and leave the player on a searching screen that nothing would ever end.
    /// </summary>
    public enum MatchmakingSearchPhase
    {
        /// <summary>Not looking for a table.</summary>
        Idle = 0,

        /// <summary>In the matchmaker's queue, and free to cancel.</summary>
        Searching = 1,

        /// <summary>Committed to a seat at a table being created. A cancel is refused in this phase.</summary>
        Reserved = 2,
    }

    /// <summary>Everything that moves the player's search phase.</summary>
    public enum MatchmakingSearchEvent
    {
        /// <summary>The client asked to enter matchmaking.</summary>
        PlayPressed,

        /// <summary>The client asked to cancel the search.</summary>
        CancelPressed,

        /// <summary>The matchmaker asks this player to commit to a seat at a table it is about to create.</summary>
        SeatOffered,

        /// <summary>The matchmaker created a table with this player seated.</summary>
        SeatAssigned,

        /// <summary>The formation this player committed to created no table.</summary>
        SeatReleased,

        /// <summary>The search timeout fired.</summary>
        SearchTimedOut,

        /// <summary>The reservation timeout fired.</summary>
        ReservationTimedOut,

        /// <summary>The player's session ended.</summary>
        SessionEnded,
    }

    /// <summary>What the player actor does with its matchmaking timeout after a step.</summary>
    public enum MatchmakingSearchTimeout
    {
        /// <summary>Leave the scheduled timeout as it is.</summary>
        Unchanged,

        /// <summary>Schedule the search timeout and set a new seating deadline.</summary>
        ArmSearch,

        /// <summary>Schedule the reservation timeout.</summary>
        ArmReservation,

        /// <summary>Make the scheduled timeout do nothing.</summary>
        Disarm,
    }

    /// <summary>The message the player actor sends to the matchmaker after a step.</summary>
    public enum MatchmakingQueueMessage
    {
        None,
        EnterQueue,
        LeaveQueue,
    }

    /// <summary>What the player actor does with a seat assignment.</summary>
    public enum MatchmakingTableAnswer
    {
        /// <summary>The step is not a seat assignment.</summary>
        None,

        /// <summary>Point the player at the table and report the seating.</summary>
        Take,

        /// <summary>The player is already at another table. Remember the new one as declined.</summary>
        Decline,
    }

    /// <summary>
    /// The facts outside the search phase that a step reads. Each event reads only some of them, named on
    /// <see cref="MatchmakingSearchPolicy.Step"/>.
    /// </summary>
    public readonly struct MatchmakingSearchFacts
    {
        /// <summary>Whether the player's match pointer is set.</summary>
        public readonly bool IsAtTable;

        /// <summary>Whether the player has a session.</summary>
        public readonly bool HasSession;

        /// <summary>
        /// Whether a client is connected. A session alone is not enough, because the SDK keeps a session alive for
        /// a linger period after its connection closes, and that period is longer than the fill wait.
        /// </summary>
        public readonly bool IsClientConnected;

        /// <summary>For <see cref="MatchmakingSearchEvent.SeatReleased"/>: whether the queue still holds the player's entry.</summary>
        public readonly bool IsStillQueued;

        public MatchmakingSearchFacts(bool isAtTable, bool hasSession, bool isClientConnected, bool isStillQueued)
        {
            IsAtTable         = isAtTable;
            HasSession        = hasSession;
            IsClientConnected = isClientConnected;
            IsStillQueued     = isStillQueued;
        }

        public bool IsPlayerHere => HasSession && IsClientConnected;
    }

    /// <summary>
    /// One step of the player's search: the phase afterwards and what the player actor does. The actor applies the
    /// fields in declaration order.
    /// </summary>
    public readonly struct MatchmakingSearchStep
    {
        public readonly MatchmakingSearchPhase Phase;

        /// <summary>Whether this step starts a search, so the actor records when the search started.</summary>
        public readonly bool StartsSearch;

        public readonly MatchmakingSearchTimeout Timeout;

        /// <summary>Whether this step ends the search without a seat, so the actor reports the wait.</summary>
        public readonly bool EndsUnseated;

        public readonly MatchmakingQueueMessage ToQueue;

        /// <summary>The status to send to the client, or null to send nothing.</summary>
        public readonly MatchmakingStatus? Reply;

        /// <summary>For <see cref="MatchmakingSearchEvent.SeatOffered"/>: the answer and its reason. Null for other events.</summary>
        public readonly SeatReservationAnswer? SeatAnswer;

        public readonly MatchmakingTableAnswer TableAnswer;

        public MatchmakingSearchStep(
            MatchmakingSearchPhase phase,
            bool startsSearch = false,
            MatchmakingSearchTimeout timeout = MatchmakingSearchTimeout.Unchanged,
            bool endsUnseated = false,
            MatchmakingQueueMessage toQueue = MatchmakingQueueMessage.None,
            MatchmakingStatus? reply = null,
            SeatReservationAnswer? seatAnswer = null,
            MatchmakingTableAnswer tableAnswer = MatchmakingTableAnswer.None)
        {
            Phase        = phase;
            StartsSearch = startsSearch;
            Timeout      = timeout;
            EndsUnseated = endsUnseated;
            ToQueue      = toQueue;
            Reply        = reply;
            SeatAnswer   = seatAnswer;
            TableAnswer  = tableAnswer;
        }

        public override string ToString() => $"{Phase} (timeout {Timeout}, queue {ToQueue}, reply {Reply?.ToString() ?? "none"})";
    }

    /// <summary>
    /// The player side of matchmaking as a pure <c>(phase, event) → step</c> function (<c>docs/matchmaking.md</c>,
    /// "Cancel" and "Timeouts").
    /// <para>
    /// The player's actor alone decides between a cancel and a seat, and the seat reservation is the commit point.
    /// Its messages are handled one at a time, so whichever of the cancel and the reservation arrives first is the
    /// outcome, and the player ends up either cancelled or seated, never both and never neither.
    /// </para>
    /// </summary>
    public static class MatchmakingSearchPolicy
    {
        /// <summary>
        /// The step <paramref name="ev"/> produces in <paramref name="phase"/>. <see cref="MatchmakingSearchFacts.IsAtTable"/>
        /// is read by Play, the seat offer and the seat assignment; the session and connection by the seat offer and
        /// the seat release; <see cref="MatchmakingSearchFacts.IsStillQueued"/> by the seat release.
        /// </summary>
        public static MatchmakingSearchStep Step(MatchmakingSearchPhase phase, MatchmakingSearchEvent ev, MatchmakingSearchFacts facts)
        {
            switch (ev)
            {
                case MatchmakingSearchEvent.PlayPressed:
                    if (facts.IsAtTable)
                        return new MatchmakingSearchStep(phase, reply: MatchmakingStatus.Unavailable);

                    // A repeated request, not a second entry. Answer with the current status, in case the client
                    // lost the first answer.
                    if (phase != MatchmakingSearchPhase.Idle)
                        return new MatchmakingSearchStep(phase, reply: ToClientStatus(phase));

                    return new MatchmakingSearchStep(MatchmakingSearchPhase.Searching, startsSearch: true, timeout: MatchmakingSearchTimeout.ArmSearch, toQueue: MatchmakingQueueMessage.EnterQueue, reply: MatchmakingStatus.Searching);

                case MatchmakingSearchEvent.CancelPressed:
                    // The seat is committed, so the cancel is refused and the table follows.
                    if (phase == MatchmakingSearchPhase.Reserved)
                        return new MatchmakingSearchStep(phase, reply: MatchmakingStatus.SeatReserved);

                    // Answered even when not searching, or the client's dialog would never close.
                    if (phase == MatchmakingSearchPhase.Idle)
                        return new MatchmakingSearchStep(phase, reply: MatchmakingStatus.NotSearching);

                    return EndUnseated(MatchmakingQueueMessage.LeaveQueue, MatchmakingStatus.NotSearching);

                case MatchmakingSearchEvent.SeatOffered:
                {
                    SeatReservationAnswer answer = MatchmakingPolicy.AnswerSeatReservation(
                        isSearching:       phase == MatchmakingSearchPhase.Searching,
                        isAtTable:         facts.IsAtTable,
                        hasSession:        facts.HasSession,
                        isClientConnected: facts.IsClientConnected);

                    switch (answer)
                    {
                        case SeatReservationAnswer.Take:
                            return new MatchmakingSearchStep(MatchmakingSearchPhase.Reserved, timeout: MatchmakingSearchTimeout.ArmReservation, reply: MatchmakingStatus.SeatReserved, seatAnswer: answer);

                        case SeatReservationAnswer.NotSearching:
                            // A cancel already won the race and was answered, or another formation holds this
                            // player's seat. Nothing changes.
                            return new MatchmakingSearchStep(phase, seatAnswer: answer);

                        default:
                            // The player is no longer a candidate. The formation already took the queue entry, so
                            // there is nothing to leave.
                            return new MatchmakingSearchStep(MatchmakingSearchPhase.Idle, timeout: MatchmakingSearchTimeout.Disarm, endsUnseated: true, seatAnswer: answer);
                    }
                }

                case MatchmakingSearchEvent.SeatAssigned:
                    // The reservation timed out, the player queued again and was seated elsewhere, and this older
                    // assignment arrived late. Accepting it would lose the table the player is at.
                    if (facts.IsAtTable)
                        return new MatchmakingSearchStep(phase, tableAnswer: MatchmakingTableAnswer.Decline);

                    return new MatchmakingSearchStep(MatchmakingSearchPhase.Idle, timeout: MatchmakingSearchTimeout.Disarm, tableAnswer: MatchmakingTableAnswer.Take);

                case MatchmakingSearchEvent.SeatReleased:
                    if (phase != MatchmakingSearchPhase.Reserved)
                        return new MatchmakingSearchStep(phase);

                    // Returning to searching would keep an absent player in the queue, and nothing would remove the
                    // entry later, because the session end found the player reserved.
                    if (!facts.IsPlayerHere)
                        return EndUnseated(MatchmakingQueueMessage.LeaveQueue, reply: null);

                    if (!facts.IsStillQueued)
                        return EndUnseated(MatchmakingQueueMessage.None, MatchmakingStatus.Unavailable);

                    return new MatchmakingSearchStep(MatchmakingSearchPhase.Searching, timeout: MatchmakingSearchTimeout.ArmSearch, reply: MatchmakingStatus.Searching);

                case MatchmakingSearchEvent.SearchTimedOut:
                    if (phase != MatchmakingSearchPhase.Searching)
                        return new MatchmakingSearchStep(phase);

                    // If the matchmaker is still running (a lost message, not a restart), this removes the stale
                    // entry instead of leaving it to seat a player who is back on the menu.
                    return EndUnseated(MatchmakingQueueMessage.LeaveQueue, MatchmakingStatus.Unavailable);

                case MatchmakingSearchEvent.ReservationTimedOut:
                    if (phase != MatchmakingSearchPhase.Reserved)
                        return new MatchmakingSearchStep(phase);

                    return EndUnseated(MatchmakingQueueMessage.None, MatchmakingStatus.Unavailable);

                case MatchmakingSearchEvent.SessionEnded:
                    // A reserved player is left as is: the table is being created with that seat, and the match
                    // pointer re-attaches the player when they return.
                    if (phase != MatchmakingSearchPhase.Searching)
                        return new MatchmakingSearchStep(phase);

                    return EndUnseated(MatchmakingQueueMessage.LeaveQueue, reply: null);

                default:
                    return new MatchmakingSearchStep(phase);
            }
        }

        /// <summary>The status the client is told for a phase.</summary>
        static MatchmakingStatus ToClientStatus(MatchmakingSearchPhase phase) => phase switch
        {
            MatchmakingSearchPhase.Searching => MatchmakingStatus.Searching,
            MatchmakingSearchPhase.Reserved  => MatchmakingStatus.SeatReserved,
            _                                => MatchmakingStatus.NotSearching,
        };

        static MatchmakingSearchStep EndUnseated(MatchmakingQueueMessage toQueue, MatchmakingStatus? reply)
            => new MatchmakingSearchStep(MatchmakingSearchPhase.Idle, timeout: MatchmakingSearchTimeout.Disarm, endsUnseated: true, toQueue: toQueue, reply: reply);
    }
}
