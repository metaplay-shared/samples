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
    [MetaSerializableDerived(2)]
    public class IdlerPvPDivisionScore : IDivisionScore, IDivisionContribution
    {
        [MetaMember(1)] public int      NumPvPPoints;
        [MetaMember(2)] public MetaTime LastActionAt;

        int IDivisionScore.CompareTo(IDivisionScore untypedOther)
        {
            IdlerPvPDivisionScore other = (IdlerPvPDivisionScore)untypedOther;

            if (NumPvPPoints < other.NumPvPPoints)
                return -1;
            if (NumPvPPoints > other.NumPvPPoints)
                return +1;

            if (LastActionAt > other.LastActionAt)
                return +1;
            if (LastActionAt < other.LastActionAt)
                return -1;

            return 0;
        }
    }

    /// <summary>
    /// Player score event example for when player upgrades a producer.
    /// </summary>
    [MetaSerializableDerived(2)]
    public class IdlerPvPDivisionScoreEvent : DivisionScoreEventBase<IdlerPvPDivisionScore>
    {
        [MetaMember(1)] public MetaTime EventAt { get; set; }

        IdlerPvPDivisionScoreEvent() { }
        
        public IdlerPvPDivisionScoreEvent(MetaTime eventAt)
        {
            EventAt = eventAt;
        }

        public override void AccumulateToContribution(IdlerPvPDivisionScore contribution)
        {
            contribution.NumPvPPoints++;
            contribution.LastActionAt = EventAt;
        }
    }

    /// <summary>
    /// Example per-participant state. We don't add anything here.
    /// </summary>
    [MetaSerializableDerived(2)]
    public class IdlerPvPDivisionParticipantState : PlayerDivisionParticipantStateBase<IdlerPvPDivisionScore, IdlerPvPDivisionScore, PlayerDivisionAvatarBase.Default>
    {
        /// <inheritdoc />
        public override string ParticipantInfo => Invariant($"Score: {PlayerContribution.NumPvPPoints}");
    }

    /// <summary>
    /// Example per-division state. We don't add anything here.
    /// </summary>
    [MetaSerializableDerived(4)]
    [SupportedSchemaVersions(1, 1)]
    public class IdlerPvPDivisionModel : PlayerDivisionModelBase<IdlerPvPDivisionModel, IdlerPvPDivisionParticipantState, IdlerPvPDivisionScore, PlayerDivisionAvatarBase.Default>
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

        public override IdlerPvPDivisionScore ComputeScore(int participantIndex)
        {
            // No special score computation logic.
            return Participants[participantIndex].PlayerContribution;
        }
    }
}
