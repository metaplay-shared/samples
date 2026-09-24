using Metaplay.Core.Model;
using System;

namespace Game.Logic
{
    /// <summary>
    /// The closed set of effect primitives the engine implements. A card composes an ordered list of steps out
    /// of these verbs; adding a card is a config change, adding a verb is an engine change
    /// (<c>Docs/effects.md</c>). The config build rejects anything outside this set.
    /// </summary>
    [MetaSerializable]
    public enum EffectOp
    {
        /// <summary> Deal damage to critters and/or Dens. Bubble absorbs the first damage above zero. </summary>
        Damage = 0,
        /// <summary> Restore health, capped at the target's maximum. </summary>
        Heal = 1,
        /// <summary> The resolving seat draws cards, one at a time. </summary>
        Draw = 2,
        /// <summary> The resolving seat gains mana, for the turn or permanently. </summary>
        GainMana = 3,
        /// <summary> Permanent stat change on critters. The only op that accepts negative amounts. </summary>
        Buff = 4,
        /// <summary> Grant an engine-flag keyword permanently. </summary>
        GrantKeyword = 5,
        /// <summary> Put instances of a token critter onto the resolving seat's board. </summary>
        Summon = 6,
        /// <summary> Return critters to their owner's hand as their catalogue card. </summary>
        Bounce = 7,
        /// <summary> Add a deterministically selected copy of a graveyard card to the resolving seat's hand. </summary>
        CopyFromGraveyard = 8,
        /// <summary> Look at the top of the deck and keep part of it. The one primitive that pauses resolution. </summary>
        Peek = 9,
    }

    /// <summary>
    /// What a step addresses. Side-relative kinds are relative to the <em>resolving owner</em>: the card's
    /// controller, or for a Weather step the seat the event belongs to.
    /// <para>
    /// This is the config-level target vocabulary. The rules engine's runtime target address (a critter
    /// instance or a Den seat) is resolved from a step's kind plus the play intent's chosen target; the engine
    /// owns that type, this enum is what constrains it.
    /// </para>
    /// </summary>
    [MetaSerializable]
    public enum EffectTargetKind
    {
        /// <summary> No target: the seat-scoped ops (<see cref="EffectOp.Draw"/>, <see cref="EffectOp.GainMana"/>, <see cref="EffectOp.Summon"/>, <see cref="EffectOp.Peek"/>) act on the resolving seat. </summary>
        None = 0,
        /// <summary> The one target the player picked when playing the card. </summary>
        Chosen = 1,
        /// <summary> The critter the effect belongs to. </summary>
        Self = 2,
        OwnDen = 3,
        EnemyDen = 4,
        /// <summary> Every critter on the resolving owner's board. Never Dens. </summary>
        AllFriendly = 5,
        /// <summary> Every critter on the opposing board. Never Dens. </summary>
        AllEnemy = 6,
        /// <summary> Every critter on both boards. Never Dens. </summary>
        AllCritters = 7,
        OwnGraveyard = 8,
        EnemyGraveyard = 9,
    }

    /// <summary> How long a <see cref="EffectOp.GainMana"/> step's mana lasts. </summary>
    [MetaSerializable]
    public enum ManaDuration
    {
        None = 0,
        /// <summary> Current mana for this turn only. </summary>
        Turn = 1,
        /// <summary>
        /// Maximum mana only, with no cap: the extra acorn is payable from the next refill, and current mana is left
        /// where it was this turn.
        /// </summary>
        Permanent = 2,
    }

    /// <summary> How <see cref="EffectOp.CopyFromGraveyard"/> picks its card. Ties break on canonical card order. </summary>
    [MetaSerializable]
    public enum GraveyardSelector
    {
        None = 0,
        Cheapest = 1,
        Costliest = 2,
    }

    /// <summary>
    /// The public quantities an amount may scale on. Every counter reads public state, so both players can
    /// compute the number a card resolves for.
    /// </summary>
    [MetaSerializable]
    public enum EffectCounter
    {
        None = 0,
        /// <summary> Cards the resolving owner has played this match, excluding the source card instance. </summary>
        PlayedThisMatch = 1,
        /// <summary> Critters on the resolving owner's board at evaluation time. </summary>
        FriendlyCritters = 2,
        /// <summary> Critters on the opposing board at evaluation time. </summary>
        EnemyCritters = 3,
    }

