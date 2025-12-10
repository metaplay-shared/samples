// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Cloud.Persistence;
using Metaplay.Cloud.RuntimeOptions;
using Metaplay.Core;
using Metaplay.Core.League;
using Metaplay.Core.Model;
using Metaplay.Server.Database;
using Metaplay.Server.League;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Metaplay.Core.Schedule;
using static System.FormattableString;

namespace Game.Server.League
{
    [RuntimeOptions("PvPLeagues", true, "Options for the leagues manager service.")]
    public class IdlerPvPLeagueManagerOptions : LeagueManagerOptionsBase
    {
        public MetaCalendarPeriod DivisionAcceptPlayersPeriod { get; set; } = new MetaCalendarPeriod(0, 0, 0, 0, 5, 0);
        public MetaCalendarPeriod DivisionRunDuration         { get; set; } = new MetaCalendarPeriod(0, 0, 0, 1, 0, 0);
        public MetaCalendarPeriod DivisionEndingSoonPeriod    { get; set; } = new MetaCalendarPeriod(0, 0, 0, 0, 10, 0);
    }
    
    [MetaSerializableDerived(2)]
    [SupportedSchemaVersions(1, 1)]
    public class IdlerPvPLeagueManagerState : LeagueManagerActorStateBase
    {
        [MetaMember(100)] public Queue<(MetaTime, DivisionIndex)> ExpiringDivisions { get; set; } = new Queue<(MetaTime, DivisionIndex)>();
    }
    
    public class IdlerPvPLeagueManagerActor : LeagueManagerActorBase<IdlerPvPLeagueManagerState, PersistedDivision, IdlerPvPLeagueManagerOptions>
    {
        protected override EntityKind ParticipantEntityKind => EntityKindCore.Player;

        readonly int numRanks = 3;

        protected override Task<IdlerPvPLeagueManagerState> InitializeNew()
        {
            IdlerPvPLeagueManagerState state = new IdlerPvPLeagueManagerState();
            return Task.FromResult(state);
        }

        protected override Task<ParticipantJoinRequestResult> SolveParticipantInitialPlacement(int currentSeason, EntityId participant, LeagueJoinRequestPayloadBase payload)
        {
            // Always start from rank 0.
            return Task.FromResult(new ParticipantJoinRequestResult(true, 0));
        }
        
        // Override the default migration behaviour to only count participants.
        // We don't clear the association data so that players can finish their current division.
        protected override async Task<SeasonMigrationResult> MigrateParticipantsToCurrentSeason()
        {
            int lastSeasonRanks = 0;
            int lastSeasonId    = -1;
            
            int estimatedParticipantsToEnumerate = 0;
            int participantsEnumerated = 0;
            
            if (State.HistoricSeasons.Count > 0)
            {
                lastSeasonRanks = State.HistoricSeasons[^1].Ranks.Count;
                lastSeasonId    = State.HistoricSeasons[^1].SeasonId;
                estimatedParticipantsToEnumerate = State.HistoricSeasons[^1].Ranks
                    .Sum(r => r.RankDetails.DivisionParticipantCount * r.NumDivisions);
            }

            SeasonMigrationResult migrationResult = new SeasonMigrationResult(lastSeasonRanks);
            
            UpdateSeasonMigrationProgress(0, "Counting last season participants");

            // Gather data on rank participant counts
            await foreach (PersistedParticipantDivisionAssociation participant in EnumerateAllParticipants())
            {
                DivisionIndex division = DivisionIndex.FromEntityId(EntityId.ParseFromString(participant.DivisionId));
                
                if (division.Season == lastSeasonId)
                {
                    migrationResult.LastSeasonRankResults[division.Rank].NumParticipants++;
                    migrationResult.LastSeasonRankResults[division.Rank].NumDropped++;

                    migrationResult.LastSeasonTotalParticipants++;
                    
                    participantsEnumerated++;
                    UpdateSeasonMigrationProgress((float)participantsEnumerated / estimatedParticipantsToEnumerate);
                }
            }
            
            // Remove all associations from the current season.
            // We don't do this due to divisions lasting longer than the season.
            // Players need to use the Reassign method in the league integration to move to the new season.
            // int affected = await MetaDatabase.Get().RemoveAllLeagueParticipantDivisionAssociationsAsync(_entityId);

            migrationResult.ParticipantsDropped = participantsEnumerated;

            return migrationResult;
        }

