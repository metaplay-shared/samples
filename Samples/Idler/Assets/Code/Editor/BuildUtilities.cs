using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Metaplay.Core.Client;
using Metaplay.Unity;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

namespace Code.Editor
{
    public class BuildUtilities
    {
        static string ExecuteCommand(string executable, string arguments)
        {
            var prc = new System.Diagnostics.Process();
            prc.StartInfo.FileName = executable;
            prc.StartInfo.Arguments = arguments;
            prc.StartInfo.RedirectStandardOutput = true;
            prc.StartInfo.UseShellExecute = false;
            prc.Start();
            prc.WaitForExit(5_000);
            string output = prc.StandardOutput.ReadToEnd();
            return output.Trim();
        }

        public static string GetGitCommitId()
        {
            // TeamCity sets BUILD_VCS_NUMBER
            string teamCityGitId = Environment.GetEnvironmentVariable("BUILD_VCS_NUMBER");
            if (!string.IsNullOrEmpty(teamCityGitId))
                return teamCityGitId;

            // \todo [petri] support other CI systems

            string gitRevParseId = ExecuteCommand("git", "rev-parse HEAD");
            if (!string.IsNullOrEmpty(gitRevParseId))
                return gitRevParseId;

            throw new InvalidOperationException("Unable to determine git commit id");
        }

        public static void UpdateCommitId(string commitId)
        {
            // Replace CommitId in ClientVersion.cs
            string buildVersionPath = "Assets/Code/Systems/ClientVersion.cs";
            Debug.Log($"Writing CommitId = {commitId} into {buildVersionPath}...");
            string content = File.ReadAllText(buildVersionPath);
            content = Regex.Replace(content, "CommitId = .*;", $"CommitId = \"{commitId}\";");
            File.WriteAllText(buildVersionPath, content);
        }

        static void ExecuteBuild(string apkPath, string environmentId, string[] includedEnvironments, BuildTarget buildTarget, BuildOptions buildOptions)
        {
            // Update CommitId in ClientVersion.cs
            //UpdateCommitId();

            // Bake the environment configs into the build
            MetaplayUnityEditorEnvironmentConfigBuilder.Build(environmentId, includedEnvironments);

            // Build game
            BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new string[] { "Assets/Scenes/App Start Scene.unity", "Assets/Scenes/Loading Scene.unity", "Assets/Scenes/Game Scene.unity" },
                locationPathName = apkPath,
                target = buildTarget,
                options = buildOptions,
            });
        }

        [MenuItem("Build/Develop Android")]
        public static void BuildDevelopAndroid()
        {
            ExecuteBuild("idler-nimbly.apk", environmentId: "nimbly", new string[] {"nimbly"}, BuildTarget.Android, BuildOptions.Development);
        }

        [MenuItem("Build/Demo Android")]
        public static void BuildDemoAndroid()
        {
            ExecuteBuild("idler-demo.apk", environmentId: "demo", new string[] {"demo"}, BuildTarget.Android, BuildOptions.None);
        }

        [PostProcessBuild]
        public static void OnPostProcessBuild(BuildTarget buildTarget, string path)
        {
    //#if UNITY_IOS
            if (buildTarget == BuildTarget.iOS)
            {
                // ..
            }
    //#endif

    //#if UNITY_ANDROID
            if (buildTarget == BuildTarget.Android)
            {
                // ..
            }
    //#endif

        }

        #if UNITY_CLOUD_BUILD
        public static void PreExport(UnityEngine.CloudBuild.BuildManifestObject manifest) {
            string offsetStr = Environment.GetEnvironmentVariable("UCB_UNITY_BUILD_NUMBER_OFFSET");
            int offset;
            if (offsetStr == null)
                offset = 0;
            else if (!int.TryParse(offsetStr, out offset))
                throw new InvalidOperationException("Could not convert  " + offsetStr + " to int");

            int buildNumber = offset + manifest.GetValue<int>("buildNumber");
            PlayerSettings.bundleVersion = $"0.1.{buildNumber}";
            PlayerSettings.iOS.buildNumber = $"{buildNumber}";
            PlayerSettings.Android.bundleVersionCode = buildNumber;
        }
        #endif
    }
}