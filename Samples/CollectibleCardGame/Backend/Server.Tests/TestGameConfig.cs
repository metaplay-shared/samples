using Game.Logic;
using Metaplay.Core.Config;
using System;
using System.Threading;

namespace Game.Server.Tests
{
    /// <summary>
    /// Loads the checked-in config archive once for the whole run — the same archive the server boots from,
    /// for the same reason the shared suite does: importing it is what catches an archive that has gone stale
    /// against the config classes.
    /// </summary>
    public static class TestGameConfig
    {
        // \note Relative to the Server.Tests/ directory, which TestHelper.SetupForTests() makes current.
        public const string StaticGameConfigPath = "../Server/GameConfig/StaticGameConfig.mpa";

        static readonly Lazy<SharedGameConfig> _shared = new Lazy<SharedGameConfig>(Import, LazyThreadSafetyMode.ExecutionAndPublication);

        /// <summary> The shared half of the built archive, imported and with all references resolved. </summary>
        public static SharedGameConfig Shared => _shared.Value;

        static SharedGameConfig Import()
        {
            ConfigArchive staticArchive = ConfigArchive.FromFileAsync(StaticGameConfigPath).GetAwaiter().GetResult();
            ConfigArchive sharedArchive = ConfigArchive.FromBytes(staticArchive.GetEntryByName("Shared.mpa").Bytes);
            return (SharedGameConfig)GameConfigUtil.ImportSharedConfig(sharedArchive);
        }
    }
}
