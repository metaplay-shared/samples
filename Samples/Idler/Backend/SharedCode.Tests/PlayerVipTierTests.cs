// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using Metaplay.Core.Config;
using Metaplay.Core.Model;
using Metaplay.Core.Player;
using NUnit.Framework;
using System;
using System.Threading.Tasks;

namespace Game.Logic.Tests
{
    /// <summary>
    /// Demonstrates the end-to-end usage of a game-defined <see cref="ICustomComparable"/>
    /// (<see cref="PlayerVipTier"/>) as a <see cref="PlayerPropertyRequirement"/>. The flow mirrors
    /// what happens when a segment row in the LiveOps spreadsheet references the
    /// <see cref="PlayerPropertyIdVipTier"/> property:
    /// <list type="number">
    /// <item>The <c>PropMin</c>/<c>PropMax</c> strings are parsed through the
    /// <see cref="ConfigParser"/> registered in <see cref="ConfigParsers"/>.</item>
    /// <item>The resulting <see cref="PlayerPropertyRequirement"/> is checked against a player.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    public class PlayerVipTierTests
    {
        // \note Relative to SharedCode.Tests/ directory
        const string StaticGameConfigPath = "../Server/GameConfig/StaticGameConfig.mpa";

        static SharedGameConfig _gameConfig;

        [OneTimeSetUp]
        public async Task SetUp()
        {
            ConfigArchive staticGameConfigArchive = await ConfigArchive.FromFileAsync(StaticGameConfigPath);
            ConfigArchive sharedGameConfigArchive = ConfigArchive.FromBytes(staticGameConfigArchive.GetEntryByName("Shared.mpa").Bytes);
            _gameConfig = (SharedGameConfig)GameConfigUtil.ImportSharedConfig(sharedGameConfigArchive);
        }

        static PlayerModel CreatePlayerWithLevel(int playerLevel)
        {
            PlayerModel player = PlayerModelUtil.CreateNewPlayerModel<PlayerModel>(
                MetaTime.FromDateTime(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
                _gameConfig,
                playerId: EntityId.CreateRandom(EntityKindCore.Player),
                name:     "vip-test");
            player.LogicVersion = IntegrationRegistry.Get<IMetaplayCoreOptionsProvider>().Options.SupportedLogicVersions.MaxVersion;
            player.OnInitialLogin();
            player.PlayerLevel = playerLevel;
            return player;
        }

        [TestCase(2,  false)] // Bronze   – below Silver
        [TestCase(5,  true)]  // Silver   – at min boundary
        [TestCase(20, true)]  // Gold     – inside range
        [TestCase(40, true)]  // Platinum – at max boundary
        [TestCase(80, false)] // Diamond  – above max
        public void SilverToPlatinumSegmentMatchesPlayerLevels(int playerLevel, bool expectedMatch)
        {
            // A segment author writes PropMin="Silver", PropMax="Platinum" against the VipTier
            // PlayerPropertyId; ParseFromStrings invokes the ConfigParser registered in
            // ConfigParsers to produce PlayerVipTier constants and build the requirement.
            PlayerPropertyRequirement req = PlayerPropertyRequirement.ParseFromStrings(
                id:     new PlayerPropertyIdVipTier(),
                minStr: nameof(PlayerVipTier.Tier.Silver),
                maxStr: nameof(PlayerVipTier.Tier.Platinum));

            PlayerModel player = CreatePlayerWithLevel(playerLevel);
            Assert.That(req.MatchesPlayer(player), Is.EqualTo(expectedMatch));
        }

        [Test]
        public void TierComparisonIsByOrdinal()
        {
            // Sanity check: ICustomComparable.CompareTo gives the natural ordering of the enum.
            PlayerVipTier silver = new PlayerVipTier(PlayerVipTier.Tier.Silver);
            PlayerVipTier gold   = new PlayerVipTier(PlayerVipTier.Tier.Gold);

            Assert.That(silver.CompareTo(gold), Is.LessThan(0));
            Assert.That(gold.CompareTo(silver), Is.GreaterThan(0));
            Assert.That(gold.CompareTo(new PlayerVipTier(PlayerVipTier.Tier.Gold)), Is.EqualTo(0));
        }
    }
}
