using Game.Client.Services;
using Game.Logic;
using Metaplay.Core;

namespace Game.Client.Tests;

/// <summary>
/// What the board says a heal would do, against the arithmetic the resolution runs.
/// <para>
/// The words are the board's and the clamp is <c>ResolutionRules</c>' — the same two functions
/// <c>ApplyHeal</c> and <c>HealDen</c> call — so what this pins is that the two cannot come apart: a
/// preview that clamped for itself is exactly how a target ends up promising a number the beat then does not
/// deliver. The amount a card offers is pinned beside the rules, in <c>HealPreviewTests</c>.
/// </para>
/// <para>
/// Needs neither a browser nor a server nor a config: a critter is a value and a Den is two integers, which
/// is what makes this half of the board's copy checkable at all.
/// </para>
/// </summary>
[TestFixture]
public class TargetPreviewTests
{
    static BoardCritter Critter(int maxHealth, int damage)
        => new BoardCritter(new CardInstanceId(1), attack: 10, maxHealth: maxHealth, damage: damage,
            keywords: KeywordFlags.None, isSleepy: false, hasAttackedThisTurn: false, bubbleIntact: false);

    const int MaxDenHp = 125;

    static CardInfo Burn(int steps = 1, EffectCounter counter = EffectCounter.None, EffectTargetKind target = EffectTargetKind.Chosen)
    {
        EffectStepInfo effect = new EffectStepInfo(EffectStepId.FromString("Burn"), EffectOp.Damage, target,
            new EffectAmount(3, counter), null, default, null, null, null, default, "");
        RankTrackInfo track = new RankTrackInfo(RankTrackId.FromString("BurnTrack"),
            new RankTrackStep(0, 0, 0, 1), new RankTrackStep(0, 0, 0, 2));
        return new CardInfo(CardId.FromString("Burn"), "Burn", null, CardType.Trick, default, 1,
            MetaRef<RankTrackInfo>.FromItem(track), hello: Enumerable.Range(0, steps)
                .Select(_ => MetaRef<EffectStepInfo>.FromItem(effect)).ToList());
    }

    [TestCase(1, 3)]
    [TestCase(3, 4)]
    [TestCase(5, 6)]
    public void LiteralBurnUsesTheSharedRankTrack(int rank, int expected)
        => Assert.That(TargetPreview.ChosenDamageAmount(Burn(), rank), Is.EqualTo(expected));

    [Test]
    public void BurnPreviewDoesNotGuessChainsCountersOrUntargetedEffects()
    {
        Assert.Multiple(() =>
        {
            Assert.That(TargetPreview.ChosenDamageAmount(Burn(steps: 2), 3), Is.Null);
            Assert.That(TargetPreview.ChosenDamageAmount(Burn(counter: EffectCounter.FriendlyCritters), 3), Is.Null);
            Assert.That(TargetPreview.ChosenDamageAmount(Burn(target: EffectTargetKind.EnemyDen), 3), Is.Null);
        });
    }

    [Test]
    public void BurnPreviewsTheShieldAndDenWithoutInventingACounterattack()
    {
        BoardCritter shielded = new BoardCritter(new CardInstanceId(1), 4, 2, 0,
            KeywordFlags.Bubble, false, false, true);
        TargetDelta shield = TargetPreview.DamageCritter(shielded, 3);
        TargetDelta den = TargetPreview.DamageDen(2, 3);
        Assert.Multiple(() =>
        {
            Assert.That(shield.Amount, Is.Zero);
            Assert.That(shield.IsBlocked, Is.True);
            Assert.That(shield.IsLethal, Is.False);
            Assert.That(den.Amount, Is.EqualTo(3));
            Assert.That(den.HealthAfter, Is.Zero);
            Assert.That(den.IsLethal, Is.True);
            Assert.That(den.Description, Does.Not.Contain("counterattack"));
        });
    }

