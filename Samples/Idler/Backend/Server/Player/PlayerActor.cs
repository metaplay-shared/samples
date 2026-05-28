// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Game.Logic;
using Game.Logic.League;
using Game.Logic.TypeCodes;
using Game.Server.GuildDiscovery;
using Game.Server.Matchmaking;
using Game.Server.Party;
using Metaplay.Cloud.Analytics;
using Metaplay.Cloud.Entity;
using Metaplay.Core;
using Metaplay.Core.Analytics;
using Metaplay.Core.Guild;
using Metaplay.Core.League;
using Metaplay.Core.Model;
using Metaplay.Core.Player;
using Metaplay.Server;
using Metaplay.Server.EventLog;
using Metaplay.Server.GuildDiscovery;
using Metaplay.Server.League;
using Metaplay.Server.Matchmaking;
using Metaplay.Server.MultiplayerEntity.InternalMessages;
using System;
using System.Threading.Tasks;
using static System.FormattableString;

namespace Game.Server.Player
{
    [EntityConfig]
    public class PlayerConfig : PlayerConfigBase
    {
        public override Type EntityActorType => typeof(PlayerActor);
    }

    /// <summary>
    /// Entity actor class representing a player.
    /// </summary>
    public sealed class PlayerActor : PlayerActorBase<PlayerModel>, IPlayerModelServerListener
    {
        DefaultPlayerLeagueIntegrationHandler<IdlerDivisionClientState> _idlerLeagueIntegration;
        DefaultPlayerLeagueIntegrationHandler<IdlerPvPDivisionClientState> _idlerPvPLeagueIntegration;
        
        public PlayerActor()
        {
            _idlerLeagueIntegration = 
                Leagues.CreateLeagueIntegrationHandler<DefaultPlayerLeagueIntegrationHandler<IdlerDivisionClientState>>(
                ClientSlotGame.IdlerLeague, 0,
                DefaultPlayerLeagueIntegrationHandler<IdlerDivisionClientState>.Create);
            
            _idlerPvPLeagueIntegration =
                Leagues.CreateLeagueIntegrationHandler<DefaultPlayerLeagueIntegrationHandler<IdlerPvPDivisionClientState>>(
                    ClientSlotGame.IdlerPvPLeague, 1,
                    DefaultPlayerLeagueIntegrationHandler<IdlerPvPDivisionClientState>.Create);
        }

        public override void OnPlayerNameChanged(string newName)
        {
            if (Model.CurrentParty != EntityId.None)
            {
                // Send the name update to our current party. We're explicitly not handling the result of this EntityAsk,
                // failure to update the name is not fatal.
                _ = EntityAskAsync(Model.CurrentParty,
                    new PartyJoinOrUpdateRequest() { PlayerName = newName });
            }
        }

        protected override string RandomNewPlayerName()
        {
            return Invariant($"Guest {Random.Shared.Next(100_000)}");
        }

        protected override void OnSwitchedToModel(PlayerModel model)
        {
            model.ServerListener = this;
        }

        protected override Task OnPlayerScheduledForDeletion()
        {
            return Task.CompletedTask;
        }

        protected override Task OnPlayerBeingDeleted()
        {
            return Task.CompletedTask;
        }

        protected override async Task OnSessionStartAsync(PlayerSessionParams sessionParams, bool isFirstLogin)
        {
            // Join idler pvp leagues on startup.
            // We are using the TryReassignPlayer method here due to being able to join the league multiple
            // times in a season. This is not the case for the other idler league. Using the TryJoinPlayerLeague
            // method would result in the player being unable to join the league after the first time.
            if (_idlerPvPLeagueIntegration.DivisionClientState.CurrentDivision == EntityId.None)
            {
                (DivisionIndex? joinedDivision, LeagueJoinRefuseReason? reason) = 
                    await _idlerPvPLeagueIntegration.TryReassignPlayer(false, 
                    EmptyLeagueJoinRequestPayload.Instance);
                if (joinedDivision.HasValue)
                    _log.Information("Joined PvP league: {Division}", joinedDivision);
                else if (reason.HasValue)
                    _log.Warning("Failed to join PvP league: {Reason}", reason);
            }

            UpdateMatchmaker();

            // Re-attach existing party, if it did exist
            if (Model.CurrentParty != EntityId.None)
                UpdateCurrentPartyAssociation();
        }

