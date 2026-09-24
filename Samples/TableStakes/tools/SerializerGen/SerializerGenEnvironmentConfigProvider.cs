using Metaplay.Core.Client;
using Metaplay.Core.Network;

namespace Game.WebAssemblySerializerGen
{
    /// <summary>
    /// Placeholder <see cref="IEnvironmentConfigProvider"/> that lets MetaplayCore initialize during serializer
    /// generation. The SDK's <c>DefaultEnvironmentConfigProvider</c> reads a config file from disk. The
    /// IntegrationRegistry uses this type in its place, because it prefers an implementation from a game assembly
    /// over the SDK's default. Serializer generation reads no environment values, so one empty offline
    /// environment is enough.
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
