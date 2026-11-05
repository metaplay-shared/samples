using WebClientBase.Services;

namespace WebClient.Services;

/// <summary> The short connection status the game's own chrome shows: the metagame's top bar and the board's pill. </summary>
public static class ConnectionStatusText
{
    public static string Describe(IMetaplayConnectionService connection)
    {
        // The session is up but its link is degraded, and the SDK is trying to restore it.
        if (connection.IsUnhealthy)
            return "Unhealthy";

        return connection.ConnectionStatus switch
        {
            ConnectionStatus.Connected  => "Connected",
            ConnectionStatus.Connecting => "Connecting…",
            ConnectionStatus.Error      => "Connection lost",
            _                           => "Offline",
        };
    }
}
