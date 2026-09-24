using System;
using Metaplay.Core;
using Metaplay.Core.Activables;
using Metaplay.Core.Config;
using Metaplay.Core.LiveOpsEvent;
using Metaplay.Core.Model;
using Metaplay.Core.Offers;
using Metaplay.Core.Player;
using Metaplay.Core.Schedule;
using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;

namespace Game.Logic.Tests
{
    /// <summary>
    /// Tests <see cref="GameConfigValidation"/>: each test gives the config build a value a designer could
    /// enter by mistake and checks that the build rejects it.
    /// <para>
    /// The tests validate a config built in this fixture, not the shipped content, so they assert rules rather
    /// than balance values that change during tuning.
    /// </para>
    /// </summary>
    [TestFixture]
    public class GameConfigValidationTests
    {
        #region Building configs to validate

        static void SetEntry(SharedGameConfig config, string entryName, IGameConfigEntry entry) => TestGameConfig.SetEntry(config, entryName, entry);

        static RewardBundle Coins(int amount) => new RewardBundle(CurrencyAmount.Coins(amount));

        static readonly DailyRewardTableId      DailyTable  = DailyRewardTableId.FromString("daily");
        static readonly FirstWeekScheduleId     WeekTable   = FirstWeekScheduleId.FromString("week");

        /// <summary>A first-week schedule that no Global field references, but that players part-way through a week may still hold.</summary>
        static readonly FirstWeekScheduleId RetiredWeekTable = FirstWeekScheduleId.FromString("week.retired");
        static readonly WheelTableId            Wheel                 = WheelTableId.FromString("wheel");
        static readonly MissionSetId            DailySet              = MissionSetId.FromString("daily.set");
        static readonly MissionSetId            WeeklySet             = MissionSetId.FromString("weekly.set");
        static readonly TournamentRewardTableId TournamentRewardTable = TournamentRewardTableId.FromString("tournament");
        static readonly CosmeticId              CheapFrame            = CosmeticId.FromString("frame.cheap");
        static readonly CosmeticId              CheapName             = CosmeticId.FromString("name.cheap");
        static readonly CosmeticId              CheapAvatar           = CosmeticId.FromString("avatar.cheap");

        /// <summary>
        /// Returns a valid cosmetics catalogue: one purchasable item in each slot, plus the default item of each
        /// slot that every player starts with, which validation requires by id (<c>docs/cosmetics.md</c>, "The
        /// starting three").
        /// </summary>
        static List<CosmeticInfo> TwoWellFormedCosmetics() =>
            new List<CosmeticInfo>
            {
                new CosmeticInfo(CheapFrame, CosmeticKind.Frame, "Cheap Frame", CurrencyAmount.Coins(500), isPurchasable: true, style: CosmeticStyle.FrameSilver),
                new CosmeticInfo(CheapName, CosmeticKind.NameEffect, "Cheap Name", CurrencyAmount.Coins(500), isPurchasable: true, style: CosmeticStyle.NameAzure),
                new CosmeticInfo(CheapAvatar, CosmeticKind.Avatar, "Cheap Face", CurrencyAmount.Coins(500), isPurchasable: true, style: CosmeticStyle.AvatarHeart),

                new CosmeticInfo(CosmeticDefaults.Avatar, CosmeticKind.Avatar, "Spade", price: null, isPurchasable: false,
                    style: CosmeticStyle.AvatarSpade, unlockRequirement: "Yours from the start"),
                new CosmeticInfo(CosmeticDefaults.Frame, CosmeticKind.Frame, "Plain Ring", price: null, isPurchasable: false,
                    style: CosmeticStyle.FramePlain, unlockRequirement: "Yours from the start"),
                new CosmeticInfo(CosmeticDefaults.NameEffect, CosmeticKind.NameEffect, "Plain", price: null, isPurchasable: false,
                    style: CosmeticStyle.NamePlain, unlockRequirement: "Yours from the start"),
            };

        /// <summary>Returns the valid catalogue with its purchasable frame replaced by <paramref name="frame"/>.</summary>
        static List<CosmeticInfo> CosmeticsWithFrame(CosmeticInfo frame)
        {
            List<CosmeticInfo> catalogue = TwoWellFormedCosmetics();
            catalogue[0] = frame;
            return catalogue;
        }

        static void SetCosmetics(SharedGameConfig config, List<CosmeticInfo> catalogue) =>
            SetEntry(config, "Cosmetics", GameConfigLibrary<CosmeticId, CosmeticInfo>.CreateSolo(catalogue));

        /// <summary>Returns the steps of a valid daily reward cycle, with consecutive positions from one and distinct ids.</summary>
        static List<DailyRewardStepInfo> SevenGrowingSteps()
        {
            List<RewardBundle> rewards = new List<RewardBundle>
            {
                Coins(100), Coins(120), Coins(150), Coins(180), Coins(220), Coins(260),
                new RewardBundle(CurrencyAmount.Coins(300), CurrencyAmount.SpinTokens(1)),
            };

            List<DailyRewardStepInfo> steps = new List<DailyRewardStepInfo>();
            for (int index = 0; index < rewards.Count; index++)
                steps.Add(DailyStep(index + 1, rewards[index]));
            return steps;
        }

        static DailyRewardStepInfo DailyStep(int step, RewardBundle reward) =>
            new DailyRewardStepInfo(DailyRewardStepId.FromString($"daily.step{step}"), step, reward);

        /// <summary>Returns the valid config with its daily reward table's steps replaced by <paramref name="steps"/>.</summary>
        static SharedGameConfig WithDailySteps(List<DailyRewardStepInfo> steps)
        {
            SharedGameConfig config = ValidConfig();
            SetEntry(config, "DailyRewards", GameConfigLibrary<DailyRewardTableId, DailyRewardTableInfo>.CreateSolo(
                new List<DailyRewardTableInfo> { new DailyRewardTableInfo(DailyTable, steps) }));
            return config;
        }

        /// <summary>
        /// Returns a daily reset schedule starting at midnight and repeating forever. With the default arguments it
        /// is valid: player-local, one day long and recurring daily.
        /// </summary>
        static MetaRecurringCalendarSchedule DailyReset(MetaScheduleTimeMode timeMode = MetaScheduleTimeMode.Local, MetaCalendarPeriod? period = null) =>
            new MetaRecurringCalendarSchedule(
                timeMode:   timeMode,
                start:      new MetaCalendarDateTime(2020, 1, 1, 0, 0, 0),
                duration:   period ?? DailyResetScheduleRules.OneDay,
                endingSoon: new MetaCalendarPeriod(),
                preview:    new MetaCalendarPeriod(),
                review:     new MetaCalendarPeriod(),
                recurrence: period ?? DailyResetScheduleRules.OneDay,
                numRepeats: null);

        /// <summary>Returns the days of a valid first-week schedule, with ids prefixed by <paramref name="prefix"/>.</summary>
        static List<FirstWeekDayInfo> SevenDays(string prefix = "week") =>
            new List<FirstWeekDayInfo>
            {
                FirstWeekDay(1, 1, Coins(250), prefix),
                FirstWeekDay(2, 1, Coins(300), prefix),
                FirstWeekDay(3, 2, Coins(350), prefix),
                FirstWeekDay(4, 2, Coins(400), prefix),
                FirstWeekDay(5, 2, Coins(450), prefix),
                FirstWeekDay(6, 3, Coins(500), prefix),
                FirstWeekDay(7, 1, new RewardBundle(CurrencyAmount.Coins(1500), CurrencyAmount.Gems(100), CurrencyAmount.SpinTokens(2)), prefix),
            };

        static FirstWeekDayInfo FirstWeekDay(int day, int matches, RewardBundle reward, string prefix = "week") =>
            new FirstWeekDayInfo(FirstWeekDayId.FromString($"{prefix}.day{day}"), day, matches, reward);

        /// <summary>
        /// Returns the sectors of a valid wheel. Each test that breaks the wheel starts from these and changes
        /// one thing.
        /// </summary>
        static List<WheelSectorInfo> TenSectors() =>
            new List<WheelSectorInfo>
            {
                Sector( 1, "s1", CurrencyAmount.Coins(100),    WheelPrizeTier.Common),
                Sector( 2, "s2", CurrencyAmount.Coins(500),    WheelPrizeTier.Uncommon),
                Sector( 3, "s3", CurrencyAmount.Coins(250),    WheelPrizeTier.Common),
                Sector( 4, "s4", CurrencyAmount.Gems(30),      WheelPrizeTier.Premium),
                Sector( 5, "s5", CurrencyAmount.SpinTokens(1), WheelPrizeTier.SpinAgain),
                Sector( 6, "s6", CurrencyAmount.Coins(1000),   WheelPrizeTier.Rare),
                Sector( 7, "s7", CurrencyAmount.Coins(250),    WheelPrizeTier.Common),
                Sector( 8, "s8", CurrencyAmount.SpinTokens(1), WheelPrizeTier.SpinAgain),
                Sector( 9, "s9", new RewardBundle(),           WheelPrizeTier.Nothing),
                Sector(10, "s10", CurrencyAmount.Coins(500),   WheelPrizeTier.Uncommon),
            };

        static WheelSectorInfo Sector(int position, string id, CurrencyAmount reward, WheelPrizeTier tier) =>
            new WheelSectorInfo(WheelSectorId.FromString(id), position, new RewardBundle(reward), tier);

        static WheelSectorInfo Sector(int position, string id, RewardBundle reward, WheelPrizeTier tier) =>
            new WheelSectorInfo(WheelSectorId.FromString(id), position, reward, tier);

        /// <summary>Returns the valid config with its missions replaced by <paramref name="missions"/>.</summary>
        static SharedGameConfig WithMissions(List<MissionInfo> missions)
        {
            SharedGameConfig config = ValidConfig();
            SetEntry(config, "Missions", GameConfigLibrary<MissionId, MissionInfo>.CreateSolo(missions));
            return config;
        }

