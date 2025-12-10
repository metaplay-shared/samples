// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Game.Logic.League;
using Metaplay.Cloud.Entity;
using Metaplay.Cloud.Sharding;
using Metaplay.Core;
using Metaplay.Core.League;
using Metaplay.Core.League.Player;
using Metaplay.Core.Model;
using Metaplay.Server.League;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Game.Logic.TypeCodes;
using Metaplay.Cloud.Application;
using Metaplay.Cloud.RuntimeOptions;
using static System.FormattableString;

namespace Game.Server.League
{
    [RuntimeOptions("Leagues", true, "Options for the leagues manager service.")]
    public class IdlerLeagueManagerOptions : LeagueManagerOptionsBase { }
    
    [MetaSerializableDerived(1)]
    [SupportedSchemaVersions(1, 1)]
    public class IdlerLeagueManagerState : LeagueManagerActorStateBase
    {
    }

    [EntityConfig]
    // Base config handles everything.
    public class IdlerLeagueManagerConfig : LeagueManagerConfigBase { }

    public class IdlerLeagueManagerRegistry : LeagueManagerRegistry
    {
        /// <inheritdoc />
        public override IReadOnlyList<LeagueInfo> LeagueInfos { get; } = new LeagueInfo[]
        {
            new LeagueInfo(
                leagueManagerId: EntityId.Create(EntityKindCloudCore.LeagueManager, 0),
                clientSlot: ClientSlotGame.IdlerLeague, 
                participantKind: EntityKindCore.Player,
                managerActorType: typeof(IdlerLeagueManagerActor),
                optionsType: typeof(IdlerLeagueManagerOptions),
                divisionActorType: typeof(IdlerPlayerDivisionActor)),
            new LeagueInfo(
                leagueManagerId: EntityId.Create(EntityKindCloudCore.LeagueManager, 1),
                clientSlot: ClientSlotGame.IdlerPvPLeague,
                participantKind: EntityKindCore.Player,
                managerActorType: typeof(IdlerPvPLeagueManagerActor),
                optionsType: typeof(IdlerPvPLeagueManagerOptions),
                divisionActorType: typeof(IdlerPvPDivisionActor)),
        };
    }


    public class IdlerLeagueManagerActor : LeagueManagerActorBase<IdlerLeagueManagerState, PersistedDivision, IdlerLeagueManagerOptions>
    {
        protected override EntityKind ParticipantEntityKind => EntityKindCore.Player;

        readonly int numRanks = 5;

        protected override Task<IdlerLeagueManagerState> InitializeNew()
        {
            IdlerLeagueManagerState state = new IdlerLeagueManagerState();
            return Task.FromResult(state);
        }

        protected override Task<ParticipantJoinRequestResult> SolveParticipantInitialPlacement(int currentSeason, EntityId participant, LeagueJoinRequestPayloadBase payload)
        {
            // Always start from rank 0.
            return Task.FromResult(new ParticipantJoinRequestResult(true, 0));
        }

        /// <inheritdoc />
        protected override ParticipantSeasonPlacementResult SolveLocalSeasonPlacement(int lastSeason, int nextSeason, int currentRank, IDivisionParticipantConclusionResult conclusionResult)
        {
            IdlerPlayerDivisionConclusionResult playerResult = conclusionResult as IdlerPlayerDivisionConclusionResult;

            if (playerResult == null)
                throw new ArgumentNullException(nameof(conclusionResult), $"Conclusion result was null or not castable to a {nameof(IdlerPlayerDivisionConclusionResult)}");

            // Demote / remove inactive players
            if (currentRank == 0 && playerResult.PlayerScore.NumProducerUpgrades == 0 && !RuntimeOptionsBase.IsLocalEnvironment)
                return ParticipantSeasonPlacementResult.ForRemoval();
            
            // Promote the top 2 players.
            if (currentRank < numRanks - 1 && playerResult.LeaderboardPlacementIndex <= 1)
                return ParticipantSeasonPlacementResult.ForRank(currentRank + 1);

            int leaderBoardPlacementFromBottom = playerResult.LeaderboardNumPlayers - playerResult.LeaderboardPlacementIndex - 1;

            // Demote the bottom 8 players if division was at least half full.
            if (currentRank > 0 && leaderBoardPlacementFromBottom < 8 && playerResult.LeaderboardNumPlayers > (GetRankDetails(currentRank, lastSeason).DivisionParticipantCount / 2))
                return ParticipantSeasonPlacementResult.ForRank(currentRank - 1);

            // Otherwise stay in the same rank.
            return ParticipantSeasonPlacementResult.ForRank(currentRank);
        }

