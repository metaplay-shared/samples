namespace WebClient.Meta;

/// <summary>
/// One confetti piece in the results screen celebration: its start position, fall and look. All values are
/// computed in C# and passed to the stylesheet as CSS custom properties, so tests can check their ranges. The
/// stylesheet only animates transform and opacity.
/// </summary>
public readonly record struct ConfettiPiece(
    int Index,
    double X,
    int DelayMs,
    int FallMs,
    double DriftRem,
    double Spin,
    int ColorNdx,
    double Scale);

/// <summary>
/// Computes the confetti pieces for a finished game. Values are hashed from the seed and the piece index instead
/// of drawn from <c>Random</c>, so the same game produces the same confetti on every render and every machine.
/// <para>
/// Each piece animates once with CSS and does not loop, so the celebration ends after the longest delay plus
/// the longest fall.
/// </para>
/// </summary>
public static class ConfettiPlan
{
    /// <summary>The number of confetti pieces per game.</summary>
    public const int Count = 48;

    /// <summary>The pieces for one game. Callers pass the match's entity id as the seed.</summary>
    public static IReadOnlyList<ConfettiPiece> For(ulong seed)
    {
        List<ConfettiPiece> pieces = new List<ConfettiPiece>(Count);
        for (int index = 0; index < Count; index++)
        {
            pieces.Add(new ConfettiPiece(
                Index: index,
                X: SeededValueBetween(seed, index, 1, 2.0, 98.0),
                DelayMs: (int)SeededValueBetween(seed, index, 2, 0.0, 500.0),
                FallMs: (int)SeededValueBetween(seed, index, 3, 1800.0, 2600.0),
                DriftRem: SeededValueBetween(seed, index, 4, -4.0, 4.0),
                Spin: (int)SeededValueBetween(seed, index, 5, -720.0, 720.0),
                ColorNdx: (int)SeededValueBetween(seed, index, 6, 0.0, 4.999999),
                Scale: SeededValueBetween(seed, index, 7, 0.7, 1.3)));
        }

        return pieces;
    }

    /// <summary>
    /// A pseudo-random value in [<paramref name="from"/>, <paramref name="to"/>) hashed from the seed, piece index and
    /// salt. A shared <c>Random</c> would give a piece a different value depending on how many values were drawn
    /// before it.
    /// </summary>
    private static double SeededValueBetween(ulong seed, int index, int salt, double from, double to)
    {
        unchecked
        {
            uint seedPart = (uint)(seed ^ (seed >> 32));
            uint hash = seedPart * 374_761_393 + (uint)index * 668_265_263 + (uint)salt * 2_246_822_519;
            hash = (hash ^ (hash >> 13)) * 1_274_126_177u;
            hash ^= hash >> 16;
            return from + (to - from) * ((hash & 0xFFFFFF) / (double)0x1000000);
        }
    }
}
