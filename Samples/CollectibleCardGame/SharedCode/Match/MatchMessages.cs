using Metaplay.Core;
using Metaplay.Core.Message;
using Metaplay.Core.Model;

namespace Game.Logic
{
    /// <summary>
    /// Why the table refused an intent, in the vocabulary the board acts on. The rules' own
    /// <see cref="MatchIntentResult"/> is a richer registry meant for logs; a client is sent one of these
    /// instead, so the wire shape does not move every time a rules refusal is added.
    /// <para>
    /// <b>A refusal never names a card.</b> It carries the code and nothing else — a refusal that echoed the
    /// instance would be a channel for probing state.
    /// </para>
    /// </summary>
    [MetaSerializable]
    public enum MatchRefusalCode
    {
        /// <summary>
        /// The intent answered a peek the table has moved past, or arrived while a resolution was held. Put
        /// the card down and unlock the input, and say nothing: in practice this means something the server
        /// did first, which is often good news.
        /// </summary>
        Stale = 0,
        /// <summary> Not this seat's turn, or not the phase for it. Same handling as <see cref="Stale"/>. </summary>
        NotYourTurn = 1,
        /// <summary> Already done — a second mulligan submission. Dismiss the control. </summary>
        AlreadyDone = 2,
        /// <summary>
        /// The rules said no. The client's own shared legality check should have prevented this, so the board
        /// puts the card down <em>and logs</em>: it is a bug signal rather than an ordinary outcome.
        /// </summary>
        Illegal = 3,
        /// <summary>
        /// The intent named something the server does not have in that seat's hand. The client's copy is
        /// already being corrected on the timeline, so the board asks for nothing.
        /// </summary>
        OutOfDate = 4,
        /// <summary> The sender is not a seat of this table. An app-shell error, not a board one. </summary>
        NotASeat = 5,
        /// <summary> The control should not have been offered: a debug win asked for outside a development environment. </summary>
        NotAvailableYet = 6,
        /// <summary>
        /// The Heist pick named a card that is not on this winner's menu — locked at enqueue by either side,
        /// never played from the loser's own deck, or already taken by an earlier pick. The screen says so
        /// rather than absorbing it: <c>Docs/client.md</c>'s "a refusal is an explicit message, not a
        /// timeout" is the reason the table refuses instead of dropping.
        /// </summary>
        NotEligible = 7,
        /// <summary>
        /// A bot is covering this seat, so the table does not take this seat's intents. The intent has marked
        /// the seat to be handed back at the next turn boundary; the board says so.
        /// </summary>
        SeatCovered = 8,
    }

    /// <summary> Mapping the rules' refusals onto the ones a board acts on. </summary>
    public static class MatchRefusals
    {
        /// <summary>
        /// The client-visible code for one rules refusal. Every <see cref="MatchIntentResults"/> member is
        /// mapped explicitly and a result with no arm here throws rather than defaulting, so a rules refusal
        /// added to the rules cannot silently arrive at a board as "illegal".
        /// </summary>
        public static MatchRefusalCode FromEngine(MatchIntentResult result)
        {
            if (result == MatchIntentResults.StaleChoice
                || result == MatchIntentResults.ResolutionHeld)
                return MatchRefusalCode.Stale;

            if (result == MatchIntentResults.NotYourTurn
                || result == MatchIntentResults.WrongPhase
                || result == MatchIntentResults.NoChoicePending)
                return MatchRefusalCode.NotYourTurn;

            if (result == MatchIntentResults.AlreadyMulliganed)
                return MatchRefusalCode.AlreadyDone;

            if (result == MatchIntentResults.UnknownInstance
                || result == MatchIntentResults.NotInYourHand)
                return MatchRefusalCode.OutOfDate;

            if (result == MatchIntentResults.NotEnoughMana
                || result == MatchIntentResults.BoardFull
                || result == MatchIntentResults.NotYourCritter
                || result == MatchIntentResults.CritterAsleep
                || result == MatchIntentResults.CritterAlreadyAttacked
                || result == MatchIntentResults.CritterHasNoAttack
                || result == MatchIntentResults.TargetIsSneaky
                || result == MatchIntentResults.DenProtectedByGuard
                || result == MatchIntentResults.IllegalTarget
                || result == MatchIntentResults.InvalidChoice
                || result == MatchIntentResults.InvalidHostAction)
                return MatchRefusalCode.Illegal;

            throw new MatchEngineException($"Rules refusal '{result}' has no client-visible refusal code. Add an arm to {nameof(MatchRefusals)}.{nameof(FromEngine)}.");
        }

        /// <summary>
        /// Whether the board should log this refusal as a bug signal rather than absorb it. Only
        /// <see cref="MatchRefusalCode.Illegal"/> qualifies: the client had the same legality function and the
        /// same inputs and offered the move anyway.
        /// </summary>
        public static bool IsBugSignal(MatchRefusalCode code) => code == MatchRefusalCode.Illegal;
    }

    /// <summary>
    /// One rules intent, addressed to the table on the client's entity channel. The intent hierarchy
    /// rides inside unreshaped: the rules judge it against the state it arrives at and address cards by match
    /// instance identity.
    /// </summary>
    [MetaMessage(MessageCodes.MatchIntent, MessageDirection.ClientToServer), MessageRoutingRuleEntityChannel]
    public class MatchIntentMessage : MetaMessage
    {
        public MatchIntent Intent    { get; private set; }
        /// <summary> Echoed on the refusal, so the client unlocks the input that raised this one. </summary>
        public int         RequestId { get; private set; }

        MatchIntentMessage() { }

        public MatchIntentMessage(MatchIntent intent, int requestId)
        {
            Intent    = intent;
            RequestId = requestId;
        }
    }

    /// <summary> One seat intent — leave, Heist pick — addressed to the table. </summary>
    [MetaMessage(MessageCodes.MatchSeatIntent, MessageDirection.ClientToServer), MessageRoutingRuleEntityChannel]
    public class MatchSeatIntentMessage : MetaMessage
    {
        public MatchSeatIntent Intent    { get; private set; }
        public int             RequestId { get; private set; }

        MatchSeatIntentMessage() { }

        public MatchSeatIntentMessage(MatchSeatIntent intent, int requestId)
        {
            Intent    = intent;
            RequestId = requestId;
        }
    }

    /// <summary>
    /// An intent was refused, sent to its submitter alone. <c>Docs/client.md</c>: "A refusal is an
    /// explicit message, not a timeout." Without it a card hangs lifted and the hand stays locked for the rest
    /// of the match.
    /// </summary>
    [MetaMessage(MessageCodes.MatchIntentRefused, MessageDirection.ServerToClient)]
    public class MatchIntentRefused : MetaMessage
    {
        public int              RequestId { get; private set; }
        public MatchRefusalCode Reason    { get; private set; }

        MatchIntentRefused() { }

        public MatchIntentRefused(int requestId, MatchRefusalCode reason)
        {
            RequestId = requestId;
            Reason    = reason;
        }
    }
}
