using Metaplay.Core;
using Metaplay.Core.Analytics;
using Metaplay.Core.Config;
using Metaplay.Core.League;
using Metaplay.Core.Model;
using Metaplay.Core.Player;
using Metaplay.Core.Serialization;
using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;

namespace Game.Logic.Tests
{
    /// <summary>
    /// Tests the cosmetics wardrobe (<c>docs/cosmetics.md</c>): buying, equipping, the public identity projection,
    /// tournament grants, acknowledgement and the schema migrations.
    /// <para>
    /// A purchase is a client action over the published catalogue and the replicated wallet, so the logic lives in
    /// shared code and these tests need no server.
    /// </para>
    /// </summary>
    [TestFixture]
    public class CosmeticsTests
    {
        static readonly MetaTime Now = MetaTime.FromMillisecondsSinceEpoch(1_700_000_000_000);

        static EntityId PlayerId(int index) => EntityId.Create(EntityKindCore.Player, (ulong)index);

        /// <summary>Counts the calls to <see cref="IPlayerModelServerListener.OnPublicIdentityChanged"/>.</summary>
        sealed class IdentityWatcher : IPlayerModelServerListener
        {
            public int IdentityChanges { get; private set; }

            public void OnPublicIdentityChanged() => IdentityChanges += 1;
        }

        static PlayerModel NewPlayer(
            List<PlayerEventBase> events   = null,
            RewardBundle          wallet   = null,
            IdentityWatcher       watcher  = null)
        {
            PlayerModel player = TestPlayers.New(Now, TestGameConfig.Build(wallet), events);

            if (watcher != null)
                player.ServerListener = watcher;

            return player;
        }

        static MetaActionResult Buy(PlayerModel player, CosmeticId id, bool commit = true) =>
            new PlayerBuyCosmetic(id).Execute(player, commit);

        static MetaActionResult Equip(PlayerModel player, CosmeticId id, bool commit = true) =>
            new PlayerEquipCosmetic(id).Execute(player, commit);

        static MetaActionResult Acknowledge(PlayerModel player, bool commit = true) =>
            new PlayerAcknowledgeCosmetics().Execute(player, commit);

        #region Buying

        /// <summary>A purchase takes the price from the wallet, adds the item to the wardrobe and equips it.</summary>
        [Test]
        public void BuyingTakesThePriceAndEquipsWhatWasBought()
        {
            PlayerModel player = NewPlayer();

            Assert.That(Buy(player, TestGameConfig.SilverFrame), Is.EqualTo(MetaActionResult.Success));

            Assert.That(player.Wallet.Coins, Is.EqualTo(3_000 - TestGameConfig.SilverFramePriceCoins));
            Assert.That(player.Cosmetics.Owns(TestGameConfig.SilverFrame), Is.True);
            Assert.That(player.Cosmetics.EquippedIn(CosmeticKind.Frame), Is.EqualTo(TestGameConfig.SilverFrame));
        }

        /// <summary>
        /// A purchase the wallet cannot pay for changes neither the balance, the owned items nor the equipped
        /// items. The action checks the wallet before it writes anything.
        /// </summary>
        [Test]
        public void APurchaseTheWalletCannotPayForMovesNothing()
        {
            PlayerModel player = NewPlayer();

            Assert.That(Buy(player, TestGameConfig.EmeraldFrame), Is.EqualTo(ActionResults.InsufficientFunds),
                "the starting 100 gems must not cover the cheapest gem item");

            Assert.That(player.Wallet.Gems, Is.EqualTo(100));
            Assert.That(player.Cosmetics.Owned, Is.EquivalentTo(CosmeticDefaults.All), "a refused purchase acquires nothing");
            Assert.That(player.Cosmetics.EquippedIn(CosmeticKind.Frame), Is.EqualTo(CosmeticDefaults.Frame));
        }

        /// <summary>
        /// With <c>commit: false</c>, the action returns the result a commit would return and writes nothing, not
        /// even an event. An event written there would be counted again when the action runs with
        /// <c>commit: true</c>.
        /// </summary>
        [Test]
        public void TheDryRunPassAnswersTheSameAndWritesNothing()
        {
            List<PlayerEventBase> events = new List<PlayerEventBase>();
            PlayerModel           player = NewPlayer(events);
            events.Clear();

            Assert.That(Buy(player, TestGameConfig.SilverFrame, commit: false), Is.EqualTo(MetaActionResult.Success));
            Assert.That(Buy(player, TestGameConfig.EmeraldFrame, commit: false), Is.EqualTo(ActionResults.InsufficientFunds));

            Assert.That(player.Wallet.Coins, Is.EqualTo(3_000));
            Assert.That(player.Cosmetics.Owned, Is.EquivalentTo(CosmeticDefaults.All), "a dry run acquires nothing");
            Assert.That(events, Is.Empty);
        }

