// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using UnityEditor;
using UnityEditor.SceneManagement;

// Initialize from App Start Scene whenever pressing Play in Unity Editor.
// NOTE: If you want to disable this, you must restart Unity as it remembers the selection.
[InitializeOnLoad]
public class EditorStartScene
{
    const string StartScenePath = "Assets/Scenes/App Start Scene.unity";

    static EditorStartScene()
    {
        SceneAsset startScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(StartScenePath);
        EditorSceneManager.playModeStartScene = startScene;
    }
}
