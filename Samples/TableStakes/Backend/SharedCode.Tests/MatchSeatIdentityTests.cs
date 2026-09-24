using Metaplay.Core;
using Metaplay.Core.Config;
using Metaplay.Core.Model;
using Metaplay.Core.MultiplayerEntity;
using Metaplay.Core.Serialization;
using NUnit.Framework;
using System.Collections.Generic;

namespace Game.Logic.Tests
{
    /// <summary>
    /// Tests the identity each match seat shows to the other players (<c>docs/cosmetics.md</c>, "At the
    /// table"). A seat carries a <see cref="PlayerPublicIdentity"/>. A human's identity comes from their own
    /// player model, and a bot's cosmetics are drawn by the table from the published catalogue.
    /// <para>
    /// The seat's identity is what the client's plaque receives, so these rules are tested here. A render test
    /// can only check that a plaque draws the identity it receives.
    /// </para>
    /// </summary>
    [TestFixture]
    public class MatchSeatIdentityTests
    {
        private const ulong DealSeed  = 0x51E7_0FA1_3C2B_9D04UL;
        private const ulong TableSeed = 0x2B7C_4411_AA09_6E35UL;

        /// <summary>
        /// Deals a table the way a host does: seat 0 is the human with <paramref name="human"/> as its identity,
        /// and the other seats are bots with no cosmetics, which <see cref="MatchHost.SetupNewMatch"/> then draws
        /// for them.
        /// </summary>
        private static MatchModel Deal(SharedGameConfig config, PlayerPublicIdentity human, ulong tableSeed = TableSeed)
        {
            MatchModel model = new MatchModel();
            ((IMultiplayerModel)model).GameConfig = config;
            ((IMultiplayerModel)model).ResetTime(MatchTestDeals.T0);

            List<MatchSeat> seats = new List<MatchSeat>(MatchRules.NumSeats)
            {
                new MatchSeat(0, human, MatchSeatOccupancy.Human, hasArrived: true),
            };
            for (int seat = 1; seat < MatchRules.NumSeats; seat++)
            {
                seats.Add(new MatchSeat(seat, PlayerPublicIdentity.ForBot(EntityId.None, $"Bot {seat}", avatarId: null),
                                        MatchSeatOccupancy.Bot, hasArrived: true));
            }

            MatchHost.SetupNewMatch(model, DealSeed, tableSeed, seats, MatchTimings.Instant,
                                    new List<BotProfile> { BotProfiles.Strongest }, MatchTestDeals.T0);
            return model;
        }

        private static PlayerPublicIdentity DressedPlayer() =>
            PlayerPublicIdentity.ForSeat(MatchTestDeals.Player(0), "Avery",
                                         avatarId:     TestGameConfig.ClubAvatar,
                                         frameId:      TestGameConfig.SapphireFrame,
                                         nameEffectId: TestGameConfig.ShimmerName);

        /// <summary>
        /// Checks that the human seat carries the identity the player's model composed, unchanged. The seat
        /// stores the identity itself rather than copies of its fields, so a plaque and a standings row cannot
        /// disagree about what a player is wearing.
        /// </summary>
        [Test]
        public void AHumanSeatCarriesExactlyWhatThePlayersModelProjected()
        {
            PlayerPublicIdentity worn  = DressedPlayer();
            MatchModel           table = Deal(TestGameConfig.Build(), worn);

            Assert.That(table.GetSeat(0).Identity, Is.EqualTo(worn));
            Assert.That(table.GetSeat(0).PlayerId, Is.EqualTo(MatchTestDeals.Player(0)));
            Assert.That(table.GetSeat(0).DisplayName, Is.EqualTo("Avery"));
        }

        /// <summary>
        /// Checks that the table does not add cosmetics to a seat that has an owner. The table draws cosmetics
        /// only for unowned seats. A rule based on occupancy instead would replace a human's cosmetics when a
        /// bot covers their seat.
        /// </summary>
        [Test]
        public void TheTableDoesNotDressASeatThatHasAnOwner()
        {
            PlayerPublicIdentity bare  = PlayerPublicIdentity.ForSeat(MatchTestDeals.Player(0), "Avery");
            MatchModel           table = Deal(TestGameConfig.Build(), bare);

            Assert.That(table.GetSeat(0).Identity.AvatarId, Is.Null, "a player wearing nothing was dressed by the table");
            Assert.That(table.GetSeat(0).Identity.NameEffectId, Is.Null);
        }

