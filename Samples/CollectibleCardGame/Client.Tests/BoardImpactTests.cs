using Game.Client.Services;
using Game.Logic;

namespace Game.Client.Tests;

[TestFixture]
public sealed class BoardImpactTests
{
    [Test]
    public void DenHitProducesOneDeltaEvenWhenBothDamageEventsArePresent()
    {
        List<BoardImpact> impacts = BoardImpact.FromEvents(new MatchEvent[] {
            new DamageDealtEvent(CardInstanceId.None, EffectTargetRef.Den(1), 4, false),
            new DenDamagedEvent(1, 4, 21),
        });
        Assert.That(impacts, Is.EqualTo(new[] { new BoardImpact("damage", "den", 1, 4) }));
    }

    [Test]
    public void BubbleAbsorptionDoesNotAdvertiseHealthLoss()
    {
        CardInstanceId id = new CardInstanceId(12);
        List<BoardImpact> impacts = BoardImpact.FromEvents(new MatchEvent[] {
            new DamageDealtEvent(CardInstanceId.None, EffectTargetRef.OnCritter(id), 8, true),
            new BubblePoppedEvent(id),
        });
        Assert.That(impacts, Is.EqualTo(new[] { new BoardImpact("blocked", "critter", 12, 0) }));
    }

    [Test]
    public void HealthDeltasUseConfirmedAmountsAndIgnoreEmptyHeals()
    {
        CardInstanceId id = new CardInstanceId(12);
        List<BoardImpact> impacts = BoardImpact.FromEvents(new MatchEvent[] {
            new DamageDealtEvent(CardInstanceId.None, EffectTargetRef.OnCritter(id), 2, false),
            new HealedEvent(EffectTargetRef.OnCritter(id), 1, 3),
            new HealedEvent(EffectTargetRef.Den(0), 3, 25),
            new HealedEvent(EffectTargetRef.Den(1), 0, 25),
        });
        Assert.That(impacts, Is.EqualTo(new[] {
            new BoardImpact("damage", "critter", 12, 2),
            new BoardImpact("heal", "critter", 12, 1),
            new BoardImpact("heal", "den", 0, 3),
        }));
    }
}
