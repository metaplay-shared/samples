namespace WebClient.Meta;

/// <summary>
/// Timing for the letter-by-letter title animation used by the reward reveal and the results overlay headlines.
/// Each letter starts <see cref="StepMs"/> after the previous one. The animation must be short because the
/// controls under the title are usable from the first frame.
/// <para>
/// The step shrinks as the title gets longer, so the total animation length has the same upper bound for any
/// title.
/// </para>
/// </summary>
public static class RevealCascade
{
    /// <summary>How long one letter's own rise takes, in milliseconds.</summary>
    public const int LetterRiseMs = 160;

    /// <summary>
    /// The delay in milliseconds between the starts of consecutive letters, for a title of
    /// <paramref name="letterCount"/> letters. The division bounds the last letter's start delay, which is the
    /// step multiplied by <c>letterCount - 1</c>. The <c>Math.Min</c> cap keeps short titles from animating
    /// slowly.
    /// </summary>
    public static int StepMs(int letterCount) => Math.Min(18, 140 / Math.Max(1, letterCount - 1));

    /// <summary>
    /// The title's words, each with the animation index of its first letter, so letter indexes continue across
    /// words. Consecutive spaces produce no empty words.
    /// </summary>
    public static IReadOnlyList<(string Word, int FirstLetter)> WordsOf(string title)
    {
        List<(string Word, int FirstLetter)> words = new List<(string Word, int FirstLetter)>();
        int letter = 0;
        foreach (string word in title.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            words.Add((word, letter));
            letter += word.Length;
        }
        return words;
    }

    /// <summary>The number of animated letters: the sum of the word lengths, excluding spaces.</summary>
    public static int LetterCount(string title)
    {
        int count = 0;
        foreach ((string word, int _) in WordsOf(title))
            count += word.Length;
        return count;
    }
}
