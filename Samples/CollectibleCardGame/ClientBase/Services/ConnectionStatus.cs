namespace Game.ClientBase.Services;

/// <summary> Where the client's connection loop is. </summary>
public enum ConnectionStatus
{
    Disconnected,
    Connecting,
    Connected,

    /// <summary> The loop has stopped: the session was superseded, the error was terminal or retries ran out. </summary>
    Error,
}

public static class ConnectionStatusText
{
    /// <summary> The short status line the top bars show. </summary>
    public static string Describe(ConnectionStatus status) => status switch
    {
        ConnectionStatus.Connected  => "Connected",
        ConnectionStatus.Connecting => "Connecting…",
        ConnectionStatus.Error      => "Connection lost",
        _                           => "Offline",
    };
}
