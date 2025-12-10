// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Game.Logic;
using Game.Logic.League;
using Metaplay.Cloud.Entity;
using Metaplay.Cloud.RuntimeOptions;
using Metaplay.Core;
using Metaplay.Core.League;
using Metaplay.Core.League.Player;
using Metaplay.Core.Model;
using Metaplay.Core.Rewards;
using Metaplay.Server.League;
using Metaplay.Server.League.Player;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Metaplay.Server.League.Player.InternalMessages;
using Metaplay.Server.MultiplayerEntity.InternalMessages;

namespace Game.Server.League
{
    public sealed class IdlerPvPDivisionActor : PlayerDivisionActorBase<IdlerPvPDivisionModel, PersistedDivision, IdlerPvPLeagueManagerOptions>
    {
        // Disable season schedule synchronization for pvp divisions.
        // Otherwise, the custom division schedule would be overwritten.
        protected override bool AutoSyncSeasonSchedule => false;

        protected override async Task SetUpModelAsync(IdlerPvPDivisionModel model, IMultiplayerEntitySetupParams setupParams)
        {
            await base.SetUpModelAsync(model, setupParams);

            // Example of division lasting a different amount of time than the league.
            model.StartsAt           = MetaTime.Now   + LeagueManagerOptions.DivisionAcceptPlayersPeriod.RoughLowerEstimatedDuration();
            model.EndsAt             = model.StartsAt + LeagueManagerOptions.DivisionRunDuration.RoughLowerEstimatedDuration();
            model.EndingSoonStartsAt = model.EndsAt   - LeagueManagerOptions.DivisionEndingSoonPeriod.RoughLowerEstimatedDuration();
        }

        protected override Task GameOnDivisionParticipantJoined(InternalPlayerDivisionJoinOrUpdateAvatarRequest request)
        {
            return base.GameOnDivisionParticipantJoined(request);
        }

        protected override IDivisionRewards CalculateDivisionRewardsForParticipant(int participantIdx)
        {
            if (!Model.Participants.TryGetValue(participantIdx, out IdlerPvPDivisionParticipantState state))
                return null;

            // No rewards for players who did nothing.
            if (state.DivisionScore.NumPvPPoints == 0)
                return null;

            // Could get these from GameConfig
            int baseGemsReward = 10;
            int actualGemsReward;

            if (state.SortOrderIndex == 0) // First player gets triple reward
                actualGemsReward = baseGemsReward * 3;
            else if (state.SortOrderIndex < 3) // 2nd and 3rd get double reward
                actualGemsReward = baseGemsReward * 2;
            else
                actualGemsReward = baseGemsReward;

            return new DivisionPlayerRewardsBase.Default(
                new List<MetaPlayerRewardBase>
                {
                    new RewardGems(actualGemsReward),
                });
        }

        protected override IDivisionHistoryEntry GetDivisionHistoryEntryForPlayer(int participantIdx, IDivisionRewards resolvedRewards)
        {
            if (!Model.Participants.TryGetValue(participantIdx, out IdlerPvPDivisionParticipantState state))
                return null;

            return new IdlerPvPDivisionHistoryEntry(
                _entityId,
                Model.DivisionIndex,
                resolvedRewards,
                state.DivisionScore,
                state.SortOrderIndex);
        }

        /// <inheritdoc />
        protected override IDivisionParticipantConclusionResult GetParticipantResult(int participantIdx)
        {
            if (!Model.Participants.TryGetValue(participantIdx, out IdlerPvPDivisionParticipantState state))
                return null;

            return new IdlerPvPDivisionConclusionResult(
                state.ParticipantId,
                state.PlayerAvatar,
                state.SortOrderIndex,
                Model.Participants.Count,
                state.DivisionScore);
        }
    }
}
