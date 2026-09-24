using Game.Logic;
using Game.Server.Match;
using Game.Server.Matchmaking;
using Metaplay.Cloud.Entity;
using Metaplay.Cloud.RuntimeOptions;
using Metaplay.Core;
using Metaplay.Core.Client;
using Metaplay.Server.MultiplayerEntity.InternalMessages;
using System;
using System.Threading.Tasks;

namespace Game.Server.Player
{
    /// <summary>
    /// The account's end of the ranked queue. The search lives on the actor rather than the model: it is a
    /// fact about an in-flight request, and must not outlive the actor.
    /// </summary>
    public sealed partial class PlayerActor
    {
        /// <summary> Where this account stands in a search. Actor-local, never persisted, never replicated. </summary>
        MatchmakingSearchPhase _searchPhase = MatchmakingSearchPhase.None;

        /// <summary>
        /// Which arming of a bound is current. Bumped on every phase change, so a bound armed for a phase the
        /// actor has since left no-ops. Nothing is ever cancelled.
        /// </summary>
        int _searchEpoch;

        MatchmakingOptions MatchmakingOptions => RuntimeOptionsRegistry.Instance.GetCurrent<MatchmakingOptions>();

        // ---------------------------------------------------------------- the two actions

        void IPlayerModelServerListener.EnqueueForRankedMatch(DeckChoice deck)
            => EnqueueOnActorContext(() => EnterRankedQueue(deck));

        void IPlayerModelServerListener.CancelMatchmaking()
            => EnqueueOnActorContext(LeaveRankedQueue);

        /// <summary>
        /// Freeze this account's seat and hand it to the queue. The seat is
        /// <see cref="MatchmakingTicketPolicy.FreezeSeat"/>, the same freeze practice takes: the deck the queue
        /// matched on, at its ranks and behind its padlocks, is the deck the match is dealt from.
        /// </summary>
        void EnterRankedQueue(DeckChoice deck)
        {
            // One entry per player: an account already queued or at a table is refused. A double-tapped Play
            // and a second browser tab both reach this path. The pointer is held through the end of the Heist.
            if (Model.IsInMatch)
            {
                _log.Info("Not queueing: the account is already at table {MatchId}", Model.CurrentMatch);
                SendToClient(new MatchmakingEnded(MatchmakingEndReason.Refused));
                return;
            }

            MatchmakingSearchStep step = MatchmakingSearchPhasePolicy.Step(
                _searchPhase, MatchmakingSearchEvent.Enqueued, Model.IsClientConnected, Model.IsInMatch);

            if (step.Outbound != MatchmakingSearchOutbound.StatusUpdate)
            {
                _log.Info("Not queueing: this account is already searching ({Phase})", _searchPhase);
                SendEndIfAny(step);
                return;
            }

            // Looked up again: a deck can be deleted, or dropped from the config, after the action ran.
            DeckChoiceResult resolved = DeckChoiceResolver.Resolve(deck, Model);
            if (!resolved.IsValid)
            {
                _log.Warning("Not queueing: deck {Deck} no longer exists ({Error})", deck, resolved.Error);
                SendToClient(new MatchmakingEnded(MatchmakingEndReason.Refused));
                return;
            }

            (MatchSeatSetup seat, int powerScore) = MatchmakingTicketPolicy.FreezeSeat(Model, _entityId, resolved.Cards, SharedGameConfig);
            MatchmakingTicket ticket = MatchmakingTicketPolicy.Freeze(Model, deck, seat, powerScore, MetaTime.Now);

            EnterPhase(step.Phase);
            CastMessage(MatchmakerActor.EntityId, new InternalMatchmakingEnqueue(ticket));

            // The one status push. The fill wait never changes for the ticket's life, so it is sent once.
            SendToClient(new MatchmakingStatusUpdate(ticket.ArrivedAt + MetaDuration.FromTimeSpan(MatchmakingOptions.FillWait) - MetaTime.Now));

            ArmSearchBound(MatchmakingOptions.SearchingBound);

            _log.Info("Queued for a ranked match with deck {Deck} at rating {Rating}, Power Score {PowerScore}",
                deck, ticket.Rating, ticket.PowerScore);
        }

