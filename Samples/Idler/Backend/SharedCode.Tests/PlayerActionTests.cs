// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using Metaplay.Core.Config;
using Metaplay.Core.Model;
using Metaplay.Core.Player;
using NUnit.Framework;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Game.Logic.Tests
{
    /// <summary>
    /// Example test fixture that shows how to test execution of <see cref="PlayerAction"/> against
    /// the game-specific <see cref="PlayerModel"/> without needing to run the server. Also, includes
    /// example for loading the built game config from disk.
    /// </summary>
    [TestFixture]
    public class PlayerActionTests
    {
        // \note Relative to SharedCode.Tests/ directory
        public const string StaticGameConfigPath = "../Server/GameConfig/StaticGameConfig.mpa";

        /// <summary> Game config loaded as fixture setup. </summary>
        static SharedGameConfig _gameConfig;

        /// <summary>
        /// Fixture setup to load the game config from disk. This could be re-used by multiple
        /// fixtures.
        /// </summary>
        [OneTimeSetUp]
        public async Task SetUp()
        {
            _gameConfig = await LoadSharedGameConfigAsync();
        }

        #region Helpers

        /// <summary>
        /// Load the game config archive file from disk. The <see cref="SharedGameConfig"/> part of
        /// the full archive is returned.
        /// </summary>
        /// <returns></returns>
        async Task<SharedGameConfig> LoadSharedGameConfigAsync()
        {
            // Read StaticGameConfig.mpa from disk
            ConfigArchive staticGameConfigArchive = await ConfigArchive.FromFileAsync(StaticGameConfigPath);

            // Extract SharedGameConfig.mpa from parent archive
            ReadOnlyMemory<byte> sharedGameConfigBytes = staticGameConfigArchive.GetEntryByName("Shared.mpa").Bytes;
            ConfigArchive sharedGameConfigArchive = ConfigArchive.FromBytes(sharedGameConfigBytes);

            // Create the SharedGameConfig from the bytes
            ISharedGameConfig sharedGameConfig = GameConfigUtil.ImportSharedConfig(sharedGameConfigArchive);
            return (SharedGameConfig)sharedGameConfig;
        }

        /// <summary>
        /// Create a <see cref="PlayerModel"/> to be used in the tests. This method initializes
        /// the model to its initial state. In more complex tests, you probably want to support
        /// initializing the model to various states.
        /// </summary>
        /// <returns>Newly created PlayerModel</returns>
        PlayerModel CreatePlayerModel()
        {
            PlayerModel playerModel = PlayerModelUtil.CreateNewPlayerModel<PlayerModel>(
                MetaTime.FromDateTime(new DateTime(2021, 5, 30, 19, 57, 17, DateTimeKind.Utc)),
                _gameConfig,
                playerId: EntityId.CreateRandom(EntityKindCore.Player),
                name: "Example name");

            // Initialize logic version to latest supported
            playerModel.LogicVersion = IntegrationRegistry.Get<IMetaplayCoreOptionsProvider>().Options.SupportedLogicVersions.MaxVersion;

            // Simulate initial login to initialize resources
            playerModel.OnInitialLogin();

            // Return the created model
            return playerModel;
        }

        /// <summary>
        /// Helper to execute a <see cref="PlayerAction"/> against the specified <see cref="PlayerModel"/>.
        /// </summary>
        /// <param name="playerModel">Model to execute the action against</param>
        /// <param name="action">Player action to execute</param>
        /// <returns>Result of the execution</returns>
        MetaActionResult ExecuteAction(PlayerModel playerModel, PlayerAction action)
        {
            MetaActionResult result = action.InvokeExecute(playerModel, commit: true);
            return result;
        }

        /// <summary>
        /// Progress the given <paramref name="playerModel"/> by <paramref name="numTicks"/> ticks.
        /// </summary>
        /// <param name="playerModel">Model to execute ticks for</param>
        /// <param name="numTicks">Number of ticks to execute</param>
        void ExecuteTicks(PlayerModel playerModel, int numTicks)
        {
            for (int ndx = 0; ndx < numTicks; ndx++)
                playerModel.Tick(NullChecksumEvaluator.Context);
        }

        #endregion // Helpers

        #region Test cases

        /// <summary>
        /// Example test case that exemplifies the operations required to test execution
        /// of <see cref="PlayerAction"/>s against a <see cref="PlayerModel"/>. You can
        /// use this as a basis for writing your own game logic test cases.
        /// </summary>
        [Test]
        public void TestPlayerActions()
        {
            // Create initial player model
            PlayerModel playerModel = CreatePlayerModel();

            ProducerTypeId wordPressId = ProducerTypeId.FromString("WordPress");
            ProducerTypeId photoShopId = ProducerTypeId.FromString("PhotoShop");

            // Check initial resources & producers
            Assert.That(_gameConfig.GlobalConfig.InitialGold, Is.EqualTo(playerModel.Wallet.NumGold));
            Assert.That(playerModel.Producers.Keys.ToArray(), Is.EqualTo(new ProducerTypeId[] { wordPressId }));

            // Try unlock WordPress (already unlocked)
            Assert.That(playerModel.Producers[wordPressId].Level, Is.EqualTo(1));
            Assert.That(ExecuteAction(playerModel, new PlayerUnlockProducer(wordPressId)), Is.EqualTo(ActionResult.AlreadyUnlocked));
            Assert.That(playerModel.Producers[wordPressId].Level, Is.EqualTo(1));

            // Upgrade WordPress to level 2
            Assert.That(playerModel.Producers[wordPressId].Level, Is.EqualTo(1));
            Assert.That(ExecuteAction(playerModel, new PlayerUpgradeProducer(wordPressId)), Is.EqualTo(ActionResult.Success));
            Assert.That(playerModel.Producers[wordPressId].Level, Is.EqualTo(2));
            Assert.That(playerModel.Wallet.NumGold, Is.LessThan(_gameConfig.GlobalConfig.InitialGold)); // some gold was spent

            // Unlock PhotoShop (should be level 1 afterwards)
            Assert.That(playerModel.Producers.ContainsKey(photoShopId), Is.EqualTo(false));
            Assert.That(ExecuteAction(playerModel, new PlayerUnlockProducer(photoShopId)), Is.EqualTo(ActionResult.Success));
            Assert.That(playerModel.Producers[photoShopId].Level, Is.EqualTo(1));

            // Accumulate gold by progressing time
            int goldBefore = playerModel.Wallet.NumGold;
            ExecuteTicks(playerModel, 100);
            Assert.That(playerModel.Wallet.NumGold, Is.GreaterThan(goldBefore));
        }

        #endregion // Test cases
    }
}
