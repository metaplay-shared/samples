using Metaplay.Core;
using Metaplay.Core.Config;
using Metaplay.Core.Model;
using Metaplay.Core.Player;
using Metaplay.Core.Serialization;
using NUnit.Framework;
using System;
using System.Collections.Generic;

namespace Game.Logic.Tests
{
    /// <summary>
    /// The one value an entry names a deck with, and the one resolver that turns it into cards. The key round
    /// trip is here rather than in the client suite because both halves of it live in shared code precisely so
    /// a pure test can pin them together — a picker whose value does not parse back renders blank.
    /// </summary>
    [TestFixture]
    public class DeckChoiceTests
    {
        static SharedGameConfig Config => TestGameConfig.Shared;

        static PlayerModel CreatePlayerModel()
        {
            PlayerModel playerModel = PlayerModelUtil.CreateNewPlayerModel<PlayerModel>(
                MetaTime.FromDateTime(new DateTime(2021, 5, 30, 19, 57, 17, DateTimeKind.Utc)),
                Config,
                playerId: EntityId.CreateRandom(EntityKindCore.Player),
                name: "Example name");

            playerModel.LogicVersion = IntegrationRegistry.Get<IMetaplayCoreOptionsProvider>().Options.SupportedLogicVersions.MaxVersion;
            playerModel.OnInitialLogin();

            return playerModel;
        }

        static StarterDeckId FirstStarterDeck()
        {
            foreach (StarterDeckInfo deck in Config.StarterDecks.Values)
                return deck.StarterDeckId;
            return null;
        }

        #region The value

        [Test]
        public void ASavedChoiceAndAStarterChoiceAreEachWellFormed()
        {
            Assert.That(DeckChoice.Saved(3).IsSaved, Is.True);
            Assert.That(DeckChoice.Saved(3).IsStarter, Is.False);
            Assert.That(DeckChoice.Saved(3).IsWellFormed, Is.True);

            DeckChoice starter = DeckChoice.Starter(StarterDeckId.FromString("FireAndFoam"));
            Assert.That(starter.IsStarter, Is.True);
            Assert.That(starter.IsSaved, Is.False);
            Assert.That(starter.IsWellFormed, Is.True);
        }

        [Test]
        public void NeitherHalfSetIsMalformed()
        {
            // A default-constructed choice, which is what a deserialized payload from an old client gives.
            Assert.That(new DeckChoice().IsWellFormed, Is.False);
            Assert.That(DeckChoice.Saved(0).IsWellFormed, Is.False);
        }

        [Test]
        public void BothHalvesSetIsMalformed()
        {
            // The factories cannot build this shape, which is the point of having them — so the control has
            // to write the members directly, standing in for the deserialized payload of a client that had
            // no such invariant.
            DeckChoice both = BothHalvesSet(2, "FireAndFoam");

            Assert.That(both.IsWellFormed, Is.False);
            Assert.That(DeckChoiceResolver.Resolve(both, CreatePlayerModel()).Error, Is.EqualTo(DeckChoiceError.Malformed));
        }

        /// <summary> A choice with both halves set, which only a hostile or stale payload can be. </summary>
        static DeckChoice BothHalvesSet(int savedDeckId, string starterDeckId)
        {
            DeckChoice choice = new DeckChoice();
            typeof(DeckChoice).GetProperty(nameof(DeckChoice.SavedDeckId)).SetValue(choice, savedDeckId);
            typeof(DeckChoice).GetProperty(nameof(DeckChoice.StarterDeckId)).SetValue(choice, StarterDeckId.FromString(starterDeckId));
            return choice;
        }

        #endregion

        #region The picker's option value

        [Test]
        public void TheKeyRoundTripsForBothKinds()
        {
            DeckChoice saved = DeckChoice.Saved(7);
            Assert.That(saved.ToKey(), Is.EqualTo("saved:7"));
            Assert.That(DeckChoice.TryParseKey(saved.ToKey(), out DeckChoice savedBack), Is.True);
            Assert.That(savedBack.SavedDeckId, Is.EqualTo(7));
            Assert.That(savedBack.IsStarter, Is.False);

            DeckChoice starter = DeckChoice.Starter(StarterDeckId.FromString("EmberAndOak"));
            Assert.That(starter.ToKey(), Is.EqualTo("starter:EmberAndOak"));
            Assert.That(DeckChoice.TryParseKey(starter.ToKey(), out DeckChoice starterBack), Is.True);
            Assert.That(starterBack.StarterDeckId, Is.EqualTo(StarterDeckId.FromString("EmberAndOak")));
            Assert.That(starterBack.SavedDeckId, Is.Zero);
        }

        [TestCase("")]
        [TestCase(null)]
        [TestCase("nonsense")]
        [TestCase("saved:")]
        [TestCase("starter:")]
        [TestCase("saved:0")]
        [TestCase("saved:x")]
        [TestCase("saved:-1")]
        [TestCase("SAVED:1")]
        public void AKeyNoPickerProducesDoesNotParse(string key)
        {
            Assert.That(DeckChoice.TryParseKey(key, out DeckChoice choice), Is.False, key);
            Assert.That(choice, Is.Null);
        }

