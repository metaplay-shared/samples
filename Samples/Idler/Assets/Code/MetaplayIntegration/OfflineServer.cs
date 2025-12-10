// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Game.Logic;
using Metaplay.Core;
using Metaplay.Core.Client;
using Metaplay.Core.Guild;
using Metaplay.Core.Guild.Actions;
using Metaplay.Core.Guild.Messages.Core;
using Metaplay.Core.GuildDiscovery;
using Metaplay.Core.Model;
using Metaplay.Core.MultiplayerEntity;
using Metaplay.Core.MultiplayerEntity.Messages;
using Metaplay.Core.Player;
using Metaplay.Core.Serialization;
using Metaplay.Unity;
using System;
using System.Collections.Generic;

public class OfflineServer : DefaultOfflineServer, IPlayerModelServerListener, IGuildModelServerListener, IGuildModelServerListenerCore
{
    GuildModel                         _guildModel;
    MetaTime                           _lastGuildTickAt;
    int                                _guildTickOperation;
    Dictionary<GuildInviteCode, int>   _activeGuildInvites = new Dictionary<GuildInviteCode, int>();

    public override void SetListeners(MetaplayClientStore clientStore)
    {
        GuildClient guildClient = clientStore.TryGetClient<GuildClient>(ClientSlotCore.Guild);
        guildClient.PhaseChanged += () =>
        {
            if (guildClient.GuildContext != null)
            {
                GuildModel guild = (GuildModel)guildClient.GuildContext.CommittedModel;
                guild.ServerListenerCore = this;
                guild.ServerListener = this;
            }
        };
    }

    protected override void SetSelfAsPlayerListener(IPlayerModelBase playerBase)
    {
        ((PlayerModel)playerBase).ServerListener = this;
    }

    public override void Update()
    {
        base.Update();
        if (_guildModel != null)
        {
            if (MetaTime.Now - _lastGuildTickAt > MetaDuration.FromSeconds(5))
                UpdateGuildTicks();
        }
    }

