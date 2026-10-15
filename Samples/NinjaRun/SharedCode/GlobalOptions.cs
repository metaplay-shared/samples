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
            projectId:              "whole-impalas-strive",
            // Display name of project.
            projectName:            "Ninja Time Trial",
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
                EnableLocalizations = false
            });
    }
}
