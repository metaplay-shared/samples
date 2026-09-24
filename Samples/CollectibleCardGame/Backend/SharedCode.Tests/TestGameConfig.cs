using Metaplay.Core.Config;
using System;
using System.Threading;

namespace Game.Logic.Tests
{
    /// <summary>
    /// Loads the checked-in config archive once for the whole test run. Importing the archive the server
    /// boots from is what catches an archive that has gone stale against the config classes, so every suite
    /// that needs content goes through here rather than building its own.
    /// </summary>
    public static class TestGameConfig
    {
        // \note Relative to the SharedCode.Tests/ directory, which TestHelper.SetupForTests() makes current.
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