        protected override Task<bool> OnAssociatedEntityRefusalAsync(AssociatedEntityRefBase association, InternalEntitySubscribeRefusedBase refusal)
        {
            if (association.GetClientSlot() == ClientSlotGame.Party &&
                refusal is InternalEntitySubscribeRefusedBase.Builtins.EntityNotSetUp)
            {
                // Party no longer exists
                Model.CurrentParty = EntityId.None;
                return Task.FromResult(true);
            }

            return base.OnAssociatedEntityRefusalAsync(association, refusal);
        }

        protected override void OnOwnerSessionEnded(EntitySubscriber _, bool wasKicked)
        {
        }

        #region IPlayerModelServerListener

        class DuplicateEventLogEventsDebug
        {
            public int NumEventsToDuplicate;
            public int NumDuplicates;
            public DuplicateEventLogEventsDebug(int numEventsToDuplicate, int numDuplicates) { NumEventsToDuplicate = numEventsToDuplicate; NumDuplicates = numDuplicates; }
        }
        
        void IPlayerModelServerListener.DuplicateEventLogEventsDebug(int numEventsToDuplicate, int numDuplicates)
        {
            _self.Tell(new DuplicateEventLogEventsDebug(numEventsToDuplicate, numDuplicates), sender: _self);
        }

        public void CreateNewParty()
        {
            EnqueueOnActorContext(() => CreateNewPartyInternalAsync(numRetries: 3));
        }

        async Task CreateNewPartyInternalAsync(int numRetries)
        {
            EntityId partyId = EntityId.CreateRandom(EntityKindGame.Party);
            InternalEntitySetupRequest setupRequest = new InternalEntitySetupRequest(new PartySetupParams(owner: _entityId, ownerName: Model.PlayerName));

            try
            {
                await EntityAskAsync(partyId, setupRequest);
            }
            catch (Exception ex)
            {
                if (ex is InternalEntitySetupRefusal && numRetries >= 0)
                {
                    // Party EntityId was already taken, try again with a different EntityId.
                    EnqueueOnActorContext(() => CreateNewPartyInternalAsync(numRetries - 1));
                }
                else
                {
                    _log.Error(ex, "Unexpected error in creating party after retries");
                    SendToClient(new PlayerPartyError("Party creation failed, try again later."));
                }

                return;
            }

            // On successful creation remember current party id and trigger update to association
            Model.CurrentParty = partyId;
            UpdateCurrentPartyAssociation();
        }

        public void JoinParty(EntityId partyId) => ExecuteOnActorContextAsync(() => JoinPartyInternalAsync(partyId));

        async Task JoinPartyInternalAsync(EntityId partyId)
        {
            try
            {
                await EntityAskAsync(partyId, new PartyJoinOrUpdateRequest() { PlayerName = Model.PlayerName });
            }
            catch (Exception ex)
            {
                if (ex is InternalEntityAskNotSetUpRefusal)
                {
                    SendToClient(new PlayerPartyError("Party no longer exists."));
                }
                else
                {
                    _log.Error(ex, "Unexpected error in joining party {PartyId}", partyId);
                    SendToClient(new PlayerPartyError("Joining party failed, try again later."));
                }
                return;
            }

            Model.CurrentParty = partyId;
            UpdateCurrentPartyAssociation();
        }

        void UpdateCurrentPartyAssociation()
        {
            AddEntityAssociation(new AssociatedEntityRefBase.Default(ClientSlotGame.Party, _entityId, Model.CurrentParty), removeOnSessionEnd: true);
        }

