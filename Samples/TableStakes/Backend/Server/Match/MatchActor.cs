using Game.Logic;
using Metaplay.Cloud.Entity;
using Metaplay.Cloud.RuntimeOptions;
using Metaplay.Cloud.Sharding;
using Metaplay.Core;
using Metaplay.Core.Client;
using Metaplay.Core.Model;
using Metaplay.Server.MultiplayerEntity;
using Metaplay.Server.MultiplayerEntity.InternalMessages;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Game.Server.Match
{
    [EntityConfig]
    public class MatchEntityConfig : PersistedEntityConfig
    {
        public override EntityKind        EntityKind           => EntityKindGame.Match;
        public override Type              EntityActorType      => typeof(MatchActor);
        public override NodeSetPlacement  NodeSetPlacement     => NodeSetPlacement.Logic;
        public override IShardingStrategy ShardingStrategy     => ShardingStrategies.CreateStaticSharded();
        public override TimeSpan          ShardShutdownTimeout => TimeSpan.FromSeconds(30);
    }

    /// <summary>
    /// The server host for a match table. It deals the table, runs the bots and the resolve pause, handles the
    /// clients' moves, and persists the table.
    /// <para>
    /// The turn flow is in <see cref="MatchHost"/>, shared with the browser's offline host, so this actor owns
    /// only timers, sessions, persistence and networking. The match does not tick, so every timer is scheduled
    /// from an absolute timestamp stored in the model, which a restored table can reschedule
    /// (<c>docs/match.md</c>, "Timers").
    /// </para>
    /// </summary>
    public sealed class MatchActor : PersistedMultiplayerEntityActorBase<MatchModel, MatchAction, PersistedMatch>
    {
        /// <summary>
        /// How long the table waits before running again after <see cref="PublishAction"/> refused an action.
        /// <see cref="MaxPublishRetries"/> is how many retries it makes before it stops running itself. Together
        /// they prevent a repeated refusal from causing a busy loop or retrying forever.
        /// </summary>
        static readonly MetaDuration PublishRetryDelay = MetaDuration.FromSeconds(1);

        const int MaxPublishRetries = 5;

        /// <summary>
        /// How long a finished table waits before sending an unacknowledged result again. A player's actor that
        /// did not answer is usually still waking up, so the delay is short.
        /// </summary>
        static readonly MetaDuration ResultRetryDelay = MetaDuration.FromSeconds(5);

        /// <summary>
        /// The minimum interval between clock sync requests that this actor answers for one session. Clock sync
        /// is the only message other than a move that a client can send at will, so it is throttled. The interval
        /// is shorter than the client's <see cref="ServerClockEstimate.ResampleInterval"/>, so a normal client is never
        /// throttled.
        /// </summary>
        static readonly MetaDuration ClockSyncMinInterval = MetaDuration.FromSeconds(10);

        /// <summary>
        /// The actor must stay alive after its last subscriber leaves for long enough to run that seat's
        /// disconnect grace and play the game out. The SDK's default linger is shorter than
        /// <c>Match:DisconnectGrace</c>, so with the default, a table whose last human disconnected would shut down
        /// before the game finished (<c>docs/match.md</c>, "Model, actor and client").
        /// </summary>
        protected override AutoShutdownPolicy ShutdownPolicy =>
            AutoShutdownPolicy.ShutdownAfterSubscribersGone(lingerDuration: Options.ActorLingerAfterLastSubscriber);

        /// <summary>A table is created by sending a setup message to a new entity id. No row is written first.</summary>
        protected override bool AllowEntityCreationOnMessage => true;

        /// <summary>
        /// No logic depends on elapsed model time, so the model does not tick. The model still declares a tick
        /// rate of one rather than zero, because the SDK divides by the tick rate when it computes model time.
        /// </summary>
        protected override bool IsTicking => false;

        static MatchOptions Options => RuntimeOptionsRegistry.Instance.GetCurrent<MatchOptions>();

        /// <summary>
        /// In a local environment, checksum every operation, so that a desync report names the action that
        /// caused it instead of only the batch that contained it. Per-operation checksums cost CPU and memory, so
        /// they are enabled only locally (a local server and the E2E harness), where few tables run. Cloud
        /// environments use the SDK default.
        /// </summary>
        protected override EntityDebugOptions DebugOptions =>
            RuntimeOptionsBase.IsLocalEnvironment
                ? new EntityDebugOptions(EntityChecksumMode.PerOperation(), EntityConsistencyChecks.None, checkInitialModelChecksum: true)
                : base.DebugOptions;

        /// <summary>
        /// Random source for the host's own choices: bot strengths and bot moves. It is separate from the deal
        /// seed, which comes from a cryptographic generator and is used for nothing else.
        /// </summary>
        readonly RandomPCG _hostRng = RandomPCG.CreateNew();

        /// <summary>
        /// The bot move waiting for its think delay to pass. It is kept on the actor, not in the model, because it
        /// is a host decision. A table restored from the database makes a new decision. The browser host keeps
        /// the same <see cref="MatchPendingBotMove"/> type.
        /// </summary>
        readonly MatchPendingBotMove _pendingBotMove = new MatchPendingBotMove();

        /// <summary>
        /// True if this actor dealt the table, false if it restored the table from the database. See
        /// <see cref="OnEntityInitialized"/> for how a restored table is handled.
        /// </summary>
        bool _dealtByThisActor;

        /// <summary>
        /// True while a round of result deliveries is in progress. The asks are awaited off the actor context, so
        /// without this flag a second round could start and ask every undelivered seat again. That would be
        /// harmless, because the player's actor records each match only once, but wasteful.
        /// </summary>
        bool _deliveringResults;

        /// <summary>
        /// When to send undelivered results again, or <see cref="MetaTime.Epoch"/> when no result is pending.
        /// <see cref="GetNextAttentionAt"/> includes it when scheduling the next wake.
        /// </summary>
        MetaTime _resultRetryAt = MetaTime.Epoch;

        /// <summary>The table's one wake, armed by <see cref="ArmNextWake"/> and run by <see cref="OnWake"/>.</summary>
        readonly DeadlineWakeup _wake = new DeadlineWakeup();

        // Retry state after a refused publish: when the table runs again, and how many retries it has made. Not
        // persisted, because a refused publish does not change the model, and a restored table runs from its
        // model.
        MetaTime _publishRetryAt = MetaTime.Epoch;
        int      _publishRetries;

        /// <summary>
        /// True if this actor stopped running the table after <see cref="MaxPublishRetries"/> refused publishes.
        /// While set, no wakes are scheduled, so only a client message runs the table. A successful publish clears
        /// it.
        /// </summary>
        bool _publishRetriesExhausted;

        // Whether any publish succeeded, and whether any was refused, since the last call to
        // RecoverFromRefusedPublish. Both can be true after one pass. A client's move publishes before the pass
        // that follows it, so the move's outcome is included.
        bool _anyPublishSucceeded;
        bool _anyPublishRefused;

        #region Lifecycle

        /// <summary>
        /// Deals the table described by the setup message. The deal seed is generated here with a cryptographic
        /// generator instead of being passed in the setup message, because anyone who knows the seed can
        /// reconstruct every hand (<c>docs/match.md</c>, "The deal seed").
        /// </summary>
        protected override Task SetUpModelAsync(MatchModel model, IMultiplayerEntitySetupParams setupParams)
        {
            MatchSetupParams setup = (MatchSetupParams)setupParams;

            List<MatchSeat> seats = new List<MatchSeat>(MatchRules.NumSeats);
            for (int seat = 0; seat < MatchRules.NumSeats; seat++)
                seats.Add(setup.Seats[seat].ToSeat(seat));

            // Bot strengths come from the model's game config. The SDK assigns the game config to the model
            // before calling SetUpModelAsync, so every seat's strength comes from the same config version.
            List<BotProfile> botProfiles = BotConfig.DrawableProfiles(model.GameConfig as SharedGameConfig);

            MatchHost.SetupNewMatch(model, MatchHost.CreateDealSeed(), _hostRng.NextULong(), seats, Options.ToMatchTimings(), botProfiles, MetaTime.Now);
            _dealtByThisActor = true;
            return Task.CompletedTask;
        }

        /// <summary>
        /// Called when the table is live, either newly dealt or restored from the database. In both cases the
        /// timers are scheduled from the absolute timestamps in the model, so a table persisted during a pause
        /// resumes at the right time.
        /// </summary>
        protected override Task OnEntityInitialized()
        {
            // A restored table's connected flags are from before the actor stopped, and no client is subscribed
            // yet. Put every human seat on the longer restart grace before the abandonment check runs. Otherwise
            // the table would look deserted on wake and be abandoned (docs/match.md, "Restart and cold wake").
            if (!_dealtByThisActor)
            {
                _log.Info("Restored table {MatchId} from the database. Holding its seats on the restart grace.", _entityId);
                MatchHost.NoteColdWake(Model, MetaTime.Now, HostEnvironment);
            }

            RunTable();
            return Task.CompletedTask;
        }

        #endregion

        #region Subscribers

        /// <summary>
        /// Only seated players may subscribe. A player whose state points at a table where they have no seat is
        /// refused. The refusal also tells the player's actor to clear the pointer, so later logins do not fail
        /// the same way (<c>docs/match.md</c>, "Lock-out cases").
        /// </summary>
        protected override Task OnClientSessionHandshake(EntityId sessionId, EntityId playerId, InternalEntitySubscribeRequestBase requestBase)
        {
            // A null model means no database row exists for this id. Refuse the same way as for a player with no
            // seat, so the player's actor clears its pointer.
            if (Model == null || Model.FindSeatOfPlayer(playerId) < 0)
            {
                _log.Warning("Player {PlayerId} tried to subscribe to a table they do not sit at. Refusing.", playerId);
                throw new InternalEntitySubscribeRefusedBase.Builtins.NotAParticipant();
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Called when a client subscribes. The seat's grace period stops, its strike count resets, and a covering
        /// bot returns the seat. The join window closes once every human seat has subscribed, so that a slow
        /// client does not miss the first cards.
        /// <para>
        /// <b>This must happen before the base call.</b> The base call serializes the subscriber's initial state,
        /// and the SDK adds the subscriber to the timeline only after this method returns, so an action published
        /// in between reaches the client neither way and fails its first checksum.
        /// </para>
        /// </summary>
        protected override Task<InternalEntitySubscribeResponseBase> OnClientSessionStart(EntityId sessionId, EntityId playerId, InternalEntitySubscribeRequestBase requestBase, List<AssociatedEntityRefBase> associatedEntities)
        {
            int seat = Model == null ? -1 : Model.FindSeatOfPlayer(playerId);
            if (seat >= 0)
            {
                MatchHost.NoteSeatPresent(Model, seat, MetaTime.Now, HostEnvironment);
                RunTable();
            }

            return base.OnClientSessionStart(sessionId, playerId, requestBase, associatedEntities);
        }

        /// <summary>
        /// Called when a client's session ends, for example because the tab closed or the connection dropped. The
        /// seat goes on its disconnect grace instead of being covered at once, because a tab switch or a brief
        /// network outage also ends the session.
        /// </summary>
        protected override void OnParticipantSessionEnded(EntitySubscriber session)
        {
            base.OnParticipantSessionEnded(session);

            ClientPeerState peer = TryGetClientPeer(session);
            if (peer == null || Model == null)
                return;

            int seat = Model.FindSeatOfPlayer(peer.PlayerId);
            if (seat < 0)
                return;

            MatchHost.NoteSeatAbsent(Model, seat, MetaTime.Now, skipGrace: false, HostEnvironment);
            RunTable();
        }

        /// <summary>
        /// Returns the subscribed session of the player at <paramref name="seat"/>, or null if that player is not
        /// subscribed. Used to send a hand update to one client.
        /// </summary>
        EntitySubscriber TryGetSessionAtSeat(int seat)
        {
            if (Model == null || seat < 0 || seat >= MatchRules.NumSeats)
                return null;

            EntityId playerId = Model.GetSeat(seat).PlayerId;
            if (!playerId.IsValid)
                return null;

            foreach ((int _, EntitySubscriber subscriber) in _subscribers)
            {
                ClientPeerState peer = TryGetClientPeer(subscriber);
                if (peer != null && peer.PlayerId == playerId)
                    return subscriber;
            }
            return null;
        }

        #endregion

        #region The timeline has one writer

        /// <summary>
        /// Refuses every client-originating action. The match action base class declares only
        /// <see cref="ModelActionExecuteFlags.LeaderSynchronized"/>, so the SDK already rejects client actions. This
        /// override is a second guard: the SDK's default for this hook allows the action, so if the base class
        /// ever gained the follower flag, clients could write to the timeline without any error
        /// (<c>docs/match.md</c>, "Only the host writes the timeline").
        /// </summary>
        protected override bool ValidateClientOriginatingAction(ClientPeerState client, MatchAction action)
        {
            _log.Warning("Refused a client-originating action {Action} from {PlayerId}: the actor is this timeline's only writer.",
                action.GetType().Name, client.PlayerId);
            return AllowsClientOriginatingActions;
        }

        /// <summary>
        /// The value <see cref="ValidateClientOriginatingAction"/> returns. It is a constant so that a test can
        /// check it without a live actor.
        /// </summary>
        public const bool AllowsClientOriginatingActions = false;

        /// <summary>
        /// Executes an action on the timeline and returns whether it succeeded.
        /// <para>
        /// <see cref="MatchHost"/> commits its server-only, unchecksummed engine state only after the action
        /// changed the board, so it must learn of a rejection. <c>ExecuteAction</c> only logs a warning, so this
        /// method dry-runs the action on the same model first. A rejection that the dry run misses is handled by
        /// the bounded retry in <see cref="RecoverFromRefusedPublish"/>.
        /// </para>
        /// </summary>
        bool PublishAction(MatchAction action)
        {
            if (!MatchHost.PassesDryRun(Model, action, _log))
            {
                _anyPublishRefused = true;
                return false;
            }

            ExecuteAction(action);
            _anyPublishSucceeded = true;
            return true;
        }

        #endregion

        #region Client messages

        /// <summary>
        /// Handles a move from a client. The seat in the message is checked against the player's actual seat
        /// before the move is applied. A move that is not played gets an explicit refusal, because otherwise the
        /// client's selected card would stay lifted and its hand would stay locked for the rest of the game.
        /// </summary>
        [PubSubMessageHandler]
        void HandlePlayCardRequest(EntitySubscriber session, MatchPlayCardRequest request)
        {
            ClientPeerState peer = TryGetClientPeer(session);
            if (peer == null || Model == null)
                return;

            MoveRefusalReason refusal = MatchHost.TrySubmitMove(
                Model, peer.PlayerId, request.Seat, request.PlayIndex, request.Card, MetaTime.Now, HostEnvironment);

            if (refusal != MoveRefusalReason.None)
                SendToClient(session, new MatchMoveRefused(request.PlayIndex, request.Card, refusal));

            // Run the table even after a refused move. A refused move does not change the model, so the run does
            // nothing. The exception is a refusal by PublishAction, which RunTable's retry handling must see.
            RunTable();
        }

        /// <summary>
        /// Handles the Leave control. The seat is treated as disconnected without a grace period, so a bot covers
        /// it immediately. The game is then played out and recorded as usual.
        /// <para>
        /// The <see cref="MatchReleaseNotification"/> cast clears the player's match pointer and takes the client
        /// off the table page immediately, without a results screen. It is sent <b>before</b> the table runs.
        /// At a table where every other seat is a bot, the run plays out the whole game and asks the player's
        /// actor to record the result, which clears the pointer. A release sent after that would arrive too late.
        /// </para>
        /// </summary>
        [PubSubMessageHandler]
        void HandleLeaveRequest(EntitySubscriber session, MatchLeaveRequest _)
        {
            ClientPeerState peer = TryGetClientPeer(session);
            if (peer == null || Model == null)
                return;

            int seat = Model.FindSeatOfPlayer(peer.PlayerId);
            if (seat < 0)
                return;

            _log.Info("Seat {Seat} left the table deliberately. Covering it without grace.", seat);

            // Release the player only if the cover action was published. If it was refused, the player still
            // holds the seat, and detaching them would leave them out of a game they are still in.
            if (MatchHost.NoteSeatAbsent(Model, seat, MetaTime.Now, skipGrace: true, HostEnvironment))
                CastMessage(peer.PlayerId, new MatchReleaseNotification(_entityId));

            RunTable();
        }

        /// <summary>
        /// Answers a client's clock sync request with this actor's current time. Client countdowns are measured
        /// against timestamps written by this actor, so the client must estimate the server clock instead of
        /// using its own device clock (<see cref="ServerClockEstimate"/>).
        /// </summary>
        [PubSubMessageHandler]
        void HandleClockSyncRequest(EntitySubscriber session, MatchClockSyncRequest request)
        {
            MatchPeerState peer = TryGetClientPeer(session) as MatchPeerState;
            if (peer == null)
                return;

            // Drop a request that arrives before the session's next allowed time, so a client cannot flood this
            // actor with clock sync requests. A normal client requests less often than ClockSyncMinInterval.
            MetaTime now = MetaTime.Now;
            if (!peer.ClockSyncThrottle.IsOpenAt(now))
                return;

            peer.ClockSyncThrottle = peer.ClockSyncThrottle.AfterRequestAt(now);
            SendToClient(session, new MatchClockSyncResponse(request.Nonce, now));
        }

        /// <summary>
        /// Per-session state for the clock sync throttle. The throttle is per session, not per table, so that
        /// seats do not use up each other's allowance.
        /// </summary>
        sealed class MatchPeerState : ClientPeerState
        {
            /// <summary>Limits this session's clock sync requests to one per <see cref="ClockSyncMinInterval"/>.</summary>
            public RequestThrottle ClockSyncThrottle = RequestThrottle.Open(ClockSyncMinInterval);

            public MatchPeerState(ClientSlot clientSlot, int clientChannelId, EntityId playerId)
                : base(clientSlot, clientChannelId, playerId)
            {
            }
        }

        protected override ClientPeerState CreateClientPeer(EntityId playerId, InternalEntitySubscribeRequestBase requestBase, InternalEntitySubscribeResponseBase responseBase)
            => new MatchPeerState(requestBase.AssociationRef.GetClientSlot(), requestBase.ClientChannelId, playerId);

        #endregion

        #region Server-to-server

        /// <summary>
        /// Answers a <see cref="MatchProbeRequest"/>. Reaching this handler means the table's row loaded, so the
        /// player's actor may re-attach. A table whose row cannot be read crashes on wake and never answers.
        /// <para>
        /// A probe for a table with <b>no database row</b> (deleted after the retention period) starts an actor with
        /// a null model, because of <see cref="AllowEntityCreationOnMessage"/>. It is refused with the same
        /// <c>EntityAskExceptionBase</c> as a crash on wake, so the asker clears its pointer, but without logging an
        /// error. The started actor then shuts down (<see cref="ShutDownIfStillEmpty"/>).
        /// </para>
        /// </summary>
        [EntityAskHandler]
        MatchProbeResponse HandleProbeRequest(EntityId askerId, MatchProbeRequest _)
        {
            if (Model == null)
            {
                ShutDownIfStillEmpty();
                throw new InvalidEntityAsk($"No table {_entityId} exists: there is no row behind this id.");
            }

            return new MatchProbeResponse(Model.Phase == MatchPhase.Playing, Model.FindSeatOfPlayer(askerId) >= 0);
        }

        /// <summary>How long an actor started by a probe waits for a setup message before it shuts down.</summary>
        static readonly TimeSpan EmptyActorGrace = TimeSpan.FromSeconds(5);

        /// <summary>
        /// Shuts down an actor that a probe started for an id with no database row. Under
        /// <see cref="ShutdownPolicy"/> alone, the actor would stay alive for the SDK's initial subscriber wait,
        /// and after a redeploy every player whose table row was deleted sends such a probe. The shutdown is
        /// delayed because a setup request could be queued behind the probe.
        /// </summary>
        void ShutDownIfStillEmpty()
        {
            ScheduleExecuteOnActorContext(EmptyActorGrace, () =>
            {
                if (Model != null || IsShutdownEnqueued)
                    return;

                _log.Debug("No table was set up behind {MatchId}. Standing down.", _entityId);
                RequestShutdown();
            });
        }

        /// <summary>
        /// Test only. Moves every pending deadline of the table to now, runs the table, and returns how many
        /// deadlines were moved. The normal timeout, cover and play-out logic runs on the moved deadlines
        /// (<c>docs/testing.md</c>, "Forcing timers").
        /// </summary>
        [EntityAskHandler]
        MatchForceExpireResponse HandleForceExpireRequest(MatchForceExpireRequest _)
        {
            if (Model == null)
                throw new InvalidEntityAsk($"No table {_entityId} exists: there is no row behind this id.");

            int numDeadlinesMoved = MatchHost.ForceExpireTimers(Model, _pendingBotMove, MetaTime.Now, HostEnvironment);
            RunTable();

            _log.Info("Force-expired {Count} pending deadlines. The table is {Phase} at index {PlayIndex}.", numDeadlinesMoved, Model.Phase, Model.Board.PlayIndex);
            return new MatchForceExpireResponse(numDeadlinesMoved, Model.Phase, Model.Board.PlayIndex);
        }

        #endregion

        #region Driving the table

        /// <summary>
        /// Advances the table as far as possible at the current time, then schedules the next wake. It is called
        /// after every state change and from every timer. The browser host calls the same logic on every frame.
        /// </summary>
        void RunTable()
        {
            if (Model == null)
                return;

            MatchHost.RunTable(Model, _pendingBotMove, MetaTime.Now, HostEnvironment);

            DeliverResults();
            RecoverFromRefusedPublish();
            ArmNextWake();
        }

        /// <summary>
        /// The <see cref="IMatchHostEnvironment"/> through which <see cref="MatchHost"/> calls back into this actor to
        /// publish actions, draw seeds, and send messages to one seat's client. Created once per actor.
        /// </summary>
        IMatchHostEnvironment HostEnvironment => _environment ??= new ActorHostEnvironment(this);

        IMatchHostEnvironment _environment;

        sealed class ActorHostEnvironment : IMatchHostEnvironment
        {
            readonly MatchActor _actor;

            public ActorHostEnvironment(MatchActor actor)
            {
                _actor = actor;
            }

            public bool Publish(MatchAction action) => _actor.PublishAction(action);

            public ulong NextSeed() => _actor._hostRng.NextULong();

            /// <summary>
            /// Called when the host changed a seat's hand without that seat's client playing a card, for example
            /// by a deadline auto-play, a covering bot's move, or a reclaim. Sends the new hand to the seat's
            /// client, if one is subscribed. The deal and reconnects are covered by the private state sent at
            /// subscribe time.
            /// </summary>
            public void OnSeatHandChanged(int seat)
            {
                EntitySubscriber session = _actor.TryGetSessionAtSeat(seat);
                if (session == null)
                    return;

                _actor.SendToClient(session, MatchHost.BuildHandDelivery(_actor.Model, seat));
            }

            /// <summary>
            /// Called when a seat's owner no longer plays it. Casts <see cref="MatchSeatLostNotification"/> to the
            /// owner's actor, which emits the analytics event.
            /// </summary>
            public void OnSeatLost(int seat, MatchSeatLossReason reason)
            {
                EntityId playerId = _actor.Model.GetSeat(seat).PlayerId;
                if (!playerId.IsValid)
                    return;

                _actor.CastMessage(playerId, new MatchSeatLostNotification(_actor._entityId, reason));
            }
        }

        /// <summary>
        /// Sends <see cref="MatchRecordResultRequest"/> to every seat that has not acknowledged its result, for both
        /// terminal phases. Each acknowledgement is persisted as <see cref="MatchSeatResult.Delivered"/>, so a
        /// restored table knows which seats still need their result. Retrying is safe because the player's actor
        /// records each match only once.
        /// </summary>
        void DeliverResults()
        {
            if (Model == null || Model.Phase == MatchPhase.Playing || _deliveringResults)
                return;

            List<MatchSeatResult> undeliveredResults = SeatsAwaitingTheirResult();
            if (undeliveredResults.Count == 0)
            {
                _resultRetryAt = MetaTime.Epoch;
                return;
            }

            _deliveringResults = true;

            // No retry is due while the round is in flight. An ask can take longer than ResultRetryDelay, and a retry
            // time that passes during the round would make every wake re-arm at once. The round's end sets the next one.
            _resultRetryAt = MetaTime.Epoch;

            // Send the asks in parallel. Sequential asks would each wait for the previous one, so one unreachable
            // player would delay every seat after it by a full ask timeout.
            List<Task<bool>> asks = new List<Task<bool>>(undeliveredResults.Count);
            foreach (MatchSeatResult result in undeliveredResults)
                asks.Add(SendResultToSeatAsync(result));

            // SendResultToSeatAsync returns false instead of throwing, so Task.WhenAll does not fault and all
            // persisting happens in OnDeliveryRoundFinished, which can await. The failure handler cannot await a
            // persist, so it only schedules another round.
            ContinueTaskOnActorContext(
                Task.WhenAll(asks),
                async delivered => await OnDeliveryRoundFinished(undeliveredResults, delivered),
                failure =>
                {
                    _log.Error("The result delivery round faulted unexpectedly: {Error}", failure);
                    _deliveringResults = false;
                    _resultRetryAt     = MetaTime.Now + ResultRetryDelay;
                    ArmNextWake();
                });
        }

        /// <summary>
        /// Returns the seats that have not acknowledged their result. An abandoned table has no captured results,
        /// so it returns an entry without a result for each human seat that has not acknowledged, so that the
        /// request still clears the player's pointer.
        /// </summary>
        List<MatchSeatResult> SeatsAwaitingTheirResult()
        {
            List<MatchSeatResult> owed = new List<MatchSeatResult>(MatchRules.NumSeats);

            if (Model.HasCapturedResults)
            {
                foreach (MatchSeatResult result in Model.SeatResults)
                {
                    if (!result.Delivered)
                        owed.Add(result);
                }
                return owed;
            }

            foreach (MatchSeat seat in Model.Seats)
            {
                if (seat.HasOwner && !_abandonedSeatsAcknowledged.Contains(seat.Seat))
                    owed.Add(new MatchSeatResult(seat.Seat, seat.PlayerId, position: -1, tricksWon: 0, humanOpponents: 0, finishedByPlayer: false, MatchSeatLossReason.None));
            }
            return owed;
        }

        /// <summary>
        /// The seats of an <b>abandoned</b> table that have acknowledged. An abandoned table has no results in the
        /// model to mark as delivered, so this is kept in memory only. Losing it on restart only causes one more
        /// request that finds the pointer already cleared.
        /// </summary>
        readonly HashSet<int> _abandonedSeatsAcknowledged = new HashSet<int>();

        /// <summary>
        /// Sends the result to one seat's player and returns true if the player's actor acknowledged it. Returns
        /// false instead of throwing on failure, for example when the player's actor is still waking or its node
        /// has gone down. The seat stays undelivered and the next wake sends the result again.
        /// </summary>
        async Task<bool> SendResultToSeatAsync(MatchSeatResult result)
        {
            MatchSeatResult payload       = Model.HasCapturedResults ? result : null;
            MetaDuration    matchDuration = Model.EndedAt > Model.DealtAt ? Model.EndedAt - Model.DealtAt : MetaDuration.Zero;

            try
            {
                await EntityAskAsync(
                    result.PlayerId,
                    new MatchRecordResultRequest(_entityId, Model.Phase, Model.EndedAt, payload, matchDuration));
                return true;
            }
            catch (Exception ex)
            {
                _log.Warning("Seat {Seat}'s player {PlayerId} did not take their result ({Error}).", result.Seat, result.PlayerId, ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Marks the seats that acknowledged as delivered, persists the model, and schedules a retry after
        /// <see cref="ResultRetryDelay"/> if any seat did not acknowledge.
        /// </summary>
        async Task OnDeliveryRoundFinished(List<MatchSeatResult> owed, bool[] acknowledged)
        {
            _deliveringResults = false;

            int deliveredCount = 0;
            int failedCount    = 0;
            for (int index = 0; index < owed.Count; index++)
            {
                if (!acknowledged[index])
                {
                    failedCount++;
                    continue;
                }

                deliveredCount++;
                if (Model.HasCapturedResults)
                    owed[index].Delivered = true;
                else
                    _abandonedSeatsAcknowledged.Add(owed[index].Seat);
            }

            if (failedCount > 0)
            {
                _log.Warning("{Failed} of {Total} results could not be delivered. Retrying in {Delay}.", failedCount, owed.Count, ResultRetryDelay);
                _resultRetryAt = MetaTime.Now + ResultRetryDelay;
            }
            else
            {
                _resultRetryAt = MetaTime.Epoch;
            }

            if (deliveredCount > 0)
            {
                _log.Info("Delivered {Delivered} of {Total} results for this {Phase} table.", deliveredCount, owed.Count, Model.Phase);

                // Persist now so that the Delivered flags survive a restart.
                await PersistStateIntermediate();
            }

            ArmNextWake();
        }

        /// <summary>
        /// Decides when to run the table again after <see cref="PublishAction"/> refused an action.
        /// <para>
        /// A refusal leaves the model unchanged, so nothing else schedules a useful wake: a refused bot move leaves
        /// no timestamp to wake on, and a refused trick resolution leaves a past timestamp that would wake the table
        /// every <see cref="WakePadding"/>. A refusal is not expected, because <see cref="RunTable"/> checks the
        /// same conditions before each publish, but nothing verifies that the two checks agree.
        /// </para>
        /// </summary>
        void RecoverFromRefusedPublish()
        {
            bool anySucceeded = _anyPublishSucceeded;
            bool anyRefused   = _anyPublishRefused;

            // Reset the flags for the next pass. They include a client's move, because the move handler runs the
            // table after publishing.
            _anyPublishSucceeded = false;
            _anyPublishRefused   = false;

            // A successful publish means the table advanced, so reset the retry count and resume running the table.
            if (anySucceeded)
            {
                _publishRetries = 0;
                _publishRetriesExhausted = false;
            }

            if (!anyRefused)
            {
                _publishRetryAt = MetaTime.Epoch;
                return;
            }

            if (_publishRetries >= MaxPublishRetries)
            {
                if (!_publishRetriesExhausted)
                    _log.Error("The timeline refused {Retries} publishes in a row. This table has stopped driving itself.", _publishRetries);

                // Keep _publishRetryAt instead of clearing it. No wake is scheduled from it while driving is
                // stopped, but it still records when the table last needed attention.
                _publishRetriesExhausted = true;
                return;
            }

            _publishRetries++;
            _publishRetryAt = MetaTime.Now + PublishRetryDelay;
        }

        /// <summary>
        /// Returns when the table next needs to run: the earliest of the model's pending deadlines, the pending
        /// bot move, the publish retry, and the result retry. Returns <see cref="MetaTime.Epoch"/> if none is
        /// pending.
        /// </summary>
        MetaTime GetNextAttentionAt()
        {
            // Include the result retry. Without it, a table that ended while a player's actor was unreachable
            // would never send the result again (docs/match.md, "Results").
            return MatchHost.Earlier(MatchHost.Earlier(MatchHost.GetNextWakeAt(Model, _pendingBotMove), _publishRetryAt), _resultRetryAt);
        }

        /// <summary>
        /// Schedules the next wake at the time from <see cref="GetNextAttentionAt"/>. When
        /// <see cref="_publishRetriesExhausted"/> is set, no wake is scheduled, because the pending timestamps are ones
        /// the table just failed to act on, and scheduling would cause a wake every <see cref="DeadlineWakeup.Padding"/>.
        /// </summary>
        void ArmNextWake()
        {
            MetaTime wakeAt = _publishRetriesExhausted ? MetaTime.Epoch : GetNextAttentionAt();
            if (_wake.TryArm(wakeAt, MetaTime.Now, out TimeSpan delay))
                ScheduleExecuteOnActorContext(delay, () => OnWake(wakeAt));
        }

        /// <summary>
        /// Runs a scheduled wake. Does nothing if <paramref name="scheduledWakeAt"/> is no longer the scheduled wake
        /// time, which is how outdated callbacks are ignored instead of cancelled.
        /// </summary>
        void OnWake(MetaTime scheduledWakeAt)
        {
            if (!_wake.TryConsume(scheduledWakeAt))
                return;

            RunTable();
        }

        #endregion
    }
}
