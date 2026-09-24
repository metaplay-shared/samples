using Game.Logic;
using Game.Server.Match;
using Game.Server.Matchmaking;
using Metaplay.Core;
using Metaplay.Core.Message;
using Metaplay.Core.Serialization;
using NUnit.Framework;
using System;
using System.Collections.Generic;

namespace Game.Server.Tests
{
    /// <summary>
    /// The ticket crosses one entity boundary, inside the enqueue cast. A member left without a
    /// <c>[MetaMember]</c> would arrive at its default, which reads as an ordinary value: nought ranked
    /// matches played, or a shield nobody waived.
    /// </summary>
    [TestFixture]
    public class MatchmakingTicketTests
    {
        [Test]
        public void TheEnqueueCastCarriesEveryFactTheTicketHolds()
        {
            MatchmakingTicket queued = FullyPopulated();

            MetaMessage       wire    = MetaSerialization.CloneTagged<MetaMessage>(new InternalMatchmakingEnqueue(queued), MetaSerializationFlags.IncludeAll, logicVersion: null, resolver: null);
            MatchmakingTicket carried = ((InternalMatchmakingEnqueue)wire).Ticket;

            Assert.That(carried.PlayerId, Is.EqualTo(queued.PlayerId));
            Assert.That(carried.Seat.DisplayName, Is.EqualTo(queued.Seat.DisplayName));
            Assert.That(carried.Seat.Occupancy, Is.EqualTo(queued.Seat.Occupancy));
            Assert.That(carried.Seat.Deck, Is.EqualTo(queued.Seat.Deck));
            Assert.That(carried.Seat.Ranks, Is.EqualTo(queued.Seat.Ranks));
            Assert.That(carried.Seat.LockedCards, Is.EqualTo(queued.Seat.LockedCards));
            Assert.That(carried.Rating, Is.EqualTo(queued.Rating));
            Assert.That(carried.PowerScore, Is.EqualTo(queued.PowerScore));
            Assert.That(carried.RankedMatchesPlayed, Is.EqualTo(queued.RankedMatchesPlayed));
            Assert.That(carried.NewcomerShieldWaived, Is.EqualTo(queued.NewcomerShieldWaived));
            Assert.That(carried.DeckChoice.StarterDeckId, Is.EqualTo(queued.DeckChoice.StarterDeckId));
            Assert.That(carried.ArrivedAt, Is.EqualTo(queued.ArrivedAt));
        }

        /// <summary> A ticket with nothing at its default, so a dropped member shows up as one. </summary>
        static MatchmakingTicket FullyPopulated()
        {
            MatchSeatSetup seat = new MatchSeatSetup(
                EntityId.Create(EntityKindCore.Player, 12345), "Pipwhistle", SeatOccupancy.Human, BotProfileId.Strongest,
                new List<CardId> { CardId.FromString("MeadowMouse") },
                new MetaDictionary<CardId, int> { { CardId.FromString("MeadowMouse"), 2 } },
                new List<CardId> { CardId.FromString("GardenSnail") },
                rating: 1234);

            return new MatchmakingTicket(
                seat,
                powerScore:           77,
                rankedMatchesPlayed:  3,
                newcomerShieldWaived: true,
                DeckChoice.Starter(StarterDeckId.FromString("FireAndFoam")),
                MetaTime.FromDateTime(new DateTime(2026, 9, 9, 12, 0, 0, DateTimeKind.Utc)));
        }
    }
}
