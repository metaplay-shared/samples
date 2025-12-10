// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using System;
using System.Collections.Generic;
using Metaplay.Core;
using Metaplay.Core.Config;
using Metaplay.Unity;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Game.Logic;
using Metaplay.Core.Client;
using UnityEditor;
using UnityEngine;
using FileUtil = Metaplay.Core.FileUtil;

public class IdlerUnityGameConfigBuildIntegration : DefaultUnityGameConfigBuildIntegration
{
    /// <summary>
    /// When implementing google sheets, you can provide authentication info here.
    /// </summary>
    public override IGameConfigSourceFetcherConfig CreateFetcherConfig()
    {
        return ((GameConfigSourceFetcherConfigCore)base.CreateFetcherConfig())
            .WithLocalFileSourcesPath("Assets/LocalGameConfigSource");
    }

    public override IEnumerable<GameConfigBuildSource> GetAvailableGameConfigBuildSources(string sourcePropertyName)
    {
        if (sourcePropertyName == nameof(IdlerGameConfigBuildParameters.OpaqueDataSource))
            return Enumerable.Repeat(new FileSystemBuildSource(FileSystemBuildSource.Format.Binary), 1);
        return base.GetAvailableGameConfigBuildSources(sourcePropertyName);
    }

    public override IEnumerable<Type> GetCustomBuildSourceTypesForSource(string sourcePropertyName)
    {
        if (sourcePropertyName == nameof(IdlerGameConfigBuildParameters.OpaqueDataSource))
            return Type.EmptyTypes;
        return base.GetCustomBuildSourceTypesForSource(sourcePropertyName);
    }
}

// This file contains Metaplay sample code. It can be adapted to suit your project's needs or you can
// replace the functionality completely with your own.
namespace Metaplay.Sample
{
    /// <summary>
    /// Minimal Game Config building utility for building the StaticGameConfig.mpa (for server to use)
    /// and SharedGameConfig.mpa (for client and offline mode to use) config archives.
    ///
    /// This has just the minimal functionality required for the Hello World sample to work.
    /// See the Idler reference project for a more comprehensive config builder.
    /// </summary>
    public static class UnityGameConfigBuilder
    {
        const string ServerLocalizationsPath = "Backend/Server/GameConfig/Localizations.mpa";
        const string ClientLocalizationsPath = "Assets/StreamingAssets/Localizations";
        
        public const string SharedGameConfigPath   = "Assets/StreamingAssets/SharedGameConfig.mpa";
        public const string StaticGameConfigPath   = "Backend/Server/GameConfig/StaticGameConfig.mpa";

        [MenuItem("Config Builder/Build Game Configs")]
        public static void BuildFullGameConfigAsync()
        {
            // Build StaticGameConfig archive & write to disk
            EditorTask.Run(nameof(BuildFullGameConfigAsync), async () =>
            {
                var gameConfigBuildIntegration = IntegrationRegistry.Get<GameConfigBuildIntegration>();
                Type defaultGameConfigBuildParametersType = gameConfigBuildIntegration.GetDefaultGameConfigBuildParametersType();
                var instance =
                    (IdlerGameConfigBuildParameters)Activator.CreateInstance(defaultGameConfigBuildParametersType);
                instance.DefaultSource =
                    gameConfigBuildIntegration.GetAvailableGameConfigBuildSources(nameof(GameConfigBuildParameters.DefaultSource)).FirstOrDefault();
                instance.LiveOpsSource =
                    gameConfigBuildIntegration.GetAvailableGameConfigBuildSources(nameof(IdlerGameConfigBuildParameters.LiveOpsSource)).FirstOrDefault();
                
                if (instance.DefaultSource == null || instance.LiveOpsSource == null)
                    DebugLog.Warning("No game config sources configured, resulting game config will be empty. For more info about configuring game config sources, please see https://docs.metaplay.io/feature-cookbooks/game-configs/working-with-game-config-data.html#building-game-configs.");
                
                await BuildFullGameConfigAsync(instance);
            });
        }
            
