using System;

namespace WebClient.Tests;

/// <summary>
/// Reads the ports of the stack under test from the environment, for stacks not on the default ports.
/// <para>
/// Only one stack per machine can bind the default ports. <c>tools/run-e2e.sh</c> starts a stack on free ports
/// and passes them in environment variables. With no variables set, the suite tests the stack on the default ports.
/// </para>
/// </summary>
internal static class E2EStackPorts
{
    /// <summary>Returns the trimmed value of environment variable <paramref name="name"/>, or null when it is unset or blank.</summary>
    public static string? ReadEnvironmentVariable(string name)
    {
        string? value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    /// <summary>
    /// The query parameters that point the client at the game server under test, or an empty string when the server
    /// is on its default ports.
    /// </summary>
    public static readonly string EndpointQuery = BuildEndpointQuery();

    private static string BuildEndpointQuery()
    {
        string? webSocketPort = ReadEnvironmentVariable("TABLESTAKES_E2E_WS_PORT");
        string? cdnPort       = ReadEnvironmentVariable("TABLESTAKES_E2E_CDN_PORT");

        if (webSocketPort == null && cdnPort == null)
            return "";

        // Require both ports or neither. A client using an isolated server with the default CDN would load another
        // stack's game config, and the failures would look like broken features.
        if (webSocketPort == null || cdnPort == null)
            throw new InvalidOperationException(
                "TABLESTAKES_E2E_WS_PORT and TABLESTAKES_E2E_CDN_PORT must be set together: a client pointed at " +
                "one stack's game server and another's asset CDN cannot load the config it is served.");

        return $"wsPort={webSocketPort}&cdnPort={cdnPort}";
    }
}
