using Game.Logic;
using Game.Server.Match;
using Game.Server.Matchmaking;
using Metaplay.Cloud.Entity;
using Metaplay.Core;
using NUnit.Framework;
using System;
using System.Collections.Generic;

namespace Game.Server.Tests
{
    /// <summary>
    /// The freeze that turns an account into a seat and a queue entry. The actor around it is not reachable
    /// from a test, so this is where a fact the model carries is pinned as reaching the queue.
    /// </summary>
    [TestFixture]
    public class MatchmakingTicketPolicyTests
    {
        static readonly EntityId Player = EntityId.Create(EntityKindCore.Player, 12345);
        static readonly MetaTime Now    = MetaTime.FromDateTime(new DateTime(2026, 9, 9, 12, 0, 0, DateTimeKind.Utc));

        /// <summary> The choice the entry named. One instance, so the freeze can be pinned as carrying it. </summary>
        static readonly DeckChoice DeckFour = DeckChoice.Saved(4);

        [Test]
        public void TheFreezeCarriesTheAccountFactsTheTierReads()
        {
            // The two facts the stakes tier is decided from, and the waiver is the one with no other route in:
            // the count is at least visible in the record, while a waiver that stopped being read would look
            // exactly like a dashboard control that does nothing.
            MatchmakingTicket ticket = MatchmakingTicketPolicy.Freeze(
                Account(rankedMatchesPlayed: 3, shieldWaived: true), DeckFour, Seat(), powerScore: 77, Now);

            Assert.That(ticket.RankedMatchesPlayed, Is.EqualTo(3));
            Assert.That(ticket.NewcomerShieldWaived, Is.True);

            // And it is the account's own answer rather than a constant: an unwaived account freezes unwaived.
            MatchmakingTicket unwaived = MatchmakingTicketPolicy.Freeze(
                Account(rankedMatchesPlayed: 0, shieldWaived: false), DeckFour, Seat(), powerScore: 77, Now);

            Assert.That(unwaived.NewcomerShieldWaived, Is.False);
        }

        [Test]
        public void TheFrozenTicketIsShieldedExactlyWhenTheAccountStillIs()
        {
            // The whole chain in one place: model → ticket → the tier's own per-seat question. A break
            // anywhere along it shows up here as a shield that outlived its waiver, or one that vanished.
            GlobalConfig global = TestGameConfig.Shared.Global;
            int          shield = global.NewcomerShieldMatches;

            Assert.That(SeatOf(rankedMatchesPlayed: 0, shieldWaived: false).IsShielded(global), Is.True, "a newcomer");
            Assert.That(SeatOf(rankedMatchesPlayed: 0, shieldWaived: true).IsShielded(global), Is.False, "a waived newcomer");
            Assert.That(SeatOf(rankedMatchesPlayed: shield, shieldWaived: false).IsShielded(global), Is.False, "past the shield");
            Assert.That(SeatOf(rankedMatchesPlayed: shield, shieldWaived: true).IsShielded(global), Is.False, "and a waiver there changes nothing");
        }

        [Test]
        public void TheFreezeCarriesTheSeatAndTheQueueFactsUnchanged()
        {
            MatchSeatSetup    seat   = Seat();
            MatchmakingTicket ticket = MatchmakingTicketPolicy.Freeze(
                Account(rankedMatchesPlayed: 3, shieldWaived: true), DeckFour, seat, powerScore: 77, Now);

            Assert.That(ticket.Seat, Is.SameAs(seat));
            Assert.That(ticket.PlayerId, Is.EqualTo(Player));
            Assert.That(ticket.Rating, Is.EqualTo(1234));
            Assert.That(ticket.PowerScore, Is.EqualTo(77));
            Assert.That(ticket.DeckChoice, Is.SameAs(DeckFour));
            Assert.That(ticket.ArrivedAt, Is.EqualTo(Now));
        }

        // ---------------------------------------------------------------- the deck freeze

