// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Game.Logic;
using Metaplay.Core;
using Metaplay.Core.Config;
using Metaplay.Core.GameConfigTool;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace GameConfigTool
{
    class GameConfigTool : GameConfigToolBase
    {
        const string GoogleSheetsCredentials = "Backend/Server/Secrets/example-idler-google-sheet-credentials.json"; // Path to Google service account credentials .json

        protected override IGameConfigSourceFetcherConfig FetcherConfig => GameConfigSourceFetcherConfigCore.Create()
            .WithGoogleCredentialsFilePath(GoogleSheetsCredentials)
            .WithLocalFileSourcesPath("Assets/LocalGameConfigSource");

        public async Task<int> RunAsync(string[] args)
        {
            // Default to 'build --dry-run' if no argument given
            if (args.Length == 0)
                args = new string[] { "build", "--dry-run" };

            // Handle different commands
            string command = args[0];
            if (command == "build")
            {
                bool isDryRun = args.Contains("--dry-run");

                Console.WriteLine("Building StaticGameConfig archive..");
                IdlerGameConfigBuildParameters buildParams = new IdlerGameConfigBuildParameters();
                GameConfigBuildIntegration integration = IntegrationRegistry.Get<GameConfigBuildIntegration>();
                buildParams.DefaultSource = integration
                    .GetAvailableGameConfigBuildSources(nameof(IdlerGameConfigBuildParameters.DefaultSource)).First();
                buildParams.LiveOpsSource = integration
                    .GetAvailableGameConfigBuildSources(nameof(IdlerGameConfigBuildParameters.LiveOpsSource)).First();
                buildParams.OpaqueDataSource = new FileSystemBuildSource(fileFormat: FileSystemBuildSource.Format.Binary);
                await BuildStaticGameConfigAsync(buildParams, writeOutputFiles: !isDryRun);
            }
            else if (command == "print")
            {
                await PrintGameConfigAsync();
            }
            else if (command == "publish")
            {
                // \todo [petri] support targets other than localhost
                string target = "localhost"; // (args.Length >= 2) ? args[1] : "localhost";

                // Publish StaticGameConfig to target
                Console.WriteLine("Publishing StaticGameConfig to '{0}'..", target);
                await PublishGameConfigAsync("http://localhost:5550/api/", "gameConfig", authorizationToken: null, queryParams: null);
            }
            else
            {
                Console.WriteLine("Invalid command '{0}'!", command);
                return 15;
            }

            return 0;
        }

        static async Task<int> Main(string[] args)
        {
            // Switch to project root directory
            // \todo [petri] This is a hack to get the same project paths to work as are used from within Unity.
            //               Need to figure out a holistic solution for configuring the builds from Unity, this
            //               utility and the dashboard.
            Directory.SetCurrentDirectory("../..");

            // Initialize and run the tool
            GameConfigTool tool = new GameConfigTool();
            return await tool.RunAsync(args);
        }
    }
}
