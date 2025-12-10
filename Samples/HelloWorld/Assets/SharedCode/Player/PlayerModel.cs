// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using Metaplay.Core.Config;
using Metaplay.Core.Model;
using Metaplay.Core.Player;
using System.Runtime.Serialization;

namespace Game.Logic
{
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
        protected override int GetTicksPerSecond() => TicksPerSecond;

        [IgnoreDataMember] public IPlayerModelServerListener ServerListener { get; set; } = EmptyPlayerModelServerListener.Instance;
        [IgnoreDataMember] public IPlayerModelClientListener ClientListener { get; set; } = EmptyPlayerModelClientListener.Instance;

        // Player profile
        [MetaMember(100)] public sealed override EntityId           PlayerId    { get; set; }
        [MetaMember(101), NoChecksum] public sealed override string PlayerName  { get; set; }
        [MetaMember(102)] public sealed override int                PlayerLevel { get; set; }

        // Game-specific state
        [MetaMember(200)] public int                                NumClicks   { get; set; } = 0;  // Number of times the button has been clicked

        protected override void GameInitializeNewPlayerModel(MetaTime now, ISharedGameConfig gameConfig, EntityId playerId, string name)
        {
            // Setup initial state for new player
            PlayerId    = playerId;
            PlayerName  = name;
        }

        #region Schema migrations

        // Example migration from schema v1 to v2
        //[MigrationFromVersion(1)]
        //void Migrate1To2()
        //{
        //    NumClicks += 10;
        //}

        #endregion
    }
}