        static List<MissionInfo> Missions() =>
            new List<MissionInfo>
            {
                new MissionInfo(MissionId.FromString("d1"), MissionCadence.Daily,  MissionObjective.MatchesCompleted, 1,  Coins(100)),
                new MissionInfo(MissionId.FromString("d2"), MissionCadence.Daily,  MissionObjective.MatchesCompleted, 3,  Coins(150)),
                new MissionInfo(MissionId.FromString("d3"), MissionCadence.Daily,  MissionObjective.MatchesWon,       1,  Coins(150)),
                new MissionInfo(MissionId.FromString("w1"), MissionCadence.Weekly, MissionObjective.MatchesCompleted, 10, new RewardBundle(CurrencyAmount.SpinTokens(1))),
                new MissionInfo(MissionId.FromString("w2"), MissionCadence.Weekly, MissionObjective.MatchesWon,       3,  new RewardBundle(CurrencyAmount.SpinTokens(1))),
            };

        /// <summary>Returns a valid player segment: a name, a description, and a condition that does not match every player.</summary>
        static DefaultPlayerSegmentInfo Segment(
            string                        id          = "cohort",
            PlayerCondition               condition   = null,
            string                        displayName = "A cohort",
            string                        description = "Players this offer is meant for.") =>
            new DefaultPlayerSegmentInfo(
                PlayerSegmentId.FromString(id),
                condition ?? PlayerSegmentConditions.AllOf(PlayerSegmentConditions.AtLeast(new PlayerPropertyGamesPlayed(), 5)),
                displayName,
                description);

        static void SetBots(SharedGameConfig config, List<BotProfileInfo> profiles, List<BotNameInfo> names)
        {
            SetEntry(config, "BotProfiles", GameConfigLibrary<BotProfileId, BotProfileInfo>.CreateSolo(profiles));
            SetEntry(config, "BotNames", GameConfigLibrary<BotNameId, BotNameInfo>.CreateSolo(names));
        }

        /// <summary>Returns the valid config with one more bot name row.</summary>
        static SharedGameConfig WithExtraBotName(string id, string name)
        {
            SharedGameConfig  config = ValidConfig();
            List<BotNameInfo> names  = TestBotConfig.NameRows();
            names.Add(new BotNameInfo(BotNameId.FromString(id), name));
            SetBots(config, TestBotConfig.ProfileRows(), names);
            return config;
        }

        static void SetSegments(SharedGameConfig config, params DefaultPlayerSegmentInfo[] segments) =>
            SetEntry(config, "PlayerSegments", GameConfigLibrary<PlayerSegmentId, DefaultPlayerSegmentInfo>.CreateSolo(segments));

        /// <summary>Returns a config that passes every check. Each test breaks one thing in it.</summary>
        static SharedGameConfig ValidConfig()
        {
            SharedGameConfig config = new SharedGameConfig();

            SetEntry(config, "DailyRewards", GameConfigLibrary<DailyRewardTableId, DailyRewardTableInfo>.CreateSolo(
                new List<DailyRewardTableInfo> { new DailyRewardTableInfo(DailyTable, SevenGrowingSteps()) }));

            SetEntry(config, "FirstWeekSchedules", GameConfigLibrary<FirstWeekScheduleId, FirstWeekScheduleInfo>.CreateSolo(
                new List<FirstWeekScheduleInfo> { new FirstWeekScheduleInfo(WeekTable, SevenDays()) }));

            SetEntry(config, "Missions", GameConfigLibrary<MissionId, MissionInfo>.CreateSolo(Missions()));

            SetEntry(config, "MissionSets", GameConfigLibrary<MissionSetId, MissionSetInfo>.CreateSolo(
                new List<MissionSetInfo>
                {
                    new MissionSetInfo(DailySet, MissionCadence.Daily, new List<MissionId> { MissionId.FromString("d1"), MissionId.FromString("d2"), MissionId.FromString("d3") }),
                    new MissionSetInfo(WeeklySet, MissionCadence.Weekly, new List<MissionId> { MissionId.FromString("w1"), MissionId.FromString("w2") }),
                }));

            SetEntry(config, "WheelTables", GameConfigLibrary<WheelTableId, WheelTableInfo>.CreateSolo(
                new List<WheelTableInfo> { new WheelTableInfo(Wheel, TenSectors()) }));

            SetCosmetics(config, TwoWellFormedCosmetics());

            SetEntry(config, "TournamentRewards", GameConfigLibrary<TournamentRewardTableId, TournamentRewardTableInfo>.CreateSolo(
                new List<TournamentRewardTableInfo>
                {
                    new TournamentRewardTableInfo(
                        TournamentRewardTable,
                        new List<TournamentMilestoneInfo> { new TournamentMilestoneInfo(1, Coins(250)), new TournamentMilestoneInfo(10, Coins(700)) },
                        new List<TournamentPlacementInfo> { new TournamentPlacementInfo(1, new RewardBundle(CurrencyAmount.Gems(50))), new TournamentPlacementInfo(5, Coins(250)) }),
                }));

            // An empty weekly-event template library is a build error, because every weekly event is created
            // from a template (docs/weekly-event.md).
            SetEntry(config, "WeeklyEventTemplates", GameConfigLibrary<LiveOpsEventTemplateId, WeeklyEventTemplateInfo>.CreateSolo(
                new List<WeeklyEventTemplateInfo>
                {
                    new WeeklyEventTemplateInfo(LiveOpsEventTemplateId.FromString("weekly.baseline"), WeeklyEventContentWith()),
                }));

            SetEntry(config, "PlayerIdentity", NameVocabulary());

            // Tables deal bots from both bot libraries, so an empty one is a build error (docs/bots.md).
            SetBots(config, TestBotConfig.ProfileRows(), TestBotConfig.NameRows());

            SetSegments(config, Segment());

            SetEntry(config, "Global", Global());

            return config;
        }

        /// <summary>
        /// Returns the Global entry of <see cref="ValidConfig"/>, with the given arguments replacing the valid
        /// values. A test passes only the fields it changes.
        /// </summary>
        static GlobalConfig Global(
            RewardBundle                  startingWallet   = null,
            int                           maxCoins         = 9_999_999,
            int                           maxGems          = 999_999,
            MetaRecurringCalendarSchedule dailyReset       = null,
            DailyRewardTableId            dailyRewardTable = null) =>
            new GlobalConfig(
                startingWallet:        startingWallet ?? new RewardBundle(CurrencyAmount.Coins(750), CurrencyAmount.SpinTokens(1)),
                maxCoins:              maxCoins,
                maxGems:               maxGems,
                maxSpinTokens:         999,
                dailyResetSchedule:    dailyReset ?? DailyReset(),
                dailyRewardTable:      dailyRewardTable ?? DailyTable,
                firstWeekSchedule:     WeekTable,
                wheelTable:            Wheel,
                dailyMissionSet:       DailySet,
                weeklyMissionSet:      WeeklySet,
                tournamentRewardTable: TournamentRewardTable);

        /// <summary>
        /// Returns a name vocabulary of <paramref name="words"/> generated adjectives and nouns. The words are not
        /// the shipped ones, because this fixture tests the checks and the config build checks the shipped list.
        /// </summary>
        static PlayerIdentityConfig NameVocabulary(int words = 48, int suffixCount = 100)
        {
            List<string> adjectives = new List<string>();
            List<string> nouns      = new List<string>();
            for (int index = 0; index < words; index++)
            {
                // The index as two base-26 letters, so every word is distinct, contains only letters, and is short
                // enough that adjective + noun + suffix fits the display-name length limit.
                string suffix = $"{(char)('a' + index / 26)}{(char)('a' + index % 26)}";
                adjectives.Add("Adj" + suffix);
                nouns.Add("Nou" + suffix);
            }

            return new PlayerIdentityConfig(adjectives, nouns, suffixCount, generatorVersion: 1);
        }

        /// <summary>
        /// Runs the checks, asserts that they fail, and returns the errors. Unless
        /// <paramref name="walkEveryGeneratedName"/> is set, skips the walk over every name the vocabulary can
        /// generate, which is slow and produces only its own error message.
        /// </summary>
        static IReadOnlyList<string> ExpectValidationErrors(SharedGameConfig config, bool walkEveryGeneratedName = false)
        {
            GameConfigValidationFailed failure = Assert.Throws<GameConfigValidationFailed>(
                () => GameConfigValidation.Validate(config, new GameConfigValidationResult("baseline"), walkEveryGeneratedName: walkEveryGeneratedName));
            return failure.Errors;
        }

        static void AssertPasses(SharedGameConfig config) => Assert.DoesNotThrow(() => GameConfigValidation.Validate(config, new GameConfigValidationResult("baseline")));

        /// <summary>Runs the load-time check, asserts that it refuses <paramref name="config"/>, and returns its message.</summary>
        static string OutdatedArchiveMessage(SharedGameConfig config) =>
            Assert.Throws<InvalidOperationException>(() => GameConfigValidation.ThrowIfArchiveIsOutdated(config)).Message;

        static void AssertMentions(IReadOnlyList<string> errors, string fragment)
        {
            Assert.That(errors.Any(error => error.Contains(fragment)), Is.True, $"expected an error mentioning '{fragment}', got: {string.Join(" | ", errors)}");
        }

        #endregion

        [Test]
        public void AWordListWithABlankOrADuplicateFailsTheBuild()
        {
            SharedGameConfig config = ValidConfig();
            PlayerIdentityConfig vocabulary = NameVocabulary();
            vocabulary.Adjectives.Add("   ");
            vocabulary.Nouns.Add(vocabulary.Nouns[0].ToUpperInvariant());
            SetEntry(config, "PlayerIdentity", vocabulary);

            IReadOnlyList<string> errors = ExpectValidationErrors(config);
            AssertMentions(errors, "blank word");
            AssertMentions(errors, "more than once");
        }

