using Game.Logic;
using Metaplay.Cloud.Entity;
using Metaplay.Cloud.Persistence;
using Metaplay.Cloud.Sharding;
using Metaplay.Core;
using Metaplay.Server;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Threading.Tasks;

namespace Game.Server.MapRegistry
{
    /// <summary>
    /// Where the registry lives. There is one of it for the whole game, at value 0 of its own
    /// <see cref="EntityKind"/>, so anything that needs the active set addresses it without a lookup.
    /// </summary>
    public static class MapRegistries
    {
        public static readonly EntityId EntityId = EntityId.Create(EntityKindGame.MapRegistry, 0);
    }

    /// <summary>The database row the registry is stored in.</summary>
    [Table("MapRegistries")]
    public class PersistedMapRegistry : IPersistedEntity
    {
        [Key]
        [PartitionKey]
        [Required]
        [MaxLength(64)]
        [Column(TypeName = "varchar(64)")]
        public string EntityId { get; set; }

        [Required]
        [Column(TypeName = "DateTime")]
        public DateTime PersistedAt { get; set; }

        [Required] public byte[] Payload { get; set; } // TaggedSerialized<MapRegistryState>

        [Required] public int SchemaVersion { get; set; }

        [Required] public bool IsFinal { get; set; }
    }

    /// <summary>
    /// Runs the registry as a singleton service entity.
    /// </summary>
    [EntityConfig]
    public class MapRegistryConfig : PersistedEntityConfig
    {
        public override EntityKind        EntityKind           => EntityKindGame.MapRegistry;
        public override Type              EntityActorType      => typeof(MapRegistryActor);
        public override NodeSetPlacement  NodeSetPlacement     => NodeSetPlacement.Service;
        public override IShardingStrategy ShardingStrategy     => ShardingStrategies.CreateSingletonService();
        public override TimeSpan          ShardShutdownTimeout => TimeSpan.FromSeconds(30);
    }

    /// <summary>
    /// The server-side owner of every published map and of the active set a run may be started on, resident
    /// for as long as the server runs. It hosts <see cref="MapRegistryState"/>, which holds the registry's
    /// rules, and drives its rotation clock.
    /// </summary>
    public class MapRegistryActor : PersistedEntityActor<PersistedMapRegistry, MapRegistryState>
    {
        /// <summary>How often the rotation clock is looked at. A due rotation happens on the first tick after <see cref="GlobalConfig.MapRotationPeriodSeconds"/> has passed.</summary>
        static readonly TimeSpan TickInterval = TimeSpan.FromMinutes(1);

        /// <summary>The tick that drives the rotation clock. Internal to the actor, so it is no message.</summary>
        class RotateTick { public static readonly RotateTick Instance = new RotateTick(); }

        protected override TimeSpan           SnapshotInterval => TimeSpan.FromMinutes(5);
        protected override AutoShutdownPolicy ShutdownPolicy   => AutoShutdownPolicy.ShutdownNever();

        MapRegistryState _state;

        protected override async Task Initialize()
        {
            PersistedMapRegistry persisted = await MetaDatabase.Get().TryGetAsync<PersistedMapRegistry>(_entityId.ToString());
            await InitializePersisted(persisted);
        }

        protected override Task<MapRegistryState> InitializeNew()
        {
            MapRegistryState state = new MapRegistryState();
            state.Initialize(MetaTime.Now, GetSharedConfig());
            LogContents("Seeded map registry", state);
            return Task.FromResult(state);
        }

        protected override Task<MapRegistryState> RestoreFromPersisted(PersistedMapRegistry persisted)
            => Task.FromResult(DeserializePersistedPayload<MapRegistryState>(persisted.Payload, resolver: null, logicVersion: null));

        protected override Task PostLoad(MapRegistryState payload, DateTime persistedAt, TimeSpan elapsedTime)
        {
            _state = payload;

            if (Reconcile() > 0)
                SchedulePersistState();

            StartPeriodicTimer(TickInterval, RotateTick.Instance);
            return Task.CompletedTask;
        }

