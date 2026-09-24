using Metaplay.Core.Config;
using Metaplay.Core.InAppPurchase;
using Metaplay.Core.LiveOpsEvent;
using Metaplay.Core.Offers;
using Metaplay.Core.Player;

namespace Game.Logic
{
    /// <summary>
    /// All shared game config entries. Each is read from the CSV file of the same name in <c>GameConfigSource/</c>
    /// (<c>docs/game-config.md</c>). The card game reads only <see cref="BotProfiles"/> and <see cref="BotNames"/>,
    /// copied as a table forms (<c>docs/bots.md</c>). Every other entry belongs to the meta game.
    /// <para>
    /// Every entry uses <c>requireArchiveEntry: false</c>, so adding an entry does not make the SDK fail to load
    /// older archives. <see cref="Validate"/> then refuses an archive that lacks entries the game reads.
    /// </para>
    /// </summary>
    public class SharedGameConfig : SharedGameConfigBase
    {
        /// <summary>Game-wide settings, including the active-table pointers.</summary>
        [GameConfigEntry("Global", requireArchiveEntry: false)]
        public GlobalConfig Global { get; private set; } = new GlobalConfig();

        /// <summary>
        /// The vocabulary for generated player names (<c>docs/player.md</c>, "Generated names").
        /// </summary>
        [GameConfigEntry("PlayerIdentity", requireArchiveEntry: false)]
        public PlayerIdentityConfig PlayerIdentity { get; private set; } = new PlayerIdentityConfig();

        /// <summary>
        /// The bot strength profiles (<c>docs/bots.md</c>). There is no active pointer, because a table draws
        /// one profile per bot seat from all rows.
        /// </summary>
        [GameConfigEntry("BotProfiles", requireArchiveEntry: false)]
        public GameConfigLibrary<BotProfileId, BotProfileInfo> BotProfiles { get; private set; } = GameConfigLibrary<BotProfileId, BotProfileInfo>.CreateEmpty();

        /// <summary>
        /// The names reserved for bots. Tables draw bot names from this library, and a player's display name must
        /// not match any of them (<c>docs/player.md</c>, "Name rules").
        /// </summary>
        [GameConfigEntry("BotNames", requireArchiveEntry: false)]
        public GameConfigLibrary<BotNameId, BotNameInfo> BotNames { get; private set; } = GameConfigLibrary<BotNameId, BotNameInfo>.CreateEmpty();

        /// <summary>Published versions of the daily-reward cycle.</summary>
        [GameConfigEntry("DailyRewards", requireArchiveEntry: false)]
        public GameConfigLibrary<DailyRewardTableId, DailyRewardTableInfo> DailyRewards { get; private set; } = GameConfigLibrary<DailyRewardTableId, DailyRewardTableInfo>.CreateEmpty();

        /// <summary>Published versions of the first-week schedule.</summary>
        [GameConfigEntry("FirstWeekSchedules", requireArchiveEntry: false)]
        public GameConfigLibrary<FirstWeekScheduleId, FirstWeekScheduleInfo> FirstWeekSchedules { get; private set; } = GameConfigLibrary<FirstWeekScheduleId, FirstWeekScheduleInfo>.CreateEmpty();

        /// <summary>Every mission the game can run, daily and weekly.</summary>
        [GameConfigEntry("Missions", requireArchiveEntry: false)]
        public GameConfigLibrary<MissionId, MissionInfo> Missions { get; private set; } = GameConfigLibrary<MissionId, MissionInfo>.CreateEmpty();

        /// <summary>Published versions of the daily and weekly mission sets.</summary>
        [GameConfigEntry("MissionSets", requireArchiveEntry: false)]
        public GameConfigLibrary<MissionSetId, MissionSetInfo> MissionSets { get; private set; } = GameConfigLibrary<MissionSetId, MissionSetInfo>.CreateEmpty();

        /// <summary>Published versions of the spin wheel's prize table.</summary>
        [GameConfigEntry("WheelTables", requireArchiveEntry: false)]
        public GameConfigLibrary<WheelTableId, WheelTableInfo> WheelTables { get; private set; } = GameConfigLibrary<WheelTableId, WheelTableInfo>.CreateEmpty();

        /// <summary>The cosmetic catalogue. Never remove a published item. Retire it by making it not purchasable.</summary>
        [GameConfigEntry("Cosmetics", requireArchiveEntry: false)]
        public GameConfigLibrary<CosmeticId, CosmeticInfo> Cosmetics { get; private set; } = GameConfigLibrary<CosmeticId, CosmeticInfo>.CreateEmpty();