        /// <summary>Ownership is permanent, so buying an owned item is refused and charges nothing.</summary>
        [Test]
        public void BuyingSomethingAlreadyOwnedIsRefusedBeforeTheWallet()
        {
            PlayerModel player = NewPlayer();
            Buy(player, TestGameConfig.SilverFrame);

            int coins = player.Wallet.Coins;

            Assert.That(Buy(player, TestGameConfig.SilverFrame), Is.EqualTo(ActionResults.CosmeticAlreadyOwned));
            Assert.That(player.Wallet.Coins, Is.EqualTo(coins));
        }

        /// <summary>The buy action refuses an item that the catalogue marks as not purchasable, such as a tournament prize.</summary>
        [Test]
        public void AnItemThatIsNotForSaleCannotBeBought()
        {
            PlayerModel player = NewPlayer();

            Assert.That(Buy(player, TestGameConfig.ChampionFrame), Is.EqualTo(ActionResults.CosmeticNotPurchasable));
            Assert.That(player.Cosmetics.Owned, Is.EquivalentTo(CosmeticDefaults.All), "a refused purchase acquires nothing");
        }

        /// <summary>
        /// Buying an avatar takes the price, equips the avatar slot, and puts the avatar in the public identity.
        /// </summary>
        [Test]
        public void BuyingAnAvatarEquipsTheAvatarSlot()
        {
            PlayerModel player = NewPlayer();

            Assert.That(Buy(player, TestGameConfig.ClubAvatar), Is.EqualTo(MetaActionResult.Success));

            Assert.That(player.Wallet.Coins, Is.EqualTo(3_000 - TestGameConfig.ClubAvatarPriceCoins));
            Assert.That(player.Cosmetics.EquippedIn(CosmeticKind.Avatar), Is.EqualTo(TestGameConfig.ClubAvatar));
            Assert.That(player.BuildPublicIdentity().AvatarId, Is.EqualTo(TestGameConfig.ClubAvatar));
        }

        /// <summary>A null id or an id missing from the catalogue is refused and charges nothing.</summary>
        [Test]
        public void AnIdThatIsNotInTheCatalogueIsRefused()
        {
            PlayerModel player = NewPlayer();

            Assert.That(Buy(player, CosmeticId.FromString("frame.invented")), Is.EqualTo(ActionResults.NoSuchCosmetic));
            Assert.That(Buy(player, null), Is.EqualTo(ActionResults.NoSuchCosmetic));
            Assert.That(player.Wallet.Coins, Is.EqualTo(3_000));
        }

        /// <summary>
        /// The slot an item is equipped in comes from the item's catalogue entry. The action carries only the id,
        /// so a client cannot put an item into another slot.
        /// </summary>
        [Test]
        public void TheSlotComesFromTheCatalogueAndNotFromTheAction()
        {
            PlayerModel player = NewPlayer();

            Buy(player, TestGameConfig.SilverFrame);
            Buy(player, TestGameConfig.AzureName);

            Assert.That(player.Cosmetics.EquippedIn(CosmeticKind.Frame), Is.EqualTo(TestGameConfig.SilverFrame));
            Assert.That(player.Cosmetics.EquippedIn(CosmeticKind.NameEffect), Is.EqualTo(TestGameConfig.AzureName));
            Assert.That(player.Cosmetics.EquippedIn(CosmeticKind.Avatar), Is.EqualTo(CosmeticDefaults.Avatar),
                "buying a frame and a name effect moved the avatar slot");
        }

        #endregion

        #region What a purchase writes to the log

        /// <summary>
        /// A purchase writes one currency sink row and one <see cref="PlayerEventCosmeticPurchased"/> with the same
        /// correlation id, so an analyst can join the price to the item.
        /// </summary>
        [Test]
        public void APurchaseWritesOneSinkRowAndOneFeatureEventUnderOneKey()
        {
            List<PlayerEventBase> events = new List<PlayerEventBase>();
            PlayerModel           player = NewPlayer(events);
            events.Clear();

            Buy(player, TestGameConfig.SapphireFrame);

            List<PlayerEventEconomyTransaction> rows = events.OfType<PlayerEventEconomyTransaction>().ToList();
            Assert.That(rows, Has.Count.EqualTo(1));
            Assert.That(rows[0].Flow, Is.EqualTo(CurrencyFlow.Sink));
            Assert.That(rows[0].Currency, Is.EqualTo(CurrencyType.Coins));
            Assert.That(rows[0].Amount, Is.EqualTo(TestGameConfig.SapphireFramePriceCoins));
            Assert.That(rows[0].Feature, Is.EqualTo(EconomyFeature.Cosmetics));
            Assert.That(rows[0].Reason, Is.EqualTo(EconomyReason.CosmeticPurchase));
            Assert.That(rows[0].ContentId, Is.EqualTo(EconomyContentId.FromString(TestGameConfig.SapphireFrame.Value)));

            PlayerEventCosmeticPurchased purchased = events.OfType<PlayerEventCosmeticPurchased>().Single();
            Assert.That(purchased.Cosmetic, Is.EqualTo(TestGameConfig.SapphireFrame));
            Assert.That(purchased.Slot, Is.EqualTo(CosmeticKind.Frame));
            Assert.That(purchased.Price, Is.EqualTo(TestGameConfig.SapphireFramePriceCoins));
            Assert.That(purchased.BalanceAfter, Is.EqualTo(3_000 - TestGameConfig.SapphireFramePriceCoins));
            Assert.That(purchased.OwnedCount, Is.EqualTo(CosmeticDefaults.All.Count + 1));
            Assert.That(purchased.Correlation, Is.EqualTo(rows[0].Correlation));

            // Buying also equips, so the purchase writes an equip row marked OnPurchase. A new account has a
            // starting item in every slot, so the first purchase replaces the starting frame
            // (docs/cosmetics.md, "The starting three").
            PlayerEventCosmeticEquipped equipped = events.OfType<PlayerEventCosmeticEquipped>().Single();
            Assert.That(equipped.OnPurchase, Is.True);
            Assert.That(equipped.Replaced, Is.EqualTo(CosmeticDefaults.Frame));
        }