        #endregion

        #region The resolver

        [Test]
        public void AStarterChoiceResolvesToTheConfiguredCardList()
        {
            StarterDeckId    id       = FirstStarterDeck();
            DeckChoiceResult resolved = DeckChoiceResolver.Resolve(DeckChoice.Starter(id), CreatePlayerModel());

            Assert.That(resolved.IsValid, Is.True);
            Assert.That(resolved.Cards, Is.EqualTo(Config.StarterDecks[id].ToCardIds()));
        }

        [Test]
        public void ASavedChoiceResolvesToThatDecksCardList()
        {
            PlayerModel  player = CreatePlayerModel();
            List<CardId> cards  = Config.StarterDecks[FirstStarterDeck()].ToCardIds();

            Assert.That(new PlayerSaveDeck(deckId: null, "Mine", cards).InvokeExecute(player, commit: true).IsSuccess, Is.True);

            DeckChoiceResult resolved = DeckChoiceResolver.Resolve(DeckChoice.Saved(1), player);

            Assert.That(resolved.IsValid, Is.True);
            Assert.That(resolved.Cards, Is.EqualTo(cards));
        }

        [Test]
        public void AMalformedChoiceIsRefusedRatherThanGuessedAt()
        {
            PlayerModel player = CreatePlayerModel();

            Assert.That(DeckChoiceResolver.Resolve(null, player).Error, Is.EqualTo(DeckChoiceError.Malformed));
            Assert.That(DeckChoiceResolver.Resolve(new DeckChoice(), player).Error, Is.EqualTo(DeckChoiceError.Malformed));
        }

        [Test]
        public void AnUnknownSavedDeckAndAnUnknownStarterDeckAreDifferentRefusals()
        {
            PlayerModel player = CreatePlayerModel();

            Assert.That(DeckChoiceResolver.Resolve(DeckChoice.Saved(99), player).Error,
                Is.EqualTo(DeckChoiceError.UnknownSavedDeck));
            Assert.That(DeckChoiceResolver.Resolve(DeckChoice.Starter(StarterDeckId.FromString("NoSuchDeck")), player).Error,
                Is.EqualTo(DeckChoiceError.UnknownStarterDeck));
        }

        [Test]
        public void EveryStarterDeckResolvesAndIsPlayableByAFreshAccount()
        {
            // The whole ownership argument in one assertion: the starter grant hands out every card a starter
            // deck can name, so a brand-new account passes the same ValidateForPlayer a saved deck runs.
            PlayerModel player = CreatePlayerModel();

            foreach (StarterDeckInfo deck in Config.StarterDecks.Values)
            {
                DeckChoiceResult resolved = DeckChoiceResolver.Resolve(DeckChoice.Starter(deck.StarterDeckId), player);
                Assert.That(resolved.IsValid, Is.True, deck.StarterDeckId.Value);
                Assert.That(DeckValidator.ValidateForPlayer(resolved.Cards, Config, player.Collection).IsValid, Is.True,
                    deck.StarterDeckId.Value);
            }
        }

        [Test]
        public void TheRefusalNamesTheChoiceThatFailed()
        {
            Assert.That(ActionResults.ForChoiceError(DeckChoiceError.Malformed), Is.EqualTo(ActionResults.MalformedDeckChoice));
            Assert.That(ActionResults.ForChoiceError(DeckChoiceError.UnknownStarterDeck), Is.EqualTo(ActionResults.UnknownStarterDeck));
            Assert.That(ActionResults.ForChoiceError(DeckChoiceError.UnknownSavedDeck), Is.EqualTo(ActionResults.UnknownDeck));
        }

        #endregion

        #region Persistence

        [Test]
        public void AChoiceRoundTripsTheSerializer()
        {
            // The value is persisted on the player model, so both halves have to survive a tagged round trip.
            DeckChoice starter = MetaSerialization.CloneTagged(
                DeckChoice.Starter(StarterDeckId.FromString("SunlitThicket")),
                MetaSerializationFlags.IncludeAll, logicVersion: null, resolver: Config);
            Assert.That(starter.StarterDeckId, Is.EqualTo(StarterDeckId.FromString("SunlitThicket")));
            Assert.That(starter.SavedDeckId, Is.Zero);
            Assert.That(starter.IsWellFormed, Is.True);

            DeckChoice saved = MetaSerialization.CloneTagged(
                DeckChoice.Saved(4), MetaSerializationFlags.IncludeAll, logicVersion: null, resolver: Config);
            Assert.That(saved.SavedDeckId, Is.EqualTo(4));
            Assert.That(saved.StarterDeckId, Is.Null);
            Assert.That(saved.IsWellFormed, Is.True);
        }

        #endregion
    }
}
