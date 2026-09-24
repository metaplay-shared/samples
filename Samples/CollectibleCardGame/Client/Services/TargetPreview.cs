using Game.Logic;
using Metaplay.Core;
using System;
using System.Collections.Generic;

namespace Game.Client.Services;

public enum TargetDeltaKind { Damage, Heal }

/// <summary> A known immediate health change, before any subsequent triggered effects. </summary>
public sealed class TargetDelta
{
    public TargetDeltaKind Kind { get; }
    public int Amount { get; }
    public int HealthAfter { get; }
    public bool IsBlocked { get; }
    public bool IsLethal { get; }
    public string Description { get; }
    public TargetDelta? Counter { get; }

    public TargetDelta(TargetDeltaKind kind, int amount, int healthAfter, bool isBlocked, bool isLethal, string description,
        TargetDelta? counter = null)
    {
        Kind = kind;
        Amount = amount;
        HealthAfter = healthAfter;
        IsBlocked = isBlocked;
        IsLethal = isLethal;
        Description = description;
        Counter = counter;
    }
}

/// <summary>
/// What aiming at a target would do, as the board says it on the target itself.
/// <para>
/// Pure, and a function of <b>public state plus the card in hand</b> — which is exactly what both sides of
/// the table already hold, so nothing here is a second answer to a rule. An attack's consequence is the
/// attacker's Attack against the target's remaining health and the trade back; a Snack's preview is the
/// amount that would actually restore, which is the heal step's amount clamped by the damage there is to
/// undo.
/// </para>
/// <para>
/// It lives in this project rather than in a component because it is arithmetic with no markup, and because
/// two callers need it: the live board, and the offline scene preview the match screen's visual work is done
/// against. Compiled into <c>Client.Tests</c> like the other pure halves of the browser client, so it can be
/// checked without a browser.
/// </para>
/// </summary>
public static class TargetPreview
{
    /// <summary> Only a single literal chosen-target burn is previewed; chains and board counters are not inferred. </summary>
    public static int? ChosenDamageAmount(CardInfo card, int rank)
    {
        IReadOnlyList<MetaRef<EffectStepInfo>> steps = card.GetSteps(CardTrigger.Hello);
        if (steps.Count != 1)
            return null;
        EffectStepInfo step = steps[0].Ref;
        if (step.Op != EffectOp.Damage || step.Target != EffectTargetKind.Chosen || step.Amount?.IsLiteralOnly != true)
            return null;
        return Math.Max(0, step.Amount.Literal + card.GetStatsAtRank(rank).EffectAmountDelta);
    }

    public static TargetDelta DamageCritter(BoardCritter target, int amount)
    {
        bool blocked = KeywordRules.BubbleAbsorbs(target, amount);
        int damage = blocked ? 0 : Math.Max(0, amount);
        bool lethal = damage > 0 && damage >= target.CurrentHealth;
        string description = blocked ? "Bubble absorbs the hit and pops; no health lost."
            : $"Takes {damage} damage; " + (lethal ? "dies on impact." : $"{target.CurrentHealth - damage} health left.");
        return new TargetDelta(TargetDeltaKind.Damage, damage, Math.Max(0, target.CurrentHealth - damage),
            blocked, lethal, description);
    }

    public static TargetDelta DamageDen(int health, int amount)
    {
        int damage = Math.Max(0, amount);
        int remaining = Math.Max(0, health - damage);
        return new TargetDelta(TargetDeltaKind.Damage, damage, remaining, false, damage > 0 && remaining == 0,
            $"Den takes {damage} damage; {remaining} health left.");
    }

    public static TargetDelta Attack(BoardCritter attacker, BoardCritter defender)
    {
        bool blocked = KeywordRules.BubbleAbsorbs(defender, attacker.Attack);
        int damage = blocked ? 0 : Math.Max(0, attacker.Attack);
        bool lethal = damage >= defender.CurrentHealth && damage > 0;
        bool counterBlocked = KeywordRules.BubbleAbsorbs(attacker, defender.Attack);
        int counter = counterBlocked ? 0 : Math.Max(0, defender.Attack);
        string target = blocked ? "its Bubble pops; no health lost"
            : lethal ? $"it dies; {damage} damage"
            : $"it takes {damage}; {defender.CurrentHealth - damage} health left";
        string trade = counterBlocked ? "your Bubble pops; no health lost"
            : counter >= attacker.CurrentHealth && counter > 0 ? $"yours dies; {counter} counterattack damage"
            : $"yours takes {counter} counterattack damage";
        return new TargetDelta(TargetDeltaKind.Damage, damage, Math.Max(0, defender.CurrentHealth - damage),
            blocked, lethal, $"On impact, {target}; {trade}. Subsequent triggered effects are not included.",
            DamageCritter(attacker, defender.Attack));
    }