        /// <summary>
        /// A purchase refused for insufficient funds writes one <see cref="PlayerEventEconomySpendRejected"/> and
        /// no purchase or equip event.
        /// </summary>
        [Test]
        public void ARefusedPurchaseWritesTheRefusalAndNoFeatureEvent()
        {
            List<PlayerEventBase> events = new List<PlayerEventBase>();
            PlayerModel           player = NewPlayer(events);
            events.Clear();

            Buy(player, TestGameConfig.EmeraldFrame);

            Assert.That(events.OfType<PlayerEventEconomySpendRejected>().Count(), Is.EqualTo(1));
            Assert.That(events.OfType<PlayerEventCosmeticPurchased>(), Is.Empty);
            Assert.That(events.OfType<PlayerEventCosmeticEquipped>(), Is.Empty);
        }

        #endregion

        #region Equipping

        /// <summary>Equipping an owned item changes what is worn and takes nothing from the wallet.</summary>
        [Test]
        public void EquippingSomethingOwnedChangesWhatIsWorn()
        {
            PlayerModel player = NewPlayer();
            Buy(player, TestGameConfig.SilverFrame);
            Buy(player, TestGameConfig.SapphireFrame);

            int coins = player.Wallet.Coins;

            Assert.That(player.Cosmetics.EquippedIn(CosmeticKind.Frame), Is.EqualTo(TestGameConfig.SapphireFrame));
            Assert.That(Equip(player, TestGameConfig.SilverFrame), Is.EqualTo(MetaActionResult.Success));
            Assert.That(player.Cosmetics.EquippedIn(CosmeticKind.Frame), Is.EqualTo(TestGameConfig.SilverFrame));
            Assert.That(player.Wallet.Coins, Is.EqualTo(coins), "equipping is free");
        }

        [Test]
        public void EquippingSomethingUnownedIsRefused()
        {
            PlayerModel player = NewPlayer();

            Assert.That(Equip(player, TestGameConfig.SapphireFrame), Is.EqualTo(ActionResults.CosmeticNotOwned));
            Assert.That(player.Cosmetics.EquippedIn(CosmeticKind.Frame), Is.EqualTo(CosmeticDefaults.Frame));
        }

        /// <summary>Equipping the item already worn is refused and writes no event.</summary>
        [Test]
        public void EquippingWhatIsAlreadyWornIsRefused()
        {
            List<PlayerEventBase> events = new List<PlayerEventBase>();
            PlayerModel           player = NewPlayer(events);
            Buy(player, TestGameConfig.SilverFrame);
            events.Clear();

            Assert.That(Equip(player, TestGameConfig.SilverFrame), Is.EqualTo(ActionResults.CosmeticAlreadyEquipped));
            Assert.That(events, Is.Empty);
        }

        /// <summary>
        /// Every change to the equipped items calls <see cref="IPlayerModelServerListener.OnPublicIdentityChanged"/>,
        /// and a refused equip does not. Without the call, other players' standings keep showing the old identity
        /// snapshot until the season ends.
        /// </summary>
        [Test]
        public void BuyingAndEquippingBothFireTheIdentityChangedHook()
        {
            IdentityWatcher watcher = new IdentityWatcher();
            PlayerModel     player  = NewPlayer(watcher: watcher);

            Buy(player, TestGameConfig.SilverFrame);
            Assert.That(watcher.IdentityChanges, Is.EqualTo(1), "a purchase changes what other people see");

            Buy(player, TestGameConfig.SapphireFrame);
            Equip(player, TestGameConfig.SilverFrame);
            Assert.That(watcher.IdentityChanges, Is.EqualTo(3));

            Equip(player, TestGameConfig.SilverFrame);
            Assert.That(watcher.IdentityChanges, Is.EqualTo(3), "a refused equip changed nothing, so nobody is told");
        }

