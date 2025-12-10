using System.Diagnostics;
using UnityEngine;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

/// <summary>
/// Ensures an EventSystem exists in the scene at runtime. Only needed for sample scene where
/// we don't know whether the legacy input manager or new input system is used.
/// Automatically adds the appropriate input module based on which input system is available.
/// </summary>
public class InputSystemInitializer : MonoBehaviour
{
    void Awake()
    {
        // Skip if an EventSystem already exists in the scene
        if (EventSystem.current != null)
            return;

        // Initialize EventSystem. Used by both new and legacy inputs.
        gameObject.AddComponent<EventSystem>();

        // Add the appropriate input module based on Player Settings.
        // Unity defines ENABLE_INPUT_SYSTEM when the Input System package is active,
        // and ENABLE_LEGACY_INPUT_MANAGER when the legacy input backend is enabled.
#if ENABLE_INPUT_SYSTEM
        UnityEngine.Debug.Log("[metaplay] HelloWorld sample: Using new Input System");
        gameObject.AddComponent<InputSystemUIInputModule>();
#elif ENABLE_LEGACY_INPUT_MANAGER
        UnityEngine.Debug.Log("[metaplay] HelloWorld sample: Using legacy Input Manager");
        gameObject.AddComponent<StandaloneInputModule>();
#else
        UnityEngine.Debug.LogError("[metaplay] HelloWorld sample: Unable to detect the Unity input system used in this project.");
#endif
    }
}
