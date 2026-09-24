namespace Game.Client.Components.Board;

/// <summary>
/// Assigns an ordered battlefield to the six visual sockets around the Den.
/// </summary>
public static class BoardSlotLayout
{
    public const int Capacity = 6;

    static readonly string[][] SlotsByCount =
    [
        [],
        ["left-near"],
        ["left-near", "right-near"],
        ["left-far", "left-near", "right-near"],
        ["left-far", "left-near", "right-near", "right-far"],
        ["left-outer", "left-far", "left-near", "right-near", "right-far"],
        ["left-outer", "left-far", "left-near", "right-near", "right-far", "right-outer"],
    ];

    public static string SlotFor(int cardCount, int cardIndex)
    {
        if (cardCount < 0 || cardCount > Capacity)
            throw new ArgumentOutOfRangeException(nameof(cardCount));
        if (cardIndex < 0 || cardIndex >= cardCount)
            throw new ArgumentOutOfRangeException(nameof(cardIndex));

        return SlotsByCount[cardCount][cardIndex];
    }
}