        #endregion

        #region The public projection

        /// <summary>
        /// <see cref="PlayerModel.BuildPublicIdentity"/> carries the equipped item of every slot, so the Profile
        /// preview and the standings show the same items.
        /// </summary>
        [Test]
        public void ThePublicIdentityCarriesEverySlot()
        {
            PlayerModel player = NewPlayer();

            // A new account wears the starting items, so every slot projects a starting item rather than null.
            PlayerPublicIdentity bare = player.BuildPublicIdentity();
            Assert.That(bare.FrameId, Is.EqualTo(CosmeticDefaults.Frame));
            Assert.That(bare.NameEffectId, Is.EqualTo(CosmeticDefaults.NameEffect));
            Assert.That(bare.AvatarId, Is.EqualTo(CosmeticDefaults.Avatar));

            Buy(player, TestGameConfig.SilverFrame);
            Buy(player, TestGameConfig.AzureName);
            Buy(player, TestGameConfig.ClubAvatar);

            PlayerPublicIdentity dressed = player.BuildPublicIdentity();
            Assert.That(dressed.FrameId, Is.EqualTo(TestGameConfig.SilverFrame));
            Assert.That(dressed.NameEffectId, Is.EqualTo(TestGameConfig.AzureName));
            Assert.That(dressed.AvatarId, Is.EqualTo(TestGameConfig.ClubAvatar));
        }

        /// <summary>
        /// Two identities that differ in one slot compare unequal. A snapshot holder compares identities to decide
        /// whether to redraw, so an equal result after a frame change would leave the old frame on screen.
        /// </summary>
        [Test]
        public void TwoIdentitiesDifferOnceASlotMoves()
        {
            PlayerModel player = NewPlayer();

            PlayerPublicIdentity before = player.BuildPublicIdentity();
            Buy(player, TestGameConfig.SilverFrame);
            PlayerPublicIdentity after = player.BuildPublicIdentity();

            Assert.That(after, Is.Not.EqualTo(before));
            Assert.That(after.GetHashCode(), Is.Not.EqualTo(before.GetHashCode()));
            Assert.That(player.BuildPublicIdentity(), Is.EqualTo(after), "the projection is a pure function of the model");
        }

        /// <summary>
        /// A tournament bot wears a purchasable avatar from the catalogue and no frame. The draw depends only on
        /// the division and the seat, so every read returns the same identity and the bot's standings row does
        /// not change between reads.
        /// </summary>
        [Test]
        public void ABotWearsAnIconFromTheCatalogue()
        {
            SharedGameConfig config = TestGameConfig.Build();

            PlayerPublicIdentity first  = TournamentBots.Identity(config, Group, seat: 3);
            PlayerPublicIdentity second = TournamentBots.Identity(config, Group, seat: 3);

            Assert.That(second, Is.EqualTo(first), "the same division and seat must draw the same dressed identity twice");

            Assert.That(config.Cosmetics.TryGetValue(first.AvatarId, out CosmeticInfo worn), Is.True, "the bot wears an id the catalogue resolves");
            Assert.That(worn.Kind, Is.EqualTo(CosmeticKind.Avatar));
            Assert.That(worn.IsPurchasable, Is.True, "the bot wears what a player could have bought");

            Assert.That(first.FrameId, Is.Null, "no roll awards a frame");
        }

        /// <summary>
        /// When the catalogue has no avatars and no name effects, a bot's identity has every slot null and the
        /// draw does not fail.
        /// </summary>
        [Test]
        public void ABotWithAnEmptyCosmeticCatalogueWearsNothing()
        {
            SharedGameConfig   config       = TestGameConfig.Build();
            List<CosmeticInfo> noDrawnSlots = TestGameConfig.Cosmetics()
                .Where(i => i.Kind != CosmeticKind.Avatar && i.Kind != CosmeticKind.NameEffect).ToList();
            TestGameConfig.SetEntry(config, "Cosmetics", GameConfigLibrary<CosmeticId, CosmeticInfo>.CreateSolo(noDrawnSlots));

            PlayerPublicIdentity bot = TournamentBots.Identity(config, Group, seat: 3);

            Assert.That(bot.AvatarId, Is.Null);
            Assert.That(bot.FrameId, Is.Null);
            Assert.That(bot.NameEffectId, Is.Null);
        }

