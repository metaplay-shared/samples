using Metaplay.Core;
using Metaplay.Core.Activables;
using Metaplay.Core.Config;
using Metaplay.Core.InAppPurchase;
using Metaplay.Core.Offers;
using Metaplay.Core.Player;
using Metaplay.Core.Schedule;
using System.Collections.Generic;

namespace Game.Logic.Tests
{
    /// <summary>
    /// A game config built in code, for tests that need a config without an archive.
    /// <para>
    /// The values copy the shipped config in <c>GameConfigSource</c>, because this test assembly cannot reference
    /// the server assembly that loads it. <c>GameConfigBuildTests</c> in the server tests checks the shipped
    /// archive against the source content.
    /// </para>
    /// </summary>
    public static class TestGameConfig
    {
        public const int MaxCoins      = 9_999_999;
        public const int MaxGems       = 999_999;
        public const int MaxSpinTokens = 999;

        /// <summary>The shipped starting wallet.</summary>
        public static RewardBundle StartingWallet() =>
            new RewardBundle(CurrencyAmount.Coins(3_000), CurrencyAmount.Gems(100), CurrencyAmount.SpinTokens(1));

        public static GlobalConfig Global() => GlobalWith(StartingWallet());

        /// <summary>The id of this config's daily-reward table.</summary>
        public static readonly DailyRewardTableId DailyTable = DailyRewardTableId.FromString("daily");

        /// <summary>The id of this config's first-week schedule.</summary>
        public static readonly FirstWeekScheduleId FirstWeekSchedule = FirstWeekScheduleId.FromString("week");

        /// <summary>The id of one day in the first-week schedule, by its one-based display position.</summary>
        public static FirstWeekDayId FirstWeekDay(int day) => FirstWeekDayId.FromString($"week.day{day}");

        /// <summary>The id of this config's wheel table.</summary>
        public static readonly WheelTableId Wheel = WheelTableId.FromString("wheel");

        public static readonly MissionSetId DailySet  = MissionSetId.FromString("missions.daily.v1");
        public static readonly MissionSetId WeeklySet = MissionSetId.FromString("missions.weekly.v1");

        public static readonly MissionId DailyPlay1   = MissionId.FromString("daily.play1");
        public static readonly MissionId DailyPlay3   = MissionId.FromString("daily.play3");
        public static readonly MissionId DailyWin1    = MissionId.FromString("daily.win1");
        public static readonly MissionId WeeklyPlay10 = MissionId.FromString("weekly.play10");
        public static readonly MissionId WeeklyWin3   = MissionId.FromString("weekly.win3");

        /// <summary>
        /// The daily reset schedule: every day at the player's local midnight, with no end. It has the same shape
        /// as the shipped schedule because <c>DailyResetScheduleRules</c> requires that shape.
        /// </summary>
        public static MetaRecurringCalendarSchedule DailyReset() =>
            new MetaRecurringCalendarSchedule(
                timeMode:   MetaScheduleTimeMode.Local,
                start:      new MetaCalendarDateTime(2020, 1, 1, 0, 0, 0),
                duration:   DailyResetScheduleRules.OneDay,
                endingSoon: new MetaCalendarPeriod(),
                preview:    new MetaCalendarPeriod(),
                review:     new MetaCalendarPeriod(),
                recurrence: DailyResetScheduleRules.OneDay,
                numRepeats: null);

        /// <summary>
        /// The shipped daily-reward cycle. The last step also grants a spin token.
        /// </summary>
        public static DailyRewardTableInfo DailyRewardTable()
        {
            int[] coins = { 150, 170, 190, 210, 240, 270, 330 };

            List<DailyRewardStepInfo> steps = new List<DailyRewardStepInfo>();
            for (int step = 1; step <= coins.Length; step++)
            {
                RewardBundle reward = step == DailyRewardTableInfo.NumSteps
                    ? new RewardBundle(CurrencyAmount.Coins(coins[step - 1]), CurrencyAmount.SpinTokens(1))
                    : new RewardBundle(CurrencyAmount.Coins(coins[step - 1]));

                steps.Add(new DailyRewardStepInfo(DailyRewardStepId.FromString($"daily.step{step}"), step, reward));
            }

            return new DailyRewardTableInfo(DailyTable, steps);
        }