    public static Dictionary<CardInstanceId, TargetDelta> Attacks(
        MatchRulesState rules, int attackerSeat, CardInstanceId attackerId, IEnumerable<CardInstanceId> targets)
    {
        Dictionary<CardInstanceId, TargetDelta> previews = new Dictionary<CardInstanceId, TargetDelta>();
        BoardCritter attacker = rules.Seat(attackerSeat).FindCritter(attackerId);
        if (attacker == null)
            return previews;
        foreach (CardInstanceId id in targets)
        {
            BoardCritter defender = rules.Seat(MatchSeats.Other(attackerSeat)).FindCritter(id);
            if (defender != null)
                previews[id] = Attack(attacker, defender);
        }
        return previews;
    }

    public static TargetDelta AttackDen(BoardCritter attacker, int health)
    {
        int amount = Math.Max(0, attacker.Attack);
        int remaining = Math.Max(0, health - amount);
        return new TargetDelta(TargetDeltaKind.Damage, amount, remaining, false, amount > 0 && remaining == 0,
            $"Den takes {amount} damage; {remaining} health left. The Den does not counterattack.");
    }

    public static TargetDelta HealCritter(BoardCritter critter, int amount)
    {
        int healed = Math.Max(0, ResolutionRules.HealableOnCritter(critter.Damage, amount));
        return new TargetDelta(TargetDeltaKind.Heal, healed, critter.CurrentHealth + healed, false, false,
            $"Restore {healed} health; {critter.CurrentHealth + healed} health after healing.");
    }

    public static TargetDelta HealDen(int health, int maximum, int amount)
    {
        int healed = Math.Max(0, ResolutionRules.HealableOnDen(health, amount, maximum));
        return new TargetDelta(TargetDeltaKind.Heal, healed, health + healed, false, false,
            $"Restore {healed} Den health; {health + healed} of {maximum} health after healing.");
    }
    /// <summary>
    /// What attacking each lit target would do. Only what the rules would actually deliver: a live Bubble
    /// absorbs the whole instance, so a trade against one is not the trade it looks like.
    /// </summary>
    public static Dictionary<CardInstanceId, string> Consequences(
        MatchRulesState rules, int attackerSeat, CardInstanceId attackerId, IEnumerable<CardInstanceId> targets)
    {
        Dictionary<CardInstanceId, string> tags = new Dictionary<CardInstanceId, string>();

        BoardCritter attacker = rules.Seat(attackerSeat).FindCritter(attackerId);
        if (attacker == null)
            return tags;

        SeatState defender = rules.Seat(MatchSeats.Other(attackerSeat));

        foreach (CardInstanceId id in targets)
        {
            BoardCritter target = defender.FindCritter(id);
            if (target == null)
                continue;

            string theirs = target.BubbleWouldAbsorb ? "its Bubble pops"
                : attacker.Attack >= target.CurrentHealth ? "it dies"
                : $"it has {target.CurrentHealth - attacker.Attack} left";

            string mine = attacker.BubbleWouldAbsorb || target.Attack <= 0 ? ""
                : target.Attack >= attacker.CurrentHealth ? " · yours dies"
                : $" · yours takes {target.Attack}";

            tags[id] = theirs + mine;
        }

        return tags;
    }

    /// <summary>
    /// How much a card in hand would restore at the rank it is held at. Zero when the card heals no chosen
    /// target, which is what makes "is this a Snack" a question the caller does not have to ask separately.
    /// <para>
    /// The amount itself is <see cref="HealPreview.ChosenHealAmount"/>'s, in shared code beside the rules:
    /// only a <c>Heal</c> step aimed at the <em>chosen</em> target counts, the rank track's delta lands on
    /// the first step of the card's Hello exactly as the engine lands it, and an amount that reads the board
    /// through a <c>Per:</c> counter is not guessed at (it answers nothing, and the board draws no badge).
    /// A second copy of that arithmetic here is how a preview and a resolution come to disagree.
    /// </para>
    /// </summary>
    public static int HealAmount(CardInfo card, int rank) => HealPreview.ChosenHealAmount(card, rank) ?? 0;

    /// <summary>
    /// What a heal of <paramref name="amount"/> would actually do to one critter. A target with nothing to
    /// undo reads "+0 · full" rather than the amount, because the cap is the thing the player has to see
    /// before spending the mana.
    /// <para>
    /// The clamp is <see cref="ResolutionRules.HealableOnCritter"/> — the function the resolution itself
    /// calls — so the words on the target and the beat that follows them cannot differ.
    /// </para>
    /// </summary>
    public static string CritterHeal(BoardCritter critter, int amount)
    {
        int healed = ResolutionRules.HealableOnCritter(critter.Damage, amount);
        return healed > 0 ? $"+{healed} → {critter.CurrentHealth + healed}" : "+0 · full";
    }

    /// <summary> The same for a Den, whose ceiling is the starting health nothing raises. </summary>
    public static string DenHeal(int denHp, int maxDenHp, int amount)
    {
        int healed = ResolutionRules.HealableOnDen(denHp, amount, maxDenHp);
        return healed > 0 ? $"+{healed} → {denHp + healed}" : "+0 · already full";
    }
}
