// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

public static class TutorialMenu
{
    static readonly string s_sharedCodePath = "Assets/SharedCode";

    static void UpdateServerFile(string file, string match, string replacement)
    {
        string fileContents = File.ReadAllText(file);
        string updated = Regex.Replace(fileContents, match, replacement);
        File.WriteAllText(file, updated);
    }

    static void SwitchToPart(string partName)
    {
        string defineForPart = $"TUTORIAL_{partName.ToUpper()}";
#if UNITY_2023_2_OR_NEWER
        string definesString = PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.FromBuildTargetGroup(EditorUserBuildSettings.selectedBuildTargetGroup));
#else
        string definesString = PlayerSettings.GetScriptingDefineSymbolsForGroup(EditorUserBuildSettings.selectedBuildTargetGroup);
#endif
        List<string> allDefines = definesString.Split ( ';' ).ToList();

        // Check if already current
        if (allDefines.Contains(defineForPart))
            return;
        allDefines = allDefines.Where(x => !x.StartsWith("TUTORIAL_")).ToList();
        allDefines.Add(defineForPart);

#if UNITY_2023_2_OR_NEWER
        PlayerSettings.SetScriptingDefineSymbols(
            NamedBuildTarget.FromBuildTargetGroup(EditorUserBuildSettings.selectedBuildTargetGroup),
            string.Join(";", allDefines.ToArray()));
#else
        PlayerSettings.SetScriptingDefineSymbolsForGroup(
             EditorUserBuildSettings.selectedBuildTargetGroup,
             string.Join(";", allDefines.ToArray()));
#endif

        // update server gamelogic path
        string serverRelativePath = "../" + s_sharedCodePath + "/" + partName;
        UpdateServerFile("Backend/Directory.Build.props",
            "<SharedCodePath>\\$\\(MSBuildThisFileDirectory\\).*</SharedCodePath>",
            $"<SharedCodePath>$(MSBuildThisFileDirectory){serverRelativePath}</SharedCodePath>");

        // update server part define
        UpdateServerFile("Backend/Directory.Build.targets",
            "<DefineConstants>.*</DefineConstants>",
            $"<DefineConstants>$(DefineConstants);{defineForPart}</DefineConstants>");
    }
    #if TUTORIAL_PART1
    [MenuItem("Tutorial/Part 1: Game Logic (Active)", isValidateFunction: false)]
    #else
    [MenuItem("Tutorial/Part 1: Game Logic", isValidateFunction: false)]
    #endif
    public static void TutorialPart1()
    {
        SwitchToPart("Part1");
    }

    #if TUTORIAL_PART2
    [MenuItem("Tutorial/Part 2: Game Configs (Active)", isValidateFunction: false)]
    #else
    [MenuItem("Tutorial/Part 2: Game Configs", isValidateFunction: false)]
    #endif
    public static void TutorialPart2()
    {
        SwitchToPart("Part2");
    }

    #if TUTORIAL_PART3
    [MenuItem("Tutorial/Part 3: Cheat-Proof Gameplay (Active)", isValidateFunction: false)]
    #else
    [MenuItem("Tutorial/Part 3: Cheat-Proof Gameplay", isValidateFunction: false)]
    #endif
    public static void TutorialPart3()
    {
        SwitchToPart("Part3");
    }

}