        [Test]
        public void AWordThatIsNotLettersFailsTheBuild()
        {
            // A word with a space or a hyphen would put punctuation into generated names, where the name rules
            // might refuse it.
            SharedGameConfig config = ValidConfig();
            PlayerIdentityConfig vocabulary = NameVocabulary();
            vocabulary.Nouns.Add("Two Words");
            SetEntry(config, "PlayerIdentity", vocabulary);

            AssertMentions(ExpectValidationErrors(config), "not letters only");
        }

        [Test]
        public void AVocabularyThatCanNameAPlayerSomethingRefusedFailsTheBuild()
        {
            // The word list is published over the air. Without this check, a bad word would first show up as a
            // generated name that the server refuses for a new player.
            SharedGameConfig config = ValidConfig();
            PlayerIdentityConfig vocabulary = NameVocabulary();
            vocabulary.Adjectives.Add("Cog");
            vocabulary.Nouns.Add(new string('x', 20));
            SetEntry(config, "PlayerIdentity", vocabulary);

            AssertMentions(ExpectValidationErrors(config, walkEveryGeneratedName: true), "names the server would refuse");
        }

        [Test]
        public void ATooNarrowVocabularyFailsTheBuild()
        {
            SharedGameConfig config = ValidConfig();
            SetEntry(config, "PlayerIdentity", NameVocabulary(words: 4, suffixCount: 10));

            AssertMentions(ExpectValidationErrors(config), "which is under the");
        }

        [Test]
        public void AMissingVocabularyFailsTheBuild()
        {
            SharedGameConfig config = ValidConfig();
            SetEntry(config, "PlayerIdentity", new PlayerIdentityConfig());

            AssertMentions(ExpectValidationErrors(config), "PlayerIdentity[PlayerIdentity].Adjectives");
        }

        [Test]
        public void AnArchiveWithNoVocabularyIsRefusedAtLoad()
        {
            // With no vocabulary, the name generator falls back to "Guest 1234" names instead of failing, so an
            // outdated archive would go unnoticed.
            SharedGameConfig config = ValidConfig();
            SetEntry(config, "PlayerIdentity", new PlayerIdentityConfig());

            Assert.That(OutdatedArchiveMessage(config), Does.Contain(nameof(SharedGameConfig.PlayerIdentity)));
        }

        [Test]
        public void TheShippedShapePasses()
        {
            AssertPasses(ValidConfig());
        }

        [Test]
        public void APriceOfZeroFailsTheBuild()
        {
            // A cleared price cell reads as zero, which is a likely tuning mistake.
            SharedGameConfig config = ValidConfig();
            SetCosmetics(config, CosmeticsWithFrame(
                new CosmeticInfo(CheapFrame, CosmeticKind.Frame, "Cheap Frame", CurrencyAmount.Coins(0), isPurchasable: true, style: CosmeticStyle.FrameSilver)));

            AssertMentions(ExpectValidationErrors(config), "Cosmetics[frame.cheap].Price");
        }

        [Test]
        public void ACosmeticPricedInSpinTokensFailsTheBuild()
        {
            // Spin tokens are spent only on wheel spins. A cosmetic priced in tokens would give tokens a second
            // use.
            SharedGameConfig config = ValidConfig();
            SetCosmetics(config, CosmeticsWithFrame(
                new CosmeticInfo(CheapFrame, CosmeticKind.Frame, "Cheap Frame", CurrencyAmount.SpinTokens(3), isPurchasable: true, style: CosmeticStyle.FrameSilver)));

            AssertMentions(ExpectValidationErrors(config), "priced in SpinTokens");
        }

        [Test]
        public void ADailyCycleThatShrinksFailsTheBuild()
        {
            List<DailyRewardStepInfo> steps = SevenGrowingSteps();
            steps[3] = DailyStep(4, Coins(10));

            AssertMentions(ExpectValidationErrors(WithDailySteps(steps)), "must not shrink");
        }

        [Test]
        public void ADailyCycleWithoutItsFinalSpinTokenFailsTheBuild()
        {
            List<DailyRewardStepInfo> steps = SevenGrowingSteps();
            steps[6] = DailyStep(7, Coins(300));

            AssertMentions(ExpectValidationErrors(WithDailySteps(steps)), "the last step grants exactly one");
        }

        [Test]
        public void ADailyCycleOfTheWrongLengthFailsTheBuild()
        {
            List<DailyRewardStepInfo> steps = SevenGrowingSteps();
            steps.RemoveAt(6);

            AssertMentions(ExpectValidationErrors(WithDailySteps(steps)), "exactly 7 entries");
        }

        [Test]
        public void ADailyCycleThatNamesAStepIdTwiceFailsTheBuild()
        {
            // Claims are recorded and reported by step id, so two steps with one id could not be told apart in
            // the analytics log.
            List<DailyRewardStepInfo> steps = SevenGrowingSteps();
            steps[2] = new DailyRewardStepInfo(steps[1].Id, 3, Coins(150));

            AssertMentions(ExpectValidationErrors(WithDailySteps(steps)), "more than once");
        }

        [Test]
        public void ADailyCycleWithASpinTokenBeforeTheLastStepFailsTheBuild()
        {
            // A spin token on an earlier step would make the last step less special than finishing the cycle
            // should be.
            List<DailyRewardStepInfo> steps = SevenGrowingSteps();
            steps[2] = DailyStep(3, new RewardBundle(CurrencyAmount.Coins(150), CurrencyAmount.SpinTokens(1)));

            AssertMentions(ExpectValidationErrors(WithDailySteps(steps)), "only the last step grants one");
        }

        [Test]
        public void ADailyCycleThatGrantsGemsFailsTheBuild()
        {
            // The daily reward pays only coins and the last step's spin token. Gems come from the first-week
            // capstone and the wheel, and the economy is not sized for gems from daily rewards.
            List<DailyRewardStepInfo> steps = SevenGrowingSteps();
            steps[1] = DailyStep(2, new RewardBundle(CurrencyAmount.Coins(120), CurrencyAmount.Gems(5)));

            AssertMentions(ExpectValidationErrors(WithDailySteps(steps)), "grants gems");
        }

        [Test]
        public void ADailyStepBiggerThanTheWalletCapFailsTheBuild()
        {
            // The wallet cannot hold a reward above its cap, so the claim would do nothing on the day it is due.
            List<DailyRewardStepInfo> steps = SevenGrowingSteps();
            steps[6] = DailyStep(7, new RewardBundle(CurrencyAmount.Coins(50_000_000), CurrencyAmount.SpinTokens(1)));

            AssertMentions(ExpectValidationErrors(WithDailySteps(steps)), "against a cap of");
        }

        [Test]
        public void ADailyResetInUtcFailsTheBuild()
        {
            // The design calls for a reset at player-local midnight. A UTC reset happens at a different local
            // hour for each player.
            SharedGameConfig config = ValidConfig();
            SetEntry(config, "Global", Global(dailyReset: DailyReset(timeMode: MetaScheduleTimeMode.Utc)));

            AssertMentions(ExpectValidationErrors(config), "the daily reset is player-local");
        }

        [Test]
        public void ADailyResetThatRecursTwiceADayFailsTheBuild()
        {
            // The streak counts schedule activations as days, so a schedule that recurs twice a day would make
            // every day-based rule count half-days.
            SharedGameConfig config = ValidConfig();
            SetEntry(config, "Global", Global(dailyReset: DailyReset(period: new MetaCalendarPeriod(0, 0, 0, 12, 0, 0))));

            AssertMentions(ExpectValidationErrors(config), "must recur exactly once a day");
        }

        [Test]
        public void AFirstWeekFinaleThatAsksForMoreThanOneMatchFailsTheBuild()
        {
            // The last day rewards returning to the game, not a long session.
            List<FirstWeekDayInfo> days = SevenDays();
            days[6] = FirstWeekDay(7, 3, new RewardBundle(CurrencyAmount.Coins(1500), CurrencyAmount.Gems(100), CurrencyAmount.SpinTokens(2)));

            AssertMentions(ExpectValidationErrors(WithFirstWeek(days)), "last day must ask for one match");
        }

        /// <summary>
        /// Checks that only the last day of the first week pays gems, so finishing the week stays the biggest
        /// reward.
        /// </summary>
        [Test]
        public void GemsOnAnOrdinaryFirstWeekDayFailTheBuild()
        {
            List<FirstWeekDayInfo> days = SevenDays();
            days[2] = FirstWeekDay(3, 2, new RewardBundle(CurrencyAmount.Coins(350), CurrencyAmount.Gems(10)));

            AssertMentions(ExpectValidationErrors(WithFirstWeek(days)), "only the last day pays premium currency");
        }

        /// <summary>
        /// Checks the coin band of the days before the last. Below the band a day is not worth returning for,
        /// and above it the last day's reward loses its value.
        /// </summary>
        [Test]
        public void AFirstWeekDayOutsideItsCoinBandFailsTheBuild()
        {
            List<FirstWeekDayInfo> days = SevenDays();
            days[1] = FirstWeekDay(2, 1, Coins(120));

            AssertMentions(ExpectValidationErrors(WithFirstWeek(days)), "outside the 250-500 band");
        }

        /// <summary>Checks the allowed range of matches a first-week day may ask for.</summary>
        [Test]
        public void AFirstWeekGoalOutsideTheAllowedBandFailsTheBuild()
        {
            List<FirstWeekDayInfo> days = SevenDays();
            days[3] = FirstWeekDay(4, 6, Coins(400));

            AssertMentions(ExpectValidationErrors(WithFirstWeek(days)), "outside the 1-3 a first-week day may ask for");
        }

