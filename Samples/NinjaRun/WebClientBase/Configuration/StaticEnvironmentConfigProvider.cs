using System;
using System.Linq;
using System.Net;
using Metaplay.Core;
using Metaplay.Core.Client;
using Metaplay.Core.Network;

namespace WebClientBase.Configuration;

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
        new()
        {
            Id = "cloud",
            DisplayName = "Cloud",
            // Server host / TLS / CDN are filled in at runtime from the page host by
            // ConfigureActiveEnvironmentFromPageHost. Port 9380 is the WebSocket gateway (wss in cloud).
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
            Id = "offline",
            DisplayName = "Offline",
            // An empty ServerHost is what the SDK reads as offline mode (ConnectionEndpointConfig.IsOfflineMode):
            // the session runs against the in-process BlazorOfflineServer, so the ports and CDN go unused.
            // Never auto-detected, reachable only via an explicit override, e.g. "?env=offline".
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
    /// Rewrite the "localhost" environment so its server and CDN hosts match the host the page was
    /// served from. This makes LAN access work: when the page is loaded from http://&lt;lan-ip&gt;:5290,
    /// the client connects to &lt;lan-ip&gt; instead of "localhost", which, in a remote browser, would
    /// point back at that device. A no-op when the page is served from "localhost".
    /// </summary>
    /// <param name="pageBaseAddress">The page's base address (e.g. from HostEnvironment.BaseAddress).</param>
    public static void UsePageHostForLocalEnvironment(string pageBaseAddress)
    {
        if (string.IsNullOrEmpty(pageBaseAddress) || !Uri.TryCreate(pageBaseAddress, UriKind.Absolute, out Uri? pageUri))
            return;

        string pageHost = pageUri.Host;

        foreach (EnvironmentConfig env in Environments)
        {
            if (env.Id != "localhost")
                continue;

            ConnectionEndpointConfig endpoint = env.ConnectionEndpointConfig;

            // Point the server connection at the same host the page was served from.
            endpoint.ServerHost = pageHost;

            // Rebuild the CDN URL with the page host, preserving its scheme/port/path.
            if (!string.IsNullOrEmpty(endpoint.CdnBaseUrl) && Uri.TryCreate(endpoint.CdnBaseUrl, UriKind.Absolute, out Uri? cdnUri))
                endpoint.CdnBaseUrl = new UriBuilder(cdnUri) { Host = pageHost }.Uri.ToString();
        }
    }

    /// <summary>
    /// Choose and configure the active environment from the host the page was served from, and return its id:
    /// <list type="bullet">
    /// <item><c>localhost</c> or a bare IP (LAN dev) → the "localhost" environment, with its server/CDN host
    /// pointed at the page host (see <see cref="UsePageHostForLocalEnvironment"/>).</item>
    /// <item>any real domain (a cloud deployment) → the "cloud" environment, connecting over <c>wss</c> to the
    /// game server. The WASM client is served from the PublicWebApi host <c>&lt;env&gt;-public.&lt;domain&gt;</c>;
    /// the game server's client gateway is <c>&lt;env&gt;.&lt;domain&gt;</c> and the asset CDN is
    /// <c>&lt;env&gt;-assets.&lt;domain&gt;</c>. TLS follows the page scheme.</item>
    /// </list>
    /// </summary>
    /// <param name="pageBaseAddress">The page's base address (e.g. from HostEnvironment.BaseAddress).</param>
    public static string ConfigureActiveEnvironmentFromPageHost(string pageBaseAddress)
    {
        if (string.IsNullOrEmpty(pageBaseAddress) || !Uri.TryCreate(pageBaseAddress, UriKind.Absolute, out Uri? pageUri))
            return "localhost";

        string pageHost = pageUri.Host;

        // localhost or a bare IP (LAN dev) → local environment, rewritten to the page host.
        if (pageHost == "localhost" || IPAddress.TryParse(pageHost, out IPAddress? _))
        {
            UsePageHostForLocalEnvironment(pageBaseAddress);
            return "localhost";
        }

        // A real domain → cloud deployment. Cloud is always served over TLS, so the game connection
        // uses wss and the CDN/PublicWebApi use https. Derive the cloud hosts from the page (PublicWebApi) host.
        int dot = pageHost.IndexOf('.');
        string firstLabel = dot < 0 ? pageHost : pageHost[..dot];
        string domainRest = dot < 0 ? "" : pageHost[dot..];

        // The client is served from "<env>-public"; the game server gateway is the bare "<env>".
        const string publicSuffix = "-public";
        string baseLabel = firstLabel.EndsWith(publicSuffix, StringComparison.Ordinal)
            ? firstLabel[..^publicSuffix.Length]
            : firstLabel;

        foreach (EnvironmentConfig env in Environments)
        {
            if (env.Id != "cloud")
                continue;

            ConnectionEndpointConfig endpoint = env.ConnectionEndpointConfig;
            endpoint.ServerHost      = baseLabel + domainRest;
            endpoint.EnableTls       = true;                                   // cloud → wss
            endpoint.CdnBaseUrl      = $"https://{baseLabel}-assets{domainRest}/";
            endpoint.PublicWebApiUrl = $"https://{pageHost}/";
        }

        return "cloud";
    }

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
