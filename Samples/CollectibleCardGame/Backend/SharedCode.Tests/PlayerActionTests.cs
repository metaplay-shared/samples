using Metaplay.Core;
using Metaplay.Core.Model;
using Metaplay.Core.Player;
using Metaplay.Core.Serialization;
using NUnit.Framework;
using System;

namespace Game.Logic.Tests
{
    /// <summary>
    /// Shows how to test execution of a <see cref="PlayerAction"/> against the game-specific
    /// <see cref="PlayerModel"/> without running a server, against the built game config.
    /// This is the fixture the rules-engine suites grow out of.
    /// </summary>
    [TestFixture]
    public class PlayerActionTests
    {
        /// <summary> The built config archive, shared by every suite that needs content. </summary>
        static SharedGameConfig _gameConfig => TestGameConfig.Shared;

        #region Helpers

        /// <summary>
        /// Create a <see cref="PlayerModel"/> in its initial state for the tests to act on.
        /// </summary>
        static PlayerModel CreatePlayerModel()
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

            return playerModel;
        }

        /// <summary>
        /// Helper to execute a <see cref="PlayerAction"/> against the specified <see cref="PlayerModel"/>.
        /// </summary>
        static MetaActionResult ExecuteAction(PlayerModel playerModel, PlayerAction action)
        {
            return action.InvokeExecute(playerModel, commit: true);
        }

        #endregion // Helpers

        #region Test cases

        [Test]
        public void NewPlayer_TakesItsNameFromTheAssignedName()
        {
            PlayerModel playerModel = CreatePlayerModel();

            Assert.That(playerModel.DisplayName, Is.EqualTo("Example name"));
            Assert.That(playerModel.PlayerName, Is.EqualTo("Example name"));
        }

        [Test]
        public void PlayerSetDisplayName_WritesTheOneName()
        {
            PlayerModel playerModel = CreatePlayerModel();

            MetaActionResult result = ExecuteAction(playerModel, new PlayerSetDisplayName("Whiskers"));

            Assert.That(result, Is.EqualTo(MetaActionResult.Success));

            // DisplayName is an alias of PlayerName, so the name the menu reads and the name the dashboard,
            // the name search index and the operator's change-name control read are the same string. A second
            // member here is the drift a playtest found: the menu renamed and the dashboard did not.
            Assert.That(playerModel.DisplayName, Is.EqualTo("Whiskers"));
            Assert.That(playerModel.PlayerName, Is.EqualTo("Whiskers"));
        }

        [Test]
        public void PlayerSetDisplayName_SurvivesASerializationRoundTrip()
        {
            PlayerModel playerModel = CreatePlayerModel();

            Assert.That(ExecuteAction(playerModel, new PlayerSetDisplayName("Whiskers")), Is.EqualTo(MetaActionResult.Success));

            // The alias itself is not serialized; the name reaches the database through PlayerName's tag. A
            // clone is the cheapest proof that the write landed on a persisted member rather than on a
            // property the serializer skips.
            PlayerModel restored = MetaSerialization.CloneTagged(playerModel, MetaSerializationFlags.IncludeAll, logicVersion: null, resolver: _gameConfig);

            Assert.That(restored.DisplayName, Is.EqualTo("Whiskers"));
            Assert.That(restored.PlayerName, Is.EqualTo("Whiskers"));
        }

        [Test]
        public void PlayerSetDisplayName_RefusesABlankName()
        {
            PlayerModel playerModel = CreatePlayerModel();

            MetaActionResult result = ExecuteAction(playerModel, new PlayerSetDisplayName("   "));

            Assert.That(result, Is.EqualTo(ActionResults.InvalidDisplayName));
            Assert.That(playerModel.DisplayName, Is.EqualTo("Example name"));
            Assert.That(playerModel.PlayerName, Is.EqualTo("Example name"));
        }

        [Test]
        public void PlayerSetDisplayName_RefusesANameShorterThanTheSdkMinimum()
        {
            PlayerModel playerModel = CreatePlayerModel();

            // Three letters is under the SDK validator's floor of five — the rule the dashboard's rename enforces,
            // and since 2026-09-10 the rule this action asks too.
            MetaActionResult result = ExecuteAction(playerModel, new PlayerSetDisplayName("Ann"));

            Assert.That(result, Is.EqualTo(ActionResults.InvalidDisplayName));
            Assert.That(playerModel.PlayerName, Is.EqualTo("Example name"));
        }

        [Test]
        public void PlayerSetDisplayName_RefusesAnOverlongName()
        {
            PlayerModel playerModel = CreatePlayerModel();
            string tooLong = new string('x', PlayerModel.MaxDisplayNameLength + 1);

            MetaActionResult result = ExecuteAction(playerModel, new PlayerSetDisplayName(tooLong));

            Assert.That(result, Is.EqualTo(ActionResults.InvalidDisplayName));
            Assert.That(playerModel.DisplayName, Is.EqualTo("Example name"));
            Assert.That(playerModel.PlayerName, Is.EqualTo("Example name"));
        }

        [Test]
        public void SharedGameConfig_LoadsFromTheBuiltArchive()
        {
            // The archive on disk is what the server boots from and what the offline client serves, so a test
            // that imports it catches a config archive that has gone stale against the config classes.
            Assert.That(_gameConfig, Is.Not.Null);
            Assert.That(_gameConfig.Global, Is.Not.Null);
        }

        #endregion // Test cases
    }
}
