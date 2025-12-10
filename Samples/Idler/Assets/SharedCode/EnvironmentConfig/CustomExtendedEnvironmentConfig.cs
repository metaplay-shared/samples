// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Client;

// Example custom environment config class
public class CustomExtendedEnvironmentConfig : EnvironmentConfig
{
    // Custom environment specific variable
    public int MyExtendedInt;

    // Override the filter method to filter out custom environment specific variables from being included in client builds
    protected override void FilterForClientBuild()
    {
        base.FilterForClientBuild();
        // We don't want to include the value of MyExtendedInt in client builds, so we reset it here
        MyExtendedInt = -1;
    }
}

// Example custom config class usage
public class ExampleCustomEnvironmentConfigUser
{
    void UseCustomEnvironmentConfig()
    {
        CustomExtendedEnvironmentConfig config = (CustomExtendedEnvironmentConfig)IEnvironmentConfigProvider.Get();
        int myInt = config.MyExtendedInt;
    }
}