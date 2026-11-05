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
    /// The game config build, run for real over the sheets the game ships in <c>GameConfigSource/</c>.
    /// <para>
    /// What this fixture asserts is the seam rather than the balance — that the CSV content becomes an archive,
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
        const string SourcesDirName      = "GameConfigSource";
        const string StaticArchivePath   = "Backend/Server/GameConfig/StaticGameConfig.mpa";
        const string ClientArchivePath   = "WebClient/wwwroot/Assets/SharedGameConfig.mpa";

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
            string sourcesDir = Path.Combine(TestContext.CurrentContext.WorkDirectory, "broken-config-sources");
            Directory.CreateDirectory(sourcesDir);
            foreach (string sheet in Directory.GetFiles(SourcesDir(), "*.csv"))
                File.Copy(sheet, Path.Combine(sourcesDir, Path.GetFileName(sheet)), overwrite: true);

            // An engine-flag keyword the engine has no flag for: it parses, so the build succeeds, and only
            // SharedGameConfig's own BuildTimeValidate objects (ContentValidator's keyword rule).
            await File.AppendAllTextAsync(Path.Combine(sourcesDir, "Keywords.csv"), "Sparkly,EngineFlag,Sparkly,Names no engine flag." + Environment.NewLine);

            ConfigArchive archive = await BuildArchiveAsync(sourcesDir);

            GameConfigMetaData metaData = GameConfigMetaData.FromArchive(archive);
            Assert.That(metaData, Is.Not.Null, "a full archive should carry build metadata");
            Assert.That(metaData.BuildSummary?.HighestMessageLevel, Is.EqualTo(GameConfigLogLevel.Error),
                "the build reported no error, so the content rule did not fire");
            Assert.That(GameConfigBuildTool.ArchiveIsClean(archive), Is.False,
                "the build tool would have written an archive carrying a content error");
        }

        /// <summary>
        /// Every entry declared in <see cref="SharedGameConfig"/> has a sheet behind it.
        /// <para>
        /// With a default source configured, the SDK reads every declared entry from the file of the same name,
        /// and a missing one fails the build from inside that entry's own build step — which surfaces as a stack
        /// trace rather than as a build message. Adding an entry and forgetting its sheet is the ordinary way
        /// into that, and this says so in one line instead.
        /// </para>
        /// </summary>
        [Test]
        public void EveryConfigEntryHasASheetBehindIt()
        {
            Assert.That(GameConfigBuildTool.AllEntriesHaveASheet(SourcesDir()), Is.True, "a declared config entry has no CSV file");
        }

        #region Helpers

        /// <summary>
        /// The real config build over a directory of sheets, with the same parameters and the same fetcher
        /// <c>tools/GameConfigGen</c> uses, so what this fixture reads is what a build writes.
        /// </summary>
        static async Task<ConfigArchive> BuildArchiveAsync(string sourcesDir)
        {
            DefaultGameConfigBuildParameters buildParams = new DefaultGameConfigBuildParameters
            {
                DefaultSource = new FileSystemBuildSource(FileSystemBuildSource.Format.Csv),
            };

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
        static string RepoRoot()
        {
            DirectoryInfo directory = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "metaplay-project.yaml")))
                directory = directory.Parent;

            Assert.That(directory, Is.Not.Null, "could not find the repo root above the test working directory");
            return directory.FullName;
        }

        #endregion // Helpers
    }
}
