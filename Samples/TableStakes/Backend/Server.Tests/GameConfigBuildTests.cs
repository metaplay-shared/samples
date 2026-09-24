using Game.GameConfigGen;
using Game.Logic;
using Game.Server.Player;
using Metaplay.Core;
using Metaplay.Core.Config;
using Metaplay.Core.InAppPurchase;
using Metaplay.Core.Offers;
using Metaplay.Core.Player;
using Metaplay.Core.Schedule;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Game.Server.Tests
{
    /// <summary>
    /// Runs the real game config build over the CSV sheets in <c>GameConfigSource/</c>.
    /// <para>
    /// The fixture tests the build pipeline more than the balance: that the CSV content builds into an archive,
    /// that the client receives the expected archive, that the committed archives match a build of the sheets,
    /// and that the game's own checks run inside the SDK's build-time validation. It uses the same build
    /// parameters as <c>tools/GameConfigGen</c>.
    /// </para>
    /// </summary>
    [TestFixture]
    public class GameConfigBuildTests
    {
        // Expected values from the design docs. They are written here rather than read from the sheets, so an
        // unintended change in a sheet fails a test. An intended change must be made both here and in the sheet.
        const int NameGeneratorVersion = 1;
        const int BrassFramePriceCoins = 900;

        // The coin and gem price bands for cosmetics (docs/cosmetics.md, docs/economy.md).
        const int CoinBandFloor   = 500;
        const int CoinBandCeiling = 2_500;
        const int GemBandFloor    = 150;
        const int GemBandCeiling  = 1_000;

        static readonly CosmeticId              ChampionFrame               = CosmeticId.FromString("frame.champion");
        static readonly TournamentRewardTableId ActiveTournamentRewardTable = TournamentRewardTableId.FromString("tournament.v1");
        static readonly InAppProductId          DemoGemBoosterPack          = InAppProductId.FromString("gem_booster_pack");
        static readonly MetaOfferId             GemBoosterPackOffer         = MetaOfferId.FromString("gem-booster-pack");

        /// <summary>The config sheet directory, relative to the repo root. Also used in assertion messages.</summary>
        const string SourcesDirName = "GameConfigSource";

        static string SourcesDir() => Path.Combine(RepoRoot(), SourcesDirName);

        /// <summary>
        /// Builds a config archive from a directory of sheets, with the same parameters and source fetcher as
        /// <c>tools/GameConfigGen</c>.
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
                // GameConfigBuildFailed has no message, so print the build report, which names the sheet, row and
                // column.
                failed.BuildReport?.PrintToConsole();
                throw;
            }
        }

        /// <summary>
        /// The shipped sheets, built once and shared by every test that only reads the result, because a config
        /// build is the slowest step in this fixture.
        /// <para>
        /// <b>A test that modifies the config must not use <see cref="ShippedClientConfig"/></b>, because an
        /// imported config is mutable and a change would affect every later test. Such a test calls
        /// <see cref="ImportFreshClientConfigAsync"/> instead, which imports its own copy from the shared archive.
        /// Tests that need a separate build for other reasons also build their own.
        /// </para>
        /// </summary>
        static readonly Lazy<Task<ConfigArchive>> ShippedArchive =
            new Lazy<Task<ConfigArchive>>(() => BuildArchiveAsync(SourcesDir()));

        static readonly Lazy<Task<SharedGameConfig>> ShippedClientConfig =
            new Lazy<Task<SharedGameConfig>>(async () => ImportSharedForClient(await ShippedArchive.Value));

        /// <summary>Returns the shared build, for a test that only reads the config.</summary>
        static Task<SharedGameConfig> GetCachedClientConfigAsync() => ShippedClientConfig.Value;

        /// <summary>
        /// Returns a separate import of the shared build, for a test that modifies the config. Each import is a new
        /// object, so a change to it does not reach the cached config or another test.
        /// </summary>
        static async Task<SharedGameConfig> ImportFreshClientConfigAsync()
            => ImportSharedForClient(await ShippedArchive.Value);

        /// <summary>The repo root, found by walking up from the fixture's working directory.</summary>
        internal static string RepoRoot()
        {
            DirectoryInfo directory = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "metaplay-project.yaml")))
                directory = directory.Parent;

            Assert.That(directory, Is.Not.Null, "could not find the repo root above the test working directory");
            return directory.FullName;
        }

        static async Task<SharedGameConfig> ImportCommittedArchiveAsync(string relativePath)
            => ImportSharedForClient(await ReadCommittedArchiveAsync(relativePath));

        static async Task<ConfigArchive> ReadCommittedArchiveAsync(string relativePath)
        {
            string path = Path.Combine(RepoRoot(), relativePath);
            Assert.That(File.Exists(path), Is.True, $"{relativePath} is missing");
            return await ConfigArchive.FromFileAsync(path);
        }

        /// <summary>
        /// Imports the shared config from a full archive or a shared-only archive, the same way a server extracts
        /// the archive it sends to clients.
        /// <para>
        /// It uses <c>GetSharedArchiveFromFullArchiveForClient</c> rather than reading the raw <c>Shared.mpa</c>
        /// entry, because the client version has <c>[ServerOnly]</c> members removed. Both sides of every
        /// comparison are filtered the same way, so adding a server-only member does not break the comparison.
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

        /// <summary>Imports the server-only config from a full archive. Clients never receive this part.</summary>
        static IServerGameConfig ImportServerConfig(ConfigArchive fullArchive)
            => GameConfigUtil.ImportServerConfig(GameConfigUtil.GetServerArchiveFromFullArchive(fullArchive));

        /// <summary>
        /// Describes a segment's condition as a string: each property and its bounds, in order. Comparing the
        /// whole condition, not only the segment ids, catches a changed threshold.
        /// </summary>
        static string DescribeSegmentCondition(PlayerSegmentInfoBase segment)
        {
            if (segment.PlayerCondition is not PlayerSegmentBasicCondition condition || condition.PropertyRequirements == null)
                return "(not a range condition)";

            return string.Join(" AND ", condition.PropertyRequirements.Select(requirement =>
                $"{requirement.Id.DisplayName} in [{requirement.Min?.ConstantValue?.ToString() ?? "*"}, {requirement.Max?.ConstantValue?.ToString() ?? "*"}]"));
        }

        /// <summary>
        /// Asserts that every entry of a committed archive matches a new build of the CSV sheets, item by item.
        /// It compares whole printed items rather than archive bytes, because every build has its own timestamp and
        /// version hash. Because it compares <see cref="PrettyPrint"/> output, a member hidden from printing
        /// (<c>[IgnoreDataMember]</c>, <c>[PrettyPrint(PrettyPrintFlag.Hide)]</c>, <c>[Sensitive]</c>) is not
        /// compared, and a <c>byte[]</c> is compared only by length. A config item with either needs its own
        /// assertion.
        /// </summary>
        static void AssertMatchesTheSources(IGameConfig committed, IGameConfig fresh, Type configType, string archiveLabel)
        {
            GameConfigTypeInfo typeInfo = GameConfigRepository.Instance.GetGameConfigTypeInfo(configType);

            Assert.Multiple(() =>
            {
                foreach ((string entryName, GameConfigEntryInfo entryInfo) in typeInfo.Entries)
                {
                    Assert.That(
                        Describe(entryInfo.GetEntry(committed)),
                        Is.EqualTo(Describe(entryInfo.GetEntry(fresh))),
                        $"{archiveLabel}: {entryName} is not what the sheets in {SourcesDirName} build. Run: dotnet run --project tools/GameConfigGen");
                }
            });
        }

        /// <summary>
        /// Describes one config entry as text: every item of a library, or the single key-value object.
        /// </summary>
        static string Describe(IGameConfigEntry entry)
        {
            if (entry is not IGameConfigLibrary library)
                return PrettyPrint.Verbose(entry).ToString();

            return string.Join("\n", library.EnumerateAll().Select(item => $"{item.Key}: {PrettyPrint.Verbose(item.Value)}"));
        }

        [Test]
        public async Task TheCommittedServerArchiveMatchesTheSheets()
        {
            // Catches a sheet edited without rebuilding the archive, which the server would load without error.
            // Both the shared and the server-only parts are compared, so a server-only entry is also covered.
            ConfigArchive committed = await ReadCommittedArchiveAsync("Backend/Server/GameConfig/StaticGameConfig.mpa");
            ConfigArchive fresh     = await ShippedArchive.Value;

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
            // Offline mode loads this copy, so it must also match the sheets.
            AssertMatchesTheSources(
                await ImportCommittedArchiveAsync("WebClient/wwwroot/Assets/SharedGameConfig.mpa"),
                await GetCachedClientConfigAsync(),
                typeof(SharedGameConfig),
                "client archive");
        }

        /// <summary>
        /// Every entry declared in <see cref="SharedGameConfig"/> has a CSV sheet.
        /// <para>
        /// A missing file fails the config build with an unhandled exception instead of a build message, so this
        /// test names the problem directly. It calls the build tool's own check, so the two agree about which
        /// entries need a sheet.
        /// </para>
        /// </summary>
        [Test]
        public void EveryConfigEntryHasASheetBehindIt()
        {
            Assert.That(GameConfigBuildTool.AllEntriesHaveASheet(SourcesDir()), Is.True, "a declared config entry has no CSV file");
        }

        /// <summary>
        /// The offer segments have the thresholds the offers design uses. The thresholds are game config and can
        /// be changed, but the offer demo depends on these values.
        /// <para>
        /// Each segment also requires <c>PersonalizedOffersEnabled</c>, so a player who opted out of personalized
        /// offers matches none of them (<c>docs/offers.md</c>, "Segment targeting").
        /// </para>
        /// </summary>
        [Test]
        public async Task TheAuthoredCohortsAreTheOnesTheOfferDemoNeeds()
        {
            SharedGameConfig config = await GetCachedClientConfigAsync();

            string Condition(PlayerSegmentId id) => DescribeSegmentCondition(config.PlayerSegments[id]);

            Assert.Multiple(() =>
            {
                Assert.That(Condition(PlayerSegmentIds.FirstWeekNonPurchaser), Is.EqualTo("Account age (days) in [0, 6] AND Validated purchases in [0, 0] AND Personalized offers enabled in [True, *]"));
                Assert.That(Condition(PlayerSegmentIds.GemFunded), Is.EqualTo("Gem balance in [500, *] AND Personalized offers enabled in [True, *]"));
                Assert.That(Condition(PlayerSegmentIds.EngagedNonPurchaser), Is.EqualTo("Games played in [5, *] AND Validated purchases in [0, 0] AND Personalized offers enabled in [True, *]"));
            });
        }

        /// <summary>
        /// The bot profiles and bot names in the archive the client receives are valid and have the expected
        /// values.
        /// </summary>
        [Test]
        public async Task TheShippedArchiveCarriesTheComputerPlayers()
        {
            SharedGameConfig config = await GetCachedClientConfigAsync();

            List<BotProfile> profiles = BotConfig.DrawableProfiles(config);
            Assert.That(profiles, Is.Not.Empty, "an archive with no strengths would deal every seat the strongest profile");
            foreach (BotProfile profile in profiles)
            {
                Assert.That(profile.Mode, Is.EqualTo(BotDecisionMode.Heuristic), $"'{profile.Id}' is published with a test decision mode");
                Assert.That(profile.MistakeChancePercent, Is.InRange(0, BotProfileInfo.MaxMistakeChancePercent), $"'{profile.Id}'");
                Assert.That(profile.LongThinkChancePercent, Is.InRange(0, 100), $"'{profile.Id}'");
            }

            // At least one bot never makes a mistake and at least one does. Identical profiles would pass every
            // per-row check above.
            Assert.That(profiles.Any(profile => profile.MistakeChancePercent == 0), Is.True, "no published opponent plays its best card every time");
            Assert.That(profiles.Any(profile => profile.MistakeChancePercent > 0), Is.True, "no published opponent ever makes a mistake");

            // Pin the values. The shared-code tests measure match timing with a copy of these profiles, because
            // they cannot read the game config (Backend/SharedCode.Tests/TestBotConfig,
            // Backend/SharedCode.Tests/MatchPacingTests). A changed profile must fail here so the copy is updated.
            Assert.That(
                string.Join(", ", profiles.OrderBy(profile => profile.Id.Value).Select(profile => $"{profile.Id}:{profile.MistakeChancePercent}/{profile.LongThinkChancePercent}")),
                Is.EqualTo("casual:25/25, sharp:0/15, steady:10/20"),
                "the shipped strengths moved; move Backend/SharedCode.Tests/TestBotConfig with them");

            BotNameRoster roster = BotConfig.ReservedNames(config);
            Assert.That(roster.Count, Is.GreaterThanOrEqualTo(MatchRules.NumSeats), "a table could be asked for more names than are published");

            // Draw names for a full table the same way the matchmaker does. A player must not be able to take
            // any of them, because the name is how players recognize a bot.
            List<string> drawn = roster.Draw(seed: 1234UL, count: MatchRules.NumSeats);
            Assert.That(drawn, Is.Unique);
            foreach (string name in drawn)
            {
                Assert.That(DisplayNamePolicy.Validate(name, roster), Is.EqualTo(DisplayNameRefusal.ReservedName), $"'{name}' is a name a player could take");
                Assert.That(DisplayNamePolicy.Validate(name, BotNameRoster.Empty), Is.EqualTo(DisplayNameRefusal.None), $"'{name}' is not a name a player could have had");
            }
        }

        /// <summary>
        /// On the server, the integration registry resolves the name validator to
        /// <see cref="TableStakesServerPlayerRequirementsValidator"/>, which also refuses bot names.
        /// <para>
        /// If the registry resolved to the shared base class instead, the LiveOps Dashboard rename would accept
        /// bot names without any error. This test is the only check of which type is used.
        /// </para>
        /// </summary>
        [Test]
        public void TheRegistryResolvesTheNameValidatorToTheServerValidator()
        {
            Assert.That(
                IntegrationRegistry.GetSingleIntegrationType<PlayerRequirementsValidator>(),
                Is.EqualTo(typeof(TableStakesServerPlayerRequirementsValidator)));
        }

        /// <summary>
        /// Every name the generated-name word lists can produce passes the rename rules. It reads the word lists
        /// from the built archive, so a list that does not survive the build fails too.
        /// </summary>
        [Test]
        public async Task EveryNameTheShippedVocabularyCanProduceIsOneAPlayerCouldKeep()
        {
            SharedGameConfig config = await GetCachedClientConfigAsync();

            Assert.That(config.PlayerIdentity.CombinationCount, Is.GreaterThanOrEqualTo(DisplayNameGenerator.MinCombinationCount));

            // Collect the refused names and assert once, because an assertion per combination is slow.
            BotNameRoster reservedBotNames = BotConfig.ReservedNames(config);
            List<string>  refused          = DisplayNameGenerator.AllCombinations(config.PlayerIdentity)
                .Where(candidate => DisplayNamePolicy.Validate(candidate, reservedBotNames) != DisplayNameRefusal.None)
                .ToList();
            Assert.That(refused, Is.Empty, "these generated names would be refused from a player");
        }

        /// <summary>
        /// New players get a generated name from the archive's word lists, the same way the server names them.
        /// </summary>
        [Test]
        public async Task AFreshPlayerIsNamedOutOfTheShippedVocabulary()
        {
            SharedGameConfig config = await GetCachedClientConfigAsync();

            for (int index = 1; index <= 100; index++)
            {
                EntityId             playerId  = EntityId.Create(EntityKindCore.Player, (ulong)index);
                GeneratedDisplayName generated = DisplayNameGenerator.Generate(config, playerId, DisplayNameGenerator.FallbackName(playerId));

                Assert.That(generated.UsedFallback, Is.False, $"player {index} fell back to a guest name");
                Assert.That(generated.GeneratorVersion, Is.EqualTo(NameGeneratorVersion));
                Assert.That(generated.Name, Does.Match("^[A-Z][a-z]+[A-Z][a-z]+[0-9]{2}$"), $"'{generated.Name}' does not read as adjective + noun + two digits");
                Assert.That(DisplayNamePolicy.Validate(generated.Name, BotConfig.ReservedNames(config)), Is.EqualTo(DisplayNameRefusal.None));
            }
        }

        /// <summary>
        /// Every purchasable cosmetic is priced inside its currency's band, both ends of each band are used, and
        /// no two cosmetics share a style (<c>docs/cosmetics.md</c>).
        /// <para>
        /// The coin band's floor is what a first session can afford, and its ceiling is the price the daily
        /// reward economy is calculated against.
        /// </para>
        /// </summary>
        [Test]
        public async Task TheCatalogueIsPricedInsideItsBands()
        {
            SharedGameConfig config = await GetCachedClientConfigAsync();

            bool sawCoinFloor   = false;
            bool sawCoinCeiling = false;
            bool sawGemFloor    = false;
            bool sawGemCeiling  = false;
            List<CosmeticStyle> styles = new List<CosmeticStyle>();

            foreach (CosmeticInfo cosmetic in config.Cosmetics.Values)
            {
                if (cosmetic.Style != CosmeticStyle.None)
                    styles.Add(cosmetic.Style);

                if (!cosmetic.IsPurchasable)
                    continue;

                if (cosmetic.Price.Currency == CurrencyType.Coins)
                {
                    Assert.That(cosmetic.Price.Amount, Is.InRange(CoinBandFloor, CoinBandCeiling), $"{cosmetic.Id} coin price");
                    sawCoinFloor   |= cosmetic.Price.Amount == CoinBandFloor;
                    sawCoinCeiling |= cosmetic.Price.Amount == CoinBandCeiling;
                }
                else
                {
                    Assert.That(cosmetic.Price.Currency, Is.EqualTo(CurrencyType.Gems), $"{cosmetic.Id} is priced in neither coins nor gems");
                    Assert.That(cosmetic.Price.Amount, Is.InRange(GemBandFloor, GemBandCeiling), $"{cosmetic.Id} gem price");
                    sawGemFloor   |= cosmetic.Price.Amount == GemBandFloor;
                    sawGemCeiling |= cosmetic.Price.Amount == GemBandCeiling;
                }
            }

            Assert.That(sawCoinFloor, Is.True, "nothing is priced at the coin band's floor, so the first session can buy nothing outright");
            Assert.That(sawCoinCeiling, Is.True, "nothing is priced at the coin band's ceiling, which the faucet arithmetic is written against");

            // The gem band's floor is the price the starting wallet is checked against.
            Assert.That(sawGemFloor, Is.True, "nothing is priced at the gem band's floor, which is the price the starting wallet is checked against");
            Assert.That(sawGemCeiling, Is.True, "nothing is priced at the gem band's ceiling");

            Assert.That(styles.Distinct().Count(), Is.EqualTo(styles.Count), "two catalogue items wear the same style");
        }

        /// <summary>
        /// Every animated name effect style is used only for a name effect and is priced in gems.
        /// </summary>
        [Test]
        public async Task PremiumNameEffectsArePricedInGems()
        {
            SharedGameConfig config = await GetCachedClientConfigAsync();

            foreach (CosmeticInfo cosmetic in config.Cosmetics.Values)
            {
                if (cosmetic.Style != CosmeticStyle.NamePrism
                    && cosmetic.Style != CosmeticStyle.NameBloom
                    && cosmetic.Style != CosmeticStyle.NameFrost)
                {
                    continue;
                }

                Assert.That(cosmetic.Kind, Is.EqualTo(CosmeticKind.NameEffect), $"{cosmetic.Id} wears a premium name style in another slot");
                Assert.That(cosmetic.Price, Is.Not.Null, $"{cosmetic.Id} has no price");
                Assert.That(cosmetic.Price.Currency, Is.EqualTo(CurrencyType.Gems), $"{cosmetic.Id} is not priced in gems");
            }
        }

        /// <summary>
        /// The champion frame is the top tournament placement's reward and is not purchasable. The config build
        /// already refuses a purchasable champion frame. This test checks the shipped content.
        /// </summary>
        [Test]
        public async Task TheChampionFrameIsAwardedAndNeverSold()
        {
            SharedGameConfig config = await GetCachedClientConfigAsync();

            CosmeticInfo champion = config.Cosmetics[ChampionFrame];
            Assert.That(champion.IsPurchasable, Is.False);
            Assert.That(champion.Price, Is.Null);
            Assert.That(champion.UnlockRequirement, Is.Not.Empty, "a locked item has to say what unlocks it");

            TournamentRewardTableInfo table = config.TournamentRewards[ActiveTournamentRewardTable];
            Assert.That(table.Placements[0].Cosmetic?.KeyObject, Is.EqualTo(ChampionFrame));
        }

        /// <summary>
        /// The avatar ids that players may already own still exist, every avatar has a glyph, and the default
        /// spade avatar is in the catalogue but not for sale (<c>docs/cosmetics.md</c>).
        /// </summary>
        [Test]
        public async Task TheAvatarSlotIsCompleteStyledAndSellsNoDefault()
        {
            SharedGameConfig config = await GetCachedClientConfigAsync();

            List<CosmeticInfo> avatars = config.Cosmetics.Values.Where(c => c.Kind == CosmeticKind.Avatar).ToList();

            foreach (string publishedId in new[] { "avatar.jack", "avatar.queen", "avatar.ace" })
            {
                Assert.That(avatars.Any(c => c.Id.Value == publishedId), Is.True, $"{publishedId} was dropped rather than kept");
            }

            Assert.That(avatars.All(c => c.Style != CosmeticStyle.None), Is.True, "an avatar with no glyph draws the default in silence");

            // The spade is in the catalogue so a player can switch back to it, and not for sale because every
            // player is granted it at creation (docs/cosmetics.md, "The starting three").
            CosmeticInfo spade = avatars.SingleOrDefault(c => c.Style == CosmeticStyle.AvatarSpade);
            Assert.That(spade, Is.Not.Null, "the starting spade is not in the catalogue, so nobody can wear it on purpose");
            Assert.That(spade.Id, Is.EqualTo(CosmeticDefaults.Avatar), "the spade is not the id the model grants");
            Assert.That(spade.IsPurchasable, Is.False, "the default spade is nobody's purchase");
        }

        [Test]
        public async Task TheDemoValueTravelsInTheArchiveTheClientReceives()
        {
            // The over-the-air update demo changes this price and publishes the config. Read it from the client's
            // archive to check that the client gets the price from config.
            SharedGameConfig config = await GetCachedClientConfigAsync();

            CosmeticInfo brassFrame = config.Cosmetics[CosmeticId.FromString("frame.brass")];
            Assert.That(brassFrame.Price.Currency, Is.EqualTo(CurrencyType.Coins));
            Assert.That(brassFrame.Price.Amount, Is.EqualTo(BrassFramePriceCoins));
        }

        /// <summary>
        /// The demo gem bundle in the client's archive has the expected contents and price. A live-server test
        /// asserts the balances after buying it, so a change here would make that test fail without a clear
        /// reason.
        /// </summary>
        [Test]
        public async Task TheDemoBundleTravelsInTheArchiveTheClientReceives()
        {
            SharedGameConfig config = await GetCachedClientConfigAsync();

            // The product has the price and the store ids. The contents are the offer's, because the product has
            // dynamic content and is sold through an OfferInfo (docs/offers.md, "In-app purchase offers").
            DemoInAppProductInfo product = config.InAppProducts[DemoGemBoosterPack];
            OfferInfo            offer   = config.Offers[GemBoosterPackOffer];

            Assert.That(offer.Contents.AmountOf(CurrencyType.Gems), Is.EqualTo(1_250));
            Assert.That(offer.Contents.AmountOf(CurrencyType.Coins), Is.EqualTo(10_000));
            Assert.That(offer.Contents.AmountOf(CurrencyType.SpinTokens), Is.EqualTo(3));
            Assert.That(product.DemoPriceText, Is.EqualTo("$4.99"));

            // No store is connected. A product with a real store id would charge real money once a store was
            // connected. Config validation refuses store ids, and this checks the shipped archive too.
            Assert.That(product.GoogleId, Is.Null);
            Assert.That(product.AppleId, Is.Null);
            Assert.That(product.SteamId, Is.Null);
        }

        /// <summary>
        /// The total rewards of the active daily and weekly mission sets match the economy budget
        /// (<c>docs/economy.md</c>). The totals are literal numbers, so a change to the missions must also
        /// change this test.
        /// </summary>
        [Test]
        public async Task ACompletedMissionSetPaysExactlyWhatTheEconomyBudgetedForIt()
        {
            SharedGameConfig config = await GetCachedClientConfigAsync();

            MissionSetInfo daily  = config.Global.ActiveDailyMissionSet.Ref;
            MissionSetInfo weekly = config.Global.ActiveWeeklyMissionSet.Ref;

            Assert.That(daily.Missions.Sum(mission => mission.Ref.Reward.AmountOf(CurrencyType.Coins)), Is.EqualTo(400));
            Assert.That(daily.Missions.Sum(mission => mission.Ref.Reward.AmountOf(CurrencyType.SpinTokens)), Is.Zero);

            Assert.That(weekly.Missions.Sum(mission => mission.Ref.Reward.AmountOf(CurrencyType.SpinTokens)), Is.EqualTo(2));
            Assert.That(weekly.Missions.Sum(mission => mission.Ref.Reward.AmountOf(CurrencyType.Coins)), Is.Zero);

            foreach (MissionInfo mission in config.Missions.Values)
                Assert.That(mission.Reward.AmountOf(CurrencyType.Gems), Is.Zero, $"{mission.Id} pays gems");

            // The number of missions per set.
            Assert.That(daily.Missions.Count, Is.EqualTo(MissionSetInfo.NumDailyMissions));
            Assert.That(weekly.Missions.Count, Is.EqualTo(MissionSetInfo.NumWeeklyMissions));
        }

        [Test]
        public async Task TheActivePointersResolveThroughTheArchive()
        {
            // MetaRefs are resolved at import, so this checks that the references between libraries survive
            // writing and reading the archive.
            SharedGameConfig config = await GetCachedClientConfigAsync();

            Assert.That(config.Global.ActiveDailyRewardTable.Ref.Steps.Count, Is.EqualTo(DailyRewardTableInfo.NumSteps));
            Assert.That(config.Global.ActiveFirstWeekSchedule.Ref.Days.Count, Is.EqualTo(FirstWeekScheduleInfo.NumDays));
            Assert.That(config.Global.ActiveWheelTable.Ref.Sectors.Count, Is.EqualTo(WheelTableInfo.NumSectors));
            Assert.That(config.Global.ActiveDailyMissionSet.Ref.Cadence, Is.EqualTo(MissionCadence.Daily));
            Assert.That(config.Global.ActiveWeeklyMissionSet.Ref.Cadence, Is.EqualTo(MissionCadence.Weekly));
            Assert.That(config.Global.ActiveTournamentRewardTable.Ref.Milestones.Count, Is.GreaterThan(0));
        }

        /// <summary>
        /// The active daily reward cycle has the rewards of the economy budget, value by value.
        /// <para>
        /// <c>GameConfigValidationTests</c> checks the structure. This test checks the <b>numbers</b>, which
        /// the economy calculations in <c>docs/economy.md</c> depend on, so a change must be made there first.
        /// </para>
        /// </summary>
        [Test]
        public async Task ThePublishedDailyCycleIsTheApprovedEconomyBudget()
        {
            SharedGameConfig     config = await GetCachedClientConfigAsync();
            DailyRewardTableInfo cycle  = config.Global.ActiveDailyRewardTable.Ref;

            Assert.Multiple(() =>
            {
                Assert.That(cycle.Steps.Select(step => step.Reward.AmountOf(CurrencyType.Coins)),
                    Is.EqualTo(new[] { 150, 170, 190, 210, 240, 270, 330 }));

                Assert.That(cycle.Steps.Sum(step => step.Reward.AmountOf(CurrencyType.Coins)), Is.EqualTo(1_560),
                    "a completed cycle is 1,560 coins");
                Assert.That(cycle.Steps.Sum(step => step.Reward.AmountOf(CurrencyType.SpinTokens)), Is.EqualTo(1),
                    "a completed cycle is one spin token");
                Assert.That(cycle.Steps.Sum(step => step.Reward.AmountOf(CurrencyType.Gems)), Is.Zero,
                    "the daily reward is not a gem faucet");

                Assert.That(cycle.Steps.Last().Reward.AmountOf(CurrencyType.SpinTokens), Is.EqualTo(1),
                    "the token is the seventh step's capstone");
                Assert.That(cycle.Steps.Select(step => step.Step), Is.EqualTo(new[] { 1, 2, 3, 4, 5, 6, 7 }));
                Assert.That(cycle.Steps.Select(step => step.Id).Distinct().Count(), Is.EqualTo(DailyRewardTableInfo.NumSteps));
            });
        }

        /// <summary>
        /// The active first-week schedule has the requirements and rewards of the economy budget, value by value.
        /// <para>
        /// <c>GameConfigValidationTests</c> checks the structure. This test checks the <b>numbers</b>, which the
        /// economy depends on. Gems and spin tokens are only on the last day, and the last day requires one match
        /// so that a player who missed a day can still finish.
        /// </para>
        /// </summary>
        [Test]
        public async Task ThePublishedFirstWeekScheduleIsTheApprovedEconomyBudget()
        {
            SharedGameConfig      config   = await GetCachedClientConfigAsync();
            FirstWeekScheduleInfo schedule = config.Global.ActiveFirstWeekSchedule.Ref;

            Assert.Multiple(() =>
            {
                Assert.That(schedule.Days.Select(day => day.MatchesRequired), Is.EqualTo(new[] { 1, 1, 2, 2, 2, 3, 1 }));
                Assert.That(schedule.Days.Sum(day => day.MatchesRequired), Is.EqualTo(12), "a perfect run is twelve matches");

                Assert.That(schedule.Days.Select(day => day.Reward.AmountOf(CurrencyType.Coins)),
                    Is.EqualTo(new[] { 250, 300, 350, 400, 450, 500, 1500 }));

                Assert.That(schedule.Days.Sum(day => day.Reward.AmountOf(CurrencyType.Coins)), Is.EqualTo(3_750));
                Assert.That(schedule.Days.Sum(day => day.Reward.AmountOf(CurrencyType.Gems)), Is.EqualTo(100));
                Assert.That(schedule.Days.Sum(day => day.Reward.AmountOf(CurrencyType.SpinTokens)), Is.EqualTo(2));

                Assert.That(schedule.Days.Take(FirstWeekScheduleInfo.NumDays - 1).Sum(day => day.Reward.AmountOf(CurrencyType.Gems)), Is.Zero,
                    "gems are the capstone's alone");
                Assert.That(schedule.Days.Take(FirstWeekScheduleInfo.NumDays - 1).Sum(day => day.Reward.AmountOf(CurrencyType.SpinTokens)), Is.Zero,
                    "and so are the spin tokens");

                Assert.That(schedule.Days.Last().MatchesRequired, Is.EqualTo(1), "day seven is forgiving on purpose");
                Assert.That(schedule.Days.Select(day => day.Day), Is.EqualTo(new[] { 1, 2, 3, 4, 5, 6, 7 }));
                Assert.That(schedule.Days.Select(day => day.Id).Distinct().Count(), Is.EqualTo(FirstWeekScheduleInfo.NumDays));
            });
        }

        /// <summary>
        /// The active wheel table has the sectors of the approved baseline, value by value.
        /// <para>
        /// <c>GameConfigValidationTests</c> checks the structure. This test checks the <b>numbers</b>, which the
        /// published odds and the economy's first-week pacing depend on, so a change must be made there first.
        /// </para>
        /// </summary>
        [Test]
        public async Task ThePublishedWheelIsTheApprovedBaselineTable()
        {
            SharedGameConfig config = await GetCachedClientConfigAsync();
            WheelTableInfo   wheel  = config.Global.ActiveWheelTable.Ref;

            Assert.Multiple(() =>
            {
                Assert.That(wheel.Sectors.Select(sector => sector.Amount),
                    Is.EqualTo(new[] { 100, 500, 250, 30, 1, 1000, 250, 1, 0, 500 }));
                Assert.That(wheel.Sectors.Select(sector => sector.Currency), Is.EqualTo(new[]
                {
                    CurrencyType.Coins, CurrencyType.Coins, CurrencyType.Coins, CurrencyType.Gems, CurrencyType.SpinTokens,
                    CurrencyType.Coins, CurrencyType.Coins, CurrencyType.SpinTokens, CurrencyType.None, CurrencyType.Coins,
                }));

                // Expected value per spin, in hundredths.
                Assert.That(wheel.ExpectedHundredthsOf(CurrencyType.Coins), Is.EqualTo(26_000));
                Assert.That(wheel.ExpectedHundredthsOf(CurrencyType.Gems), Is.EqualTo(300));
                Assert.That(wheel.ExpectedHundredthsOf(CurrencyType.SpinTokens), Is.EqualTo(20));

                // The published odds, computed from the archive. The blank sector is the last entry.
                Assert.That(wheel.Odds().Select(entry => entry.ChancePercent), Is.EqualTo(new[] { 10, 20, 20, 10, 10, 20, 10 }));
                Assert.That(wheel.Odds().Sum(entry => entry.ChancePercent), Is.EqualTo(100));

                Assert.That(wheel.Sectors.Count(sector => sector.Tier == WheelPrizeTier.SpinAgain), Is.EqualTo(2),
                    "the wheel carries exactly two replacement-token sectors");
                Assert.That(wheel.Sectors.Count(sector => sector.IsBlank), Is.EqualTo(1),
                    "the wheel carries exactly one blank sector");
                Assert.That(wheel.Sectors.Select(sector => sector.Id).Distinct().Count(), Is.EqualTo(WheelTableInfo.NumSectors));
            });
        }

        /// <summary>
        /// The starting wallet pays for exactly one spin. This depends on both the game config and
        /// <see cref="SpinWheelPolicy.SpinCost"/>, which no config validation rule checks together.
        /// </summary>
        [Test]
        public async Task AFreshPlayerCanAffordExactlyOneSpin()
        {
            SharedGameConfig config = await GetCachedClientConfigAsync();

            Assert.That(config.Global.StartingWallet.AmountOf(CurrencyType.SpinTokens), Is.EqualTo(1));
            Assert.That(SpinWheelPolicy.SpinCost.Amount, Is.EqualTo(1));
            Assert.That(SpinWheelPolicy.SpinCost.Currency, Is.EqualTo(CurrencyType.SpinTokens));
        }

        /// <summary>
        /// The daily reset schedule in the archive is in player-local time, repeats daily without end, and starts
        /// at midnight, as the streak logic assumes. A UTC schedule would move every player's reset time.
        /// </summary>
        [Test]
        public async Task ThePublishedDailyResetIsAPlayerLocalDay()
        {
            SharedGameConfig config = await GetCachedClientConfigAsync();
            MetaRecurringCalendarSchedule reset = config.Global.DailyResetSchedule;

            Assert.That(reset, Is.Not.Null);
            Assert.That(reset.TimeMode, Is.EqualTo(MetaScheduleTimeMode.Local));
            Assert.That(DailyResetScheduleRules.IsOneDay(reset.Recurrence.Value), Is.True);
            Assert.That(DailyResetScheduleRules.IsOneDay(reset.Duration), Is.True);
            Assert.That(reset.NumRepeats, Is.Null);
            Assert.That(reset.Start.Hour, Is.Zero);
        }

        [Test]
        public async Task TheBuildsValidationEntryPointRunsTheGamesOwnChecks()
        {
            // The SDK calls BuildTimeValidate on every config it builds. This checks that the call runs the
            // game's own checks, so they cannot be skipped.
            //
            // Uses a separate import, because the entry replacement below modifies the config.
            SharedGameConfig config = await ImportFreshClientConfigAsync();

            // The entries have private setters, so replace one the same way the importer sets it.
            GameConfigRepository.Instance.GetGameConfigTypeInfo(typeof(SharedGameConfig)).Entries["Cosmetics"].SetEntry(
                config,
                GameConfigLibrary<CosmeticId, CosmeticInfo>.CreateSolo(
                    new List<CosmeticInfo> { new CosmeticInfo(CosmeticId.FromString("frame.free"), CosmeticKind.Frame, "Free Frame", CurrencyAmount.Coins(0)) }));

            GameConfigValidationFailed failure = Assert.Throws<GameConfigValidationFailed>(
                () => config.BuildTimeValidate(new GameConfigValidationResult("baseline")));

            Assert.That(failure.Message, Does.Contain("Cosmetics[frame.free].Price"));
        }

        /// <summary>
        /// The build tool refuses to write an archive with an error that the SDK's validators only <i>report</i>
        /// and do not throw.
        /// <para>
        /// The game's own checks throw. The SDK's validators instead add a message to the build report and return
        /// the archive, so a build that only checked for exceptions would write it and exit with success. The
        /// build tool checks the report instead. This test builds sheets with such an error: an offer group that
        /// lists the same offer twice.
        /// </para>
        /// </summary>
        [Test]
        public async Task AnErrorTheSdkOnlyReportsStillFailsTheBuild()
        {
            string brokenSources = Path.Combine(Path.GetTempPath(), "gameconfig-sheets-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(brokenSources);

            try
            {
                foreach (string sheet in Directory.GetFiles(SourcesDir(), "*.csv"))
                    File.Copy(sheet, Path.Combine(brokenSources, Path.GetFileName(sheet)));

                string offerGroupsCsvPath = Path.Combine(brokenSources, "OfferGroups.csv");
                string[] rows             = await File.ReadAllLinesAsync(offerGroupsCsvPath);
                int      row              = Array.FindIndex(rows, line => line.StartsWith(",,,,,starter-pack-2,", StringComparison.Ordinal));
                Assert.That(row, Is.GreaterThanOrEqualTo(0), "the catalogue group's second offer row moved; this fixture edits it by hand");
                rows[row] = rows[row].Replace("starter-pack-2", "starter-pack-1", StringComparison.Ordinal);
                await File.WriteAllLinesAsync(offerGroupsCsvPath, rows);

                // The build itself succeeds and returns an archive containing the error, so the build tool's
                // check must refuse it.
                ConfigArchive broken = await BuildArchiveAsync(brokenSources);

                Assert.That(
                    GameConfigMetaData.FromArchive(broken)?.BuildSummary?.HighestMessageLevel,
                    Is.EqualTo(GameConfigLogLevel.Error),
                    "a group listing one offer twice is an error the SDK reports rather than throws");

                Assert.That(
                    GameConfigBuildTool.BuildReportHasNoErrors(broken),
                    Is.False,
                    "the build command must refuse to write an archive carrying an error-level message");

                Assert.That(
                    GameConfigBuildTool.BuildReportHasNoErrors(await ShippedArchive.Value),
                    Is.True,
                    "and must accept the shipped sheets");
            }
            finally
            {
                Directory.Delete(brokenSources, recursive: true);
            }
        }
    }
}