        public static async Task BuildFullGameConfigAsync(GameConfigBuildParameters buildParameters)
        {
            var idlerGameConfigBuildParameters = buildParameters as IdlerGameConfigBuildParameters;

            idlerGameConfigBuildParameters.OpaqueDataSource =
                new FileSystemBuildSource(fileFormat: FileSystemBuildSource.Format.Binary);
            
            var unityGameConfigBuildIntegration = IntegrationRegistry.Get<IUnityGameConfigBuildIntegration>();
            
            await BuildArchiveAsync(
                StaticGameConfigPath,
                buildFunc: () => StaticFullGameConfigBuilder.BuildArchiveAsync(MetaTime.Now, parentId: MetaGuid.None, parent: null, buildParams: buildParameters, fetcherConfig: unityGameConfigBuildIntegration.CreateFetcherConfig()),
                onCompletedHandler: null,
                onSuccessHandler: fullArchive =>
                {
                    // Export SharedGameConfig.mpa into StreamingAssets/
                    (ContentHash sharedVersion, byte[] sharedBytes) = GameConfigUtil.GetSharedArchiveFromFullArchiveForClient(fullArchive);
                    FileUtil.WriteAllBytes(SharedGameConfigPath, sharedBytes);
                    if (MetaplayClient.State != null)
                    {
                        // Inform Metaplay that GameConfigs have been built, so it can hot-load the GameConfigs in offline mode
                        // \todo [petri] Move into core?
                        Debug.Log("invoke MetaplayClient.OnSharedGameConfigUpdated()");
                        ConfigArchive sharedGameConfig = ConfigArchive.FromBytes(sharedBytes);
                        MetaplayClient.State.OnSharedGameConfigUpdated(sharedGameConfig);
                    }
                });
        }
        
        
        static async Task<ConfigArchive> BuildLocalizationsArchiveAsync(MetaTime timestamp)
        {
            var unityGameConfigBuildIntegration = IntegrationRegistry.Get<IUnityGameConfigBuildIntegration>();
            GameConfigBuildIntegration integration = IntegrationRegistry.Get<GameConfigBuildIntegration>();
            LocalizationsBuild build = integration.MakeLocalizationsBuild(unityGameConfigBuildIntegration.CreateFetcherConfig());

            // \note: hard-coded to using first available build source for localizations.
            GameConfigBuildSource source = integration.GetAvailableLocalizationsBuildSources(nameof(LocalizationsBuildParameters.DefaultSource)).First();

            DefaultLocalizationsBuildParameters buildParams = new DefaultLocalizationsBuildParameters()
                { DefaultSource = source };

            return await build.CreateArchiveAsync(timestamp, buildParams, CancellationToken.None);
        }

        [MenuItem("Idler/Build Localizations", isValidateFunction: false, priority = 101)]
        public static async Task TryBuildLocalizations()
        {
            // Build Localizations & write each language in its own file
            await BuildArchiveAsync(ServerLocalizationsPath,
                () => BuildLocalizationsArchiveAsync(MetaTime.Now),
                onSuccessHandler: fullArchive =>
                {
                    // Export Localizations into StreamingAssets/ in FolderEncoding format
                    Debug.Log($"Writing Localizations archive as multiple files into {ClientLocalizationsPath}");
                    ConfigArchiveBuildUtility.FolderEncoding.WriteToDirectory(fullArchive, ClientLocalizationsPath);
                });
        }

        // PUBLISH TO SERVER

        [MenuItem("Idler/Publish Localizations/To local server", isValidateFunction: false, priority = 210)]
        public static async Task PublishLocalizationsToLocal()
        {
            ConfigArchive localizationsArchive = await ConfigArchive.FromFileAsync(ServerLocalizationsPath);
            await BackendAdminApi.UploadLocalizationArchiveToServerAsync("http://localhost:5550/api/", localizationsArchive, authorizationToken: null);
        }

        [MenuItem("Idler/Publish Localizations/To lovely-wombats-build-nimbly.p2-eu", isValidateFunction: false, priority = 211)]
        public static async Task PublishLocalizationsToDevelopP1()
        {
            EnvironmentConfig environmentConfig = DefaultEnvironmentConfigProvider.Instance.GetEnvironmentConfig("nimbly");
            ConfigArchive localizationsArchive = await ConfigArchive.FromFileAsync(ServerLocalizationsPath);
            string authorizationToken = await environmentConfig.FetchAuthorizationTokenAsync();
            await BackendAdminApi.UploadLocalizationArchiveToServerAsync(environmentConfig.ClientGameConfigBuildApiConfig.AdminApiBaseUrl, localizationsArchive, authorizationToken);
        }
        
        /// <summary>
        /// Helper method for building an archive from within Unity. Performs the operation in a new Task so as not
        /// to block Unity as the operation may take some time to finish. Only allows one build task to be active
        /// at a time to avoid conflicts when writing the output files.
        /// </summary>
        /// <param name="targetPath">Path where to store the built archive</param>
        /// <param name="buildFunc">Callback method that builds the config archive</param>
        /// <param name="onSuccessHandler">Callback method for when archive was successfully built</param>
        /// <param name="onCompletedHandler">Callback method for when operation has been completed (successfully or non-successfully)</param>
        static async Task BuildArchiveAsync(string targetPath, Func<Task<ConfigArchive>> buildFunc, Action<ConfigArchive> onSuccessHandler = null, Action onCompletedHandler = null)
        {
            // Check target directory
            if (string.IsNullOrEmpty(targetPath))
                throw new ArgumentNullException(nameof(targetPath));

            try
            {
                // Build the ConfigArchive (fetch source data, convert to binary and export as archive).
                ConfigArchive configArchive = await buildFunc();

                // Write archive as single file
                Debug.Log($"Writing ConfigArchive as single file {targetPath} with {configArchive.Entries.Count} entries:\n{string.Join("\n", configArchive.Entries.Select(entry => $"  {entry.Name} ({entry.Bytes.Length} bytes): {entry.Hash}"))}");
                await ConfigArchiveBuildUtility.WriteToFileAsync(targetPath, configArchive);

                // Refresh AssetDatabase to make sure Unity sees changed files
                // \note Must be called from main thread!
                AssetDatabase.Refresh();

                // Invoke success callback
                onSuccessHandler?.Invoke(configArchive);
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to build archive {targetPath}: {ex}");
                throw;
            }
            finally
            {
                // Invoke completion callback
                onCompletedHandler?.Invoke();
            }
        }
    }
}