        /// <summary>
        /// Most bots show plain text, and few wear a basic or premium (gem-priced) name effect, so the standings do
        /// not read as an advertisement. The bounds are checked over many divisions so that one division's draws
        /// cannot pass or fail the test, and as a share of all bots, which is what the standings show.
        /// </summary>
        [Test]
        public void FewBotsWearAPremiumNameEffect()
        {
            SharedGameConfig config = TestGameConfig.Build();

            const int NumDivisions        = 40;
            const int NumSeatsPerDivision = 20;

            int premium = 0;
            int basic   = 0;

            for (int season = 0; season < NumDivisions; season++)
            {
                DivisionIndex division = new DivisionIndex(Group.League, Group.Season + season, Group.Rank, Group.Division);

                for (int seat = 1; seat <= NumSeatsPerDivision; seat++)
                {
                    CosmeticId worn = TournamentBots.Identity(config, division, seat).NameEffectId;
                    if (worn == null)
                        continue;

                    CurrencyType currency = config.Cosmetics[worn].Price.Currency;
                    if (currency == CurrencyType.Gems)
                        premium += 1;
                    else
                        basic += 1;
                }
            }

            int total = NumDivisions * NumSeatsPerDivision;

            Assert.Multiple(() =>
            {
                Assert.That(premium, Is.GreaterThan(0), "no bot drew a premium effect over 800 rolls");
                Assert.That(basic,   Is.GreaterThan(0), "no bot drew a basic effect over 800 rolls");
                Assert.That(premium, Is.LessThanOrEqualTo(6 * total / 100), $"{premium} of {total} bots wear premium, above the 6% bound");
                Assert.That(basic,   Is.LessThanOrEqualTo(16 * total / 100), $"{basic} of {total} bots wear basic, above the 16% bound");
            });
        }

        /// <summary>
        /// <see cref="PlayerPublicIdentity"/> keeps every slot through network serialization. Standings rows are
        /// drawn from the serialized copy in the division's snapshot of the player, so a dropped slot would show
        /// as empty to other players while the buyer's own HUD still shows it.
        /// </summary>
        [Test]
        public void ThePublicIdentityRoundTripsWithEverySlotOn()
        {
            PlayerModel player = NewPlayer();
            Buy(player, TestGameConfig.SilverFrame);
            Buy(player, TestGameConfig.AzureName);
            Buy(player, TestGameConfig.ClubAvatar);

            byte[] bytes = MetaSerialization.SerializeTagged(player.BuildPublicIdentity(), MetaSerializationFlags.SendOverNetwork, logicVersion: null);
            PlayerPublicIdentity restored = MetaSerialization.DeserializeTagged<PlayerPublicIdentity>(bytes, MetaSerializationFlags.SendOverNetwork, resolver: null, logicVersion: null);

            Assert.That(restored, Is.EqualTo(player.BuildPublicIdentity()));
            Assert.That(restored.FrameId, Is.EqualTo(TestGameConfig.SilverFrame));
            Assert.That(restored.NameEffectId, Is.EqualTo(TestGameConfig.AzureName));
            Assert.That(restored.AvatarId, Is.EqualTo(TestGameConfig.ClubAvatar));
        }

        #endregion

        #region The tournament's grant

        static readonly DivisionIndex Group = new DivisionIndex(league: 0, season: 12, rank: 0, division: 0);

        static EntityId DivisionId(int index) =>
            new DivisionIndex(Group.League, Group.Season + index, Group.Rank, Group.Division).ToEntityId();

        /// <summary>Adds a concluded first-place season, with <paramref name="cosmetic"/> as its prize, to the player's tournament history.</summary>
        static void Conclude(PlayerModel player, EntityId divisionId, CosmeticId cosmetic)
        {
            TournamentRewardTableInfo table = TestGameConfig.TournamentRewards();

            ((TournamentClientState)player.PlayerSubClientStates[ClientSlotGame.Tournament]).HistoricalDivisions.Add(
                new TournamentHistoryEntry(
                    divisionId,
                    DivisionIndex.FromEntityId(divisionId),
                    placement: 1,
                    wins: 6,
                    scoredMatches: 10,
                    humanCount: 4,
                    groupSize: TournamentRules.GroupSize,
                    reward: table.BandFor(1)?.Reward,
                    rewardCosmetic: cosmetic,
                    rewardTable: table.Id));
        }

        static PlayerModel Champion(out EntityId divisionId, CosmeticId cosmetic)
        {
            PlayerModel player = NewPlayer();
            player.PlayerSubClientStates[ClientSlotGame.Tournament] = new TournamentClientState();

            divisionId = DivisionId(0);
            Conclude(player, divisionId, cosmetic);
            return player;
        }