    protected override void HandleCustomMessage(MetaMessage msg)
    {
        switch (msg)
        {
            case GuildCreateRequest request:
            {
                EntitySerializedState guildSerializedState = SetupFakeGuild(request.CreationParams);
                SendToClient(new GuildCreateResponse(guildSerializedState, guildChannelId: 0));
                break;
            }

            case GuildJoinRequest request:
            {
                if (request.GuildId == EntityId.ParseFromString("Guild:Fake00000A"))
                {
                    // this is fake guild in discovery.
                    EntitySerializedState guildSerializedState = SetupFakeGuild(null);
                    SendToClient(new GuildJoinResponse(guildSerializedState, guildChannelId: 0));
                }
                else
                {
                    SendToClient(GuildJoinResponse.CreateRefusal());
                }
                break;
            }

            case GuildLeaveRequest _:
            {
                _guildModel = null;
                _lastGuildTickAt = MetaTime.Epoch;
                break;
            }

            case GuildEnqueueActionsRequest request:
            {
                UpdateGuildTicks();

                List<GuildTimelineUpdateMessage.Operation> operations = new List<GuildTimelineUpdateMessage.Operation>();
                int firstOperation = _guildTickOperation;

                foreach (GuildClientActionBase action in request.Actions.Deserialize(_guildModel.GameConfig, _logicVersion))
                {
                    action.InvokingPlayerId = OfflinePlayerId;
                    MetaActionResult result = ModelUtil.RunAction(_guildModel, action);
                    if (result.IsSuccess)
                    {
                        operations.Add(new GuildTimelineUpdateMessage.Operation(action, OfflinePlayerId));
                        _guildTickOperation++;
                    }
                }

                uint finalChecksum = ComputeGuildModelChecksum();
                SendToClient(new GuildTimelineUpdateMessage(new MetaSerialized<List<GuildTimelineUpdateMessage.Operation>>(operations, MetaSerializationFlags.SendOverNetwork, _logicVersion), _guildModel.CurrentTick, firstOperation, finalChecksum, guildChannelId: 0));
                break;
            }

            case GuildTransactionRequest request:
            {
                var preceedingUpdate = TryGetGuildTickUpdate();
                if (preceedingUpdate != null)
                    SendToClient(preceedingUpdate);

                var transaction = request.Transaction.Deserialize(null, _logicVersion);
                transaction.InvokingPlayerId = PlayerModel.PlayerId;

                ITransactionPlan playerPlan = transaction.PlanForPlayer(PlayerModel); // \todo: this is broken -- client has already mutated the shared PlayerModel.
                ITransactionPlan guildPlan;
                ITransactionPlan finalPlan;
                try
                {
                    if (!_guildModel.TryGetMember(OfflinePlayerId, out GuildMemberBase guildMember))
                        throw new InvalidOperationException("Guild state is broken");

                    guildPlan = transaction.PlanForGuild(_guildModel, guildMember);
                    finalPlan = transaction.PlanForFinalizing(playerPlan, guildPlan, serverPlan: null);
                }
                catch (TransactionPlanningFailure)
                {
                    var cancelAction = transaction.CreateCancelingPlayerAction(playerPlan);
                    SendToClient(new GuildTransactionResponse(
                        playerAction: new MetaSerialized<PlayerTransactionFinalizingActionBase>(cancelAction, MetaSerializationFlags.SendOverNetwork, _logicVersion),
                        guildAction: default,
                        playerActionTrackingId: 123
                        ));
                    break;
                }

                var guildAction = transaction.CreateFinalizingGuildAction(finalPlan);
                var playerAction = transaction.CreateFinalizingPlayerAction(finalPlan);

                if (guildAction != null)
                {
                    guildAction.InvokingPlayerId = PlayerModel.PlayerId;
                    ModelUtil.RunAction(_guildModel, guildAction);
                    _guildTickOperation++;
                }

                SendToClient(new GuildTransactionResponse(
                    playerAction: new MetaSerialized<PlayerTransactionFinalizingActionBase>(playerAction, MetaSerializationFlags.SendOverNetwork, _logicVersion),
                    guildAction: new MetaSerialized<GuildActionBase>(guildAction, MetaSerializationFlags.SendOverNetwork, _logicVersion),
                    playerActionTrackingId: 123
                    ));
                break;
            }

            case GuildDiscoveryRequest _:
            {
                SendToClient(new GuildDiscoveryResponse(GetDiscoveryFakeGuildInfos()));
                break;
            }

            case GuildSearchRequest request:
            {
                List<GuildDiscoveryInfoBase> infos = new List<GuildDiscoveryInfoBase>();
                foreach (var guildInfo in GetDiscoveryFakeGuildInfos())
                {
                    // minimal fake search
                    if (!guildInfo.DisplayName.ToLowerInvariant().Contains(request.SearchParams.SearchString.ToLowerInvariant()))
                        continue;
                    infos.Add(guildInfo);
                }
                SendToClient(new GuildSearchResponse(isError: false, infos));
                break;
            }

            case GuildCreateInvitationRequest request:
            {
                UpdateGuildTicks();

                if (_guildModel == null)
                {
                    SendToClient(GuildCreateInvitationResponse.CreateRefusal(request.QueryId, GuildCreateInvitationResponse.StatusCode.NotAMember));
                    break;
                }

                if (!_guildModel.TryGetMember(OfflinePlayerId, out GuildMemberBase member))
                    throw new InvalidOperationException("Guild state is broken");

                if (member.Invites.Count >= member.MaxNumInvites)
                {
                    SendToClient(GuildCreateInvitationResponse.CreateRefusal(request.QueryId, GuildCreateInvitationResponse.StatusCode.TooManyInvites));
                    break;
                }
                if (!((IGuildModelBase)_guildModel).HasPermissionToInvite(OfflinePlayerId, request.Type))
                {
                    SendToClient(GuildCreateInvitationResponse.CreateRefusal(request.QueryId, GuildCreateInvitationResponse.StatusCode.NotAllowed));
                    break;
                }

                int inviteId = _guildModel.RunningInviteId++;
                GuildInviteCode inviteCode = GuildInviteCode.CreateNewUnsafe(RandomPCG.CreateNew());
                GuildInviteState invite = new GuildInviteState(
                    type: GuildInviteType.InviteCode,
                    createdAt: MetaTime.Now,
                    expiresAfter: request.ExpiresAfter,
                    numMaxUsages: request.NumMaxUsages,
                    numTimesUsed: 0,
                    inviteCode: inviteCode);

                _activeGuildInvites.Add(inviteCode, inviteId);

                // Execute adding action
                GuildInviteUpdate action = new GuildInviteUpdate(OfflinePlayerId, inviteId, invite);
                RunAndFlushGuildAction(action);

                // reply to original erquest
                SendToClient(GuildCreateInvitationResponse.CreateSuccess(request.QueryId, inviteId));
                break;
            }

            case GuildRevokeInvitationRequest request:
            {
                UpdateGuildTicks();
                GuildInviteUpdate action = new GuildInviteUpdate(OfflinePlayerId, request.InviteId, null);
                RunAndFlushGuildAction(action);
                break;
            }

            case GuildInspectInvitationRequest request:
            {
                if (!_activeGuildInvites.ContainsKey(request.InviteCode))
                {
                    SendToClient(GuildInspectInvitationResponse.CreateInvalidOrExpired(request.QueryId));
                    break;
                }

                GuildInviterAvatar fakeInviterAvatar = new GuildInviterAvatar()
                {
                    PlayerId = EntityId.Create(EntityKindCore.Player, 100),
                    DisplayName = "inviter display name"
                };

                MetaSerialized<GuildDiscoveryInfoBase> discoveryInfo = new MetaSerialized<GuildDiscoveryInfoBase>(GetDiscoveryFakeGuildInfo(1), MetaSerializationFlags.SendOverNetwork, _logicVersion);
                MetaSerialized<GuildInviterAvatarBase> inviterAvatar = new MetaSerialized<GuildInviterAvatarBase>(fakeInviterAvatar, MetaSerializationFlags.SendOverNetwork, _logicVersion);
                SendToClient(GuildInspectInvitationResponse.CreateSuccess(request.QueryId, _activeGuildInvites[request.InviteCode], discoveryInfo, inviterAvatar));
                break;
            }

            default:
                base.HandleCustomMessage(msg);
                break;
        }
    }

