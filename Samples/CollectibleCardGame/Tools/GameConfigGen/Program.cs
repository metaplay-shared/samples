using Metaplay.Core;
using Metaplay.Core.Config;
using Metaplay.Core.GameConfigTool;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Game.GameConfigGen
{
    /// <summary>
    /// Builds the game config archives from the local CSV sources in <c>GameConfigSource/</c>:
    /// <list type="bullet">
    /// <item><c>Backend/Server/GameConfig/StaticGameConfig.mpa</c> — what the server boots from.</item>
    /// <item><c>Client/wwwroot/Assets/SharedGameConfig.mpa</c> — the built-in archive offline mode serves.</item>
    /// </list>
    /// The server has to boot from an archive on disk, so the archive is checked in and this is what
    /// regenerates it. Run from the sample root, whenever the config classes or the CSV sources change:
    /// <code>dotnet run --project Tools/GameConfigGen</code>
    /// <para>
    /// The build is gated on its own report: a parse failure or a content-validation error leaves the
    /// checked-in archives untouched and exits non-zero. That gate is the tool's own, because
    /// <see cref="GameConfigToolBase.BuildStaticGameConfigAsync"/> swallows a failed build and content
    /// validation reports into the build metadata rather than throwing.
    /// </para>
    /// </summary>
    class GameConfigGen : GameConfigToolBase
    {
        /// <summary> Where the config build reads its CSV sheets from (one file per config entry). </summary>
        const string LocalSourcePath = "GameConfigSource";

        protected override IGameConfigSourceFetcherConfig FetcherConfig =>
            GameConfigSourceFetcherConfigCore.Create().WithLocalFileSourcesPath(LocalSourcePath);

        protected override string StaticGameConfigPath => "Backend/Server/GameConfig/StaticGameConfig.mpa";
        protected override string SharedGameConfigPath => "Client/wwwroot/Assets/SharedGameConfig.mpa";

        async Task<int> RunAsync(string[] args)
        {
            bool isDryRun = Array.IndexOf(args, "--dry-run") >= 0;

            if (!Directory.Exists(LocalSourcePath))
            {
                Console.Error.WriteLine($"No config sources at '{Path.GetFullPath(LocalSourcePath)}'. Run this from the sample root.");
                return 1;
            }

            Console.WriteLine("Building StaticGameConfig archive..");
            DefaultGameConfigBuildParameters buildParams = new DefaultGameConfigBuildParameters
            {
                // One local-CSV source for every entry: GameConfigSource/<EntryName>.csv.
                DefaultSource = new FileSystemBuildSource(FileSystemBuildSource.Format.Csv),
            };

            ConfigArchive staticArchive;
            try
            {
                staticArchive = await StaticFullGameConfigBuilder.BuildArchiveAsync(
                    MetaTime.Now,
                    parentId: MetaGuid.None,
                    parent: null,
                    buildParams,
                    FetcherConfig,
                    new GameConfigBuildDebugOptions { EnableDebugPrints = true });
            }
            catch (GameConfigBuildFailed failed)
            {
                Console.Error.WriteLine("Game config build FAILED:");
                failed.BuildReport.PrintToConsole();
                return 1;
            }

            GameConfigMetaData metaData = GameConfigMetaData.FromArchive(staticArchive);
            if (metaData?.BuildSummary != null && metaData.BuildSummary.HighestMessageLevel >= GameConfigLogLevel.Error)
            {
                Console.Error.WriteLine("Game config build produced validation errors; the checked-in archives were left alone:");
                metaData.BuildReport?.PrintToConsole();
                return 1;
            }

            if (isDryRun)
            {
                Console.WriteLine("Dry run: archives built and validated, nothing written.");
                return 0;
            }

            await WriteArchivesAsync(staticArchive);
            return 0;
        }

        /// <summary>
        /// Writes both outputs: the full static archive the server boots from, and the shared half of it that
        /// the web client carries as its built-in archive for offline mode.
        /// </summary>
        async Task WriteArchivesAsync(ConfigArchive staticArchive)
        {
            ConfigArchive sharedArchive = ConfigArchive.FromBytes(staticArchive.GetEntryBytes("Shared.mpa"));

            Directory.CreateDirectory(Path.GetDirectoryName(StaticGameConfigPath));
            Directory.CreateDirectory(Path.GetDirectoryName(SharedGameConfigPath));

            await WriteArchiveAsync(StaticGameConfigPath, staticArchive);
            await WriteArchiveAsync(SharedGameConfigPath, sharedArchive);
        }

        static async Task WriteArchiveAsync(string path, ConfigArchive archive)
        {
            byte[] bytes = ConfigArchiveBuildUtility.ToBytes(archive);
            string entries = string.Join("\n", archive.Entries.Select(entry => $"  {entry.Name}: {entry.Bytes.Length} bytes"));
            Console.WriteLine($"\nWriting {path} ({bytes.Length} bytes):\n{entries}");
            await FileUtil.WriteAllBytesAsync(path, bytes);
        }

        static async Task<int> Main(string[] args)
        {
            GameConfigGen tool = new GameConfigGen();
            return await tool.RunAsync(args);
        }
    }
}
