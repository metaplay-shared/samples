using Game.Logic;
using Metaplay.Core;
using Metaplay.Core.Config;
using Metaplay.Core.GameConfigTool;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Game.GameConfigGen
{
    /// <summary>
    /// Builds the game config archives from the CSV sheets in <c>GameConfigSource/</c> (one file per config entry)
    /// and writes the server's <c>StaticGameConfig.mpa</c> and offline mode's built-in <c>SharedGameConfig.mpa</c>,
    /// which are both checked in. Run <c>dotnet run --project tools/GameConfigGen</c> from the project root whenever
    /// a sheet or a config class changes (<c>docs/game-config.md</c>).
    /// If the build throws or its report has an error-level message (<see cref="BuildReportHasNoErrors"/>), the tool
    /// writes nothing and exits with code 1. <c>--dry-run</c> runs the same build and checks without writing, for
    /// CI. The tool does not publish. Activating an archive on a server is done from the LiveOps Dashboard, which
    /// handles authentication and the audit log.
    /// </summary>
    public class GameConfigBuildTool : GameConfigToolBase
    {
        const string DefaultSourcesDir       = "GameConfigSource";
        const string DefaultStaticConfigPath = "Backend/Server/GameConfig/StaticGameConfig.mpa";
        const string DefaultSharedConfigPath = "WebClient/wwwroot/Assets/SharedGameConfig.mpa";

        /// <summary>
        /// Shared archive entries smaller than this many bytes are written uncompressed. Keep this equal to the
        /// default of <c>ContentDeliveryOptions.ArchiveMinimumSizeBeforeCompression</c>, so that the built-in
        /// copy has the same bytes as the archive a server sends for the same content. A client can read
        /// either form.
        /// </summary>
        const int MinBytesBeforeCompression = 500;

        string _sourcesDir;
        string _staticConfigPath;
        string _sharedConfigPath;

        /// <summary>Where the config build reads its CSV sheets from (one file per config entry).</summary>
        protected override IGameConfigSourceFetcherConfig FetcherConfig =>
            GameConfigSourceFetcherConfigCore.Create().WithLocalFileSourcesPath(_sourcesDir);

        protected override string StaticGameConfigPath => _staticConfigPath;
        protected override string SharedGameConfigPath => _sharedConfigPath;

        async Task<int> RunAsync(string[] args)
        {
            string sourcesDir       = null;
            string staticConfigPath = null;
            string sharedConfigPath = null;
            string printArchivePath = null;
            bool   isDryRun         = false;

            for (int index = 0; index < args.Length; index++)
            {
                switch (args[index])
                {
                    case "--print":
                        if (!TryTakeValue(args, ref index, "the path of a config archive", out printArchivePath))
                            return 1;
                        break;

                    case "--dry-run":
                        isDryRun = true;
                        break;

                    case "--sources":
                        if (!TryTakeValue(args, ref index, "the directory holding the config CSV files", out sourcesDir))
                            return 1;
                        break;

                    case "--static":
                        if (!TryTakeValue(args, ref index, "a path", out staticConfigPath))
                            return 1;
                        break;

                    case "--shared":
                        if (!TryTakeValue(args, ref index, "a path", out sharedConfigPath))
                            return 1;
                        break;

                    default:
                        return Usage($"Unknown argument '{args[index]}'");
                }
            }

            // Resolve every relative path against the directory holding metaplay-project.yaml, not the working
            // directory, so that the archives are written to where the server and the client read them from
            // even when the tool is run from a subdirectory.
            string projectRoot = TryFindProjectRoot();
            if (projectRoot == null)
            {
                Console.Error.WriteLine($"Could not find the repo root above {Directory.GetCurrentDirectory()} — no directory on the way up holds metaplay-project.yaml.");
                Console.Error.WriteLine("Run this from a checkout, or give absolute paths for --sources, --static and --shared.");
                return 1;
            }

            _sourcesDir       = Rooted(projectRoot, sourcesDir       ?? DefaultSourcesDir);
            _staticConfigPath = Rooted(projectRoot, staticConfigPath ?? DefaultStaticConfigPath);
            _sharedConfigPath = Rooted(projectRoot, sharedConfigPath ?? DefaultSharedConfigPath);

            if (printArchivePath != null)
                return await PrintAsync(Rooted(projectRoot, printArchivePath));

            if (!Directory.Exists(_sourcesDir))
            {
                Console.Error.WriteLine($"No config sources at {_sourcesDir}. That directory holds one CSV file per game config entry.");
                return 1;
            }

            if (!AllEntriesHaveASheet(_sourcesDir))
                return 1;

            // The game has no custom GameConfigBuild. With a default source set, the SDK reads every entry
            // declared in the config types from the CSV file with the entry's name.
            DefaultGameConfigBuildParameters buildParams = new DefaultGameConfigBuildParameters
            {
                DefaultSource = new FileSystemBuildSource(FileSystemBuildSource.Format.Csv),
            };

            // Build the whole archive before writing anything, so that a failed build leaves the previous archives
            // on disk. GameConfigToolBase.BuildStaticGameConfigAsync writes while it builds and only prints a
            // failure, so this method calls the builder directly instead.
            ConfigArchive fullArchive;
            try
            {
                fullArchive = await StaticFullGameConfigBuilder.BuildArchiveAsync(
                    MetaTime.Now,
                    parentId:      MetaGuid.None,
                    parent:        null,
                    buildParams:   buildParams,
                    fetcherConfig: FetcherConfig);
            }
            catch (GameConfigBuildFailed failed)
            {
                // The exception has no useful message. Only the build report says which sheet, row and column
                // failed, so print the report.
                Console.Error.WriteLine("Game config build FAILED. No archive was written.\n");
                failed.BuildReport?.PrintToConsole();

                // GameConfigValidationFailed, thrown by the game's own checks, has a message that lists every
                // invalid value, so print it after the report. The SDK's GameConfigBuildFailed message contains
                // nothing that the report does not.
                if (failed is GameConfigValidationFailed validationFailed)
                    Console.Error.WriteLine(validationFailed.Message);

                return 1;
            }

            if (!BuildReportHasNoErrors(fullArchive))
                return 1;

            if (isDryRun)
            {
                Console.WriteLine($"Dry run: the sheets in {_sourcesDir} build and validate. Nothing was written.");
                return 0;
            }

            // Extract the shared archive with the same call and settings a server uses for the archive it sends
            // to clients, so that the built-in copy has the same bytes.
            (ContentHash _, byte[] sharedBytes) = GameConfigUtil.GetSharedArchiveFromFullArchiveForClient(
                fullArchive, CompressionAlgorithm.Deflate, MinBytesBeforeCompression);

            EnsureParentDirectoryExists(StaticGameConfigPath);
            EnsureParentDirectoryExists(SharedGameConfigPath);

            await ConfigArchiveBuildUtility.WriteToFileAsync(StaticGameConfigPath, fullArchive);
            await FileUtil.WriteAllBytesAsync(SharedGameConfigPath, sharedBytes);

            Console.WriteLine($"Wrote {StaticGameConfigPath} (version {fullArchive.Version})");
            Console.WriteLine($"Wrote {SharedGameConfigPath} ({sharedBytes.Length} bytes)");
            return 0;
        }

        /// <summary>
        /// Returns true if the build report stored in a full archive has no error-level message. Prints the
        /// report and returns false otherwise, or if the archive has no build metadata.
        /// <para>
        /// A successful build can still contain errors. This game's own checks throw
        /// <see cref="GameConfigValidationFailed"/>, which stops the build. The SDK's item validators (for example,
        /// an offer group that lists the same offer twice) only add a message to the build report, and the build
        /// still returns the archive. Keep this check even if all of a game's own checks throw.
        /// </para>
        /// </summary>
        public static bool BuildReportHasNoErrors(ConfigArchive archive)
        {
            GameConfigMetaData metaData = GameConfigMetaData.FromArchive(archive);
            if (metaData == null)
            {
                // Only a full (static) archive has build metadata. Without it the report cannot be checked, so
                // return false instead of passing the archive unchecked.
                Console.Error.WriteLine("The archive carries no build metadata, so its build report cannot be checked. Expected a full (static) archive.");
                return false;
            }

            if (metaData.BuildSummary == null || metaData.BuildSummary.HighestMessageLevel < GameConfigLogLevel.Error)
                return true;

            Console.Error.WriteLine("Game config build produced validation errors. No archive was written.\n");
            metaData.BuildReport?.PrintToConsole();
            return false;
        }

        /// <summary>
        /// Returns true if every shared and server config entry that uses the default source has a CSV file in
        /// <paramref name="sourcesDir"/>. Otherwise prints the missing file names and returns false. It runs before
        /// the build, where a missing file fails as a stack trace in the build report that is hard to read. Entries
        /// with their own build source (<c>[GameConfigEntry(configBuildSource: ...)]</c>) are skipped, because their
        /// source need not be a CSV file in this directory.
        /// </summary>
        public static bool AllEntriesHaveASheet(string sourcesDir)
        {
            GameConfigRepository repository = GameConfigRepository.Instance;
            List<string>         missing    = new List<string>();

            foreach (Type configType in new Type[] { repository.SharedGameConfigType, repository.ServerGameConfigType })
            {
                foreach ((string entryName, GameConfigEntryInfo entry) in repository.GetGameConfigTypeInfo(configType).Entries)
                {
                    if (entry.BuildParamsSourceProperty != null)
                        continue;

                    if (!File.Exists(Path.Combine(sourcesDir, entryName + ".csv")))
                        missing.Add(entryName + ".csv");
                }
            }

            if (missing.Count == 0)
                return true;

            Console.Error.WriteLine($"No CSV file in {sourcesDir} for: {string.Join(", ", missing)}");
            Console.Error.WriteLine("Every game config entry is read from the file of the same name, so adding an entry means adding its sheet.");
            return false;
        }

        /// <summary>
        /// Prints the contents of a config archive. Accepts a full archive, from which it reads the shared
        /// archive inside, or a shared archive on its own (the form a client downloads).
        /// </summary>
        static async Task<int> PrintAsync(string archivePath)
        {
            if (!File.Exists(archivePath))
            {
                Console.Error.WriteLine($"No config archive at {archivePath}.");
                return 1;
            }

            ConfigArchive archive = await ConfigArchive.FromFileAsync(archivePath);

            // Print the file's own version first, then the version of the shared archive inside it. A full archive
            // and its shared archive have different versions, and each is printed under its own name.
            Console.WriteLine($"{archivePath} (version {archive.Version})");
            if (archive.ContainsEntryWithName("Shared.mpa"))
            {
                archive = ConfigArchive.FromBytes(archive.GetEntryBytes("Shared.mpa"));
                Console.WriteLine($"  Shared.mpa (version {archive.Version}) — the half a client is served");
            }

            ISharedGameConfig  sharedConfig = GameConfigUtil.ImportSharedConfig(archive);
            GameConfigTypeInfo typeInfo     = GameConfigRepository.Instance.GetGameConfigTypeInfo(sharedConfig.GetType());

            foreach ((string entryName, GameConfigEntryInfo entryInfo) in typeInfo.Entries)
            {
                IGameConfigEntry entry = entryInfo.GetEntry(sharedConfig);
                if (entry is IGameConfigLibrary library)
                {
                    Console.WriteLine($"{entryName}:");
                    foreach ((object key, object value) in library.EnumerateAll())
                        Console.WriteLine($"  {key}: {PrettyPrint.Compact(value)}");
                }
                else
                {
                    Console.WriteLine($"{entryName}: {PrettyPrint.Compact(entry)}");
                }
            }
            return 0;
        }

        /// <summary>
        /// Reads the value after the option at <paramref name="index"/> and advances <paramref name="index"/> past
        /// it. Prints usage and returns false if the value is missing or starts with <c>--</c>. Without the second
        /// check, <c>--static --dry-run</c> would write an archive to a file named <c>--dry-run</c>.
        /// </summary>
        static bool TryTakeValue(string[] args, ref int index, string valueDescription, out string value)
        {
            string option = args[index];
            if (index + 1 >= args.Length)
            {
                value = null;
                Usage($"{option} needs {valueDescription}");
                return false;
            }

            value = args[index + 1];
            if (value.StartsWith("--", StringComparison.Ordinal))
            {
                Usage($"{option} needs {valueDescription}, but was given the option '{value}'");
                value = null;
                return false;
            }

            index++;
            return true;
        }

        /// <summary>Returns the nearest directory at or above the working directory that holds metaplay-project.yaml, or null.</summary>
        static string TryFindProjectRoot()
        {
            DirectoryInfo directory = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "metaplay-project.yaml")))
                directory = directory.Parent;

            return directory?.FullName;
        }

        static string Rooted(string projectRoot, string path) => Path.IsPathRooted(path) ? path : Path.Combine(projectRoot, path);

        /// <summary>Creates the parent directory of <paramref name="filePath"/>. Does nothing if the path has no directory part.</summary>
        static void EnsureParentDirectoryExists(string filePath)
        {
            string directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
        }

        static int Usage(string error)
        {
            Console.Error.WriteLine(error);
            Console.Error.WriteLine("Usage: dotnet run --project tools/GameConfigGen [--sources <dir>] [--static <path>] [--shared <path>] [--dry-run]");
            Console.Error.WriteLine("                                               [--print <archivePath>]");
            return 1;
        }

        static async Task<int> Main(string[] args)
        {
            GameConfigBuildTool tool = new GameConfigBuildTool();
            return await tool.RunAsync(args);
        }
    }
}
