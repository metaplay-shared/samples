using NUnit.Framework;

namespace Game.Logic.Tests
{
    /// <summary>
    /// Tests for <see cref="MatchmakingSearchPolicy"/>, the player side of matchmaking. The player's actor handles
    /// one message at a time, so exactly one of a cancel and a seat offer arrives first. These tests drive each
    /// order instead of racing them (<c>docs/matchmaking.md</c>, "Cancel" and "Timeouts").
    /// </summary>
    [TestFixture]
    public class MatchmakingSearchPolicyTests
    {
        /// <summary>A connected player who is not at a table.</summary>
        static MatchmakingSearchFacts Facts(bool isAtTable = false, bool hasSession = true, bool isClientConnected = true, bool isStillQueued = false)
            => new MatchmakingSearchFacts(isAtTable, hasSession, isClientConnected, isStillQueued);

        static readonly MatchmakingSearchFacts Here = Facts();

        static MatchmakingSearchStep Step(MatchmakingSearchPhase phase, MatchmakingSearchEvent ev) => MatchmakingSearchPolicy.Step(phase, ev, Here);

        static MatchmakingSearchStep Step(MatchmakingSearchPhase phase, MatchmakingSearchEvent ev, MatchmakingSearchFacts facts) => MatchmakingSearchPolicy.Step(phase, ev, facts);

        static void AssertNothing(MatchmakingSearchStep step, MatchmakingSearchPhase phase)
        {
            Assert.That(step.Phase, Is.EqualTo(phase));
            Assert.That(step.Timeout, Is.EqualTo(MatchmakingSearchTimeout.Unchanged));
            Assert.That(step.EndsUnseated, Is.False);
            Assert.That(step.ToQueue, Is.EqualTo(MatchmakingQueueMessage.None));
            Assert.That(step.Reply, Is.Null);
        }

        static void AssertEndedUnseated(MatchmakingSearchStep step)
        {
            Assert.That(step.Phase, Is.EqualTo(MatchmakingSearchPhase.Idle));
            Assert.That(step.Timeout, Is.EqualTo(MatchmakingSearchTimeout.Disarm), "an ended search's timeout must do nothing");
            Assert.That(step.EndsUnseated, Is.True, "every unseated end reports the wait");
        }

        #region Play

        [Test]
        public void PlayFromIdleStartsASearch()
        {
            MatchmakingSearchStep step = Step(MatchmakingSearchPhase.Idle, MatchmakingSearchEvent.PlayPressed);

            Assert.That(step.Phase, Is.EqualTo(MatchmakingSearchPhase.Searching));
            Assert.That(step.StartsSearch, Is.True);
            Assert.That(step.Timeout, Is.EqualTo(MatchmakingSearchTimeout.ArmSearch));
            Assert.That(step.ToQueue, Is.EqualTo(MatchmakingQueueMessage.EnterQueue));
            Assert.That(step.Reply, Is.EqualTo(MatchmakingStatus.Searching));
        }

        [TestCase(MatchmakingSearchPhase.Searching, MatchmakingStatus.Searching)]
        [TestCase(MatchmakingSearchPhase.Reserved, MatchmakingStatus.SeatReserved)]
        public void ARepeatedPlayIsAnsweredWithTheCurrentStatusAndQueuesNothing(MatchmakingSearchPhase phase, MatchmakingStatus expected)
        {
            MatchmakingSearchStep step = Step(phase, MatchmakingSearchEvent.PlayPressed);

            Assert.That(step.Phase, Is.EqualTo(phase));
            Assert.That(step.StartsSearch, Is.False, "the countdown already running is kept");
            Assert.That(step.Timeout, Is.EqualTo(MatchmakingSearchTimeout.Unchanged));
            Assert.That(step.ToQueue, Is.EqualTo(MatchmakingQueueMessage.None), "a double press must not queue the player twice");
            Assert.That(step.Reply, Is.EqualTo(expected));
        }

