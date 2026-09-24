using Metaplay.Core;
using Metaplay.Core.Config;
using Metaplay.Core.Model;
using Metaplay.Core.Player;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.Logic.Tests
{
    /// <summary>
    /// The account's own logic: the starter grant, the ownership half of deck validation, Power Score, and
    /// every player action that changes a collection, a deck or a lock. All pure shared code against the built
    /// config archive — the Playwright suites prove the screens use these correctly rather than re-deriving
    /// their edge cases through a browser.
    /// </summary>
    [TestFixture]
    public class PlayerCollectionTests
    {
        static SharedGameConfig Config => TestGameConfig.Shared;

        #region Helpers

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

        static MetaActionResult Execute(PlayerModel playerModel, PlayerAction action)
            => action.InvokeExecute(playerModel, commit: true);

        /// <summary>
        /// A server-issued action, which has a base class of its own because only the server may raise it. The
        /// account's own screens see it as an ordinary timeline action arriving.
        /// </summary>
        static MetaActionResult Execute(PlayerModel playerModel, PlayerSynchronizedServerAction action)
            => action.InvokeExecute(playerModel, commit: true);

        /// <summary>
        /// A legal deck built out of what the player owns: every clan-exempt card first, then whole clans until
        /// the deck is full, which keeps the clan count at or under the limit by construction.
        /// </summary>
        static List<CardId> BuildLegalDeck(PlayerModel player)
        {
            int deckSize = Config.Global.DeckSize;
            List<CardId> deck = new List<CardId>();

            List<CardId> owned = player.Collection.Keys
                .Where(cardId => Config.Cards[cardId].Collectible)
                .OrderBy(cardId => cardId.Value, StringComparer.Ordinal)
                .ToList();

            foreach (CardId cardId in owned)
            {
                if (deck.Count >= deckSize)
                    break;
                if (!Config.Cards[cardId].Clan.Ref.CountsTowardClanLimit)
                    deck.Add(cardId);
            }

            HashSet<ClanId> clans = new HashSet<ClanId>();
            foreach (CardId cardId in owned)
            {
                if (deck.Count >= deckSize)
                    break;

                ClanInfo clan = Config.Cards[cardId].Clan.Ref;
                if (!clan.CountsTowardClanLimit)
                    continue;

                if (!clans.Contains(clan.ClanId))
                {
                    if (clans.Count >= Config.Global.MaxClansPerDeck)
                        continue;
                    clans.Add(clan.ClanId);
                }

                deck.Add(cardId);
            }

            Assert.That(deck.Count, Is.EqualTo(deckSize), "the starter collection must be able to fill a deck");
            return deck;
        }

        /// <summary>
        /// A card the player owns that is not in <paramref name="deck"/> and can replace one of its cards
        /// without spending a clan slot the deck has not already spent.
        /// </summary>
        static CardId OwnedCardOutside(PlayerModel player, IReadOnlyList<CardId> deck)
        {
            HashSet<ClanId> deckClans = new HashSet<ClanId>(deck.Select(cardId => Config.Cards[cardId].Clan.Ref.ClanId));

            return player.Collection.Keys.First(cardId =>
            {
                CardInfo card = Config.Cards[cardId];
                return !deck.Contains(cardId)
                    && card.Collectible
                    && (!card.Clan.Ref.CountsTowardClanLimit || deckClans.Contains(card.Clan.Ref.ClanId));
            });
        }

        #endregion

        #region Starter grant

        [Test]
        public void NewPlayer_IsSeededAtTheConfiguredRating()
        {
            // The queue bands on this number, so an account that started at zero would sit a whole rating
            // band away from every other new account and pair with none of them.
            PlayerModel player = CreatePlayerModel();

            Assert.That(player.Record.Rating, Is.EqualTo(Config.Global.InitialRating));
            Assert.That(player.Record.Rating, Is.GreaterThan(0));
        }

        [Test]
        public void NewPlayer_ReceivesEveryStarterCard_AtTheRankFloor()
        {
            PlayerModel player = CreatePlayerModel();

            List<CardId> expected = Config.Cards
                .Where(entry => entry.Value.InStarterCollection)
                .Select(entry => entry.Key)
                .ToList();

            Assert.That(expected, Is.Not.Empty);
            Assert.That(player.Collection.Count, Is.EqualTo(expected.Count));
            foreach (CardId cardId in expected)
            {
                Assert.That(player.Collection.ContainsKey(cardId), Is.True, $"{cardId} was not granted");
                Assert.That(player.Collection[cardId], Is.EqualTo(Config.Global.RankMin));
            }
        }

        [Test]
        public void NewPlayer_IsNotGrantedNonStarterCards()
        {
            PlayerModel player = CreatePlayerModel();

            foreach ((CardId cardId, CardInfo card) in Config.Cards)
            {
                if (!card.InStarterCollection)
                    Assert.That(player.Collection.ContainsKey(cardId), Is.False, $"{cardId} should not be granted");
            }
        }

        [Test]
        public void NewPlayer_StartsWithTheConfiguredLockSlots_AndNothingLocked()
        {
            PlayerModel player = CreatePlayerModel();

            Assert.That(player.LockSlotCount, Is.EqualTo(Config.Global.InitialLockSlots));
            Assert.That(player.LockSlots, Is.Empty);
        }

        [Test]
        public void NewPlayer_StartsWithNoDecksAndAnEmptyRecord()
        {
            PlayerModel player = CreatePlayerModel();

            Assert.That(player.Decks, Is.Empty);
            Assert.That(player.NextDeckId, Is.EqualTo(1));
            Assert.That(player.Record, Is.Not.Null);
            Assert.That(player.Record.RankedDecided, Is.Zero);
            Assert.That(player.Record.RankedMatchesPlayed, Is.Zero);
        }

        [Test]
        public void StarterCollection_CanBuildALegalDeck()
        {
            PlayerModel player = CreatePlayerModel();
            List<CardId> deck = BuildLegalDeck(player);

            Assert.That(DeckValidator.ValidateForPlayer(deck, Config, player.Collection).IsValid, Is.True);
        }

        #endregion

        #region Ownership and Power Score

        [Test]
        public void ValidateForPlayer_RefusesACardTheCollectionDoesNotHold()
        {
            PlayerModel player = CreatePlayerModel();
            List<CardId> deck = BuildLegalDeck(player);

            CardId disowned = deck[0];
            player.Collection.Remove(disowned);

            DeckValidationResult result = DeckValidator.ValidateForPlayer(deck, Config, player.Collection);

            Assert.That(result.Error, Is.EqualTo(DeckValidationError.NotOwned));
            Assert.That(result.OffendingCard, Is.EqualTo(disowned));
        }

        [Test]
        public void ValidateForPlayer_ReportsAShapeFailureBeforeOwnership()
        {
            PlayerModel player = CreatePlayerModel();
            List<CardId> deck = BuildLegalDeck(player);
            deck.RemoveAt(0);

            // The shortened list is both the wrong size and (after the removal below) short of an owned card.
            // Shape is the answer, because it is the rule the player can act on.
            player.Collection.Remove(deck[0]);

            Assert.That(DeckValidator.ValidateForPlayer(deck, Config, player.Collection).Error,
                Is.EqualTo(DeckValidationError.WrongSize));
        }

        [Test]
        public void ComputePowerScore_SumsTheRanksOfTheDecksCards()
        {
            PlayerModel player = CreatePlayerModel();
            List<CardId> deck = BuildLegalDeck(player);

            // Every starter card is at the floor, so a fresh deck scores its own size.
            Assert.That(DeckValidator.ComputePowerScore(deck, player.Collection), Is.EqualTo(Config.Global.DeckSize));

            player.Collection[deck[0]] = Config.Global.RankMax;

            Assert.That(DeckValidator.ComputePowerScore(deck, player.Collection),
                Is.EqualTo(Config.Global.DeckSize - Config.Global.RankMin + Config.Global.RankMax));
        }

        [Test]
        public void ComputePowerScore_CountsAnUnownedCardAsNothing()
        {
            PlayerModel player = CreatePlayerModel();
            List<CardId> deck = BuildLegalDeck(player);
            player.Collection.Remove(deck[0]);

            Assert.That(DeckValidator.ComputePowerScore(deck, player.Collection),
                Is.EqualTo(Config.Global.DeckSize - Config.Global.RankMin));
        }

        [Test]
        public void ComputePowerScore_OfAnEmptyListIsZero()
        {
            PlayerModel player = CreatePlayerModel();

            Assert.That(DeckValidator.ComputePowerScore(new List<CardId>(), player.Collection), Is.Zero);
        }

        #endregion

        #region Saving decks

        [Test]
        public void PlayerSaveDeck_CreatesADeckAndAllocatesItsId()
        {
            PlayerModel player = CreatePlayerModel();
            List<CardId> deck = BuildLegalDeck(player);

            MetaActionResult result = Execute(player, new PlayerSaveDeck(null, "Zoomies", deck));

            Assert.That(result, Is.EqualTo(MetaActionResult.Success));
            Assert.That(player.Decks.Count, Is.EqualTo(1));
            Assert.That(player.Decks[1].Name, Is.EqualTo("Zoomies"));
            Assert.That(player.Decks[1].Cards, Is.EqualTo(deck));
            Assert.That(player.NextDeckId, Is.EqualTo(2));
        }

        [Test]
        public void PlayerSaveDeck_OverwritesAnExistingDeckWithoutAllocatingAnId()
        {
            PlayerModel player = CreatePlayerModel();
            List<CardId> deck = BuildLegalDeck(player);
            Execute(player, new PlayerSaveDeck(null, "First", deck));

            List<CardId> edited = new List<CardId>(deck);
            edited[0] = OwnedCardOutside(player, deck);

            MetaActionResult result = Execute(player, new PlayerSaveDeck(1, "Second", edited));

            Assert.That(result, Is.EqualTo(MetaActionResult.Success));
            Assert.That(player.Decks.Count, Is.EqualTo(1));
            Assert.That(player.Decks[1].Name, Is.EqualTo("Second"));
            Assert.That(player.Decks[1].Cards[0], Is.EqualTo(edited[0]));
            Assert.That(player.NextDeckId, Is.EqualTo(2));
        }

        [Test]
        public void PlayerSaveDeck_RefusesAnIdTheClientInvented()
        {
            PlayerModel player = CreatePlayerModel();
            List<CardId> deck = BuildLegalDeck(player);

            Assert.That(Execute(player, new PlayerSaveDeck(7, "Ghost", deck)), Is.EqualTo(ActionResults.UnknownDeck));
            Assert.That(player.Decks, Is.Empty);
        }

        [Test]
        public void PlayerSaveDeck_RefusesABlankName()
        {
            PlayerModel player = CreatePlayerModel();
            List<CardId> deck = BuildLegalDeck(player);

            Assert.That(Execute(player, new PlayerSaveDeck(null, "   ", deck)), Is.EqualTo(ActionResults.InvalidDeckName));
            Assert.That(player.Decks, Is.Empty);
        }

        [Test]
        public void PlayerSaveDeck_RefusesAnOverlongName()
        {
            PlayerModel player = CreatePlayerModel();
            List<CardId> deck = BuildLegalDeck(player);
            string tooLong = new string('x', PlayerDeck.MaxNameLength + 1);

            Assert.That(Execute(player, new PlayerSaveDeck(null, tooLong, deck)), Is.EqualTo(ActionResults.InvalidDeckName));
        }

        [Test]
        public void PlayerSaveDeck_RefusesMoreDecksThanTheCap()
        {
            PlayerModel player = CreatePlayerModel();
            List<CardId> deck = BuildLegalDeck(player);

            for (int i = 0; i < PlayerModel.MaxSavedDecks; i++)
                Assert.That(Execute(player, new PlayerSaveDeck(null, $"Deck {i}", deck)), Is.EqualTo(MetaActionResult.Success));

            Assert.That(Execute(player, new PlayerSaveDeck(null, "One too many", deck)), Is.EqualTo(ActionResults.TooManySavedDecks));
            Assert.That(player.Decks.Count, Is.EqualTo(PlayerModel.MaxSavedDecks));

            // A full account can still overwrite what it already has.
            Assert.That(Execute(player, new PlayerSaveDeck(1, "Renamed", deck)), Is.EqualTo(MetaActionResult.Success));
        }

        [Test]
        public void PlayerSaveDeck_RefusesADeckOfTheWrongSize()
        {
            PlayerModel player = CreatePlayerModel();
            List<CardId> deck = BuildLegalDeck(player);
            deck.RemoveAt(0);

            Assert.That(Execute(player, new PlayerSaveDeck(null, "Short", deck)), Is.EqualTo(ActionResults.DeckWrongSize));
        }

        [Test]
        public void PlayerSaveDeck_RefusesADuplicateCard()
        {
            PlayerModel player = CreatePlayerModel();
            List<CardId> deck = BuildLegalDeck(player);
            deck[1] = deck[0];

            Assert.That(Execute(player, new PlayerSaveDeck(null, "Twins", deck)), Is.EqualTo(ActionResults.DeckDuplicateCard));
        }

        [Test]
        public void PlayerSaveDeck_RefusesACardThePlayerDoesNotOwn()
        {
            PlayerModel player = CreatePlayerModel();
            List<CardId> deck = BuildLegalDeck(player);
            player.Collection.Remove(deck[0]);

            Assert.That(Execute(player, new PlayerSaveDeck(null, "Borrowed", deck)), Is.EqualTo(ActionResults.DeckCardNotOwned));
            Assert.That(player.Decks, Is.Empty);
        }

        [Test]
        public void PlayerSaveDeck_RefusesAnUnownedExpansionCard()
        {
            PlayerModel player = CreatePlayerModel();
            List<CardId> deck = Config.StarterDecks[StarterDeckId.FromString("FireAndFoam")].ToCardIds();
            CardId expansion = CardId.FromString("EmberEaredHare");
            Assert.That(Config.Cards[expansion].Collectible, Is.True);
            Assert.That(player.Collection.ContainsKey(expansion), Is.False);
            deck[0] = expansion;
            Assert.That(Execute(player, new PlayerSaveDeck(null, "New faces", deck)), Is.EqualTo(ActionResults.DeckCardNotOwned));
            Assert.That(player.Decks, Is.Empty);
        }

        [Test]
        public void PlayerSaveDeck_RefusesACardThatIsNotInTheCatalogue()
        {
            PlayerModel player = CreatePlayerModel();
            List<CardId> deck = BuildLegalDeck(player);

            // Owned as far as the collection is concerned, but no such card exists — the shape check catches
            // it before ownership is ever consulted.
            CardId ghost = CardId.FromString("NoSuchCard");
            player.Collection[ghost] = Config.Global.RankMin;
            deck[0] = ghost;

            Assert.That(Execute(player, new PlayerSaveDeck(null, "Ghosts", deck)), Is.EqualTo(ActionResults.DeckUnknownCard));
            Assert.That(player.Decks, Is.Empty);
        }

        [Test]
        public void PlayerSaveDeck_RefusesANonCollectibleCard()
        {
            PlayerModel player = CreatePlayerModel();
            List<CardId> deck = BuildLegalDeck(player);

            CardId token = Config.Cards.Values.First(card => !card.Collectible).CardId;
            player.Collection[token] = Config.Global.RankMin;
            deck[0] = token;

            Assert.That(Execute(player, new PlayerSaveDeck(null, "Tokens", deck)), Is.EqualTo(ActionResults.DeckNotCollectible));
            Assert.That(player.Decks, Is.Empty);
        }

        [Test]
        public void PlayerSaveDeck_TrimsTheNameItStores()
        {
            PlayerModel player = CreatePlayerModel();
            List<CardId> deck = BuildLegalDeck(player);

            Assert.That(Execute(player, new PlayerSaveDeck(null, "  Zoomies  ", deck)), Is.EqualTo(MetaActionResult.Success));
            Assert.That(player.Decks[1].Name, Is.EqualTo("Zoomies"));

            // Measured after trimming, so trailing space is never what pushes a name over the limit.
            string atTheLimit = new string('x', PlayerDeck.MaxNameLength);
            Assert.That(Execute(player, new PlayerSaveDeck(1, $"  {atTheLimit}  ", deck)), Is.EqualTo(MetaActionResult.Success));
            Assert.That(player.Decks[1].Name, Is.EqualTo(atTheLimit));
        }

        [Test]
        public void PlayerSetDisplayName_TrimsTheNameItStores()
        {
            PlayerModel player = CreatePlayerModel();

            // The same rule as every other name in the game.
            Assert.That(Execute(player, new PlayerSetDisplayName("  Whiskers  ")), Is.EqualTo(MetaActionResult.Success));
            Assert.That(player.DisplayName, Is.EqualTo("Whiskers"));
            Assert.That(player.PlayerName, Is.EqualTo("Whiskers"));
        }

        [Test]
        public void ForDeckError_NeverReportsSuccess()
        {
            // The default arm exists for a rule added to DeckValidationError without a result of its own.
            // Reporting Success there would be a save that silently did not happen.
            foreach (DeckValidationError error in Enum.GetValues<DeckValidationError>())
            {
                if (error == DeckValidationError.None)
                    continue;

                Assert.That(ActionResults.ForDeckError(error), Is.Not.EqualTo(MetaActionResult.Success), $"{error}");
            }

            Assert.That(ActionResults.ForDeckError((DeckValidationError)9999), Is.EqualTo(ActionResults.IllegalDeck));
            Assert.That(ActionResults.ForDeckError(DeckValidationError.None), Is.Not.EqualTo(MetaActionResult.Success));
        }

        [Test]
        public void PlayerRenameDeck_RenamesWithoutTouchingTheCards()
        {
            PlayerModel player = CreatePlayerModel();
            List<CardId> deck = BuildLegalDeck(player);
            Execute(player, new PlayerSaveDeck(null, "Before", deck));

            Assert.That(Execute(player, new PlayerRenameDeck(1, "After")), Is.EqualTo(MetaActionResult.Success));
            Assert.That(player.Decks[1].Name, Is.EqualTo("After"));
            Assert.That(player.Decks[1].Cards, Is.EqualTo(deck));
        }

        [Test]
        public void PlayerRenameDeck_RefusesAnUnknownDeckAndABlankName()
        {
            PlayerModel player = CreatePlayerModel();
            List<CardId> deck = BuildLegalDeck(player);
            Execute(player, new PlayerSaveDeck(null, "Before", deck));

            Assert.That(Execute(player, new PlayerRenameDeck(9, "Whatever")), Is.EqualTo(ActionResults.UnknownDeck));
            Assert.That(Execute(player, new PlayerRenameDeck(1, "")), Is.EqualTo(ActionResults.InvalidDeckName));
            Assert.That(player.Decks[1].Name, Is.EqualTo("Before"));
        }

        [Test]
        public void PlayerDeleteDeck_RemovesItAndRefusesAnUnknownId()
        {
            PlayerModel player = CreatePlayerModel();
            List<CardId> deck = BuildLegalDeck(player);
            Execute(player, new PlayerSaveDeck(null, "Doomed", deck));

            Assert.That(Execute(player, new PlayerDeleteDeck(9)), Is.EqualTo(ActionResults.UnknownDeck));
            Assert.That(Execute(player, new PlayerDeleteDeck(1)), Is.EqualTo(MetaActionResult.Success));
            Assert.That(player.Decks, Is.Empty);

            // The id allocator does not rewind, so a deleted id is never handed out again.
            Assert.That(player.NextDeckId, Is.EqualTo(2));
        }

        #endregion

        #region The last played deck

        [Test]
        public void PlayerNoteDeckPlayed_RecordsTheDeckTheAccountWasSeatedWith()
        {
            PlayerModel player = CreatePlayerModel();
            List<CardId> deck = BuildLegalDeck(player);
            Execute(player, new PlayerSaveDeck(null, "First", deck));
            Execute(player, new PlayerSaveDeck(null, "Second", deck));

            Assert.That(player.LastPlayedDeck, Is.Null, "a fresh account has played nothing");

            MetaActionResult result = Execute(player, new PlayerNoteDeckPlayed(DeckChoice.Saved(2)));

            Assert.That(result, Is.EqualTo(MetaActionResult.Success));
            Assert.That(player.LastPlayedDeck.SavedDeckId, Is.EqualTo(2));
        }

        [Test]
        public void PlayerStartPracticeMatch_RecordsNothingItself_BecauseItsRefusalIsInvisibleToTheClient()
        {
            // The shape this pins: the entry refusal is decided by CurrentMatch, a
            // ServerOnly member that reads default here — and this execution is exactly the one a client runs
            // — so the commit body may not write payload-dependent public state. If it did, an entry the
            // server refused would leave the two models disagreeing about a checksummed member, which ends
            // the session on a mismatch. The deck is recorded by PlayerNoteDeckPlayed instead, issued by the
            // actor where the pointer is assigned.
            PlayerModel player = CreatePlayerModel();
            List<CardId> deck = BuildLegalDeck(player);
            Execute(player, new PlayerSaveDeck(null, "First", deck));
            Execute(player, new PlayerSaveDeck(null, "Second", deck));

            Assert.That(Execute(player, new PlayerStartPracticeMatch(DeckChoice.Saved(2), BotProfileId.Practiced)),
                Is.EqualTo(MetaActionResult.Success), "a client's predicted run always passes the pointer check");
            Assert.That(player.LastPlayedDeck, Is.Null, "and it must record nothing while doing so");
        }

        [Test]
        public void PlayerStartPracticeMatch_RefusedEntry_RecordsNothing()
        {
            PlayerModel player = CreatePlayerModel();
            List<CardId> deck = BuildLegalDeck(player);
            Execute(player, new PlayerSaveDeck(null, "First", deck));
            Execute(player, new PlayerNoteDeckPlayed(DeckChoice.Saved(1)));

            Assert.That(Execute(player, new PlayerStartPracticeMatch(DeckChoice.Saved(9), BotProfileId.Practiced)),
                Is.EqualTo(ActionResults.UnknownDeck));
            Assert.That(player.LastPlayedDeck.SavedDeckId, Is.EqualTo(1), "a refusal must not move the remembered deck");
        }

        #endregion

        #region The ranked entry

        [Test]
        public void PlayerEnqueueForRankedMatch_AcceptsALegalDeckAndRecordsNothingItself()
        {
            // The same shape PlayerStartPracticeMatch has to keep, and for the same reason: both refusals a
            // second entry can hit are decided by state the client cannot see — the match pointer is
            // ServerOnly and the search phase is not model state at all — so this execution, which is exactly
            // the one a client runs, always reaches the commit body while the server's may not. A
            // payload-dependent write here would diverge the two models on a refused entry, and a checksum
            // mismatch ends the session.
            PlayerModel player = CreatePlayerModel();
            List<CardId> deck = BuildLegalDeck(player);
            Execute(player, new PlayerSaveDeck(null, "First", deck));
            Execute(player, new PlayerSaveDeck(null, "Second", deck));
            Execute(player, new PlayerNoteDeckPlayed(DeckChoice.Saved(1)));

            Assert.That(Execute(player, new PlayerEnqueueForRankedMatch(DeckChoice.Saved(2))), Is.EqualTo(MetaActionResult.Success));
            Assert.That(player.LastPlayedDeck.SavedDeckId, Is.EqualTo(1), "the deck is recorded where the seat is, not here");
        }

        [Test]
        public void PlayerEnqueueForRankedMatch_ClearsTheNoticeAboutTheLastMatch()
        {
            // A constant, so a refused entry leaves the two models agreeing on it — and it has to be cleared,
            // or the searching dialog goes up over an obituary for a match two ago.
            PlayerModel player = CreatePlayerModel();
            List<CardId> deck = BuildLegalDeck(player);
            Execute(player, new PlayerSaveDeck(null, "First", deck));
            Execute(player, new PlayerNoteMatchGone());

            Assert.That(player.MatchGoneUnseen, Is.True);

            Execute(player, new PlayerEnqueueForRankedMatch(DeckChoice.Saved(1)));

            Assert.That(player.MatchGoneUnseen, Is.False);
        }

        [Test]
        public void PlayerEnqueueForRankedMatch_RefusesAnUnknownDeck()
        {
            PlayerModel player = CreatePlayerModel();

            Assert.That(Execute(player, new PlayerEnqueueForRankedMatch(DeckChoice.Saved(9))), Is.EqualTo(ActionResults.UnknownDeck));
        }

        [Test]
        public void PlayerStartPracticeMatch_AcceptsAStarterDeckOnAnAccountWithNoSavedDecks()
        {
            // The feature, at the action layer: a fresh account can enter with no saved deck at all, because
            // the starter grant already owns every card the starter deck names.
            PlayerModel   player = CreatePlayerModel();
            StarterDeckId first  = null;
            foreach (StarterDeckInfo deck in Config.StarterDecks.Values)
            {
                first = deck.StarterDeckId;
                break;
            }

            Assert.That(player.Decks, Is.Empty);
            Assert.That(Execute(player, new PlayerStartPracticeMatch(DeckChoice.Starter(first), BotProfileId.Practiced)),
                Is.EqualTo(MetaActionResult.Success));
            Assert.That(player.LastPlayedDeck, Is.Null, "and it still records nothing itself");
        }

        [Test]
        public void PlayerEnqueueForRankedMatch_AcceptsAStarterDeck()
        {
            PlayerModel player = CreatePlayerModel();

            foreach (StarterDeckInfo deck in Config.StarterDecks.Values)
            {
                Assert.That(Execute(player, new PlayerEnqueueForRankedMatch(DeckChoice.Starter(deck.StarterDeckId))),
                    Is.EqualTo(MetaActionResult.Success), deck.StarterDeckId.Value);
            }
        }

        [Test]
        public void BothEntries_RefuseAStarterDeckNamingACardTheAccountDoesNotOwn()
        {
            // The one case the ownership argument does not cover, and it is a real one: an account created
            // before a card existed never re-runs the starter grant, so it comes back without that card
            // (Docs/meta.md, "The starter grant"). A starter deck naming it is refused exactly like any
            // other deck with a card the account does not hold — the entry runs the same shared check, and
            // nothing about a starter deck is exempted from it.
            PlayerModel     player = CreatePlayerModel();
            StarterDeckInfo deck   = Config.StarterDecks[StarterDeckId.FromString("FireAndFoam")];
            DeckChoice      choice = DeckChoice.Starter(deck.StarterDeckId);

            Assert.That(Execute(player, new PlayerStartPracticeMatch(choice, BotProfileId.Practiced)),
                Is.EqualTo(MetaActionResult.Success), "the deck is playable while the grant is intact");

            player.Collection.Remove(deck.ToCardIds()[0]);

            Assert.That(Execute(player, new PlayerStartPracticeMatch(choice, BotProfileId.Practiced)),
                Is.EqualTo(ActionResults.DeckCardNotOwned));
            Assert.That(Execute(player, new PlayerEnqueueForRankedMatch(choice)),
                Is.EqualTo(ActionResults.DeckCardNotOwned));
        }

        [Test]
        public void BothEntries_RefuseAStarterDeckTheConfigDoesNotCarry()
        {
            PlayerModel player = CreatePlayerModel();
            DeckChoice  ghost  = DeckChoice.Starter(StarterDeckId.FromString("NoSuchDeck"));

            Assert.That(Execute(player, new PlayerStartPracticeMatch(ghost, BotProfileId.Practiced)),
                Is.EqualTo(ActionResults.UnknownStarterDeck));
            Assert.That(Execute(player, new PlayerEnqueueForRankedMatch(ghost)),
                Is.EqualTo(ActionResults.UnknownStarterDeck));
        }

        [Test]
        public void BothEntries_RefuseAMalformedDeckChoice()
        {
            // A payload no picker produces: neither half set. Refused rather than guessed at, on both entries.
            PlayerModel player = CreatePlayerModel();

            Assert.That(Execute(player, new PlayerStartPracticeMatch(new DeckChoice(), BotProfileId.Practiced)),
                Is.EqualTo(ActionResults.MalformedDeckChoice));
            Assert.That(Execute(player, new PlayerEnqueueForRankedMatch(null)),
                Is.EqualTo(ActionResults.MalformedDeckChoice));
        }

        [Test]
        public void PlayerEnqueueForRankedMatch_RefusesADeckThatIsNoLongerLegal()
        {
            // The deck the queue matches on is the deck the match is dealt from, so a list that is illegal now
            // is refused before anybody is paired against its Power Score.
            PlayerModel player = CreatePlayerModel();
            List<CardId> deck = BuildLegalDeck(player);
            Execute(player, new PlayerSaveDeck(null, "First", deck));

            player.Collection.Remove(deck[0]);

            Assert.That(Execute(player, new PlayerEnqueueForRankedMatch(DeckChoice.Saved(1))), Is.EqualTo(ActionResults.DeckCardNotOwned));
        }

        [Test]
        public void PlayerEnqueueForRankedMatch_LeavesTheOneEntryRuleToTheActor()
        {
            // Deliberately NOT an action result, and the difference from PlayerStartPracticeMatch is the point
            // of the directed channel: a refusal decided by ServerOnly state is one nobody can
            // observe — the model does not change and the SDK has no route for an action's result. The actor
            // refuses instead and answers with MatchmakingEnded(Refused).
            PlayerModel player = CreatePlayerModel();
            List<CardId> deck = BuildLegalDeck(player);
            Execute(player, new PlayerSaveDeck(null, "First", deck));
            player.CurrentMatch = EntityId.CreateRandom(EntityKindCore.Player);

            Assert.That(Execute(player, new PlayerEnqueueForRankedMatch(DeckChoice.Saved(1))), Is.EqualTo(MetaActionResult.Success));

            // And it still records nothing while doing so, which is what keeps the two models agreeing when
            // the account turns out to be at a table after all.
            Assert.That(player.LastPlayedDeck, Is.Null);
        }

        [Test]
        public void PlayerCancelMatchmaking_AlwaysSucceedsAndChangesNothingOnTheModel()
        {
            // The client cannot know the search phase, so it never predicts a refusal; the real defence lives
            // on the player actor, where the seat reservation commits.
            PlayerModel player = CreatePlayerModel();

            Assert.That(Execute(player, new PlayerCancelMatchmaking()), Is.EqualTo(MetaActionResult.Success));
            Assert.That(player.MatchGoneUnseen, Is.False);
            Assert.That(player.CurrentMatch, Is.EqualTo(EntityId.None));
        }

        [Test]
        public void PlayerClearMatchPointer_ReleasesTheAccountFromItsTable()
        {
            PlayerModel player = CreatePlayerModel();
            player.CurrentMatch = EntityId.CreateRandom(EntityKindGame.Match);
            Assert.That(player.IsInMatch, Is.True);

            Assert.That(Execute(player, new PlayerClearMatchPointer()), Is.EqualTo(MetaActionResult.Success));

            Assert.That(player.CurrentMatch, Is.EqualTo(EntityId.None));
            Assert.That(player.IsInMatch, Is.False);
        }

        [Test]
        public void PlayerNoteMatchGone_IsWhatTellsThePlayerTheirTableWent()
        {
            // The other half of every clear: the pointer going is invisible on the client — CurrentMatch is
            // ServerOnly and reads as none there either way — so without this public flag a player whose game
            // vanished would simply find themselves on Home with no explanation. Both server clear sites pair
            // the two.
            PlayerModel player = CreatePlayerModel();
            player.CurrentMatch = EntityId.CreateRandom(EntityKindGame.Match);

            Assert.That(Execute(player, new PlayerNoteMatchGone()), Is.EqualTo(MetaActionResult.Success));

            Assert.That(player.MatchGoneUnseen, Is.True);
        }

        [Test]
        public void PlayerSetNewcomerShieldWaived_TurnsTheWaiverOnAndOffAgain()
        {
            // The dashboard's whole control over the shield, and the flag the ticket freezes at enqueue.
            PlayerModel player = CreatePlayerModel();
            Assert.That(player.NewcomerShieldWaived, Is.False, "a new account carries its shield");

            Assert.That(Execute(player, new PlayerSetNewcomerShieldWaived(true)), Is.EqualTo(MetaActionResult.Success));
            Assert.That(player.NewcomerShieldWaived, Is.True);

            Assert.That(Execute(player, new PlayerSetNewcomerShieldWaived(false)), Is.EqualTo(MetaActionResult.Success));
            Assert.That(player.NewcomerShieldWaived, Is.False, "and it can be handed back");
        }

        [Test]
        public void PlayerSetNewcomerShieldWaived_LeavesTheRankedCountAlone()
        {
            // The waiver is a second fact beside the count rather than a forged count: the tier is decided by
            // both flags and both counts at formation, and the record is what the Heist reads back.
            PlayerModel player = CreatePlayerModel();

            Execute(player, new PlayerSetNewcomerShieldWaived(true));

            Assert.That(player.Record.RankedMatchesPlayed, Is.Zero);
        }

        [Test]
        public void PlayerSetNewcomerShieldWaived_SeedsTheDashboardFormFromTheAccountsOwnValue()
        {
            // What makes the generated modal tell the truth. The SDK builds the form's initial state from an
            // instance with every member at its default and then calls this hook if the action implements
            // IPlayerDashboardAction<>; without it the form opens on false for every account, and the value
            // that restores a shield is then the one value the modal refuses as "no changes".
            PlayerModel player = CreatePlayerModel();

            PlayerSetNewcomerShieldWaived onAShieldedAccount = new PlayerSetNewcomerShieldWaived(true);
            onAShieldedAccount.InitializeDefaultStateForDashboard(player);
            Assert.That(onAShieldedAccount.Waived, Is.False, "the form opens on a shielded account's own value");

            Execute(player, new PlayerSetNewcomerShieldWaived(true));

            PlayerSetNewcomerShieldWaived onAWaivedAccount = new PlayerSetNewcomerShieldWaived(false);
            onAWaivedAccount.InitializeDefaultStateForDashboard(player);
            Assert.That(onAWaivedAccount.Waived, Is.True, "and on a waived account's own value");
        }

        [Test]
        public void PlayerSetNewcomerShieldWaived_IsIdempotentAtTheValueTheAccountAlreadyHolds()
        {
            // Confirming the value the form opened on is what restores a shield once the hook above lands, so
            // re-submitting the current value has to be a no-op rather than a toggle.
            PlayerModel player = CreatePlayerModel();
            Execute(player, new PlayerSetNewcomerShieldWaived(true));

            Assert.That(Execute(player, new PlayerSetNewcomerShieldWaived(true)), Is.EqualTo(MetaActionResult.Success));
            Assert.That(player.NewcomerShieldWaived, Is.True);
            Assert.That(player.Record.RankedMatchesPlayed, Is.Zero);
        }

        #endregion

        #region The result the table delivers

        /// <summary> A result with no Heist on it, which is every tier that moves no ranks. </summary>
        static PlayerApplyMatchResult Result(MatchAccountOutcome outcome, bool wasRanked, int ratingDelta)
            => new PlayerApplyMatchResult(outcome, wasRanked, ratingDelta, heistPicks: null);

        /// <summary> A decided ranked result carrying the picks the Heist took. </summary>
        static PlayerApplyMatchResult Heisted(MatchAccountOutcome outcome, params CardId[] picks)
            => new PlayerApplyMatchResult(outcome, wasRanked: true, ratingDelta: 0, heistPicks: new List<CardId>(picks));

        /// <summary> One of the account's cards, held at a named rank. </summary>
        static CardId CardAtRank(PlayerModel player, int rank, int ndx = 0)
        {
            CardId card = player.Collection.Keys.Skip(ndx).First();
            player.Collection[card] = rank;
            return card;
        }

        [Test]
        public void PlayerApplyMatchResult_ARankedResultMovesTheRatingByItsDelta()
        {
            PlayerModel player = CreatePlayerModel();
            int seeded = player.Record.Rating;

            Assert.That(Execute(player, Result(MatchAccountOutcome.Win, wasRanked: true, ratingDelta: 16)),
                Is.EqualTo(MetaActionResult.Success));

            Assert.That(player.Record.Rating, Is.EqualTo(seeded + 16));
            Assert.That(player.Record.RankedWins, Is.EqualTo(1));
            Assert.That(player.Record.RankedMatchesPlayed, Is.EqualTo(1));
        }

        [Test]
        public void PlayerApplyMatchResult_APracticeResultMovesTheRatingByNothing()
        {
            // The gate is IsRanked, not the tier: a practice table carries a zero delta and must not touch the
            // number the queue bands on, however the counters move.
            PlayerModel player = CreatePlayerModel();
            int seeded = player.Record.Rating;

            Execute(player, Result(MatchAccountOutcome.Loss, wasRanked: false, ratingDelta: 0));

            Assert.That(player.Record.Rating, Is.EqualTo(seeded));
            Assert.That(player.Record.UnrankedMatchesPlayed, Is.EqualTo(1));
            Assert.That(player.Record.RankedMatchesPlayed, Is.Zero);
        }

        [Test]
        public void PlayerApplyMatchResult_ALossTakesTheDeltaOffAndNeverBelowZero()
        {
            // A run of losses cannot take an account below the bottom of the ordering, and the floor is
            // applied in the action so both sides of a synchronized server action reach the same number.
            PlayerModel player = CreatePlayerModel();
            player.Record.Rating = 10;

            Execute(player, Result(MatchAccountOutcome.Loss, wasRanked: true, ratingDelta: -16));

            Assert.That(player.Record.Rating, Is.Zero);
            Assert.That(player.Record.RankedLosses, Is.EqualTo(1));
        }

        [Test]
        public void PlayerApplyMatchResult_ADrawCountsAsADrawAndStillCarriesItsDelta()
        {
            PlayerModel player = CreatePlayerModel();
            int seeded = player.Record.Rating;

            Execute(player, Result(MatchAccountOutcome.Draw, wasRanked: true, ratingDelta: -3));

            Assert.That(player.Record.RankedDraws, Is.EqualTo(1));
            Assert.That(player.Record.Rating, Is.EqualTo(seeded - 3));
        }

        #endregion

        #region The rank transfer

        // The payout matrix, against a real PlayerModel rather than against a copy of the arithmetic: the
        // collection is checksummed, so what has to be right is the write path as well as the sum. The pure
        // halves of the rule are pinned beside the rest of the Heist in Server.Tests/MatchHeistPolicyTests.

        [Test]
        public void PlayerApplyMatchResult_TheWinnersCopyGainsARank()
        {
            PlayerModel player = CreatePlayerModel();
            CardId card = CardAtRank(player, 3);

            Assert.That(Execute(player, Heisted(MatchAccountOutcome.Win, card)), Is.EqualTo(MetaActionResult.Success));
            Assert.That(player.Collection[card], Is.EqualTo(4));
        }

        [Test]
        public void PlayerApplyMatchResult_AWinnerWhoOwnsNoCopyIsGivenOneAtTheFloor()
        {
            // The acquisition branch, and the only place in the game a card ENTERS a collection after the
            // starter grant. It is unreachable in this build — every account holds every collectible card —
            // so it is reached here by taking the card away first. What it also pins is that a synchronized
            // server action may ADD a key to the checksummed collection: both sides run the same body in the
            // same order, so the insertion agrees.
            PlayerModel player = CreatePlayerModel();
            CardId card = player.Collection.Keys.First();
            player.Collection.Remove(card);

            Execute(player, Heisted(MatchAccountOutcome.Win, card));

            Assert.That(player.Collection.ContainsKey(card), Is.True, "NEW! joins the collection at the floor");
            Assert.That(player.Collection[card], Is.EqualTo(Config.Global.RankMin));
        }

        [Test]
        public void PlayerApplyMatchResult_AWinnerAtTheCeilingGainsNothing()
        {
            // Nothing above rank five, and — with the cosmetic currency deferred rather than designed away —
            // nothing paid instead. The pick simply moves nothing on this side.
            PlayerModel player = CreatePlayerModel();
            CardId card = CardAtRank(player, Config.Global.RankMax);

            Execute(player, Heisted(MatchAccountOutcome.Win, card));

            Assert.That(player.Collection[card], Is.EqualTo(Config.Global.RankMax));
        }

        [Test]
        public void PlayerApplyMatchResult_AWinnerWhoLockedTheirOwnCopyGainsNothing()
        {
            // A lock runs both ways: retirement, not insurance with upside. The menu already subtracts the
            // winner's frozen set, so reaching this needs a lock taken AFTER the pick — which is exactly the
            // case the live re-read exists for.
            PlayerModel player = CreatePlayerModel();
            CardId card = CardAtRank(player, 3);
            Execute(player, new PlayerSetLockSlot(0, card));

            Execute(player, Heisted(MatchAccountOutcome.Win, card));

            Assert.That(player.Collection[card], Is.EqualTo(3), "frozen cards do not grow either");
        }

        [Test]
        public void PlayerApplyMatchResult_TheLosersCopyLosesARank()
        {
            PlayerModel player = CreatePlayerModel();
            CardId card = CardAtRank(player, 3);

            Execute(player, Heisted(MatchAccountOutcome.Loss, card));

            Assert.That(player.Collection[card], Is.EqualTo(2));
        }

        [Test]
        public void PlayerApplyMatchResult_TheLoserAtTheFloorKeepsTheCard()
        {
            // Cards are never removed from a collection and rank one is safe, so the floor holds rather than
            // going to zero or negative.
            PlayerModel player = CreatePlayerModel();
            CardId card = CardAtRank(player, Config.Global.RankMin);

            Execute(player, Heisted(MatchAccountOutcome.Loss, card));

            Assert.That(player.Collection.ContainsKey(card), Is.True);
            Assert.That(player.Collection[card], Is.EqualTo(Config.Global.RankMin));
        }

        [Test]
        public void PlayerApplyMatchResult_ALoserWhoLockedTheCardSinceTheFinishKeepsItsRank()
        {
            // The lock is re-read at the moment the transfer applies rather than trusted off the snapshot the
            // pick was filtered against. Refusing to move a locked rank is the whole promise the feature
            // makes, and a promise that holds except on the retry path is not one.
            PlayerModel player = CreatePlayerModel();
            CardId card = CardAtRank(player, 4);
            Execute(player, new PlayerSetLockSlot(0, card));

            Execute(player, Heisted(MatchAccountOutcome.Loss, card));

            Assert.That(player.Collection[card], Is.EqualTo(4));
        }

        [Test]
        public void PlayerApplyMatchResult_TheUnderdogsTwoPicksApplyIndependently()
        {
            PlayerModel player = CreatePlayerModel();
            CardId first  = CardAtRank(player, 3, ndx: 0);
            CardId second = CardAtRank(player, Config.Global.RankMin, ndx: 1);

            Execute(player, Heisted(MatchAccountOutcome.Loss, first, second));

            Assert.That(player.Collection[first], Is.EqualTo(2));
            Assert.That(player.Collection[second], Is.EqualTo(Config.Global.RankMin),
                "one pick hitting the floor does not stop the other from moving");
        }

        [Test]
        public void PlayerApplyMatchResult_TwoPicksOfOneCardMoveItTwice()
        {
            // A loser who played two copies offers two, so the upset may take both — and each pick is worth
            // one rank, which means the second re-reads what the first left.
            PlayerModel winner = CreatePlayerModel();
            CardId card = CardAtRank(winner, 2);

            Execute(winner, Heisted(MatchAccountOutcome.Win, card, card));
            Assert.That(winner.Collection[card], Is.EqualTo(4));

            PlayerModel loser = CreatePlayerModel();
            loser.Collection[card] = 4;

            Execute(loser, Heisted(MatchAccountOutcome.Loss, card, card));
            Assert.That(loser.Collection[card], Is.EqualTo(2));
        }

        [Test]
        public void PlayerApplyMatchResult_ATierThatMovesNoRanksTouchesNoCollection()
        {
            // A favourite's win, a shielded match, practice and a draw all arrive with no picks at all, which
            // is what makes "nothing moves" one code path rather than four.
            PlayerModel player = CreatePlayerModel();
            CardId card = CardAtRank(player, 3);

            Execute(player, Result(MatchAccountOutcome.Win, wasRanked: true, ratingDelta: 12));

            Assert.That(player.Collection[card], Is.EqualTo(3));
        }

        [Test]
        public void PlayerApplyMatchResult_ADrawMovesNoRankEvenIfPicksArrive()
        {
            // No Heist runs on a draw — neither seat is the loser — so a payload that carried picks anyway
            // would have no side to apply. It moves nothing rather than guessing one.
            PlayerModel player = CreatePlayerModel();
            CardId card = CardAtRank(player, 3);

            Execute(player, Heisted(MatchAccountOutcome.Draw, card));

            Assert.That(player.Collection[card], Is.EqualTo(3));
        }


        [Test]
        public void PlayerApplyMatchResult_ReadsNothingServerOnly()
        {
            // The action is synchronized, so the client replays it against a model where every ServerOnly
            // member is default. A body that branched on one — the match pointer, the applied-results set —
            // would leave the two models disagreeing about a checksummed collection, and a checksum mismatch
            // ends the session. So the same payload against two models that differ ONLY in their server-only
            // halves has to land in the same place.
            PlayerModel asServer = CreatePlayerModel();
            PlayerModel asClient = CreatePlayerModel();

            CardId card = CardAtRank(asServer, 3);
            asClient.Collection[card] = 3;

            asServer.CurrentMatch = EntityId.CreateRandom(EntityKindGame.Match);
            asServer.AppliedMatchResults.Add(asServer.CurrentMatch);

            Execute(asServer, Heisted(MatchAccountOutcome.Win, card));
            Execute(asClient, Heisted(MatchAccountOutcome.Win, card));

            Assert.That(asServer.Collection[card], Is.EqualTo(4));
            Assert.That(asClient.Collection[card], Is.EqualTo(asServer.Collection[card]));
            Assert.That(asClient.Record.Rating, Is.EqualTo(asServer.Record.Rating));
        }

        #endregion

        #region Locks

        /// <summary>
        /// One of the account's cards, grown to the lowest rank a lock accepts. Every card a fresh account
        /// owns is at the rank floor and <c>Global.MinLockRank</c> keeps the floor unlockable, so a lock test
        /// has to raise a card first — which is what the Heist will do once it exists.
        /// </summary>
        static CardId LockableCard(PlayerModel player, int ndx = 0)
        {
            CardId card = player.Collection.Keys.Skip(ndx).First();
            player.Collection[card] = Config.Global.MinLockRank;
            return card;
        }

        static List<CardId> LockableCards(PlayerModel player, int count)
        {
            List<CardId> cards = new List<CardId>();
            for (int ndx = 0; ndx < count; ndx++)
                cards.Add(LockableCard(player, ndx));

            return cards;
        }

        [Test]
        public void PlayerSetLockSlot_LocksAnOwnedCard()
        {
            PlayerModel player = CreatePlayerModel();
            CardId card = LockableCard(player);

            Assert.That(Execute(player, new PlayerSetLockSlot(0, card)), Is.EqualTo(MetaActionResult.Success));
            Assert.That(player.LockSlots[0], Is.EqualTo(card));
        }

        [Test]
        public void PlayerSetLockSlot_RefusesACardBelowTheRankThreshold()
        {
            // A rank-1 card is already safe — the floor protects it — and has no growth to freeze, so locking
            // one was a free denial play: it took the card off a winner's Heist menu at no cost (F6).
            PlayerModel player = CreatePlayerModel();
            CardId card = player.Collection.Keys.First();

            Assert.That(player.Collection[card], Is.LessThan(Config.Global.MinLockRank), "a fresh account is all at the floor");
            Assert.That(Execute(player, new PlayerSetLockSlot(0, card)), Is.EqualTo(ActionResults.CardRankTooLowToLock));
            Assert.That(player.LockSlots, Is.Empty);
        }

        [Test]
        public void PlayerSetLockSlot_ClearingASlotIsNeverRefusedByTheRankRule()
        {
            // A lock set while the card cleared the threshold must be undoable afterwards whatever the card's
            // rank is now, or a threshold raised by a content edit would strand a slot forever.
            PlayerModel player = CreatePlayerModel();
            CardId card = LockableCard(player);
            Execute(player, new PlayerSetLockSlot(0, card));
            player.Collection[card] = Config.Global.RankMin;

            Assert.That(Execute(player, new PlayerSetLockSlot(0, null)), Is.EqualTo(MetaActionResult.Success));
            Assert.That(player.LockSlots, Is.Empty);
        }

        [Test]
        public void PlayerSetLockSlot_UnlocksAndIsIdempotentOnAnEmptySlot()
        {
            PlayerModel player = CreatePlayerModel();
            CardId card = LockableCard(player);
            Execute(player, new PlayerSetLockSlot(0, card));

            Assert.That(Execute(player, new PlayerSetLockSlot(0, null)), Is.EqualTo(MetaActionResult.Success));
            Assert.That(player.LockSlots.ContainsKey(0), Is.False);

            Assert.That(Execute(player, new PlayerSetLockSlot(0, null)), Is.EqualTo(MetaActionResult.Success));
        }

        [Test]
        public void PlayerSetLockSlot_RefusesASlotOutsideTheAccountsCount()
        {
            PlayerModel player = CreatePlayerModel();
            CardId card = LockableCard(player);

            Assert.That(Execute(player, new PlayerSetLockSlot(-1, card)), Is.EqualTo(ActionResults.InvalidLockSlot));
            Assert.That(Execute(player, new PlayerSetLockSlot(player.LockSlotCount, card)), Is.EqualTo(ActionResults.InvalidLockSlot));
            Assert.That(player.LockSlots, Is.Empty);
        }

        [Test]
        public void PlayerSetLockSlot_RefusesACardThePlayerDoesNotOwn()
        {
            PlayerModel player = CreatePlayerModel();
            CardId card = LockableCard(player);
            player.Collection.Remove(card);

            Assert.That(Execute(player, new PlayerSetLockSlot(0, card)), Is.EqualTo(ActionResults.CardNotOwned));
        }

        [Test]
        public void PlayerSetLockSlot_MovesALockRatherThanDuplicatingIt()
        {
            PlayerModel player = CreatePlayerModel();
            List<CardId> owned = LockableCards(player, 2);

            Execute(player, new PlayerSetLockSlot(0, owned[0]));
            Execute(player, new PlayerSetLockSlot(1, owned[1]));

            // Locking a card that already sits in slot 0 into slot 1 vacates slot 0 rather than locking it twice.
            Assert.That(Execute(player, new PlayerSetLockSlot(1, owned[0])), Is.EqualTo(MetaActionResult.Success));
            Assert.That(player.LockSlots.ContainsKey(0), Is.False);
            Assert.That(player.LockSlots[1], Is.EqualTo(owned[0]));
        }

        [Test]
        public void PlayerSetLockSlot_ReplacingAFullSlotUnlocksWhatWasThere()
        {
            PlayerModel player = CreatePlayerModel();
            List<CardId> owned = LockableCards(player, 3);

            Execute(player, new PlayerSetLockSlot(0, owned[0]));
            Execute(player, new PlayerSetLockSlot(1, owned[1]));
            Execute(player, new PlayerSetLockSlot(1, owned[2]));

            Assert.That(player.LockSlots[0], Is.EqualTo(owned[0]));
            Assert.That(player.LockSlots[1], Is.EqualTo(owned[2]));
            Assert.That(player.LockSlots.Values.Contains(owned[1]), Is.False);
        }

        [Test]
        public void PlayerSetLockSlot_ReLockingTheSameSlotWithTheSameCardIsANoOp()
        {
            PlayerModel player = CreatePlayerModel();
            CardId card = LockableCard(player);

            Execute(player, new PlayerSetLockSlot(0, card));
            Assert.That(Execute(player, new PlayerSetLockSlot(0, card)), Is.EqualTo(MetaActionResult.Success));
            Assert.That(player.LockSlots.Count, Is.EqualTo(1));
            Assert.That(player.LockSlots[0], Is.EqualTo(card));
        }

        [Test]
        public void PlayerDevSetCardRank_MovesARankAndRefusesTheRest()
        {
            // The development-only route a screen uses to reach a rule that turns on rank; nothing in the game
            // moves a rank until the Heist lands.
            PlayerModel player = CreatePlayerModel();
            CardId card = player.Collection.Keys.First();

            Assert.That(Execute(player, new PlayerDevSetCardRank(card, Config.Global.RankMax)), Is.EqualTo(MetaActionResult.Success));
            Assert.That(player.Collection[card], Is.EqualTo(Config.Global.RankMax));

            Assert.That(Execute(player, new PlayerDevSetCardRank(card, Config.Global.RankMax + 1)), Is.EqualTo(ActionResults.InvalidCardRank));
            Assert.That(Execute(player, new PlayerDevSetCardRank(card, Config.Global.RankMin - 1)), Is.EqualTo(ActionResults.InvalidCardRank));
            Assert.That(player.Collection[card], Is.EqualTo(Config.Global.RankMax), "a refused rank moves nothing");

            player.Collection.Remove(card);
            Assert.That(Execute(player, new PlayerDevSetCardRank(card, Config.Global.RankMin)), Is.EqualTo(ActionResults.CardNotOwned));
        }

        [Test]
        public void PlayerDevSetCardRank_RefusesALockedCard()
        {
            // A locked card can neither lose a rank nor gain one, and the only rank-mover in the build has to
            // keep that rule rather than be the exception later code copies.
            PlayerModel player = CreatePlayerModel();
            CardId card = LockableCard(player);
            Execute(player, new PlayerSetLockSlot(0, card));

            Assert.That(Execute(player, new PlayerDevSetCardRank(card, Config.Global.RankMax)), Is.EqualTo(ActionResults.CardIsLocked));
            Assert.That(player.Collection[card], Is.EqualTo(Config.Global.MinLockRank), "the frozen rank did not move");

            // Unlocked, it moves again.
            Execute(player, new PlayerSetLockSlot(0, null));
            Assert.That(Execute(player, new PlayerDevSetCardRank(card, Config.Global.RankMax)), Is.EqualTo(MetaActionResult.Success));
        }

        [Test]
        public void ARaisedCardCanBeLocked_AndTheFloorCannot()
        {
            // Both sides of the threshold in one place, since the whole of F6 is which side a card is on.
            PlayerModel player = CreatePlayerModel();
            List<CardId> owned = player.Collection.Keys.Take(2).ToList();

            Execute(player, new PlayerDevSetCardRank(owned[0], Config.Global.MinLockRank));

            Assert.That(Execute(player, new PlayerSetLockSlot(0, owned[0])), Is.EqualTo(MetaActionResult.Success));
            Assert.That(Execute(player, new PlayerSetLockSlot(1, owned[1])), Is.EqualTo(ActionResults.CardRankTooLowToLock));
            Assert.That(player.LockSlots.Count, Is.EqualTo(1));
        }

        #endregion

        #region Rank tracks

        [Test]
        public void CardStats_AtTheRankFloorAreThePrintedNumbers()
        {
            CardInfo card = Config.Cards[CardId.FromString("EmberKit")];
            CardStats stats = card.GetStatsAtRank(Config.Global.RankMin);

            Assert.That(stats.Attack, Is.EqualTo(card.Attack));
            Assert.That(stats.Health, Is.EqualTo(card.Health));
            Assert.That(stats.Cost, Is.EqualTo(card.Cost));
            Assert.That(card.HasRankGrowth(Config.Global.RankMin), Is.False);
        }

        [Test]
        public void CardStats_AccumulateTheTracksMilestoneDeltas()
        {
            // CritterDefault: +0/+1 at rank 3, +1/+0 at rank 5.
            CardInfo card = Config.Cards[CardId.FromString("EmberKit")];

            CardStats atThree = card.GetStatsAtRank(3);
            Assert.That(atThree.Attack, Is.EqualTo(card.Attack));
            Assert.That(atThree.Health, Is.EqualTo(card.Health + 1));

            CardStats atFive = card.GetStatsAtRank(Config.Global.RankMax);
            Assert.That(atFive.Attack, Is.EqualTo(card.Attack + 1));
            Assert.That(atFive.Health, Is.EqualTo(card.Health + 1));
            Assert.That(card.HasRankGrowth(Config.Global.RankMax), Is.True);
        }

        [Test]
        public void CardStats_OfACardWithNoTrackAreItsPrintedNumbers()
        {
            // Every catalogue card is authored against a track, so this covers the one case config cannot
            // produce: a card built in code with none at all.
            CardInfo trackless = new CardInfo(
                CardId.FromString("Trackless"),
                "Trackless",
                MetaRef<ClanInfo>.FromItem(Config.Clans.Values.First()),
                CardType.Critter,
                CardRarity.Common,
                cost: 3,
                rankTrack: null,
                attack: 10,
                health: 20);

            for (int rank = Config.Global.RankMin; rank <= Config.Global.RankMax; rank++)
            {
                CardStats stats = trackless.GetStatsAtRank(rank);
                Assert.That(stats.Attack, Is.EqualTo(10));
                Assert.That(stats.Health, Is.EqualTo(20));
                Assert.That(stats.Cost, Is.EqualTo(3));
                Assert.That(stats.EffectAmountDelta, Is.Zero);
                Assert.That(trackless.HasRankGrowth(rank), Is.False);
            }
        }

        [Test]
        public void CardStats_OfAFlatTrackNeverMove()
        {
            // Cinder Storm is authored on the None track: its numbers are the same at every rank.
            CardInfo card = Config.Cards[CardId.FromString("CinderStorm")];

            for (int rank = Config.Global.RankMin; rank <= Config.Global.RankMax; rank++)
                Assert.That(card.HasRankGrowth(rank), Is.False, $"rank {rank} moved a flat card");
        }

        #endregion
    }
}