        /// <summary>
        /// Checks that every bot wears a purchasable avatar from the published catalogue, so bots show only
        /// items a player can buy.
        /// </summary>
        [Test]
        public void EveryBotSeatWearsAPurchasableAvatarFromTheCatalogue()
        {
            SharedGameConfig config = TestGameConfig.Build();
            MatchModel       table  = Deal(config, DressedPlayer());

            for (int seat = 1; seat < MatchRules.NumSeats; seat++)
            {
                CosmeticId avatarId = table.GetSeat(seat).Identity.AvatarId;
                Assert.That(avatarId, Is.Not.Null, $"seat {seat} was left undressed");

                CosmeticInfo info = config.Cosmetics[avatarId];
                Assert.That(info.Kind, Is.EqualTo(CosmeticKind.Avatar));
                Assert.That(info.IsPurchasable, Is.True, "a bot is wearing something the shop does not sell");

                // Bots never get a frame. The unpurchasable frame is the season prize, and a bot wearing it
                // would contradict that it is earned only by winning a season.
                Assert.That(table.GetSeat(seat).Identity.FrameId, Is.Null, $"seat {seat} was dealt a frame");
            }
        }

        /// <summary>
        /// Checks that any name effect a bot wears is a purchasable name effect. How often a bot wears one is
        /// tested in <c>CosmeticDrawsTests</c>.
        /// </summary>
        [Test]
        public void ANameEffectABotWearsIsOneTheCatalogueSells()
        {
            SharedGameConfig config = TestGameConfig.Build();

            for (ulong seed = 0; seed < 64; seed++)
            {
                MatchModel table = Deal(config, DressedPlayer(), tableSeed: seed);
                for (int seat = 1; seat < MatchRules.NumSeats; seat++)
                {
                    CosmeticId effectId = table.GetSeat(seat).Identity.NameEffectId;
                    if (effectId == null)
                        continue;

                    CosmeticInfo info = config.Cosmetics[effectId];
                    Assert.That(info.Kind, Is.EqualTo(CosmeticKind.NameEffect));
                    Assert.That(info.IsPurchasable, Is.True);
                }
            }
        }

        /// <summary>
        /// Checks that bot identities depend only on the table seed and the game config, so dealing the same
        /// seed under the same config reproduces the same opponents.
        /// </summary>
        [Test]
        public void TheFacesAreAPureFunctionOfTheTableSeed()
        {
            SharedGameConfig config = TestGameConfig.Build();

            MatchModel first  = Deal(config, DressedPlayer());
            MatchModel second = Deal(config, DressedPlayer());

            for (int seat = 1; seat < MatchRules.NumSeats; seat++)
                Assert.That(second.GetSeat(seat).Identity, Is.EqualTo(first.GetSeat(seat).Identity));
        }

        /// <summary>
        /// Checks that each seat draws from its own random stream, so the bots at one table do not all wear the
        /// same items. A shared stream would make every seat at every seeded table agree, which the loop looks
        /// for in both the avatar and the name-effect slot.
        /// <para>
        /// The fixture catalogue has only one purchasable avatar, which would make every seat agree regardless
        /// of the streams. This test adds a second avatar with <see cref="WithTwoAvatars"/> instead of changing
        /// the shared fixture.
        /// </para>
        /// </summary>
        [Test]
        public void EverySeatAtATableDrawsOnAStreamOfItsOwn()
        {
            SharedGameConfig config = WithTwoAvatars();

            bool avatarsDisagreed = false;
            bool effectsDisagreed = false;

            for (ulong seed = 0; seed < 256 && !(avatarsDisagreed && effectsDisagreed); seed++)
            {
                MatchModel table = Deal(config, DressedPlayer(), tableSeed: seed);

                for (int seat = 2; seat < MatchRules.NumSeats; seat++)
                {
                    PlayerPublicIdentity left  = table.GetSeat(seat - 1).Identity;
                    PlayerPublicIdentity right = table.GetSeat(seat).Identity;

                    avatarsDisagreed |= !Equals(left.AvatarId, right.AvatarId);
                    effectsDisagreed |= !Equals(left.NameEffectId, right.NameEffectId);
                }
            }

            Assert.Multiple(() =>
            {
                Assert.That(avatarsDisagreed, Is.True, "every seat at every seeded table drew the same avatar");
                Assert.That(effectsDisagreed, Is.True, "every seat at every seeded table drew the same name effect");
            });
        }

        /// <summary>
        /// Returns the fixture config with a second purchasable avatar added to the cosmetics catalogue.
        /// </summary>
        private static SharedGameConfig WithTwoAvatars()
        {
            SharedGameConfig config = TestGameConfig.Build();

            List<CosmeticInfo> catalogue = TestGameConfig.Cosmetics();
            catalogue.Add(new CosmeticInfo(
                CosmeticId.FromString("avatar.queen"), CosmeticKind.Avatar, "Queen of Hearts",
                CurrencyAmount.Coins(600), isPurchasable: true, style: CosmeticStyle.AvatarHeart,
                flavour: "A favour, worn openly."));

            TestGameConfig.SetEntry(config, "Cosmetics", GameConfigLibrary<CosmeticId, CosmeticInfo>.CreateSolo(catalogue));
            return config;
        }

