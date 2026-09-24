using NUnit.Framework;
using System;

namespace Game.Logic.Tests
{
    /// <summary>
    /// Tests for <see cref="SeatRotation"/>: the viewer's seat is drawn at South, seat indices increase
    /// clockwise, and a viewer without a seat is shown the table from seat 0.
    /// </summary>
    [TestFixture]
    public class SeatRotationTests
    {
        [Test]
        public void IncreasingSeatIndexRunsClockwiseFromSouth()
        {
            // Clockwise on a clock face with North at twelve: South, West, North, East.
            TableScreenPosition[] expected = new TableScreenPosition[]
            {
                TableScreenPosition.South,
                TableScreenPosition.West,
                TableScreenPosition.North,
                TableScreenPosition.East,
            };

            for (int ownSeat = 0; ownSeat < MatchRules.NumSeats; ownSeat++)
            {
                for (int step = 0; step < MatchRules.NumSeats; step++)
                {
                    int seat = (ownSeat + step) % MatchRules.NumSeats;
                    Assert.That(SeatRotation.ToScreenPosition(seat, ownSeat), Is.EqualTo(expected[step]), $"own seat {ownSeat}, seat {seat}");
                }
            }
        }

        [Test]
        public void RotationRoundTripsFromEveryViewerSeat()
        {
            foreach (int ownSeat in new int[] { SeatRotation.NoSeat, 0, 1, 2, 3 })
            {
                for (int seat = 0; seat < MatchRules.NumSeats; seat++)
                {
                    TableScreenPosition position = SeatRotation.ToScreenPosition(seat, ownSeat);
                    Assert.That(SeatRotation.ToSeat(position, ownSeat), Is.EqualTo(seat), $"own seat {ownSeat}, seat {seat}");
                }

                foreach (TableScreenPosition position in SeatRotation.ScreenPositionsClockwise)
                {
                    int seat = SeatRotation.ToSeat(position, ownSeat);
                    Assert.That(SeatRotation.ToScreenPosition(seat, ownSeat), Is.EqualTo(position), $"own seat {ownSeat}, position {position}");
                }
            }
        }

        [Test]
        public void UnseatedViewerFallsBackToSeatZeroAtSouth()
        {
            Assert.That(SeatRotation.IsSeated(SeatRotation.NoSeat), Is.False);
            Assert.That(SeatRotation.ResolveViewerSeat(SeatRotation.NoSeat), Is.EqualTo(SeatRotation.UnseatedViewerSeat));

            Assert.That(SeatRotation.ToSeat(TableScreenPosition.South, SeatRotation.NoSeat), Is.EqualTo(0));
            Assert.That(SeatRotation.ToScreenPosition(0, SeatRotation.NoSeat), Is.EqualTo(TableScreenPosition.South));
            Assert.That(SeatRotation.ToScreenPosition(1, SeatRotation.NoSeat), Is.EqualTo(TableScreenPosition.West));
            Assert.That(SeatRotation.ToScreenPosition(2, SeatRotation.NoSeat), Is.EqualTo(TableScreenPosition.North));
            Assert.That(SeatRotation.ToScreenPosition(3, SeatRotation.NoSeat), Is.EqualTo(TableScreenPosition.East));
        }

        [Test]
        public void AnOwnSeatOutsideTheTableUsesTheSameFallback()
        {
            foreach (int ownSeat in new int[] { -7, MatchRules.NumSeats, 99 })
            {
                Assert.That(SeatRotation.IsSeated(ownSeat), Is.False, $"own seat {ownSeat}");
                for (int seat = 0; seat < MatchRules.NumSeats; seat++)
                    Assert.That(SeatRotation.ToScreenPosition(seat, ownSeat), Is.EqualTo(SeatRotation.ToScreenPosition(seat, SeatRotation.UnseatedViewerSeat)), $"own seat {ownSeat}, seat {seat}");
            }
        }

        [Test]
        public void SeatedViewerIsRecognised()
        {
            for (int ownSeat = 0; ownSeat < MatchRules.NumSeats; ownSeat++)
            {
                Assert.That(SeatRotation.IsSeated(ownSeat), Is.True, $"own seat {ownSeat}");
                Assert.That(SeatRotation.ResolveViewerSeat(ownSeat), Is.EqualTo(ownSeat));
            }
        }

        [Test]
        public void AnOutOfRangeSeatIsRejected()
        {
            Assert.That(() => SeatRotation.ToScreenPosition(-1, 0), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => SeatRotation.ToScreenPosition(MatchRules.NumSeats, 0), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => SeatRotation.ToSeat((TableScreenPosition)9, 0), Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void ScreenPositionsAreListedClockwiseFromSouth()
        {
            Assert.That(SeatRotation.ScreenPositionsClockwise, Is.EqualTo(new TableScreenPosition[]
            {
                TableScreenPosition.South,
                TableScreenPosition.West,
                TableScreenPosition.North,
                TableScreenPosition.East,
            }));
        }
    }
}