    [TestCase(0, true, 0, false)]
    [TestCase(4, true, 0, true)]
    [TestCase(4, false, 4, false)]
    public void CombatDeltaUsesTheRulesBubbleAbsorption(int attack, bool bubble, int expected, bool blocked)
    {
        BoardCritter attacker = new BoardCritter(new CardInstanceId(1), attack, 5, 0,
            KeywordFlags.None, false, false, false);
        BoardCritter defender = new BoardCritter(new CardInstanceId(2), 2, 3, 0,
            bubble ? KeywordFlags.Bubble : KeywordFlags.None, false, false, bubble);
        TargetDelta delta = TargetPreview.Attack(attacker, defender);
        Assert.Multiple(() =>
        {
            Assert.That(delta.Amount, Is.EqualTo(expected));
            Assert.That(delta.IsBlocked, Is.EqualTo(KeywordRules.BubbleAbsorbs(defender, attack)));
            Assert.That(delta.IsBlocked, Is.EqualTo(blocked));
            Assert.That(delta.IsLethal, Is.EqualTo(expected >= defender.CurrentHealth && expected > 0));
            Assert.That(delta.Description, Does.Contain("counterattack"));
        });
    }

    [Test]
    public void LethalCounterattackAndBothBubblesRemainAccessible()
    {
        BoardCritter attacker = new BoardCritter(new CardInstanceId(1), 5, 2, 0,
            KeywordFlags.None, false, false, false);
        BoardCritter defender = new BoardCritter(new CardInstanceId(2), 3, 4, 0,
            KeywordFlags.None, false, false, false);
        TargetDelta lethalTrade = TargetPreview.Attack(attacker, defender);
        Assert.Multiple(() =>
        {
            Assert.That(lethalTrade.Description, Does.Contain("yours dies"));
            Assert.That(lethalTrade.Counter, Is.Not.Null);
            Assert.That(lethalTrade.Counter!.Amount, Is.EqualTo(3));
            Assert.That(lethalTrade.Counter.IsLethal, Is.True);
            Assert.That(lethalTrade.Counter.IsBlocked, Is.False);
        });
        BoardCritter protectedAttacker = new BoardCritter(new CardInstanceId(1), 5, 2, 0,
            KeywordFlags.Bubble, false, false, true);
        BoardCritter protectedDefender = new BoardCritter(new CardInstanceId(2), 3, 4, 0,
            KeywordFlags.Bubble, false, false, true);
        TargetDelta protectedTrade = TargetPreview.Attack(protectedAttacker, protectedDefender);
        Assert.Multiple(() =>
        {
            Assert.That(protectedTrade.Amount, Is.Zero);
            Assert.That(protectedTrade.IsLethal, Is.False);
            Assert.That(protectedTrade.Description, Does.Contain("your Bubble pops"));
            Assert.That(protectedTrade.Counter!.Amount, Is.Zero);
            Assert.That(protectedTrade.Counter.IsBlocked, Is.True);
            Assert.That(protectedTrade.Counter.IsLethal, Is.False);
        });
    }

    [Test]
    public void DenAttackHasNoCounterattackAndPreservesOverkillAmount()
    {
        BoardCritter attacker = new BoardCritter(new CardInstanceId(1), 7, 2, 0,
            KeywordFlags.None, false, false, false);
        TargetDelta delta = TargetPreview.AttackDen(attacker, 3);
        Assert.Multiple(() =>
        {
            Assert.That(delta.Amount, Is.EqualTo(7));
            Assert.That(delta.HealthAfter, Is.Zero);
            Assert.That(delta.IsLethal, Is.True);
            Assert.That(delta.Description, Does.Contain("does not counterattack"));
            Assert.That(delta.Counter, Is.Null);
        });
    }