        /// <summary>The shipped player segment thresholds.</summary>
        public const int FirstWeekSegmentMaxAccountAgeDays = 6;
        public const int GemFundedMinGems                  = 500;
        public const int EngagedMinMatchesPlayed           = 5;

        /// <summary>
        /// The shipped player segments.
        /// </summary>
        public static List<DefaultPlayerSegmentInfo> PlayerSegments() =>
            new List<DefaultPlayerSegmentInfo>
            {
                new DefaultPlayerSegmentInfo(
                    PlayerSegmentIds.FirstWeekNonPurchaser,
                    PlayerSegmentConditions.AllOf(
                        PlayerSegmentConditions.Between(new PlayerPropertyAccountAgeDays(), 0, FirstWeekSegmentMaxAccountAgeDays),
                        PlayerSegmentConditions.Exactly(new PlayerPropertyValidatedPurchases(), 0),
                        PlayerSegmentConditions.IsTrue(new PlayerPropertyPersonalizedOffersEnabled())),
                    "First week, no purchase",
                    $"Created less than {FirstWeekSegmentMaxAccountAgeDays + 1} days ago and has never had a purchase granted."),

                new DefaultPlayerSegmentInfo(
                    PlayerSegmentIds.GemFunded,
                    PlayerSegmentConditions.AllOf(
                        PlayerSegmentConditions.AtLeast(new PlayerPropertyGems(), GemFundedMinGems),
                        PlayerSegmentConditions.IsTrue(new PlayerPropertyPersonalizedOffersEnabled())),
                    "Gem funded",
                    $"Holds at least {GemFundedMinGems} gems, so a premium bundle is already affordable."),

                new DefaultPlayerSegmentInfo(
                    PlayerSegmentIds.EngagedNonPurchaser,
                    PlayerSegmentConditions.AllOf(
                        PlayerSegmentConditions.AtLeast(new PlayerPropertyGamesPlayed(), EngagedMinMatchesPlayed),
                        PlayerSegmentConditions.Exactly(new PlayerPropertyValidatedPurchases(), 0),
                        PlayerSegmentConditions.IsTrue(new PlayerPropertyPersonalizedOffersEnabled())),
                    "Engaged, no purchase",
                    $"Has finished at least {EngagedMinMatchesPlayed} games and has never had a purchase granted."),
            };

        /// <summary>The shipped offer prices and contents.</summary>
        public const int LuckySpinBundlePriceGems  = 500;
        public const int LuckySpinBundleCoins      = 2000;
        public const int LuckySpinBundleSpinTokens = 5;

        public const int DailySpinDealPriceCoins = 750;
        public const int DailySpinDealSpinTokens = 1;

        public const double StarterPack1PriceUsd   = 0.99;
        public const int    StarterPack1Coins      = 2000;
        public const int    StarterPack1Gems       = 300;
        public const int    StarterPack1SpinTokens = 1;

        public const double StarterPack2PriceUsd   = 2.99;
        public const int    StarterPack2Coins      = 5000;
        public const int    StarterPack2Gems       = 700;
        public const int    StarterPack2SpinTokens = 2;

        public const double GemBoosterPackPriceUsd   = 4.99;
        public const int    GemBoosterPackCoins      = 10000;
        public const int    GemBoosterPackGems       = 1250;
        public const int    GemBoosterPackSpinTokens = 3;

        public const double CoinVaultPriceUsd = 2.99;
        public const int    CoinVaultCoins    = 25000;

        public static readonly OfferPlacementId ShopFeaturedPlacement  = OfferPlacementIds.ShopFeatured;
        public static readonly OfferPlacementId ShopCataloguePlacement = OfferPlacementIds.ShopCatalogue;

        public static readonly MetaOfferId StarterPack1Offer    = MetaOfferId.FromString("starter-pack-1");
        public static readonly MetaOfferId StarterPack2Offer    = MetaOfferId.FromString("starter-pack-2");
        public static readonly MetaOfferId GemBoosterPackOffer  = MetaOfferId.FromString("gem-booster-pack");
        public static readonly MetaOfferId CoinVaultOffer       = MetaOfferId.FromString("coin-vault");
        public static readonly MetaOfferId LuckySpinBundleOffer = MetaOfferId.FromString("lucky-spin-bundle");
        public static readonly MetaOfferId DailySpinDealOffer   = MetaOfferId.FromString("daily-spin-deal");

