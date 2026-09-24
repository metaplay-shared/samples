using Metaplay.Core;
using Metaplay.Core.Message;
using Metaplay.Core.Session;
using Metaplay.Core.Session.ConnectionStates;
using WebClientBase.Services;

namespace WebClient.Tests;

/// <summary>
/// Tests for <see cref="ConnectionTroublePolicy"/>, which maps an SDK connection error to a
/// <see cref="ConnectionTroubleKind"/> and decides whether the client may reconnect automatically.
/// <para>
/// A wrong classification changes behavior, not only the message. Treating a second tab's session takeover as an
/// ordinary drop makes the two tabs take the session from each other for as long as both are open. Treating a
/// desync as an outage tells the player the server is unreachable and stops the reconnect that would fix it.
/// </para>
/// </summary>
[TestFixture]
public class ConnectionTroublePolicyTests
{
    static ConnectionTroubleKind Classify(Metaplay.Core.Session.ConnectionState technicalError)
        => ConnectionTroublePolicy.Classify(technicalError);

    #region A second tab

    [Test]
    public void ASecondTabOnTheSameProfileIsItsOwnCase()
    {
        // The server force-terminates this session when a newer one starts for the same player. The error is an
        // ordinary transient error, so only the reason tells it apart from a dropped socket.
        Assert.That(
            Classify(new TransientError.SessionForceTerminated(new SessionForceTerminateReason.ReceivedAnotherConnection())),
            Is.EqualTo(ConnectionTroubleKind.SessionSuperseded));
    }

    [TestCaseSource(nameof(ForceTerminateReasonsThatAreNotASecondTab))]
    public void EveryOtherForceTerminateReasonStaysAnOrdinaryRetryableLoss(SessionForceTerminateReason reason)
    {
        // Every other reason, such as an admin kick, a lost server node, maintenance starting, or a config or logic
        // version update, must stay retryable. Classifying by error type instead of reason would stop retries for
        // all of them, and would show an admin kick a screen that tells the player to use another tab.
        ConnectionTroubleKind kind = Classify(new TransientError.SessionForceTerminated(reason));

        Assert.That(kind, Is.EqualTo(ConnectionTroubleKind.LinkLost), $"{reason.GetType().Name} was read as something other than a lost link");
        Assert.That(ConnectionTroublePolicy.MayRetryAutomatically(kind), Is.True, $"{reason.GetType().Name} left the client unable to retry");
    }

    static IEnumerable<SessionForceTerminateReason> ForceTerminateReasonsThatAreNotASecondTab()
    {
        yield return new SessionForceTerminateReason.KickedByAdminAction();
        yield return new SessionForceTerminateReason.InternalServerError();
        yield return new SessionForceTerminateReason.Unknown();
        yield return new SessionForceTerminateReason.ClientTimeTooFarBehind();
        yield return new SessionForceTerminateReason.ClientTimeTooFarAhead();
        yield return new SessionForceTerminateReason.SessionTooLong();
        yield return new SessionForceTerminateReason.MaintenanceModeStarted();
        yield return new SessionForceTerminateReason.PauseDeadlineExceeded();
        yield return new SessionForceTerminateReason.GameConfigUpdated();
        yield return new SessionForceTerminateReason.LogicVersionUpdated();
        yield return new SessionForceTerminateReason.DirectConnectionLost();
        yield return new SessionForceTerminateReason.ServerNodeLost();
        yield return new SessionForceTerminateReason.ClientTimeout();
    }

    #endregion

    #region A desync

    [TestCaseSource(nameof(Desyncs))]
    public void EveryDesyncErrorIsADesync(Metaplay.Core.Session.ConnectionState technicalError)
    {
        Assert.That(Classify(technicalError), Is.EqualTo(ConnectionTroubleKind.Desync), $"{technicalError.GetType().Name} was read as something other than a desync");
    }

    static IEnumerable<Metaplay.Core.Session.ConnectionState> Desyncs()
    {
        yield return new TransientError.PlayerChecksumMismatchConnectionError(tickNumber: 7, action: null, modelDiff: "diff", vagueDifferencePathsMaybe: null);

        // The SDK has two initial checksum errors: one for the player's state and one for a multiplayer entity's.
        yield return new TransientError.PlayerInitialChecksumMismatchConnectionError("details");
        yield return new TransientError.EntityInitialChecksumMismatchConnectionError(EntityId.None, "details");

        // The SDK has no error type for a match timeline desync. Its handler closes the connection with
        // TerminalError.Unknown, which is terminal and so stops the reconnect that would fetch fresh state. The
        // game raises EntityTimelineDesyncConnectionError instead, and it must classify as a desync.
        yield return new EntityTimelineDesyncConnectionError(EntityId.None);
    }

    #endregion

    #region Everything else

    [TestCaseSource(nameof(OrdinaryLosses))]
    public void AnOrdinaryTransportFailureIsALostLink(Metaplay.Core.Session.ConnectionState technicalError)
    {
        Assert.That(Classify(technicalError), Is.EqualTo(ConnectionTroubleKind.LinkLost), $"{technicalError.GetType().Name} was read as something other than a lost link");
    }

    static IEnumerable<Metaplay.Core.Session.ConnectionState> OrdinaryLosses()
    {
        yield return new TransientError.Closed();
        yield return new TransientError.Timeout(TransientError.Timeout.TimeoutSource.Stream);
        yield return new TransientError.ClusterNotReady(TransientError.ClusterNotReady.ClusterStatus.ClusterStarting);
        yield return new TransientError.SessionLostInBackground();
        yield return new TransientError.ConfigFetchFailed(new System.Exception("boom"), TransientError.ConfigFetchFailed.FailureSource.ResourceFetch);
        yield return new TerminalError.Unknown();
    }

    [Test]
    public void ALossWithNoTechnicalErrorAtAllIsALostLink()
    {
        // An unrecognized case must default to the retryable kind, so the client keeps reconnecting.
        Assert.That(ConnectionTroublePolicy.Classify((ConnectionLostEvent?)null), Is.EqualTo(ConnectionTroubleKind.LinkLost));
        Assert.That(ConnectionTroublePolicy.Classify((Metaplay.Core.Session.ConnectionState?)null), Is.EqualTo(ConnectionTroubleKind.LinkLost));
    }

    #endregion

    #region The retry veto

    [Test]
    public void OnlyASupersededSessionVetoesTheAutomaticRetry()
    {
        // If a superseded session reconnected automatically, it would terminate the newer tab's session, which
        // would then reconnect and terminate this one, for as long as both tabs are open. So this is the only
        // kind where the client waits for the player. Reconnecting fixes a desync, because the new session delivers
        // fresh state that replaces the diverged copy.
        Assert.That(ConnectionTroublePolicy.MayRetryAutomatically(ConnectionTroubleKind.SessionSuperseded), Is.False);

        Assert.That(ConnectionTroublePolicy.MayRetryAutomatically(ConnectionTroubleKind.None), Is.True);
        Assert.That(ConnectionTroublePolicy.MayRetryAutomatically(ConnectionTroubleKind.LinkLost), Is.True);
        Assert.That(ConnectionTroublePolicy.MayRetryAutomatically(ConnectionTroubleKind.Desync), Is.True);
    }

    #endregion
}