        /// <summary>
        /// Claiming the placement adds the season's prize to the wardrobe as owned, unequipped and unacknowledged.
        /// The wardrobe is the only record of ownership, so the wardrobe grid and the standings cannot disagree.
        /// </summary>
        [Test]
        public void WinningASeasonGrantsTheFrameIntoTheWardrobe()
        {
            PlayerModel player = Champion(out EntityId divisionId, TestGameConfig.ChampionFrame);

            Assert.That(new PlayerTournamentPlacementClaim(divisionId).Execute(player, commit: true), Is.EqualTo(MetaActionResult.Success));

            Assert.That(player.Cosmetics.Owns(TestGameConfig.ChampionFrame), Is.True);
            Assert.That(player.Cosmetics.EquippedIn(CosmeticKind.Frame), Is.EqualTo(CosmeticDefaults.Frame),
                "a grant does not dress the player: the slot keeps the plain ring they started in");
            Assert.That(player.Cosmetics.HasUnacknowledged, Is.True);
            Assert.That(player.Tournament.EarnedCosmetics, Has.Count.EqualTo(1), "the tournament still records what the season paid");
        }

        /// <summary>Claiming a second season with the same prize frame does not add a second copy.</summary>
        [Test]
        public void ASecondWinGrantsNoSecondCopy()
        {
            PlayerModel player = Champion(out EntityId first, TestGameConfig.ChampionFrame);
            EntityId    second = DivisionId(1);
            Conclude(player, second, TestGameConfig.ChampionFrame);

            new PlayerTournamentPlacementClaim(first).Execute(player, commit: true);
            new PlayerTournamentPlacementClaim(second).Execute(player, commit: true);

            Assert.That(player.Cosmetics.Owned.Count(id => id == TestGameConfig.ChampionFrame), Is.EqualTo(1));
        }

        /// <summary>A granted prize frame can be equipped like a purchased one.</summary>
        [Test]
        public void AGrantedFrameCanBeEquipped()
        {
            PlayerModel player = Champion(out EntityId divisionId, TestGameConfig.ChampionFrame);
            new PlayerTournamentPlacementClaim(divisionId).Execute(player, commit: true);

            Assert.That(Equip(player, TestGameConfig.ChampionFrame), Is.EqualTo(MetaActionResult.Success));
            Assert.That(player.BuildPublicIdentity().FrameId, Is.EqualTo(TestGameConfig.ChampionFrame));
        }

        #endregion

        #region Acknowledgement

        [Test]
        public void AcknowledgingClearsWhatIsWaitingAndASecondIsRefused()
        {
            PlayerModel player = Champion(out EntityId divisionId, TestGameConfig.ChampionFrame);
            new PlayerTournamentPlacementClaim(divisionId).Execute(player, commit: true);

            Assert.That(Acknowledge(player), Is.EqualTo(MetaActionResult.Success));
            Assert.That(player.Cosmetics.HasUnacknowledged, Is.False);
            Assert.That(Acknowledge(player), Is.EqualTo(ActionResults.NoCosmeticToAcknowledge));
        }

        /// <summary>Acknowledging clears only the unacknowledged list. It changes no balance, owned item or equipped item.</summary>
        [Test]
        public void AcknowledgingMovesNothingElse()
        {
            PlayerModel player = Champion(out EntityId divisionId, TestGameConfig.ChampionFrame);
            new PlayerTournamentPlacementClaim(divisionId).Execute(player, commit: true);

            int  coins = player.Wallet.Coins;
            int  owned = player.Cosmetics.Owned.Count;

            Acknowledge(player);

            Assert.That(player.Wallet.Coins, Is.EqualTo(coins));
            Assert.That(player.Cosmetics.Owned, Has.Count.EqualTo(owned));
            Assert.That(player.Cosmetics.EquippedIn(CosmeticKind.Frame), Is.EqualTo(CosmeticDefaults.Frame));
        }

        /// <summary>A purchased item is not added to the unacknowledged list, because the player bought it on screen.</summary>
        [Test]
        public void APurchaseLeavesNothingWaitingToBeAcknowledged()
        {
            PlayerModel player = NewPlayer();

            Buy(player, TestGameConfig.SilverFrame);

            Assert.That(player.Cosmetics.HasUnacknowledged, Is.False);
            Assert.That(Acknowledge(player), Is.EqualTo(ActionResults.NoCosmeticToAcknowledge));
        }

        #endregion

        #region The starting three

        /// <summary>
        /// A new account owns and wears every item in <see cref="CosmeticDefaults.All"/>. Every slot therefore holds
        /// an owned item, and the player can always equip the starting item again
        /// (<c>docs/cosmetics.md</c>, "The starting three").
        /// </summary>
        [Test]
        public void ANewPlayerOwnsAndWearsTheStartingThree()
        {
            PlayerModel player = NewPlayer();

            Assert.That(player.Cosmetics.Owned, Is.EquivalentTo(CosmeticDefaults.All));
            Assert.That(player.Cosmetics.EquippedIn(CosmeticKind.Avatar), Is.EqualTo(CosmeticDefaults.Avatar));
            Assert.That(player.Cosmetics.EquippedIn(CosmeticKind.Frame), Is.EqualTo(CosmeticDefaults.Frame));
            Assert.That(player.Cosmetics.EquippedIn(CosmeticKind.NameEffect), Is.EqualTo(CosmeticDefaults.NameEffect));
        }