        [TestCase(MatchmakingSearchPhase.Idle)]
        [TestCase(MatchmakingSearchPhase.Searching)]
        public void PlayAtATableIsRefused(MatchmakingSearchPhase phase)
        {
            MatchmakingSearchStep step = Step(phase, MatchmakingSearchEvent.PlayPressed, Facts(isAtTable: true));

            Assert.That(step.Phase, Is.EqualTo(phase));
            Assert.That(step.ToQueue, Is.EqualTo(MatchmakingQueueMessage.None));
            Assert.That(step.Reply, Is.EqualTo(MatchmakingStatus.Unavailable));
        }

        #endregion

        #region Cancel versus seat offer

        [Test]
        public void CancelBeforeTheOfferLeavesTheQueueAndTheOfferIsDeclined()
        {
            MatchmakingSearchStep cancel = Step(MatchmakingSearchPhase.Searching, MatchmakingSearchEvent.CancelPressed);

            AssertEndedUnseated(cancel);
            Assert.That(cancel.ToQueue, Is.EqualTo(MatchmakingQueueMessage.LeaveQueue));
            Assert.That(cancel.Reply, Is.EqualTo(MatchmakingStatus.NotSearching));

            MatchmakingSearchStep offer = Step(cancel.Phase, MatchmakingSearchEvent.SeatOffered);

            Assert.That(offer.SeatAnswer, Is.EqualTo(SeatReservationAnswer.NotSearching));
            Assert.That(offer.Phase, Is.EqualTo(MatchmakingSearchPhase.Idle));
            Assert.That(offer.EndsUnseated, Is.False, "the cancel already reported the wait");
            Assert.That(offer.Reply, Is.Null, "the cancel was already answered");
        }

        [Test]
        public void AnOfferBeforeTheCancelCommitsTheSeatAndTheCancelIsRefused()
        {
            MatchmakingSearchStep offer = Step(MatchmakingSearchPhase.Searching, MatchmakingSearchEvent.SeatOffered);

            Assert.That(offer.SeatAnswer, Is.EqualTo(SeatReservationAnswer.Take));
            Assert.That(offer.Phase, Is.EqualTo(MatchmakingSearchPhase.Reserved), "the reservation is the commit point");
            Assert.That(offer.Timeout, Is.EqualTo(MatchmakingSearchTimeout.ArmReservation));
            Assert.That(offer.Reply, Is.EqualTo(MatchmakingStatus.SeatReserved));

            MatchmakingSearchStep cancel = Step(offer.Phase, MatchmakingSearchEvent.CancelPressed);

            Assert.That(cancel.Phase, Is.EqualTo(MatchmakingSearchPhase.Reserved));
            Assert.That(cancel.ToQueue, Is.EqualTo(MatchmakingQueueMessage.None));
            Assert.That(cancel.Reply, Is.EqualTo(MatchmakingStatus.SeatReserved), "the client keeps waiting for the table");
        }

        [Test]
        public void ACancelWhileIdleIsStillAnswered()
        {
            MatchmakingSearchStep step = Step(MatchmakingSearchPhase.Idle, MatchmakingSearchEvent.CancelPressed);

            Assert.That(step.Phase, Is.EqualTo(MatchmakingSearchPhase.Idle));
            Assert.That(step.EndsUnseated, Is.False);
            Assert.That(step.ToQueue, Is.EqualTo(MatchmakingQueueMessage.None));
            Assert.That(step.Reply, Is.EqualTo(MatchmakingStatus.NotSearching), "otherwise the client's dialog never closes");
        }

        [Test]
        public void ASecondOfferWhileReservedIsDeclinedAndKeepsTheFirstSeat()
        {
            // Formations run concurrently, so a player who re-queued can be offered a second seat while the first
            // is still committed.
            MatchmakingSearchStep step = Step(MatchmakingSearchPhase.Reserved, MatchmakingSearchEvent.SeatOffered);

            Assert.That(step.SeatAnswer, Is.EqualTo(SeatReservationAnswer.NotSearching));
            AssertNothing(step, MatchmakingSearchPhase.Reserved);
        }

