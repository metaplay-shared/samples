// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

// Disabled on non-iOS targets to avoid needlessly requiring UnityEditor.iOS.Xcode to be installed.
#if UNITY_IOS

using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;
using UnityEngine;

public class DisableBitcodeOnIosBuildProcessor : IPostprocessBuildWithReport
{
    public int callbackOrder => 999;

    /// <summary>
    /// Disable 'Bitcode' for iOS builds. Bitcode is deprecated in Xcode 14 and the App Store
    /// no longer allows is. Bitcode support is also being stripped out from various libraries
    /// which causes build failures if the project is trying to use it.
    /// </summary>
    public void OnPostprocessBuild(BuildReport report)
    {
        if (report.summary.platform != BuildTarget.iOS)
            return;

        // Read .pxxproj file
        string projectPath = report.summary.outputPath + "/Unity-iPhone.xcodeproj/project.pbxproj";
        PBXProject pbxProject = new PBXProject();
        pbxProject.ReadFromFile(projectPath);

        // Main
        string target = pbxProject.GetUnityMainTargetGuid();
        pbxProject.SetBuildProperty(target, "ENABLE_BITCODE", "NO");

        // Unity Tests
        target = pbxProject.TargetGuidByName(PBXProject.GetUnityTestTargetName());
        pbxProject.SetBuildProperty(target, "ENABLE_BITCODE", "NO");

        // Unity Framework
        target = pbxProject.GetUnityFrameworkTargetGuid();
        pbxProject.SetBuildProperty(target, "ENABLE_BITCODE", "NO");

        // Save .pbxproj file
        pbxProject.WriteToFile(projectPath);
        Debug.Log("Disabled bitcode in Xcode project");
    }
}

#endif