    [Test]
    public void TypedHealingUsesTheResolutionClampAcrossAllSmallAmounts()
    {
        for (int amount = 0; amount <= 12; amount++)
        {
            for (int damage = 0; damage <= 12; damage++)
            {
                TargetDelta critter = TargetPreview.HealCritter(Critter(6, damage), amount);
                TargetDelta den = TargetPreview.HealDen(MaxDenHp - damage, MaxDenHp, amount);
                Assert.Multiple(() =>
                {
                    Assert.That(critter.Kind, Is.EqualTo(TargetDeltaKind.Heal));
                    Assert.That(critter.Amount, Is.EqualTo(ResolutionRules.HealableOnCritter(damage, amount)));
                    Assert.That(den.Amount, Is.EqualTo(ResolutionRules.HealableOnDen(MaxDenHp - damage, amount, MaxDenHp)));
                    Assert.That(critter.HealthAfter, Is.EqualTo(6 - damage + critter.Amount));
                    Assert.That(den.HealthAfter, Is.EqualTo(MaxDenHp - damage + den.Amount));
                });
            }
        }
    }

    [Test]
    public void ADenAtFullHealth_SaysSoRatherThanNamingTheAmount()
    {
        // An amount the game can actually produce: the smallest authored heal is a whole stat quantum.
        Assert.That(TargetPreview.DenHeal(MaxDenHp, MaxDenHp, 20), Is.EqualTo("+0 · already full"));
        Assert.That(TargetPreview.DenHeal(MaxDenHp, MaxDenHp, 5), Is.EqualTo("+0 · already full"));
    }

    [Test]
    public void ADamagedDen_NamesTheRoomItHas_NotTheCardsNumber()
    {
        // Twenty offered, ten points of room: the cap is the whole reason the preview exists.
        Assert.That(TargetPreview.DenHeal(MaxDenHp - 10, MaxDenHp, 20), Is.EqualTo("+10 → 125"));
        Assert.That(TargetPreview.DenHeal(MaxDenHp - 45, MaxDenHp, 20), Is.EqualTo("+20 → 100"));
    }

    [Test]
    public void ACritterNamesTheDamageItCanUndo()
    {
        // A heal larger than the damage is capped by the damage, which is the case the first line is for.
        Assert.That(TargetPreview.CritterHeal(Critter(maxHealth: 20, damage: 5), 20), Is.EqualTo("+5 → 20"));
        Assert.That(TargetPreview.CritterHeal(Critter(maxHealth: 45, damage: 40), 20), Is.EqualTo("+20 → 25"));
        Assert.That(TargetPreview.CritterHeal(Critter(maxHealth: 20, damage: 0), 20), Is.EqualTo("+0 · full"));
    }

    [Test]
    public void EveryPreviewedAmount_IsTheAmountTheResolutionWouldApply()
    {
        // The property, over the whole small space rather than at the three cases above: whatever the board
        // says, it says the resolution's own number.
        for (int amount = 0; amount <= 12; amount++)
        {
            // The body is 30, comfortably above the damage range, so every iteration describes a critter the
            // game could really have rather than one carrying more damage than health.
            for (int damage = 0; damage <= 12; damage++)
            {
                int healed = ResolutionRules.HealableOnCritter(damage, amount);
                string expected = healed > 0 ? $"+{healed} → {30 - damage + healed}" : "+0 · full";
                Assert.That(TargetPreview.CritterHeal(Critter(maxHealth: 30, damage: damage), amount), Is.EqualTo(expected),
                    $"critter with {damage} damage, healed for {amount}");
            }

            for (int denHp = MaxDenHp - 12; denHp <= MaxDenHp; denHp++)
            {
                int healed = ResolutionRules.HealableOnDen(denHp, amount, MaxDenHp);
                string expected = healed > 0 ? $"+{healed} → {denHp + healed}" : "+0 · already full";
                Assert.That(TargetPreview.DenHeal(denHp, MaxDenHp, amount), Is.EqualTo(expected),
                    $"Den at {denHp}, healed for {amount}");
            }
        }
    }
}