        /// <summary>
        /// Checks that day ids are unique. Claims and progress are recorded by day id, so two days with one id
        /// could not be told apart.
        /// </summary>
        [Test]
        public void ARepeatedFirstWeekDayIdFailsTheBuild()
        {
            List<FirstWeekDayInfo> days = SevenDays();
            days[4] = new FirstWeekDayInfo(FirstWeekDayId.FromString("week.day4"), 5, 2, Coins(450));

            AssertMentions(ExpectValidationErrors(WithFirstWeek(days)), "names day id 'week.day4' more than once");
        }

        /// <summary>Checks the total a perfect week pays, which no per-day check covers.</summary>
        [Test]
        public void AFirstWeekTotalOutsideTheEconomysBandFailsTheBuild()
        {
            // Every day is inside its own band, but the total is below the week's band.
            List<FirstWeekDayInfo> days = SevenDays();
            for (int day = 1; day <= 6; day++)
                days[day - 1] = FirstWeekDay(day, days[day - 1].MatchesRequired, Coins(250));
            days[6] = FirstWeekDay(7, 1, new RewardBundle(CurrencyAmount.Coins(1000), CurrencyAmount.Gems(100), CurrencyAmount.SpinTokens(2)));

            AssertMentions(ExpectValidationErrors(WithFirstWeek(days)), "for a perfect run, outside the 3250-5000");
        }

        /// <summary>Checks the last day's coin, gem and spin-token bands. It is the only day that pays gems and spin tokens.</summary>
        [Test]
        public void ACapstoneOutsideItsBandsFailsTheBuild()
        {
            List<FirstWeekDayInfo> days = SevenDays();
            days[6] = FirstWeekDay(7, 1, new RewardBundle(CurrencyAmount.Coins(1500), CurrencyAmount.Gems(400), CurrencyAmount.SpinTokens(2)));

            AssertMentions(ExpectValidationErrors(WithFirstWeek(days)), "gems, outside the 50-100 the capstone keeps");
        }

        /// <summary>
        /// Checks that the first-week schedule named in Global exists in the library. A missing schedule would
        /// give every new player no first-week event.
        /// </summary>
        [Test]
        public void AnActiveFirstWeekPointerAtAnUnknownScheduleFailsTheBuild()
        {
            SharedGameConfig config = ValidConfig();
            SetEntry(config, "FirstWeekSchedules", GameConfigLibrary<FirstWeekScheduleId, FirstWeekScheduleInfo>.CreateSolo(
                new List<FirstWeekScheduleInfo> { new FirstWeekScheduleInfo(FirstWeekScheduleId.FromString("somewhere.else"), SevenDays()) }));

            AssertMentions(ExpectValidationErrors(config), "which is not in the library");
        }

        /// <summary>
        /// Checks every first-week reward against the wallet caps. A reward above a cap can never be paid, so
        /// the Claim would do nothing on the day it is due.
        /// </summary>
        [Test]
        public void AFirstWeekRewardBiggerThanTheWalletCapFailsTheBuild()
        {
            SharedGameConfig config = ValidConfig();
            SetEntry(config, "Global", Global(maxGems: 50));

            AssertMentions(ExpectValidationErrors(config), "grants 100 Gems on day 7");
        }

        /// <summary>
        /// Checks every schedule in the library against the caps, not only the one Global names. A player's
        /// model keeps the id of the schedule they started for their whole week, and the schedule stays in the
        /// archive so their claims still resolve. Lowering a cap must not leave that player's last day
        /// unclaimable.
        /// </summary>
        [Test]
        public void ARetiredFirstWeekSchedulePlayersAreStillOnIsCheckedAgainstTheCapsToo()
        {
            SharedGameConfig config = ValidConfig();
            SetEntry(config, "FirstWeekSchedules", GameConfigLibrary<FirstWeekScheduleId, FirstWeekScheduleInfo>.CreateSolo(
                new List<FirstWeekScheduleInfo>
                {
                    new FirstWeekScheduleInfo(WeekTable, SevenDays()),
                    new FirstWeekScheduleInfo(RetiredWeekTable, SevenDays("retired")),
                }));
            SetEntry(config, "Global", Global(maxGems: 50));

            AssertMentions(ExpectValidationErrors(config), $"'{RetiredWeekTable}' grants 100 Gems on day 7");
        }

        /// <summary>Returns the valid config with its first-week schedule days replaced by <paramref name="days"/>.</summary>
        static SharedGameConfig WithFirstWeek(List<FirstWeekDayInfo> days)
        {
            SharedGameConfig config = ValidConfig();
            SetEntry(config, "FirstWeekSchedules", GameConfigLibrary<FirstWeekScheduleId, FirstWeekScheduleInfo>.CreateSolo(
                new List<FirstWeekScheduleInfo> { new FirstWeekScheduleInfo(WeekTable, days) }));
            return config;
        }

        [Test]
        public void AWeeklyMissionPayingCoinsFailsTheBuild()
        {
            // Daily missions pay coins and weekly missions pay spin tokens. A weekly mission paying coins would
            // reduce the spin-token supply the wheel depends on.
            List<MissionInfo> missions = Missions();
            missions[3] = new MissionInfo(MissionId.FromString("w1"), MissionCadence.Weekly, MissionObjective.MatchesCompleted, 10, Coins(400));

            AssertMentions(ExpectValidationErrors(WithMissions(missions)), "weekly mission must pay spin tokens");
        }

        [Test]
        public void AMissionPayingGemsFailsTheBuild()
        {
            List<MissionInfo> missions = Missions();
            missions[0] = new MissionInfo(MissionId.FromString("d1"), MissionCadence.Daily, MissionObjective.MatchesCompleted, 1, new RewardBundle(CurrencyAmount.Coins(100), CurrencyAmount.Gems(5)));

            AssertMentions(ExpectValidationErrors(WithMissions(missions)), "grants gems");
        }

        [Test]
        public void ADailyMissionAskingForMoreMatchesThanTheBandAllowsFailsTheBuild()
        {
            // A target above the band makes the mission a chore, and nothing but the config build would catch
            // it.
            List<MissionInfo> missions = Missions();
            missions[1] = new MissionInfo(MissionId.FromString("d2"), MissionCadence.Daily, MissionObjective.MatchesCompleted, 9, Coins(150));

            AssertMentions(ExpectValidationErrors(WithMissions(missions)), "outside the 1-4 a Daily MatchesCompleted mission may ask for");
        }

        [Test]
        public void ADailySetOutsideTheEconomysCoinBandFailsTheBuild()
        {
            // The economy is balanced against the coins a completed daily set pays. A total outside the band is
            // an economy change and must be made in the validation rule, not in the sheet alone.
            List<MissionInfo> missions = Missions();
            missions[0] = new MissionInfo(MissionId.FromString("d1"), MissionCadence.Daily, MissionObjective.MatchesCompleted, 1, Coins(1000));

            AssertMentions(ExpectValidationErrors(WithMissions(missions)), "outside the 300-700 the economy is balanced against");
        }

        [Test]
        public void AWeeklySetPayingMoreThanTwoSpinTokensFailsTheBuild()
        {
            List<MissionInfo> missions = Missions();
            missions[4] = new MissionInfo(MissionId.FromString("w2"), MissionCadence.Weekly, MissionObjective.MatchesWon, 3, new RewardBundle(CurrencyAmount.SpinTokens(4)));

            AssertMentions(ExpectValidationErrors(WithMissions(missions)), "must be at most 2, is 5");
        }

        [Test]
        public void ASetAskingForTheSameThingTwiceFailsTheBuild()
        {
            // Two missions with the same objective and target complete on the same game, so they are one
            // mission shown twice.
            List<MissionInfo> missions = Missions();
            missions[1] = new MissionInfo(MissionId.FromString("d2"), MissionCadence.Daily, MissionObjective.MatchesCompleted, 1, Coins(150));

            AssertMentions(ExpectValidationErrors(WithMissions(missions)), "asks for 1 MatchesCompleted twice");
        }

        [Test]
        public void AMissionSetHoldingTheWrongCadenceFailsTheBuild()
        {
            SharedGameConfig config = ValidConfig();
            SetEntry(config, "MissionSets", GameConfigLibrary<MissionSetId, MissionSetInfo>.CreateSolo(
                new List<MissionSetInfo>
                {
                    new MissionSetInfo(DailySet, MissionCadence.Daily, new List<MissionId> { MissionId.FromString("d1"), MissionId.FromString("d2"), MissionId.FromString("w1") }),
                    new MissionSetInfo(WeeklySet, MissionCadence.Weekly, new List<MissionId> { MissionId.FromString("w1"), MissionId.FromString("w2") }),
                }));

            AssertMentions(ExpectValidationErrors(config), "which is a Weekly mission");
        }

        [Test]
        public void AWheelOfTheWrongSizeFailsTheBuild()
        {
            // The wheel's odds come from its equal sectors, so the sector count is fixed.
            List<WheelSectorInfo> sectors = TenSectors();
            sectors.RemoveAt(9);

            AssertMentions(ExpectValidationErrors(WithWheel(sectors)), "exactly 10 entries");
        }

        [Test]
        public void AWheelSectorThatGrantsNothingFailsTheBuild()
        {
            // Only a Nothing-tier sector may grant nothing. An empty reward on another tier would be an
            // unlisted blank.
            SharedGameConfig config = WheelWith(3, Sector(3, "s3", new RewardBundle(), WheelPrizeTier.Common));

            AssertMentions(ExpectValidationErrors(config), "must grant something");
        }

        /// <summary>Returns the valid config with its wheel's sectors replaced by <paramref name="sectors"/>.</summary>
        static SharedGameConfig WithWheel(List<WheelSectorInfo> sectors)
        {
            SharedGameConfig config = ValidConfig();
            SetEntry(config, "WheelTables", GameConfigLibrary<WheelTableId, WheelTableInfo>.CreateSolo(
                new List<WheelTableInfo> { new WheelTableInfo(Wheel, sectors) }));
            return config;
        }