        public static readonly MetaOfferGroupId FeaturedFirstWeekGroup = MetaOfferGroupId.FromString("shop_featured.first_week");
        public static readonly MetaOfferGroupId FeaturedGemFundedGroup = MetaOfferGroupId.FromString("shop_featured.gem_funded");
        public static readonly MetaOfferGroupId FeaturedEngagedGroup   = MetaOfferGroupId.FromString("shop_featured.engaged");
        public static readonly MetaOfferGroupId FeaturedFallbackGroup  = MetaOfferGroupId.FromString("shop_featured.fallback");
        public static readonly MetaOfferGroupId CatalogueDailyGroup    = MetaOfferGroupId.FromString("shop_catalogue.daily");

        public static readonly InAppProductId DemoStarterPack1   = InAppProductId.FromString("starter_pack_1");
        public static readonly InAppProductId DemoStarterPack2   = InAppProductId.FromString("starter_pack_2");
        public static readonly InAppProductId DemoGemBoosterPack = InAppProductId.FromString("gem_booster_pack");
        public static readonly InAppProductId DemoCoinVault      = InAppProductId.FromString("coin_vault");

        /// <summary>The shipped demo in-app products behind the demo-priced offers in <see cref="Offers"/>.</summary>
        public static List<DemoInAppProductInfo> InAppProducts() =>
            new List<DemoInAppProductInfo>
            {
                DemoInAppProductInfo.ForOffer(DemoStarterPack1, "Starter Pack", "Available during your first week.", StarterPack1PriceUsd),
                DemoInAppProductInfo.ForOffer(DemoStarterPack2, "Starter Pack II", "The chain's second step.", StarterPack2PriceUsd),
                DemoInAppProductInfo.ForOffer(DemoGemBoosterPack, "Gem Booster Pack", "Power up your game.", GemBoosterPackPriceUsd),
                DemoInAppProductInfo.ForOffer(DemoCoinVault, "Coin Vault", "A coin-only vault.", CoinVaultPriceUsd),
            };

        /// <summary>The shipped offers.</summary>
        public static List<OfferInfo> Offers() =>
            new List<OfferInfo>
            {
                OfferInfo.Demo(
                    StarterPack1Offer, "Starter Pack", "Available during your first week.", "chest-plum",
                    MetaRef<InAppProductInfoBase>.FromKey(DemoStarterPack1),
                    new RewardBundle(CurrencyAmount.Coins(StarterPack1Coins), CurrencyAmount.Gems(StarterPack1Gems), CurrencyAmount.SpinTokens(StarterPack1SpinTokens)),
                    segments: new List<MetaRef<PlayerSegmentInfoBase>> { MetaRef<PlayerSegmentInfoBase>.FromKey(PlayerSegmentIds.FirstWeekNonPurchaser) }),

                OfferInfo.Demo(
                    StarterPack2Offer, "Starter Pack II", "Unlocked once your Starter Pack has run its course.", "chest-gold",
                    MetaRef<InAppProductInfoBase>.FromKey(DemoStarterPack2),
                    new RewardBundle(CurrencyAmount.Coins(StarterPack2Coins), CurrencyAmount.Gems(StarterPack2Gems), CurrencyAmount.SpinTokens(StarterPack2SpinTokens)),
                    additionalConditions: new List<PlayerCondition> { new MetaOfferPrecursorCondition(StarterPack1Offer, purchased: true, delay: MetaDuration.Zero) }),

                OfferInfo.Demo(
                    GemBoosterPackOffer, "Gem Booster Pack", "For players who keep coming back to the table.", "card-pack",
                    MetaRef<InAppProductInfoBase>.FromKey(DemoGemBoosterPack),
                    new RewardBundle(CurrencyAmount.Coins(GemBoosterPackCoins), CurrencyAmount.Gems(GemBoosterPackGems), CurrencyAmount.SpinTokens(GemBoosterPackSpinTokens)),
                    segments: new List<MetaRef<PlayerSegmentInfoBase>> { MetaRef<PlayerSegmentInfoBase>.FromKey(PlayerSegmentIds.EngagedNonPurchaser) }),

                OfferInfo.Demo(
                    CoinVaultOffer, "Coin Vault", "A coin-only vault for anyone building toward a big purchase.", "vault",
                    MetaRef<InAppProductInfoBase>.FromKey(DemoCoinVault),
                    new RewardBundle(CurrencyAmount.Coins(CoinVaultCoins))),

                OfferInfo.Wallet(
                    LuckySpinBundleOffer, "Lucky Spin Bundle", "Five spins and a pile of coins, paid for in gems.", "wheel",
                    CurrencyType.Gems, LuckySpinBundlePriceGems,
                    new RewardBundle(CurrencyAmount.Coins(LuckySpinBundleCoins), CurrencyAmount.SpinTokens(LuckySpinBundleSpinTokens))),

                OfferInfo.Wallet(
                    DailySpinDealOffer, "Daily Spin Deal", "A coin deal for everyone.", "spin",
                    CurrencyType.Coins, DailySpinDealPriceCoins,
                    new RewardBundle(CurrencyAmount.SpinTokens(DailySpinDealSpinTokens)),
                    maxPurchasesPerPlayer: null, maxPurchasesPerActivation: 1),
            };

