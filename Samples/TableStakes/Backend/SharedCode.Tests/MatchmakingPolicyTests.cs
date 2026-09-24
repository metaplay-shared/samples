using Metaplay.Core;
using NUnit.Framework;
using System.Collections.Generic;

namespace Game.Logic.Tests
{
    /// <summary>
    /// Tests for <see cref="MatchmakingPolicy"/> and <see cref="MatchmakingQueue"/>: whether to form a table
    /// now or keep waiting, how many waiters to seat, and the queue rules. The logic is pure, so no test needs an
    /// actor or a server (<c>docs/matchmaking.md</c>, "The matchmaker entity").
    /// </summary>
    [TestFixture]
    public class MatchmakingPolicyTests
    {
        static readonly MetaTime     T0       = MatchTestDeals.T0;
        static readonly MetaDuration FillWait = MetaDuration.FromSeconds(5);

        static MatchmakingWaiter Waiter(int index, MetaTime enqueuedAt)
            => new MatchmakingWaiter(PlayerPublicIdentity.ForSeat(EntityId.Create(EntityKindCore.Player, (ulong)(1000 + index)), $"Player {index}"), enqueuedAt);

        static MatchmakingQueue QueueOf(params MetaTime[] enqueueTimes)
        {
            MatchmakingQueue queue = new MatchmakingQueue();
            for (int index = 0; index < enqueueTimes.Length; index++)
                Assert.That(queue.TryEnqueue(Waiter(index, enqueueTimes[index])), Is.True);
            return queue;
        }

        #region Form or wait

        [Test]
        public void AnEmptyQueueWaitsOnNothing()
        {
            MatchmakingDecision decision = MatchmakingPolicy.Decide(new MatchmakingQueue().Waiters, T0, FillWait);

            Assert.That(decision.Action, Is.EqualTo(MatchmakingAction.Wait));
            Assert.That(decision.NextLookAt, Is.EqualTo(MetaTime.Epoch), "an empty queue is the only case with nothing to look at again");
        }

        [TestCase(4)]
        [TestCase(6)]
        public void AFullQueueFormsOneTableAtOnce(int numWaiting)
        {
            // A full table forms immediately. Waiting out the fill wait would delay the players for nothing.
            MetaTime[] enqueueTimes = new MetaTime[numWaiting];
            for (int index = 0; index < numWaiting; index++)
                enqueueTimes[index] = T0;

            MatchmakingDecision decision = MatchmakingPolicy.Decide(QueueOf(enqueueTimes).Waiters, T0, FillWait);

            Assert.That(decision.Action, Is.EqualTo(MatchmakingAction.Form));
            Assert.That(decision.NumWaitersToSeat, Is.EqualTo(MatchRules.NumSeats));
        }

        [Test]
        public void AnIncompleteQueueWaitsForTheFillWait()
        {
            MatchmakingQueue    queue    = QueueOf(T0);
            MatchmakingDecision decision = MatchmakingPolicy.Decide(queue.Waiters, T0 + MetaDuration.FromSeconds(4), FillWait);

            Assert.That(decision.Action, Is.EqualTo(MatchmakingAction.Wait));
            Assert.That(decision.NextLookAt, Is.EqualTo(T0 + FillWait));
        }

        [Test]
        public void AnIncompleteQueueFormsWhenTheFillWaitHasElapsed()
        {
            MatchmakingQueue    queue    = QueueOf(T0);
            MatchmakingDecision decision = MatchmakingPolicy.Decide(queue.Waiters, T0 + FillWait, FillWait);

            Assert.That(decision.Action, Is.EqualTo(MatchmakingAction.Form));
            Assert.That(decision.NumWaitersToSeat, Is.EqualTo(1), "one human and three bots is the modal table");
        }

        [Test]
        public void AZeroFillWaitFormsTheInstantSomeoneQueues()
        {
            MatchmakingQueue    queue    = QueueOf(T0);
            MatchmakingDecision decision = MatchmakingPolicy.Decide(queue.Waiters, T0, MetaDuration.Zero);

            Assert.That(decision.Action, Is.EqualTo(MatchmakingAction.Form));
            Assert.That(decision.NumWaitersToSeat, Is.EqualTo(1));
        }

        [Test]
        public void TheFillWaitIsMeasuredFromTheOldestWaiterAndNotResetByArrivals()
        {
            // A player who joins late waits only for the rest of the oldest waiter's fill wait. Restarting the
            // wait on every arrival would let a steady trickle of arrivals keep the queue open forever.
            MatchmakingQueue    queue    = QueueOf(T0, T0 + MetaDuration.FromSeconds(4));
            MatchmakingDecision decision = MatchmakingPolicy.Decide(queue.Waiters, T0 + MetaDuration.FromSeconds(4), FillWait);

            Assert.That(decision.Action, Is.EqualTo(MatchmakingAction.Wait));
            Assert.That(decision.NextLookAt, Is.EqualTo(T0 + FillWait));
        }

        #endregion

        #region The timer is re-armed for a remainder

