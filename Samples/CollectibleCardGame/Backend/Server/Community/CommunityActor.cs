using Game.Logic;
using Game.Server.Matchmaking;
using Metaplay.Cloud.Entity;
using Metaplay.Cloud.Persistence;
using Metaplay.Cloud.Sharding;
using Metaplay.Core;
using Metaplay.Core.Model;
using Metaplay.Server;
using Metaplay.Server.Database;
using Metaplay.Server.Player;
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Threading.Tasks;

namespace Game.Server.Community;

[Table("Community")]
public class PersistedCommunity : IPersistedEntity
{
    [Key, PartitionKey, Required, MaxLength(64), Column(TypeName = "varchar(64)")]
    public string EntityId { get; set; }
    [Required, Column(TypeName = "DateTime")] public DateTime PersistedAt { get; set; }
    [Required] public byte[] Payload { get; set; }
    [Required] public int SchemaVersion { get; set; }
    [Required] public bool IsFinal { get; set; }
}

[EntityConfig]
public class CommunityEntityConfig : PersistedEntityConfig
{
    public override EntityKind EntityKind => EntityKindGame.Community;
    public override Type EntityActorType => typeof(CommunityActor);
    public override NodeSetPlacement NodeSetPlacement => NodeSetPlacement.Service;
    public override IShardingStrategy ShardingStrategy => ShardingStrategies.CreateSingletonService();
    public override TimeSpan ShardShutdownTimeout => TimeSpan.FromSeconds(30);
}

/// <summary>A small global ladder using the SDK's singleton routing, persistence and entity asks.</summary>
public class CommunityActor : PersistedEntityActor<PersistedCommunity, CommunityState>
{
    public static readonly EntityId EntityId = EntityId.Create(EntityKindGame.Community, 0);
    protected override AutoShutdownPolicy ShutdownPolicy => AutoShutdownPolicy.ShutdownNever();
    protected override TimeSpan SnapshotInterval => TimeSpan.FromSeconds(30);
    CommunityState _state;
    readonly MatchPopulation _matches = new MatchPopulation();
    PagedIterator _seedIterator = PagedIterator.Start;
    DateTime _sampledAt;
    int _online;
    int _queued;

    protected override async Task Initialize()
        => await InitializePersisted(await MetaDatabase.Get().TryGetAsync<PersistedCommunity>(_entityId.ToString()));
    protected override Task<CommunityState> InitializeNew() => Task.FromResult(new CommunityState());
    protected override Task<CommunityState> RestoreFromPersisted(PersistedCommunity persisted)
        => Task.FromResult(DeserializePersistedPayload<CommunityState>(persisted.Payload, resolver: null, logicVersion: null));
    protected override Task PostLoad(CommunityState payload, DateTime persistedAt, TimeSpan elapsedTime)
    {
        _state = payload;
        if (!_state.SeededFromPlayers)
            ScheduleExecuteOnActorContext(DateTime.UtcNow + TimeSpan.FromSeconds(1), SeedPlayersAsync);
        return Task.CompletedTask;
    }
    protected override async Task PersistStateImpl(bool isInitial, bool isFinal)
    {
        PersistedCommunity persisted = new PersistedCommunity
        {
            EntityId = _entityId.ToString(), PersistedAt = DateTime.UtcNow,
            Payload = SerializeToPersistedPayload(_state, resolver: null, logicVersion: null),
            SchemaVersion = SchemaMigrationRegistry.Instance.GetSchemaMigrator<CommunityState>().CurrentSchemaVersion, IsFinal = isFinal,
        };
        if (isInitial)
            await MetaDatabase.Get().InsertAsync(persisted);
        else
            await MetaDatabase.Get().UpdateAsync(persisted);
    }

    // Seed existing offline accounts in small pages without waking player actors or delaying service startup.
    // Live player reports win over database snapshots; a restart safely resumes by re-reading missing entries.
    async Task SeedPlayersAsync()
    {
        try
        {
            PagedQueryResult<PersistedPlayerBase> page = await MetaDatabase.Get(QueryPriority.Low)
                .QueryPagedAsync<PersistedPlayerBase>("SeedCommunity", _seedIterator, 50);
            SharedGameConfig config = (SharedGameConfig)GlobalStateProxyActor.ActiveGameConfig.Get().BaselineGameConfig.SharedConfig;
            foreach (PersistedPlayerBase persisted in page.Items)
            {
                if (persisted.Payload == null)
                    continue;
                EntityId playerId = Metaplay.Core.EntityId.ParseFromString(persisted.EntityId);
                if (_state.Players.ContainsKey(playerId))
                    continue;
                try
                {
                    PlayerModel player = (PlayerModel)ServerPlayerModelUtility.OneTimeLoadFromPersisted(
                        playerId, persisted, config, persisted.LogicVersion);
                    _state.Update(new LeaderboardEntry
                    {
                        PlayerId = playerId, DisplayName = player.DisplayName, Rating = player.Record.Rating,
                        Wins = player.Record.RankedWins, Matches = player.Record.RankedMatchesPlayed,
                    });
                }
                catch (Exception ex)
                {
                    _log.Warning("Could not seed leaderboard entry for {PlayerId}: {Error}", playerId, ex.GetType().Name);
                }
            }
            _seedIterator = page.Iterator;
            if (_seedIterator.IsFinished)
            {
                _state.SeededFromPlayers = true;
                await PersistStateIntermediate();
                return;
            }
        }
        catch (Exception ex)
        {
            _log.Warning("Leaderboard seeding will retry: {Error}", ex.GetType().Name);
        }
        ScheduleExecuteOnActorContext(DateTime.UtcNow + TimeSpan.FromSeconds(1), SeedPlayersAsync);
    }

    [MessageHandler]
    void HandleLeaderboardUpdate(EntityId sender, InternalLeaderboardUpdate message)
    {
        if (sender.Kind == EntityKindCore.Player && message.Entry.PlayerId == sender)
            _state.Update(message.Entry);
    }

    [MessageHandler]
    void HandleMatchPopulation(EntityId sender, InternalMatchPopulation message)
    {
        if (sender.Kind == EntityKindGame.Match)
            _matches.Update(sender, message.Players, DateTime.UtcNow);
    }

    [EntityAskHandler]
    async Task<InternalCommunityReadResponse> HandleRead(EntityId sender, InternalCommunityRead request)
    {
        if (DateTime.UtcNow - _sampledAt >= TimeSpan.FromSeconds(5))
        {
            StatsCollectorNumConcurrentsResponse online = await EntityAskAsync<StatsCollectorNumConcurrentsResponse>(
                StatsCollectorManager.EntityId, StatsCollectorNumConcurrentsRequest.Instance);
            InternalQueuePopulationResponse queued = await EntityAskAsync<InternalQueuePopulationResponse>(
                MatchmakerActor.EntityId, new InternalQueuePopulationRequest());
            _online = online.NumConcurrents;
            _queued = queued.Players;
            _sampledAt = DateTime.UtcNow;
        }
        CommunitySnapshot snapshot = _state.Read(sender);
        snapshot.Online = _online;
        snapshot.Queued = _queued;
        snapshot.InMatch = _matches.Count(DateTime.UtcNow);
        snapshot.SampledAt = MetaTime.Now;
        return new InternalCommunityReadResponse(snapshot);
    }
}