        /// <summary>Returns the valid config with the wheel sector at <paramref name="position"/> replaced by <paramref name="sector"/>.</summary>
        static SharedGameConfig WheelWith(int position, WheelSectorInfo sector)
        {
            List<WheelSectorInfo> sectors = TenSectors();
            sectors[position - 1] = sector;
            return WithWheel(sectors);
        }

        [Test]
        public void AWheelSectorPayingTwoCurrenciesFailsTheBuild()
        {
            // A sector is one result. The odds sheet lists one row per single-currency prize, so a sector paying
            // two currencies would have no row.
            SharedGameConfig config = WheelWith(3, Sector(3, "s3", new RewardBundle(CurrencyAmount.Coins(250), CurrencyAmount.Gems(10)), WheelPrizeTier.Common));

            AssertMentions(ExpectValidationErrors(config), "a sector pays exactly one");
        }

        [Test]
        public void AWheelSectorOutsideTheCoinBandFailsTheBuild()
        {
            SharedGameConfig config = WheelWith(3, Sector(3, "s3", CurrencyAmount.Coins(50), WheelPrizeTier.Common));

            AssertMentions(ExpectValidationErrors(config), "outside the 100-1000 band");
        }

        [Test]
        public void ATableWithOnlyOneReplacementTokenSectorFailsTheBuild()
        {
            // The published odds are counted from the sectors, and the screen promises two spin-again sectors,
            // so the count is fixed.
            SharedGameConfig config = WheelWith(5, Sector(5, "s5", CurrencyAmount.Coins(100), WheelPrizeTier.Common));

            AssertMentions(ExpectValidationErrors(config), "has 1 spin-again sectors; the wheel has exactly two");
        }

        [Test]
        public void ATableWithTwoBlanksFailsTheBuild()
        {
            // The design has exactly one blank sector, and the published odds count on it.
            SharedGameConfig config = WheelWith(10, Sector(10, "s10", new RewardBundle(), WheelPrizeTier.Nothing));

            AssertMentions(ExpectValidationErrors(config), "has 2 blank sectors; the wheel has exactly one");
        }

        [Test]
        public void AJackpotThatIsNotTheLargestCoinPrizeFails()
        {
            // The Rare tier is shown as the jackpot, so its coin prize must be the largest on the wheel.
            SharedGameConfig config = WheelWith(6, Sector(6, "s6", CurrencyAmount.Coins(250), WheelPrizeTier.Rare));

            AssertMentions(ExpectValidationErrors(config), "the jackpot is the largest coin prize");
        }

        [Test]
        public void ATableWithoutABlankSectorFailsTheBuild()
        {
            // The odds sheet lists the blank, so a wheel without one would publish odds that do not match it.
            SharedGameConfig config = WheelWith(9, Sector(9, "s9", CurrencyAmount.Coins(100), WheelPrizeTier.Common));

            AssertMentions(ExpectValidationErrors(config), "has 0 blank sectors; the wheel has exactly one");
        }

        [Test]
        public void AWheelWorthTooMuchPerSpinFailsTheBuild()
        {
            // The sector counts are valid, but the ordinary coin sectors are raised so that the average value of
            // a spin is above the allowed band.
            List<WheelSectorInfo> sectors = new List<WheelSectorInfo>
            {
                Sector( 1, "s1",  CurrencyAmount.Coins(600),    WheelPrizeTier.Common),
                Sector( 2, "s2",  CurrencyAmount.Coins(600),    WheelPrizeTier.Uncommon),
                Sector( 3, "s3",  CurrencyAmount.Coins(600),    WheelPrizeTier.Common),
                Sector( 4, "s4",  CurrencyAmount.Gems(30),      WheelPrizeTier.Premium),
                Sector( 5, "s5",  CurrencyAmount.SpinTokens(1), WheelPrizeTier.SpinAgain),
                Sector( 6, "s6",  CurrencyAmount.Coins(1000),   WheelPrizeTier.Rare),
                Sector( 7, "s7",  CurrencyAmount.Coins(600),    WheelPrizeTier.Common),
                Sector( 8, "s8",  CurrencyAmount.SpinTokens(1), WheelPrizeTier.SpinAgain),
                Sector( 9, "s9",  new RewardBundle(),           WheelPrizeTier.Nothing),
                Sector(10, "s10", CurrencyAmount.Coins(600),    WheelPrizeTier.Common),
            };

            AssertMentions(ExpectValidationErrors(WithWheel(sectors)), "hundredths of a coin a spin on average");
        }

        [Test]
        public void AWheelSectorDrawnAsTheWrongTierFailsTheBuild()
        {
            // The tier only affects presentation, but a gem sector shown as a coin tier would misrepresent the
            // prize.
            SharedGameConfig config = WheelWith(4, Sector(4, "s4", CurrencyAmount.Gems(30), WheelPrizeTier.Common));

            AssertMentions(ExpectValidationErrors(config), "pays gems but is drawn as Common");
        }

        [Test]
        public void AWheelWithARepeatedSectorIdFailsTheBuild()
        {
            // A resolved spin is recorded by sector id, so two sectors with one id could not be told apart in
            // the analytics log.
            SharedGameConfig config = WheelWith(3, Sector(3, "s1", CurrencyAmount.Coins(250), WheelPrizeTier.Common));

            AssertMentions(ExpectValidationErrors(config), "more than once");
        }

        [Test]
        public void AWheelAuthoredOutOfDrawnOrderFailsTheBuild()
        {
            SharedGameConfig config = WheelWith(3, Sector(7, "s3", CurrencyAmount.Coins(250), WheelPrizeTier.Common));

            AssertMentions(ExpectValidationErrors(config), "the table is authored in drawn order");
        }

        [Test]
        public void AWheelSectorAboveAWalletCapFailsTheBuild()
        {
            // Neither the wheel table nor the Global caps can check the other on its own. The wheel refuses to
            // spin while any sector would overflow a cap, so one oversized sector disables the whole wheel.
            SharedGameConfig config = ValidConfig();
            SetEntry(config, "Global", Global(
                startingWallet: new RewardBundle(CurrencyAmount.Coins(3_000), CurrencyAmount.SpinTokens(1)),
                maxGems: 20));

            AssertMentions(ExpectValidationErrors(config), "pays 30 Gems against a cap of 20");
        }

        [Test]
        public void AStartingWalletWithNoTokenCannotPayForASpinAndFailsTheBuild()
        {
            // The first session must be able to spin the wheel. The starting wallet sets the tokens and the wheel
            // sets the price, so only a cross-library check can compare them.
            SharedGameConfig config = ValidConfig();
            SetEntry(config, "Global", Global(
                startingWallet: new RewardBundle(CurrencyAmount.Coins(3_000), CurrencyAmount.Gems(100))));

            AssertMentions(ExpectValidationErrors(config), "which the starting wallet cannot pay");
        }

        [Test]
        public void AStartingWalletThatCannotAffordAnythingFailsTheBuild()
        {
            // The starting wallet and the cosmetics catalogue can each be valid alone but leave a new player
            // unable to buy anything.
            SharedGameConfig config = ValidConfig();
            SetEntry(config, "Global", Global(
                startingWallet: new RewardBundle(CurrencyAmount.Coins(10), CurrencyAmount.SpinTokens(1))));

            AssertMentions(ExpectValidationErrors(config), "does not buy the cheapest coin-priced cosmetic");
        }

        [Test]
        public void AStartingWalletThatAlreadyBuysAGemCosmeticFailsTheBuild()
        {
            // The opposite of the coin check: a new player must not be able to afford a gem-priced cosmetic,
            // because gems are the currency a new player saves up for.
            SharedGameConfig config = ValidConfig();
            SetCosmetics(config, CosmeticsWithFrame(
                new CosmeticInfo(CheapFrame, CosmeticKind.Frame, "Cheap Frame", CurrencyAmount.Gems(50), isPurchasable: true, style: CosmeticStyle.FrameSilver)));
            SetEntry(config, "Global", Global(
                startingWallet: new RewardBundle(CurrencyAmount.Coins(750), CurrencyAmount.Gems(100), CurrencyAmount.SpinTokens(1))));

            AssertMentions(ExpectValidationErrors(config), "already buys the cheapest gem-priced cosmetic");
        }

        [Test]
        public void ACosmeticWithNoStyleFailsTheBuild()
        {
            // A cosmetic with no style can be bought and equipped, but renders as nothing on every screen that
            // shows it.
            SharedGameConfig config = ValidConfig();
            SetCosmetics(config, CosmeticsWithFrame(
                new CosmeticInfo(CheapFrame, CosmeticKind.Frame, "Cheap Frame", CurrencyAmount.Coins(500))));

            AssertMentions(ExpectValidationErrors(config), "names no style");
        }

        [Test]
        public void ACosmeticWearingAnotherSlotsStyleFailsTheBuild()
        {
            SharedGameConfig config = ValidConfig();
            SetCosmetics(config, CosmeticsWithFrame(
                new CosmeticInfo(CheapFrame, CosmeticKind.Frame, "Cheap Frame", CurrencyAmount.Coins(500), isPurchasable: true, style: CosmeticStyle.NameAzure)));

            AssertMentions(ExpectValidationErrors(config), "is a Frame wearing the NameEffect style");
        }

        [Test]
        public void AnAvatarWithNoStyleFailsTheBuild()
        {
            // An avatar with no style can be bought and equipped, but renders as the default avatar.
            SharedGameConfig config = ValidConfig();
            List<CosmeticInfo> catalogue = TwoWellFormedCosmetics();
            catalogue.Add(new CosmeticInfo(CosmeticId.FromString("avatar.face"), CosmeticKind.Avatar, "A Face", CurrencyAmount.Coins(500)));
            SetCosmetics(config, catalogue);

            AssertMentions(ExpectValidationErrors(config), "names no style");
        }

