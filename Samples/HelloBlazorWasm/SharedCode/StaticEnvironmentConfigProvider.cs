// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core;
using Metaplay.Core.Client;
using System;
using System.Linq;

namespace Game.Logic
{
    /// <summary>
    /// Environment config provider with statically-defined environments.
    /// Configure via static properties before MetaplayCore initialization.
    /// </summary>
    public class StaticEnvironmentConfigProvider : IEnvironmentConfigProvider
    {
        /// <summary>
        /// The active environment ID. Must match one of the IDs in <see cref="Environments"/>.
        /// </summary>
        public static string ActiveEnvironmentId { get; set; } = "localhost";

        /// <summary>
        /// Available environments. Override this before initialization to customize.
        /// </summary>
        public static EnvironmentConfig[] Environments { get; set; } =
        [
            new()
            {
                Id = "offline",
                DisplayName = "Offline Mode",
                ConnectionEndpointConfig = new()
                {
                    ServerHost = "",
                    ServerPort = 0,
                    BackupGateways = [],
                },
                ClientLoggingConfig = new() { LogLevel = LogLevel.Debug },
            },
            new()
            {
                Id = "localhost",
                DisplayName = "Local Development",
                ConnectionEndpointConfig = new()
                {
                    ServerHost = "localhost",
                    ServerPort = 9339,
                    ServerPortForWebSocket = 9380,
                    EnableTls = false,
                    CdnBaseUrl = "http://localhost:5552/",
                    BackupGateways = [],
                },
                ClientLoggingConfig = new() { LogLevel = LogLevel.Debug },
            },
        ];

        public void InitializeSingleton()
        {
            if (!Environments.Any(e => e.Id == ActiveEnvironmentId))
            {
                string available = string.Join(", ", Environments.Select(e => e.Id));
                throw new ArgumentException($"Unknown environment '{ActiveEnvironmentId}'. Available: {available}");
            }
        }

        public EnvironmentConfig GetCurrent() => Environments.First(e => e.Id == ActiveEnvironmentId);
    }
}