        /// <inheritdoc />
        protected override ParticipantSeasonPlacementResult SolveLocalSeasonPlacement(int lastSeason, int nextSeason, int currentRank, IDivisionParticipantConclusionResult conclusionResult)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc />
        protected override Dictionary<EntityId, ParticipantSeasonPlacementResult> SolveGlobalSeasonPlacement(int lastSeason, int nextSeason, Dictionary<EntityId, GlobalRankUpParticipantData> allParticipants)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc />
        protected override LeagueRankDetails GetRankDetails(int rank, int season)
        {
            switch (rank)
            {
                case 0:
                    return new LeagueRankDetails("Wood", "Wood rank.", 100);
                case 1:
                    return new LeagueRankDetails("Iron", "Mid-level players.", 50);
                case 2:
                    return new LeagueRankDetails("Platinum", "For platinum-tier players.", 20);
                default:
                    throw new ArgumentOutOfRangeException(nameof(rank), "Given rank is out of range of known ranks");
            }
        }

        /// <inheritdoc />
        protected override LeagueSeasonDetails GetSeasonDetails(int season)
        {
            return new LeagueSeasonDetails(Invariant($"Season {season}"), "A normal pvp season.", numRanks);
        }

        /// <inheritdoc />
        protected override LeagueDetails GetLeagueDetails()
        {
            return new LeagueDetails("Idler PvP League", "Players get points based on how many producers they upgrade per season.");
        }

        protected override void OnNewDivisionCreated(DivisionIndex newDivision, bool isSeasonMigration)
        {
            MetaTime divisionExpiresAt = MetaTime.Now + Options.DivisionAcceptPlayersPeriod.RoughLowerEstimatedDuration();
            State.ExpiringDivisions.Enqueue((divisionExpiresAt, newDivision));
            _log.Information("Division {Division} created, expires at {ExpiresAt}", newDivision, divisionExpiresAt);
        }

        protected override Task OnDivisionsRecoveredAfterCrash(IEnumerable<DivisionIndex> divisions)
        {
            // Assume all divisions are expired.
            // We can't easily recover expired
            for (int rank = 0; rank < State.CurrentSeason.Ranks.Count; rank++)
            {
                int maxParticipants = GetRankDetails(rank, State.CurrentSeason.SeasonId).DivisionParticipantCount;
                
                for (int division = 0; division < State.CurrentSeason.Ranks[rank].NumDivisions; division++)
                {
                    _participantCountState.SetParticipantCount(rank, division, maxParticipants);
                }
            }
            
            return Task.CompletedTask;
        }

        protected override void OnBeforeAssignParticipantToRank(EntityId participant, int startingRank)
        {
            while (State.ExpiringDivisions.TryPeek(out (MetaTime expiresAt, DivisionIndex division) result))
            {
                if (result.division.Season != State.CurrentSeason.SeasonId)
                {
                    State.ExpiringDivisions.Dequeue();
                    continue;
                }

                if (result.expiresAt <= MetaTime.Now)
                {
                    _log.Information("Division {Division} expired.", result.division);
                    State.ExpiringDivisions.Dequeue();
                    _participantCountState.SetParticipantCount(result.division.Rank, result.division.Division, 
                        GetRankDetails(startingRank, State.CurrentSeason.SeasonId).DivisionParticipantCount);
                    continue;
                }
                
                break;
            }
        }
    }
}

