using Game.Logic;
using System.Collections.Generic;

namespace Game.Client.Services;

/// <summary>
/// Which deck Home's picker shows. A pure function of the account's saved deck ids, the config's starter deck
/// ids, the deck it last played and whatever is currently selected, so it can be asked on every entry to the
/// screen rather than only when something changes.
/// <para>
/// A <c>&lt;select&gt;</c> whose value matches no option shows nothing at all, so the answer has to name a
/// deck the picker actually lists: Home asks on entry as well as on every change, and the fallback chain is
/// what keeps a deleted deck from leaving the picker pointing at nothing.
/// </para>
/// </summary>
public static class PracticeDeckPick
{
    /// <summary>
    /// The deck to show: the current selection if it still exists, else the last played deck if it does, else
    /// the first starter deck, else the first saved deck, else null. A selection the player made themselves
    /// therefore survives every later re-seed, and only a choice that has gone away is replaced.
    /// <para>
    /// A starter deck comes before the first saved deck because that is the picker's own group order, so the
    /// value the <c>&lt;select&gt;</c> shows and the first option it lists are the same thing. Null is only
    /// reachable when the config carries no starter decks at all, which the config build refuses.
    /// </para>
    /// </summary>
    public static DeckChoice? Choose(
        DeckChoice? currentSelection,
        DeckChoice? lastPlayed,
        IEnumerable<int> savedDeckIds,
        IEnumerable<StarterDeckId> starterDeckIds)
    {
        List<int>           saved   = new List<int>(savedDeckIds);
        List<StarterDeckId> starter = new List<StarterDeckId>(starterDeckIds);

        if (Exists(currentSelection, saved, starter))
            return currentSelection;

        if (Exists(lastPlayed, saved, starter))
            return lastPlayed;

        if (starter.Count > 0)
            return DeckChoice.Starter(starter[0]);

        if (saved.Count > 0)
            return DeckChoice.Saved(saved[0]);

        return null;
    }

    /// <summary> Whether a choice names a deck the picker still lists. A malformed choice names nothing. </summary>
    static bool Exists(DeckChoice? choice, List<int> savedDeckIds, List<StarterDeckId> starterDeckIds)
    {
        if (choice == null || !choice.IsWellFormed)
            return false;

        if (choice.IsStarter)
            return starterDeckIds.Contains(choice.StarterDeckId);

        return savedDeckIds.Contains(choice.SavedDeckId);
    }
}
