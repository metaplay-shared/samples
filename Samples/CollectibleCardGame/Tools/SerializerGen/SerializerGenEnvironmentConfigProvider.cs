using Metaplay.Core.Client;
using Metaplay.Core.Network;

namespace Game.WebAssemblySerializerGen
{
    /// <summary>
    /// Minimal non-default <see cref="IEnvironmentConfigProvider"/> so MetaplayCore initialization succeeds during
    /// serializer generation. The SDK's file-based <c>DefaultEnvironmentConfigProvider</c> lives in the same assembly
    /// as the interface, so it is treated as a "default" integration and dropped in favour of this user-assembly type
    /// (see IntegrationRegistry). Serializer generation never reads environment values, so a single dummy
    /// (offline) environment is enough — it just has to initialize without trying to read a config file from disk.
    /// </summary>
    public class SerializerGenEnvironmentConfigProvider : IEnvironmentConfigProvider
    {
        static readonly EnvironmentConfig _config = new()
        {
            Id = "serializergen",
            DisplayName = "Serializer Generation",
            ConnectionEndpointConfig = new()
            {
                ServerHost = "",
                ServerPort = 0,
                BackupGateways = [],
            },
        };

        public void InitializeSingleton() { }

        public EnvironmentConfig GetCurrent() => _config;
    }
}