        static MetaActivableParams SegmentGatedParams(PlayerSegmentId segment, MetaDuration lifetime, MetaDuration cooldown) =>
            new MetaActivableParams(
                isEnabled: true,
                segments: new List<MetaRef<PlayerSegmentInfoBase>> { MetaRef<PlayerSegmentInfoBase>.FromKey(segment) },
                additionalConditions: null,
                lifetime: new MetaActivableLifetimeSpec.Fixed(lifetime),
                isTransient: true,
                schedule: null,
                maxActivations: null,
                maxTotalConsumes: null,
                maxConsumesPerActivation: null,
                cooldown: cooldown == MetaDuration.Zero ? MetaActivableCooldownSpec.Fixed.Zero : new MetaActivableCooldownSpec.Fixed(cooldown),
                allowActivationAdjustment: true,
                developerOnly: false);

        static MetaActivableParams ScheduleGatedParams() =>
            new MetaActivableParams(
                isEnabled: true, segments: null, additionalConditions: null,
                lifetime: MetaActivableLifetimeSpec.ScheduleBased.Instance, isTransient: false,
                schedule: DailyReset(), maxActivations: null, maxTotalConsumes: null,
                maxConsumesPerActivation: null, cooldown: MetaActivableCooldownSpec.Fixed.Zero,
                allowActivationAdjustment: true, developerOnly: false);

        /// <summary>The shipped offer groups.</summary>
        public static List<OfferGroupInfo> OfferGroups() =>
            new List<OfferGroupInfo>
            {
                new OfferGroupInfo(
                    FeaturedFirstWeekGroup, "First week", "The starter bundle, while it is still the obvious next step.",
                    ShopFeaturedPlacement, priority: 10, StarterPack1Offer,
                    SegmentGatedParams(PlayerSegmentIds.FirstWeekNonPurchaser, MetaDuration.FromHours(72), MetaDuration.Zero)),

                new OfferGroupInfo(
                    FeaturedGemFundedGroup, "Gem funded", "A premium bundle for a player who can already afford it.",
                    ShopFeaturedPlacement, priority: 20, LuckySpinBundleOffer,
                    SegmentGatedParams(PlayerSegmentIds.GemFunded, MetaDuration.FromHours(24), MetaDuration.FromDays(7))),

                new OfferGroupInfo(
                    FeaturedEngagedGroup, "Engaged", "A deal for a player who keeps coming back to the table.",
                    ShopFeaturedPlacement, priority: 30, GemBoosterPackOffer,
                    SegmentGatedParams(PlayerSegmentIds.EngagedNonPurchaser, MetaDuration.FromHours(72), MetaDuration.FromDays(7))),

                new OfferGroupInfo(
                    FeaturedFallbackGroup, "Daily fallback", "The offer everyone sees when no cohort's own offer applies.",
                    ShopFeaturedPlacement, priority: 100, DailySpinDealOffer,
                    ScheduleGatedParams()),

                new OfferGroupInfo(
                    CatalogueDailyGroup, "Shop catalogue", "The whole catalogue, sold-out entries included.",
                    ShopCataloguePlacement, priority: 10,
                    new List<MetaOfferId> { StarterPack1Offer, StarterPack2Offer, GemBoosterPackOffer, LuckySpinBundleOffer, CoinVaultOffer, DailySpinDealOffer },
                    ScheduleGatedParams()),
            };

