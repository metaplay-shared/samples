// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

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
            // Unique project ID. DO NOT CHANGE !
            projectId:              "cyan-signs-bathe",
            // Display name of project.
            projectName:            "Hello World",
            // The range of client logic versions that the server accepts connections from.
            supportedLogicVersions: new MetaVersionRange(1, 1),
            // The logic version of the current client.
            clientLogicVersion:     1,
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
