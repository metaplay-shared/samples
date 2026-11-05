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
    /// Builds the game config archives from the CSV sheets in <c>GameConfigSource/</c>, one file per config
    /// entry, and writes both copies:
    /// <list type="bullet">
    /// <item><c>Backend/Server/GameConfig/StaticGameConfig.mpa</c> — what the server boots from.</item>
    /// <item><c>WebClient/wwwroot/Assets/SharedGameConfig.mpa</c> — the built-in archive offline mode serves.</item>
    /// </list>
    /// The server has to boot from an archive on disk, so both are checked in and this is what regenerates
    /// them. Run from the repo root, whenever a sheet or a config class changes:
    /// <code>dotnet run --project tools/GameConfigGen</code>
    /// <para>
    /// <b>The build is gated on its own report.</b> A parse failure, or an error a validator merely reports,
    /// leaves the checked-in archives untouched and exits non-zero — see <see cref="ArchiveIsClean"/> for why
    /// the report and not just the exception. <c>--dry-run</c> is that same build with nothing written, which
    /// is the form to run in CI.
    /// </para>
    /// <para>
    /// There is no game-specific <c>GameConfigBuild</c> behind any of this. Naming a CSV directory as the
    /// build's default source is all the SDK needs to read every declared entry from the file of the same
    /// name.
    /// </para>
    /// <para>
    /// Publishing is deliberately not here. Making a built archive active on a server is done from the LiveOps
    /// Dashboard, which owns the auth flow and the audit trail for it; this tool only builds bytes.
    /// </para>
    /// </summary>
    public class GameConfigBuildTool : GameConfigToolBase
    {
        const string DefaultSourcesDir       = "GameConfigSource";
        const string DefaultStaticConfigPath = "Backend/Server/GameConfig/StaticGameConfig.mpa";
        const string DefaultSharedConfigPath = "WebClient/wwwroot/Assets/SharedGameConfig.mpa";

        /// <summary>
        /// Content below this many bytes is written uncompressed, matching the default the server publishes a
        /// shared archive with (<c>ContentDeliveryOptions.ArchiveMinimumSizeBeforeCompression</c>). Keeping the
        /// two the same means the built-in copy is the same bytes a live server would hand out for the same
        /// content; a client reads either form regardless.
        /// </summary>
        const int MinSizeBeforeCompression = 500;

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

            // The tool runs with the caller's working directory, which is meant to be the repo root. Rooting
            // every path against the repo rather than against the cwd is what makes it behave the same when it
            // is run from somewhere else: the archives land where the server and the client look for them.
            string repoRoot = TryFindRepoRoot();
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
                Console.Error.WriteLine($"No config sources at {_sourcesDir}. That directory holds one CSV file per game config entry.");
                return 1;
            }

            if (!AllEntriesHaveASheet(_sourcesDir))
                return 1;

            // The stock spreadsheet pipeline: naming a default source is all it takes for every entry declared
            // in SharedGameConfig to be read from the CSV file of the same name.
            DefaultGameConfigBuildParameters buildParams = new DefaultGameConfigBuildParameters
            {
                DefaultSource = new FileSystemBuildSource(FileSystemBuildSource.Format.Csv),
            };

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
                // sheet, row and column were at fault. Printing it here is what makes the failure readable.
                Console.Error.WriteLine("Game config build FAILED. No archive was written.\n");
                failed.BuildReport?.PrintToConsole();
                return 1;
            }

            if (!ArchiveIsClean(fullArchive))
                return 1;

            if (isDryRun)
            {
                Console.WriteLine($"Dry run: the sheets in {_sourcesDir} build and validate. Nothing was written.");
                return 0;
            }

            // The client's half taken the way a live server takes the one it hands out — re-serialized so
            // [ServerOnly] members are stripped — so the built-in copy and the served one are the same bytes
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
        /// Whether a built archive carries no error-level message, which is not the same question as whether
        /// the build threw.
        /// <para>
        /// <b>Validation reaches the build two ways, and only one of them is an exception.</b> A parse failure
        /// throws, so a malformed cell never gets this far. Item validators — the SDK's own, and this game's in
        /// <see cref="Game.Logic.SharedGameConfig.BuildTimeValidate"/> — report instead: the message lands in
        /// the build report, the build reports success, and the archive is handed back with the error inside
        /// it. Written out, that archive is what the server boots from.
        /// </para>
        /// <para>
        /// So the report is the gate, and it holds whoever wrote the check. A game adding checks of its own
        /// should keep it even if every one of them throws, because the SDK's do not.
        /// </para>
        /// </summary>
        public static bool ArchiveIsClean(ConfigArchive archive)
        {
            GameConfigMetaData metaData = GameConfigMetaData.FromArchive(archive);
            if (metaData == null)
            {
                // Only a full (static) archive carries build metadata. Without it there is no report to judge,
                // and answering "clean" would be a silent pass rather than an answer.
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
        /// Whether every declared config entry that is read from the default source has a sheet, checked before
        /// the build rather than during it.
        /// <para>
        /// The build reads each entry's file inside that entry's own build step, so a missing one comes back as
        /// a stack trace buried in the build report — the least legible failure this tool has, and the one an
        /// ordinary mistake produces: declaring an entry and forgetting its sheet. Checking first turns it into
        /// the list of missing files.
        /// </para>
        /// <para>
        /// An entry naming its own build source (<c>[GameConfigEntry(configBuildSource: ...)]</c>) is skipped:
        /// it is read from whatever that source is, which need not be a CSV file in this directory.
        /// </para>
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
        /// Dump the contents of a config archive. Accepts either the full archive, in which case the client's
        /// half is unwrapped from it, or a shared archive on its own — the shape a client downloads.
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
        /// Reads the value following an option, rejecting a missing one and one that looks like the next option
        /// rather than taking it as a path. <c>--static --dry-run</c> would otherwise write an archive to a
        /// file called <c>--dry-run</c> and report success.
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

        /// <summary>The repo root, found by walking up from the working directory, or null if there is none above it.</summary>
        static string TryFindRepoRoot()
        {
            DirectoryInfo directory = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "metaplay-project.yaml")))
                directory = directory.Parent;

            return directory?.FullName;
        }

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
