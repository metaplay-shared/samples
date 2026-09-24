using Game.Client.Services;
using Game.Logic;
using System.Collections.Generic;
using System.Linq;

namespace Game.Client.Tests;

/// <summary>
/// Which deck Home's picker shows. Needs neither a browser nor a server: the rule is a
/// pure function of the saved deck ids, the starter deck ids, the last played deck and the current selection,
/// which is exactly what makes the fallback chain assertable at all.
/// </summary>
[TestFixture]
public class PracticeDeckPickTests
{
    static readonly List<int> TwoSavedDecks = new List<int> { 1, 2 };

    static readonly List<StarterDeckId> SixStarterDecks =
        new[] { "FireAndFoam", "SunlitThicket", "AlleySparks", "PorchlightPack", "RiverbankPatience", "EmberAndOak" }
            .Select(StarterDeckId.FromString)
            .ToList();

    static readonly List<StarterDeckId> NoStarterDecks = new List<StarterDeckId>();

    static string Key(DeckChoice? choice) => choice?.ToKey() ?? "";

    #region Saved decks

    [Test]
    public void WithNothingSelectedYet_TakesTheLastPlayedDeck()
    {
        Assert.That(
            Key(PracticeDeckPick.Choose(null, DeckChoice.Saved(2), TwoSavedDecks, SixStarterDecks)),
            Is.EqualTo("saved:2"));
    }

    [Test]
    public void WithNoLastPlayedDeck_TakesTheFirstStarterDeck()
    {
        // The fallback order matches the picker's group order — starter decks first — so the value the
        // select shows and the first option it lists are the same thing.
        Assert.That(
            Key(PracticeDeckPick.Choose(null, null, TwoSavedDecks, SixStarterDecks)),
            Is.EqualTo("starter:FireAndFoam"));
    }

    [Test]
    public void WithNoStarterDecksAtAll_TakesTheFirstSavedDeck()
    {
        Assert.That(
            Key(PracticeDeckPick.Choose(null, null, TwoSavedDecks, NoStarterDecks)),
            Is.EqualTo("saved:1"));
    }

    [Test]
    public void WhenTheLastPlayedDeckWasDeleted_FallsBackRatherThanNamingNothing()
    {
        // The remembered deck is a choice, not a guarantee: 7 was deleted since it was played.
        Assert.That(
            Key(PracticeDeckPick.Choose(null, DeckChoice.Saved(7), TwoSavedDecks, SixStarterDecks)),
            Is.EqualTo("starter:FireAndFoam"));
    }

    [Test]
    public void WhenTheSelectedDeckWasDeleted_FallsBackRatherThanNamingNothing()
    {
        // This is the shape the defect took: a select whose value matches no option renders empty.
        Assert.That(
            Key(PracticeDeckPick.Choose(DeckChoice.Saved(7), null, TwoSavedDecks, SixStarterDecks)),
            Is.EqualTo("starter:FireAndFoam"));
    }

    [Test]
    public void AnExistingSelection_SurvivesAReSeed()
    {
        // Home re-seeds on every state change, so a pick the player made themselves has to win over both the
        // last played deck and the first one.
        Assert.That(
            Key(PracticeDeckPick.Choose(DeckChoice.Saved(1), DeckChoice.Saved(2), TwoSavedDecks, SixStarterDecks)),
            Is.EqualTo("saved:1"));
    }

    #endregion

    #region Starter decks

    [Test]
    public void WithNothingSelectedAndNoSavedDecks_TakesTheFirstStarterDeck()
    {
        // The fresh-account default, which is the whole point of the change: no gate and no empty picker.
        Assert.That(
            Key(PracticeDeckPick.Choose(null, null, new List<int>(), SixStarterDecks)),
            Is.EqualTo("starter:FireAndFoam"));
    }

    [Test]
    public void WithNothingSelected_TheLastPlayedStarterDeckWins()
    {
        Assert.That(
            Key(PracticeDeckPick.Choose(null, DeckChoice.Starter(StarterDeckId.FromString("EmberAndOak")), TwoSavedDecks, SixStarterDecks)),
            Is.EqualTo("starter:EmberAndOak"));
    }

    [Test]
    public void WhenTheLastPlayedStarterDeckLeftTheConfig_FallsBackToTheFirst()
    {
        // A content edit that drops a starter deck must not blank the picker for the accounts that played it.
        Assert.That(
            Key(PracticeDeckPick.Choose(null, DeckChoice.Starter(StarterDeckId.FromString("RetiredDeck")), TwoSavedDecks, SixStarterDecks)),
            Is.EqualTo("starter:FireAndFoam"));
    }

    [Test]
    public void AStarterSelection_SurvivesAReSeed()
    {
        Assert.That(
            Key(PracticeDeckPick.Choose(
                DeckChoice.Starter(StarterDeckId.FromString("AlleySparks")),
                DeckChoice.Saved(2),
                TwoSavedDecks,
                SixStarterDecks)),
            Is.EqualTo("starter:AlleySparks"));
    }

    #endregion

    #region Nothing to name

    [Test]
    public void WithNoDecksOfEitherKind_ChoosesNothing()
    {
        // The defensive null. Unreachable in shipped content: the config build refuses an empty starter pool.
        Assert.That(
            PracticeDeckPick.Choose(DeckChoice.Saved(3), DeckChoice.Saved(2), new List<int>(), NoStarterDecks),
            Is.Null);
    }

    [Test]
    public void AMalformedChoice_IsReplacedRatherThanNamed()
    {
        // Neither half set: a payload no picker produces, which must not become the select's value.
        Assert.That(
            Key(PracticeDeckPick.Choose(new DeckChoice(), null, TwoSavedDecks, SixStarterDecks)),
            Is.EqualTo("starter:FireAndFoam"));
        Assert.That(
            Key(PracticeDeckPick.Choose(null, new DeckChoice(), TwoSavedDecks, SixStarterDecks)),
            Is.EqualTo("starter:FireAndFoam"));
    }

    #endregion
}