        [Test]
        public void AWaiterLeftBehindByAFormationIsStillWaitedFor()
        {
            // The timer must be armed whenever the queue is non-empty and no timer is running, including right
            // after a formation that left waiters behind. Arming it only when the queue goes from empty to
            // non-empty would leave the leftover player waiting for arrivals that may never come.
            MetaTime         fifthArrivedAt = T0 + MetaDuration.FromSeconds(2);
            MatchmakingQueue queue          = QueueOf(T0, T0, T0, T0, fifthArrivedAt);

            MatchmakingDecision first = MatchmakingPolicy.Decide(queue.Waiters, T0 + MetaDuration.FromSeconds(2), FillWait);
            Assert.That(first.Action, Is.EqualTo(MatchmakingAction.Form));
            queue.TakeFromHead(first.NumWaitersToSeat);

            MatchmakingDecision second = MatchmakingPolicy.Decide(queue.Waiters, T0 + MetaDuration.FromSeconds(2), FillWait);
            Assert.That(second.Action, Is.EqualTo(MatchmakingAction.Wait));
            Assert.That(second.NextLookAt, Is.EqualTo(fifthArrivedAt + FillWait),
                "the leftover waiter's own fill wait, measured from when they arrived");
        }

        [Test]
        public void AFormationThatEmptiesTheQueueLeavesNothingArmed()
        {
            MatchmakingQueue queue = QueueOf(T0, T0, T0, T0);

            MatchmakingDecision first = MatchmakingPolicy.Decide(queue.Waiters, T0, FillWait);
            queue.TakeFromHead(first.NumWaitersToSeat);

            MatchmakingDecision second = MatchmakingPolicy.Decide(queue.Waiters, T0, FillWait);
            Assert.That(second.Action, Is.EqualTo(MatchmakingAction.Wait));
            Assert.That(second.NextLookAt, Is.EqualTo(MetaTime.Epoch));
        }

        #endregion

        #region One entry per player

        [Test]
        public void APlayerAlreadyWaitingIsNotQueuedTwice()
        {
            MatchmakingQueue queue  = new MatchmakingQueue();
            MatchmakingWaiter waiter = Waiter(0, T0);

            Assert.That(queue.TryEnqueue(waiter), Is.True);
            Assert.That(queue.TryEnqueue(new MatchmakingWaiter(PlayerPublicIdentity.ForSeat(waiter.PlayerId, "Renamed"), T0 + MetaDuration.FromSeconds(1))), Is.False);
            Assert.That(queue.Count, Is.EqualTo(1));
            Assert.That(queue.Waiters[0].EnqueuedAt, Is.EqualTo(T0), "the first entry stands; a second tap does not restart the wait");
        }

        [Test]
        public void AnInvalidPlayerIsNeverQueued()
        {
            MatchmakingQueue queue = new MatchmakingQueue();

            Assert.That(queue.TryEnqueue(new MatchmakingWaiter(PlayerPublicIdentity.ForSeat(EntityId.None, "Nobody"), T0)), Is.False);
            Assert.That(queue.Count, Is.EqualTo(0));
        }

        [Test]
        public void LeavingRemovesExactlyOneEntryAndIsANoOpTwice()
        {
            MatchmakingQueue queue = QueueOf(T0, T0, T0);
            EntityId         second = queue.Waiters[1].PlayerId;

            Assert.That(queue.Remove(second), Is.True);
            Assert.That(queue.Count, Is.EqualTo(2));
            Assert.That(queue.Contains(second), Is.False);

            // A cancel that arrives after a formation took the entry finds nothing to remove, so the late cancel
            // has no effect on the queue.
            Assert.That(queue.Remove(second), Is.False);
            Assert.That(queue.Count, Is.EqualTo(2));
        }

        #endregion

        #region A failed mint leaves the waiters queued

        [Test]
        public void WaitersFromAFailedFormationGoBackAtTheHeadWithTheirOwnStamps()
        {
            MatchmakingQueue        queue     = QueueOf(T0, T0 + MetaDuration.FromSeconds(1));
            List<MatchmakingWaiter> formation = queue.TakeFromHead(2);
            Assert.That(queue.Count, Is.EqualTo(0));

            // Another player joins before the mint fails. The returned waiters go in front of that player.
            Assert.That(queue.TryEnqueue(Waiter(9, T0 + MetaDuration.FromSeconds(3))), Is.True);
            queue.ReturnToHead(formation);

            Assert.That(queue.Count, Is.EqualTo(3));
            Assert.That(queue.Waiters[0].PlayerId, Is.EqualTo(formation[0].PlayerId));
            Assert.That(queue.Waiters[1].PlayerId, Is.EqualTo(formation[1].PlayerId));
            Assert.That(queue.Waiters[0].EnqueuedAt, Is.EqualTo(T0), "the fill wait still runs from when they started waiting");

            MatchmakingDecision decision = MatchmakingPolicy.Decide(queue.Waiters, T0 + MetaDuration.FromSeconds(3), FillWait);
            Assert.That(decision.Action, Is.EqualTo(MatchmakingAction.Wait));
            Assert.That(decision.NextLookAt, Is.EqualTo(T0 + FillWait));
        }

