namespace Game.Server.Matchmaking
{
    /// <summary> What one seat's reservation ask came back with. </summary>
    public enum MatchmakingSeatAnswer
    {
        /// <summary> The actor answered and committed. </summary>
        Accepted,
        /// <summary> The actor answered and refused: cancelled, already in a match, or nobody at the other end. </summary>
        Declined,
        /// <summary> The ask never came back. Whether the actor committed is unknown and unknowable. </summary>
        TimedOut,
    }

    /// <summary> What the matchmaker owes one seat once formation has resolved. </summary>
    public enum FormationSeatAction
    {
        /// <summary> The table exists and this seat is at it. </summary>
        Seated,
        /// <summary> Release the reservation and put the ticket back, <b>with its original arrival stamp</b>. </summary>
        Requeue,
        /// <summary> Tell it the seat is gone. Not re-queued. </summary>
        TellSeatGone,
        /// <summary> Nothing. It declined, so it already knows and is not waiting on an answer. </summary>
        Nothing,
    }

    /// <summary> Both seats' actions, and whether a table was actually formed. </summary>
    public readonly struct FormationOutcome
    {
        public readonly FormationSeatAction SeatA;
        public readonly FormationSeatAction SeatB;

        /// <summary> Whether the formation committed. Only then is a table minted and pointed at. </summary>
        public readonly bool Formed;

        public FormationOutcome(FormationSeatAction seatA, FormationSeatAction seatB, bool formed)
        {
            SeatA  = seatA;
            SeatB  = seatB;
            Formed = formed;
        }
    }

    /// <summary>
    /// The dissolved-pairing bookkeeping, as a function. With two seats a decline does not merely thin the
    /// roster — it <b>dissolves the pairing</b> — so the surviving committer has to be handled explicitly
    /// rather than incidentally (<c>Docs/matchmaking.md</c>, "Formation is atomic, and validated").
    /// <para>
    /// The whole rule is that the discriminator is a seat's <em>own</em> answer, not the other seat's:
    /// </para>
    /// <list type="bullet">
    /// <item>A seat that <b>answered</b> is demonstrably responsive right now, so it is re-queued with the
    /// stamp it arrived with. It keeps the band it had earned and loses a fraction of a second.</item>
    /// <item>A seat whose <b>answer was lost</b> may or may not have committed, and an actor that could not
    /// answer once would be re-taken by the next formation and stall that one too. It is told the seat is
    /// gone and pays one tap; everyone else pays nothing.</item>
    /// <item>A seat that <b>declined</b> is owed nothing: it made the decision and its own phase machine has
    /// already moved it out of the search.</item>
    /// </list>
    /// <para>
    /// A mint that fails is the same shape as a decline arriving late — both seats committed, so both are
    /// re-queued with the stamps they arrived with rather than being left staring at a searching screen.
    /// </para>
    /// </summary>
    public static class FormationOutcomePolicy
    {
        /// <summary>
        /// What the two seats are owed. <paramref name="mintOk"/> is only consulted when both seats accepted,
        /// because a mint is only attempted then — an unformed pairing has nothing to mint for.
        /// </summary>
        public static FormationOutcome Decide(MatchmakingSeatAnswer a, MatchmakingSeatAnswer b, bool mintOk)
        {
            bool bothAccepted = a == MatchmakingSeatAnswer.Accepted && b == MatchmakingSeatAnswer.Accepted;
            bool formed       = bothAccepted && mintOk;

            return new FormationOutcome(ForSeat(a, formed), ForSeat(b, formed), formed);
        }

        /// <summary>
        /// The single-seat form, which is also what a bot-fallback formation uses: there is one human, so
        /// there is one answer and no pairing to dissolve.
        /// </summary>
        public static FormationSeatAction ForSeat(MatchmakingSeatAnswer own, bool formed)
        {
            if (formed)
                return FormationSeatAction.Seated;

            switch (own)
            {
                case MatchmakingSeatAnswer.Accepted: return FormationSeatAction.Requeue;
                case MatchmakingSeatAnswer.TimedOut: return FormationSeatAction.TellSeatGone;
                default:                             return FormationSeatAction.Nothing;
            }
        }
    }
}