        [Test]
        public void AnOfferToAPlayerAtATableIsDeclinedAndEndsTheSearch()
        {
            MatchmakingSearchStep step = Step(MatchmakingSearchPhase.Searching, MatchmakingSearchEvent.SeatOffered, Facts(isAtTable: true));

            Assert.That(step.SeatAnswer, Is.EqualTo(SeatReservationAnswer.AlreadyAtTable));
            AssertEndedUnseated(step);
            Assert.That(step.ToQueue, Is.EqualTo(MatchmakingQueueMessage.None), "the formation already took the queue entry");
        }

        [TestCase(false, true)]
        [TestCase(true, false)]
        public void AnOfferToAPlayerWithoutALiveConnectionIsDeclinedAndEndsTheSearch(bool hasSession, bool isClientConnected)
        {
            MatchmakingSearchStep step = Step(MatchmakingSearchPhase.Searching, MatchmakingSearchEvent.SeatOffered, Facts(hasSession: hasSession, isClientConnected: isClientConnected));

            Assert.That(step.SeatAnswer, Is.EqualTo(SeatReservationAnswer.NotLive));
            AssertEndedUnseated(step);
        }

        #endregion

        #region Seat assigned

        [TestCase(MatchmakingSearchPhase.Reserved)]
        [TestCase(MatchmakingSearchPhase.Searching)]
        [TestCase(MatchmakingSearchPhase.Idle)]
        public void ASeatAssignmentTakesTheTableAndEndsTheSearch(MatchmakingSearchPhase phase)
        {
            // From Searching or Idle, the assignment arrived after the reservation timed out. The table still holds
            // the seat, so the player takes it.
            MatchmakingSearchStep step = Step(phase, MatchmakingSearchEvent.SeatAssigned);

            Assert.That(step.TableAnswer, Is.EqualTo(MatchmakingTableAnswer.Take));
            Assert.That(step.Phase, Is.EqualTo(MatchmakingSearchPhase.Idle));
            Assert.That(step.Timeout, Is.EqualTo(MatchmakingSearchTimeout.Disarm));
            Assert.That(step.EndsUnseated, Is.False, "a seating reports the wait itself, with the number of humans");
        }

        [Test]
        public void ASeatAssignmentForAPlayerAlreadyAtATableIsDeclined()
        {
            MatchmakingSearchStep step = Step(MatchmakingSearchPhase.Searching, MatchmakingSearchEvent.SeatAssigned, Facts(isAtTable: true));

            Assert.That(step.TableAnswer, Is.EqualTo(MatchmakingTableAnswer.Decline), "accepting it would lose the table the player is at");
            AssertNothing(step, MatchmakingSearchPhase.Searching);
        }

        #endregion

        #region Seat released

        [Test]
        public void AReleaseThatKeepsThePlayerQueuedReturnsThemToSearching()
        {
            MatchmakingSearchStep step = Step(MatchmakingSearchPhase.Reserved, MatchmakingSearchEvent.SeatReleased, Facts(isStillQueued: true));

            Assert.That(step.Phase, Is.EqualTo(MatchmakingSearchPhase.Searching));
            Assert.That(step.Timeout, Is.EqualTo(MatchmakingSearchTimeout.ArmSearch));
            Assert.That(step.EndsUnseated, Is.False);
            Assert.That(step.ToQueue, Is.EqualTo(MatchmakingQueueMessage.None), "the matchmaker already put the entry back");
            Assert.That(step.Reply, Is.EqualTo(MatchmakingStatus.Searching));
        }