        [Test]
        public void AnAvatarWearingAnotherSlotsStyleFailsTheBuild()
        {
            SharedGameConfig config = ValidConfig();
            List<CosmeticInfo> catalogue = TwoWellFormedCosmetics();
            catalogue.Add(new CosmeticInfo(CosmeticId.FromString("avatar.face"), CosmeticKind.Avatar, "A Face", CurrencyAmount.Coins(500),
                isPurchasable: true, style: CosmeticStyle.FrameGold));
            SetCosmetics(config, catalogue);

            AssertMentions(ExpectValidationErrors(config), "wearing the Frame style");
        }

        [Test]
        public void ASlotWithNothingForSaleFailsTheBuild()
        {
            // The shop's cosmetics grid has no design for a slot tab with nothing for sale.
            SharedGameConfig config = ValidConfig();
            SetCosmetics(config, new List<CosmeticInfo> { TwoWellFormedCosmetics()[0] });

            AssertMentions(ExpectValidationErrors(config), "has nothing for sale in the NameEffect slot");
        }

        [Test]
        public void AnEarnedCosmeticThatSaysNothingAboutHowItIsEarnedFailsTheBuild()
        {
            SharedGameConfig config = ValidConfig();
            List<CosmeticInfo> catalogue = TwoWellFormedCosmetics();
            catalogue.Add(new CosmeticInfo(CosmeticId.FromString("frame.mystery"), CosmeticKind.Frame, "Mystery", price: null,
                isPurchasable: false, style: CosmeticStyle.FrameGold));
            SetCosmetics(config, catalogue);

            AssertMentions(ExpectValidationErrors(config), "says nothing about how it is earned");
        }

        [Test]
        public void APlacementBandAwardingSomethingTheShopSellsFailsTheBuild()
        {
            // MetaRef checks that the prize exists. This check also requires that the shop does not sell it, so
            // the prize can only be earned by winning a season.
            SharedGameConfig config = ValidConfig();
            SetEntry(config, "TournamentRewards", GameConfigLibrary<TournamentRewardTableId, TournamentRewardTableInfo>.CreateSolo(
                new List<TournamentRewardTableInfo>
                {
                    new TournamentRewardTableInfo(
                        TournamentRewardTable,
                        new List<TournamentMilestoneInfo> { new TournamentMilestoneInfo(1, Coins(250)), new TournamentMilestoneInfo(10, Coins(700)) },
                        new List<TournamentPlacementInfo>
                        {
                            new TournamentPlacementInfo(1, new RewardBundle(CurrencyAmount.Gems(50)), CheapFrame),
                            new TournamentPlacementInfo(5, Coins(250)),
                        }),
                }));

            AssertMentions(ExpectValidationErrors(config), "and the shop sells the same item");
        }

        [Test]
        public void AStartingWalletWithoutASpinTokenFailsTheBuild()
        {
            SharedGameConfig config = ValidConfig();
            SetEntry(config, "Global", Global(startingWallet: new RewardBundle(CurrencyAmount.Coins(750))));

            AssertMentions(ExpectValidationErrors(config), "must include a spin token");
        }

        [Test]
        public void AnActivePointerAtAMissingTableFailsTheBuild()
        {
            SharedGameConfig config = ValidConfig();
            SetEntry(config, "Global", Global(dailyRewardTable: DailyRewardTableId.FromString("daily.v99")));

            AssertMentions(ExpectValidationErrors(config), "which is not in the library");
        }

        [Test]
        public void EveryProblemIsReportedNotJustTheFirst()
        {
            // The build reports every error at once, so fixing several mistakes does not take one build each.
            SharedGameConfig config = ValidConfig();
            SetCosmetics(config, new List<CosmeticInfo>
            {
                new CosmeticInfo(CosmeticId.FromString("a"), CosmeticKind.Frame, "A", CurrencyAmount.Coins(0), isPurchasable: true, style: CosmeticStyle.FrameSilver),
                new CosmeticInfo(CosmeticId.FromString("b"), CosmeticKind.NameEffect, "B", CurrencyAmount.Coins(-5), isPurchasable: true, style: CosmeticStyle.NameAzure),
            });

            Assert.That(ExpectValidationErrors(config), Has.Count.GreaterThanOrEqualTo(2));
        }

        [Test]
        public void AMissionSetNamingAMissingMissionSaysWhichSetAndWhichMission()
        {
            SharedGameConfig config = ValidConfig();
            SetEntry(config, "MissionSets", GameConfigLibrary<MissionSetId, MissionSetInfo>.CreateSolo(
                new List<MissionSetInfo>
                {
                    new MissionSetInfo(DailySet, MissionCadence.Daily, new List<MissionId> { MissionId.FromString("d1"), MissionId.FromString("d2"), MissionId.FromString("gone") }),
                    new MissionSetInfo(WeeklySet, MissionCadence.Weekly, new List<MissionId> { MissionId.FromString("w1"), MissionId.FromString("w2") }),
                }));

            // The SDK's reference check also fails the build, but its message names neither the set nor the
            // mission.
            AssertMentions(ExpectValidationErrors(config), "MissionSets[daily.set].Missions: names mission 'gone', which is not in the library");
        }

        [Test]
        public void AWalletCapAboveTheSafeCeilingFailsTheBuild()
        {
            // WalletLimits.MaxSafeCap keeps a cap far enough below int.MaxValue that adding a grant to a full
            // balance cannot overflow.
            SharedGameConfig config = ValidConfig();
            SetEntry(config, "Global", Global(maxCoins: WalletLimits.MaxSafeCap + 1));

            AssertMentions(ExpectValidationErrors(config), "Global[Global].MaxCoins");
        }

        /// <summary>
        /// Every config entry is optional, so an archive built before an entry existed imports without error.
        /// <see cref="GameConfigValidation.ThrowIfArchiveIsOutdated"/> refuses it at load instead of letting a
        /// player session read a null reference, and names what is missing and the tool that rebuilds it.
        /// <para>
        /// It also refuses missing wallet limits. A default <see cref="GlobalConfig"/> has no starting wallet and
        /// caps of zero, which would look like a valid economy rather than an outdated archive.
        /// </para>
        /// </summary>
        [TestCase("ActiveWheelTable")]
        [TestCase("tools/GameConfigGen")]
        [TestCase(nameof(GlobalConfig.StartingWallet))]
        [TestCase(nameof(GlobalConfig.MaxCoins))]
        [TestCase(nameof(GlobalConfig.MaxGems))]
        [TestCase(nameof(GlobalConfig.MaxSpinTokens))]
        public void AConfigOlderThanTheEntriesTheGameReadsIsRefusedAtLoad(string expectedInMessage)
        {
            Assert.That(OutdatedArchiveMessage(new SharedGameConfig()), Does.Contain(expectedInMessage));
        }

        [Test]
        public void ThePublishedOddsAreReadFromTheTable()
        {
            // The odds screen renders ChancePercentOf rather than an authored string, so it matches the wheel.
            WheelTableInfo wheel = new WheelTableInfo(Wheel, TenSectors());

            Assert.That(wheel.ChancePercentOf(CurrencyType.Coins), Is.EqualTo(60));
            Assert.That(wheel.ChancePercentOf(CurrencyType.Gems), Is.EqualTo(10));
            Assert.That(wheel.ChancePercentOf(CurrencyType.SpinTokens), Is.EqualTo(20));
        }

        #region Player segments

        /// <summary>
        /// Checks that a segment has a display name and a description. The Dashboard shows both, and the sample
        /// targets authored segments so that an operator can read them (<c>docs/offers.md</c>).
        /// </summary>
        [Test]
        public void ASegmentWithNoNameOrNoDescriptionFailsTheBuild()
        {
            SharedGameConfig config = ValidConfig();
            SetSegments(config, Segment(id: "nameless", displayName: " "), Segment(id: "mute", description: null));

            IReadOnlyList<string> errors = ExpectValidationErrors(config);
            AssertMentions(errors, "no display name");
            AssertMentions(errors, "no description");
        }

        /// <summary>
        /// Checks that a segment with no conditions fails. It would match every player while appearing to
        /// target a cohort, and nothing on screen would show the mistake.
        /// </summary>
        [Test]
        public void ASegmentWithNoConditionsFailsTheBuild()
        {
            SharedGameConfig config = ValidConfig();
            SetSegments(config, Segment(condition: PlayerSegmentConditions.AllOf()));

            AssertMentions(ExpectValidationErrors(config), "matches every player");
        }

        /// <summary>
        /// Checks that a segment conditions on each property once. The SDK combines two ranges on one property
        /// with AND, so the pair is either redundant or matches nobody.
        /// </summary>
        [Test]
        public void ASegmentConditioningOnOnePropertyTwiceFailsTheBuild()
        {
            SharedGameConfig config = ValidConfig();
            SetSegments(config, Segment(condition: PlayerSegmentConditions.AllOf(
                PlayerSegmentConditions.AtLeast(new PlayerPropertyGems(), 100),
                PlayerSegmentConditions.Between(new PlayerPropertyGems(), 0, 50))));

            AssertMentions(ExpectValidationErrors(config), "Gem balance twice");
        }

        /// <summary>
        /// Checks that a game-defined <see cref="PlayerCondition"/> subclass fails. A boundary in compiled code
        /// cannot be changed over the air, and the Dashboard shows an operator only the serialized condition
        /// (<c>docs/offers.md</c>, "Player segments"). The error message names the rejected type.
        /// </summary>
        [Test]
        public void ASegmentWithACustomPlayerConditionFailsTheBuild()
        {
            SharedGameConfig config = ValidConfig();
            SetSegments(config, Segment(condition: new GameDefinedPlayerCondition()));

            AssertMentions(ExpectValidationErrors(config), "GameDefinedPlayerCondition condition");
        }

        /// <summary>
        /// A game-defined condition, for the test that checks the build refuses one.
        /// </summary>
        [MetaSerializableDerived(9000)]
        public class GameDefinedPlayerCondition : PlayerCondition
        {
            public override bool MatchesPlayer(IPlayerModelBase player) => true;

