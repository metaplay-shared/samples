// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using Metaplay.Core.Config;
using Metaplay.Core.Model;
using Metaplay.Core.Player;
using System.Runtime.Serialization;
using System.Collections.Generic;

namespace Game.Logic
{
    public interface IPlayerModelClientListener
    {
    }

    public class EmptyPlayerModelClientListener : IPlayerModelClientListener
    {
        public static readonly EmptyPlayerModelClientListener Instance = new EmptyPlayerModelClientListener();
    }

    [MetaSerializable]
    public enum RoundStatus
    {
        Playing,    // Round is still ongoing
        Win,        // Player won the round
        Loss,       // Player lost the round
    }

    [MetaSerializable]
    public enum GuessResult
    {
        Empty,      // No result yet
        Correct,    // Correct letter in correct place
        Partial,    // Correct letter in wrong place
        Wrong       // Incorrect letter
    }

    /// <summary>
    /// Class for storing the state and updating the logic for a single player.
    /// </summary>
    [MetaSerializableDerived(1)]
    [SupportedSchemaVersions(1, 1)]
    public class PlayerModel :
        PlayerModelBase<
            PlayerModel,
            PlayerStatisticsCore
            >
    {
        public const int TicksPerSecond = 10;
        public const int WordSize = 5;
        public const int MaxGuesses = 6;

        protected override int GetTicksPerSecond() => TicksPerSecond;

        // External services, not serialized or PrettyPrinted
        [IgnoreDataMember] public new SharedGameConfig GameConfig => GetGameConfig<SharedGameConfig>();

        // Player profile
        [MetaMember(100)] public sealed override EntityId PlayerId { get; set; }
        [MetaMember(101), NoChecksum] public sealed override string PlayerName { get; set; }
        [MetaMember(102)] public sealed override int PlayerLevel { get; set; }

        // Round status
        [MetaMember(200)] public int RoundIndex { get; set; } = 0;
        [MetaMember(201)] public RoundStatus RoundStatus { get; set; } = RoundStatus.Playing;

        // Active round state
        [MetaMember(202)] public string CurrentWord { get; set; } = "";
        [MetaMember(203)] public List<string> GuessedWords { get; set; } = new List<string>();
        [MetaMember(204)] public List<GuessResult[]> GuessResults { get; set; } = new List<GuessResult[]>();

        // Statistics
        [MetaMember(205)] public int RoundsPlayed { get; set; } = 0;
        [MetaMember(206)] public int RoundsWon { get; set; } = 0;
        [MetaMember(207)] public int WinStreak { get; set; } = 0;
        [MetaMember(208)] public int MaxWinStreak { get; set; } = 0;

        [MetaMember(209)] public string CurrentSolution { get; set; } = "";

        /// <summary>
        /// Empty constructor is used when deserializing the state. This happens on the server
        /// when restoring the state from the database, and on the client when deserializing the
        /// state from the server-provided snapshot.
        /// </summary>
        public PlayerModel()
        {
        }
        #region snippet1
        protected override void GameInitializeNewPlayerModel(MetaTime now, ISharedGameConfig gameConfig, EntityId playerId, string name)
        {
            // Setup initial state for new player
            PlayerId = playerId;
            PlayerName = name;
            if (CurrentSolution == "")
            {
                CurrentSolution = GameConfig.GetSolutionForRound(RoundIndex);
            }
        }
        #endregion snippet1
    }
}
