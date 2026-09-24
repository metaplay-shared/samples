// ---------------------------------------------------------------------------------------------------------
// VENDORED SDK SOURCE — NOT SAMPLE CODE.
//
// Copied from MetaplaySDK/Backend/Server/MultiplayerEntity/ at SDK 38.0.0 + SDK-263 (per-member timeline
// operations). Present only because SDK-263 ships in R39 and this sample targets released R38. Delete this
// folder on the R39 upgrade and point MatchActor back at the SDK's base. Differences from the original are
// marked `VENDORED CHANGE:`. See README.md in this folder.
// ---------------------------------------------------------------------------------------------------------

// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud;
using Metaplay.Cloud.Entity;
using Metaplay.Cloud.RuntimeOptions;
using Metaplay.Core;
using Metaplay.Core.Client;
using Metaplay.Core.Config;
using Metaplay.Core.IO;
using Metaplay.Core.Message;
using Metaplay.Core.Model;
using Metaplay.Core.MultiplayerEntity;
using Metaplay.Core.MultiplayerEntity.Messages;
using Metaplay.Core.Serialization;
using Metaplay.Server.MultiplayerEntity.InternalMessages;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

using Metaplay.Server;
using Metaplay.Server.MultiplayerEntity;

namespace Game.Server.SdkPreview
{
    // VENDORED CHANGE: DefaultMultiplayerEntityActiveEntityInfo is NOT copied here. It is a typecoded
    // MetaSerializable, and a second declaration collides with the SDK's registration (MP_DTC_00). The
    // SDK's own type is used instead, via `using Metaplay.Server.MultiplayerEntity`.

