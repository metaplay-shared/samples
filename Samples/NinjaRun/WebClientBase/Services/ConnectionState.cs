namespace WebClientBase.Services;

/// <summary>
/// Connection state enumeration for Metaplay client connections.
/// </summary>
public enum ConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Error
}
