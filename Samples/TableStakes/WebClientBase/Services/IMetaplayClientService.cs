using Metaplay.Core;
using Metaplay.Core.Model;
using Metaplay.Core.Player;

namespace WebClientBase.Services;

/// <summary>
/// The connection state and controls, for components that do not need the typed player model.
/// </summary>
public interface IMetaplayConnectionService
{
    /// <summary>
    /// Raised when the connection state or the player model changes.
    /// </summary>
    event Action? OnStateChanged;

    /// <summary>
    /// The current connection state.
    /// </summary>
    ConnectionStatus ConnectionStatus { get; }

    /// <summary>
    /// The error message from the last failed connection, or null.
    /// </summary>
    string? ErrorMessage { get; }

    /// <summary>
    /// The player ID when connected, otherwise null.
    /// </summary>
    EntityId? PlayerId { get; }

    /// <summary>
    /// What is wrong with the connection, for the connection shell to render. <see cref="ConnectionTrouble.None"/>
    /// while the connection works and before the first session starts, so the screens show their own connecting
    /// state at startup.
    /// </summary>
    ConnectionTrouble Trouble { get; }

    /// <summary>
    /// Reconnects now: skips the current backoff delay, or restarts the reconnect loop if it has stopped. Can be
    /// called at any time.
    /// </summary>
    void RetryNow();

    /// <summary>
    /// Tells the client whether its page is visible. A browser throttles the timers of a hidden tab so much that
    /// the connection can drop, so a loss around a tab switch is shown as a pill instead of an outage
    /// (<see cref="ConnectionTrouble.IsQuiet"/>).
    /// </summary>
    void NotifyPageVisibilityChanged(bool isVisible);

    /// <summary>
    /// Initializes the client and connects to the Metaplay server.
    /// </summary>
    Task ConnectAsync();

    /// <summary>
    /// Resets the client: disconnects, deletes the credentials and resets the state.
    /// Returns true if the reset succeeded.
    /// </summary>
    bool Reset();
}

/// <summary>
/// Interface for Metaplay client services that manage player connection and state.
/// </summary>
/// <typeparam name="TPlayerModel">The game-specific PlayerModel type.</typeparam>
public interface IMetaplayClientService<TPlayerModel> : IMetaplayConnectionService
    where TPlayerModel : IPlayerModelBase
{
    /// <summary>
    /// The current player model, or null if not connected.
    /// </summary>
    TPlayerModel? PlayerModel { get; }
}