    /// <summary>
    /// Internal base class for actors managing a Multiplayer Entity.
    /// <para>
    /// Multiplayer Entity is an basic implementation for server-driven Entity. Multiple Clients may subscribe to an Multiplayer Entity and propose
    /// actions. Actions and Ticks executed by server are delivered back to client, allowing clients to see the changes in their copy of the Model.
    /// </para>
    /// </summary>
    public abstract partial class MultiplayerEntityActorBase<TModel, TAction>
        // VENDORED CHANGE: the SDK base derives from EntityActor and the ephemeral subclass adds the
        // SDK-internal IEphemeralEntityActor marker, which game code cannot implement. This copy is
        // ephemeral-only, so it takes the marker by inheritance instead. Reverts with the R39 upgrade.
        : EphemeralEntityActor
        where TModel : class, IMultiplayerModel<TModel>, new()
        where TAction : ModelAction
    {
        partial class SessionUserData
        {
            public ClientPeerState PeerState;
        }

        protected override AutoShutdownPolicy ShutdownPolicy => AutoShutdownPolicy.ShutdownAfterSubscribersGone(lingerDuration: TimeSpan.FromSeconds(5));

        /// <summary> LogicVersion in use for this actor </summary>
        protected readonly int _logicVersion;
        /// <summary> Logging channel to route log events from Model into Akka logger </summary>
        protected readonly LogChannel _modelLogChannel;
        /// <summary> Currently active config. This is always set. </summary>
        protected ActiveGameConfig _activeGameConfig;

        /// <summary>
        /// Currently active baseline-version of the game config. This is always set.
        /// Note that Multiplayer Entities do not participate in Player Experiments and hence will not have
        /// specialized configs.
        /// </summary>
        protected FullGameConfig _baselineGameConfig;
        /// <summary>
        /// Resolver for <see cref="_baselineGameConfig"/>. This is always set.
        /// Note that Multiplayer Entities do not participate in Player Experiments and hence will not have
        /// specialized configs.
        /// </summary>
        protected IGameConfigDataResolver _baselineGameConfigResolver;

        /// <summary>
        /// If true, on client's login the client will need to download the game config before the session can start. This blocks client session
        /// start, but when the session eventually completes, this entity is guaranteed to be have been loaded too.
        /// If false, the client will download the game config on background.
        /// Note that if an entity is associated to the player during the session, the game config is always loaded on the background.
        /// </summary>
        protected virtual bool RequireClientHasGameConfigPresentOnSessionStart => false;

        /// <summary>
        /// Name tag for the log messages in Model's log channel.
        /// Defaults to the type name of the <typeparamref name="TModel"/> with:
        /// * Lower cased
        /// * -Model suffix is removed
        /// </summary>
        protected virtual string LogChannelName
        {
            get
            {
                string name = typeof(TModel).Name.ToLowerInvariant();
                if (name.EndsWith("model", StringComparison.Ordinal))
                    name = name.Substring(0, name.Length - 5);
                return name;
            }
        }

        protected MultiplayerEntityActorBase()
        {
            _modelLogChannel = CreateModelLogChannel(LogChannelName);

            // Fetch current LogicVersion & GameConfigs
            _logicVersion               = GlobalStateProxyActor.ActiveClientCompatibilitySettings.Get().ClientCompatibilitySettings.ActiveLogicVersion;
            _activeGameConfig           = GlobalStateProxyActor.ActiveGameConfig.Get();
            _baselineGameConfig         = _activeGameConfig.BaselineGameConfig;
            _baselineGameConfigResolver = _baselineGameConfig.SharedConfig;
        }

        /// <param name="logChannelName">Name tag for the log messages from the Actor and the Model. Defaults to the type name of the <typeparamref name="TModel"/>.</param>
        [Obsolete("Prefer using parameterless constructor.")]
        protected MultiplayerEntityActorBase(EntityId entityId, string logChannelName = null) : this()
        {
            if (logChannelName != null)
                _modelLogChannel = CreateModelLogChannel(logChannelName);
        }

        protected override async Task Initialize()
        {
            InitializeTimeline();
            UpdateInActiveEntitiesList();

            await base.Initialize();
        }

        protected override void PostStop()
        {
            base.PostStop();
            // VENDORED CHANGE: direct-connection support is not copied; see SdkPreview/README.md.
        }

        /// <summary>
        /// Switches immediately to the given model (by calling <see cref="OnSwitchedToModel"/>) and creates a new journal for it. The model
        /// clock will be reset to MetaTime.now, and CurrentTick to 0.
        /// </summary>
        protected void SwitchToNewModelImmediately(TModel model)
        {
            OnSwitchedToModelCore(model);
            ResetJournalToModel(model);

            // Start ticking
            if (IsTicking)
                StartTickTimer();
        }

        /// <summary>
        /// Don't call directly. Use <see cref="SwitchToNewModelImmediately"/>.
        /// </summary>
        internal void ResetJournalToModel(TModel model)
        {
            ResetTimelineTo(model);

            // The model is now the currently-active `Model`.
            // Trigger event log flushing in case new events were logged in the model before it became the currently-active `Model`.
            // \todo: TryEnqueueTriggerEventLogFlushing();
        }

        internal void OnSwitchedToModelCore(TModel model)
        {
            AssignBasicRuntimePropertiesToModel(model, _modelLogChannel);

            // Forward to game.
            OnSwitchedToModel(model);
        }

        LogChannel CreateModelLogChannel(string name)
        {
            return new LogChannel(name, _log, MetaLogger.MetaLogLevelSwitch);
        }

        /// <summary>
        /// Assign to the model runtime properties that should always be present, such as Game Configs and logic versions.
        /// </summary>
        internal void AssignBasicRuntimePropertiesToModel(TModel model, LogChannel logChannel)
        {
            // \note ServerListener is not assigned here. That's only done when the model becomes the actor's current execution model (i.e. in OnSwitchedToModel).
            //       Here should be assigned runtime properties that we want to be always available in the model, even during MigrateState.
            model.LogicVersion          = _logicVersion;
            model.GameConfig            = _activeGameConfig.BaselineGameConfig.SharedConfig;
            model.Log                   = logChannel; // \note Assigning the specified log channel, which is not necessarily _modelLogChannel.
            // \todo: model.AnalyticsEventHandler = _analyticsEventHandler;
            model.ResetTime(MetaTime.Now);
        }

        [EntityAskHandler]
        async Task<InternalEntitySetupResponse> HandleInternalEntitySetupRequest(InternalEntitySetupRequest request)
        {
            if (Model != null)
                throw new InternalEntitySetupRefusal();

            await SetUpEntity(request.SetupParams);

            return new InternalEntitySetupResponse();
        }

        /// <summary>
        /// Runs the Entity Initialization as if it had received <see cref="InternalEntitySetupRequest" />.
        /// Specifically, this call sets <see cref="Model"/>.
        /// </summary>
        /// <param name="setupParams">The setup parameters from the InternalEntitySetupRequest.</param>
        protected async Task SetUpEntity(IMultiplayerEntitySetupParams setupParams)
        {
            if (Model != null)
                throw new InvalidOperationException("Entity is already set up.");

            TModel model = new TModel();
            model.EntityId  = _entityId;
            model.CreatedAt = MetaTime.Now;
            AssignBasicRuntimePropertiesToModel(model, _modelLogChannel);

            OnSwitchedToModelCore(model);
            await SetUpModelAsync(model, setupParams);
            ResetJournalToModel(model);
            await PersistSnapshot();

            // Initialization with setup request
            await OnEntityInitialized();

            // Start ticking
            if (IsTicking)
                StartTickTimer();
        }

        /// <summary>
        /// Updates the entity in Dashboard Active Entities List.
        /// </summary>
        protected void UpdateInActiveEntitiesList()
        {
            if (Model == null)
            {
                // Not set up yet. Don't announce
                return;
            }

            IActiveEntityInfo info = CreateActiveEntityInfo();
            if (info != null)
                Context.System.EventStream.Publish(new ActiveEntityInfoEnvelope(info));
        }

        [EntityAskHandler]
        InternalEntityStateResponse HandleInternalEntityStateRequest(InternalEntityStateRequest _)
        {
            MetaSerialized<IModel> serialized;
            if (Model == null)
            {
                // If the entity is not set up, return no state.
                serialized = default;
            }
            else
            {
                serialized = MetaSerialization.ToMetaSerialized<IModel>(Model, MetaSerializationFlags.IncludeAll, _logicVersion);
            }

            return new InternalEntityStateResponse(
                model:                  serialized,
                logicVersion:           _logicVersion,
                staticGameConfigId:     _activeGameConfig.BaselineStaticGameConfigId,
                dynamicGameConfigId:    _activeGameConfig.BaselineDynamicGameConfigId,
                specializationKey:      null, // by convention, null specialization key means no specialization.
                logicVersionMismatched: false);
        }

        sealed class SessionHandlerTimings
        {
            public TimeSpan OnClientSessionHandshake;
            public TimeSpan OnClientSessionStart;
        }

        protected override async Task<MetaMessage> OnNewSubscriber(EntitySubscriber subscriber, MetaMessage message)
        {
            if (subscriber.Topic == EntityTopic.Participant && message is InternalEntitySubscribeRequestBase request)
            {
                Stopwatch sw = Stopwatch.StartNew();
                SessionHandlerTimings handlerTimings = new SessionHandlerTimings();
                try
                {
                    return await OnParticipantSessionSubscriberAsync(subscriber, request, handlerTimings);
                }
                finally
                {
                    // If we spend too much time, report error. Session has already timed out.
                    TimeSpan elapsed = sw.Elapsed;
                    SessionOptions sessionOpts = RuntimeOptionsRegistry.Instance.GetCurrent<SessionOptions>();
                    if (elapsed > sessionOpts.SessionEntitySubscribeTimeout)
                        _log.Error("Session start handler took too long: {Elapsed}. The method must complete in {MaxLimit}. Common causes for this are OnClientSessionHandshake ({HandshakeElapsed}) or OnClientSessionStart ({SessionStartElapsed}) performing long operations.", elapsed, sessionOpts.SessionEntitySubscribeTimeout, handlerTimings.OnClientSessionHandshake, handlerTimings.OnClientSessionStart);
                }
            }

            _log.Warning("Subscriber {EntityId} on unknown topic [{Topic}]", subscriber.EntityId, subscriber.Topic);
            throw new InvalidOperationException();
        }

        async Task<InternalEntitySubscribeResponseBase> OnParticipantSessionSubscriberAsync(EntitySubscriber subscriber, InternalEntitySubscribeRequestBase request, SessionHandlerTimings handlerTimings)
        {
            if (Model == null)
            {
                _log.Warning("Entity {SourceEntity} from Session {SessionId} declared an association to this Entity but this entity is not set up yet. Refusing with EntityNotSetUp.", request.AssociationRef.SourceEntity, subscriber.EntityId);
                throw new InternalEntitySubscribeRefusedBase.Builtins.EntityNotSetUp();
            }

            EntityId playerId = SessionIdUtil.ToPlayerId(subscriber.EntityId);
            Stopwatch handlerSw = Stopwatch.StartNew();
            await OnClientSessionHandshake(sessionId: subscriber.EntityId, playerId, request);
            handlerTimings.OnClientSessionHandshake = handlerSw.Elapsed;

            // Check resource proposal, if such was given. Session start has a resource proposal but for mid-session joins
            // the client loads the config during session.
            if (request.ResourceProposal.HasValue && (RequireClientHasGameConfigPresentOnSessionStart || request.IsDryRun))
            {
                // If we have a correction and corrections are required, refuse session start with a correction.
                //
                // If we don't require corrections and there are optional corrections, hint them in the DryRun-success result.
                // Dry run means some preceding entity has failed resource correction, and in that case we want to tell client
                // all corrections, both mandatory and optional ones. If client is interrupted and will need to download some
                // resources, it might as well correct all of them.
                if (TryGetNewSessionResourceCorrection(request.AssociationRef, request.ResourceProposal.Value, request.SupportedArchiveCompressions, out SessionProtocol.SessionResourceCorrection resourceCorrection))
                {
                    if (RequireClientHasGameConfigPresentOnSessionStart)
                        throw new InternalEntitySubscribeRefusedBase.Builtins.ResourceCorrection(resourceCorrection, _associatedEntities.Values.ToList());
                    if (request.IsDryRun)
                        throw new InternalEntitySubscribeRefusedBase.Builtins.DryRunSuccess(resourceCorrection, _associatedEntities.Values.ToList());
                }
            }
            if (request.IsDryRun)
                throw new InternalEntitySubscribeRefusedBase.Builtins.DryRunSuccess(new SessionProtocol.SessionResourceCorrection(), _associatedEntities.Values.ToList());

            handlerSw.Restart();
            InternalEntitySubscribeResponseBase response = await OnClientSessionStart(sessionId: subscriber.EntityId, playerId, request, _associatedEntities.Values.ToList());
            handlerTimings.OnClientSessionStart = handlerSw.Elapsed;
            subscriber.SetUserData<SessionUserData>(new SessionUserData()
            {
                PeerState = CreateClientPeer(playerId, request, response)
            });

            UpdateInActiveEntitiesList();

            // Other clients could have pending unflushed actions. To avoid having the clients in the different steps, flush pending ops of other clients.
            FlushActions();

            // Check if we are ticking after the session subscribed.
            EnqueueOnActorContext(() =>
            {
                if (IsTicking)
                    StartTickTimer();
            });

            return response;
        }

        protected override Task OnSubscriberLostAsync(EntitySubscriber subscriber)
        {
            if (subscriber.Topic == EntityTopic.Participant)
                OnParticipantSessionEndedCore(subscriber);
            return Task.CompletedTask;
        }

        protected override void OnSubscriberKicked(EntitySubscriber subscriber, MetaMessage message)
        {
            if (subscriber.Topic == EntityTopic.Participant)
                OnParticipantSessionEndedCore(subscriber);
        }

        void OnParticipantSessionEndedCore(EntitySubscriber session)
        {
            // VENDORED CHANGE: the Direct Transport death pact is not copied; see SdkPreview/README.md.

            // \todo: move this "ended" callback into EntityActor
            OnParticipantSessionEnded(session);
        }

        protected override Task OnSubscriptionEndedAsync(EntitySubscription subscription, SubscriptionEndCause cause)
        {
            // VENDORED CHANGE: direct-transport loss handling is not copied; see SdkPreview/README.md.
            return Task.CompletedTask;
        }

        /// <summary>
        /// Creates a config correction if such is necessary.
        /// </summary>
        /// <param name="association">Information how a source entity is associated with this entity.</param>
        bool TryGetNewSessionResourceCorrection(AssociatedEntityRefBase association, SessionProtocol.SessionResourceProposal proposal, CompressionAlgorithmSet supportedArchiveCompressions, out SessionProtocol.SessionResourceCorrection correction)
        {
            if (!proposal.ConfigVersions.Contains(_activeGameConfig.ClientSharedGameConfigContentHash))
            {
                // Client is missing the game config available.
                SessionProtocol.SessionResourceCorrection.ConfigArchiveUpdateInfo updateInfo = _activeGameConfig.BaselineGameConfigSharedConfigDeliverySources.GetCorrection(supportedArchiveCompressions);
                correction = new SessionProtocol.SessionResourceCorrection();
                correction.ConfigUpdates.Add(updateInfo);
                return true;
            }

            correction = default;
            return false;
        }

        /// <summary>
        /// Publishes the message to the active sessions (SessionActor of the clients that are online).
        /// </summary>
        protected void SendToAllSessions(MetaMessage message)
        {
            foreach (EntitySubscriber subscriber in _subscribers.Values)
            {
                if (!subscriber.TryGetUserData<SessionUserData>(out SessionUserData sessionData))
                    continue;

                SendMessage(subscriber, message);
            }
        }

        /// <summary>
        /// Publishes the message to the entities in the given ClientSlots of the active sessions (EntityActors of the given slots of the clients that are online).
        /// </summary>
        protected void SendToAllSessions(OrderedSet<ClientSlot> targetSlots, MetaMessage message)
        {
            SendToAllSessions(new InternalSessionEntityBroadcastMessage(targetSlots, message));
        }

        /// <inheritdoc cref="SendToAllSessions(OrderedSet{ClientSlot}, MetaMessage)"/>
        protected void SendToAllSessions(ClientSlot targetSlot, MetaMessage message) => SendToAllSessions(new OrderedSet<ClientSlot>() { targetSlot }, message);

        /// <inheritdoc cref="SendToAllSessions(OrderedSet{ClientSlot}, MetaMessage)"/>
        protected void SendToAllSessions(ClientSlot targetSlot1, ClientSlot targetSlot2, MetaMessage message) => SendToAllSessions(new OrderedSet<ClientSlot>() { targetSlot1, targetSlot2 }, message);

        /// <summary>
        /// Helper for creating <see cref="EntitySerializedState"/> of the current state. If <paramref name="memberId"/> is given,
        /// the state contains the Private state of the member, as given by <see cref="IMultiplayerModel.GetMemberPrivateState(EntityId)"/>.
        /// Note that while Member is usually the player, this does not need to be the case.
        /// </summary>
        protected EntitySerializedState CreateSerializedStateForSubscriber(EntityId? memberId = null)
        {
            // Snapshot of the state
            MetaSerialized<IMultiplayerModel> publicData = MetaSerialization.ToMetaSerialized<IMultiplayerModel>(Model, MetaSerializationFlags.SendOverNetwork, _logicVersion);
            MetaSerialized<MultiplayerMemberPrivateStateBase> memberData  = default;
            if (memberId != null)
            {
                MultiplayerMemberPrivateStateBase memberDataState = Model.GetMemberPrivateState(memberId.Value);
                if (memberDataState != null)
                    memberData = MetaSerialization.ToMetaSerialized(memberDataState, MetaSerializationFlags.SendOverNetwork, _logicVersion);
            }

            uint initialChecksum = 0;
            if (_debugOptions.CheckInitialModelChecksum)
                initialChecksum = ChecksumUtil.ComputeHashForMetaSerialized<IModel>(Model, MetaSerializationFlags.ComputeChecksum, Model.LogicVersion);
            EntitySerializedState state = new EntitySerializedState(
                publicState:                publicData,
                memberPrivateState:         memberData,
                currentOperation:           _timelineCurrentPosition.Operation,
                logicVersion:               _logicVersion,
                sharedGameConfigVersion:    _activeGameConfig.ClientSharedGameConfigContentHash,
                sharedConfigPatchesVersion: ContentHash.None,
                activeExperiments:          Array.Empty<EntityActiveExperiment>(),
                initialChecksum:            initialChecksum);
            return state;
        }

        /// <summary>
        /// For persisted entities, persists a snapshot of the current entity state to a persisted storage.
        /// If the entity crashes, it will continue next time from this snapshot.
        /// For ephemeral entities, this method does nothing.
        /// </summary>
        public abstract Task PersistSnapshot();

        #region Session Client State

        /// <summary>
        /// Per-Session state to identify the client-side peer of the communications. Implementation may inherit this class to
        /// extend it and then override <see cref="CreateClientPeer"/> with custom implementation.
        /// </summary>
        protected class ClientPeerState
        {
            public readonly ClientSlot ClientSlot;
            public readonly int ClientChannelId;
            public readonly EntityId PlayerId;

            /// <summary>
            /// The id of the Member of the Model that this client follows the timeline as. Per-member operations address a member's
            /// follower by this id, see <see cref="ExecuteActionPerMember(EntityId, TAction, bool)"/>. A client session stands for its
            /// player, so by default this is <see cref="PlayerId"/>; an implementation whose clients follow as some other member overrides it.
            /// </summary>
            public virtual EntityId MemberEntityId => PlayerId;

            public ClientPeerState(ClientSlot clientSlot, int clientChannelId, EntityId playerId)
            {
                ClientSlot = clientSlot;
                ClientChannelId = clientChannelId;
                PlayerId = playerId;
            }
        }

        /// <summary>
        /// Gets the Client-side Session State for a subscriber client, or null if the subscriber is not an online session.
        /// </summary>
        protected ClientPeerState TryGetClientPeer(EntitySubscriber sessionSubscriber)
        {
            if (sessionSubscriber.TryGetUserData<SessionUserData>(out SessionUserData userData))
                return userData.PeerState;
            return null;
        }

        /// <summary>
        /// Create the Client Peer State for new Session. Implementation may override this to add
        /// custom data into session state by extending <see cref="ClientPeerState"/>.
        /// </summary>
        protected virtual ClientPeerState CreateClientPeer(EntityId playerId, InternalEntitySubscribeRequestBase requestBase, InternalEntitySubscribeResponseBase responseBase)
        {
            return new ClientPeerState(requestBase.AssociationRef.GetClientSlot(), requestBase.ClientChannelId, playerId);
        }

        #endregion

        #region Entity-Client Message Transport

        [PubSubMessageHandler]
        async Task HandleClientToEntityEnvelope(EntitySubscriber session, EntityClientToServerEnvelope envelope)
        {
            EntityId playerId = SessionIdUtil.ToPlayerId(session.EntityId);
            if (!session.TryGetUserData<SessionUserData>(out SessionUserData sessionData))
            {
                _log.Warning("Got ClientToEntityEnvelope from non-active player session {PlayerId}. Ignoring.", playerId);
                return;
            }

            await DispatchClientEnvelopeAsync(session, envelope.Message.Bytes);
        }

        async Task DispatchClientEnvelopeAsync(EntitySubscriber session, ReadOnlyMemory<byte> serialized)
        {
            // Unwrap envelopes and re-dispatch to handlers transparently. Ensure unwrapped message is legal.
            MetaMessage contents;
            MetaMessageSpec messageSpec;
            try
            {
                int messageTypeCode = MetaSerializationUtil.PeekDerivedTypeCode(serialized);
                messageSpec = MetaMessageRepository.Instance.GetFromTypeCode(messageTypeCode);

                MessageDirection messageDirection = messageSpec.MessageDirection;
                if (messageDirection != MessageDirection.ClientToServer && messageDirection != MessageDirection.Bidirectional)
                    throw new InvalidOperationException($"Received message {messageSpec.Name} from client containing message with invalid message direction: {messageDirection}");

                using (IOReader reader = new IOReader(serialized))
                {
                    contents = MetaSerialization.DeserializeTagged<MetaMessage>(reader, MetaSerializationFlags.SendOverNetwork, _baselineGameConfigResolver, _logicVersion);
                }
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Player {Player} sent malformed or illegal message", SessionIdUtil.ToPlayerId(session.EntityId));
                KickSubscriber(session, message: null);
                return;
            }

            // Re-dispatch
            Type msgType = messageSpec.Type;
            if (_dispatcher.TryGetPubSubSubscriberDispatchFunc(msgType, out var pubSubDispatchFunc))                await pubSubDispatchFunc(this, session, contents).ConfigureAwait(false);
            else if (_dispatcher.TryGetMessageDispatchFunc(msgType, out var msgDispatchFunc))                       await msgDispatchFunc(this, session.EntityId, contents).ConfigureAwait(false);
            else if (_dispatcher.TryGetPubSubSubscriberDispatchFunc(typeof(MetaMessage), out pubSubDispatchFunc))   await pubSubDispatchFunc(this, session, contents).ConfigureAwait(false);
            else                                                                                                    await HandleUnknownMessage(session.EntityId, contents).ConfigureAwait(false);
        }

        /// <inheritdoc cref="SendToClient"/>
        [Obsolete("Deprecated name, use SendToClient")]
        protected void SendToClientEntity(EntitySubscriber session, MetaMessage message) => SendToClient(session, message);

        /// <inheritdoc cref="SendToAllClients"/>
        [Obsolete("Deprecated name, use SendToAllClients")]
        protected void SendToAllClientEntities(MetaMessage message, EntitySubscriber excludingSession = null) => SendToAllClients(message, excludingSession);

        /// <summary>
        /// Sends the message to the clients that are online. The message is delivered to the corresponding Entity
        /// on a client. To handle the message, a listener needs to be attached to the EntityClient's MessageDispatcher.
        /// </summary>
        protected void SendToAllClients(MetaMessage message, EntitySubscriber excludingSession = null)
        {
            MetaSerialized<MetaMessage> contents = default;
            foreach (EntitySubscriber subscriber in _subscribers.Values)
            {
                if (subscriber == excludingSession)
                    continue;
                if (!subscriber.TryGetUserData<SessionUserData>(out SessionUserData sessionData))
                    continue;

                // Serialize lazily.
                if (contents.IsEmpty)
                    contents = MetaSerialization.ToMetaSerialized<MetaMessage>(message, MetaSerializationFlags.SendOverNetwork, _logicVersion);

                SendContentsToClient(subscriber, sessionData, contents);
            }
        }

        /// <summary>
        /// Sends the message to the specified client. The message is delivered to the corresponding Entity on a
        /// client. To handle the message, a listener needs to be attached to the EntityClient's MessageDispatcher.
        /// </summary>
        protected void SendToClient(EntitySubscriber session, MetaMessage message)
        {
            if (!session.TryGetUserData<SessionUserData>(out SessionUserData sessionData))
                return;

            SendContentsToClient(session, sessionData, MetaSerialization.ToMetaSerialized<MetaMessage>(message, MetaSerializationFlags.SendOverNetwork, _logicVersion));
        }

        void SendContentsToClient(EntitySubscriber session, SessionUserData sessionData, MetaSerialized<MetaMessage> contents)
        {
            // VENDORED CHANGE: the direct-connection branch is not copied, so everything goes in an
            // envelope over the session connection; see SdkPreview/README.md.
            ClientPeerState peer = sessionData.PeerState;
            SendMessage(session, new EntityServerToClientEnvelope(peer.ClientChannelId, contents));
        }

        #endregion

        #region Misc

        [PubSubMessageHandler]
        void HandleInternalSessionNotifyClientAppStatusChanged(EntitySubscriber session, InternalSessionNotifyClientAppStatusChanged notify)
        {
            if (session.TryGetUserData<SessionUserData>(out SessionUserData sessionData))
                OnClientAppStatusChanged(session, sessionData.PeerState.PlayerId, notify);
        }

        #endregion

        #region Callbacks to userland

        /// <summary>
        /// Called when entity is set up for the first time, or is woken up from a persisted state after being set up. During and after this call, the <c>Model</c>
        /// exists and has been set up with the <see cref="SetUpModelAsync"/>.
        /// </summary>
        protected virtual Task OnEntityInitialized() => Task.CompletedTask;

        /// <summary>
        /// Called when the entity is being set up the first time with <see cref="InternalEntitySetupRequest"/>. Implementation should use the <paramref name="setupParams"/>
        /// to set up the <paramref name="model"/>. The given model is just initialized and does not need to be cleaned up before setup.
        /// </summary>
        protected abstract Task SetUpModelAsync(TModel model, IMultiplayerEntitySetupParams setupParams);

        /// <summary>
        /// Called when new model instance is becoming active. Callee should should set up listeners.
        /// After switching to the model is complete, the <see cref="Model"/> can be used to refer to it.
        ///
        /// This function is called during entity initialization, after state reset, and in
        /// <see cref="SwitchToNewModelImmediately(TModel)"/>.
        /// </summary>
        protected virtual void OnSwitchedToModel(TModel model) { }

        /// <summary>
        /// Creates the Active Entity info for the Entity. This information is shown in Dashboard in Recently Active Entities
        /// list. If the default values need to be extended, implement <see cref="IActiveEntityInfo"/> in a custom class and construct
        /// such class here. Returning <c>null</c> means the entity will not be added to the Recently Active list.
        /// </summary>
        protected virtual IActiveEntityInfo CreateActiveEntityInfo()
        {
            return new DefaultMultiplayerEntityActiveEntityInfo(
                entityId:       _entityId,
                activityAt:     MetaTime.Now,
                createdAt:      Model.CreatedAt,
                displayName:    Model.GetDisplayNameForDashboard());
        }

        /// <summary>
        /// Called when session (on Participant topic channel) is kicked, terminated, or unsubscribes from this entity.
        /// </summary>
        protected virtual void OnParticipantSessionEnded(EntitySubscriber session) { }

        /// <summary>
        /// Called when Session (i.e. client, i.e. user) subscribes to this Entity. Implementation should check the subscription requirements and throw
        /// an <see cref="InternalEntitySubscribeRefusedBase"/> if there if a failure. For example, entity should check the player is (still) a
        /// participant in this entity. This handshake may not lead into a successful session creation (via <see cref="OnClientSessionStart"/>.
        /// <para>
        /// SDK handles Resource Correction and DryRuns automatically.
        /// </para>
        /// </summary>
        /// <param name="sessionId">The EntityID of the session actor subscribing.</param>
        /// <param name="playerId">The PlayerId of the session subscribing.</param>
        /// <param name="requestBase">The Subscribe payload from session.</param>
        protected virtual Task OnClientSessionHandshake(EntityId sessionId, EntityId playerId, InternalEntitySubscribeRequestBase requestBase) => Task.CompletedTask;

        /// <summary>
        /// Called when Session (i.e. client, i.e. user) subscribes into this Entity and the requirements have been checked (in <see cref="OnClientSessionHandshake"/>.
        /// Implementation should respond with the entity state. See also <see cref="InternalEntitySubscribeResponseBase.Default"/> for default type implementation and
        /// <see cref="CreateSerializedStateForSubscriber"/> helper. To override only the entity client data, see <see cref="CreateClientSessionStartEntityClientData"/>.
        /// </summary>
        /// <param name="sessionId">The EntityId of the session actor subscribing.</param>
        /// <param name="playerId">The PlayerId of the session subscribing.</param>
        /// <param name="requestBase">The Subscribe payload from session.</param>
        /// <param name="associatedEntities">The set of currently associated entities as managed by <see cref="AddEntityAssociation(AssociatedEntityRefBase)"/> and <see cref="RemoveEntityAssociation(ClientSlot)"/></param>
        protected virtual Task<InternalEntitySubscribeResponseBase> OnClientSessionStart(EntityId sessionId, EntityId playerId, InternalEntitySubscribeRequestBase requestBase, List<AssociatedEntityRefBase> associatedEntities)
        {
            EntitySerializedState serialized = CreateSerializedStateForSubscriber(memberId: playerId);
            return Task.FromResult<InternalEntitySubscribeResponseBase>(new InternalEntitySubscribeResponseBase.Default(
                new EntityInitialState(
                    state:          serialized,
                    channelId:      requestBase.ClientChannelId,
                    clientData:     CreateClientSessionStartEntityClientData(sessionId, playerId, requestBase, associatedEntities),
                    debugConfig:    new EntityClientDebugConfig(
                        // All consistency checks enable consistency checks on client
                        clientConsistencyChecks: _debugOptions.ConsistencyChecks == EntityConsistencyChecks.All,
                        // Rollback detects the specific operation when mismatch happened. That is only useful if all operations
                        // have been checksummed.
                        clientEnableChecksumMismatchRollback: _debugOptions.Checksumming.Mode == EntityChecksumMode.ModeCode.PerOperation
                        )),
                associatedEntities));
        }

        /// <summary>
        /// Called when client app's lifecycle status changes, for example in the case when app is put on background, or connection is lost.
        /// For PlayerActor, this is called <see cref="PlayerActorBase{TModel, TPersisted}.OnClientConnectivityStatusChanged"/>
        /// </summary>
        protected virtual void OnClientAppStatusChanged(EntitySubscriber session, EntityId playerId, InternalSessionNotifyClientAppStatusChanged notification) { }

        /// <summary>
        /// Called by default <see cref="OnClientSessionStart"/> implementation to create entity client data. Implementation may override this method to
        /// use custom client data for additional custom payload.
        /// </summary>
        /// <param name="sessionId">The EntityId of the session actor subscribing.</param>
        /// <param name="playerId">The PlayerId of the session subscribing.</param>
        /// <param name="requestBase">The Subscribe payload from session.</param>
        /// <param name="associatedEntities">The set of currently associated entities as managed by <see cref="AddEntityAssociation(AssociatedEntityRefBase)"/> and <see cref="RemoveEntityAssociation(ClientSlot)"/></param>
        protected virtual EntityClientData CreateClientSessionStartEntityClientData(EntityId sessionId, EntityId playerId, InternalEntitySubscribeRequestBase requestBase, List<AssociatedEntityRefBase> associatedEntities)
        {
            return new EntityClientData.Default(clientSlot: requestBase.AssociationRef.GetClientSlot());
        }

        /// <inheritdoc cref="EntityActor.OnSubscriberUnsubscribedAsync(EntitySubscriber, MetaMessage)"/>
        /// <remarks>
        /// If the <paramref name="subscriber"/> is the session subscriber (SessionActor), then the <paramref name="goodbyeMessage"/>
        /// is a type of <see cref="InternalEntitySessionGoodbyeMessage"/>.
        /// </remarks>
        [Obsolete("Use OnSubscriberEndedAsync to handle all cases of subscriber leaving")]
        protected override Task OnSubscriberUnsubscribedAsync(EntitySubscriber subscriber, MetaMessage goodbyeMessage) => Task.CompletedTask;

        #endregion
    }
}
