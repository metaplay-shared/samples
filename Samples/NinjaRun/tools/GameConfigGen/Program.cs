using Game.Logic;
using Metaplay.Core;
using Metaplay.Core.Config;
using Metaplay.Core.GameConfigTool;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Game.GameConfigGen
{
    /// <summary>
    /// Builds the game config archives from the files in <c>GameConfigSource/</c> (one CSV sheet per config
    /// entry, or one JSON file for an entry a sheet cannot hold) and writes both copies:
    /// <list type="bullet">
    /// <item><c>Backend/Server/GameConfig/StaticGameConfig.mpa</c>: what the server boots from.</item>
    /// <item><c>WebClient/wwwroot/Assets/SharedGameConfig.mpa</c>: the built-in archive offline mode serves.</item>
    /// </list>
    /// Run from the repo root whenever a sheet or a config class changes. A build with any error-level message
    /// writes nothing and exits non-zero. <c>--dry-run</c> is the same build with nothing written.
    /// </summary>
    public class GameConfigBuildTool : GameConfigToolBase
    {
        const string DefaultSourcesDir       = "GameConfigSource";
        const string DefaultStaticConfigPath = "Backend/Server/GameConfig/StaticGameConfig.mpa";
        const string DefaultSharedConfigPath = "WebClient/wwwroot/Assets/SharedGameConfig.mpa";

        /// <summary>
        /// Content below this many bytes is written uncompressed, matching the default the server publishes a
        /// shared archive with (<c>ContentDeliveryOptions.ArchiveMinimumSizeBeforeCompression</c>), so the
        /// built-in copy is the same bytes a live server would hand out for the same content.
        /// </summary>
        const int MinSizeBeforeCompression = 500;

        string _sourcesDir;
        string _staticConfigPath;
        string _sharedConfigPath;

        /// <summary>
        /// Where the config build reads its source files from (one per config entry). Every file-system source
        /// in the build parameters reads this one directory, the CSV one as sheets and the binary one as bytes.
        /// </summary>
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
                        if (!TryTakeValue(args, ref index, "the directory holding the config source files", out sourcesDir))
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

            // The tool runs with the caller's working directory, which is meant to be the repo root. Rooting
            // every path against the repo rather than against the cwd is what makes it behave the same when it
            // is run from somewhere else: the archives land where the server and the client look for them.
            string repoRoot = RepoRoot.TryFind();
            if (repoRoot == null)
            {
                Console.Error.WriteLine($"Could not find the repo root above {Directory.GetCurrentDirectory()} — no directory on the way up holds metaplay-project.yaml.");
                Console.Error.WriteLine("Run this from a checkout, or give absolute paths for --sources, --static and --shared.");
                return 1;
            }

            _sourcesDir       = Rooted(repoRoot, sourcesDir       ?? DefaultSourcesDir);
            _staticConfigPath = Rooted(repoRoot, staticConfigPath ?? DefaultStaticConfigPath);
            _sharedConfigPath = Rooted(repoRoot, sharedConfigPath ?? DefaultSharedConfigPath);

            if (printArchivePath != null)
                return await PrintAsync(Rooted(repoRoot, printArchivePath));

            if (!Directory.Exists(_sourcesDir))
            {
                Console.Error.WriteLine($"No config sources at {_sourcesDir}. That directory holds one CSV or JSON file per game config entry.");
                return 1;
            }

            // Checked before the build, because inside it a missing file surfaces only as a stack trace in the
            // build report.
            if (!AllEntriesHaveASourceFile(_sourcesDir))
                return 1;

            // The stock spreadsheet pipeline for every entry declared in SharedGameConfig. Naming a default
            // source is all it takes for each to be read from the CSV file of the same name, plus the raw-file
            // source the JSON-authored entries name for themselves.
            GameBuildParameters buildParams = GameBuildParameters.FromSourceFiles();

            // Built first and written second, on purpose: the archives on disk are what the server boots from
            // and what the client ships, so a build with anything wrong in it must leave the previous ones
            // standing. GameConfigToolBase.BuildStaticGameConfigAsync writes as it goes and treats a failed
            // build as something to print, which is why this drives the builder itself.
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
                // The exception itself carries no message, so the report is the only thing that says which
                // sheet, row and column were at fault.
                Console.Error.WriteLine("Game config build FAILED. No archive was written.\n");
                failed.BuildReport?.PrintToConsole();
                return 1;
            }

            if (!ArchiveIsClean(fullArchive))
                return 1;

            if (isDryRun)
            {
                Console.WriteLine($"Dry run: the sources in {_sourcesDir} build and validate. Nothing was written.");
                return 0;
            }

            // The client's half taken the way a live server takes the one it hands out, re-serialized so
            // [ServerOnly] members are stripped, so the built-in copy and the served one are the same bytes
            // for the same content.
            (ContentHash _, byte[] sharedBytes) = GameConfigUtil.GetSharedArchiveFromFullArchiveForClient(
                fullArchive, CompressionAlgorithm.Deflate, MinSizeBeforeCompression);

            EnsureDirectoryExists(StaticGameConfigPath);
            EnsureDirectoryExists(SharedGameConfigPath);

            await ConfigArchiveBuildUtility.WriteToFileAsync(StaticGameConfigPath, fullArchive);
            await FileUtil.WriteAllBytesAsync(SharedGameConfigPath, sharedBytes);

            Console.WriteLine($"Wrote {StaticGameConfigPath} (version {fullArchive.Version})");
            Console.WriteLine($"Wrote {SharedGameConfigPath} ({sharedBytes.Length} bytes)");
            return 0;
        }

        /// <summary>
        /// Whether a built archive's build report carries no error-level message. Prints the report when it does.
        /// </summary>
        public static bool ArchiveIsClean(ConfigArchive archive)
        {
            GameConfigMetaData metaData = GameConfigMetaData.FromArchive(archive);
            if (metaData == null)
            {
                Console.Error.WriteLine("The archive carries no build metadata, so its build report cannot be checked. Expected a full (static) archive.");
                return false;
            }

            // Item validators, the SDK's and SharedGameConfig.BuildTimeValidate, report into the build report
            // instead of throwing, so a build that succeeded can still hand back an archive with an error in it.
            if (metaData.BuildSummary == null || metaData.BuildSummary.HighestMessageLevel < GameConfigLogLevel.Error)
                return true;

            Console.Error.WriteLine("Game config build produced validation errors. No archive was written.\n");
            metaData.BuildReport?.PrintToConsole();
            return false;
        }

        /// <summary>
        /// Whether every declared config entry has the file it is read from, a sheet or, for one in
        /// <see cref="Game.Logic.GameConfigBuild.JsonSourcedEntries"/>, a JSON file, and no JSON-sourced entry
        /// has a sheet beside it. Prints what is missing or unread.
        /// </summary>
        public static bool AllEntriesHaveASourceFile(string sourcesDir)
        {
            GameConfigRepository repository  = GameConfigRepository.Instance;
            List<string>         missingCsv  = new List<string>();
            List<string>         missingJson = new List<string>();
            List<string>         unread      = new List<string>();

            foreach (Type configType in new Type[] { repository.SharedGameConfigType, repository.ServerGameConfigType })
            {
                foreach ((string entryName, GameConfigEntryInfo entry) in repository.GetGameConfigTypeInfo(configType).Entries)
                {
                    bool hasSheet = File.Exists(Path.Combine(sourcesDir, entryName + ".csv"));

                    if (configType == repository.SharedGameConfigType && Game.Logic.GameConfigBuild.JsonSourcedEntries.Contains(entryName))
                    {
                        string jsonFile = Game.Logic.GameConfigBuild.JsonSourceFileName(entryName);
                        if (!File.Exists(Path.Combine(sourcesDir, jsonFile)))
                            missingJson.Add(jsonFile);
                        if (hasSheet)
                            unread.Add(entryName + ".csv");
                    }
                    else if (entry.BuildParamsSourceProperty != null)
                    {
                        // Read from its own build source, which need not be a file in this directory.
                        continue;
                    }
                    else if (!hasSheet)
                    {
                        missingCsv.Add(entryName + ".csv");
                    }
                }
            }

            if (missingCsv.Count == 0 && missingJson.Count == 0 && unread.Count == 0)
                return true;

            if (missingCsv.Count > 0)
            {
                Console.Error.WriteLine($"No CSV file in {sourcesDir} for: {string.Join(", ", missingCsv)}");
                Console.Error.WriteLine("Every game config entry is read from the file of the same name, so adding an entry means adding its sheet.");
            }

            if (missingJson.Count > 0)
            {
                Console.Error.WriteLine($"No JSON file in {sourcesDir} for: {string.Join(", ", missingJson)}");
                Console.Error.WriteLine($"These entries are read from a JSON file of the same name rather than from a sheet ({nameof(Game.Logic.GameConfigBuild)}.{nameof(Game.Logic.GameConfigBuild.JsonSourcedEntries)}), so the build has nothing to read them from.");
            }

            if (unread.Count > 0)
            {
                Console.Error.WriteLine($"A CSV file in {sourcesDir} that nothing reads: {string.Join(", ", unread)}");
                Console.Error.WriteLine("These entries are read from the JSON file of the same name, so a sheet for one of them is content the build would ignore. Delete it, and edit the JSON file instead.");
            }

            return false;
        }

        /// <summary>
        /// Dump the contents of a config archive. Accepts either the full archive, in which case the client's
        /// half is unwrapped from it, or a shared archive on its own, the shape a client downloads.
        /// </summary>
        static async Task<int> PrintAsync(string archivePath)
        {
            if (!File.Exists(archivePath))
            {
                Console.Error.WriteLine($"No config archive at {archivePath}.");
                return 1;
            }

            ConfigArchive archive = await ConfigArchive.FromFileAsync(archivePath);

            // The file's own version first. A full archive's version is not its shared half's version, and
            // printing one under the other's name is how a "which config is active?" comparison goes wrong.
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
        /// Reads the value following an option, rejecting a missing one and one that is itself an option.
        /// </summary>
        static bool TryTakeValue(string[] args, ref int index, string what, out string value)
        {
            string option = args[index];
            if (index + 1 >= args.Length)
            {
                value = null;
                Usage($"{option} needs {what}");
                return false;
            }

            value = args[index + 1];
            if (value.StartsWith("--", StringComparison.Ordinal))
            {
                Usage($"{option} needs {what}, but was given the option '{value}'");
                value = null;
                return false;
            }

            index++;
            return true;
        }

        /// <summary>The path resolved against the repo root, unless it is already absolute.</summary>
        static string Rooted(string repoRoot, string path) => Path.IsPathRooted(path) ? path : Path.Combine(repoRoot, path);

        /// <summary>Creates the directory an output file goes in, tolerating a path that names no directory.</summary>
        static void EnsureDirectoryExists(string filePath)
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
