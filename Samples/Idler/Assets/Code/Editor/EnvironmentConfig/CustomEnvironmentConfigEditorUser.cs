using Metaplay.Unity;

// Example usage of the Editor-only members of the custom environment config. The runtime config seen through
// IEnvironmentConfigProvider holds only the members tagged with [MetaMember], so Editor code reads the rest from
// the authoring-time environment configs.
public class CustomEnvironmentConfigEditorUser
{
    void UseEditorOnlyEnvironmentConfig()
    {
        MetaplayUnityEditorEnvironmentConfig environment = MetaplayUnityEditorEnvironmentConfigStore.Instance.Get("offline");
        string myString = ((CustomEnvironmentConfig)environment.Extension).MyEditorOnlyString;
    }
}