        /// <summary>
        /// The demo in-app purchase catalogue. The SDK reads this library by the name <c>InAppProducts</c> to
        /// resolve a purchase. This entry sets the item type (<c>docs/offers.md</c>).
        /// </summary>
        [GameConfigEntry("InAppProducts", requireArchiveEntry: false)]
        public GameConfigLibrary<InAppProductId, DemoInAppProductInfo> InAppProducts { get; private set; } = GameConfigLibrary<InAppProductId, DemoInAppProductInfo>.CreateEmpty();

        /// <summary>
        /// The player segments that offer groups and LiveOps events target (<c>docs/offers.md</c>, "Player
        /// segments").
        /// <para>
        /// The SDK reads this library by the name <c>PlayerSegments</c> to resolve targeting, estimate segment
        /// sizes and show which segments a player is in. This entry sets the item type. The sample uses the SDK's
        /// <see cref="DefaultPlayerSegmentInfo"/> without extra columns.
        /// </para>
        /// </summary>
        [GameConfigEntry("PlayerSegments", requireArchiveEntry: false)]
        [GameConfigEntryTransform(typeof(DefaultPlayerSegmentBasicInfoSourceItem))]
        public GameConfigLibrary<PlayerSegmentId, DefaultPlayerSegmentInfo> PlayerSegments { get; private set; } = GameConfigLibrary<PlayerSegmentId, DefaultPlayerSegmentInfo>.CreateEmpty();

        /// <summary>
        /// The weekly themed event templates that a weekly event is created from (<c>docs/weekly-event.md</c>).
        /// <para>
        /// There is no active pointer, because weekly events are created as LiveOps events and any template can
        /// be picked for the next week. The config build therefore checks every template against the wallet caps.
        /// </para>
        /// </summary>
        [GameConfigEntry("WeeklyEventTemplates", requireArchiveEntry: false)]
        public GameConfigLibrary<LiveOpsEventTemplateId, WeeklyEventTemplateInfo> WeeklyEventTemplates { get; private set; } = GameConfigLibrary<LiveOpsEventTemplateId, WeeklyEventTemplateInfo>.CreateEmpty();

        /// <summary>Published versions of the seasonal tournament's reward table.</summary>
        [GameConfigEntry("TournamentRewards", requireArchiveEntry: false)]
        public GameConfigLibrary<TournamentRewardTableId, TournamentRewardTableInfo> TournamentRewards { get; private set; } = GameConfigLibrary<TournamentRewardTableId, TournamentRewardTableInfo>.CreateEmpty();

        /// <summary>
        /// The offer catalogue (<c>docs/offers.md</c>). The SDK reads this library by the name <c>Offers</c>
        /// through <c>SharedGameConfigBase.RegisterSDKIntegrations</c>.
        /// <para>
        /// <see cref="OfferInfo"/> is one concrete type for both wallet-priced and demo-IAP offers, because the
        /// SDK's config-repository generator refuses an abstract library item type such as
        /// <see cref="MetaOfferInfoBase"/>.
        /// </para>
        /// </summary>
        [GameConfigEntry("Offers", requireArchiveEntry: false)]
        public GameConfigLibrary<MetaOfferId, OfferInfo> Offers { get; private set; } = GameConfigLibrary<MetaOfferId, OfferInfo>.CreateEmpty();

        /// <summary>The offer groups: placement, priority, targeting and schedule for the offers in <see cref="Offers"/>.</summary>
        [GameConfigEntry("OfferGroups", requireArchiveEntry: false)]
        [GameConfigEntryTransform(typeof(OfferGroupSourceItem))]
        public GameConfigLibrary<MetaOfferGroupId, OfferGroupInfo> OfferGroups { get; private set; } = GameConfigLibrary<MetaOfferGroupId, OfferGroupInfo>.CreateEmpty();

        /// <summary>
        /// Runs the SDK's and the game's config checks during the config build. Any error fails the build.
        /// </summary>
        public override void BuildTimeValidate(GameConfigValidationResult validationResult)
        {
            base.BuildTimeValidate(validationResult);
            GameConfigValidation.Validate(this, validationResult);
        }

        /// <summary>
        /// Refuses to load a config that lacks entries or values the game reads. See
        /// <see cref="GameConfigValidation.ThrowIfArchiveIsOutdated"/>.
        /// </summary>
        protected override void Validate()
        {
            base.Validate();
            GameConfigValidation.ThrowIfArchiveIsOutdated(this);
        }
    }
}