        [Test]
        public void AReleaseThatDoesNotKeepThePlayerQueuedEndsTheSearch()
        {
            MatchmakingSearchStep step = Step(MatchmakingSearchPhase.Reserved, MatchmakingSearchEvent.SeatReleased, Facts(isStillQueued: false));

            AssertEndedUnseated(step);
            Assert.That(step.ToQueue, Is.EqualTo(MatchmakingQueueMessage.None));
            Assert.That(step.Reply, Is.EqualTo(MatchmakingStatus.Unavailable));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void AReleaseForAPlayerWhoLeftLeavesTheQueue(bool isStillQueued)
        {
            // Their session ended while reserved, so nothing else would remove a returned entry.
            MatchmakingSearchStep step = Step(MatchmakingSearchPhase.Reserved, MatchmakingSearchEvent.SeatReleased, Facts(isClientConnected: false, isStillQueued: isStillQueued));

            AssertEndedUnseated(step);
            Assert.That(step.ToQueue, Is.EqualTo(MatchmakingQueueMessage.LeaveQueue));
            Assert.That(step.Reply, Is.Null);
        }

        [TestCase(MatchmakingSearchPhase.Idle)]
        [TestCase(MatchmakingSearchPhase.Searching)]
        public void AReleaseForAPlayerWhoDidNotCommitIsIgnored(MatchmakingSearchPhase phase)
        {
            MatchmakingSearchStep step = Step(phase, MatchmakingSearchEvent.SeatReleased, Facts(isStillQueued: true));

            AssertNothing(step, phase);
        }

        #endregion

        #region Timeouts and session end

        [Test]
        public void TheSearchTimeoutLeavesTheQueueAndTellsTheClient()
        {
            MatchmakingSearchStep step = Step(MatchmakingSearchPhase.Searching, MatchmakingSearchEvent.SearchTimedOut);

            AssertEndedUnseated(step);
            Assert.That(step.ToQueue, Is.EqualTo(MatchmakingQueueMessage.LeaveQueue), "a still-running matchmaker drops the stale entry");
            Assert.That(step.Reply, Is.EqualTo(MatchmakingStatus.Unavailable));
        }

        [Test]
        public void TheReservationTimeoutEndsTheSearchWithoutAQueueMessage()
        {
            MatchmakingSearchStep step = Step(MatchmakingSearchPhase.Reserved, MatchmakingSearchEvent.ReservationTimedOut);

            AssertEndedUnseated(step);
            Assert.That(step.ToQueue, Is.EqualTo(MatchmakingQueueMessage.None));
            Assert.That(step.Reply, Is.EqualTo(MatchmakingStatus.Unavailable));
        }

        [TestCase(MatchmakingSearchEvent.SearchTimedOut, MatchmakingSearchPhase.Idle)]
        [TestCase(MatchmakingSearchEvent.SearchTimedOut, MatchmakingSearchPhase.Reserved)]
        [TestCase(MatchmakingSearchEvent.ReservationTimedOut, MatchmakingSearchPhase.Idle)]
        [TestCase(MatchmakingSearchEvent.ReservationTimedOut, MatchmakingSearchPhase.Searching)]
        public void ATimeoutForAnotherPhaseDoesNothing(MatchmakingSearchEvent timeout, MatchmakingSearchPhase phase)
        {
            AssertNothing(Step(phase, timeout), phase);
        }

        [Test]
        public void ASessionEndWhileSearchingLeavesTheQueue()
        {
            MatchmakingSearchStep step = Step(MatchmakingSearchPhase.Searching, MatchmakingSearchEvent.SessionEnded);

            AssertEndedUnseated(step);
            Assert.That(step.ToQueue, Is.EqualTo(MatchmakingQueueMessage.LeaveQueue));
            Assert.That(step.Reply, Is.Null, "there is no client to tell");
        }

        [TestCase(MatchmakingSearchPhase.Idle)]
        [TestCase(MatchmakingSearchPhase.Reserved)]
        public void ASessionEndOutsideASearchLeavesTheSeatAlone(MatchmakingSearchPhase phase)
        {
            // A reserved player's table is being created with that seat. The match pointer re-attaches them later.
            AssertNothing(Step(phase, MatchmakingSearchEvent.SessionEnded), phase);
        }

        #endregion
    }
}