        [Test]
        public void TheFreezeReadsThisAccountsOwnRanksForAStarterDecksCards()
        {
            // A starter deck is a card list and nothing more, so what it is seated at is whatever the account
            // holds — which is what keeps a veteran's Power Score honest when they pick one.
            SharedGameConfig config    = TestGameConfig.Shared;
            List<CardId>     deck      = config.StarterDecks[StarterDeckId.FromString("FireAndFoam")].ToCardIds();
            PlayerModel      account   = AccountOwning(deck, config.Global.RankMin);

            account.Collection[CardId.FromString("EmberKit")] = 3;

            (MatchSeatSetup seat, int powerScore) = MatchmakingTicketPolicy.FreezeSeat(account, Player, deck, config);

            Assert.That(seat.PlayerId, Is.EqualTo(Player));
            Assert.That(seat.DisplayName, Is.EqualTo("Pipwhistle"));
            Assert.That(seat.Occupancy, Is.EqualTo(SeatOccupancy.Human));
            Assert.That(seat.Deck, Is.EqualTo(deck));
            Assert.That(seat.Ranks[CardId.FromString("EmberKit")], Is.EqualTo(3));
            Assert.That(seat.Ranks[CardId.FromString("Foxfire")], Is.EqualTo(config.Global.RankMin));
            Assert.That(powerScore, Is.EqualTo(config.Global.DeckSize - 1 + 3), "24 cards at the floor and one at rank 3");
            Assert.That(seat.Rating, Is.EqualTo(1234));
        }

        [Test]
        public void TheFreezeCarriesEveryPadlockTheAccountHeld()
        {
            // The padlock set is the queue's other freeze, and it is the account's whole set rather than the
            // part of it inside the deck: a padlock is open information about the account, and the pre-match
            // screen states the count for both seats. A padlock toggled after pairing must not be able to
            // withdraw a card the screen had already shown as at risk.
            SharedGameConfig config  = TestGameConfig.Shared;
            List<CardId>     deck    = config.StarterDecks[StarterDeckId.FromString("FireAndFoam")].ToCardIds();
            PlayerModel      account = AccountOwning(deck, config.Global.RankMin);

            account.LockSlots[0] = CardId.FromString("EmberKit");     // inside the starter deck
            account.LockSlots[1] = null;                              // an empty slot contributes nothing
            account.LockSlots[2] = CardId.FromString("OldMossback");   // outside it, and still frozen

            (MatchSeatSetup seat, int _) = MatchmakingTicketPolicy.FreezeSeat(account, Player, deck, config);

            Assert.That(seat.LockedCards, Is.EqualTo(new List<CardId>
            {
                CardId.FromString("EmberKit"),
                CardId.FromString("OldMossback"),
            }));

            // And an account with nothing locked freezes an empty set rather than null, which is what the
            // stakes pairing and the reveal both index into.
            (MatchSeatSetup unlocked, int _) = MatchmakingTicketPolicy.FreezeSeat(AccountOwning(deck, config.Global.RankMin), Player, deck, config);
            Assert.That(unlocked.LockedCards, Is.Empty);
        }

        /// <summary> An account that owns every card of <paramref name="deck"/> at one rank. </summary>
        static PlayerModel AccountOwning(List<CardId> deck, int rank)
        {
            PlayerModel model = Account(rankedMatchesPlayed: 0, shieldWaived: false);
            foreach (CardId cardId in deck)
                model.Collection[cardId] = rank;

            model.Collection[CardId.FromString("OldMossback")] = rank;
            return model;
        }

        /// <summary> The seat facts a frozen ticket produces, which is the path the formation actually takes. </summary>
        static StakesSeatFacts SeatOf(int rankedMatchesPlayed, bool shieldWaived)
            => StakesSeatFacts.OfTicket(MatchmakingTicketPolicy.Freeze(
                Account(rankedMatchesPlayed, shieldWaived), DeckFour, Seat(), powerScore: 77, Now));

        /// <summary>
        /// An account holding only the facts the freeze reads. Built by hand rather than through
        /// <c>PlayerModelUtil</c>: the freeze is a pure read of four members, and a model that went through
        /// the starter grant would make the fixture about the grant instead.
        /// </summary>
        static PlayerModel Account(int rankedMatchesPlayed, bool shieldWaived)
        {
            PlayerModel model = new PlayerModel
            {
                DisplayName          = "Pipwhistle",
                NewcomerShieldWaived = shieldWaived,
            };

            model.Record.Rating              = 1234;
            model.Record.RankedMatchesPlayed = rankedMatchesPlayed;
            return model;
        }

        static MatchSeatSetup Seat()
            => new MatchSeatSetup(
                Player, "Pipwhistle", SeatOccupancy.Human, BotProfileId.Strongest,
                new List<CardId> { CardId.FromString("MeadowMouse") },
                new MetaDictionary<CardId, int> { { CardId.FromString("MeadowMouse"), 2 } },
                new List<CardId> { CardId.FromString("GardenSnail") },
                rating: 1234);
    }
}