        [CommandHandler]
        async Task HandleDuplicateEventLogEventsDebug(DuplicateEventLogEventsDebug cmd)
        {
            PlayerEventLogScanResponse logResponse = await HandleEntityEventLogScanRequest(new EntityEventLogScanRequest(
                startCursor: new EntityEventLogCursorOldest(),
                numEntries: cmd.NumEventsToDuplicate,
                scanDirection: EntityEventLogScanDirection.TowardsNewer,
                startTime: null,
                endTime: null));

            for (int duplicateNdx = 0; duplicateNdx < cmd.NumDuplicates; duplicateNdx++)
            {
                foreach (PlayerEventLogEntry entry in logResponse.Entries.Deserialize(_baselineGameConfigResolver, ServerLogicVersion))
                    Model.EventStream.Event(entry.Payload);
            }
        }

        void IPlayerModelServerListener.OnProducerUpgraded(ProducerTypeId producer, int newLevel)
        {
            // Emit score event to league integration.
            _idlerLeagueIntegration.EmitDivisionScoreEvent(
                new IdlerPlayerDivisionProducerScoreEvent(MetaTime.Now, producer, newLevel));
        }

        #endregion

        protected override async Task OnPlayerBanStateChanged(bool isBanned, PlayerBanInfo banInfo)
        {
            // Example behavior: If a player is banned for more than 24 hours,
            // remove the player from the guild they are on.
            if (isBanned)
            {
                if (Model.GuildState.GuildId != EntityId.None)
                {
                    bool shouldLeaveGuild;
                    if (banInfo.EndTimeOrNull is not MetaTime banEndsAt)
                    {
                        // permanently banned
                        shouldLeaveGuild = true;
                    }
                    else if (banEndsAt > MetaTime.Now + MetaDuration.FromHours(24))
                    {
                        // long term ban
                        shouldLeaveGuild = true;
                    }
                    else
                    {
                        // short ban
                        shouldLeaveGuild = false;
                    }

                    if (shouldLeaveGuild)
                        _ = await Guilds.LeaveGuildAsync(forceLeave: false);
                }
            }
        }

        public sealed class GuildComponent : GuildComponentBase<PlayerActor>
        {
            public GuildComponent(PlayerActor player) : base(player) { }

            protected override GuildInviterAvatarBase CreateGuildInviterAvatar()
            {
                return new GuildInviterAvatar()
                {
                    PlayerId = Player._entityId,
                    DisplayName = Player.Model.PlayerName,
                };
            }

            protected override GuildMemberPlayerData CreateGuildMemberPlayerData()
            {
                return new GuildMemberPlayerData(
                    displayName: Player.Model.PlayerName,
                    playerLevel: Player.Model.PlayerLevel
                    );
            }

            protected override GuildDiscoveryPlayerContextBase CreateGuildDiscoveryContext()
            {
                return new GuildDiscoveryPlayerContext()
                {
                    PlayerLevel = Player.Model.PlayerLevel,
                };
            }

            protected override GuildCreationParamsBase TryCreateGuildCreationParamsFromRequest(GuildCreationRequestParamsBase paramsBase)
            {
                GuildCreationRequestParams requestParams = (GuildCreationRequestParams)paramsBase;

                // no special validation, or custom data. Just pass data thru. The data will
                // be validated again in GuildRequirementsValidator
                return new GuildCreationParams()
                {
                    DisplayName = requestParams?.DisplayName ?? "Cool Guild #" + Util.ObjectToStringInvariant(Random.Shared.Next(100, 999)),
                    Description = requestParams?.Description ?? "",
                };
            }

            protected override GuildCreationParamsBase TryCreateGuildCreationParamsForTest()
            {
                return new GuildCreationParams()
                {
                    DisplayName = "Test guild",
                    Description = "guild for unit tests",
                };
            }
        }

        public sealed class LeagueComponent : LeagueComponentBase
        {
            public LeagueComponent(PlayerActorBase<PlayerModel, PersistedPlayerBase> playerActor) : base(playerActor) { }
        }

        protected override GuildComponentBase CreateGuildComponent()
        {
            return new GuildComponent(this);
        }
        
        protected override LeagueComponentBase CreateLeagueComponent()
        {
            return new LeagueComponent(this);
        }

