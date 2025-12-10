// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using Metaplay.Core.League;
using Metaplay.Core.League.Player;
using Metaplay.Core.Model;
using System;
using static System.FormattableString;

namespace Game.Logic.League
{
    /// <summary>
    /// Example score is the number of producer upgrades. There is no special score computations logic, so let's
    /// use the same type as the Contribution.
    /// </summary>
    [MetaSerializableDerived(1)]
    public class IdlerPlayerDivisionScore : IDivisionScore, IDivisionContribution
    {
        [MetaMember(1)] public int      NumProducerUpgrades;
        [MetaMember(2)] public MetaTime LastActionAt;

        int IDivisionScore.CompareTo(IDivisionScore untypedOther)
        {
            IdlerPlayerDivisionScore other = (IdlerPlayerDivisionScore)untypedOther;

            if (NumProducerUpgrades < other.NumProducerUpgrades)
                return -1;
            if (NumProducerUpgrades > other.NumProducerUpgrades)
                return +1;

            if (LastActionAt < other.LastActionAt)
                return +1;
            if (LastActionAt > other.LastActionAt)
                return -1;

            return 0;
        }
    }

    /// <summary>
    /// Player score event example for when player upgrades a producer.
    /// </summary>
    [MetaSerializableDerived(1)]
    public class IdlerPlayerDivisionProducerScoreEvent : DivisionScoreEventBase<IdlerPlayerDivisionScore>
    {
        [MetaMember(1)] public MetaTime       EventAt  { get; set; }
        [MetaMember(2)] public ProducerTypeId Producer { get; set; }
        [MetaMember(3)] public int            NewLevel { get; set; }

        IdlerPlayerDivisionProducerScoreEvent() { }
        public IdlerPlayerDivisionProducerScoreEvent(MetaTime eventAt, ProducerTypeId producer, int newLevel)
        {
            EventAt  = eventAt;
            Producer = producer;
            NewLevel = newLevel;
        }

        public override void AccumulateToContribution(IdlerPlayerDivisionScore contribution)
        {
            contribution.NumProducerUpgrades++;
            contribution.LastActionAt = EventAt;
        }
    }

    /// <summary>
    /// Example per-participant state. We don't add anything here.
    /// </summary>
    [MetaSerializableDerived(1)]
    public class IdlerPlayerDivisionParticipantState : PlayerDivisionParticipantStateBase<IdlerPlayerDivisionScore, IdlerPlayerDivisionScore, PlayerDivisionAvatarBase.Default>
    {
        /// <inheritdoc />
        public override string ParticipantInfo => Invariant($"Score: {PlayerContribution.NumProducerUpgrades}");
    }

    /// <summary>
    /// Example per-division state. We don't add anything here.
    /// </summary>
    [MetaSerializableDerived(3)]
    [SupportedSchemaVersions(1,2)]
    public class IdlerPlayerDivisionModel : PlayerDivisionModelBase<IdlerPlayerDivisionModel, IdlerPlayerDivisionParticipantState, IdlerPlayerDivisionScore, PlayerDivisionAvatarBase.Default>
    {
        public override int TicksPerSecond => 1;

        public override void OnTick()
        {
            // Nothing
        }

        public override void OnFastForwardTime(MetaDuration elapsedTime)
        {
            // Nothing
        }

        public override IdlerPlayerDivisionScore ComputeScore(int participantIndex)
        {
            // No special score computation logic.
            return Participants[participantIndex].PlayerContribution;
        }

        [MigrationFromVersion(1)]
        void MigrateParticipantData()
        {
            #pragma warning disable CS0618
            if (ServerModel != null)
            {
                foreach ((EntityId participantId, IdlerPlayerDivisionParticipantState participantState) in LegacyParticipants)
                {
                    // Set participant index
                    participantState.ParticipantIndex = NextParticipantIdx++;
                    participantState.ParticipantId    = participantId;
                    // Update ServerModel
                    ServerModel.ParticipantIndexToEntityId.Add(participantState.ParticipantIndex, participantId);
                    Participants.Add(participantState.ParticipantIndex, participantState);
                }
                LegacyParticipants = null;
            }
            else
                throw new InvalidOperationException("ServerModel is null. Cannot migrate participant data.");
            #pragma warning restore CS0618
        }
    }
}
