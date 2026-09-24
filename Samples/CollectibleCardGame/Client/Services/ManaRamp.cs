using Game.Logic;
using System.Collections.Generic;

namespace Game.Client.Services;

/// <summary>
/// Which mana changes are worth showing a player, and the only one that is: a <b>permanent ramp</b>, where the
/// maximum grows and the pool stands still. A turn start's refill moves both numbers and the turn line already
/// announces it; a spend is the card that was played. A ramp has nothing else on the board to show it — the
/// acorn it adds arrives spent, beside the acorns the card itself cost — which is what made "I played Acorn
/// Hoard but I don't think my mana increased" the right complaint.
/// <para>
/// The question is asked of the history rather than of one event, because <see cref="ManaChangedEvent"/>
/// carries the state after it and no delta. It is a pure function of that history, which is what lets the feed
/// line and the acorn pop rest on one predicate and be tested away from the browser.
/// </para>
/// </summary>
public static class ManaRamp
{
    /// <summary>
    /// Whether the mana change at <paramref name="index"/> raised the maximum and left the pool where it was,
    /// and what the maximum had been.
    /// <para>
    /// A seat's <em>first</em> mana change is its first turn's refill, which moves both numbers, so having
    /// nothing earlier for that seat to compare against is not a ramp.
    /// </para>
    /// </summary>
    public static bool IsMaxOnlyRamp(IReadOnlyList<MatchEvent>? history, int index, out int previousMax)
    {
        previousMax = 0;
        if (history == null || index < 1 || index >= history.Count || history[index] is not ManaChangedEvent mana)
            return false;

        for (int ndx = index - 1; ndx >= 0; ndx--)
        {
            if (history[ndx] is not ManaChangedEvent earlier || earlier.Seat != mana.Seat)
                continue;

            previousMax = earlier.MaxMana;
            return mana.MaxMana > earlier.MaxMana && mana.Mana == earlier.Mana;
        }

        return false;
    }

    /// <summary>
    /// The same question asked about an event rather than an index — what a caller holding one beat has. The
    /// events on the timeline are the very instances the listener was handed (<c>MatchModel.Emit</c> adds to
    /// the history and dispatches the same object), so identity is the lookup.
    /// </summary>
    public static bool IsMaxOnlyRamp(IReadOnlyList<MatchEvent>? history, MatchEvent ev)
    {
        if (history == null)
            return false;

        for (int ndx = history.Count - 1; ndx >= 0; ndx--)
        {
            if (ReferenceEquals(history[ndx], ev))
                return IsMaxOnlyRamp(history, ndx, out int _);
        }

        return false;
    }
}
