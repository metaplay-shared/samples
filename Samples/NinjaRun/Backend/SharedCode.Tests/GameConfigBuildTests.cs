using Game.GameConfigGen;
using Metaplay.Core;
using Metaplay.Core.Config;
using NUnit.Framework;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Game.Logic.Tests
{
    /// <summary>
    /// The game config build, run for real over the sources the game ships in <c>GameConfigSource/</c> with
    /// the parameters <c>tools/GameConfigGen</c> uses, and the committed archives checked against it.
    /// </summary>
    [TestFixture]
    public class GameConfigBuildTests
    {
        const string SourcesDirName      = "GameConfigSource";
        const string StaticArchivePath   = "Backend/Server/GameConfig/StaticGameConfig.mpa";
        const string ClientArchivePath   = "WebClient/wwwroot/Assets/SharedGameConfig.mpa";

        [Test]
        public async Task TheSheetsBuildIntoACleanArchive()
        {
            ConfigArchive archive = await BuildArchiveAsync(SourcesDir());

            // Item validators report into the build report rather than throwing, so a build can succeed and
            // still hand back an archive with an error inside it.
            Assert.That(GameConfigBuildTool.ArchiveIsClean(archive), Is.True, "the built archive carries an error-level build message");
        }

        [Test]
        public async Task TheCommittedServerArchiveMatchesTheSheets()
        {
            // Both halves of the static archive are compared, so the entries a ServerGameConfig declares are held
            // to the sources as well as the half a client is served.
            ConfigArchive committed = await ReadCommittedArchiveAsync(StaticArchivePath);
            ConfigArchive fresh     = await BuildArchiveAsync(SourcesDir());

            AssertMatchesTheSources(
                ImportSharedForClient(committed),
                ImportSharedForClient(fresh),
                typeof(SharedGameConfig),
                "server archive, shared half");

            AssertMatchesTheSources(
                ImportServerConfig(committed),
                ImportServerConfig(fresh),
                GameConfigRepository.Instance.ServerGameConfigType,
                "server archive, server half");
        }

        [Test]
        public async Task TheCommittedClientArchiveMatchesTheSheets()
        {
            // Offline mode loads this copy instead of the server's.
            AssertMatchesTheSources(
                await ImportCommittedArchiveAsync(ClientArchivePath),
                await BuildAndImportClientConfigAsync(),
                typeof(SharedGameConfig),
                "client archive");
        }

        /// <summary>
        /// A content rule that does not hold stops the archive through the build report, without the build
        /// throwing.
        /// </summary>
        [Test]
        public async Task AContentErrorIsReportedAndStopsTheArchive()
        {
            // Every file the shipped sources hold, so the build has one per declared entry and the only thing
            // wrong with it is the one value below.
            string sourcesDir = CopyOfTheSources("broken-config-sources");

            // A runner that never moves forward: it parses, so the build succeeds, and only SharedGameConfig's
            // own BuildTimeValidate objects.
            await File.WriteAllTextAsync(
                Path.Combine(sourcesDir, "TimeTrialRules.csv"),
                "Member,Value" + Environment.NewLine + "RunSpeed,0" + Environment.NewLine);

            ConfigArchive archive = await BuildArchiveAsync(sourcesDir);

            GameConfigMetaData metaData = GameConfigMetaData.FromArchive(archive);
            Assert.That(metaData, Is.Not.Null, "a full archive should carry build metadata");
            Assert.That(metaData.BuildSummary?.HighestMessageLevel, Is.EqualTo(GameConfigLogLevel.Error),
                "the build reported no error, so the content rule did not fire");
            Assert.That(GameConfigBuildTool.ArchiveIsClean(archive), Is.False,
                "the build tool would have written an archive carrying a content error");
        }

        /// <summary>
        /// Every declared config entry has the file it is read from, and every JSON-sourced entry names its build
        /// source and has its JSON file and no sheet.
        /// </summary>
        [Test]
        public void EveryConfigEntryHasItsSourceFile()
        {
            Assert.That(GameConfigBuildTool.AllEntriesHaveASourceFile(SourcesDir()), Is.True, "a declared config entry has no source file");

            GameConfigTypeInfo typeInfo = GameConfigRepository.Instance.GetGameConfigTypeInfo(typeof(SharedGameConfig));

            Assert.Multiple(() =>
            {
                foreach (string entryName in GameConfigBuild.JsonSourcedEntries)
                {
                    Assert.That(typeInfo.Entries.ContainsKey(entryName), Is.True,
                        $"{entryName} is named as read from JSON but is not an entry of SharedGameConfig");
                    if (typeInfo.Entries.TryGetValue(entryName, out GameConfigEntryInfo entry))
                    {
                        Assert.That(entry.BuildParamsSourceProperty, Is.EqualTo(nameof(GameBuildParameters.CoursePartsSource)),
                            $"{entryName} does not name the build source its JSON file is read through");
                    }
                    Assert.That(File.Exists(Path.Combine(SourcesDir(), GameConfigBuild.JsonSourceFileName(entryName))), Is.True,
                        $"{SourcesDirName}/{GameConfigBuild.JsonSourceFileName(entryName)} is missing");
                    Assert.That(File.Exists(Path.Combine(SourcesDir(), entryName + ".csv")), Is.False,
                        $"{SourcesDirName}/{entryName}.csv is a sheet nothing reads, because that entry is read from JSON");
                }
            });
        }

        /// <summary>
        /// A sheet beside a JSON-sourced entry is refused before the build runs.
        /// </summary>
        [Test]
        public void ASheetBesideAJsonSourcedEntryIsRefused()
        {
            string sourcesDir = CopyOfTheSources("unread-sheet-config-sources");
            File.WriteAllText(Path.Combine(sourcesDir, nameof(SharedGameConfig.CourseParts) + ".csv"), "");

            Assert.That(GameConfigBuildTool.AllEntriesHaveASourceFile(sourcesDir), Is.False,
                "a sheet for an entry read from JSON was accepted, so nobody would be told it is never read");
        }

        /// <summary>A JSON-sourced entry whose file is missing is named before the build runs.</summary>
        [Test]
        public void AMissingJsonFileIsRefused()
        {
            string sourcesDir = CopyOfTheSources("missing-json-config-sources");
            File.Delete(Path.Combine(sourcesDir, GameConfigBuild.JsonSourceFileName(nameof(SharedGameConfig.CourseParts))));

            Assert.That(GameConfigBuildTool.AllEntriesHaveASourceFile(sourcesDir), Is.False,
                "an entry read from JSON was accepted with no JSON file behind it");
        }

        /// <summary>
        /// A malformed parts file fails the build itself, and the failure names the part and the field. This is
        /// the path an edit that <see cref="CoursePartJson"/> refuses takes through the SDK's build.
        /// </summary>
        [Test]
        public void AMalformedPartsFileFailsTheBuild()
        {
            string sourcesDir = CopyOfTheSources("malformed-json-config-sources");
            File.WriteAllText(
                Path.Combine(sourcesDir, GameConfigBuild.JsonSourceFileName(nameof(SharedGameConfig.CourseParts))),
                "[ { \"id\": \"broken\", \"positions\": [\"First\"], \"isSetPiece\": false, \"ground\": [ { \"startX\": 0, \"topY\": 0 } ], \"hazards\": [], \"bridges\": [] } ]");

            GameConfigBuildFailed failed = Assert.ThrowsAsync<GameConfigBuildFailed>(async () => await BuildArchiveAsync(sourcesDir));
            Assert.That(failed.BuildReport, Is.Not.Null, "the failed build carries no report to say what was wrong");
            Assert.That(PrettyPrint.Verbose(failed.BuildReport).ToString(), Does.Contain("part [0] ('broken'): ground[0].endX is missing"),
                "the build report does not name the part and the field at fault");
        }

        #region Helpers

        /// <summary>
        /// The real config build over a directory of sheets, with the same parameters and the same fetcher
        /// <c>tools/GameConfigGen</c> uses, so what this fixture reads is what a build writes.
        /// </summary>
        internal static async Task<ConfigArchive> BuildArchiveAsync(string sourcesDir)
        {
            GameBuildParameters buildParams = GameBuildParameters.FromSourceFiles();

            try
            {
                return await StaticFullGameConfigBuilder.BuildArchiveAsync(
                    MetaTime.Now,
                    parentId:      MetaGuid.None,
                    parent:        null,
                    buildParams:   buildParams,
                    fetcherConfig: GameConfigSourceFetcherConfigCore.Create().WithLocalFileSourcesPath(sourcesDir));
            }
            catch (GameConfigBuildFailed failed)
            {
                // GameConfigBuildFailed carries no message of its own, so an uncaught one fails the suite with
                // nothing but a stack trace. The report is what names the sheet, row and column.
                failed.BuildReport?.PrintToConsole();
                throw;
            }
        }

        static async Task<SharedGameConfig> BuildAndImportClientConfigAsync()
            => ImportSharedForClient(await BuildArchiveAsync(SourcesDir()));

        static async Task<SharedGameConfig> ImportCommittedArchiveAsync(string relativePath)
            => ImportSharedForClient(await ReadCommittedArchiveAsync(relativePath));

        static async Task<ConfigArchive> ReadCommittedArchiveAsync(string relativePath)
        {
            string path = Path.Combine(TestGameConfig.RepoRoot(), relativePath);
            Assert.That(File.Exists(path), Is.True, $"{relativePath} is missing");
            return await ConfigArchive.FromFileAsync(path);
        }

        /// <summary>
        /// The shared half of an archive, taken the way a live server takes the one it hands out. Accepts a full
        /// archive or a shared one on its own.
        /// </summary>
        static SharedGameConfig ImportSharedForClient(ConfigArchive archive)
        {
            if (archive.ContainsEntryWithName("Shared.mpa"))
            {
                // Strips [ServerOnly] members as the committed client archive was, so both sides of a comparison
                // are filtered the same way.
                (ContentHash _, byte[] sharedBytes) = GameConfigUtil.GetSharedArchiveFromFullArchiveForClient(archive);
                archive = ConfigArchive.FromBytes(sharedBytes);
            }

            return (SharedGameConfig)GameConfigUtil.ImportSharedConfig(archive);
        }

        /// <summary>The server half of a full archive, which no client ever receives.</summary>
        static IServerGameConfig ImportServerConfig(ConfigArchive fullArchive)
        {
            ConfigArchive serverArchive = ConfigArchive.FromBytes(fullArchive.GetEntryBytes("Server.mpa"));
            return GameConfigUtil.ImportServerConfig(serverArchive);
        }

        /// <summary>
        /// Every entry of a committed archive, item by item, against a fresh build of the sources.
        /// </summary>
        static void AssertMatchesTheSources(IGameConfig committed, IGameConfig fresh, Type configType, string what)
        {
            GameConfigTypeInfo typeInfo = GameConfigRepository.Instance.GetGameConfigTypeInfo(configType);

            Assert.Multiple(() =>
            {
                foreach ((string entryName, GameConfigEntryInfo entryInfo) in typeInfo.Entries)
                {
                    Assert.That(
                        Describe(entryInfo.GetEntry(committed)),
                        Is.EqualTo(Describe(entryInfo.GetEntry(fresh))),
                        $"{what}: {entryName} is not what the sheets in {SourcesDirName} build. Run: dotnet run --project tools/GameConfigGen");
                }
            });
        }

        /// <summary>One config entry as comparable text: every item it holds, printed in full.</summary>
        static string Describe(IGameConfigEntry entry)
        {
            // A member PrettyPrint hides ([IgnoreDataMember], [PrettyPrint(PrettyPrintFlag.Hide)], [Sensitive]) is
            // not compared, and a byte[] prints as its length. A config item carrying either needs its own assertion.
            if (entry is not IGameConfigLibrary library)
                return PrettyPrint.Verbose(entry).ToString();

            return string.Join("\n", library.EnumerateAll().Select(item => $"{item.Key}: {PrettyPrint.Verbose(item.Value)}"));
        }

        static string SourcesDir() => Path.Combine(TestGameConfig.RepoRoot(), SourcesDirName);

        /// <summary>
        /// A fresh copy of every source file the game ships (the sheets and the JSON files) in a directory of
        /// the test's own, for a test to break one thing in.
        /// </summary>
        internal static string CopyOfTheSources(string directoryName)
        {
            string sourcesDir = Path.Combine(TestContext.CurrentContext.WorkDirectory, directoryName);
            if (Directory.Exists(sourcesDir))
                Directory.Delete(sourcesDir, recursive: true);
            Directory.CreateDirectory(sourcesDir);

            foreach (string pattern in new string[] { "*.csv", "*.json" })
            {
                foreach (string file in Directory.GetFiles(SourcesDir(), pattern))
                    File.Copy(file, Path.Combine(sourcesDir, Path.GetFileName(file)));
            }

            return sourcesDir;
        }

        #endregion // Helpers
    }
}
