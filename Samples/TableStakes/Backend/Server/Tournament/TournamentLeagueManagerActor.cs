using Game.Logic;
using Metaplay.Cloud.Entity;
using Metaplay.Core;
using Metaplay.Core.Client;
using Metaplay.Core.League;
using Metaplay.Core.Model;
using Metaplay.Server.League;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Game.Server.Tournament
{
    /// <summary>
    /// Registers the game's only league, the seasonal tournament (<c>docs/seasonal-tournament.md</c>).
    /// <para>
    /// The SDK identifies a league manager by number and looks up its actor, options and division types here.
    /// </para>
    /// </summary>
    public class TableStakesLeagueRegistry : LeagueManagerRegistry
    {
        public override IReadOnlyList<LeagueInfo> LeagueInfos { get; } = new List<LeagueInfo>
        {
            new LeagueInfo(
                leagueManagerId: EntityId.Create(EntityKindCloudCore.LeagueManager, TournamentRules.LeagueId),
                clientSlot:      ClientSlotGame.Tournament,
                participantKind: EntityKindCore.Player,
                managerActorType: typeof(TournamentLeagueManagerActor),
                optionsType:      typeof(TournamentOptions),
                divisionActorType: typeof(TournamentDivisionActor)),
        };
    }

    [EntityConfig]
    public class TournamentLeagueManagerConfig : LeagueManagerConfigBase
    {
    }

    /// <summary>
    /// The league manager's persisted state. It adds nothing to the SDK's base state, because the tournament
    /// has one rank and no promotion.
    /// </summary>
    [MetaSerializableDerived(200)]
    [SupportedSchemaVersions(1, 1)]
    public class TournamentLeagueManagerState : LeagueManagerActorStateBase
    {
        public TournamentLeagueManagerState() { }
    }

    /// <summary>
    /// Runs the tournament seasons: starts each season on the schedule in <see cref="TournamentOptions"/>,
    /// places joining players into groups, and ends each season.
    /// <para>
    /// <b>Humans are grouped together.</b> The SDK assigns a joining participant to the lowest-numbered group that
    /// is not full. Bots are not participants and take no seats. <see cref="TournamentBots"/> fills every seat no
    /// person holds, so a joining person replaces the lowest-ranked bot and the other bots keep their places.
    /// </para>
    /// </summary>
    public class TournamentLeagueManagerActor : LeagueManagerActorBase<TournamentLeagueManagerState, PersistedTournamentDivision, TournamentOptions>
    {
        protected override EntityKind ParticipantEntityKind => EntityKindCore.Player;

        public TournamentLeagueManagerActor()
        {
            LeagueId = TournamentRules.LeagueId;
        }

        protected override Task<TournamentLeagueManagerState> InitializeNew() =>
            Task.FromResult(new TournamentLeagueManagerState());

        /// <summary>
        /// Every player may join, at the only rank. There is no rating, requirement or seeding.
        /// </summary>
        protected override Task<ParticipantJoinRequestResult> SolveParticipantInitialPlacement(int currentSeason, EntityId participant, LeagueJoinRequestPayloadBase payload) =>
            Task.FromResult(new ParticipantJoinRequestResult(canJoin: true, startingRank: TournamentRules.OnlyRank));

        /// <summary>
        /// Removes every participant at the end of the season, because players join each season explicitly and
        /// there are no ranks to be promoted to. The concluded season's result and unclaimed reward are stored on
        /// the player's model, so removal from the group loses nothing.
        /// </summary>
        protected override ParticipantSeasonPlacementResult SolveLocalSeasonPlacement(int lastSeason, int nextSeason, int currentRank, IDivisionParticipantConclusionResult conclusionResult) =>
            ParticipantSeasonPlacementResult.ForRemoval();

        /// <summary>
        /// The single rank, with <see cref="TournamentRules.GroupSize"/> seats per group. These names are shown to
        /// operators in the LiveOps Dashboard. Players see only the name "Seasonal Tournament", in the client.
        /// </summary>
        protected override LeagueRankDetails GetRankDetails(int rank, int season) =>
            new LeagueRankDetails("Tournament group", "One tournament group of twenty seats. Seats no player holds are filled by derived computer players.", TournamentRules.GroupSize);

        protected override LeagueSeasonDetails GetSeasonDetails(int season) =>
            new LeagueSeasonDetails($"Season {season}", "One week of the seasonal tournament.", numRanks: 1);

        protected override LeagueDetails GetLeagueDetails() =>
            new LeagueDetails("Seasonal Tournament", "The game's one competitive feature: a weekly points race in groups of twenty.");
    }
}