        /// <summary>
        /// The player cancelled. The matchmaker is told by a cast: the removal is idempotent, and the real
        /// defence is this actor's own phase.
        /// </summary>
        void LeaveRankedQueue()
        {
            MatchmakingSearchStep step = MatchmakingSearchPhasePolicy.Step(
                _searchPhase, MatchmakingSearchEvent.Cancelled, Model.IsClientConnected, Model.IsInMatch);

            if (step.EndReason != MatchmakingEndReason.Cancelled)
            {
                // The seat is already committed and the association is inbound, so the client keeps waiting.
                _log.Debug("A cancel arrived after the seat was committed; refusing it");
                return;
            }

            if (_searchPhase == MatchmakingSearchPhase.None)
            {
                // The client believes it is searching when this actor does not. Answered anyway, or its dialog
                // would never close.
                _log.Info("A cancel arrived with no search to cancel; answering it so the dialog can close");
            }

            EnterPhase(step.Phase);
            CastMessage(MatchmakerActor.EntityId, new InternalMatchmakingCancel(_entityId));
            SendEndIfAny(step);
        }

        /// <summary>
        /// The session ended while the player was searching, so the ticket comes out of the queue
        /// (<c>Docs/matchmaking.md</c>, "Leaving the queue"). A reload or a second tab pays one tap. Only
        /// from <see cref="MatchmakingSearchPhase.Searching"/>: a committed seat has no ticket left to remove.
        /// </summary>
        protected override void OnOwnerSessionEnded(EntitySubscriber subscriber, bool wasKicked)
        {
            base.OnOwnerSessionEnded(subscriber, wasKicked);

            if (_searchPhase != MatchmakingSearchPhase.Searching)
                return;

            _log.Info("The session ended while this account was searching; taking its ticket out of the queue");

            EnterPhase(MatchmakingSearchPhase.None);
            CastMessage(MatchmakerActor.EntityId, new InternalMatchmakingCancel(_entityId));
        }

        // ---------------------------------------------------------------- the seat reservation

        /// <summary>
        /// <b>The commit point.</b> Whichever of the cancel and this ask reaches the mailbox first is the
        /// outcome; the single-threaded actor needs no lock (<c>Docs/matchmaking.md</c>, "A cancel and a
        /// formation cannot both win").
        /// </summary>
        [EntityAskHandler]
        InternalPlayerSeatInMatchResponse HandleInternalPlayerSeatInMatchRequest(InternalPlayerSeatInMatchRequest _)
        {
            // Liveness is a live connection, not a live session: a session outlives its connection by about a
            // minute, which is as long as a search.
            bool connected = Model.IsClientConnected;
            bool inMatch   = Model.IsInMatch;

            MatchmakingSearchStep step = MatchmakingSearchPhasePolicy.Step(
                _searchPhase, MatchmakingSearchEvent.SeatAsked, connected, inMatch);

            MatchmakingSearchPhase before = _searchPhase;
            EnterPhase(step.Phase);

            if (step.Outbound == MatchmakingSearchOutbound.AcceptSeat)
            {
                // The player can no longer cancel, so the seated bound starts.
                ArmSearchBound(MatchmakingOptions.SeatedBound);
                return new InternalPlayerSeatInMatchResponse(accepted: true, reason: null);
            }

            // A decline ends the search: the matchmaker does not re-queue a declined seat.
            SendEndIfAny(step);

            string reason = inMatch ? "this account is already at a table"
                          : !connected ? "nobody is on the other end of this account's connection"
                          : before == MatchmakingSearchPhase.None ? "this account is not searching"
                          : "this account has already committed to a seat";

            return new InternalPlayerSeatInMatchResponse(accepted: false, reason);
        }

        // ---------------------------------------------------------------- what the matchmaker sends back

        /// <summary>
        /// The pairing dissolved and this seat answered, so its ticket is back in the queue under the stamp it
        /// arrived with. Nothing is sent to the client: the dialog never stopped being true.
        /// </summary>
        [MessageHandler]
        void HandleInternalMatchmakingReservationReleased(InternalMatchmakingReservationReleased _)
        {
            MatchmakingSearchStep step = MatchmakingSearchPhasePolicy.Step(
                _searchPhase, MatchmakingSearchEvent.ReservationReleased, Model.IsClientConnected, Model.IsInMatch);

            EnterPhase(step.Phase);
            ArmSearchBound(MatchmakingOptions.SearchingBound);
            SendEndIfAny(step);
        }