        /// <summary>
        /// The shipped first-week schedule. Only the last day grants gems and spin tokens.
        /// </summary>
        public static FirstWeekScheduleInfo FirstWeekScheduleTable()
        {
            int[] matchGoals = { 1, 1, 2, 2, 2, 3, 1 };
            int[] coins      = { 250, 300, 350, 400, 450, 500, 1500 };

            List<FirstWeekDayInfo> days = new List<FirstWeekDayInfo>();
            for (int day = 1; day <= FirstWeekScheduleInfo.NumDays; day++)
            {
                RewardBundle reward = day == FirstWeekScheduleInfo.NumDays
                    ? new RewardBundle(CurrencyAmount.Coins(coins[day - 1]), CurrencyAmount.Gems(100), CurrencyAmount.SpinTokens(2))
                    : new RewardBundle(CurrencyAmount.Coins(coins[day - 1]));

                days.Add(new FirstWeekDayInfo(FirstWeekDay(day), day, matchGoals[day - 1], reward));
            }

            return new FirstWeekScheduleInfo(FirstWeekSchedule, days);
        }

        /// <summary>The cheapest coin-priced frame, priced at the bottom of the coin price band.</summary>
        public static readonly CosmeticId SilverFrame = CosmeticId.FromString("frame.silver");

        /// <summary>The most expensive coin-priced item, priced at the top of the coin price band.</summary>
        public static readonly CosmeticId SapphireFrame = CosmeticId.FromString("frame.sapphire");

        /// <summary>A gem-priced frame that costs more than the starting wallet's gems.</summary>
        public static readonly CosmeticId EmeraldFrame = CosmeticId.FromString("frame.emerald");

        /// <summary>A coin-priced name effect, so tests can buy an item for the name effect slot.</summary>
        public static readonly CosmeticId AzureName = CosmeticId.FromString("name.azure");

        /// <summary>
        /// A gem-priced name effect at the top of the gem price band. Derived bots draw their premium name
        /// effect from this tier.
        /// </summary>
        public static readonly CosmeticId ShimmerName = CosmeticId.FromString("name.shimmer");

        /// <summary>Earned by winning a season, never sold.</summary>
        public static readonly CosmeticId ChampionFrame = CosmeticId.FromString("frame.champion");

        /// <summary>A purchasable avatar.</summary>
        public static readonly CosmeticId ClubAvatar = CosmeticId.FromString("avatar.jack");

        public const int SilverFramePriceCoins   = 500;
        public const int SapphireFramePriceCoins = 2_500;
        public const int EmeraldFramePriceGems   = 150;
        public const int AzureNamePriceCoins     = 700;
        public const int ShimmerNamePriceGems    = 1_000;
        public const int ClubAvatarPriceCoins    = 500;

