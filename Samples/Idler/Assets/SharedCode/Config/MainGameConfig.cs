// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using Metaplay.Core.Config;
using Metaplay.Core.InAppPurchase;
using Metaplay.Core.LiveOpsEvent;
using Metaplay.Core.Localization;
using Metaplay.Core.Model;
using Metaplay.Core.Offers;
using Metaplay.Core.Player;
using System.Collections.Generic;

namespace Game.Logic
{
    /// <summary>
    /// Global configuration variables. Fetched from Google Sheet named 'Global'.
    /// </summary>
    [MetaSerializable]
    [MetaBlockedMembers(10)]
    public class GlobalConfig : GameConfigKeyValue<GlobalConfig>
    {
        [MetaMember(2)] public MetaRef<ProducerInfo>    InitialProducer;
        [MetaMember(8)] public int                      InitialGold                 = 50;
        [MetaMember(9)] public int                      InitialGems                 = 10;

        // \todo: move to specific Guild config
        [MetaMember(3)]  public int                     GuildsNumGoldPerSoldPoke    = 50;
        [MetaMember(4)]  public int                     GuildsVanityCostNumGold     = 100;
        [MetaMember(5)]  public IReadOnlyList<int>      GuildsVanityRankThresholds  = new[] { 5, 10, 15 };
        [MetaMember(6)]  public IReadOnlyList<int>      GuildsVanityRankRewardGold  = new[] { 50, 50, 50 };
        [MetaMember(7)]  public IReadOnlyList<int>      GuildsVanityRankRewardGems  = new[] { 0, 100, 100 };
        [MetaMember(11)] public int                     GuildCreationGemCost        = 10;

        [MetaDeserializationConstructor]
        public GlobalConfig(MetaRef<ProducerInfo> initialProducer, int initialGold, int initialGems, int guildsNumGoldPerSoldPoke, int guildsVanityCostNumGold, IReadOnlyList<int> guildsVanityRankThresholds = null, IReadOnlyList<int> guildsVanityRankRewardGold = null, IReadOnlyList<int> guildsVanityRankRewardGems = null, int guildCreationGemCost = 10)
        {
            InitialProducer = initialProducer;
            InitialGold = initialGold;
            InitialGems = initialGems;
            GuildsNumGoldPerSoldPoke = guildsNumGoldPerSoldPoke;
            GuildsVanityCostNumGold = guildsVanityCostNumGold;
            GuildsVanityRankThresholds = guildsVanityRankThresholds ?? new[] { 5, 10, 15 };
            GuildsVanityRankRewardGold = guildsVanityRankRewardGold ?? new[] { 50, 50, 50 };
            GuildsVanityRankRewardGems = guildsVanityRankRewardGems ?? new[] { 0, 100, 100 };
            GuildCreationGemCost = guildCreationGemCost;
        }

        public GlobalConfig()
        {
        }
    }

    [MetaSerializable]
    public class OpaqueSourceTestId : StringId<OpaqueSourceTestId> { }

    [MetaSerializable]
    public class OpaqueSourceTestInfo : IGameConfigData<OpaqueSourceTestId>
    {
        [MetaMember(2)] public OpaqueSourceTestId Id;
        [MetaMember(3)] public string Name;
        [MetaMember(1)] public MetaRef<ProducerInfo> Producer;

        public OpaqueSourceTestId ConfigKey => Id;
    }

    /// <summary>
    /// Registry for all game configuration data. Should be used for the game's economy data, entity data (units,
    /// buildings, etc.), in-app definitions, localization metadata (which languages are supported), etc.
    /// </summary>
    /// <remarks>
    /// The typical workflow when using GameConfigs is:
    /// - Build the <see cref="SharedGameConfig"/> and <see cref="ServerGameConfig"/> from source data using a class derived from <see cref="GameConfigBuildTemplate{TSharedConfig, TServerConfig, TBuildParameters}"/>.
    /// - Export the generated configs as binary <see cref="ConfigArchive"/>s.
    /// - During runtime, <see cref="ConfigArchive"/> is used (binary allows for fast loading times), on both
    ///   the client and the server.
    /// - Optionally, new versions of config archives can be published to the server, and then
    ///   updated to all the clients during their login flow.
    ///
    /// The various instances of <see cref="GameConfigLibrary{KeyT, InfoT}"/> can also be implicitly
    /// accessed by using the ValueT as members in game model classes. The references are automatically resolved.
    /// </remarks>
    public class SharedGameConfig : SharedGameConfigBase
    {
        #region Metaplay SDK integrations