        /// <summary>
        /// <see cref="MatchmakingQueue.ReturnToHead"/> keeps one entry per player, like
        /// <see cref="MatchmakingQueue.TryEnqueue"/>. The matchmaker actor handles one message at a time, so it
        /// never enqueues a player while a formation is in flight. This test pins the queue's own guarantee for
        /// any other caller.
        /// </summary>
        [Test]
        public void ReturningAWaiterTheQueueAlreadyHoldsDoesNotQueueThemTwice()
        {
            MatchmakingQueue        queue     = QueueOf(T0);
            List<MatchmakingWaiter> formation = queue.TakeFromHead(1);

            Assert.That(queue.TryEnqueue(new MatchmakingWaiter(PlayerPublicIdentity.ForSeat(formation[0].PlayerId, "Player 0"), T0 + MetaDuration.FromSeconds(2))), Is.True);
            queue.ReturnToHead(formation);

            Assert.That(queue.Count, Is.EqualTo(1));
            Assert.That(queue.Waiters[0].EnqueuedAt, Is.EqualTo(T0 + MetaDuration.FromSeconds(2)));
        }

        #endregion

        #region Randomised seat order

        [Test]
        public void ASeatOrderIsAlwaysAPermutationOfTheTable()
        {
            for (ulong seed = 0; seed < 200; seed++)
            {
                int[]     order = MatchmakingPolicy.DrawSeatOrder(seed);
                List<int> seen  = new List<int>(order);
                seen.Sort();

                Assert.That(order.Length, Is.EqualTo(MatchRules.NumSeats), $"seed {seed}");
                for (int seat = 0; seat < MatchRules.NumSeats; seat++)
                    Assert.That(seen[seat], Is.EqualTo(seat), $"seed {seed}: every seat is filled exactly once");
            }
        }

        [Test]
        public void TheSameSeedDrawsTheSameSeatOrder()
        {
            int[] first  = MatchmakingPolicy.DrawSeatOrder(777UL);
            int[] second = MatchmakingPolicy.DrawSeatOrder(777UL);

            Assert.That(second, Is.EqualTo(first));
        }

        [Test]
        public void TheFirstArrivalDoesNotAlwaysGetSeatZero()
        {
            // A player's seat must not be predictable from the order in which players tapped Play. A draw that
            // always put the first arrival in seat 0 would pass every other test in this region.
            int[] timesFirstWaiterGotSeat = new int[MatchRules.NumSeats];
            for (ulong seed = 0; seed < 400; seed++)
                timesFirstWaiterGotSeat[MatchmakingPolicy.DrawSeatOrder(seed)[0]]++;

            for (int seat = 0; seat < MatchRules.NumSeats; seat++)
                Assert.That(timesFirstWaiterGotSeat[seat], Is.GreaterThan(0), $"the first waiter never landed on seat {seat} in 400 draws");
        }

        #endregion

        #region Answering a seat reservation

        /// <summary>
        /// Checks the answer, and so the reason a player declines. A cancel that reached the player actor before
        /// the seat reservation left it not searching, so the actor declines the seat and the player stays
        /// cancelled. A session outlives its connection by up to several fill waits: a player who closed the tab
        /// right after tapping Play still has a session when the formation asks, so checking the session alone
        /// would seat a player who is gone. This is why the check also takes whether the client is connected.
        /// </summary>
        [TestCase(true,  false, true,  true,  SeatReservationAnswer.Take)]
        [TestCase(false, false, true,  true,  SeatReservationAnswer.NotSearching)]
        [TestCase(true,  true,  true,  true,  SeatReservationAnswer.AlreadyAtTable)]
        [TestCase(true,  false, true,  false, SeatReservationAnswer.NotLive)]
        [TestCase(true,  false, false, false, SeatReservationAnswer.NotLive)]
        public void ASeatReservationIsAnsweredWithTheReason(bool isSearching, bool isAtTable, bool hasSession, bool isClientConnected, SeatReservationAnswer expected)
        {
            Assert.That(MatchmakingPolicy.AnswerSeatReservation(isSearching, isAtTable, hasSession, isClientConnected), Is.EqualTo(expected));
        }

        /// <summary>
        /// Checks every combination of inputs: a player takes a seat only when searching, not already at a table,
        /// with a session, and with a connected client. This guarantees that a table is never minted with a seat
        /// whose player is gone.
        /// </summary>
        [Test]
        public void OnlyASearchingUnseatedPresentPlayerEverTakesASeat()
        {
            for (int shape = 0; shape < 16; shape++)
            {
                bool isSearching       = (shape & 1) != 0;
                bool isAtTable         = (shape & 2) != 0;
                bool hasSession        = (shape & 4) != 0;
                bool isClientConnected = (shape & 8) != 0;

                bool takesSeat = MatchmakingPolicy.AnswerSeatReservation(isSearching, isAtTable, hasSession, isClientConnected) == SeatReservationAnswer.Take;

                Assert.That(takesSeat, Is.EqualTo(isSearching && !isAtTable && hasSession && isClientConnected),
                    $"searching={isSearching}, atTable={isAtTable}, session={hasSession}, connected={isClientConnected}");
            }
        }

        #endregion
    }
}