        /// <summary>
        /// A cosmetics catalogue with the same kinds of items as the shipped one.
        /// </summary>
        public static List<CosmeticInfo> Cosmetics() =>
            new List<CosmeticInfo>
            {
                new CosmeticInfo(SilverFrame, CosmeticKind.Frame, "Silver Frame", CurrencyAmount.Coins(SilverFramePriceCoins),
                    isPurchasable: true, style: CosmeticStyle.FrameSilver, flavour: "Where everybody starts."),
                new CosmeticInfo(SapphireFrame, CosmeticKind.Frame, "Sapphire Frame", CurrencyAmount.Coins(SapphireFramePriceCoins),
                    isPurchasable: true, style: CosmeticStyle.FrameSapphire, flavour: "Prestige, elegant, timeless."),
                new CosmeticInfo(EmeraldFrame, CosmeticKind.Frame, "Emerald Frame", CurrencyAmount.Gems(EmeraldFramePriceGems),
                    isPurchasable: true, style: CosmeticStyle.FrameEmerald, flavour: "Quiet confidence."),
                new CosmeticInfo(AzureName, CosmeticKind.NameEffect, "Azure", CurrencyAmount.Coins(AzureNamePriceCoins),
                    isPurchasable: true, style: CosmeticStyle.NameAzure, flavour: "Cool under pressure."),
                new CosmeticInfo(ShimmerName, CosmeticKind.NameEffect, "Shimmer", CurrencyAmount.Gems(ShimmerNamePriceGems),
                    isPurchasable: true, style: CosmeticStyle.NamePrism, flavour: "Every colour at once."),
                new CosmeticInfo(ChampionFrame, CosmeticKind.Frame, "Tournament Champion", price: null, isPurchasable: false,
                    style: CosmeticStyle.FrameGold, flavour: "Top of the table.", unlockRequirement: "Win a tournament season"),
                new CosmeticInfo(ClubAvatar, CosmeticKind.Avatar, "Jack of Clubs", CurrencyAmount.Coins(ClubAvatarPriceCoins),
                    isPurchasable: true, style: CosmeticStyle.AvatarClub, flavour: "One of the court."),

                // The defaults every player starts owning and wearing. A catalogue without them fails validation,
                // and a new player would have empty slots (docs/cosmetics.md, "The starting three").
                new CosmeticInfo(CosmeticDefaults.Avatar, CosmeticKind.Avatar, "Spade", price: null, isPurchasable: false,
                    style: CosmeticStyle.AvatarSpade, flavour: "Where every hand begins.", unlockRequirement: "Yours from the start"),
                new CosmeticInfo(CosmeticDefaults.Frame, CosmeticKind.Frame, "Plain Ring", price: null, isPurchasable: false,
                    style: CosmeticStyle.FramePlain, flavour: "A thin line of gold.", unlockRequirement: "Yours from the start"),
                new CosmeticInfo(CosmeticDefaults.NameEffect, CosmeticKind.NameEffect, "Plain", price: null, isPurchasable: false,
                    style: CosmeticStyle.NamePlain, flavour: "Your name as it is.", unlockRequirement: "Yours from the start"),
            };

        public static GlobalConfig GlobalWith(RewardBundle startingWallet) => GlobalWith(startingWallet, DailySet, WeeklySet);

        public static GlobalConfig GlobalWith(RewardBundle startingWallet, MissionSetId dailySet, MissionSetId weeklySet) =>
            new GlobalConfig(
                startingWallet:        startingWallet,
                maxCoins:              MaxCoins,
                maxGems:               MaxGems,
                maxSpinTokens:         MaxSpinTokens,
                dailyResetSchedule:    DailyReset(),
                dailyRewardTable:      DailyTable,
                firstWeekSchedule:     FirstWeekSchedule,
                wheelTable:            Wheel,
                dailyMissionSet:       dailySet,
                weeklyMissionSet:      weeklySet,
                tournamentRewardTable: TournamentRewardTableId.FromString("tournament"));

        /// <summary>
        /// The shipped tournament rewards, under the id that <see cref="GlobalWith(RewardBundle, MissionSetId,
        /// MissionSetId)"/> points to.
        /// </summary>
        public static TournamentRewardTableInfo TournamentRewards() =>
            new TournamentRewardTableInfo(
                TournamentRewardTableId.FromString("tournament"),
                new List<TournamentMilestoneInfo>
                {
                    new TournamentMilestoneInfo(1,  new RewardBundle(CurrencyAmount.Coins(250))),
                    new TournamentMilestoneInfo(3,  new RewardBundle(CurrencyAmount.Coins(250))),
                    new TournamentMilestoneInfo(7,  new RewardBundle(CurrencyAmount.Coins(500))),
                    new TournamentMilestoneInfo(10, new RewardBundle(CurrencyAmount.SpinTokens(1))),
                },
                new List<TournamentPlacementInfo>
                {
                    new TournamentPlacementInfo(1, new RewardBundle(CurrencyAmount.Coins(1500), CurrencyAmount.Gems(50))),
                    new TournamentPlacementInfo(3, new RewardBundle(CurrencyAmount.Coins(1000), CurrencyAmount.SpinTokens(1))),
                    new TournamentPlacementInfo(5, new RewardBundle(CurrencyAmount.Coins(500))),
                });

