namespace WebClientBase.Services;

/// <summary>
/// The connection state of the Metaplay client.
/// </summary>
public enum ConnectionStatus
{
    Disconnected,
    Connecting,
    Connected,
    Error
}
