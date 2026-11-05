using Metaplay.Core;
using Metaplay.Core.Model;
using Metaplay.Core.Player;
using Metaplay.Core.Session;

namespace WebClientBase.Services;

/// <summary>
/// Non-generic interface providing connection state for components that don't need typed PlayerModel access.
/// </summary>
public interface IMetaplayConnectionService
{
    /// <summary>
    /// Event fired when the connection status changes, and while connected on every frame, or for a service that
    /// renders only on a change, on a frame that requested a render.
    /// </summary>
    event Action? OnStateChanged;

    /// <summary>
    /// Current connection state.
    /// </summary>
    ConnectionStatus ConnectionStatus { get; }

    /// <summary>
    /// The connection loss waiting for the player to reconnect, for the connection overlay to show; null while
    /// connected or connecting.
    /// </summary>
    ConnectionLostEvent? ConnectionLost { get; }

    /// <summary>
    /// Player ID if connected, otherwise null.
    /// </summary>
    EntityId? PlayerId { get; }

    /// <summary>
    /// Whether the session is running but the SDK reports its connection as unhealthy: the connection is degraded and
    /// the SDK tries to restore it.
    /// </summary>
    bool IsUnhealthy { get; }

    /// <summary>
    /// Start connecting to the Metaplay server, and keep the connection up until <see cref="Reset"/>. Later calls do
    /// nothing.
    /// </summary>
    void Start();

    /// <summary>
    /// Reconnect after the loss in <see cref="ConnectionLost"/>, at the player's request.
    /// </summary>
    void Reconnect();

    /// <summary>
    /// Reset the client: disconnect, and delete the credentials and the offline player. The service stays stopped,
    /// so reload the page afterwards to start as a new player. Returns true if reset was successful.
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

    /// <summary>
    /// Execute a player action. Returns its result, or null if there is no session or the action threw.
    /// </summary>
    MetaActionResult? ExecuteAction(PlayerActionBase action);
}