            public override IEnumerable<PlayerSegmentId> GetSegmentReferences() => Enumerable.Empty<PlayerSegmentId>();
        }

        /// <summary>
        /// Checks that a segment's balance threshold is not above that currency's cap, which no player could
        /// reach. The segment and the cap are in different libraries, so only a cross-library check can compare
        /// them.
        /// </summary>
        [Test]
        public void ASegmentAskingForMoreCurrencyThanTheCapAllowsFailsTheBuild()
        {
            SharedGameConfig config = ValidConfig();
            SetSegments(config, Segment(condition: PlayerSegmentConditions.AllOf(
                PlayerSegmentConditions.AtLeast(new PlayerPropertyGems(), 5_000_000))));

            AssertMentions(ExpectValidationErrors(config), "above the cap");
        }

        /// <summary>Checks that a threshold equal to the cap is accepted, because a player can reach it.</summary>
        [Test]
        public void ASegmentAskingForExactlyTheCapIsAccepted()
        {
            SharedGameConfig config = ValidConfig();
            SetSegments(config, Segment(condition: PlayerSegmentConditions.AllOf(
                PlayerSegmentConditions.AtLeast(new PlayerPropertyGems(), 999_999))));

            AssertPasses(config);
        }

        /// <summary>
        /// Checks that an archive with no segments is refused at load. Every player would get the untargeted
        /// fallback, which would look like targeting was turned off rather than like an outdated archive.
        /// </summary>
        [Test]
        public void AnArchiveWithNoSegmentsIsRefusedAtLoad()
        {
            SharedGameConfig config = ValidConfig();
            SetEntry(config, "PlayerSegments", GameConfigLibrary<PlayerSegmentId, DefaultPlayerSegmentInfo>.CreateEmpty());

            Assert.That(OutdatedArchiveMessage(config), Does.Contain("PlayerSegments"));
        }

        #endregion

        #region Offers

        static OfferInfo WalletOffer(
            string       id       = "offer",
            CurrencyType currency = CurrencyType.Coins,
            int          amount   = 100) =>
            OfferInfo.Wallet(
                MetaOfferId.FromString(id), "An Offer", "A tagline.", "chest",
                currency, amount,
                new RewardBundle(CurrencyAmount.Gems(10)));

        static OfferInfo DemoOffer(string id = "offer", int? maxPurchasesPerPlayer = 1) =>
            OfferInfo.Demo(
                MetaOfferId.FromString(id), "An Offer", "A tagline.", "chest",
                inAppProduct: null,
                new RewardBundle(CurrencyAmount.Gems(10)),
                maxPurchasesPerPlayer: maxPurchasesPerPlayer);

        static MetaActivableParams SegmentGate(PlayerSegmentId segment) =>
            new MetaActivableParams(
                isEnabled: true,
                segments: segment == null ? null : new List<MetaRef<PlayerSegmentInfoBase>> { MetaRef<PlayerSegmentInfoBase>.FromKey(segment) },
                additionalConditions: null,
                lifetime: MetaActivableLifetimeSpec.Forever.Instance,
                isTransient: true,
                schedule: null,
                maxActivations: null,
                maxTotalConsumes: null,
                maxConsumesPerActivation: null,
                cooldown: MetaActivableCooldownSpec.Fixed.Zero,
                allowActivationAdjustment: true,
                developerOnly: false);

        static OfferGroupInfo OfferGroup(
            string          groupId     = "group",
            string          placement   = "placement",
            int             priority    = 10,
            string          offerId     = "offer",
            PlayerSegmentId segment     = null) =>
            new OfferGroupInfo(
                MetaOfferGroupId.FromString(groupId), "A Group", "A description.",
                OfferPlacementId.FromString(placement), priority, MetaOfferId.FromString(offerId),
                SegmentGate(segment));

        static void SetOffers(SharedGameConfig config, params OfferInfo[] offers) =>
            SetEntry(config, "Offers", GameConfigLibrary<MetaOfferId, OfferInfo>.CreateSolo(offers));

        static void SetOfferGroups(SharedGameConfig config, params OfferGroupInfo[] groups) =>
            SetEntry(config, "OfferGroups", GameConfigLibrary<MetaOfferGroupId, OfferGroupInfo>.CreateSolo(groups));

        /// <summary>Checks that a valid wallet offer and offer group pass validation.</summary>
        [Test]
        public void AWellFormedWalletOfferIsAccepted()
        {
            SharedGameConfig config = ValidConfig();
            SetOffers(config, WalletOffer());
            SetOfferGroups(config, OfferGroup(segment: PlayerSegmentId.FromString("cohort")));

            AssertPasses(config);
        }

        /// <summary>Checks that an offer group cannot target a segment id that is not in the segment library.</summary>
        [Test]
        public void AnOfferGroupTargetingAnUnknownSegmentFailsTheBuild()
        {
            SharedGameConfig config = ValidConfig();
            SetOffers(config, WalletOffer());
            SetOfferGroups(config, OfferGroup(segment: PlayerSegmentId.FromString("no-such-segment")));

            AssertMentions(ExpectValidationErrors(config), "not in the library");
        }

        /// <summary>
        /// Checks that no two offer groups share a placement and a priority. The SDK orders the groups on a
        /// placement by priority, so two groups with equal priority have no defined order.
        /// </summary>
        [Test]
        public void TwoOfferGroupsSharingAPlacementAndPriorityFailTheBuild()
        {
            SharedGameConfig config = ValidConfig();
            SetOffers(config, WalletOffer(id: "a"), WalletOffer(id: "b"));
            SetOfferGroups(config,
                OfferGroup(groupId: "ga", placement: "shared", priority: 10, offerId: "a"),
                OfferGroup(groupId: "gb", placement: "shared", priority: 10, offerId: "b"));

            AssertMentions(ExpectValidationErrors(config), "shares priority 10 on placement 'shared'");
        }

        /// <summary>Checks that a wallet offer's price is not above the currency's cap, which no player could afford.</summary>
        [Test]
        public void AWalletOfferPricedAboveTheCapFailsTheBuild()
        {
            SharedGameConfig config = ValidConfig();
            SetOffers(config, WalletOffer(currency: CurrencyType.Coins, amount: 10_000_000));
            SetOfferGroups(config, OfferGroup());

            AssertMentions(ExpectValidationErrors(config), "PriceAmount");
        }

        /// <summary>
        /// Checks the offers design rule that every demo in-app purchase offer sells once per player.
        /// </summary>
        [Test]
        public void ADemoOfferWithAPurchaseLimitOtherThanOneFailsTheBuild()
        {
            SharedGameConfig config = ValidConfig();
            SetOffers(config, DemoOffer(maxPurchasesPerPlayer: 2));
            SetOfferGroups(config, OfferGroup());

            AssertMentions(ExpectValidationErrors(config), "MaxPurchasesPerPlayer");
        }

        /// <summary>Checks that a demo offer with a limit of one purchase per player passes.</summary>
        [Test]
        public void AWellFormedDemoOfferIsAccepted()
        {
            SharedGameConfig config = ValidConfig();
            SetOffers(config, DemoOffer(maxPurchasesPerPlayer: 1));
            SetOfferGroups(config, OfferGroup());

            AssertPasses(config);
        }

        #endregion

        #region The weekly themed event's templates

        /// <summary>
        /// Returns the valid config with one weekly-event template holding <paramref name="content"/>. Global has
        /// no field naming the active template, because a LiveOps event can be created from any template, so
        /// validation checks every template in the library.
        /// </summary>
        static SharedGameConfig WithWeeklyTemplate(WeeklyEventContent content)
        {
            SharedGameConfig config = ValidConfig();

            SetEntry(config, "WeeklyEventTemplates", GameConfigLibrary<LiveOpsEventTemplateId, WeeklyEventTemplateInfo>.CreateSolo(
                new List<WeeklyEventTemplateInfo>
                {
                    new WeeklyEventTemplateInfo(LiveOpsEventTemplateId.FromString("weekly.test"), content),
                }));

            return config;
        }

        static WeeklyEventContent WeeklyEventContentWith(
            string       theme    = "Trickster's Week",
            string       tagline  = "Every trick counts.",
            int          target   = 100,
            int          winBonus = 5,
            RewardBundle reward   = null) =>
            new WeeklyEventContent(theme, tagline, target, winBonus, reward ?? new RewardBundle(CurrencyAmount.Coins(2000), CurrencyAmount.Gems(100)));

        /// <summary>
        /// Checks that every currency used in a price is paid by something always available on the Events hub.
        /// A player short of a currency is sent to the Events hub to earn it (<c>docs/economy.md</c>).
        /// <para>
        /// In this config the first-week event, the weekly event and the tournament still pay gems, and the build
        /// still fails, because the check asks whether a player can earn the currency at any time. The wheel's
        /// expected-gems band also rejects this config, so the test asserts the message of this rule.
        /// </para>
        /// </summary>
        [Test]
        public void AGemPriceWithNoPermanentGemFaucetFailsTheBuild()
        {
            SharedGameConfig config = ValidConfig();

            // A cosmetic priced in gems.
            SetCosmetics(config, CosmeticsWithFrame(
                new CosmeticInfo(CheapFrame, CosmeticKind.Frame, "Cheap Frame", CurrencyAmount.Gems(200), isPurchasable: true, style: CosmeticStyle.FrameSilver)));

            // The wheel no longer pays gems. The daily reward and the missions, the other always-available
            // sources, never pay gems.
            List<WheelSectorInfo> sectors = TenSectors();
            sectors[3] = Sector(4, "s4", CurrencyAmount.Coins(300), WheelPrizeTier.Rare);
            SetEntry(config, "WheelTables", GameConfigLibrary<WheelTableId, WheelTableInfo>.CreateSolo(
                new List<WheelTableInfo> { new WheelTableInfo(Wheel, sectors) }));

            IReadOnlyList<string> errors = ExpectValidationErrors(config);

            AssertMentions(errors, "nothing permanently on the Events hub grants");

            // The error names the row with the price, because that is the row a fix would edit.
            AssertMentions(errors, "Cosmetics[frame.cheap]");
        }

