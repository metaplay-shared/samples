using Metaplay.Core.Config;

namespace Game.Logic
{
    /// <summary>
    /// Registry for all game configuration data shared by the client and the server. Cards, clans, keywords,
    /// Weathers, rank tracks and starter decks are pure config data, so this registry is the whole content
    /// surface of the game: adding a card is a config change, not a code change.
    /// <para>
    /// Every entry is built from a CSV sheet of the same name in <c>GameConfigSource/</c>. What the data is
    /// allowed to mean is checked in <see cref="BuildTimeValidate"/> — the build fails rather than warns when
    /// content cannot mean anything.
    /// </para>
    /// </summary>
    public class SharedGameConfig : SharedGameConfigBase
    {
        /// <summary>
        /// Global, singleton game configuration values (game-wide tunables).
        /// </summary>
        [GameConfigEntry("Global")]
        public GlobalConfig Global { get; private set; } = new GlobalConfig();

        /// <summary> The animal clans, plus the clan-limit-exempt Wanderers. </summary>
        [GameConfigEntry("Clans")]
        public GameConfigLibrary<ClanId, ClanInfo> Clans { get; private set; }

        /// <summary> The seven player-facing keywords: five engine flags and two trigger labels. </summary>
        [GameConfigEntry("Keywords")]
        public GameConfigLibrary<KeywordId, KeywordInfo> Keywords { get; private set; }

        /// <summary> The shared rank-growth templates cards are authored against. </summary>
        [GameConfigEntry("RankTracks")]
        public GameConfigLibrary<RankTrackId, RankTrackInfo> RankTracks { get; private set; }

        /// <summary> The library of primitive invocations that cards and Weathers compose their behavior from. </summary>
        [GameConfigEntry("EffectSteps")]
        [GameConfigEntryTransform(typeof(EffectStepSourceItem))]
        public GameConfigLibrary<EffectStepId, EffectStepInfo> EffectSteps { get; private set; }

        /// <summary> The whole card catalogue, collectible and not. </summary>
        [GameConfigEntry("Cards")]
        public GameConfigLibrary<CardId, CardInfo> Cards { get; private set; }

        /// <summary> The Weather pool one modifier is drawn from before each match's mulligan. </summary>
        [GameConfigEntry("Weathers")]
        public GameConfigLibrary<WeatherId, WeatherInfo> Weathers { get; private set; }

        /// <summary> The starter decks Home offers beside the player's own. Content, not a code path. </summary>
        [GameConfigEntry("StarterDecks")]
        public GameConfigLibrary<StarterDeckId, StarterDeckInfo> StarterDecks { get; private set; }

        /// <summary>
        /// Runs every content rule the build must prove, reporting each failure against the sheet row that
        /// caused it. Errors here block a config from being published; the same checks run as unit tests over
        /// deliberately broken fixtures, because a validator that rejects nothing passes everything.
        /// </summary>
        public override void BuildTimeValidate(GameConfigValidationResult validationResult)
        {
            base.BuildTimeValidate(validationResult);

            ContentValidator.Validate(ContentPool.FromConfig(this), new GameConfigValidationSink(validationResult));
        }
    }

    /// <summary> Routes <see cref="ContentValidator"/>'s findings into the SDK's config build report. </summary>
    public sealed class GameConfigValidationSink : IContentIssueSink
    {
        readonly GameConfigValidationResult _result;

        public GameConfigValidationSink(GameConfigValidationResult result)
        {
            _result = result;
        }

        public void Error(string sheet, string row, string column, string message)
            => _result.Error(sheet, row, message, columnHint: column ?? "");

        public void Warning(string sheet, string row, string column, string message)
            => _result.Warning(sheet, row, message, columnHint: column ?? "");
    }
}