        /// <summary>
        /// After buying and wearing a frame, the player can equip the starting frame again and keeps the purchase.
        /// </summary>
        [Test]
        public void APlayerCanGoBackToWhatTheyStartedIn()
        {
            PlayerModel player = NewPlayer();

            Assert.That(Buy(player, TestGameConfig.SilverFrame), Is.EqualTo(MetaActionResult.Success));
            Assert.That(player.Cosmetics.EquippedIn(CosmeticKind.Frame), Is.EqualTo(TestGameConfig.SilverFrame));

            Assert.That(Equip(player, CosmeticDefaults.Frame), Is.EqualTo(MetaActionResult.Success));

            Assert.That(player.Cosmetics.EquippedIn(CosmeticKind.Frame), Is.EqualTo(CosmeticDefaults.Frame));
            Assert.That(player.Cosmetics.Owns(TestGameConfig.SilverFrame), Is.True, "going back does not give the purchase up");
        }

        /// <summary>The starting items are not purchasable, so buying one is refused and charges nothing.</summary>
        [Test]
        public void TheStartingThreeCannotBeBought()
        {
            PlayerModel player = NewPlayer();
            int         coins  = player.Wallet.Coins;

            // The buy action checks purchasability before ownership, so the refusal is CosmeticNotPurchasable.
            foreach (CosmeticId id in CosmeticDefaults.All)
                Assert.That(Buy(player, id), Is.EqualTo(ActionResults.CosmeticNotPurchasable), $"{id} was offered for sale");

            Assert.That(player.Wallet.Coins, Is.EqualTo(coins));
        }

        /// <summary>
        /// Equipping the starting frame calls <see cref="IPlayerModelServerListener.OnPublicIdentityChanged"/> like
        /// any other equip, so other players' standings stop showing the removed frame.
        /// </summary>
        [Test]
        public void GoingBackToPlainTellsTheSnapshotHolders()
        {
            IdentityWatcher watcher = new IdentityWatcher();
            PlayerModel     player  = NewPlayer(watcher: watcher);

            Buy(player, TestGameConfig.SilverFrame);
            int afterPurchase = watcher.IdentityChanges;

            Equip(player, CosmeticDefaults.Frame);

            Assert.That(watcher.IdentityChanges, Is.EqualTo(afterPurchase + 1));
            Assert.That(player.BuildPublicIdentity().FrameId, Is.EqualTo(CosmeticDefaults.Frame));
        }

        /// <summary>
        /// The migration from schema version 3 puts the starting item into each empty slot and leaves a slot that
        /// holds a purchase unchanged. The client draws an empty slot as the starting item, so filling it changes
        /// nothing on screen.
        /// </summary>
        [Test]
        public void TheMigrationFillsEmptySlotsAndLeavesWornOnesAlone()
        {
            PlayerModel player = NewPlayer();
            Buy(player, TestGameConfig.SilverFrame);

            // Recreate a schema 3 account: the purchased frame is worn and the other slots are empty.
            player.Cosmetics.Equip(CosmeticKind.Avatar, null);
            player.Cosmetics.Equip(CosmeticKind.NameEffect, null);

            Migrate(player, fromVersion: 3);

            Assert.That(player.Cosmetics.EquippedIn(CosmeticKind.Frame), Is.EqualTo(TestGameConfig.SilverFrame),
                "the migration dressed a player who had chosen a frame");
            Assert.That(player.Cosmetics.EquippedIn(CosmeticKind.Avatar), Is.EqualTo(CosmeticDefaults.Avatar));
            Assert.That(player.Cosmetics.EquippedIn(CosmeticKind.NameEffect), Is.EqualTo(CosmeticDefaults.NameEffect));
            Assert.That(player.Cosmetics.HasUnacknowledged, Is.False, "nothing arrived, so nothing is announced");
        }

        #endregion

        #region The schema migration

        static void Migrate(PlayerModel player, int fromVersion) =>
            SchemaMigrationRegistry.Instance.GetSchemaMigrator<PlayerModel>().RunMigrations(player, fromVersion);