        /// <summary>
        /// Checks that the weekly-event template library is not empty. The seeder creates every week from a
        /// template, so with none every player would always see the empty state. Nothing at run time would
        /// report it, because the empty state is a valid screen.
        /// </summary>
        [Test]
        public void AWeeklyEventLibraryWithNoTemplatesFailsTheBuild()
        {
            SharedGameConfig config = ValidConfig();
            SetEntry(config, "WeeklyEventTemplates", GameConfigLibrary<LiveOpsEventTemplateId, WeeklyEventTemplateInfo>.CreateEmpty());

            AssertMentions(ExpectValidationErrors(config), "holds no templates");
        }

        [Test]
        public void AWellFormedWeeklyEventTemplateIsAccepted()
        {
            AssertPasses(WithWeeklyTemplate(WeeklyEventContentWith()));
        }

        [Test]
        public void AWeeklyEventWithNoThemeFailsTheBuild()
        {
            AssertMentions(ExpectValidationErrors(WithWeeklyTemplate(WeeklyEventContentWith(theme: " "))), "theme");
        }

        [Test]
        public void AWeeklyEventWithNoTaglineFailsTheBuild()
        {
            AssertMentions(ExpectValidationErrors(WithWeeklyTemplate(WeeklyEventContentWith(tagline: ""))), "tagline");
        }

        [Test]
        public void AWeeklyEventTargetOfZeroFailsTheBuild()
        {
            AssertMentions(ExpectValidationErrors(WithWeeklyTemplate(WeeklyEventContentWith(target: 0))), "not a target");
        }

        /// <summary>
        /// Checks that the target can be reached within <see cref="WeeklyEventScoring.MaxTargetInPerfectMatches"/>
        /// perfect games. That limit also bounds the set of scored match ids, which would otherwise grow with
        /// every game a player finishes.
        /// </summary>
        [Test]
        public void AWeeklyEventTargetNobodyCouldReachFailsTheBuild()
        {
            int unreachable = WeeklyEventScoring.MaxPointsPerMatch(5) * WeeklyEventScoring.MaxTargetInPerfectMatches + 1;
            AssertMentions(ExpectValidationErrors(WithWeeklyTemplate(WeeklyEventContentWith(target: unreachable))), "more than");
        }

        /// <summary>
        /// Checks the minimum target, a balance rule that only the config build enforces. A week that one session
        /// can finish would be over on its first day.
        /// </summary>
        [Test]
        public void AWeeklyEventTargetASessionCouldFinishFailsTheBuild()
        {
            AssertMentions(ExpectValidationErrors(WithWeeklyTemplate(WeeklyEventContentWith(target: 5))), "fewer than");
        }

        /// <summary>
        /// Checks that the minimum target is enforced only by the config build, not by the check that runs when
        /// an event is created. The test endpoint relies on this to create a week that one match can finish, so
        /// a live test can reach the target.
        /// </summary>
        [Test]
        public void TheCreationHookAllowsATargetTheConfigBuildRefuses()
        {
            WeeklyEventContent easy = WeeklyEventContentWith(target: 5);

            Assert.That(WeeklyEventScoring.ProblemsWith(easy), Is.Empty, "the creation hook refused an easy week");
            Assert.That(WeeklyEventScoring.BalanceProblemsWith(easy), Is.Not.Empty, "the config build allowed one");
        }

        [Test]
        public void AWeeklyEventWinBonusOutsideItsBandFailsTheBuild()
        {
            AssertMentions(ExpectValidationErrors(WithWeeklyTemplate(WeeklyEventContentWith(winBonus: WeeklyEventScoring.MaxWinBonusPoints + 1))), "points for a win");
        }

        [Test]
        public void AWeeklyEventPayingSpinTokensFailsTheBuild()
        {
            AssertMentions(
                ExpectValidationErrors(WithWeeklyTemplate(WeeklyEventContentWith(reward: new RewardBundle(CurrencyAmount.Coins(2000), CurrencyAmount.SpinTokens(1))))),
                "spin tokens");
        }

        [Test]
        public void AWeeklyEventGrantingNothingFailsTheBuild()
        {
            AssertMentions(ExpectValidationErrors(WithWeeklyTemplate(WeeklyEventContentWith(reward: new RewardBundle()))), "grants nothing");
        }

        /// <summary>Checks weekly-event rewards against the wallet caps in Global, which a template cannot check on its own.</summary>
        [Test]
        public void AWeeklyEventRewardBiggerThanTheWalletCapFailsTheBuild()
        {
            AssertMentions(
                ExpectValidationErrors(WithWeeklyTemplate(WeeklyEventContentWith(reward: new RewardBundle(CurrencyAmount.Gems(5_000_000))))),
                "against a cap of");
        }

        #endregion

        #region The computer players

        [Test]
        public void AnEmptyBotProfileLibraryFailsTheBuild()
        {
            // With no profiles, a table gives every bot the strongest profile, and nothing at run time would
            // report it.
            SharedGameConfig config = ValidConfig();
            SetBots(config, new List<BotProfileInfo>(), TestBotConfig.NameRows());

            AssertMentions(ExpectValidationErrors(config), "BotProfiles[(library)]");
        }

        [Test]
        public void ASingleBotStrengthFailsTheBuild()
        {
            // With one profile, every bot at every table plays at the same strength, and nothing on screen shows
            // it.
            SharedGameConfig config = ValidConfig();
            SetBots(config, new List<BotProfileInfo> { TestBotConfig.SharpRow }, TestBotConfig.NameRows());

            AssertMentions(ExpectValidationErrors(config), "BotProfiles[(library)]");
        }

        [Test]
        public void ABotProfileErringMoreOftenThanItPlaysWellFailsTheBuild()
        {
            SharedGameConfig config = ValidConfig();
            SetBots(
                config,
                new List<BotProfileInfo> { new BotProfileInfo(BotProfileId.FromString("hopeless"), "Hopeless", mistakeChancePercent: 90, longThinkChancePercent: 10) },
                TestBotConfig.NameRows());

            AssertMentions(ExpectValidationErrors(config), "BotProfiles[hopeless].MistakeChancePercent");
        }

        [Test]
        public void ARosterShorterThanTheTableFailsTheBuild()
        {
            // Bot names are drawn without replacement, so the roster needs at least as many names as a table has
            // seats.
            SharedGameConfig  config = ValidConfig();
            List<BotNameInfo> names  = TestBotConfig.NameRows().GetRange(0, MatchRules.NumSeats - 1);
            SetBots(config, TestBotConfig.ProfileRows(), names);

            AssertMentions(ExpectValidationErrors(config), "BotNames[(library)]");
        }

        /// <summary>
        /// A bot name row that the build must refuse:
        /// <list type="bullet">
        /// <item>Two rows with different ids but the same name after case and punctuation are ignored could put
        /// the same name on two seats.</item>
        /// <item>Bot names are checked with the same policy as a player rename. The policy's length limit keeps a
        /// name inside a seat plaque, and a bot name no player could choose would not reserve anything.</item>
        /// <item>The reserved names in code, such as Support, apply to bots as well as players, because a bot named
        /// Support would impersonate the game's support channel.</item>
        /// </list>
        /// </summary>
        [TestCase("cog.wheel", "Cog-Wheel",             "is the same name as")]
        [TestCase("verylong",  "Overclocked Unit Nine", "BotNames[verylong].Name")]
        [TestCase("support",   "Support",               "BotNames[support].Name")]
        public void ABotNameTheServerWouldRefuseFailsTheBuild(string id, string name, string expectedError)
        {
            AssertMentions(ExpectValidationErrors(WithExtraBotName(id, name)), expectedError);
        }

        /// <summary>
        /// Checks that the name generator cannot produce a bot name. Bot names are reserved, so a generated
        /// name that matched one would be refused by the server. Bot names and the name vocabulary are in
        /// different libraries, so only a cross-library check can compare them.
        /// </summary>
        [Test]
        public void ABotNameTheNameGeneratorCanProduceFailsTheBuild()
        {
            SharedGameConfig config = WithExtraBotName("collision", "AdjaaNouaa00");

            AssertMentions(ExpectValidationErrors(config, walkEveryGeneratedName: true), "names the server would refuse");
        }

        [Test]
        public void AnArchiveWithNoComputerPlayersIsRefusedAtLoad()
        {
            // A forming table reads both bot libraries. Without names the bot seats have no name, and without
            // profiles every bot plays at full strength.
            SharedGameConfig withoutNames = ValidConfig();
            SetBots(withoutNames, TestBotConfig.ProfileRows(), new List<BotNameInfo>());
            Assert.That(OutdatedArchiveMessage(withoutNames), Does.Contain(nameof(SharedGameConfig.BotNames)));

            // A roster shorter than the table makes table formation throw, because names are drawn without
            // replacement. An archive not produced by this build must be refused at load on the same rule.
            SharedGameConfig withTooFewNames = ValidConfig();
            SetBots(withTooFewNames, TestBotConfig.ProfileRows(), TestBotConfig.NameRows().GetRange(0, MatchRules.NumSeats - 1));
            Assert.That(OutdatedArchiveMessage(withTooFewNames), Does.Contain(nameof(SharedGameConfig.BotNames)));

            SharedGameConfig withoutProfiles = ValidConfig();
            SetBots(withoutProfiles, new List<BotProfileInfo>(), TestBotConfig.NameRows());
            Assert.That(OutdatedArchiveMessage(withoutProfiles), Does.Contain(nameof(SharedGameConfig.BotProfiles)));
        }

        #endregion
    }
}
