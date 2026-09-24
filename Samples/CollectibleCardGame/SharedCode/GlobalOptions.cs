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
            // Technical name for the project. DO NOT CHANGE! It also keys the WebAssembly client's browser
            // storage: the "{ProjectId}:" localStorage prefix and the "{ProjectId}/MetaWebBlobStore" IndexedDB.
            projectId:              "StickyPaws",
            // Display name for the project.
            projectName:            "Sticky Paws",
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
                // Built-in localizations need the language list synchronously at startup, and the browser
                // client has async file IO only. Not supported on this platform yet.
                EnableLocalizations = false
            });
    }
}
