using Metaplay.Core;

namespace WebClientBase.Services;

/// <summary>
/// Connection state for components that don't need typed PlayerModel access.
/// </summary>
public interface IMetaplayConnectionService
{
    /// <summary>
    /// Event fired when the connection state changes and when the player model changes; never on a frame that
    /// changed neither.
    /// </summary>
    event Action? OnStateChanged;

    /// <summary>
    /// Current connection state.
    /// </summary>
    ConnectionState ConnectionState { get; }

    /// <summary>
    /// Error message if connection failed.
    /// </summary>
    string? ErrorMessage { get; }

    /// <summary>
    /// Player ID if connected, otherwise null.
    /// </summary>
    EntityId? PlayerId { get; }

    /// <summary>
    /// Initialize and connect to the Metaplay server.
    /// </summary>
    Task ConnectAsync();

    /// <summary>
    /// Reset the client: disconnect, delete credentials, and reset state.
    /// Returns true if reset was successful.
    /// </summary>
    bool Reset();
}
