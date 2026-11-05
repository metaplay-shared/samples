namespace WebClient.Tests;

/// <summary>
/// Where the stack under test is. The E2E harness (<c>run_e2e.py</c>) starts its stack on free ports and names them in
/// the environment. With nothing set, the tests use a hand-started stack on the default ports.
/// </summary>
internal static class E2EStack
{
    /// <summary>The game server's WebSocket port.</summary>
    public static string WebSocketPort => Setting("E2E_WS_PORT") ?? "9380";

    /// <summary>The port of the game server's asset CDN, which serves the game config.</summary>
    public static string CdnPort => Setting("E2E_CDN_PORT") ?? "5552";

    /// <summary>The published client that <c>run_e2e.py --trimmed</c> serves, or null for the dev server.</summary>
    public static string? PublishedWwwroot => Setting("E2E_CLIENT_WWWROOT");

    /// <summary>
    /// The query that points the client at the harness's game server, or an empty string for the default ports, which
    /// the client's own configuration names.
    /// </summary>
    public static readonly string EndpointQuery = BuildEndpointQuery();

    /// <summary>One setting from the environment, or null when it is unset or blank.</summary>
    public static string? Setting(string name)
    {
        string? value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string BuildEndpointQuery()
    {
        string? webSocketPort = Setting("E2E_WS_PORT");
        string? cdnPort       = Setting("E2E_CDN_PORT");

        if (webSocketPort == null && cdnPort == null)
            return "";

        // Both or neither: a client pointed at one stack's game server and another's CDN downloads a game config that
        // does not match, which fails in a way that looks like a broken feature.
        if (webSocketPort == null || cdnPort == null)
            throw new InvalidOperationException("E2E_WS_PORT and E2E_CDN_PORT must be set together.");

        return $"wsPort={webSocketPort}&cdnPort={cdnPort}";
    }
}
