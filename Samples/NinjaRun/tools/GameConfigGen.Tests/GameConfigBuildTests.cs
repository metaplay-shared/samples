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
    /// The game config build, run for real over the sources the game ships in <c>GameConfigSource/</c>: the
    /// sheets, and the JSON files of the entries a sheet cannot hold.
    /// <para>
    /// What this fixture asserts is the seam rather than the balance — that the source content becomes an archive,
    /// that the archive is the one a client would be handed, and that the committed archives are the ones those
    /// sheets build. It runs the build with the same parameters <c>tools/GameConfigGen</c> uses, so a sheet that
    /// only this fixture can read would be a bug in the fixture.
    /// </para>
    /// <para>
    /// Assertions about what the numbers ought to be belong here too, as the game grows: a fixture that only
    /// proves the pipeline works will not notice a price that moved by a factor of ten.
    /// </para>
    /// </summary>
    [TestFixture]
    public class GameConfigBuildTests
    {
        const string          SourcesDirName    = "GameConfigSource";
        internal const string StaticArchivePath = "Backend/Server/GameConfig/StaticGameConfig.mpa";
        const string          ClientArchivePath = "WebClient/wwwroot/Assets/SharedGameConfig.mpa";

        [Test]
        public async Task TheSheetsBuildIntoACleanArchive()
        {
            ConfigArchive archive = await BuildArchiveAsync(SourcesDir());

            // The gate the build tool applies, asserted here as well: the SDK's own item validators report
            // into the build report rather than throwing, so a build can succeed and still hand back an
            // archive with an error inside it.
            Assert.That(GameConfigBuildTool.ArchiveIsClean(archive), Is.True, "the built archive carries an error-level build message");
        }

        [Test]
        public async Task TheCommittedServerArchiveMatchesTheSheets()
        {
            // Editing a sheet and forgetting to rebuild would otherwise leave every test green while the server
            // boots a stale archive. This is the check that makes the committed archive part of the source it
            // was built from.
            //
            // Both halves of the static archive are compared. The shared half is what a client is served; the
            // server half holds the entries a ServerGameConfig declares, and comparing only the shared one
            // would exempt the first server-only entry a game adds from this whole check.
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
            // The same trap one step further along: offline mode loads this copy, so a rebuild that wrote only
            // the server's archive would leave the client running yesterday's content.
            AssertMatchesTheSources(
                await ImportCommittedArchiveAsync(ClientArchivePath),
                await BuildAndImportClientConfigAsync(),
                typeof(SharedGameConfig),
                "client archive");
        }

        /// <summary>
        /// A content rule that does not hold stops the archive, and does so through the report rather than by
        /// throwing.
        /// <para>
        /// This is the negative control for <see cref="GameConfigBuildTool.ArchiveIsClean"/>. Item validators
        /// report into the build report instead of failing the build, so without that gate the build would
        /// succeed here and hand back an archive with the error inside it. A validator that rejects nothing
        /// passes everything, which is why this builds deliberately broken content rather than trusting the
        /// shipped sheets to exercise the path.
        /// </para>
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
        /// Every entry declared in <see cref="SharedGameConfig"/> has the file it is read from behind it, and
        /// every JSON-sourced entry names its build source and has its JSON file and no sheet.
        /// <para>
        /// With a default source configured, the SDK reads every declared entry from the file of the same name,
        /// and a missing one fails the build from inside that entry's own build step — which surfaces as a stack
        /// trace rather than as a build message. Adding an entry and forgetting its sheet is the ordinary way
        /// into that, and this says so in one line instead.
        /// </para>
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
        /// The real config build over a directory of sources, with the same parameters and the same fetcher
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
            string path = Path.Combine(RepoRoot(), relativePath);
            Assert.That(File.Exists(path), Is.True, $"{relativePath} is missing");
            return await ConfigArchive.FromFileAsync(path);
        }

        /// <summary>
        /// The shared half of an archive, taken the way a live server takes the one it hands out. Accepts a full
        /// archive or a shared one on its own.
        /// <para>
        /// Going through <c>GetSharedArchiveFromFullArchiveForClient</c> rather than unwrapping the raw
        /// <c>Shared.mpa</c> entry matters: the client's half has <c>[ServerOnly]</c> members stripped, and both
        /// sides of every comparison here are filtered the same way. Unwrapping raw on one side would fail on
        /// the first server-only value anybody adds.
        /// </para>
        /// </summary>
        static SharedGameConfig ImportSharedForClient(ConfigArchive archive)
        {
            if (archive.ContainsEntryWithName("Shared.mpa"))
            {
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
        /// Every entry of a committed archive, item by item, against a fresh build of the CSV sources.
        /// <para>
        /// The comparison is the whole printed item rather than a chosen list of fields, which is what makes it
        /// total: a value that moved in a sheet and not in the archive is caught wherever it lives, including
        /// in the fields nobody thought to assert on. What it cannot say is whether the numbers are the right
        /// ones. Note that the archives are not compared as bytes — every build stamps its own timestamp and
        /// version hash, so identical content produces different files.
        /// </para>
        /// <para>
        /// Two blind spots are worth knowing about, both inherited from <see cref="PrettyPrint"/>: a member
        /// hidden from printing (<c>[IgnoreDataMember]</c>, <c>[PrettyPrint(PrettyPrintFlag.Hide)]</c>,
        /// <c>[Sensitive]</c>) is not compared, and a <c>byte[]</c> is rendered as its length rather than its
        /// contents. A config item carrying either needs an assertion of its own.
        /// </para>
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
            if (entry is not IGameConfigLibrary library)
                return PrettyPrint.Verbose(entry).ToString();

            return string.Join("\n", library.EnumerateAll().Select(item => $"{item.Key}: {PrettyPrint.Verbose(item.Value)}"));
        }

        static string SourcesDir() => Path.Combine(RepoRoot(), SourcesDirName);

        /// <summary>
        /// The repo root, found by walking up from the test working directory. <c>TestHelper.SetupForTests()</c>
        /// makes that the test project's own directory, so nothing here depends on how deep it sits.
        /// </summary>
        internal static string RepoRoot()
        {
            DirectoryInfo directory = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "metaplay-project.yaml")))
                directory = directory.Parent;

            Assert.That(directory, Is.Not.Null, "could not find the repo root above the test working directory");
            return directory.FullName;
        }

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
