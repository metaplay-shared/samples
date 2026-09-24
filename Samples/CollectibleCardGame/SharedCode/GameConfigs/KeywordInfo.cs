using Metaplay.Core;
using Metaplay.Core.Config;
using Metaplay.Core.Model;

namespace Game.Logic
{
    /// <summary> Identifier for a <see cref="KeywordInfo"/> row. </summary>
    [MetaSerializable]
    public class KeywordId : StringId<KeywordId>
    {
    }

    /// <summary>
    /// One of the seven player-facing keywords. Five are engine flags whose semantics live in the rules
    /// engine; two — Hello and Goodbye — are the display names of triggers, shown because a card binds them.
    /// The row itself carries identity and glossary text only.
    /// </summary>
    [MetaSerializable]
    public class KeywordInfo : IGameConfigData<KeywordId>
    {
        /// <summary> For engine-flag rows the id <em>is</em> the flag name, which is what ties the two together. </summary>
        [MetaMember(1)] public KeywordId   KeywordId   { get; private set; }
        [MetaMember(2)] public KeywordKind Kind        { get; private set; }
        [MetaMember(3)] public string      DisplayName { get; private set; }
        /// <summary> The rules text shown in the keyword glossary. </summary>
        [MetaMember(4)] public string      Description { get; private set; }

        public KeywordId ConfigKey => KeywordId;

        public KeywordInfo() { }

        public KeywordInfo(KeywordId keywordId, KeywordKind kind, string displayName, string description = null)
        {
            KeywordId   = keywordId;
            Kind        = kind;
            DisplayName = displayName;
            Description = description;
        }

        /// <summary>
        /// The engine flag this row names, or <see cref="KeywordFlags.None"/> for a trigger label or an id the
        /// engine does not implement. Derived rather than authored, so the id and the flag cannot drift apart.
        /// </summary>
        public KeywordFlags EngineFlag =>
            Kind == KeywordKind.EngineFlag ? EffectVocabulary.ParseEngineFlag(KeywordId?.Value) : KeywordFlags.None;
    }
}
