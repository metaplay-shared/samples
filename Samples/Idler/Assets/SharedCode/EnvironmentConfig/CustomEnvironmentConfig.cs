using Metaplay.Core.Client;
using Metaplay.Core.Model;
using System;

// Example game-specific environment settings. Edited as part of each environment in the Metaplay/Environment Configs
// window and stored in ProjectSettings/Metaplay/EnvironmentConfigs.json. Only the members tagged with [MetaMember]
// are included in client builds.
[Serializable]
[MetaSerializableDerived(1)]
public class CustomEnvironmentConfig : EnvironmentConfigExtension
{
    // Custom environment specific variable, included in client builds
    [MetaMember(1)] public int MyExtendedInt;

    // Custom environment specific variable for Editor tooling only, left out of client builds
    public string MyEditorOnlyString;
}