        [GameConfigEntry("Languages")]
        public GameConfigLibrary<LanguageId, LanguageInfo> Languages { get; private set; }

        [GameConfigEntry("InAppProducts")]
        public GameConfigLibrary<InAppProductId, InAppProductInfo> InAppProducts { get; private set; }

        [GameConfigEntry("PlayerSegments")]
        [GameConfigEntryTransform(typeof(PlayerSegmentInfoSourceItem))]
        public GameConfigLibrary<PlayerSegmentId, PlayerSegmentInfo> PlayerSegments { get; private set; }

        [GameConfigEntry("Offers", configBuildSource: nameof(IdlerGameConfigBuildParameters.LiveOpsSource))]
        [GameConfigEntryTransform(typeof(IdlerOfferSourceConfigItem))]
        public GameConfigLibrary<MetaOfferId, IdlerOfferInfo> Offers { get; private set; }

        [GameConfigEntry("OfferGroups", configBuildSource: nameof(IdlerGameConfigBuildParameters.LiveOpsSource))]
        [GameConfigEntryTransform(typeof(IdlerOfferGroupSourceConfigItem))]
        public GameConfigLibrary<MetaOfferGroupId, IdlerOfferGroupInfo> OfferGroups { get; private set; }

        #endregion

        [GameConfigEntry("ProducerKinds")]
        public GameConfigLibrary<ProducerKindId, ProducerKindInfo>                  ProducerKinds           { get; private set; }

        // Note: This is intentionally using different "entry name" (ProducerData) and C# member name (Producers),
        //       to help catch possible bugs where one is accidentally used in place of the other.
        //       The entry name is used when building/loading the game config archive,
        //       as well as the default name for the source sheet when building the config.
        //       The C# member name is meant for most everything else.
        [GameConfigEntry("ProducerData")]
        public GameConfigLibrary<ProducerTypeId, ProducerInfo>                      Producers               { get; private set; }

        [GameConfigEntry("OpaqueSourceTest")]
        public GameConfigLibrary<OpaqueSourceTestId, OpaqueSourceTestInfo>          OpaqueSourceTest        { get; set; } // public setter for config builder

        /// <summary>
        /// Note that the member name is different from the config entry name for testing reasons,
        /// same as with <see cref="Producers"/>.
        /// </summary>
        [GameConfigEntry("Global")]
        [GameConfigSyntaxAdapter(ensureHasKeyValueSheetHeader: true)]
        public GlobalConfig GlobalConfig { get; private set; }

        [GameConfigEntry("HappyHours", configBuildSource: nameof(IdlerGameConfigBuildParameters.LiveOpsSource))]
        [GameConfigEntryTransform(typeof(HappyHourSourceConfigItem))]
        public GameConfigLibrary<HappyHourId, HappyHourInfo>                        HappyHours              { get; private set; }

        [GameConfigEntry("SpecialProducerEvents", configBuildSource: nameof(IdlerGameConfigBuildParameters.LiveOpsSource))]
        [GameConfigEntryTransform(typeof(SpecialProducerEventSourceConfigItem))]
        public GameConfigLibrary<SpecialProducerEventId, SpecialProducerEventInfo>  SpecialProducerEvents   { get; private set; }

        public override void BuildTimeValidate(GameConfigValidationResult validationResult)
        {
            base.BuildTimeValidate(validationResult);

            foreach (ProducerInfo value in Producers.Values)
            {
                if (value.UnmodifiedUnlockCost > 500)
                    validationResult.Warning(nameof(Producers), value.ConfigKey.ToString(), "Example warning: Producers unlock cost is higher than 500", columnHint: "UnlockCost");
            }
        }

        /// <summary>
        /// Validate that all the data is valid, especially references between the various libraries.
        /// </summary>
        protected override void Validate()
        {
            base.Validate();
        }
    }

    public class ServerGameConfig : ServerGameConfigBase
    {
        [GameConfigEntry(PlayerExperimentsEntryName)]
        public GameConfigLibrary<PlayerExperimentId, PlayerExperimentInfo> PlayerExperiments { get; private set; }
        [GameConfigEntry("TestLiveOpsEventTemplates")]
        public GameConfigLibrary<LiveOpsEventTemplateId, LiveOpsEventTemplateConfigData<IdlerTestLiveOpsEvent>> TestLiveOpsEventTemplates { get; private set; }
    }
}