        /// <summary>
        /// The migration from schema version 2 adds each prize from the tournament's claimed history to the
        /// wardrobe, unequipped and unacknowledged. The tournament history is not read as ownership, so without the
        /// migration the wardrobe grid would show the prize as locked.
        /// </summary>
        [Test]
        public void TheMigrationPutsAnEarlierSeasonsPrizeIntoTheWardrobe()
        {
            PlayerModel player = NewPlayer();
            player.PlayerSubClientStates[ClientSlotGame.Tournament] = new TournamentClientState();

            // Recreate a schema 2 account: the claimed prize is in the tournament history and not in the wardrobe.
            EntityId divisionId = DivisionId(0);
            Conclude(player, divisionId, TestGameConfig.ChampionFrame);
            player.Tournament.MarkPlacementClaimed(player.TournamentResultOf(divisionId));

            Assert.That(player.Cosmetics.Owns(TestGameConfig.ChampionFrame), Is.False);

            Migrate(player, fromVersion: 2);

            Assert.That(player.Cosmetics.Owns(TestGameConfig.ChampionFrame), Is.True);
            Assert.That(player.Cosmetics.EquippedIn(CosmeticKind.Frame), Is.EqualTo(CosmeticDefaults.Frame),
                "a migration does not dress anybody: the prize is owned and the slot keeps the plain ring");
            Assert.That(player.Cosmetics.HasUnacknowledged, Is.True);
        }

        /// <summary>
        /// Migrating a player with no tournament prizes leaves only the starting items owned and nothing
        /// unacknowledged.
        /// </summary>
        [Test]
        public void TheMigrationLeavesAPlayerWithNoPrizesAlone()
        {
            PlayerModel player = NewPlayer();

            Migrate(player, fromVersion: 2);

            Assert.That(player.Cosmetics.Owned, Is.EquivalentTo(CosmeticDefaults.All));
            Assert.That(player.Cosmetics.HasUnacknowledged, Is.False);
        }

        #endregion

        #region The wardrobe is replicated and checksummed

        /// <summary>The wardrobe survives a serialization round trip, which is how the client receives its copy.</summary>
        [Test]
        public void TheWardrobeRoundTrips()
        {
            PlayerModel player = NewPlayer();
            Buy(player, TestGameConfig.SilverFrame);
            Buy(player, TestGameConfig.AzureName);

            byte[]      bytes   = TestPlayers.Snapshot(player);
            PlayerModel restored = MetaSerialization.DeserializeTagged<PlayerModel>(bytes, MetaSerializationFlags.IncludeAll, resolver: null, logicVersion: null);

            Assert.That(restored.Cosmetics.Owned, Has.Count.EqualTo(CosmeticDefaults.All.Count + 2));
            Assert.That(restored.Cosmetics.EquippedIn(CosmeticKind.Frame), Is.EqualTo(TestGameConfig.SilverFrame));
            Assert.That(restored.Cosmetics.EquippedIn(CosmeticKind.NameEffect), Is.EqualTo(TestGameConfig.AzureName));
        }

        /// <summary>
        /// The wardrobe is included in the checksum serialization. State written by unsynchronized server actions
        /// is excluded, because the client and the server apply those actions on different ticks. Every action that
        /// writes the wardrobe runs at the same timeline position on both sides, so a checksum mismatch there is a
        /// real desync. The test checks the serialized bytes instead of trusting the member's attributes.
        /// </summary>
        [Test]
        public void TheWardrobeIsPartOfTheChecksum()
        {
            PlayerModel player = NewPlayer();

            byte[] before = MetaSerialization.SerializeTagged(player, MetaSerializationFlags.SendOverNetwork | MetaSerializationFlags.ComputeChecksum, logicVersion: null);

            Buy(player, TestGameConfig.SilverFrame);

            byte[] after = MetaSerialization.SerializeTagged(player, MetaSerializationFlags.SendOverNetwork | MetaSerializationFlags.ComputeChecksum, logicVersion: null);

            Assert.That(after, Is.Not.EqualTo(before));
        }

        #endregion

        #region Styles

        /// <summary>
        /// Every style except <see cref="CosmeticStyle.None"/> belongs to one slot that the client draws and has a
        /// CSS token. <c>WebClient.Tests</c> checks that the stylesheet has a rule for each token.
        /// </summary>
        [Test]
        public void EveryStyleHasASlotAndAToken()
        {
            foreach (CosmeticStyle style in System.Enum.GetValues<CosmeticStyle>())
            {
                if (style == CosmeticStyle.None)
                {
                    Assert.That(CosmeticStyles.TokenOf(style), Is.Empty);
                    continue;
                }

                CosmeticKind slot = CosmeticStyles.SlotOf(style);
                Assert.That(slot, Is.Not.EqualTo(CosmeticKind.None), $"{style} belongs to no slot");
                Assert.That(CosmeticStyles.IsShipped(slot), Is.True, $"{style} is in the unrendered {slot} slot");
                Assert.That(CosmeticStyles.TokenOf(style), Is.Not.Empty, $"{style} has no CSS token");
            }
        }

        /// <summary>Every style has its own CSS token, so no two styles look the same.</summary>
        [Test]
        public void NoTwoStylesShareAToken()
        {
            IEnumerable<string> tokens = System.Enum.GetValues<CosmeticStyle>()
                .Where(style => style != CosmeticStyle.None)
                .Select(CosmeticStyles.TokenOf);

            Assert.That(tokens, Is.Unique);
        }

        #endregion
    }
}
