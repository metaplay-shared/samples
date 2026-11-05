namespace WebClientBase.Services;

/// <summary>
/// Connection state enumeration for Metaplay client connections.
/// </summary>
public enum ConnectionStatus
{
    Disconnected,
    Connecting,
    Connected,
    Error
}
