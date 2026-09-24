using Game.Logic;
using Game.Logic.Tests;
using Game.Server.Match;
using Metaplay.Core;
using NUnit.Framework;
using System.Collections.Generic;

namespace Game.Server.Tests
{
    /// <summary>
    /// Tests how the server creates the seats of a new table and how the join window treats them. This is the
    /// seating path the server uses: the matchmaker's <see cref="MatchSetupParams"/>, converted to seats by
    /// <see cref="MatchSeatSetup.ToSeat"/>. These are server-only types, so the shared-code tests cannot use
    /// them and construct their seats by hand instead, in states this path does not produce. This fixture
    /// covers the join window and abandonment on the real path.
    /// </summary>
    [TestFixture]
    public class MatchSeatSetupTests
    {
        static readonly MetaTime     T0         = MatchTestDeals.T0;
        static readonly MetaDuration JoinWindow = MetaDuration.FromSeconds(10);

        static EntityId Player(int seat) => MatchTestDeals.Player(seat);

        /// <summary>A host environment that executes published actions directly on the model, as a real host's timeline would.</summary>
        static TestMatchHost Host(MatchModel model) => new TestMatchHost(model, seed: 99UL);

        static MatchTimings Timings()
        {
            MatchTimings timings = MatchTimings.Instant;
            timings.JoinWindow = JoinWindow;
            return timings;
        }

        /// <summary>Creates a table the same way the matchmaker does, with humans first and bots in the rest.</summary>
        static MatchModel MintedTable(int humanSeats)
        {
            List<MatchSeatSetup> setups = new List<MatchSeatSetup>(MatchRules.NumSeats);
            for (int seat = 0; seat < MatchRules.NumSeats; seat++)
            {
                if (seat < humanSeats)
                    setups.Add(new MatchSeatSetup(PlayerPublicIdentity.ForSeat(Player(seat), $"Player {seat}")));
                else
                    setups.Add(new MatchSeatSetup(PlayerPublicIdentity.ForBot(EntityId.None, $"Bot {seat}", avatarId: null)));
            }

            MatchSetupParams setup = new MatchSetupParams(setups);

            List<MatchSeat> seats = new List<MatchSeat>(MatchRules.NumSeats);
            for (int seat = 0; seat < MatchRules.NumSeats; seat++)
                seats.Add(setup.Seats[seat].ToSeat(seat));

            MatchModel model = new MatchModel();
            MatchHost.SetupNewMatch(model, 7717UL, 4242UL, seats, Timings(), NoPublishedProfiles, T0);
            return model;
        }

        /// <summary>
        /// No bot profiles. The host then gives every bot seat the strongest profile, which is enough for these
        /// seating tests.
        /// </summary>
        static readonly List<BotProfile> NoPublishedProfiles = new List<BotProfile>();

        [Test]
        public void AMintedSeatIsSeatedAsNotYetArrived()
        {
            // A table is created before any client knows which table it is in, so a new human seat must not be
            // marked as connected.
            MatchSeat human = new MatchSeatSetup(PlayerPublicIdentity.ForSeat(Player(0), "Player 0")).ToSeat(0);
            MatchSeat bot   = new MatchSeatSetup(PlayerPublicIdentity.ForBot(EntityId.None, "Bot 3", avatarId: null)).ToSeat(3);

            Assert.That(human.Occupancy, Is.EqualTo(MatchSeatOccupancy.Human));
            Assert.That(human.PlayerId, Is.EqualTo(Player(0)));
            Assert.That(human.IsConnected, Is.False, "a minted seat reported a client that has not connected yet");
            Assert.That(human.HasEverConnected, Is.False, "a minted seat reported a client that has never connected as having arrived");

            Assert.That(bot.Occupancy, Is.EqualTo(MatchSeatOccupancy.Bot));
            Assert.That(bot.HasOwner, Is.False);
        }