        protected override async Task PersistStateImpl(bool isInitial, bool isFinal)
        {
            PersistedMapRegistry persisted = new PersistedMapRegistry
            {
                EntityId      = _entityId.ToString(),
                PersistedAt   = MetaTime.Now.ToDateTime(),
                Payload       = SerializeToPersistedPayload(_state, resolver: null, logicVersion: null),
                SchemaVersion = CurrentSchemaVersion,
                IsFinal       = isFinal,
            };

            if (isInitial)
                await MetaDatabase.Get().InsertAsync(persisted).ConfigureAwait(false);
            else
                await MetaDatabase.Get().UpdateAsync(persisted).ConfigureAwait(false);
        }

        /// <summary>
        /// The rotation clock. Every tick holds the active set to what the published config can serve, and
        /// rotates one map out and one in once the period has passed.
        /// </summary>
        [CommandHandler]
        void HandleRotateTick(RotateTick _)
        {
            SharedGameConfig sharedConfig = GetSharedConfig();
            MetaTime         now          = MetaTime.Now;
            bool             changed      = false;

            // A registry whose seeding found no map the scripted runners could finish has nothing in it yet,
            // and tries again against whatever config is published now.
            if (!_state.IsInitialized)
            {
                _state.Initialize(now, sharedConfig);
                LogContents("Seeded map registry", _state);
                changed = _state.IsInitialized;
            }

            changed |= Reconcile() > 0;

            if (_state.IsRotationDue(now, RotationPeriodOf(sharedConfig)))
            {
                (CourseId outgoing, CourseId incoming) = _state.Rotate(now, sharedConfig);
                if (incoming == null)
                {
                    _log.Warning("Map rotation is due, but no candidate passed the publication gate. The active set is unchanged: {ActiveMaps}", string.Join(", ", _state.ActiveMapIds));
                }
                else
                {
                    _log.Info("Rotated the map registry: {Outgoing} left, {Incoming} entered. Active: {ActiveMaps}", outgoing?.Value ?? "nothing", incoming, string.Join(", ", _state.ActiveMapIds));
                    changed = true;
                }
            }

            if (changed)
                SchedulePersistState();
        }

        /// <summary>The maps a run may be started on right now.</summary>
        [EntityAskHandler]
        public MapRegistryActiveSetResponse HandleActiveSetRequest(MapRegistryActiveSetRequest request)
            => new MapRegistryActiveSetResponse(new List<CourseId>(_state.ActiveMapIds));

        /// <summary>
        /// Hold the active set to what the published config can serve, and return how many maps that changed.
        /// </summary>
        int Reconcile()
        {
            int changed = _state.Reconcile(MetaTime.Now, GetSharedConfig());
            if (changed > 0)
                _log.Warning("Reconciled the map registry against the published config: {Changed} map(s) changed. Active: {ActiveMaps}", changed, string.Join(", ", _state.ActiveMapIds));

            if (_state.IsInitialized && _state.ActiveMapIds.Count < MapRegistryState.ActiveSetSize)
                _log.Warning("The active set holds {Count} of {Wanted} maps: the published parts compose no further map that the scripted runners can finish", _state.ActiveMapIds.Count, MapRegistryState.ActiveSetSize);

            return changed;
        }

        /// <summary>What the registry holds, for the log: every published map and which of them are active.</summary>
        void LogContents(string what, MapRegistryState state)
            => _log.Info("{What}: {MapCount} map(s) published [{Maps}], {ActiveCount} active [{ActiveMaps}]",
                what,
                state.Maps.Count,
                string.Join(", ", state.Maps.ConvertAll(entry => entry.MapId.Value)),
                state.ActiveMapIds.Count,
                string.Join(", ", state.ActiveMapIds));

        /// <summary>
        /// How long a map stays in the active set before the one that has been there longest is replaced, from
        /// the published config.
        /// </summary>
        static MetaDuration RotationPeriodOf(SharedGameConfig sharedConfig)
            => MetaDuration.FromSeconds(sharedConfig.Global.MapRotationPeriodSeconds);

        /// <summary>
        /// The shared half of the active game config, which the global state proxies publish before the shard
        /// group the registry starts in comes up.
        /// </summary>
        static SharedGameConfig GetSharedConfig()
            => (SharedGameConfig)GlobalStateProxyActor.ActiveGameConfig.Get().BaselineGameConfig.SharedConfig;
    }
}