        /// <summary> The pairing dissolved and this seat's answer was lost. It pays one tap. </summary>
        [MessageHandler]
        void HandleInternalMatchmakingSeatGone(InternalMatchmakingSeatGone _)
        {
            MatchmakingSearchStep step = MatchmakingSearchPhasePolicy.Step(
                _searchPhase, MatchmakingSearchEvent.SeatGone, Model.IsClientConnected, Model.IsInMatch);

            EnterPhase(step.Phase);
            SendEndIfAny(step);
        }

        /// <summary>
        /// A table exists and this account is at it. The deck is recorded here rather than in the entry action:
        /// the entry refusal depends on state the client cannot see, so a deck recorded in the predicted run
        /// could be one the server never recorded, which is a checksum mismatch.
        /// </summary>
        [MessageHandler]
        async Task HandleInternalMatchmakingFormed(InternalMatchmakingFormed message)
        {
            MatchmakingSearchStep step = MatchmakingSearchPhasePolicy.Step(
                _searchPhase, MatchmakingSearchEvent.Formed, Model.IsClientConnected, Model.IsInMatch);

            EnterPhase(step.Phase);
            SendEndIfAny(step);

            if (Model.IsInMatch && Model.CurrentMatch != message.MatchId)
            {
                // Seated elsewhere in between. The live pointer stays; the new table's join window collects it.
                _log.Warning("Formed at {MatchId} while already at {Existing}; keeping the pointer that is live",
                    message.MatchId, Model.CurrentMatch);
                return;
            }

            Model.CurrentMatch = message.MatchId;
            AddEntityAssociation(new AssociatedEntityRefBase.Default(ClientSlotGame.Match, _entityId, message.MatchId), removeOnSessionEnd: true);
            EnqueueServerAction(new PlayerNoteDeckPlayed(message.DeckChoice));

            await PersistStateIntermediate();

            _log.Info("Seated at ranked table {MatchId} in seat {Seat} with deck {Deck}", message.MatchId, message.Seat, message.DeckChoice);
        }

        // ---------------------------------------------------------------- the bounds, and the plumbing

        /// <summary>
        /// Enter a phase and invalidate whatever bound was armed for the last one. Every transition goes
        /// through here, which is what makes an armed bound a fact about one phase rather than about the actor.
        /// </summary>
        void EnterPhase(MatchmakingSearchPhase phase)
        {
            _searchPhase = phase;
            _searchEpoch++;
        }

        /// <summary>
        /// Bound the phase the actor is now in. Both states a player can wait in are bounded here, on the
        /// player's own actor, because both wait on something held in another entity's memory: a queue entry
        /// the matchmaker could lose to a restart, and a table being minted. Both land the player back on the
        /// menu with a way to try again.
        /// </summary>
        void ArmSearchBound(TimeSpan bound)
        {
            int epoch = _searchEpoch;

            ScheduleExecuteOnActorContext(DateTime.UtcNow + bound, () => OnSearchBoundExpired(epoch));
        }

        void OnSearchBoundExpired(int epoch)
        {
            if (epoch != _searchEpoch || _searchPhase == MatchmakingSearchPhase.None)
                return;

            MatchmakingSearchStep step = MatchmakingSearchPhasePolicy.Step(
                _searchPhase, MatchmakingSearchEvent.BoundExpired, Model.IsClientConnected, Model.IsInMatch);

            _log.Info("Giving up on the search after its own bound elapsed in {Phase}", _searchPhase);

            EnterPhase(step.Phase);

            // Best effort, and harmless if the queue never had it or has already dropped it: the removal is
            // idempotent, and the premise of this bound is that the matchmaker may not be there to hear it.
            CastMessage(MatchmakerActor.EntityId, new InternalMatchmakingCancel(_entityId));

            SendEndIfAny(step);
        }

        /// <summary>
        /// Tell this account's client its search ended, when the step says so. A server-only refusal on the
        /// meta screens is otherwise invisible: the model does not change and the SDK has no route for an
        /// action's result.
        /// </summary>
        void SendEndIfAny(MatchmakingSearchStep step)
        {
            if (step.EndReason is MatchmakingEndReason reason)
                SendToClient(new MatchmakingEnded(reason));
        }
    }
}
