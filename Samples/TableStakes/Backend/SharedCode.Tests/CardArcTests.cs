using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.Logic.Tests
{
    /// <summary>
    /// Tests <see cref="CardArc"/>: a fanned hand is symmetric around its middle, its cards are tilted a fixed
    /// step apart, they sit on an arc that peaks in the middle, and the fan never opens wider than a maximum
    /// spread. Also tests the deal order.
    /// </summary>
    [TestFixture]
    public class CardArcTests
    {
        [TestCase(0, 1)]
        [TestCase(2, 5)]
        public void TheMiddleCardOfAnOddHandIsUprightAtTheTopOfTheArc(int index, int count)
        {
            CardArcPlacement placement = CardArc.GetPlacement(index, count);
            Assert.That(placement.AngleDegrees, Is.EqualTo(0.0));
            Assert.That(placement.LiftFraction, Is.EqualTo(1.0));
        }

        [Test]
        public void AnEvenHandHasNoUprightCard()
        {
            for (int index = 0; index < 4; index++)
                Assert.That(CardArc.GetPlacement(index, 4).AngleDegrees, Is.Not.EqualTo(0.0), $"card {index} of 4");
        }

        [Test]
        public void TheFanIsSymmetricAroundItsMiddle()
        {
            for (int count = 2; count <= MatchRules.CardsPerSeat; count++)
            {
                for (int index = 0; index < count; index++)
                {
                    CardArcPlacement left  = CardArc.GetPlacement(index, count);
                    CardArcPlacement right = CardArc.GetPlacement(count - 1 - index, count);

                    Assert.That(left.AngleDegrees, Is.EqualTo(-right.AngleDegrees).Within(1e-9), $"card {index} of {count} tilts opposite its mirror");
                    Assert.That(left.LiftFraction, Is.EqualTo(right.LiftFraction).Within(1e-9), $"card {index} of {count} rides as high as its mirror");
                }
            }
        }

        [Test]
        public void NeighbouringCardsAreOneStepApart()
        {
            for (int index = 1; index < 5; index++)
            {
                double gap = CardArc.GetPlacement(index, 5).AngleDegrees - CardArc.GetPlacement(index - 1, 5).AngleDegrees;
                Assert.That(gap, Is.EqualTo(CardArc.DefaultStepDegrees).Within(1e-9), $"between cards {index - 1} and {index}");
            }
        }

        [Test]
        public void TheArcPeaksInTheMiddleAndMeetsTheTableAtBothEnds()
        {
            Assert.That(CardArc.GetPlacement(0, 5).LiftFraction, Is.EqualTo(0.0).Within(1e-9));
            Assert.That(CardArc.GetPlacement(4, 5).LiftFraction, Is.EqualTo(0.0).Within(1e-9));

            // The lift strictly rises from the left end to the middle and stays within 0..1.
            for (int index = 1; index <= 2; index++)
            {
                Assert.That(CardArc.GetPlacement(index, 5).LiftFraction, Is.GreaterThan(CardArc.GetPlacement(index - 1, 5).LiftFraction), $"card {index} rides above card {index - 1}");
                Assert.That(CardArc.GetPlacement(index, 5).LiftFraction, Is.InRange(0.0, 1.0));
            }
        }

        [Test]
        public void AHandTooBigToSpreadAtFullStepClosesUpInsteadOfOpeningWider()
        {
            // At the default step this many cards would exceed the maximum spread, so the step shrinks and the
            // fan opens to exactly the maximum.
            const int count = 15;
            double    spread = CardArc.GetPlacement(count - 1, count).AngleDegrees - CardArc.GetPlacement(0, count).AngleDegrees;
            Assert.That(spread, Is.EqualTo(CardArc.DefaultMaxSpreadDegrees).Within(1e-9));

            // A full hand in this game fits within the maximum spread, so it fans at the default step.
            double dealt = CardArc.GetPlacement(MatchRules.CardsPerSeat - 1, MatchRules.CardsPerSeat).AngleDegrees
                         - CardArc.GetPlacement(0, MatchRules.CardsPerSeat).AngleDegrees;
            Assert.That(dealt, Is.EqualTo((MatchRules.CardsPerSeat - 1) * CardArc.DefaultStepDegrees).Within(1e-9));
            Assert.That(dealt, Is.LessThan(CardArc.DefaultMaxSpreadDegrees));
        }

        [Test]
        public void NoFanEverOpensPastTheCap()
        {
            for (int count = 2; count <= 30; count++)
            {
                double spread = CardArc.GetPlacement(count - 1, count).AngleDegrees - CardArc.GetPlacement(0, count).AngleDegrees;
                Assert.That(spread, Is.LessThanOrEqualTo(CardArc.DefaultMaxSpreadDegrees + 1e-9), $"a fan of {count}");
            }
        }

        [Test]
        public void TheDealGoesRoundTheTableOneCardAtATime()
        {
            // Each dealt card gets a distinct deal position, and the positions are exactly 0 to NumPlays - 1.
            HashSet<int> places = new HashSet<int>();
            for (int cardIndex = 0; cardIndex < MatchRules.CardsPerSeat; cardIndex++)
            {
                foreach (TableScreenPosition position in SeatRotation.ScreenPositionsClockwise)
                    Assert.That(places.Add(CardArc.GetDealOrder(cardIndex, position)), Is.True, $"card {cardIndex} to {position}");
            }
            Assert.That(places, Is.EquivalentTo(Enumerable.Range(0, MatchRules.NumPlays)));
        }

        [Test]
        public void TheViewerIsDealtToFirstAndTheTableClockwiseAfterThem()
        {
            Assert.That(CardArc.GetDealOrder(0, TableScreenPosition.South), Is.EqualTo(0));

            // The first round goes clockwise, the same order as play.
            int previous = -1;
            foreach (TableScreenPosition position in SeatRotation.ScreenPositionsClockwise)
            {
                int place = CardArc.GetDealOrder(0, position);
                Assert.That(place, Is.GreaterThan(previous), $"{position} is dealt to after the seat before it");
                previous = place;
            }

            // Every seat gets its first card before any seat gets a second.
            Assert.That(CardArc.GetDealOrder(1, TableScreenPosition.South), Is.GreaterThan(previous));
        }

        [Test]
        public void EachSeatsCardsAreDealtInOrder()
        {
            foreach (TableScreenPosition position in SeatRotation.ScreenPositionsClockwise)
            {
                for (int cardIndex = 1; cardIndex < MatchRules.CardsPerSeat; cardIndex++)
                {
                    Assert.That(
                        CardArc.GetDealOrder(cardIndex, position),
                        Is.GreaterThan(CardArc.GetDealOrder(cardIndex - 1, position)),
                        $"{position}'s card {cardIndex}");
                }
            }
        }

        [TestCase(0, 0)]
        [TestCase(-1, 5)]
        [TestCase(5, 5)]
        [TestCase(0, -1)]
        public void ACardOutsideTheHandIsAMistakeRatherThanAPlacement(int index, int count)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => CardArc.GetPlacement(index, count));
        }

        [Test]
        public void ANegativeCardIndexHasNoDealOrder()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => CardArc.GetDealOrder(-1, TableScreenPosition.South));
        }
    }
}
