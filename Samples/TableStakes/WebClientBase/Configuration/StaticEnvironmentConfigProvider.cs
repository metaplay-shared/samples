using System;
using System.Linq;
using System.Net;
using Metaplay.Core;
using Metaplay.Core.Client;
using Metaplay.Core.Network;

namespace WebClientBase.Configuration;

/// <summary>
/// Environment config provider with statically defined environments. Set the static properties before
/// MetaplayCore is initialized.
/// </summary>
public class StaticEnvironmentConfigProvider : IEnvironmentConfigProvider
{
    /// <summary>The ID of the local development environment.</summary>
    public const string LocalId = "localhost";

    /// <summary>The ID of the cloud environment, configured from the page host.</summary>
    public const string CloudId = "cloud";

    /// <summary>The ID of the offline environment, which runs against the in-process offline server.</summary>
    public const string OfflineId = "offline";

    /// <summary>
    /// The active environment ID. Must match one of the IDs in <see cref="Environments"/>.
    /// </summary>
    public static string ActiveEnvironmentId { get; set; } = LocalId;

    /// <summary>Whether the active environment is the offline one.</summary>
    public static bool IsOffline => ActiveEnvironmentId == OfflineId;

    /// <summary>
    /// The available environments. Replace the array before initialization to change them.
    /// </summary>
    public static EnvironmentConfig[] Environments { get; set; } =
    [
        new()
        {
            Id = LocalId,
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
        new()
        {
            Id = CloudId,
            DisplayName = "Cloud",
            // ConfigureActiveEnvironmentFromPageHost sets the server host, TLS and CDN URL from the page host at
            // run time.
            ConnectionEndpointConfig = new()
            {
                ServerHost = "",
                ServerPort = 9339,
                ServerPortForWebSocket = 9380,
                EnableTls = true,
                BackupGateways = [],
            },
            ClientLoggingConfig = new() { LogLevel = LogLevel.Information },
        },
        new()
        {
            Id = OfflineId,
            DisplayName = "Offline",
            // The SDK treats an empty ServerHost as offline mode (ConnectionEndpointConfig.IsOfflineMode): the
            // session runs against the in-process BlazorOfflineServer, and the ports and CDN are not used.
            // ConfigureActiveEnvironmentFromPageHost never selects this environment. Select it with "?env=offline".
            ConnectionEndpointConfig = new()
            {
                ServerHost = "",
                ServerPort = 0,
                ServerPortForWebSocket = 0,
                EnableTls = false,
                BackupGateways = [],
            },
            ClientLoggingConfig = new() { LogLevel = LogLevel.Debug },
        },
    ];

    /// <summary>
    /// Sets the "localhost" environment's server and CDN hosts to the host the page was served from. When the
    /// page is loaded from another device on the LAN at http://&lt;lan-ip&gt;:&lt;port&gt;, the client then
    /// connects to &lt;lan-ip&gt;, because "localhost" on that device would point at the device itself. Changes
    /// nothing when the page is served from "localhost".
    /// </summary>
    /// <param name="pageBaseAddress">The page's base address, for example from HostEnvironment.BaseAddress.</param>
    public static void UsePageHostForLocalEnvironment(string pageBaseAddress)
    {
        if (string.IsNullOrEmpty(pageBaseAddress) || !Uri.TryCreate(pageBaseAddress, UriKind.Absolute, out Uri? pageUri))
            return;

        ConnectionEndpointConfig? endpoint = EndpointOf(LocalId);
        if (endpoint == null)
            return;

        string pageHost = pageUri.Host;
        endpoint.ServerHost = pageHost;

        // Replace only the host in the CDN URL and keep its scheme, port and path.
        if (!string.IsNullOrEmpty(endpoint.CdnBaseUrl) && Uri.TryCreate(endpoint.CdnBaseUrl, UriKind.Absolute, out Uri? cdnUri))
            endpoint.CdnBaseUrl = new UriBuilder(cdnUri) { Host = pageHost }.Uri.ToString();
    }

    /// <summary>
    /// Sets the "localhost" environment's WebSocket and CDN ports, replacing the defaults. The E2E harness
    /// (<c>tools/run-e2e.py</c>) starts a server on free ports for each worktree and passes the ports to the page
    /// as <c>?wsPort=</c> and <c>?cdnPort=</c>. The cloud environment is not changed, because a cloud deployment
    /// uses the standard ports.
    /// </summary>
    /// <param name="serverWebSocketPort">The game server's WebSocket port, or null to keep the default.</param>
    /// <param name="cdnPort">The asset CDN's port, or null to keep the default.</param>
    public static void UseEndpointPortsForLocalEnvironment(int? serverWebSocketPort, int? cdnPort)
    {
        if (serverWebSocketPort == null && cdnPort == null)
            return;

        ConnectionEndpointConfig? endpoint = EndpointOf(LocalId);
        if (endpoint == null)
            return;

        if (serverWebSocketPort != null)
            endpoint.ServerPortForWebSocket = serverWebSocketPort.Value;

        if (cdnPort != null && !string.IsNullOrEmpty(endpoint.CdnBaseUrl)
            && Uri.TryCreate(endpoint.CdnBaseUrl, UriKind.Absolute, out Uri? cdnUri))
            endpoint.CdnBaseUrl = new UriBuilder(cdnUri) { Port = cdnPort.Value }.Uri.ToString();
    }

    /// <summary>
    /// Chooses and configures the environment for the host the page was served from, and returns its ID. A page on
    /// <c>localhost</c> or an IP address gets "localhost" through <see cref="UsePageHostForLocalEnvironment"/>, and
    /// a non-absolute <paramref name="pageBaseAddress"/> gets "localhost" unchanged. Any other host gets "cloud",
    /// which always uses TLS. Its page host is the PublicWebApi host <c>&lt;env&gt;-public.&lt;domain&gt;</c>,
    /// the game server gateway is <c>&lt;env&gt;.&lt;domain&gt;</c>, and the CDN is
    /// <c>&lt;env&gt;-assets.&lt;domain&gt;</c>.
    /// </summary>
    /// <param name="pageBaseAddress">The page's base address, for example from HostEnvironment.BaseAddress.</param>
    public static string ConfigureActiveEnvironmentFromPageHost(string pageBaseAddress)
    {
        if (string.IsNullOrEmpty(pageBaseAddress) || !Uri.TryCreate(pageBaseAddress, UriKind.Absolute, out Uri? pageUri))
            return LocalId;

        string pageHost = pageUri.Host;

        if (pageHost == "localhost" || IPAddress.TryParse(pageHost, out IPAddress? _))
        {
            UsePageHostForLocalEnvironment(pageBaseAddress);
            return LocalId;
        }

        // A cloud deployment is always served over TLS, so the game connection uses wss and the CDN and
        // PublicWebApi use https. The cloud hosts are derived from the page host, which is the PublicWebApi host.
        int dot = pageHost.IndexOf('.');
        string firstLabel = dot < 0 ? pageHost : pageHost[..dot];
        string domainSuffix = dot < 0 ? "" : pageHost[dot..];

        // The client is served from "<env>-public" and the game server gateway is "<env>".
        const string publicSuffix = "-public";
        string envLabel = firstLabel.EndsWith(publicSuffix, StringComparison.Ordinal)
            ? firstLabel[..^publicSuffix.Length]
            : firstLabel;

        ConnectionEndpointConfig? endpoint = EndpointOf(CloudId);
        if (endpoint != null)
        {
            endpoint.ServerHost      = envLabel + domainSuffix;
            endpoint.EnableTls       = true;
            endpoint.CdnBaseUrl      = $"https://{envLabel}-assets{domainSuffix}/";
            endpoint.PublicWebApiUrl = $"https://{pageHost}/";
        }

        return CloudId;
    }

    /// <summary>The connection endpoint of the environment with this ID, or null if there is none.</summary>
    static ConnectionEndpointConfig? EndpointOf(string id) =>
        Environments.FirstOrDefault(env => env.Id == id)?.ConnectionEndpointConfig;

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
