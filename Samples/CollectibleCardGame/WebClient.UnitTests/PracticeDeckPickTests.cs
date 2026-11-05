using WebClient.Services;
using Game.Logic;
using System.Collections.Generic;
using System.Linq;

namespace WebClient.UnitTests;

/// <summary>
/// Which deck Home's picker shows. Needs neither a browser nor a server: the rule is a
/// pure function of the saved deck ids, the starter deck ids, the last played deck and the current selection,
/// which is exactly what makes the fallback chain assertable at all.
/// </summary>
[TestFixture]
public class PracticeDeckPickTests
{
    static readonly List<int> TwoSavedDecks = new List<int> { 1, 2 };

    static readonly List<int> NoSavedDecks = new List<int>();

    static readonly List<StarterDeckId> SixStarterDecks =
        new[] { "FireAndFoam", "SunlitThicket", "AlleySparks", "PorchlightPack", "RiverbankPatience", "EmberAndOak" }
            .Select(StarterDeckId.FromString)
            .ToList();

    static readonly List<StarterDeckId> NoStarterDecks = new List<StarterDeckId>();

    static DeckChoice Starter(string id) => DeckChoice.Starter(StarterDeckId.FromString(id));

    static TestCaseData Case(string name, DeckChoice? selected, DeckChoice? lastPlayed, List<int> saved, List<StarterDeckId> starters, string? expected)
        => new TestCaseData(selected, lastPlayed, saved, starters, expected).SetName(name);

    /// <summary>
    /// The fallback order matches the picker's group order — starter decks first — so the value the select
    /// shows and the first option it lists are the same thing. Home re-seeds on every state change, so a pick
    /// the player made themselves wins over both the last played deck and the first one. A remembered or
    /// selected deck is a choice, not a guarantee: one that no longer exists falls back rather than naming
    /// nothing, because a select whose value matches no option renders empty.
    /// </summary>
    static IEnumerable<TestCaseData> Picks()
    {
        yield return Case("With nothing selected yet, takes the last played deck", null, DeckChoice.Saved(2), TwoSavedDecks, SixStarterDecks, "saved:2");
        yield return Case("With no last played deck, takes the first starter deck", null, null, TwoSavedDecks, SixStarterDecks, "starter:FireAndFoam");
        yield return Case("With no starter decks at all, takes the first saved deck", null, null, TwoSavedDecks, NoStarterDecks, "saved:1");
        yield return Case("When the last played deck was deleted, falls back", null, DeckChoice.Saved(7), TwoSavedDecks, SixStarterDecks, "starter:FireAndFoam");
        yield return Case("When the selected deck was deleted, falls back", DeckChoice.Saved(7), null, TwoSavedDecks, SixStarterDecks, "starter:FireAndFoam");
        yield return Case("An existing selection survives a re-seed", DeckChoice.Saved(1), DeckChoice.Saved(2), TwoSavedDecks, SixStarterDecks, "saved:1");
        // The fresh-account default: no gate and no empty picker.
        yield return Case("With nothing selected and no saved decks, takes the first starter deck", null, null, NoSavedDecks, SixStarterDecks, "starter:FireAndFoam");
        yield return Case("With nothing selected, the last played starter deck wins", null, Starter("EmberAndOak"), TwoSavedDecks, SixStarterDecks, "starter:EmberAndOak");
        // A content edit that drops a starter deck must not blank the picker for the accounts that played it.
        yield return Case("When the last played starter deck left the config, falls back to the first", null, Starter("RetiredDeck"), TwoSavedDecks, SixStarterDecks, "starter:FireAndFoam");
        yield return Case("A starter selection survives a re-seed", Starter("AlleySparks"), DeckChoice.Saved(2), TwoSavedDecks, SixStarterDecks, "starter:AlleySparks");
        // The defensive null. Unreachable in shipped content: the config build refuses an empty starter pool.
        yield return Case("With no decks of either kind, chooses nothing", DeckChoice.Saved(3), DeckChoice.Saved(2), NoSavedDecks, NoStarterDecks, null);
        // Neither half set: a payload no picker produces, which must not become the select's value.
        yield return Case("A malformed selection is replaced rather than named", new DeckChoice(), null, TwoSavedDecks, SixStarterDecks, "starter:FireAndFoam");
        yield return Case("A malformed last played deck is replaced rather than named", null, new DeckChoice(), TwoSavedDecks, SixStarterDecks, "starter:FireAndFoam");
    }

    [TestCaseSource(nameof(Picks))]
    public void ThePickerShowsARealDeck(DeckChoice? selected, DeckChoice? lastPlayed, List<int> saved, List<StarterDeckId> starters, string? expected)
    {
        DeckChoice? chosen = PracticeDeckPick.Choose(selected, lastPlayed, saved, starters);

        Assert.That(chosen?.ToKey(), Is.EqualTo(expected));
    }
}