    uint ComputeGuildModelChecksum() => ComputeModelChecksum(_guildModel);

    GuildTimelineUpdateMessage TryGetGuildTickUpdate()
    {
        MetaTime now            = MetaTime.Now;
        long lastTotalTicks     = _guildModel.CurrentTick;
        long currentTotalTicks  = ModelUtil.TotalNumTicksElapsedAt(now, _guildModel.TimeAtFirstTick, GuildModel.TicksPerSecond);
        long newTicks           = currentTotalTicks - lastTotalTicks;

        _lastGuildTickAt = now;

        if (newTicks == 0)
            return null;

        List<GuildTimelineUpdateMessage.Operation>  operations  = new List<GuildTimelineUpdateMessage.Operation>();
        long                                        firstTick   = _guildModel.CurrentTick + 1;

        _guildTickOperation = 1;

        for (long tick = 0; tick < newTicks; tick++)
        {
            ModelUtil.RunTick(_guildModel, NullChecksumEvaluator.Context);
            operations.Add(new GuildTimelineUpdateMessage.Operation(null, EntityId.None));
        }

        uint finalChecksum = ComputeGuildModelChecksum();
        return new GuildTimelineUpdateMessage(new MetaSerialized<List<GuildTimelineUpdateMessage.Operation>>(operations, MetaSerializationFlags.SendOverNetwork, _logicVersion), firstTick, 0, finalChecksum, guildChannelId: 0);
    }

    void UpdateGuildTicks()
    {
        var update = TryGetGuildTickUpdate();
        if (update != null)
            SendToClient(update);
    }

    void RunAndFlushGuildAction(GuildActionBase action)
    {
        List<GuildTimelineUpdateMessage.Operation>  operations      = new List<GuildTimelineUpdateMessage.Operation>();
        int                                         firstOperation  = _guildTickOperation;

        action.InvokingPlayerId = OfflinePlayerId;
        MetaActionResult result = ModelUtil.RunAction(_guildModel, action);
        if (result.IsSuccess)
        {
            operations.Add(new GuildTimelineUpdateMessage.Operation(action, OfflinePlayerId));
            _guildTickOperation++;
        }

        uint finalChecksum = ComputeGuildModelChecksum();
        SendToClient(new GuildTimelineUpdateMessage(new MetaSerialized<List<GuildTimelineUpdateMessage.Operation>>(operations, MetaSerializationFlags.SendOverNetwork, _logicVersion), _guildModel.CurrentTick, firstOperation, finalChecksum, guildChannelId: 0));
    }

