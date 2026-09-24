using Game.Logic;
using Metaplay.Core;

namespace Game.Server.Match
{
    public sealed partial class MatchActor
    {
        /// <summary>
        /// Per seat, whether a resolution was held on it the last time we looked. A peek moves no card, so the
        /// change is detected here rather than inferred from the hand.
        /// </summary>
        readonly bool[] _hadPendingChoice = new bool[MatchSeats.Count];

        /// <summary>
        /// Put one host-built action on the timeline, <b>prepared first</b>: the SDK's own <c>ExecuteAction</c>
        /// only warns on a refused action. A refusal is logged as an error when the action is one the actor
        /// built from its own state, and returned quietly for the deadline paths where refusal is ordinary.
        /// </summary>
        internal MatchIntentResult ExecuteMatchAction(MatchHostAction action, bool refusalIsBug = true)
        {
            MatchIntentResult gate = action.ServerPrepare(Model);
            if (!gate.IsSuccess)
            {
                if (refusalIsBug)
                    _log.Error("Refused to stage {Action}: {Refusal}. A server-built action that cannot apply is a bug here.", action.GetType().Name, gate);
                return gate;
            }

            StageMatchAction(action);
            return MatchIntentResults.Success;
        }

        /// <summary>
        /// An intent from a seat, or from a policy playing that seat, as the action it becomes. The seat is the
        /// authenticated one.
        /// </summary>
        MatchIntentResult SubmitIntent(int seat, MatchIntent intent)
        {
            MatchIntentResult gate = intent.Prepare(Model, seat, out MatchAction action);
            if (!gate.IsSuccess)
                return gate;

            StageMatchAction(action);
            return MatchIntentResults.Success;
        }

        /// <summary>
        /// Answer a held peek on an absent seat's behalf with the deterministic default, through the same
        /// intent path the seat would have taken.
        /// </summary>
        MatchIntentResult SubmitDefaultChoice()
        {
            PendingEffectChoice pending = Model.Rules.PendingChoice;
            if (pending == null)
                return MatchIntentResults.NoChoicePending;

            return SubmitIntent(pending.Seat, EffectChoiceIntent.Default(Model, pending.Seat));
        }

        /// <summary> The bookkeeping every host call owes: after an intent, a lapsed deadline and a play-out alike. </summary>
        void AfterActions()
        {
            // The result goes on the model the moment the game decides, because a non-null result is what
            // bars a seat reclaim.
            if (Model.Rules.Phase == MatchPhase.Complete && Model.Result == null)
                RecordResult();

            // The phase follows the result, and where the stakes owe a pick, the Heist runs first.
            if (Model.Result != null && Model.Phase == MatchTablePhase.Playing)
            {
                if (HeistPickIsOwed())
                    EnterHeistPick();
                else
                    EnterTerminalPhase();
            }

            HandBackSeatsAtTurnBoundary();
            MaybeDriveBotSeat();
            RearmFromModel();
        }

        /// <summary>
        /// Stage one match action, then address whatever it queued for a seat's own eyes, so each addressed
        /// operation lands directly after the operation that caused it.
        /// </summary>
        void StageMatchAction(MatchAction action)
        {
            ExecuteAction(action);
            DeliverOwnHandChanges();
        }

        /// <summary>
        /// Stage everything the rules queued in <see cref="MatchModel.Outbox"/> as <b>addressed</b> timeline
        /// operations: the real action to the seat entitled to it, a no-op to everyone else. A seat with no
        /// session has its operations dropped and gets a fresh hand from <c>GetMemberPrivateState</c> when it
        /// next subscribes.
        /// </summary>
        void DeliverOwnHandChanges()
        {
            foreach ((int seat, MatchAddressedAction action) in Model.Outbox)
            {
                EntityId playerId = Model.Seats[seat].PlayerId;
                if (playerId.IsValid)
                    ExecuteActionPerMember(playerId, action);
            }

            Model.Outbox.Clear();

            for (int seat = 0; seat < MatchSeats.Count; seat++)
                DeliverPeekReveal(seat, Model.Seats[seat].PlayerId);
        }

        /// <summary>
        /// A held peek shows its seat cards nobody else may see. Only the identities travel here, and only when
        /// the seat gains or loses the choice.
        /// </summary>
        void DeliverPeekReveal(int seat, EntityId playerId)
        {
            PendingEffectChoice pending   = Model.Rules.PendingChoice;
            bool                hasChoice = pending != null && pending.Seat == seat;

            if (hasChoice == _hadPendingChoice[seat])
                return;

            _hadPendingChoice[seat] = hasChoice;

            if (!playerId.IsValid)
                return;

            PendingChoiceView revealed = hasChoice ? HandViews.BuildPendingChoice(Model, seat) : null;
            ExecuteActionPerMember(playerId, new MatchOwnPeekRevealed(revealed));
        }
    }
}
