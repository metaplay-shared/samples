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

namespace Game.Server.CourseGhosts
{
    /// <summary>
    /// Which entity owns a course's ghost pool. The id is derived from the course id, so any actor that holds
    /// a <see cref="CourseId"/> can address the pool without a lookup table, and a course always resolves to
    /// the same entity.
    /// </summary>
    public static class CourseGhostPools
    {
        public static EntityId EntityIdFor(CourseId courseId)
            => EntityId.Create(EntityKindGame.CourseGhostPool, CourseIdHash.Of(courseId) & EntityId.ValueMask);
    }

    /// <summary>The database row one course's pool is stored in.</summary>
    [Table("CourseGhostPools")]
    public class PersistedCourseGhostPool : IPersistedEntity
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

        [Required] public byte[] Payload { get; set; } // TaggedSerialized<CourseGhostPoolState>

        [Required] public int SchemaVersion { get; set; }

        [Required] public bool IsFinal { get; set; }
    }

    [EntityConfig]
    public class CourseGhostPoolConfig : PersistedEntityConfig
    {
        public override EntityKind        EntityKind           => EntityKindGame.CourseGhostPool;
        public override Type              EntityActorType      => typeof(CourseGhostPoolActor);
        public override NodeSetPlacement  NodeSetPlacement     => NodeSetPlacement.Logic;
        public override IShardingStrategy ShardingStrategy     => ShardingStrategies.CreateStaticSharded();
        public override TimeSpan          ShardShutdownTimeout => TimeSpan.FromSeconds(30);
    }

    /// <summary>
    /// The server-side owner of one course's ghost pool (<see cref="CourseGhostPoolState"/>): it persists the
    /// state, seeds it from the published config, and answers lineup requests and accepted runs.
    /// </summary>
    public class CourseGhostPoolActor : PersistedEntityActor<PersistedCourseGhostPool, CourseGhostPoolState>
    {
        /// <summary>How long the pool stays resident after the last ask, so a busy course is not reloaded per run.</summary>
        static readonly TimeSpan WakelockDuration = TimeSpan.FromMinutes(10);

        protected override TimeSpan           SnapshotInterval => TimeSpan.FromMinutes(5);
        protected override AutoShutdownPolicy ShutdownPolicy   => AutoShutdownPolicy.ShutdownAfterSubscribersGone(lingerDuration: TimeSpan.FromMinutes(1), initialWaitDuration: WakelockDuration);

        CourseGhostPoolState _state;
        Wakelock             _wakelock;

        protected override async Task Initialize()
        {
            PersistedCourseGhostPool persisted = await MetaDatabase.Get().TryGetAsync<PersistedCourseGhostPool>(_entityId.ToString());
            await InitializePersisted(persisted);
        }

        protected override Task<CourseGhostPoolState> InitializeNew()
        {
            // The pool cannot seed itself here: the entity is named after a hash of the course id, so which
            // course this is arrives with the first ask. Until then the state is empty and holds no par.
            return Task.FromResult(new CourseGhostPoolState());
        }

        protected override Task<CourseGhostPoolState> RestoreFromPersisted(PersistedCourseGhostPool persisted)
            => Task.FromResult(DeserializePersistedPayload<CourseGhostPoolState>(persisted.Payload, resolver: null, logicVersion: null));

        protected override Task PostLoad(CourseGhostPoolState payload, DateTime persistedAt, TimeSpan elapsedTime)
        {
            _state = payload;
            return Task.CompletedTask;
        }

        protected override async Task PersistStateImpl(bool isInitial, bool isFinal)
        {
            PersistedCourseGhostPool persisted = new PersistedCourseGhostPool
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

        [EntityAskHandler]
        public CourseGhostLineupResponse HandleLineupRequest(CourseGhostLineupRequest request)
        {
            StayAwake();

            if (!EnsureSeeded(request.CourseId))
                return new CourseGhostLineupResponse(new List<TimeTrialGhost>());

            List<TimeTrialGhost> lineup = _state.SelectLineup(request.PlayerId, request.PlayerBestMs, request.MaxGhosts, RandomPCG.CreateNew());
            return new CourseGhostLineupResponse(lineup);
        }

        [EntityAskHandler]
        public CourseGhostRunAcceptedResult HandleRunAccepted(CourseGhostRunAccepted request)
        {
            StayAwake();

            if (!EnsureSeeded(request.CourseId))
                return new CourseGhostRunAcceptedResult(null);

            _state.AcceptRun(
                request.PlayerId,
                request.DisplayName,
                request.FinishTimeMs,
                request.IsNewBest,
                request.PreviousBestMs,
                request.Recording,
                request.GateTimesMs,
                request.ClimbEntryTimesMs,
                MetaTime.Now,
                request.SkillId);

            SchedulePersistState();

            return new CourseGhostRunAcceptedResult(_state.StandingFor(request.FinishTimeMs));
        }

        /// <summary>
        /// Makes sure this pool belongs to <paramref name="courseId"/> and is seeded under the current ruleset.
        /// Returns false when it cannot be.
        /// </summary>
        bool EnsureSeeded(CourseId courseId)
        {
            if (courseId == null)
                return false;

            if (CourseGhostPools.EntityIdFor(courseId) != _entityId)
            {
                _log.Warning("Ghost pool {EntityId} asked about {Course}, which belongs to another pool", _entityId, courseId);
                return false;
            }

            SharedGameConfig sharedConfig = (SharedGameConfig)GlobalStateProxyActor.ActiveGameConfig.Get().BaselineGameConfig.SharedConfig;

            // A recording only means anything under the ruleset it was made against, so a ruleset bump reseeds
            // the pool and starts the course's results over.
            if (_state.IsSeeded && _state.CourseId == courseId && _state.RulesetVersion == TimeTrialConstants.RulesetVersion)
                return true;

            CourseDefinition course = CourseResolver.TryResolve(courseId, sharedConfig);
            if (course == null)
            {
                _log.Warning("Ghost pool asked about {Course}, which the published config does not resolve", courseId);
                return false;
            }

            if (!_state.Seed(course, sharedConfig.TimeTrialRules))
            {
                _log.Error("Course {Course} has no seed opponents: a scripted runner failed to finish it, so no results are kept for it", courseId);
                return false;
            }

            _log.Info("Seeded ghost pool for {Course} with par {ParMs}ms, {Entries} opponents and a house field of {HouseCount} times", courseId, _state.ParTimeMs, _state.EntryCount, _state.Distribution.HouseCount);
            SchedulePersistState();
            return true;
        }

        /// <summary>Hold the pool open for a while after an ask, so a course being played is not reloaded per run.</summary>
        void StayAwake()
        {
            try
            {
                _wakelock?.Dispose();
                _wakelock = CreateWakelock(WakelockDuration);
            }
            catch (EntityAlreadyShuttingDownException)
            {
                // The ask is still answered from the state in hand; the next one wakes the pool again.
                _wakelock = null;
            }
        }
    }
}
