using Game.Logic;
using Game.Server.Matchmaking;
using NUnit.Framework;

namespace Game.Server.Tests
{
    /// <summary>
    /// The cancel-versus-formation race, driven rather than raced. The actor model already guarantees that
    /// exactly one of the two wins — the player's own actor is the single arbiter and its mailbox is
    /// sequential — so what is worth testing is that the ordering-dependent <em>behaviour</em> is right for
    /// each of the two orderings.
    /// </summary>
    [TestFixture]
    public class MatchmakingSearchPhaseTests
    {
        static MatchmakingSearchStep Step(MatchmakingSearchPhase phase, MatchmakingSearchEvent ev, bool connected = true, bool inMatch = false)
            => MatchmakingSearchPhasePolicy.Step(phase, ev, connected, inMatch);

        [Test]
        public void AnEntryFromIdleStartsTheSearchAndPushesTheOneStatus()
        {
            MatchmakingSearchStep step = Step(MatchmakingSearchPhase.None, MatchmakingSearchEvent.Enqueued);

            Assert.That(step.Phase, Is.EqualTo(MatchmakingSearchPhase.Searching));
            Assert.That(step.Outbound, Is.EqualTo(MatchmakingSearchOutbound.StatusUpdate));
        }

        [Test]
        public void ASecondEntryIsRefusedVisibly()
        {
            // One entry per player, and the refusal gets an answer rather than being invisible: this is the
            // directed server-to-client channel on the player timeline.
            foreach (MatchmakingSearchPhase busy in new[] { MatchmakingSearchPhase.Searching, MatchmakingSearchPhase.SeatReserved })
            {
                MatchmakingSearchStep step = Step(busy, MatchmakingSearchEvent.Enqueued);

                Assert.That(step.Phase, Is.EqualTo(busy), $"a refused entry must not move the phase ({busy})");
                Assert.That(step.EndReason, Is.EqualTo(MatchmakingEndReason.Refused));
            }
        }

        [Test]
        public void CancelBeforeTheAskLeavesTheSeatDeclinedAndThePlayerCancelled()
        {
            MatchmakingSearchStep cancel = Step(MatchmakingSearchPhase.Searching, MatchmakingSearchEvent.Cancelled);

            Assert.That(cancel.Phase, Is.EqualTo(MatchmakingSearchPhase.None));
            Assert.That(cancel.EndReason, Is.EqualTo(MatchmakingEndReason.Cancelled));

            MatchmakingSearchStep ask = Step(cancel.Phase, MatchmakingSearchEvent.SeatAsked);

            Assert.That(ask.Outbound, Is.EqualTo(MatchmakingSearchOutbound.DeclineSeat));
            Assert.That(ask.Phase, Is.EqualTo(MatchmakingSearchPhase.None));
        }

        [Test]
        public void TheAskBeforeTheCancelSeatsThePlayerAndTheLaterCancelSaysNothing()
        {
            MatchmakingSearchStep ask = Step(MatchmakingSearchPhase.Searching, MatchmakingSearchEvent.SeatAsked);

            Assert.That(ask.Phase, Is.EqualTo(MatchmakingSearchPhase.SeatReserved), "the commit point");
            Assert.That(ask.Outbound, Is.EqualTo(MatchmakingSearchOutbound.AcceptSeat));

            MatchmakingSearchStep cancel = Step(ask.Phase, MatchmakingSearchEvent.Cancelled);

            Assert.That(cancel.Phase, Is.EqualTo(MatchmakingSearchPhase.SeatReserved), "a late cancel is a no-op");
            Assert.That(cancel.EndReason, Is.Null,
                "and the client keeps waiting rather than being shown a menu it is about to be pulled off");
        }

        [Test]
        public void ADeadConnectionDeclinesTheSeatWhateverThePhaseSays()
        {
            // Liveness is a live connection, not a live session. In a 1v1 game a dead human is the whole
            // opposition, and the seat a session check would hand out is worse than a missing seat.
            foreach (MatchmakingSearchPhase phase in new[] { MatchmakingSearchPhase.None, MatchmakingSearchPhase.Searching, MatchmakingSearchPhase.SeatReserved })
            {
                MatchmakingSearchStep step = Step(phase, MatchmakingSearchEvent.SeatAsked, connected: false);

                Assert.That(step.Phase, Is.EqualTo(MatchmakingSearchPhase.None), $"the search is over ({phase})");
                Assert.That(step.Outbound, Is.Not.EqualTo(MatchmakingSearchOutbound.AcceptSeat), $"from {phase}");
            }
        }

        [Test]
        public void AnAccountAlreadyAtATableDeclinesTheSeat()
        {
            // The third of the design's three declines, and the one that was missing: "an actor that has since
            // cancelled, is already in a match, or has nobody on the other end of its connection declines". A
            // seat handed to an account that cannot subscribe to it leaves the other human playing a ranked
            // game against a phantom.
            foreach (MatchmakingSearchPhase phase in new[] { MatchmakingSearchPhase.Searching, MatchmakingSearchPhase.SeatReserved })
            {
                MatchmakingSearchStep step = Step(phase, MatchmakingSearchEvent.SeatAsked, connected: true, inMatch: true);

                Assert.That(step.Outbound, Is.Not.EqualTo(MatchmakingSearchOutbound.AcceptSeat), $"from {phase}");
                Assert.That(step.Phase, Is.EqualTo(MatchmakingSearchPhase.None), $"and the search is over ({phase})");
            }
        }

