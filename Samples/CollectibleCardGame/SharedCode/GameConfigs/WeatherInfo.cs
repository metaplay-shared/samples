using Metaplay.Core;
using Metaplay.Core.Config;
using Metaplay.Core.Model;
using System.Collections.Generic;

namespace Game.Logic
{
    /// <summary> Identifier for a <see cref="WeatherInfo"/> row. </summary>
    [MetaSerializable]
    public class WeatherId : StringId<WeatherId>
    {
    }

    /// <summary>
    /// One Weather: the public, symmetric, match-wide modifier drawn before the mulligan. Up to three hooks,
    /// all drawn from vocabulary that already exists — a keyword aura, a cost rule, and a triggered effect
    /// resolved as the seat the event belongs to, which is what makes one row symmetric by construction.
    /// <para>
    /// Weathers are the only source of auras and cost modification in the game; card-borne versions are
    /// deliberately out of scope.
    /// </para>
    /// </summary>
    [MetaSerializable]
    public class WeatherInfo : IGameConfigData<WeatherId>
    {
        [MetaMember(1)] public WeatherId                     WeatherId   { get; private set; }
        [MetaMember(2)] public string                        DisplayName { get; private set; }
        /// <summary> A keyword every critter of both sides has while the Weather holds. Null for no aura. </summary>
        [MetaMember(3)] public MetaRef<KeywordInfo>          AuraKeyword { get; private set; }
        [MetaMember(4)] public WeatherCostScope              CostScope   { get; private set; }
        /// <summary> The discount or surcharge the cost rule applies. Costs floor at zero. </summary>
        [MetaMember(5)] public int                           CostDelta   { get; private set; }
        [MetaMember(6)] public WeatherTrigger                Trigger     { get; private set; }
        /// <summary> The steps the trigger enqueues, in authored order. Weather steps go before any card's. </summary>
        [MetaMember(7)] public List<MetaRef<EffectStepInfo>> Steps       { get; private set; } = new List<MetaRef<EffectStepInfo>>();
        [MetaMember(8)] public string                        ArtEmoji    { get; private set; }
        /// <summary> The sentence shown at the reveal, before the mulligan. </summary>
        [MetaMember(9)] public string                        Description { get; private set; }

        public WeatherId ConfigKey => WeatherId;

        public WeatherInfo() { }

        public WeatherInfo(
            WeatherId weatherId,
            string displayName,
            MetaRef<KeywordInfo> auraKeyword = null,
            WeatherCostScope costScope = WeatherCostScope.None,
            int costDelta = 0,
            WeatherTrigger trigger = WeatherTrigger.None,
            List<MetaRef<EffectStepInfo>> steps = null,
            string artEmoji = null,
            string description = null)
        {
            WeatherId   = weatherId;
            DisplayName = displayName;
            AuraKeyword = auraKeyword;
            CostScope   = costScope;
            CostDelta   = costDelta;
            Trigger     = trigger;
            Steps       = steps ?? new List<MetaRef<EffectStepInfo>>();
            ArtEmoji    = artEmoji;
            Description = description;
        }

        public bool HasAura     => AuraKeyword != null;
        public bool HasCostRule => CostScope != WeatherCostScope.None;
        public bool HasTrigger  => Trigger != WeatherTrigger.None;
    }
}