    /// <summary> Which card types a <see cref="EffectFilter"/> clause admits. </summary>
    [MetaSerializable]
    public enum CardTypeFilter
    {
        Any = 0,
        Critter = 1,
        Trick = 2,
    }

    /// <summary>
    /// The events a card may bind steps to. Closed vocabulary: the engine raises more points internally, but
    /// config may name only these (<c>Docs/effects.md</c>).
    /// </summary>
    [MetaSerializable]
    public enum CardTrigger
    {
        /// <summary> Played from hand. A trick's whole body is its Hello. </summary>
        Hello = 0,
        /// <summary> The critter was destroyed. </summary>
        Goodbye = 1,
        /// <summary> The critter attacked, resolved after combat damage and the death sweep. </summary>
        OnAttack = 2,
        /// <summary> The start of the owner's own turn. </summary>
        TurnStart = 3,
        /// <summary> The end of the owner's own turn. </summary>
        TurnEnd = 4,
    }

    /// <summary>
    /// The engine-flag keywords, as a flag set so a critter's granted and printed keywords are one value.
    /// The names are the <c>Keywords</c> config ids of the rows whose kind is <see cref="KeywordKind.EngineFlag"/>.
    /// </summary>
    [MetaSerializable]
    [Flags]
    public enum KeywordFlags
    {
        None = 0,
        /// <summary> Enemy critters cannot attack the Den while a legally attackable Guard is on the board. </summary>
        Guard = 1 << 0,
        /// <summary> May attack the turn it enters play. </summary>
        Zoomies = 1 << 1,
        /// <summary> Ignores the first damage above zero, then pops. </summary>
        Bubble = 1 << 2,
        /// <summary> Cannot be attacked or targeted by the enemy until it deals damage. </summary>
        Sneaky = 1 << 3,
        /// <summary> Damage this critter sources also heals its owner's Den. </summary>
        Snacktime = 1 << 4,
    }

    /// <summary> Whether a keyword row is an engine rule or the display name of a trigger. </summary>
    [MetaSerializable]
    public enum KeywordKind
    {
        /// <summary> Combat and targeting semantics implemented in the engine; the row contributes identity and display. </summary>
        EngineFlag = 0,
        /// <summary> The display name of a trigger. A card shows it because it binds that trigger, never as a property. </summary>
        TriggerLabel = 1,
    }

    /// <summary> The two card types. Persistent enchantments and relics are future work. </summary>
    [MetaSerializable]
    public enum CardType
    {
        Critter = 0,
        Trick = 1,
    }

    /// <summary> Rarity signals complexity and steal-desirability, not raw power. </summary>
    [MetaSerializable]
    public enum CardRarity
    {
        Common = 0,
        Rare = 1,
        Epic = 2,
    }

    /// <summary>
    /// The class of target a card asks the player to pick when it is played. A card declares at most one, and
    /// every step of the card that wants a choice shares it.
    /// </summary>
    [MetaSerializable]
    public enum ChooseTargetKind
    {
        None = 0,
        AnyCritter = 1,
        EnemyCritter = 2,
        FriendlyCritter = 3,
        FriendlyCritterOrOwnDen = 4,
        /// <summary> Any critter or either Den. </summary>
        AnyTarget = 5,
    }

    /// <summary> Which plays a Weather's cost rule applies to. </summary>
    [MetaSerializable]
    public enum WeatherCostScope
    {
        None = 0,
        /// <summary> The first trick each seat casts on its own turn. </summary>
        FirstTrickPerTurn = 1,
        AllTricks = 2,
        AllCritters = 3,
    }

    /// <summary> The event a Weather's triggered steps hang off. Resolved as the seat the event belongs to. </summary>
    [MetaSerializable]
    public enum WeatherTrigger
    {
        None = 0,
        TurnStart = 1,
        TurnEnd = 2,
        /// <summary> Any critter died. Resolves as the dying critter's owner. </summary>
        CritterDies = 3,
    }

