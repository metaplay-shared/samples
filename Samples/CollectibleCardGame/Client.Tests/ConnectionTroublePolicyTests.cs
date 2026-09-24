using Metaplay.Core;
using Metaplay.Core.Message;
using Metaplay.Core.Session;
using Metaplay.Core.Session.ConnectionStates;
using Game.ClientBase.Services;

namespace Game.Client.Tests;

/// <summary>
/// The app shell's classification of a broken connection: which of the four cases the SDK's error is, and
/// whether the client may retry it on its own.
/// <para>
/// This is the one part of the shell that is pure, and it is where the failure modes are. Getting a case
/// wrong is not a wrong string: reading a second tab's kick as an ordinary drop makes two tabs kick each
/// other for as long as both are open, and reading a desync as an outage tells the player the server is
/// unreachable while stopping the reconnect that would fix it. None of that needs a browser to test.
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
        // The server force-terminates this session when a newer one starts for the same player. It arrives as
        // an ordinary transient error, so only the reason tells it apart from a socket that died.
        Assert.That(
            Classify(new TransientError.SessionForceTerminated(new SessionForceTerminateReason.ReceivedAnotherConnection())),
            Is.EqualTo(ConnectionTroubleKind.SessionSuperseded));
    }

    [Test]
    public void AnAdminKickIsNotASecondTab()
    {
        // The discriminator is the reason, not the error type. An admin kick is the same
        // SessionForceTerminated, and treating it as a second tab would park a player on a screen telling
        // them to use a tab that does not exist — with no retry, because a superseded session has none.
        Assert.That(
            Classify(new TransientError.SessionForceTerminated(new SessionForceTerminateReason.KickedByAdminAction())),
            Is.EqualTo(ConnectionTroubleKind.LinkLost));
    }

    [TestCaseSource(nameof(ForceTerminateReasonsThatAreNotASecondTab))]
    public void EveryOtherForceTerminateReasonStaysAnOrdinaryRetryableLoss(SessionForceTerminateReason reason)
    {
        // The rest of the family, named one by one. Every one of them is something the client should keep
        // trying through: a node going away, a maintenance break starting, a config or logic version moving
        // on. Widening the second-tab case to the error type would strand a player on all of them.
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

    [Test]
    public void AMidSessionPlayerChecksumMismatchIsADesync()
    {
        Assert.That(
            Classify(new TransientError.PlayerChecksumMismatchConnectionError(tickNumber: 7, action: null, modelDiff: "diff", vagueDifferencePathsMaybe: null)),
            Is.EqualTo(ConnectionTroubleKind.Desync));
    }

    [Test]
    public void ASubscribeTimeChecksumMismatchIsADesync()
    {
        // Both flavours the SDK has: the player's own initial state, and a multiplayer entity's.
        Assert.That(Classify(new TransientError.PlayerInitialChecksumMismatchConnectionError("details")), Is.EqualTo(ConnectionTroubleKind.Desync));
        Assert.That(Classify(new TransientError.EntityInitialChecksumMismatchConnectionError(EntityId.None, "details")), Is.EqualTo(ConnectionTroubleKind.Desync));
    }

    [Test]
    public void AMidSessionMatchTimelineDesyncIsADesync()
    {
        // The one the match's per-operation checksums exist to catch, and the one the SDK has no error for:
        // its own handler closes the connection with a generic TerminalError.Unknown, which reads as "can't
        // reach the server" and — being terminal — stops the reconnect that fetches the fresh copy. This is
        // the error the game raises in its place, and it has to land here.
        Assert.That(Classify(new EntityTimelineDesyncConnectionError(EntityId.None)), Is.EqualTo(ConnectionTroubleKind.Desync));
    }

    [Test]
    public void ADesyncIsRetriedAutomatically()
    {
        // Reconnecting is the cure, not a hope: the fresh state the new session hands over is what replaces
        // the copy that diverged.
        Assert.That(ConnectionTroublePolicy.MayRetryAutomatically(ConnectionTroubleKind.Desync), Is.True);
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
        // The default has to be the retryable one. A case nothing recognizes is a case the client should keep
        // working on, not one it parks the player on.
        Assert.That(ConnectionTroublePolicy.Classify((ConnectionLostEvent?)null), Is.EqualTo(ConnectionTroubleKind.LinkLost));
        Assert.That(ConnectionTroublePolicy.Classify((Metaplay.Core.Session.ConnectionState?)null), Is.EqualTo(ConnectionTroubleKind.LinkLost));
    }

    #endregion

    #region The retry veto

    [Test]
    public void OnlyASupersededSessionVetoesTheAutomaticRetry()
    {
        // The rule whose failure mode is two tabs kicking each other forever: this tab reconnects, which
        // terminates the newer tab's session, whose own reconnect terminates this one, for as long as both
        // are open. It is the only case where the client must stop and ask.
        Assert.That(ConnectionTroublePolicy.MayRetryAutomatically(ConnectionTroubleKind.SessionSuperseded), Is.False);

        Assert.That(ConnectionTroublePolicy.MayRetryAutomatically(ConnectionTroubleKind.None), Is.True);
        Assert.That(ConnectionTroublePolicy.MayRetryAutomatically(ConnectionTroubleKind.LinkLost), Is.True);
        Assert.That(ConnectionTroublePolicy.MayRetryAutomatically(ConnectionTroubleKind.Desync), Is.True);
    }

    #endregion
}
