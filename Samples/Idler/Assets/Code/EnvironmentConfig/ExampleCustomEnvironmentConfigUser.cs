using Metaplay.Core.Client;

// Example custom config usage
public class ExampleCustomEnvironmentConfigUser
{
    void UseCustomEnvironmentConfig()
    {
        CustomEnvironmentConfig config = IEnvironmentConfigProvider.Get().GetExtension<CustomEnvironmentConfig>();
        int myInt = config.MyExtendedInt;
    }
}