        /// <summary>
        /// A small name vocabulary, so tests use the <i>generated</i> naming path rather than the fallback. Derived
        /// tournament bots are named from this config, so leaving it null would skip the code a real client runs.
        /// </summary>
        public static PlayerIdentityConfig PlayerIdentity(int generatorVersion = 1) =>
            new PlayerIdentityConfig(
                new List<string> { "Calm", "Nimble", "Hushed", "Mighty" },
                new List<string> { "Harbor", "Quartz", "Pilot", "Spade" },
                suffixCount: 100,
                generatorVersion: generatorVersion);

        /// <summary>The shipped wheel table.</summary>
        public static WheelTableInfo WheelTable()
        {
            return new WheelTableInfo(Wheel, new List<WheelSectorInfo>
            {
                Sector( 1, CurrencyAmount.Coins(100),    WheelPrizeTier.Common),
                Sector( 2, CurrencyAmount.Coins(500),    WheelPrizeTier.Uncommon),
                Sector( 3, CurrencyAmount.Coins(250),    WheelPrizeTier.Common),
                Sector( 4, CurrencyAmount.Gems(30),      WheelPrizeTier.Premium),
                Sector( 5, CurrencyAmount.SpinTokens(1), WheelPrizeTier.SpinAgain),
                Sector( 6, CurrencyAmount.Coins(1000),   WheelPrizeTier.Rare),
                Sector( 7, CurrencyAmount.Coins(250),    WheelPrizeTier.Common),
                Sector( 8, CurrencyAmount.SpinTokens(1), WheelPrizeTier.SpinAgain),
                Sector( 9, new RewardBundle(),           WheelPrizeTier.Nothing),
                Sector(10, CurrencyAmount.Coins(500),    WheelPrizeTier.Uncommon),
            });
        }

        /// <summary>One sector, with an id that includes its position so a failure names the sector.</summary>
        public static WheelSectorInfo Sector(int position, CurrencyAmount reward, WheelPrizeTier tier) =>
            new WheelSectorInfo(WheelSectorId.FromString($"wheel.s{position}"), position, new RewardBundle(reward), tier);

        /// <summary>
        /// One sector with a whole reward bundle. The blank sector uses this with an empty bundle, because a null
        /// reward is rejected.
        /// </summary>
        public static WheelSectorInfo Sector(int position, RewardBundle reward, WheelPrizeTier tier) =>
            new WheelSectorInfo(WheelSectorId.FromString($"wheel.s{position}"), position, reward, tier);

        /// <summary>The shipped missions (<c>GameConfigSource/Missions.csv</c>).</summary>
        public static List<MissionInfo> Missions() =>
            new List<MissionInfo>
            {
                new MissionInfo(DailyPlay1,   MissionCadence.Daily,  MissionObjective.MatchesCompleted, 1,  new RewardBundle(CurrencyAmount.Coins(100))),
                new MissionInfo(DailyPlay3,   MissionCadence.Daily,  MissionObjective.MatchesCompleted, 3,  new RewardBundle(CurrencyAmount.Coins(150))),
                new MissionInfo(DailyWin1,    MissionCadence.Daily,  MissionObjective.MatchesWon,       1,  new RewardBundle(CurrencyAmount.Coins(150))),
                new MissionInfo(WeeklyPlay10, MissionCadence.Weekly, MissionObjective.MatchesCompleted, 10, new RewardBundle(CurrencyAmount.SpinTokens(1))),
                new MissionInfo(WeeklyWin3,   MissionCadence.Weekly, MissionObjective.MatchesWon,       3,  new RewardBundle(CurrencyAmount.SpinTokens(1))),
            };

        public static List<MissionSetInfo> MissionSets() =>
            new List<MissionSetInfo>
            {
                new MissionSetInfo(DailySet, MissionCadence.Daily, new List<MissionId> { DailyPlay1, DailyPlay3, DailyWin1 }),
                new MissionSetInfo(WeeklySet, MissionCadence.Weekly, new List<MissionId> { WeeklyPlay10, WeeklyWin3 }),
            };

        public static void SetMissions(SharedGameConfig config, List<MissionInfo> missions, List<MissionSetInfo> sets)
        {
            SetEntry(config, "Missions", GameConfigLibrary<MissionId, MissionInfo>.CreateSolo(missions));
            SetEntry(config, "MissionSets", GameConfigLibrary<MissionSetId, MissionSetInfo>.CreateSolo(sets));
        }

