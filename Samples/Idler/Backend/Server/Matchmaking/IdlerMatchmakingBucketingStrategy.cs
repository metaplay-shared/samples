// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Game.Logic;
using Metaplay.Core.Model;
using Metaplay.Server.Matchmaking;

namespace Game.Server.Matchmaking
{
    public class IdlerMatchmakingBucketingStrategy : AsyncMatchmakerBucketingStrategyBase<
        IdlerMatchmakingBucketingStrategy.IdlerBucketingLabel,
        IdlerMatchmakerPlayerModel,
        IdlerMatchmakerQuery>
    {
        [MetaSerializableDerived(1)]
        public class IdlerBucketingLabel : IDistinctBucketLabel<IdlerBucketingLabel>
        {
            [MetaMember(1)] public ProducerTypeId ProducerType { get; private set; }

            public string DashboardLabel => ProducerType.ToString();

            IdlerBucketingLabel() { }

            public IdlerBucketingLabel(ProducerTypeId producerType)
            {
                ProducerType = producerType;
            }

            public bool Equals(IdlerBucketingLabel other)
            {
                if (ReferenceEquals(null, other))
                    return false;

                return ProducerType.Equals(other.ProducerType);
            }

            public override bool Equals(object obj)
            {
                if (ReferenceEquals(null, obj))
                    return false;
                if (ReferenceEquals(this, obj))
                    return true;
                if (obj.GetType() != this.GetType())
                    return false;

                return Equals((IdlerBucketingLabel)obj);
            }

            public override int GetHashCode()
            {
                return (ProducerType != null ? ProducerType.GetHashCode() : 0);
            }
        }

        public override string LabelDashboardName => "Highest Level Producer";
        
        public override bool IsHardRequirement => false;

        public override IdlerBucketingLabel GetBucketLabel(IdlerMatchmakerPlayerModel model)
        {
            return new IdlerBucketingLabel(model.HighestLevelProducer);
        }

        public override IdlerBucketingLabel GetBucketLabel(IdlerMatchmakerQuery query)
        {
            return new IdlerBucketingLabel(query.HighestLevelProducer);
        }
    }
}
