// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using UnityEditor;

public class MenuItems
{
    [MenuItem("Idler/Configure IAP Fake Store", isValidateFunction: false, priority = 801)]
    public static void ConfigureIAPFakeStore()
    {
        Selection.activeObject = Metaplay.Unity.IAP.IAPFakeStoreConfig.Instance;
    }
}