        /// <inheritdoc />
        protected override Dictionary<EntityId, ParticipantSeasonPlacementResult> SolveGlobalSeasonPlacement(int lastSeason, int nextSeason, Dictionary<EntityId, GlobalRankUpParticipantData> allParticipants)
        {
            Dictionary<EntityId, ParticipantSeasonPlacementResult> results = new Dictionary<EntityId, ParticipantSeasonPlacementResult>();

            // Legend rank size is 50% of diamond players or max 100, whichever is smaller.
            int legendRankSize = Math.Min(GetRankDetails(GetSeasonDetails(nextSeason).NumRanks - 1, lastSeason).DivisionParticipantCount, (int)(allParticipants.Count * 0.5f));

            IEnumerable<(EntityId Id, GlobalRankUpParticipantData Data)> topParticipants = allParticipants.OrderByDescending(
                (x) => ((IdlerPlayerDivisionConclusionResult)x.Value.ConclusionResult).PlayerScore.NumProducerUpgrades).Select((x) => (x.Key, x.Value)).Take(legendRankSize);

            foreach ((EntityId Id, GlobalRankUpParticipantData Data) topParticipant in topParticipants)
            {
                // Top players go to legend
                results.Add(topParticipant.Id, ParticipantSeasonPlacementResult.ForRank(numRanks - 1));
            }

            foreach (KeyValuePair<EntityId, GlobalRankUpParticipantData> participant in allParticipants)
            {
                // Skip top participants
                if(results.ContainsKey(participant.Key))
                    continue;

                IdlerPlayerDivisionConclusionResult playerResult = participant.Value.ConclusionResult as IdlerPlayerDivisionConclusionResult;

                if (playerResult == null)
                    throw new NullReferenceException($"Conclusion result was null or not castable to a {nameof(IdlerPlayerDivisionConclusionResult)}");

                int leaderBoardPlacementFromBottom = playerResult.LeaderboardNumPlayers - playerResult.LeaderboardPlacementIndex - 1;

                // Demote the bottom 8 players of diamond if division was at least half full.
                if (participant.Value.CurrentRank == numRanks - 2 && leaderBoardPlacementFromBottom < 8 && playerResult.LeaderboardNumPlayers > (
                        GetRankDetails(participant.Value.CurrentRank, lastSeason).DivisionParticipantCount / 2))
                    results.Add(participant.Key, ParticipantSeasonPlacementResult.ForRank(participant.Value.CurrentRank - 1));
                else
                    results.Add(participant.Key, ParticipantSeasonPlacementResult.ForRank(numRanks - 2));
            }

            return results;
        }

        /// <inheritdoc />
        protected override LeagueRankDetails GetRankDetails(int rank, int season)
        {
            switch (rank)
            {
                case 0:
                    return new LeagueRankDetails("Bronze", "The lowest tier.", 100);
                case 1:
                    return new LeagueRankDetails("Silver", "For the best of the worst.", 50);
                case 2:
                    return new LeagueRankDetails("Gold", "Good idlers live here.", 30);
                case 3:
                    return new LeagueRankDetails("Diamond", "The best idlers.", 20);
                case 4:
                    return new LeagueRankDetails("Legend", "Only the best of the best.", 100);
                default:
                    throw new ArgumentOutOfRangeException(nameof(rank), rank, "Given rank is out of range of known ranks");
            }
        }

        /// <inheritdoc />
        protected override LeagueRankUpStrategy GetRankUpStrategy(int rank, int season)
        {
            switch (rank)
            {
                case 0:
                    return new LeagueRankUpStrategy();
                case 1:
                    return new LeagueRankUpStrategy(preferNonEmptyDivisions: true);
                case 2:
                    return new LeagueRankUpStrategy(preferNonEmptyDivisions: true);
                case 3:
                    return new LeagueRankUpStrategy(preferNonEmptyDivisions: true, rankUpMethod: LeagueRankUpMethod.Global);
                case 4:
                    return new LeagueRankUpStrategy(isSingleDivision: true, rankUpMethod: LeagueRankUpMethod.Global);
                default:
                    throw new ArgumentOutOfRangeException(nameof(rank), rank, "Given rank is out of range of known ranks");
            }
        }

        /// <inheritdoc />
        protected override LeagueSeasonDetails GetSeasonDetails(int season)
        {
            return new LeagueSeasonDetails(Invariant($"Season {season}"), "A normal idling season.", numRanks);
        }

        /// <inheritdoc />
        protected override LeagueDetails GetLeagueDetails()
        {
            return new LeagueDetails("Idling League", "Players get points based on how many producers they upgrade per season.");
        }
    }
}