        /// <summary>
        /// A new seat keeps the player's equipped cosmetics. The matchmaker builds the seat from the identity the
        /// player's actor returned when it accepted the seat, and <see cref="MatchSeatSetup.ToSeat"/> must pass
        /// that identity on unchanged (<c>docs/cosmetics.md</c>).
        /// </summary>
        [Test]
        public void AMintedSeatCarriesWhatThePlayerIsWearing()
        {
            PlayerPublicIdentity worn = PlayerPublicIdentity.ForSeat(
                Player(0), "Avery",
                avatarId:     CosmeticId.FromString("avatar.jack"),
                frameId:      CosmeticId.FromString("frame.sapphire"),
                nameEffectId: CosmeticId.FromString("name.azure"));

            MatchSeat seated = new MatchSeatSetup(worn).ToSeat(0);

            Assert.That(seated.Identity, Is.EqualTo(worn));
            Assert.That(seated.DisplayName, Is.EqualTo("Avery"));
        }

        [Test]
        public void AMintedTableDoesNotBeginPlayBeforeItsPlayersSubscribe()
        {
            // The actor runs the table once from OnEntityInitialized, before any client has subscribed. If the
            // seats counted as arrived, that first run would close the join window and start play before any
            // client could see it.
            MatchModel model = MintedTable(humanSeats: 2);

            Assert.That(MatchSeatPolicy.ShouldBeginPlay(model, T0), Is.False, "play began on the frame the table was minted");
            Assert.That(MatchSeatPolicy.EveryHumanSeatHasArrived(model), Is.False);

            MatchHost.RunTable(model, new MatchPendingBotMove(), T0, Host(model));
            Assert.That(model.PlayHasBegun, Is.False, "the first run of a minted table began play");
            Assert.That(model.Board.PlayIndex, Is.Zero);

            // One of the two players arrives. The table waits for the other.
            MatchHost.NoteSeatPresent(model, 0, T0, Host(model));
            Assert.That(MatchSeatPolicy.ShouldBeginPlay(model, T0), Is.False, "the table started with one seat still connecting");

            MatchHost.NoteSeatPresent(model, 1, T0, Host(model));
            Assert.That(MatchSeatPolicy.ShouldBeginPlay(model, T0), Is.True, "the table would not start with everybody at it");
        }

        [Test]
        public void AMintedTableNobodyEverSubscribesToIsAbandoned()
        {
            // A table that no player ever joins, for example because the matchmaker failed after creating it,
            // is abandoned when the join window ends instead of being played out by bots.
            MatchModel    model       = MintedTable(humanSeats: 1);
            TestMatchHost environment = Host(model);
            MetaTime      after       = T0 + JoinWindow;

            Assert.That(MatchSeatPolicy.IsAbandonedAtStart(model), Is.True, "a table nobody joined was not abandoned");

            MatchHost.RunTable(model, new MatchPendingBotMove(), after, environment);
            Assert.That(model.Phase, Is.EqualTo(MatchPhase.Abandoned), "the join window ran out with nobody there and the table played on");
            Assert.That(model.Board.PlayIndex, Is.Zero, "an abandoned table played a card");
        }

        [Test]
        public void ASeatThatMissesTheJoinWindowIsCoveredAndTheOthersPlayOn()
        {
            // The join window limits the wait. A player whose client never arrives does not delay the others:
            // their seat is covered by a bot when play begins, and they can still reclaim it by arriving.
            MatchModel    model       = MintedTable(humanSeats: 2);
            TestMatchHost environment = Host(model);

            MatchHost.NoteSeatPresent(model, 0, T0, environment);
            MatchHost.RunTable(model, new MatchPendingBotMove(), T0 + JoinWindow, environment);

            Assert.That(model.PlayHasBegun, Is.True, "the window ran out and the table still had not started");
            Assert.That(model.GetSeat(0).Occupancy, Is.EqualTo(MatchSeatOccupancy.Human));
            Assert.That(model.GetSeat(1).Occupancy, Is.EqualTo(MatchSeatOccupancy.HumanCoveredByBot), "a seat that never arrived was left waiting for a player who is not coming");
            Assert.That(model.GetSeat(1).PlayerId, Is.EqualTo(Player(1)), "the cover lost the owner, so they could never reclaim the seat");
        }
    }
}