        /// <summary>
        /// Checks that a table dealt with no game config seats bots with no cosmetics instead of failing. An
        /// empty cosmetic slot is a valid state, and the client draws its defaults for it.
        /// </summary>
        [Test]
        public void ATableDealtWithNoCatalogueSeatsUndressedOpponents()
        {
            MatchModel table = Deal(config: null, human: DressedPlayer());

            for (int seat = 1; seat < MatchRules.NumSeats; seat++)
            {
                Assert.That(table.GetSeat(seat).Identity.AvatarId, Is.Null);
                Assert.That(table.GetSeat(seat).Identity.NameEffectId, Is.Null);
                Assert.That(table.GetSeat(seat).DisplayName, Is.EqualTo($"Bot {seat}"));
            }
        }

        /// <summary>
        /// Checks that a seat's identity survives network serialization. A slot lost in serialization would
        /// look like an empty slot, which is a valid state, so nothing else would report the loss.
        /// </summary>
        [Test]
        public void TheWornSlotsSurviveTheWire()
        {
            MatchSeat seat = new MatchSeat(2, DressedPlayer(), MatchSeatOccupancy.Human, hasArrived: true);

            byte[]    bytes    = MetaSerialization.SerializeTagged(seat, MetaSerializationFlags.SendOverNetwork, logicVersion: null);
            MatchSeat restored = MetaSerialization.DeserializeTagged<MatchSeat>(bytes, MetaSerializationFlags.SendOverNetwork, resolver: null, logicVersion: null);

            Assert.That(restored.Identity, Is.EqualTo(seat.Identity));
            Assert.That(restored.DisplayName, Is.EqualTo("Avery"));
        }

        /// <summary>
        /// Checks that a seat with a null identity returns empty values instead of throwing. The schema
        /// migration gives every seat an identity, so this state is not expected. If it does occur, it shows as
        /// a blank name rather than a <see cref="System.NullReferenceException"/>.
        /// </summary>
        [Test]
        public void ASeatWithNoProjectionStillAnswersWhoItIs()
        {
            MatchSeat seat = new MatchSeat(1, identity: null, MatchSeatOccupancy.Bot, hasArrived: false);

            Assert.That(seat.PlayerId, Is.EqualTo(EntityId.None));
            Assert.That(seat.DisplayName, Is.Null);
            Assert.That(seat.HasOwner, Is.False);
        }

        /// <summary>
        /// Checks that migrating a schema v1 table, whose seats store the owner and name in the legacy members,
        /// keeps every seat's owner and name (<c>docs/match.md</c>, "Persistence").
        /// <para>
        /// Tagged serialization skips members it does not know, so without the migration a v1 row would
        /// deserialize without error but with no owner on any seat, and the table would accept no moves. The test
        /// runs the registered schema migrator rather than the migration method.
        /// </para>
        /// </summary>
        [Test]
        public void ATablePersistedBeforeTheProjectionKeepsWhoIsSittingAtIt()
        {
            MatchModel table = Deal(TestGameConfig.Build(), DressedPlayer());

            // Recreate the state a v1 row deserializes into: no identity on any seat, and the owner and name in
            // the legacy members.
            for (int seat = 0; seat < MatchRules.NumSeats; seat++)
            {
                MatchSeat pre = table.GetSeat(seat);
                pre.LegacyPlayerId    = seat == 0 ? MatchTestDeals.Player(0) : EntityId.None;
                pre.LegacyDisplayName = seat == 0 ? "Avery" : $"Bot {seat}";
                pre.ReplaceIdentity(null);
            }

            SchemaMigrationRegistry.Instance.GetSchemaMigrator(typeof(MatchModel)).RunMigrations(table, fromVersion: 1);

            Assert.That(table.GetSeat(0).PlayerId, Is.EqualTo(MatchTestDeals.Player(0)), "the human seat lost its owner across the migration");
            Assert.That(table.GetSeat(0).DisplayName, Is.EqualTo("Avery"));
            Assert.That(table.FindSeatOfPlayer(MatchTestDeals.Player(0)), Is.Zero, "the migrated table cannot place the player sitting at it");

            for (int seat = 1; seat < MatchRules.NumSeats; seat++)
            {
                Assert.That(table.GetSeat(seat).DisplayName, Is.EqualTo($"Bot {seat}"));
                Assert.That(table.GetSeat(seat).HasOwner, Is.False);

                // A v1 table recorded no cosmetics, so the migrated seats have none.
                Assert.That(table.GetSeat(seat).Identity.AvatarId, Is.Null);
            }
        }
    }
}
