using Game.Logic;
using Metaplay.Cloud.Entity;
using Metaplay.Core;
using Metaplay.Core.League;
using Metaplay.Core.League.Player;
using Metaplay.Core.Model;
using Metaplay.Server.League;
using Metaplay.Server.League.Player;
using Metaplay.Server.MultiplayerEntity;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace Game.Server.Tournament
{
    /// <summary>
    /// The database row of one tournament group (an SDK division). The payload is the whole model: the human
    /// participants, their contributions, and the season's schedule. Bots are not stored, because they are
    /// derived from the group (<see cref="TournamentBots"/>).
    /// </summary>
    [Table("Divisions")]
    public class PersistedTournamentDivision : PersistedDivisionBase
    {
    }

    [EntityConfig]
    public class TournamentDivisionEntityConfig : DivisionEntityConfigBase
    {
    }

    /// <summary>
    /// One tournament group for one season (<c>docs/seasonal-tournament.md</c>).
    /// <para>
    /// The SDK handles the schedule, participants, avatars, score events and conclusion. This class adds each
    /// participant's placement among the humans and the derived bots, and the reward for that placement
    /// (<see cref="CalculateDivisionRewardsForParticipant"/>).
    /// </para>
    /// </summary>
    public class TournamentDivisionActor : PlayerDivisionActorBase<TournamentDivisionModel, PersistedTournamentDivision, TournamentOptions>
    {
        /// <summary>
        /// The participant info shown for a standings row. The SDK's default implementation accepts only
        /// <see cref="PlayerDivisionAvatarBase.Default"/> and throws on any other avatar type, so a game with its
        /// own avatar type must override it.
        /// </summary>
        protected override DivisionEventParticipantInfo GetParticipantInfo(int participantIdx, EntityId participantId, PlayerDivisionAvatarBase avatar)
        {
            string displayName = (avatar as TournamentAvatar)?.Identity?.DisplayName ?? "?";
            return new DivisionEventParticipantInfo(participantIdx, participantId, displayName);
        }

        /// <summary>
        /// Computes one participant's result when the group concludes: their placement among the humans
        /// <i>and</i> the derived bots, and the reward for that placement from the currently active reward table.
        /// <para>
        /// <b>It is computed here, not when the player claims</b>, because the player may not log in for days,
        /// and a config publish in the meantime must not change the reward. The claim reads the stored reward.
        /// </para>
        /// </summary>
        protected override IDivisionRewards CalculateDivisionRewardsForParticipant(int participantIndex)
        {
            List<TournamentEntrant> ranked    = Model.Standings(Model.EndsAt);
            int                     placement = TournamentStandings.PlacementOf(ranked, participantIndex);

            int wins          = 0;
            int scoredMatches = 0;
            if (Model.Participants.TryGetValue(participantIndex, out TournamentParticipantState participant) && participant.PlayerContribution != null)
            {
                wins          = participant.PlayerContribution.Wins;
                scoredMatches = participant.PlayerContribution.ScoredMatches;
            }

            TournamentRewardTableInfo table = Model.SharedGameConfig?.Global?.ActiveTournamentRewardTable?.Ref;
            TournamentPlacementInfo   band  = table?.BandFor(placement);

            return new TournamentDivisionRewards(
                placement,
                wins,
                scoredMatches,
                humanCount: Model.Participants.Count,
                groupSize:  ranked.Count,
                reward:     band?.Reward,
                rewardCosmetic: band?.Cosmetic?.Ref?.Id,
                rewardTable:    table?.Id);
        }

        /// <summary>
        /// Converts the result resolved at conclusion into the history entry stored on the player's model until
        /// they claim the reward.
        /// <para>
        /// The SDK's claim path is not used (<see cref="TournamentDivisionRewards"/> explains why), so the reward
        /// is stored on the entry as a plain bundle and <c>PlayerTournamentPlacementClaim</c> pays it out.
        /// </para>
        /// </summary>
        protected override IDivisionHistoryEntry GetDivisionHistoryEntryForPlayer(int participantIndex, IDivisionRewards resolvedRewards)
        {
            // Rewards are normally resolved for every participant when the group concludes. The fallback covers a
            // participant that the resolution skipped, one with no entity id, and uses the currently active
            // reward table instead of returning nothing.
            TournamentDivisionRewards resolved = resolvedRewards as TournamentDivisionRewards
                ?? (TournamentDivisionRewards)CalculateDivisionRewardsForParticipant(participantIndex);

            return new TournamentHistoryEntry(
                _entityId,
                Model.DivisionIndex,
                resolved.Placement,
                resolved.Wins,
                resolved.ScoredMatches,
                resolved.HumanCount,
                resolved.GroupSize,
                resolved.Reward,
                resolved.RewardCosmetic,
                resolved.RewardTable);
        }

        /// <summary>
        /// The result reported to the league manager for a participant whose season ended. The tournament has one
        /// rank and nobody carries over to the next season, so the result contains only the id and avatar.
        /// </summary>
        protected override IDivisionParticipantConclusionResult GetParticipantResult(int participantIndex)
        {
            EntityId participantId = Model.ServerModel.ParticipantIndexToEntityId.GetValueOrDefault(participantIndex, EntityId.None);
            Model.Participants.TryGetValue(participantIndex, out TournamentParticipantState participant);

            return new TournamentConclusionResult(participantId, participant?.PlayerAvatar);
        }
    }
}