    /// <summary>
    /// What each member of the closed vocabulary admits. The rules the config build checks and the engine
    /// obeys are stated once, here, rather than restated at every use.
    /// </summary>
    public static class EffectVocabulary
    {
        /// <summary> Target kinds that address one or more critters. </summary>
        public static bool TargetsCritters(EffectTargetKind target)
        {
            switch (target)
            {
                case EffectTargetKind.Chosen:
                case EffectTargetKind.Self:
                case EffectTargetKind.AllFriendly:
                case EffectTargetKind.AllEnemy:
                case EffectTargetKind.AllCritters:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Target kinds that may address a Den. <see cref="EffectTargetKind.Chosen"/> counts: whether it can
        /// actually land on one depends on the card's <see cref="ChooseTargetKind"/>.
        /// </summary>
        public static bool TargetsDen(EffectTargetKind target)
            => target == EffectTargetKind.OwnDen || target == EffectTargetKind.EnemyDen || target == EffectTargetKind.Chosen;

        /// <summary> Target kinds that address a graveyard. </summary>
        public static bool TargetsGraveyard(EffectTargetKind target)
            => target == EffectTargetKind.OwnGraveyard || target == EffectTargetKind.EnemyGraveyard;

        /// <summary> Ops whose target must be a critter — a Den can neither be bounced, buffed nor granted a keyword. </summary>
        public static bool IsCritterOnly(EffectOp op)
            => op == EffectOp.Buff || op == EffectOp.GrantKeyword || op == EffectOp.Bounce;

        /// <summary> Whether a chosen-target class can resolve to a Den, which critter-only ops may not accept. </summary>
        public static bool ChoiceAdmitsDen(ChooseTargetKind choose)
            => choose == ChooseTargetKind.AnyTarget || choose == ChooseTargetKind.FriendlyCritterOrOwnDen;

        /// <summary> Whether a chosen-target class only ever points at the choosing seat's own side. </summary>
        public static bool ChoiceIsFriendlyOnly(ChooseTargetKind choose)
            => choose == ChooseTargetKind.FriendlyCritter || choose == ChooseTargetKind.FriendlyCritterOrOwnDen;

        /// <summary> The engine flag a keyword id names, or <see cref="KeywordFlags.None"/> if it names none. </summary>
        public static KeywordFlags ParseEngineFlag(string keywordId)
        {
            if (string.IsNullOrEmpty(keywordId))
                return KeywordFlags.None;

            // Enum.TryParse accepts comma-separated flag lists and bare integers; a keyword id is neither.
            switch (keywordId)
            {
                case nameof(KeywordFlags.Guard):     return KeywordFlags.Guard;
                case nameof(KeywordFlags.Zoomies):   return KeywordFlags.Zoomies;
                case nameof(KeywordFlags.Bubble):    return KeywordFlags.Bubble;
                case nameof(KeywordFlags.Sneaky):    return KeywordFlags.Sneaky;
                case nameof(KeywordFlags.Snacktime): return KeywordFlags.Snacktime;
                default:                             return KeywordFlags.None;
            }
        }

        /// <summary>
        /// Parse an exact enum member name. Unlike <see cref="Enum.TryParse{TEnum}(string, out TEnum)"/> this
        /// rejects numeric strings and comma-separated flag lists, neither of which is a vocabulary word.
        /// </summary>
        public static bool TryParseName<TEnum>(string name, out TEnum value) where TEnum : struct, Enum
        {
            value = default;
            if (string.IsNullOrEmpty(name) || !Enum.IsDefined(typeof(TEnum), name))
                return false;

            value = (TEnum)Enum.Parse(typeof(TEnum), name);
            return true;
        }

        /// <summary> Every trigger a card may bind, in the order the UI lists them. </summary>
        public static readonly CardTrigger[] AllCardTriggers =
        {
            CardTrigger.Hello,
            CardTrigger.Goodbye,
            CardTrigger.OnAttack,
            CardTrigger.TurnStart,
            CardTrigger.TurnEnd,
        };
    }
}