        [Test]
        public void ASecondReservationAgainstACommittedSeatIsDeclinedAndChangesNothing()
        {
            // The one branch holding "never two pairings" up. Two tickets for one account would satisfy the
            // band policy at a gap of zero, and this decline is what unwinds it — so it is asserted rather
            // than left to the one formation that would have to go wrong to reach it.
            MatchmakingSearchStep step = Step(MatchmakingSearchPhase.SeatReserved, MatchmakingSearchEvent.SeatAsked);

            Assert.That(step.Outbound, Is.EqualTo(MatchmakingSearchOutbound.DeclineSeat));
            Assert.That(step.Phase, Is.EqualTo(MatchmakingSearchPhase.SeatReserved),
                "the seat it already committed to is still its own, and its SeatedBound is still running");
        }

        [Test]
        public void ADeclineThePlayerDidNotAskForIsToldToThem()
        {
            // The matchmaker takes the ticket out of the queue before the ask goes out and never puts a
            // declined seat back, so a search that ends here ends with nothing on screen having changed —
            // which is the searching dialog up forever, over a search that is over.
            Assert.That(Step(MatchmakingSearchPhase.Searching, MatchmakingSearchEvent.SeatAsked, connected: false).EndReason,
                Is.EqualTo(MatchmakingEndReason.SeatLost));
            Assert.That(Step(MatchmakingSearchPhase.SeatReserved, MatchmakingSearchEvent.SeatAsked, connected: false).EndReason,
                Is.EqualTo(MatchmakingEndReason.SeatLost));
            Assert.That(Step(MatchmakingSearchPhase.Searching, MatchmakingSearchEvent.SeatAsked, inMatch: true).EndReason,
                Is.EqualTo(MatchmakingEndReason.SeatLost));

            // And the one decline the player DID ask for stays silent: their cancel won the race and was
            // already answered with Cancelled, so a second message would close a dialog twice.
            Assert.That(Step(MatchmakingSearchPhase.None, MatchmakingSearchEvent.SeatAsked).Outbound,
                Is.EqualTo(MatchmakingSearchOutbound.DeclineSeat));
            Assert.That(Step(MatchmakingSearchPhase.None, MatchmakingSearchEvent.SeatAsked).EndReason, Is.Null);
        }

        [Test]
        public void AReleasedReservationGoesBackToSearchingAndSaysNothing()
        {
            // The ticket is re-queued by the matchmaker with its original stamp, so the dialog never stopped
            // being true and there is nothing to tell the player.
            MatchmakingSearchStep step = Step(MatchmakingSearchPhase.SeatReserved, MatchmakingSearchEvent.ReservationReleased);

            Assert.That(step.Phase, Is.EqualTo(MatchmakingSearchPhase.Searching));
            Assert.That(step.Outbound, Is.EqualTo(MatchmakingSearchOutbound.Nothing));
        }

        [Test]
        public void ASeatThatIsGoneEndsTheSearchAndIsNotLeftWaiting()
        {
            MatchmakingSearchStep step = Step(MatchmakingSearchPhase.SeatReserved, MatchmakingSearchEvent.SeatGone);

            Assert.That(step.Phase, Is.EqualTo(MatchmakingSearchPhase.None), "not re-queued, and not still searching");
            Assert.That(step.EndReason, Is.EqualTo(MatchmakingEndReason.SeatLost));

            // And a stray one against an idle actor says nothing rather than raising a dialog out of nowhere.
            Assert.That(Step(MatchmakingSearchPhase.None, MatchmakingSearchEvent.SeatGone).Outbound,
                Is.EqualTo(MatchmakingSearchOutbound.Nothing));
        }

        [Test]
        public void FormationEndsTheSearchWithoutTellingTheClientAnything()
        {
            // The board appearing is what dismisses the dialog: the client is never told "you have been
            // matched" as a message of its own kind.
            MatchmakingSearchStep step = Step(MatchmakingSearchPhase.SeatReserved, MatchmakingSearchEvent.Formed);

            Assert.That(step.Phase, Is.EqualTo(MatchmakingSearchPhase.None));
            Assert.That(step.Outbound, Is.EqualTo(MatchmakingSearchOutbound.Nothing));
        }

        [Test]
        public void ABoundThatExpiresLandsThePlayerBackOnTheMenu()
        {
            foreach (MatchmakingSearchPhase waiting in new[] { MatchmakingSearchPhase.Searching, MatchmakingSearchPhase.SeatReserved })
            {
                MatchmakingSearchStep step = Step(waiting, MatchmakingSearchEvent.BoundExpired);

                Assert.That(step.Phase, Is.EqualTo(MatchmakingSearchPhase.None), $"from {waiting}");
                Assert.That(step.EndReason, Is.EqualTo(MatchmakingEndReason.TimedOut), $"from {waiting}");
            }

            // A bound that fires against an idle actor is the ordinary case — nothing is cancelled, so every
            // bound eventually fires — and it must say nothing at all.
            MatchmakingSearchStep idle = Step(MatchmakingSearchPhase.None, MatchmakingSearchEvent.BoundExpired);

            Assert.That(idle.Outbound, Is.EqualTo(MatchmakingSearchOutbound.Nothing));
        }

        [Test]
        public void ACancelWithNothingToCancelIsAnsweredAnyway()
        {
            // The client is the one that can be wrong here: a seat declined for liveness and an actor that
            // restarted mid-search both leave a dialog up with nothing coming, and a Cancel the server
            // silently drops is a dialog that never closes. Answering costs one message and can only agree
            // with what has already happened.
            MatchmakingSearchStep step = Step(MatchmakingSearchPhase.None, MatchmakingSearchEvent.Cancelled);

            Assert.That(step.Phase, Is.EqualTo(MatchmakingSearchPhase.None));
            Assert.That(step.EndReason, Is.EqualTo(MatchmakingEndReason.Cancelled));
        }
    }
}