        /// <summary>
        /// A config with every entry the tests read, including the daily-reward table that a claim reads through
        /// <see cref="GlobalConfig.ActiveDailyRewardTable"/>. A null argument uses the shipped value.
        /// </summary>
        public static SharedGameConfig Build(
            RewardBundle          startingWallet = null,
            PlayerIdentityConfig  identity       = null,
            DailyRewardTableInfo  dailyRewards   = null,
            FirstWeekScheduleInfo firstWeek      = null,
            WheelTableInfo        wheel          = null)
        {
            SharedGameConfig config = BuildWithoutMissions(startingWallet, identity, dailyRewards, firstWeek, wheel);
            SetMissions(config, Missions(), MissionSets());
            return config;
        }

        /// <summary>
        /// The same config without the mission libraries, for tests that check a broken optional feature cannot
        /// break the game.
        /// </summary>
        public static SharedGameConfig BuildWithoutMissions(
            RewardBundle          startingWallet = null,
            PlayerIdentityConfig  identity       = null,
            DailyRewardTableInfo  dailyRewards   = null,
            FirstWeekScheduleInfo firstWeek      = null,
            WheelTableInfo        wheel          = null)
        {
            SharedGameConfig config = new SharedGameConfig();

            SetEntry(config, "PlayerIdentity", identity ?? PlayerIdentity());
            SetEntry(config, "BotProfiles", GameConfigLibrary<BotProfileId, BotProfileInfo>.CreateSolo(TestBotConfig.ProfileRows()));
            SetEntry(config, "BotNames", GameConfigLibrary<BotNameId, BotNameInfo>.CreateSolo(TestBotConfig.NameRows()));
            SetEntry(config, "Global", GlobalWith(startingWallet ?? StartingWallet()));
            SetEntry(config, "FirstWeekSchedules", GameConfigLibrary<FirstWeekScheduleId, FirstWeekScheduleInfo>.CreateSolo(
                new List<FirstWeekScheduleInfo> { firstWeek ?? FirstWeekScheduleTable() }));
            SetEntry(config, "TournamentRewards", GameConfigLibrary<TournamentRewardTableId, TournamentRewardTableInfo>.CreateSolo(
                new List<TournamentRewardTableInfo> { TournamentRewards() }));
            SetEntry(config, "DailyRewards", GameConfigLibrary<DailyRewardTableId, DailyRewardTableInfo>.CreateSolo(
                new List<DailyRewardTableInfo> { dailyRewards ?? DailyRewardTable() }));
            SetEntry(config, "PlayerSegments", GameConfigLibrary<PlayerSegmentId, DefaultPlayerSegmentInfo>.CreateSolo(PlayerSegments()));
            SetEntry(config, "InAppProducts", GameConfigLibrary<InAppProductId, DemoInAppProductInfo>.CreateSolo(InAppProducts()));
            SetEntry(config, "Offers", GameConfigLibrary<MetaOfferId, OfferInfo>.CreateSolo(Offers()));
            SetEntry(config, "OfferGroups", GameConfigLibrary<MetaOfferGroupId, OfferGroupInfo>.CreateSolo(OfferGroups()));
            SetEntry(config, "Cosmetics", GameConfigLibrary<CosmeticId, CosmeticInfo>.CreateSolo(Cosmetics()));
            SetEntry(config, "WheelTables", GameConfigLibrary<WheelTableId, WheelTableInfo>.CreateSolo(
                new List<WheelTableInfo> { wheel ?? WheelTable() }));

            // The active pointers stay unresolved because no importer runs on a config built in code.
            // DailyRewardPolicy.ActiveTable looks an unresolved pointer up in the library by key, so the same code
            // reads both a config built in code and an imported archive.
            return config;
        }

        /// <summary>
        /// Set one config entry the way the importer does. The entry properties have private setters so game code
        /// cannot replace a library on a config shared by every player, so tests use the SDK's entry setter.
        /// </summary>
        public static void SetEntry(SharedGameConfig config, string entryName, IGameConfigEntry entry)
        {
            GameConfigRepository.Instance.GetGameConfigTypeInfo(typeof(SharedGameConfig)).Entries[entryName].SetEntry(config, entry);
        }
    }
}