    protected virtual void SetupFakeGuildParams(GuildModel model, GuildCreationRequestParamsBase creationParamsMaybe)
    {
        // \todo: spin these into Actions to allow code reuse with server
        model.DisplayName = ((GuildCreationRequestParams)creationParamsMaybe)?.DisplayName ?? "default name";
        model.Description = ((GuildCreationRequestParams)creationParamsMaybe)?.Description ?? "default description";
    }

    EntitySerializedState SetupFakeGuild(GuildCreationRequestParamsBase creationParamsMaybe)
    {
        var playerId = OfflinePlayerId;
        GuildModel model = new GuildModel();

        SetupFakeGuildParams(model, creationParamsMaybe);

        model.ResetTime(MetaTime.Now);
        model.GuildId = EntityId.Create(EntityKindCore.Guild, 200);
        model.LifecyclePhase = GuildLifecyclePhase.Running;

        model.LogicVersion = _logicVersion;
        model.Log = MetaplaySDK.Logs.CreateChannel("guild");
        model.SetGameConfig(_gameConfig);

        // \todo: should this be part of PlayerModel? Or can this contain data that is not part of player? Maybe a hybrid?
        GuildMemberPlayerData playerData = new GuildMemberPlayerData(
            displayName: PlayerModel.PlayerName
            );
        ModelUtil.RunAction(model, new GuildMemberAdd(playerId, 1, playerData));
        ModelUtil.RunAction(model, new GuildMemberIsOnlineUpdate(playerId, isOnline: true, lastUpdateAt: MetaTime.Now));

        _guildModel = model;
        _guildTickOperation = 1; // tick has been completed

        MetaSerialized<IMultiplayerModel>                   publicData      = new MetaSerialized<IMultiplayerModel>(model, MetaSerializationFlags.SendOverNetwork, _logicVersion);
        MetaSerialized<MultiplayerMemberPrivateStateBase>   memberData      = default;
        GuildMemberPrivateStateBase                         memberDataState = ((IGuildModelBase)model).GetMemberPrivateState(OfflinePlayerId);
        if (memberDataState != null)
            memberData = new MetaSerialized<MultiplayerMemberPrivateStateBase>(memberDataState, MetaSerializationFlags.SendOverNetwork, _logicVersion);

        ContentHash sharedGameConfigVersion = GameConfigArchive.Version;
        return new EntitySerializedState(
            publicState:                publicData,
            memberPrivateState:         memberData,
            currentOperation:           _guildTickOperation,
            logicVersion:               _logicVersion,
            sharedGameConfigVersion:    sharedGameConfigVersion,
            sharedConfigPatchesVersion: ContentHash.None,
            activeExperiments:          Array.Empty<EntityActiveExperiment>());
    }

    GuildDiscoveryInfoBase GetDiscoveryFakeGuildInfo(int key)
    {
        return new GuildDiscoveryInfo()
        {
            GuildId         = EntityId.ParseFromString("Guild:Fake00000A"),
            DisplayName     = "Fake guild",
            NumMembers      = 1,
            MaxNumMembers   = 12,
        };
    }

    List<GuildDiscoveryInfoBase> GetDiscoveryFakeGuildInfos()
    {
        List<GuildDiscoveryInfoBase> infos = new List<GuildDiscoveryInfoBase>();
        infos.Add(GetDiscoveryFakeGuildInfo(1));
        return infos;
    }

    void IPlayerModelServerListener.DuplicateEventLogEventsDebug(int numEventsToDuplicate, int numDuplicates)
    {
    }

    public void CreateNewParty()
    {
    }

    public void JoinParty(EntityId partyId)
    {
    }

    void IPlayerModelServerListener.OnProducerUpgraded(ProducerTypeId producer, int newLevel)
    {
    }

    void IGuildModelServerListenerCore.PlayerKicked(EntityId kickedPlayerId, GuildMemberBase kickedMember, EntityId kickingPlayerId, IGuildMemberKickReason kickReasonOrNull)
    {
        if (kickedPlayerId == OfflinePlayerId)
            SendToClient(new GuildSwitchedMessage(null, 1));
    }
}
