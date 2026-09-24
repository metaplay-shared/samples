using Metaplay.Core;
using Metaplay.Core.Message;
using Metaplay.Core.Session;
using Metaplay.Core.Session.ConnectionStates;

namespace WebClientBase.Services;

/// <summary>
/// A replicated entity's timeline diverged from the server's during a session: after applying an update, the
/// client's checksum did not match the server's, so the client's copy of that entity is wrong.
/// <para>
/// The SDK has no error type for this. Its default handler closes the connection with
/// <c>TerminalError.Unknown</c>, which the shell would show as an unreachable server and which stops the
/// reconnect. This error is a <see cref="TransientError"/>, so the reconnect loop runs and the new session
/// fetches a fresh copy of the model.
/// </para>
/// </summary>
public sealed class EntityTimelineDesyncConnectionError : TransientError
{
    /// <summary>The entity whose timeline diverged.</summary>
    public EntityId EntityId { get; }

    public EntityTimelineDesyncConnectionError(EntityId entityId)
    {
        EntityId = entityId;
    }

    public override string TryGetReasonOverrideForIncidentReport()
        => $"{nameof(EntityTimelineDesyncConnectionError)}\nEntityId: {EntityId}\n";
}

/// <summary>
/// Why the connection shell is covering the app. The shell shows different text for each kind instead of one
/// generic connection error.
/// </summary>
public enum ConnectionTroubleKind
{
    /// <summary>The connection is working. The shell shows nothing.</summary>
    None,

    /// <summary>The connection is lost, and the client is retrying or has run out of retries.</summary>
    LinkLost,

    /// <summary>
    /// The replicated model diverged from the server's. The reconnect fetches fresh state, so the shell shows
    /// this as re-syncing, not as a network fault.
    /// </summary>
    Desync,

    /// <summary>
    /// The server terminated the session because another connection logged in as the same player, for example
    /// a second browser tab. This tab does not reconnect on its own, because each reconnect would terminate the
    /// other tab's session and the two tabs would keep taking the session from each other.
    /// </summary>
    SessionSuperseded,
}

/// <summary>
/// The state of a connection that is not working, from <see cref="IMetaplayConnectionService.Trouble"/>. Only
/// the connection shell reads it.
/// </summary>
public sealed class ConnectionTrouble
{
    /// <summary>The connection is working.</summary>
    public static readonly ConnectionTrouble None = new ConnectionTrouble(ConnectionTroubleKind.None, isRetrying: false, isQuiet: false, attemptNumber: 0, maxAttempts: 0);

    /// <summary>The kind of connection trouble.</summary>
    public ConnectionTroubleKind Kind { get; }

    /// <summary>Whether the client is still retrying on its own. When false, only the player can retry.</summary>
    public bool IsRetrying { get; }

    /// <summary>
    /// Whether the shell shows a pill instead of the modal. True only while the tab is hidden or has just become
    /// visible, when a lost connection is more likely caused by browser timer throttling than by an outage.
    /// </summary>
    public bool IsQuiet { get; }

    /// <summary>The current reconnect attempt, counting from one. Zero before the first attempt.</summary>
    public int AttemptNumber { get; }

    /// <summary>How many reconnect attempts the client makes before it stops and asks the player.</summary>
    public int MaxAttempts { get; }

    public ConnectionTrouble(ConnectionTroubleKind kind, bool isRetrying, bool isQuiet, int attemptNumber, int maxAttempts)
    {
        Kind = kind;
        IsRetrying = isRetrying;
        IsQuiet = isQuiet;
        AttemptNumber = attemptNumber;
        MaxAttempts = maxAttempts;
    }
}

/// <summary>
/// Classifies a connection loss into a <see cref="ConnectionTroubleKind"/> and decides whether the client may
/// retry it. The methods depend only on the SDK's error types, so they need no browser or server.
/// </summary>
public static class ConnectionTroublePolicy
{
    /// <summary>
    /// Classifies a connection loss. The SDK reports a second tab taking over the session and a model desync as
    /// transient errors, the same as socket failures, so the kind is decided by the technical error's type. A loss
    /// with no technical error is <see cref="ConnectionTroubleKind.LinkLost"/>, which is retryable.
    /// </summary>
    public static ConnectionTroubleKind Classify(ConnectionLostEvent? connectionLost) =>
        Classify(connectionLost?.TechnicalError);

    /// <inheritdoc cref="Classify(ConnectionLostEvent)"/>
    public static ConnectionTroubleKind Classify(Metaplay.Core.Session.ConnectionState? technicalError)
    {
        // A newer connection logged in as the same player. The server's SessionActorBase sends this when a second
        // session starts or resumes for a player that already has one.
        if (technicalError is TransientError.SessionForceTerminated forceTerminated
            && forceTerminated.Reason is SessionForceTerminateReason.ReceivedAnotherConnection)
            return ConnectionTroubleKind.SessionSuperseded;

        // The replicated model diverged. The player timeline found a checksum mismatch during the session, the
        // initial state at subscribe had the wrong checksum, or a multiplayer entity's timeline update produced
        // the wrong checksum (EntityTimelineDesyncConnectionError). The player sees the same thing for all of them.
        if (technicalError is TransientError.PlayerChecksumMismatchConnectionError
            or TransientError.InitialChecksumMismatchConnectionError
            or EntityTimelineDesyncConnectionError)
            return ConnectionTroubleKind.Desync;

        return ConnectionTroubleKind.LinkLost;
    }

    /// <summary>
    /// Whether the client may reconnect on its own after a loss of this kind. False only for
    /// <see cref="ConnectionTroubleKind.SessionSuperseded"/>.
    /// </summary>
    public static bool MayRetryAutomatically(ConnectionTroubleKind kind) =>
        kind != ConnectionTroubleKind.SessionSuperseded;

    /// <summary>
    /// Whether the client may reconnect on its own after this loss: the kind must be retryable and the technical
    /// error must not be terminal (<see cref="IsTerminal"/>).
    /// </summary>
    public static bool MayRetryAutomatically(ConnectionLostEvent? connectionLost) =>
        MayRetryAutomatically(Classify(connectionLost)) && !IsTerminal(connectionLost?.TechnicalError);

    /// <summary>
    /// Whether the error stops automatic reconnects. The SDK reports a maintenance break as
    /// <c>TerminalError.InMaintenance</c>, but this method returns false for it, so the client keeps retrying
    /// until the maintenance ends.
    /// </summary>
    public static bool IsTerminal(Metaplay.Core.Session.ConnectionState? technicalError)
    {
        if (technicalError is TerminalError.InMaintenance)
            return false;

        return technicalError is TerminalError;
    }
}
