using Metaplay.Core;
using Metaplay.Core.Config;
using Metaplay.Core.Model;

namespace Game.Logic
{
    /// <summary> Identifier for an <see cref="EffectStepInfo"/> row. </summary>
    [MetaSerializable]
    public class EffectStepId : StringId<EffectStepId>
    {
    }

    /// <summary>
    /// One invocation of one effect primitive. Steps are a shared library: several cards and Weathers may
    /// reference the same step, and a card's behavior is an ordered list of them bound to a trigger.
    /// <para>
    /// Which parameters a step must and must not carry depends on its <see cref="Op"/>; the config build
    /// checks every combination, so the engine can read a step's parameters without re-deriving legality.
    /// </para>
    /// </summary>
    [MetaSerializable]
    public class EffectStepInfo : IGameConfigData<EffectStepId>
    {
        [MetaMember(1)]  public EffectStepId        StepId   { get; private set; }
        [MetaMember(2)]  public EffectOp            Op       { get; private set; }
        [MetaMember(3)]  public EffectTargetKind    Target   { get; private set; }
        /// <summary> The primary amount, or null when the op takes none. </summary>
        [MetaMember(4)]  public EffectAmount        Amount   { get; private set; }
        /// <summary> The second amount: Buff's health delta, Peek's keep count. Null otherwise. </summary>
        [MetaMember(5)]  public EffectAmount        Amount2  { get; private set; }
        [MetaMember(6)]  public ManaDuration        Duration { get; private set; }
        /// <summary> The keyword a <see cref="EffectOp.GrantKeyword"/> step grants. </summary>
        [MetaMember(7)]  public MetaRef<KeywordInfo> Keyword { get; private set; }
        /// <summary> The critter a <see cref="EffectOp.Summon"/> step puts onto the board. </summary>
        [MetaMember(8)]  public MetaRef<CardInfo>   Card     { get; private set; }
        /// <summary> Constrains a <c>Per:</c> counter or a graveyard selection. Null when unconstrained. </summary>
        [MetaMember(9)]  public EffectFilter        Filter   { get; private set; }
        [MetaMember(10)] public GraveyardSelector   Selector { get; private set; }
        /// <summary> Designer note. Never read by the engine. </summary>
        [MetaMember(11)] public string              Notes    { get; private set; }

        public EffectStepId ConfigKey => StepId;

        public EffectStepInfo() { }

        public EffectStepInfo(
            EffectStepId stepId,
            EffectOp op,
            EffectTargetKind target,
            EffectAmount amount,
            EffectAmount amount2,
            ManaDuration duration,
            MetaRef<KeywordInfo> keyword,
            MetaRef<CardInfo> card,
            EffectFilter filter,
            GraveyardSelector selector,
            string notes)
        {
            StepId   = stepId;
            Op       = op;
            Target   = target;
            Amount   = amount;
            Amount2  = amount2;
            Duration = duration;
            Keyword  = keyword;
            Card     = card;
            Filter   = filter;
            Selector = selector;
            Notes    = notes;
        }
    }

    /// <summary>
    /// The <c>EffectSteps</c> sheet as authored: the amount and filter columns are small string grammars, so
    /// they arrive as text and are parsed into <see cref="EffectStepInfo"/>'s typed form here. A cell that
    /// does not parse fails the config build with the row named, rather than surfacing at runtime.
    /// </summary>
    public class EffectStepSourceItem : IGameConfigSourceItem<EffectStepId, EffectStepInfo>
    {
        public EffectStepId      StepId;
        public EffectOp          Op;
        public EffectTargetKind  Target;
        public string            Amount;
        public string            Amount2;
        public ManaDuration      Duration;
        public KeywordId         Keyword;
        public CardId            Card;
        public string            Filter;
        public GraveyardSelector Selector;
        public string            Notes;

        public EffectStepId ConfigKey => StepId;

        public EffectStepInfo ToConfigData(GameConfigBuildLog buildLog)
        {
            EffectAmount amount  = ParseAmount(buildLog, nameof(Amount), Amount);
            EffectAmount amount2 = ParseAmount(buildLog, nameof(Amount2), Amount2);
            EffectFilter filter  = ParseFilter(buildLog, Filter);

            return new EffectStepInfo(
                StepId,
                Op,
                Target,
                amount,
                amount2,
                Duration,
                Keyword != null ? MetaRef<KeywordInfo>.FromKey(Keyword) : null,
                Card != null ? MetaRef<CardInfo>.FromKey(Card) : null,
                filter,
                Selector,
                Notes);
        }

        EffectAmount ParseAmount(GameConfigBuildLog buildLog, string column, string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            if (!EffectAmount.TryParse(text, out EffectAmount amount, out string error))
            {
                buildLog.Error($"EffectSteps '{StepId}': {column} '{text}' does not parse: {error}");
                return null;
            }

            return amount;
        }

        EffectFilter ParseFilter(GameConfigBuildLog buildLog, string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            if (!EffectFilter.TryParse(text, out EffectFilter filter, out string error))
            {
                buildLog.Error($"EffectSteps '{StepId}': Filter '{text}' does not parse: {error}");
                return null;
            }

            return filter;
        }
    }
}
