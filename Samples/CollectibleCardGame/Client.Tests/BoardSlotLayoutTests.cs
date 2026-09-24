using Game.Client.Components.Board;

namespace Game.Client.Tests;

public sealed class BoardSlotLayoutTests
{
    [Test]
    public void SixCardsFillAllSocketsInBoardOrder()
    {
        string[] expected = ["left-outer", "left-far", "left-near", "right-near", "right-far", "right-outer"];

        string[] actual = Enumerable.Range(0, 6).Select(index => BoardSlotLayout.SlotFor(6, index)).ToArray();

        Assert.That(actual, Is.EqualTo(expected));
    }

    [TestCase(1, new[] { "left-near" })]
    [TestCase(2, new[] { "left-near", "right-near" })]
    [TestCase(3, new[] { "left-far", "left-near", "right-near" })]
    [TestCase(4, new[] { "left-far", "left-near", "right-near", "right-far" })]
    [TestCase(5, new[] { "left-outer", "left-far", "left-near", "right-near", "right-far" })]
    public void PartialRowsStayPackedAroundDen(int cardCount, string[] expected)
    {
        string[] actual = Enumerable.Range(0, cardCount)
            .Select(index => BoardSlotLayout.SlotFor(cardCount, index))
            .ToArray();

        Assert.That(actual, Is.EqualTo(expected));
    }

    [Test]
    public void RemovingNearDenCardMovesFurtherCardIntoItsSocket()
    {
        string before = BoardSlotLayout.SlotFor(3, 0);
        string after = BoardSlotLayout.SlotFor(2, 0);

        Assert.That(before, Is.EqualTo("left-far"));
        Assert.That(after, Is.EqualTo("left-near"));
    }

    [TestCase(-1, 0)]
    [TestCase(7, 0)]
    [TestCase(2, -1)]
    [TestCase(2, 2)]
    public void InvalidCoordinatesAreRejected(int cardCount, int cardIndex)
    {
        Assert.That(() => BoardSlotLayout.SlotFor(cardCount, cardIndex), Throws.TypeOf<ArgumentOutOfRangeException>());
    }
}
