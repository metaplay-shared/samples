using Game.Client.Components.Board;

namespace Game.Client.Tests;

/// <summary>
/// The hand fan's geometry. A pure function of how many cards are in the hand, which is what makes it
/// checkable at all — the alternative is a layout nobody can verify without looking at it
/// (<c>Docs/client.md</c>, "Testability").
/// </summary>
[TestFixture]
public class HandFanTests
{
    [Test]
    public void AnEmptyHandFansNothing()
    {
        Assert.That(HandFanGeometry.Layout(0), Is.Empty);
        Assert.That(HandFanGeometry.Layout(-1), Is.Empty);
    }

    [Test]
    public void AHandOfOneIsUprightAndCentred()
    {
        List<HandCardPlacement> fan = HandFanGeometry.Layout(1);

        Assert.That(fan.Count, Is.EqualTo(1));
        Assert.That(fan[0].OffsetX, Is.EqualTo(0.0));
        Assert.That(fan[0].RotationDeg, Is.EqualTo(0.0));
    }

    [Test]
    public void EverySizeUpToTheHandCapLaysOutSymmetrically()
    {
        // The hand cap is nine, so nine is the widest fan the board ever draws.
        for (int count = 2; count <= 9; count++)
        {
            List<HandCardPlacement> fan = HandFanGeometry.Layout(count);

            Assert.That(fan.Count, Is.EqualTo(count), $"a hand of {count}");

            for (int ndx = 0; ndx < count; ndx++)
            {
                HandCardPlacement left  = fan[ndx];
                HandCardPlacement right = fan[count - 1 - ndx];

                Assert.That(left.OffsetX, Is.EqualTo(-right.OffsetX).Within(1e-9), $"a hand of {count} is not symmetric");
                Assert.That(left.RotationDeg, Is.EqualTo(-right.RotationDeg).Within(1e-9), $"a hand of {count} does not tilt symmetrically");
                Assert.That(left.OffsetY, Is.EqualTo(right.OffsetY).Within(1e-9), $"a hand of {count} does not rise symmetrically");
            }
        }
    }

    [Test]
    public void CardsGoLeftToRightInHandOrder()
    {
        // The fan is drawn in hand order, and nothing the client sends names a position in it — but a fan
        // that reordered the cards under the player's finger would read as a rendering bug.
        for (int count = 2; count <= 9; count++)
        {
            List<HandCardPlacement> fan = HandFanGeometry.Layout(count);

            for (int ndx = 1; ndx < count; ndx++)
                Assert.That(fan[ndx].OffsetX, Is.GreaterThan(fan[ndx - 1].OffsetX), $"a hand of {count} at index {ndx}");
        }
    }

    [Test]
    public void TheMiddleOfTheFanRidesHighest()
    {
        // What makes the fan read as held rather than laid out.
        for (int count = 3; count <= 9; count++)
        {
            List<HandCardPlacement> fan = HandFanGeometry.Layout(count);

            double middle = fan[count / 2].OffsetY;
            Assert.That(middle, Is.GreaterThan(fan[0].OffsetY), $"a hand of {count}");
            Assert.That(middle, Is.GreaterThan(fan[count - 1].OffsetY), $"a hand of {count}");
        }
    }

    [Test]
    public void TheFanCrowdsRatherThanOutgrowingTheBoard()
    {
        // Past the point where an even spread would run off the board's own rectangle, the cards overlap more
        // instead. A fan anchored to the viewport would put the outermost card out in the desk furniture.
        for (int count = 2; count <= 9; count++)
        {
            List<HandCardPlacement> fan = HandFanGeometry.Layout(count);
            double width = fan[count - 1].OffsetX - fan[0].OffsetX;

            Assert.That(width, Is.LessThanOrEqualTo(HandFanGeometry.MaxWidth + 1e-9), $"a hand of {count} is {width} wide");
        }
    }

    [Test]
    public void TheOutermostCardsTiltNoFurtherThanTheStatedMaximum()
    {
        for (int count = 2; count <= 9; count++)
        {
            foreach (HandCardPlacement place in HandFanGeometry.Layout(count))
                Assert.That(Math.Abs(place.RotationDeg), Is.LessThanOrEqualTo(HandFanGeometry.MaxRotationDeg + 1e-9));
        }
    }
}
