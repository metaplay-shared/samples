using WebClient.Meta;

namespace WebClient.Tests;

/// <summary>
/// The timing of <see cref="RevealCascade"/>. The reveal's claim button works from the first frame, so the letter
/// animation must finish quickly for any title length. The step between letters shrinks as the title grows, so
/// the last letter always lands within the time limit asserted below.
/// </summary>
[TestFixture]
public class RevealCascadeTests
{
    [TestCase(1, ExpectedResult = 18)]  // A short title uses the maximum step.
    [TestCase(22, ExpectedResult = 6)]  // A title longer than any the app uses.
    public int TheStepNarrowsAsTheTitleGrows(int letterCount) => RevealCascade.StepMs(letterCount);

    [Test]
    public void EveryTitleLengthFinishesInsideTheEntranceBudget()
    {
        for (int letterCount = 1; letterCount <= 40; letterCount++)
        {
            int lastLetterLandsAt = (letterCount - 1) * RevealCascade.StepMs(letterCount) + RevealCascade.LetterRiseMs;

            Assert.That(lastLetterLandsAt, Is.LessThanOrEqualTo(300),
                $"{letterCount} letters: the last letter lands at {lastLetterLandsAt} ms");
        }
    }

    [Test]
    public void WordsSplitAtSpacesAndContinueTheCascadeAcrossThem()
    {
        Assert.That(RevealCascade.WordsOf("You win!"), Is.EqualTo(new[]
        {
            (Word: "You", FirstLetter: 0),
            (Word: "win!", FirstLetter: 3),
        }));
    }

    [TestCase("You win!",    ExpectedResult = 7)]
    [TestCase("Robin wins!", ExpectedResult = 10)]
    public int TheLetterCountCountsLettersAndNotGaps(string title) => RevealCascade.LetterCount(title);
}
