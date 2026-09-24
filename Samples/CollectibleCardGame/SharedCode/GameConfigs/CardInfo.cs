using Metaplay.Core;
using Metaplay.Core.Config;
using Metaplay.Core.Model;
using System;
using System.Collections.Generic;

namespace Game.Logic
{
    /// <summary> Identifier for a <see cref="CardInfo"/> row. </summary>
    [MetaSerializable]
    public class CardId : StringId<CardId>
    {
    }

    /// <summary>
    /// One card in the catalogue. Everything a card does is data: its clan, its numbers, the engine-flag
    /// keywords it is printed with, and the ordered effect steps it binds to each trigger. The client can
    /// render a card it has never seen because there is nothing to a card but this row.
    /// </summary>
    [MetaSerializable]
    public class CardInfo : IGameConfigData<CardId>
    {
        [MetaMember(1)]  public CardId                        CardId              { get; private set; }
        [MetaMember(2)]  public string                        DisplayName         { get; private set; }
        [MetaMember(3)]  public MetaRef<ClanInfo>             Clan                { get; private set; }
        [MetaMember(4)]  public CardType                      Type                { get; private set; }
        [MetaMember(5)]  public CardRarity                    Rarity              { get; private set; }
        [MetaMember(6)]  public int                           Cost                { get; private set; }
        /// <summary> Critters only. </summary>
        [MetaMember(7)]  public int                           Attack              { get; private set; }
        /// <summary> Critters only, and at least 1. </summary>
        [MetaMember(8)]  public int                           Health              { get; private set; }
        /// <summary> Tricks only: the Snack subtype, which points at your own side. </summary>
        [MetaMember(9)]  public bool                          IsSnack             { get; private set; }
        /// <summary> Printed engine-flag keywords. Hello and Goodbye are never listed — they follow from the bindings. </summary>
        [MetaMember(10)] public List<MetaRef<KeywordInfo>>    Keywords            { get; private set; } = new List<MetaRef<KeywordInfo>>();
        [MetaMember(11)] public ChooseTargetKind              ChooseTarget        { get; private set; }
        [MetaMember(12)] public List<MetaRef<EffectStepInfo>> Hello               { get; private set; } = new List<MetaRef<EffectStepInfo>>();
        [MetaMember(13)] public List<MetaRef<EffectStepInfo>> Goodbye             { get; private set; } = new List<MetaRef<EffectStepInfo>>();
        [MetaMember(14)] public List<MetaRef<EffectStepInfo>> OnAttack            { get; private set; } = new List<MetaRef<EffectStepInfo>>();
        [MetaMember(15)] public List<MetaRef<EffectStepInfo>> TurnStart           { get; private set; } = new List<MetaRef<EffectStepInfo>>();
        [MetaMember(16)] public List<MetaRef<EffectStepInfo>> TurnEnd             { get; private set; } = new List<MetaRef<EffectStepInfo>>();
        [MetaMember(17)] public MetaRef<RankTrackInfo>        RankTrack           { get; private set; }
        /// <summary> False for tokens and The Acorn: they are never in a collection and never in a deck. </summary>
        [MetaMember(18)] public bool                          Collectible         { get; private set; }
        /// <summary> Whether a new player is granted this card on first login. </summary>
        [MetaMember(19)] public bool                          InStarterCollection { get; private set; }
        [MetaMember(20)] public string                        ArtEmoji            { get; private set; }
        /// <summary> The authored English rendering of what the composition does. </summary>
        [MetaMember(21)] public string                        RulesText           { get; private set; }
        [MetaMember(22)] public string                        Flavor              { get; private set; }

        public CardId ConfigKey => CardId;

        public CardInfo() { }

        public CardInfo(
            CardId cardId,
            string displayName,
            MetaRef<ClanInfo> clan,
            CardType type,
            CardRarity rarity,
            int cost,
            MetaRef<RankTrackInfo> rankTrack,
            int attack = 0,
            int health = 0,
            bool isSnack = false,
            List<MetaRef<KeywordInfo>> keywords = null,
            ChooseTargetKind chooseTarget = ChooseTargetKind.None,
            List<MetaRef<EffectStepInfo>> hello = null,
            List<MetaRef<EffectStepInfo>> goodbye = null,
            List<MetaRef<EffectStepInfo>> onAttack = null,
            List<MetaRef<EffectStepInfo>> turnStart = null,
            List<MetaRef<EffectStepInfo>> turnEnd = null,
            bool collectible = true,
            bool inStarterCollection = true,
            string artEmoji = null,
            string rulesText = null,
            string flavor = null)
        {
            CardId              = cardId;
            DisplayName         = displayName;
            Clan                = clan;
            Type                = type;
            Rarity              = rarity;
            Cost                = cost;
            RankTrack           = rankTrack;
            Attack              = attack;
            Health              = health;
            IsSnack             = isSnack;
            Keywords            = keywords  ?? new List<MetaRef<KeywordInfo>>();
            ChooseTarget        = chooseTarget;
            Hello               = hello     ?? new List<MetaRef<EffectStepInfo>>();
            Goodbye             = goodbye   ?? new List<MetaRef<EffectStepInfo>>();
            OnAttack            = onAttack  ?? new List<MetaRef<EffectStepInfo>>();
            TurnStart           = turnStart ?? new List<MetaRef<EffectStepInfo>>();
            TurnEnd             = turnEnd   ?? new List<MetaRef<EffectStepInfo>>();
            Collectible         = collectible;
            InStarterCollection = inStarterCollection;
            ArtEmoji            = artEmoji;
            RulesText           = rulesText;
            Flavor              = flavor;
        }

        /// <summary> The steps bound to one trigger, in authored resolution order. Never null. </summary>
        public IReadOnlyList<MetaRef<EffectStepInfo>> GetSteps(CardTrigger trigger)
        {
            List<MetaRef<EffectStepInfo>> steps;
            switch (trigger)
            {
                case CardTrigger.Hello:     steps = Hello;     break;
                case CardTrigger.Goodbye:   steps = Goodbye;   break;
                case CardTrigger.OnAttack:  steps = OnAttack;  break;
                case CardTrigger.TurnStart: steps = TurnStart; break;
                case CardTrigger.TurnEnd:   steps = TurnEnd;   break;
                default: throw new ArgumentOutOfRangeException(nameof(trigger), trigger, "Not a card trigger");
            }

            return (IReadOnlyList<MetaRef<EffectStepInfo>>)steps ?? EmptySteps;
        }

        static readonly MetaRef<EffectStepInfo>[] EmptySteps = new MetaRef<EffectStepInfo>[0];

        /// <summary> Whether the card binds any step to <paramref name="trigger"/>. </summary>
        public bool BindsTrigger(CardTrigger trigger) => GetSteps(trigger).Count > 0;

        /// <summary>
        /// The printed engine flags as one value. Requires the keyword references to be resolved, which they
        /// are for any card that came out of a config archive.
        /// </summary>
        public KeywordFlags GetKeywordFlags()
        {
            KeywordFlags flags = KeywordFlags.None;
            if (Keywords != null)
            {
                foreach (MetaRef<KeywordInfo> keyword in Keywords)
                    flags |= keyword.Ref.EngineFlag;
            }
            return flags;
        }

        /// <summary>
        /// Canonical card order: ordinal comparison of card ids. This is the tie-break wherever a rule-made
        /// selection needs one — graveyard selectors, the peek default, the Heist auto-pick — so that every
        /// deterministic choice in the game breaks ties the same way.
        /// </summary>
        public static int CompareCanonical(CardId a, CardId b)
            => string.CompareOrdinal(a?.Value, b?.Value);
    }
}
