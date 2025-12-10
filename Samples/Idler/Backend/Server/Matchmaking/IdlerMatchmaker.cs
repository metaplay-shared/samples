using Game.Logic;
using Metaplay.Cloud.Entity;
using Metaplay.Cloud.RuntimeOptions;
using Metaplay.Core;
using Metaplay.Core.Model;
using Metaplay.Server.Matchmaking;
using System;
using System.Collections.Generic;
using System.Linq;
using static System.FormattableString;

namespace Game.Server.Matchmaking
{
    [RuntimeOptions("Matchmaker", isStatic: true, "Options for asynchronous matchmaker.")]
    public class MatchmakerOptions : AsyncMatchmakerOptionsBase { }

    [EntityConfig]
    public class AsyncMatchmakerConfig : AsyncMatchmakerConfigBase
    {
        /// <inheritdoc />
        public override EntityKind          EntityKind              => EntityKindGame.AsyncMatchmaker;
        /// <inheritdoc />
        public override Type                EntityActorType         => typeof(IdlerAsyncMatchmakerActor);
    }

    [MetaSerializable]
    public struct IdlerMatchmakerPlayerModel : IAsyncMatchmakerPlayerModel
    {
        [MetaMember(1)] public EntityId         PlayerId                { get; set; }
        [MetaMember(2)] public int              DefenseMmr              { get; set; }
        [MetaMember(3)] public ProducerTypeId   HighestLevelProducer    { get; set; }

        public IdlerMatchmakerPlayerModel(EntityId playerId, int defenseMmr, ProducerTypeId highestLevelProducer)
        {
            PlayerId             = playerId;
            DefenseMmr           = defenseMmr;
            HighestLevelProducer = highestLevelProducer;
        }

        /// <inheritdoc />
        public string GetDashboardSummary()
        {
            return Invariant($"MMR: {DefenseMmr}\xA0\xA0\xA0\xA0{HighestLevelProducer}");
        }

        public static int CalculatePlayerMmr(PlayerModel player)
        {
            return player.Producers.Values.Sum(producer => producer.Level);
        }

        public static ProducerTypeId GetHighestLevelProducer(PlayerModel player)
        {
            return player.Producers.Count > 0 ?
                player.Producers.MaxBy(x => x.Value.Level).Key :
                ProducerTypeId.None;
        }

        public static IdlerMatchmakerPlayerModel? TryCreateModel(PlayerModel player)
        {
            return new IdlerMatchmakerPlayerModel(
                player.PlayerId,
                CalculatePlayerMmr(player),
                GetHighestLevelProducer(player));
        }
    }

    [MetaSerializableDerived(1)]
    public class IdlerMatchmakerQuery : AsyncMatchmakerQueryBase
    {
        [MetaMember(1)] public ProducerTypeId HighestLevelProducer { get; set; }

        public IdlerMatchmakerQuery() : base() { }

        public IdlerMatchmakerQuery(EntityId attackerId, int attackMmr, ProducerTypeId highestLevelProducer) : base(attackerId, attackMmr)
        {
            HighestLevelProducer = highestLevelProducer;
        }
    }

    public class IdlerAsyncMatchmakerActor : AsyncMatchmakerActorBase<
        PlayerModel,
        IdlerMatchmakerPlayerModel,
        IdlerMatchmakerQuery,
        IdlerAsyncMatchmakerActor.IdlerAsyncMatchmakerActorState,
        MatchmakerOptions
    >
    {
        [MetaSerializableDerived(1)]
        [SupportedSchemaVersions(2, 2)]
        public class IdlerAsyncMatchmakerActorState : MatchmakerStateBase { }

        /// <inheritdoc />
        protected override string MatchmakerName => "Idler Matchmaker";
        /// <inheritdoc />
        protected override string MatchmakerDescription => "Matches players using their total amount of producers as their MMR.";

        /// <inheritdoc />
        protected override bool EnableDatabaseScan => true;

        /// <inheritdoc />
        protected override bool ReturnModelInQueryResponse => true;

        /// <inheritdoc />
        protected override BucketFillLevelThreshold PlayerIgnoreUpdateInsertThreshold => BucketFillLevelThreshold.HighPopulation;

        /// <inheritdoc />
        protected override IdlerMatchmakerPlayerModel? TryCreateModel(PlayerModel model)
            => IdlerMatchmakerPlayerModel.TryCreateModel(model);

        /// <inheritdoc />
        protected override bool CheckPossibleMatchForQuery(IdlerMatchmakerQuery query, in IdlerMatchmakerPlayerModel player, int numRetries, out float quality)
        {
            quality = 1000 - Math.Abs(player.DefenseMmr - query.AttackMmr);
            return true;
        }

        /// <inheritdoc />
        protected override IEnumerable<IAsyncMatchmakerBucketingStrategy<IdlerMatchmakerPlayerModel, IdlerMatchmakerQuery>> InitializeAdditionalBucketingStrategies()
        {
            yield return new IdlerMatchmakingBucketingStrategy();
        }
    }
}

