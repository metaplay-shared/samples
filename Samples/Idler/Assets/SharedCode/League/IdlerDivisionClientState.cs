// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using Metaplay.Core.League;
using Metaplay.Core.League.Player;
using Metaplay.Core.Model;

namespace Game.Logic.League
{
    /// <summary>
    /// An example conclusion result for divisions.
    /// </summary>
    [MetaSerializableDerived(1)]
    public class IdlerPlayerDivisionConclusionResult : PlayerDivisionParticipantConclusionResultBase<PlayerDivisionAvatarBase.Default>
    {
        [MetaMember(1)] public int                      LeaderboardPlacementIndex { get; private set; }
        [MetaMember(2)] public int                      LeaderboardNumPlayers     { get; private set; }
        [MetaMember(3)] public IdlerPlayerDivisionScore PlayerScore               { get; private set; }

        public IdlerPlayerDivisionConclusionResult(EntityId participantId, PlayerDivisionAvatarBase.Default avatar, int leaderboardPlacementIndex,
            int leaderboardNumPlayers, IdlerPlayerDivisionScore playerScore) : base(participantId, avatar)
        {
            LeaderboardPlacementIndex = leaderboardPlacementIndex;
            LeaderboardNumPlayers     = leaderboardNumPlayers;
            PlayerScore               = playerScore;
        }

        IdlerPlayerDivisionConclusionResult() : base(default, default) { }
    }

    /// <summary>
    /// An example historical entry of a division for the idler leagues.
    /// </summary>
    [MetaSerializableDerived(1)]
    public class IdlerPlayerDivisionHistoryEntry : PlayerDivisionHistoryEntryBase
    {
        /// <summary>
        /// The player's score in the division.
        /// </summary>
        [MetaMember(1)] public IdlerPlayerDivisionScore PlayerScore { get; private set; }

        /// <summary>
        /// The player's placement in the final division leaderboard. 0 is 1st .
        /// </summary>
        [MetaMember(2)] public int LeaderboardPlacementIndex { get; private set; }

        public IdlerPlayerDivisionHistoryEntry(EntityId divisionId, DivisionIndex divisionIndex, IDivisionRewards rewards, IdlerPlayerDivisionScore playerScore,
            int leaderboardPlacementIndex) : base(divisionId, divisionIndex, rewards)
        {
            PlayerScore               = playerScore;
            LeaderboardPlacementIndex = leaderboardPlacementIndex;
        }

        IdlerPlayerDivisionHistoryEntry() : base(EntityId.None, default, null) { }
    }

    /// <summary>
    /// Example state for the leagues that is stored within the PlayerModel.
    /// This contains the current division and a history.
    /// </summary>
    [MetaSerializableDerived(1)]
    public class IdlerDivisionClientState : DivisionClientStateBase<IdlerPlayerDivisionHistoryEntry> { }


    /// <summary>
    /// An example conclusion result for pvp divisions.
    /// </summary>
    [MetaSerializableDerived(2)]
    public class IdlerPvPDivisionConclusionResult : PlayerDivisionParticipantConclusionResultBase<PlayerDivisionAvatarBase.Default>
    {
        [MetaMember(1)] public int LeaderboardPlacementIndex { get; private set; }
        [MetaMember(2)] public int LeaderboardNumPlayers { get; private set; }
        [MetaMember(3)] public IdlerPvPDivisionScore PlayerScore { get; private set; }

        public IdlerPvPDivisionConclusionResult(EntityId participantId, PlayerDivisionAvatarBase.Default avatar,
            int leaderboardPlacementIndex,
            int leaderboardNumPlayers, IdlerPvPDivisionScore playerScore) : base(participantId, avatar)
        {
            LeaderboardPlacementIndex = leaderboardPlacementIndex;
            LeaderboardNumPlayers = leaderboardNumPlayers;
            PlayerScore = playerScore;
        }

        IdlerPvPDivisionConclusionResult() : base(default, default)
        {
        }
    }

    /// <summary>
    /// An example historical entry of a division for the pvp leagues.
    /// </summary>
    [MetaSerializableDerived(2)]
    public class IdlerPvPDivisionHistoryEntry : PlayerDivisionHistoryEntryBase
    {
        /// <summary>
        /// The player's score in the division.
        /// </summary>
        [MetaMember(1)]
        public IdlerPvPDivisionScore PlayerScore { get; private set; }

        /// <summary>
        /// The player's placement in the final division leaderboard. 0 is 1st .
        /// </summary>
        [MetaMember(2)]
        public int LeaderboardPlacementIndex { get; private set; }

        public IdlerPvPDivisionHistoryEntry(EntityId divisionId, DivisionIndex divisionIndex,
            IDivisionRewards rewards, IdlerPvPDivisionScore playerScore,
            int leaderboardPlacementIndex) : base(divisionId, divisionIndex, rewards)
        {
            PlayerScore = playerScore;
            LeaderboardPlacementIndex = leaderboardPlacementIndex;
        }

        IdlerPvPDivisionHistoryEntry() : base(EntityId.None, default, null)
        {
        }
    }
    
    
    /// <summary>
    /// Another example state for the pvp leagues that is stored within the PlayerModel.
    /// This contains the current division and a history.
    /// </summary>
    [MetaSerializableDerived(2)]
    public class IdlerPvPDivisionClientState : DivisionClientStateBase<IdlerPvPDivisionHistoryEntry>
    {
    }
}
