using Metaplay.Core.Config;
using NUnit.Framework;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Game.Logic.Tests
{
    /// <summary>
    /// The game config the tests run against: the committed archive, read the way a server reads it. Tests that
    /// assert on tuned behavior go through this rather than constructing config objects, so retuning a sheet
    /// shows up here rather than leaving a test green against numbers nobody ships.
    /// </summary>
    public static class TestGameConfig
    {
        // \note Relative to the test project's directory (SharedCode.Tests/ or Server.Tests/), which is the test working directory.
        const string StaticGameConfigPath = "../Server/GameConfig/StaticGameConfig.mpa";

        /// <summary>
        /// Load the game config archive file from disk. The <see cref="SharedGameConfig"/> part of the full
        /// archive is returned.
        /// </summary>
        public static async Task<SharedGameConfig> LoadSharedAsync()
        {
            // Read StaticGameConfig.mpa from disk
            ConfigArchive staticGameConfigArchive = await ConfigArchive.FromFileAsync(StaticGameConfigPath);

            // Extract Shared.mpa from the parent archive
            ReadOnlyMemory<byte> sharedGameConfigBytes = staticGameConfigArchive.GetEntryByName("Shared.mpa").Bytes;
            ConfigArchive sharedGameConfigArchive = ConfigArchive.FromBytes(sharedGameConfigBytes);

            // Create the SharedGameConfig from the bytes
            ISharedGameConfig sharedGameConfig = GameConfigUtil.ImportSharedConfig(sharedGameConfigArchive);
            return (SharedGameConfig)sharedGameConfig;
        }

        /// <summary>
        /// The repo root, found by walking up from the test working directory. <c>TestHelper.SetupForTests()</c>
        /// makes that the test project's own directory, so nothing here depends on how deep it sits.
        /// </summary>
        public static string RepoRoot()
        {
            DirectoryInfo directory = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "metaplay-project.yaml")))
                directory = directory.Parent;

            Assert.That(directory, Is.Not.Null, "could not find the repo root above the test working directory");
            return directory.FullName;
        }
    }
}
