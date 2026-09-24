using Metaplay.Core;
using Metaplay.Core.Localization;

namespace Game.Logic
{
    public class GlobalOptions : IMetaplayCoreOptionsProvider
    {
        /// <summary>
        /// Game-specific constant options for core Metaplay SDK.
        /// </summary>
        public MetaplayCoreOptions Options { get; } = new MetaplayCoreOptions(
            // Unique project ID. Pick your own when starting a new game; keep it stable thereafter.
            projectId:              "table-stakes",
            // Display name of project.
            projectName:            "Table Stakes",
            // The range of client logic versions that the server accepts connections from.
            supportedLogicVersions: new MetaVersionRange(2, 2),
            // The logic version of the current client.
            clientLogicVersion:     2,
            // Salt for generating guild invite codes.
            guildInviteCodeSalt:    0x17,
            // List of namespaces that contain shared game code logic.
            sharedNamespaces:       new string[] { "Game.Logic" },
            // Default language used by the game.
            defaultLanguage:        LanguageId.FromString("en"),
            // Configure enabled SDK features.
            featureFlags: new MetaplayFeatureFlags
            {
                EnableLocalizations = false,

                // The seasonal tournament is built on the SDK's Leagues framework, which provides the season
                // schedule, divisions, standings, season resolution and history (docs/seasonal-tournament.md).
                // This flag enables the League entity kinds, the league database tables and the player's league
                // integration, so the tournament does not work without it. The game UI never says "league".
                EnablePlayerLeagues = true,
            });
    }
}
