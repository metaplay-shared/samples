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
            projectId:              "lovely-wombats-build",
            projectName:            "Idler",
            gameMagic:              "IDLR",
            supportedLogicVersions: new MetaVersionRange(4, 5),
            clientLogicVersion:     5,
            guildInviteCodeSalt:    0x17,
            sharedNamespaces:       new string[] { "Game.Logic" },
            defaultLanguage:        LanguageId.FromString("en"),
            featureFlags: new MetaplayFeatureFlags
            {
                EnableLocalizations = true,
                EnableGuilds = true,
                EnablePlayerLeagues = true,
            });
    }
}
