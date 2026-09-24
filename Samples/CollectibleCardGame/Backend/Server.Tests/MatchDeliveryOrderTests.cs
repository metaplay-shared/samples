using Game.Logic;
using Game.Server.Match;
using Metaplay.Core;
using NUnit.Framework;
using System.Collections.Generic;

namespace Game.Server.Tests
{
    /// <summary>
    /// Which seat is told the result first. A crash between the two deliveries should err downward: a rank
    /// lost and not gained is floored, one gained and not lost is minted from nothing.
    /// </summary>
    [TestFixture]
    public class MatchDeliveryOrderTests
    {
        static readonly MetaTime Now = MetaTime.FromMillisecondsSinceEpoch(1_700_000_000_000);

        static MatchOutcomeRecord Record(MatchOutcome outcome, int winnerSeat)
            => new MatchOutcomeRecord(outcome, winnerSeat, finalTurn: 14, MatchEndCause.DenAtZero,
                                      new List<bool> { true, true }, wasRanked: true, decidedAt: Now);

        [Test]
        public void TheLoserIsToldFirst()
        {
            Assert.That(MatchActor.DeliveryOrder(Record(MatchOutcome.Seat0Wins, winnerSeat: 0)), Is.EqualTo(new[] { 1, 0 }));
            Assert.That(MatchActor.DeliveryOrder(Record(MatchOutcome.Seat1Wins, winnerSeat: 1)), Is.EqualTo(new[] { 0, 1 }));
        }

        [Test]
        public void ADrawOrAResultWithNoWinnerSeatTakesSeatOrder()
        {
            Assert.That(MatchActor.DeliveryOrder(Record(MatchOutcome.Draw, winnerSeat: MatchSeats.None)), Is.EqualTo(new[] { 0, 1 }));
            Assert.That(MatchActor.DeliveryOrder(Record(MatchOutcome.Seat0Wins, winnerSeat: 7)), Is.EqualTo(new[] { 0, 1 }));
            Assert.That(MatchActor.DeliveryOrder(null), Is.EqualTo(new[] { 0, 1 }));
        }
    }
}
