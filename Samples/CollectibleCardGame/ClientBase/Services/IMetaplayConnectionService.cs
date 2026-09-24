using Metaplay.Core;

namespace Game.ClientBase.Services;

/// <summary>
/// The connection surface ClientBase's components read, without knowing the game's player model.
/// </summary>
public interface IMetaplayConnectionService
{
    /// <summary>
    /// Event fired when connection state or player model changes.
    /// </summary>
    event Action? OnStateChanged;

    /// <summary>
    /// Where the connection loop is.
    /// </summary>
    ConnectionStatus ConnectionStatus { get; }

    /// <summary>
    /// Why the connection loop stopped, or null while it is running.
    /// </summary>
    string? ErrorMessage { get; }

    /// <summary>
    /// Player ID if connected, otherwise null.
    /// </summary>
    EntityId? PlayerId { get; }

    /// <summary>
    /// What is wrong with the connection, for the app shell to render over the whole app.
    /// <see cref="ConnectionTrouble.None"/> while the link is healthy and while a cold start is still trying, so
    /// that is left to the screen's own connecting state.
    /// </summary>
    ConnectionTrouble Trouble { get; }

    /// <summary>
    /// Reconnect now: skip whatever backoff delay is being waited out, and start the connection loop again if
    /// it has given up. Safe to call at any time.
    /// </summary>
    void RetryNow();

    /// <summary>
    /// Tell the client whether its page is on screen. A browser throttles a hidden tab's update pump hard
    /// enough to lapse the link through nobody's fault, so the client forgives a loss around a tab switch
    /// rather than reporting it as an outage.
    /// </summary>
    void NotifyPageVisibilityChanged(bool isVisible);

    /// <summary>
    /// Reset the client: disconnect and delete the credentials and the offline state. The caller reloads the page,
    /// which comes back up as a new player. Returns true if the reset succeeded.
    /// </summary>
    bool Reset();
}