        [EntityAskHandler]
        InternalPlayerGetBattleAttackParamsResponse HandleAttackParamsRequest(InternalPlayerGetBattleAttackParamsRequest _)
        {
            return new InternalPlayerGetBattleAttackParamsResponse(
                IdlerMatchmakerPlayerModel.CalculatePlayerMmr(Model),
                IdlerMatchmakerPlayerModel.GetHighestLevelProducer(Model));
        }

        void UpdateMatchmaker()
        {
            IdlerMatchmakerPlayerModel? model = IdlerMatchmakerPlayerModel.TryCreateModel(Model);
            AsyncMatchmakingPlayerStateUpdate playerStateUpdate = AsyncMatchmakingPlayerStateUpdate.Create(
                Model.PlayerId, model);

            EntityId matchmakerId = IdlerAsyncMatchmakerActor.Entities.GetMatchmakerForDefenderPlayer(Model.PlayerId);

            CastMessage(matchmakerId, playerStateUpdate);
        }

        [MessageHandler]
        async Task HandlePlayerJoinIdleLeagueRequest(PlayerJoinIdleLeagueRequest _)
        {
            (DivisionIndex? divisionId, LeagueJoinRefuseReason? refuseReason) = await _idlerLeagueIntegration.TryJoinPlayerLeague();

            if (divisionId.HasValue)
                SendToClient(PlayerJoinIdleLeagueResponse.ForSuccess(divisionId.Value));
            else
                SendToClient(PlayerJoinIdleLeagueResponse.ForFailure(refuseReason.GetValueOrDefault(LeagueJoinRefuseReason.UnknownReason)));
        }

        [MessageHandler]
        void HandleInternalWinIdlerPvPBattleMessage(InternalWinIdlerPvPBattleMessage _)
        {
            if (_idlerPvPLeagueIntegration.DivisionClientState.CurrentDivision == EntityId.None)
                return;
            
            _idlerPvPLeagueIntegration.EmitDivisionScoreEvent(new IdlerPvPDivisionScoreEvent(MetaTime.Now));
        }

        [MessageHandler]
        void HandlePlayerCrashActorMessage(PlayerDebugCrashActor crash)
        {
            throw new InvalidOperationException($"Debug-crashing upon client's request. Client's reason: {crash.Reason ?? "<null>"}");
        }

        protected override PlayerAnalyticsContext CreateAnalyticsContext(PlayerModel model, int? sessionNumber, MetaDictionary<string, string> experiments)
        {
            return new IdlerPlayerAnalyticsContext(sessionNumber, experiments, numGold: model.Wallet.NumGold, numGems: model.Wallet.NumGems);
        }

        protected override MetaDictionary<AnalyticsLabel, string> GetAnalyticsLabels(PlayerModel model)
        {
            return new MetaDictionary<AnalyticsLabel, string>
            {
                { IdlerAnalyticsLabel.Language, model.Language?.Value },
                { IdlerAnalyticsLabel.Country, model.LastKnownLocation?.Country.IsoCode },
            };
        }
    }

    [MetaSerializableDerived(100)]
    public sealed class IdlerPlayerAnalyticsContext : PlayerAnalyticsContext
    {
        [MetaMember(100)] public int NumGold { get; private set; }
        [MetaMember(101)] public int NumGems { get; private set; }

        IdlerPlayerAnalyticsContext() {}
        public IdlerPlayerAnalyticsContext(int? sessionNumber, MetaDictionary<string, string> experiments, int numGold, int numGems)
            : base(sessionNumber, experiments)
        {
            NumGold = numGold;
            NumGems = numGems;
        }
    }

    [MetaSerializable]
    public class IdlerAnalyticsLabel : AnalyticsLabel
    {
        protected IdlerAnalyticsLabel(int value, string name) : base(value, name) { }

        public static readonly IdlerAnalyticsLabel Language = new IdlerAnalyticsLabel(1, nameof(Language));
        public static readonly IdlerAnalyticsLabel Country = new IdlerAnalyticsLabel(2, nameof(Country));
    }
}
