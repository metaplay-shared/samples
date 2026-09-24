using Metaplay.Core;
using Metaplay.Core.Message;
using Metaplay.Core.Session;
using Metaplay.Core.Session.ConnectionStates;

namespace Game.ClientBase.Services;

/// <summary>
/// A replicated entity's timeline diverged from the server's mid-session: an update did not hash to what the
/// server said applying it would produce, so this client's copy of that entity cannot be trusted.
/// <para>
/// It exists because the SDK has no error of its own for this. Its default handler closes the connection
/// with a generic <c>TerminalError.Unknown</c>, which is wrong twice over: a shell classifying it reports an
/// unreachable server, and a terminal error stops the reconnect that is the actual cure. This is a
/// <see cref="TransientError"/> — the ordinary reconnect loop runs, and it fetches a fresh copy of the
/// model.
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
/// Why the app shell has taken input away from the app. These are not the same event and they do not read the
/// same way to a player, so the shell keeps them apart rather than showing one "connection error".
/// </summary>
public enum ConnectionTroubleKind
{
    /// <summary>The link is healthy; the shell shows nothing.</summary>
    None,

    /// <summary>The link is down and the client is working through its retry loop, or has run out of retries.</summary>
    LinkLost,

    /// <summary>
    /// The replicated model diverged from the server's. The SDK drops what it had and pulls fresh state, so this
    /// is recoverable and reads as re-syncing rather than as a network fault.
    /// </summary>
    Desync,

    /// <summary>
    /// The session was force-terminated because another connection logged in as the same player — in a browser,
    /// a second tab on the same profile. Retrying here would kick the newer tab, whose own reconnect would kick
    /// this one back, so this tab parks instead.
    /// </summary>
    SessionSuperseded,
}

/// <summary>
/// What the app shell knows about a connection that is not working. Produced by
/// <see cref="IMetaplayConnectionService.Trouble"/> and rendered by the shell; no screen reads it.
/// </summary>
public sealed class ConnectionTrouble
{
    /// <summary>The link is healthy.</summary>
    public static readonly ConnectionTrouble None = new ConnectionTrouble(ConnectionTroubleKind.None, isRetrying: false, isQuiet: false, attempt: 0, maxAttempts: 0);

    /// <summary>Which of the four cases this is.</summary>
    public ConnectionTroubleKind Kind { get; }

    /// <summary>Whether the client is still trying by itself. When false, the only way on is the player's retry.</summary>
    public bool IsRetrying { get; }

    /// <summary>
    /// Whether this is quiet enough to show as a pill rather than as a modal. It is true only while the tab is
    /// away or has just come back, where a lapsed link is more likely a throttled update pump than an outage.
    /// </summary>
    public bool IsQuiet { get; }

    /// <summary>Which reconnect attempt is in flight, counting from one. Zero before the first one.</summary>
    public int Attempt { get; }

    /// <summary>How many reconnect attempts the client makes before it stops and asks the player.</summary>
    public int MaxAttempts { get; }

    public ConnectionTrouble(ConnectionTroubleKind kind, bool isRetrying, bool isQuiet, int attempt, int maxAttempts)
    {
        Kind = kind;
        IsRetrying = isRetrying;
        IsQuiet = isQuiet;
        Attempt = attempt;
        MaxAttempts = maxAttempts;
    }
}

/// <summary>
/// The pure half of the shell's connection handling: which of the four cases a connection loss is, and whether
/// the client may retry it. Everything here is a function of the SDK's own error classification, so it can be
/// reasoned about — and eventually tested — without a browser or a server.
/// </summary>
public static class ConnectionTroublePolicy
{
    /// <summary>
    /// Classify a connection loss. The SDK reports a second tab's kick and a model desync as transient errors
    /// alongside ordinary socket failures, so the technical error is what separates them. A loss with no
    /// technical error at all is a lost link: it is the honest answer, and it is the retryable one.
    /// </summary>
    public static ConnectionTroubleKind Classify(ConnectionLostEvent? connectionLost) =>
        Classify(connectionLost?.TechnicalError);

    /// <inheritdoc cref="Classify(ConnectionLostEvent)"/>
    public static ConnectionTroubleKind Classify(ConnectionState? technicalError)
    {
        // A newer connection logging in as the same player. Metaplay's server sends this from
        // SessionActorBase when a second session starts or resumes for a player that already has one.
        if (technicalError is TransientError.SessionForceTerminated forceTerminated
            && forceTerminated.Reason is SessionForceTerminateReason.ReceivedAnotherConnection)
            return ConnectionTroubleKind.SessionSuperseded;

        // The replicated model diverged. Three ways in, and all three are the same event to a player: the
        // player timeline caught a mismatch mid-session, the state handed over at subscribe did not hash to
        // what the server said it would, or a multiplayer entity's timeline update did not apply — which is
        // what the match's per-operation checksums exist to catch, and the SDK has no error of its own for.
        if (technicalError is TransientError.PlayerChecksumMismatchConnectionError
            or TransientError.InitialChecksumMismatchConnectionError
            or EntityTimelineDesyncConnectionError)
            return ConnectionTroubleKind.Desync;

        return ConnectionTroubleKind.LinkLost;
    }

    /// <summary>
    /// Whether the client may reconnect on its own after a loss of this kind. A superseded session is the one
    /// case where it may not: two tabs that both reconnect kick each other for as long as they are both open.
    /// </summary>
    public static bool MayRetryAutomatically(ConnectionTroubleKind kind) =>
        kind != ConnectionTroubleKind.SessionSuperseded;
}
