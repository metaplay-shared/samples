using Game.Client.Services;
using Game.Logic;
using System.Collections.Generic;

namespace Game.Client.Tests;

/// <summary>
/// Which mana change the board presents. The feed line and the acorn pop rest on one
/// predicate over the match history, and this is where the four readings are pinned: a permanent ramp, a turn
/// start's refill, a spend, and a seat's very first change. Neither a browser nor a server is needed — the
/// history is a list, which is the point of the predicate being pure.
/// </summary>
[TestFixture]
public class ManaRampTests
{
    static ManaChangedEvent Mana(int seat, int mana, int maxMana) => new ManaChangedEvent(seat, mana, maxMana);

    static List<MatchEvent> History(params MatchEvent[] events) => new List<MatchEvent>(events);

    [Test]
    public void APermanentRamp_IsARamp_AndNamesThePreviousMaximum()
    {
        // Acorn Hoard on 4 mana: the cost first, then the ramp — the maximum moves and the pool does not.
        List<MatchEvent> history = History(Mana(0, 2, 3), Mana(0, 2, 4));

        Assert.That(ManaRamp.IsMaxOnlyRamp(history, 1, out int previousMax), Is.True);
        Assert.That(previousMax, Is.EqualTo(3));
    }

    [Test]
    public void ATurnStartRefill_IsNotARamp()
    {
        // Both numbers move, and the turn line has already announced the turn.
        List<MatchEvent> history = History(Mana(0, 1, 3), Mana(0, 4, 4));

        Assert.That(ManaRamp.IsMaxOnlyRamp(history, 1, out int _), Is.False);
    }

    [Test]
    public void ASpend_IsNotARamp()
    {
        // The maximum stands still and the pool falls: what a player sees is the card being played.
        List<MatchEvent> history = History(Mana(0, 4, 4), Mana(0, 2, 4));

        Assert.That(ManaRamp.IsMaxOnlyRamp(history, 1, out int _), Is.False);
    }

    [Test]
    public void ASeatsFirstChange_IsNotARamp()
    {
        // A seat's first mana change is its first turn's refill from nothing, which moves both numbers. There
        // is nothing earlier to compare against, and the answer must be no rather than an accidental yes.
        List<MatchEvent> history = History(Mana(0, 1, 1));

        Assert.That(ManaRamp.IsMaxOnlyRamp(history, 0, out int _), Is.False);
    }

    [Test]
    public void TheOtherSeatsChanges_AreNotTheComparison()
    {
        // Interleaved seats: seat 0's ramp is measured against seat 0's previous change, not against whatever
        // happened last on the timeline.
        List<MatchEvent> history = History(
            Mana(0, 2, 3),
            Mana(1, 5, 5),
            Mana(1, 3, 5),
            Mana(0, 2, 4));

        Assert.That(ManaRamp.IsMaxOnlyRamp(history, 3, out int previousMax), Is.True);
        Assert.That(previousMax, Is.EqualTo(3), "seat 0's own previous maximum");
    }

    [Test]
    public void TheEventOverload_FindsItsOwnPositionByIdentity()
    {
        // What the board has is the beat on screen, not an index. The timeline holds the very instances the
        // listener was handed, so identity is the lookup.
        ManaChangedEvent ramp = Mana(0, 2, 4);
        List<MatchEvent> history = History(Mana(0, 2, 3), ramp);

        Assert.That(ManaRamp.IsMaxOnlyRamp(history, ramp), Is.True);
        Assert.That(ManaRamp.IsMaxOnlyRamp(history, Mana(0, 2, 4)), Is.False, "an equal-looking event is not this one");
    }

    [Test]
    public void AnEmptyOrAbsentHistory_IsNotARamp()
    {
        Assert.That(ManaRamp.IsMaxOnlyRamp(null, 0, out int _), Is.False);
        Assert.That(ManaRamp.IsMaxOnlyRamp(History(), 0, out int _), Is.False);
        Assert.That(ManaRamp.IsMaxOnlyRamp(History(Mana(0, 1, 1)), 7, out int _), Is.False);
    }
}
