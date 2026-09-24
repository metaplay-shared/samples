using Game.Logic;
using Game.Server.Match;
using Game.Server.Matchmaking;
using Metaplay.Core;
using NUnit.Framework;
using System;
using System.Collections.Generic;

namespace Game.Server.Tests
{
    /// <summary>
    /// The band policy, with no actor, no clock and no server: construct a queue snapshot, call
    /// <c>Evaluate</c> at an instant, assert the verdict. That is the whole point of the function being pure —
    /// form-or-wait, each band step and its boundaries, the longer-waiter tolerance rule, closest-partner
    /// selection, the fill wait producing a bot, and the whole schedule at a zero fill wait
    /// (<c>Docs/matchmaking.md</c>, "Testing").
    /// </summary>
    [TestFixture]
    public class MatchmakingPolicyTests
    {
        static readonly MetaTime Now = MetaTime.FromDateTime(new DateTime(2026, 9, 9, 12, 0, 0, DateTimeKind.Utc));

        static MatchmakingBandSchedule Schedule => MatchmakingPolicy.ScheduleWithFillWait(MetaDuration.FromSeconds(45));

        static int _nextPlayer;

        /// <summary> One waiter, described by the three things the policy reads. </summary>
        static MatchmakingTicket Ticket(int rating, int powerScore, double waitedSeconds)
        {
            EntityId playerId = EntityId.Create(EntityKindCore.Player, (ulong)(++_nextPlayer));
            return TicketFor(playerId, rating, powerScore, Now - MetaDuration.FromMilliseconds((long)(waitedSeconds * 1000)));
        }

        static MatchmakingTicket TicketFor(EntityId playerId, int rating, int powerScore, MetaTime arrivedAt)
        {
            MatchSeatSetup seat = new MatchSeatSetup(
                playerId, $"Waiter {playerId.Value}", SeatOccupancy.Human, BotProfileId.Strongest,
                new List<CardId>(), new MetaDictionary<CardId, int>(), new List<CardId>(), rating);

            return new MatchmakingTicket(seat, powerScore, rankedMatchesPlayed: 0, newcomerShieldWaived: false, DeckChoice.Saved(0), arrivedAt);
        }

        static List<MatchmakingTicket> Queue(params MatchmakingTicket[] tickets) => new List<MatchmakingTicket>(tickets);

        [Test]
        public void AnEmptyQueueArmsNothingAtAll()
        {
            // Null is the strong claim: not "look again soon", but "there is nothing to look at". The host
            // arms no timer, and the next arrival is what re-arms one.
            MatchmakingPolicy.Verdict verdict = MatchmakingPolicy.Evaluate(Queue(), Now, Schedule);

            Assert.That(verdict, Is.InstanceOf<MatchmakingPolicy.Wait>());
            Assert.That(((MatchmakingPolicy.Wait)verdict).NextEvaluationAt, Is.Null);
        }

        [Test]
        public void ASingleTicketWaitsUntilItsNextBandStep()
        {
            MatchmakingTicket alone = Ticket(rating: 1000, powerScore: 60, waitedSeconds: 2);

            MatchmakingPolicy.Verdict verdict = MatchmakingPolicy.Evaluate(Queue(alone), Now, Schedule);

            Assert.That(verdict, Is.InstanceOf<MatchmakingPolicy.Wait>());
            Assert.That(((MatchmakingPolicy.Wait)verdict).NextEvaluationAt,
                Is.EqualTo(alone.ArrivedAt + MetaDuration.FromSeconds(10)));
        }

        [Test]
        public void TwoFreshWaitersInsideBothOpeningBandsPairAtOnce()
        {
            MatchmakingTicket a = Ticket(rating: 1000, powerScore: 60, waitedSeconds: 1);
            MatchmakingTicket b = Ticket(rating: 1050, powerScore: 65, waitedSeconds: 0);

            MatchmakingPolicy.Verdict verdict = MatchmakingPolicy.Evaluate(Queue(a, b), Now, Schedule);

            Assert.That(verdict, Is.InstanceOf<MatchmakingPolicy.FormPair>());
        }

        [Test]
        public void ARatingGapOutsideTheOpeningBandWaits()
        {
            MatchmakingTicket a = Ticket(rating: 1000, powerScore: 60, waitedSeconds: 1);
            MatchmakingTicket b = Ticket(rating: 1150, powerScore: 60, waitedSeconds: 0);

            Assert.That(MatchmakingPolicy.Evaluate(Queue(a, b), Now, Schedule), Is.InstanceOf<MatchmakingPolicy.Wait>());
        }

        [Test]
        public void TheSamePairIsCompatibleOnceTheBandHasWidened()
        {
            // Nothing happens in between: the answer changes because time passed, which is the whole reason
            // the queue's timer has to wake a quiet queue at all.
            MatchmakingTicket a = Ticket(rating: 1000, powerScore: 60, waitedSeconds: 12);
            MatchmakingTicket b = Ticket(rating: 1150, powerScore: 60, waitedSeconds: 12);

            Assert.That(MatchmakingPolicy.Evaluate(Queue(a, b), Now, Schedule), Is.InstanceOf<MatchmakingPolicy.FormPair>());
        }

        [Test]
        public void APowerScoreGapInsideTheOpeningBandPairs()
        {
            MatchmakingTicket a = Ticket(rating: 1000, powerScore: 60, waitedSeconds: 5);
            MatchmakingTicket b = Ticket(rating: 1000, powerScore: 72, waitedSeconds: 5);

            // 12 apart is outside the opening ±10 on both sides.
            Assert.That(MatchmakingPolicy.Evaluate(Queue(a, b), Now, Schedule), Is.InstanceOf<MatchmakingPolicy.Wait>());

            MatchmakingTicket c = Ticket(rating: 1000, powerScore: 60, waitedSeconds: 5);
            MatchmakingTicket d = Ticket(rating: 1000, powerScore: 69, waitedSeconds: 5);

            Assert.That(MatchmakingPolicy.Evaluate(Queue(c, d), Now, Schedule), Is.InstanceOf<MatchmakingPolicy.FormPair>());
        }

        [Test]
        public void ThePairIsJudgedByTheMoreTolerantOfTheTwoBands()
        {
            // The rule stated in general, asked with the FRESHER ticket as the pivot — which is the case
            // Evaluate's own oldest-first scan can never produce, and therefore the case that would let a
            // "just use the pivot's band" implementation pass unnoticed.
            MatchmakingTicket fresh = Ticket(rating: 1000, powerScore: 60, waitedSeconds: 5);
            MatchmakingTicket old   = Ticket(rating: 1000, powerScore: 72, waitedSeconds: 40);

            Assert.That(MatchmakingPolicy.AreCompatible(fresh, old, Now, Schedule), Is.True,
                "the longer waiter's ±40 Power Score band should cover a gap of 12");
            Assert.That(MatchmakingPolicy.AreCompatible(old, fresh, Now, Schedule), Is.True,
                "and the rule must be symmetric");

            // And a fresh pair at the same gap is not compatible, so the case above is about the band rather
            // than about the gap being small.
            MatchmakingTicket other = Ticket(rating: 1000, powerScore: 72, waitedSeconds: 5);
            Assert.That(MatchmakingPolicy.AreCompatible(fresh, other, Now, Schedule), Is.False);
        }

        [Test]
        public void AnUnboundedRatingBandOnEitherSideOpensTheRatingAxis()
        {
            MatchmakingTicket fresh = Ticket(rating: 1000, powerScore: 60, waitedSeconds: 1);
            MatchmakingTicket old   = Ticket(rating: 4000, powerScore: 60, waitedSeconds: 30);

            Assert.That(MatchmakingPolicy.AreCompatible(fresh, old, Now, Schedule), Is.True);

            // The Power Score band never opens, though, whatever the wait: a rating mismatch produces an
            // unpleasant game and a Power Score mismatch produces a distorted wager.
            MatchmakingTicket wideScore = Ticket(rating: 4000, powerScore: 120, waitedSeconds: 30);
            Assert.That(MatchmakingPolicy.AreCompatible(fresh, wideScore, Now, Schedule), Is.False);
        }

        [Test]
        public void TheOldestWaiterTakesItsClosestPartnerRatherThanTheFirstFound()
        {
            MatchmakingTicket oldest = Ticket(rating: 1000, powerScore: 60, waitedSeconds: 30);
            MatchmakingTicket far    = Ticket(rating: 1200, powerScore: 60, waitedSeconds: 20);
            MatchmakingTicket near   = Ticket(rating: 1010, powerScore: 60, waitedSeconds: 5);

            MatchmakingPolicy.Verdict verdict = MatchmakingPolicy.Evaluate(Queue(oldest, far, near), Now, Schedule);

            MatchmakingPolicy.FormPair pair = (MatchmakingPolicy.FormPair)verdict;
            Assert.That(pair.A, Is.SameAs(oldest), "the longest waiter gets first refusal");
            Assert.That(pair.B, Is.SameAs(near), "and takes the closest of its compatible partners");
        }

        [Test]
        public void ThePowerScoreGapIsTheTieBreakOnAnEqualRatingGap()
        {
            MatchmakingTicket oldest = Ticket(rating: 1000, powerScore: 60, waitedSeconds: 30);
            MatchmakingTicket wide   = Ticket(rating: 1020, powerScore: 75, waitedSeconds: 20);
            MatchmakingTicket tight  = Ticket(rating: 1020, powerScore: 62, waitedSeconds: 10);

            MatchmakingPolicy.FormPair pair = (MatchmakingPolicy.FormPair)MatchmakingPolicy.Evaluate(Queue(oldest, wide, tight), Now, Schedule);

            Assert.That(pair.B, Is.SameAs(tight));
        }

        [Test]
        public void AnExactTieGoesToTheOlderCandidate()
        {
            // Determinism, and first refusal among equals: the second-oldest waiter should not lose a coin
            // flip decided by the list's own order.
            MatchmakingTicket oldest = Ticket(rating: 1000, powerScore: 60, waitedSeconds: 30);
            MatchmakingTicket younger = Ticket(rating: 1010, powerScore: 61, waitedSeconds: 3);
            MatchmakingTicket older   = Ticket(rating: 1010, powerScore: 61, waitedSeconds: 20);

            MatchmakingPolicy.FormPair pair = (MatchmakingPolicy.FormPair)MatchmakingPolicy.Evaluate(Queue(oldest, younger, older), Now, Schedule);

            Assert.That(pair.B, Is.SameAs(older));
        }

        [Test]
        public void AWaiterAtExactlyTheFillWaitTakesTheBot()
        {
            MatchmakingTicket alone = Ticket(rating: 1000, powerScore: 60, waitedSeconds: 45.0);

            MatchmakingPolicy.Verdict verdict = MatchmakingPolicy.Evaluate(Queue(alone), Now, Schedule);

            Assert.That(verdict, Is.InstanceOf<MatchmakingPolicy.FormBotMatch>());
            Assert.That(((MatchmakingPolicy.FormBotMatch)verdict).Waiter, Is.SameAs(alone));
        }

        [Test]
        public void AWaiterJustInsideTheFillWaitDoesNot()
        {
            MatchmakingTicket alone = Ticket(rating: 1000, powerScore: 60, waitedSeconds: 44.9);

            MatchmakingPolicy.Verdict verdict = MatchmakingPolicy.Evaluate(Queue(alone), Now, Schedule);

            Assert.That(verdict, Is.InstanceOf<MatchmakingPolicy.Wait>());
            Assert.That(((MatchmakingPolicy.Wait)verdict).NextEvaluationAt,
                Is.EqualTo(alone.ArrivedAt + MetaDuration.FromSeconds(45)));
        }

        [Test]
        public void AnExpiredWaiterTakesTheBotAndLeavesTheRestOfTheQueueAlone()
        {
            MatchmakingTicket expired = Ticket(rating: 1000, powerScore: 20, waitedSeconds: 50);
            MatchmakingTicket other   = Ticket(rating: 1000, powerScore: 120, waitedSeconds: 3);

            MatchmakingPolicy.Verdict verdict = MatchmakingPolicy.Evaluate(Queue(expired, other), Now, Schedule);

            Assert.That(verdict, Is.InstanceOf<MatchmakingPolicy.FormBotMatch>());
            Assert.That(((MatchmakingPolicy.FormBotMatch)verdict).Waiter, Is.SameAs(expired));
        }

        [Test]
        public void APairingBeatsTheFillWaitWhenBothAreAvailable()
        {
            // The fill wait is what the queue does when it has run out of humans, not a deadline that
            // outranks one.
            MatchmakingTicket expired = Ticket(rating: 1000, powerScore: 60, waitedSeconds: 50);
            MatchmakingTicket partner = Ticket(rating: 1005, powerScore: 62, waitedSeconds: 1);

            Assert.That(MatchmakingPolicy.Evaluate(Queue(expired, partner), Now, Schedule), Is.InstanceOf<MatchmakingPolicy.FormPair>());
        }

        [Test]
        public void AtAZeroFillWaitEveryWaiterTakesTheBotImmediately()
        {
            // The policy is unit-tested at zero even though a live end-to-end test must not use it: zero means
            // "form the instant the first waiter arrives", which makes seating two players together
            // impossible by construction.
            MatchmakingBandSchedule zero = MatchmakingPolicy.ScheduleWithFillWait(MetaDuration.Zero);

            MatchmakingTicket a = Ticket(rating: 1000, powerScore: 60, waitedSeconds: 0);
            MatchmakingTicket b = Ticket(rating: 4000, powerScore: 120, waitedSeconds: 0);

            MatchmakingPolicy.Verdict verdict = MatchmakingPolicy.Evaluate(Queue(a, b), Now, zero);

            Assert.That(verdict, Is.InstanceOf<MatchmakingPolicy.FormBotMatch>());
            Assert.That(((MatchmakingPolicy.FormBotMatch)verdict).Waiter, Is.SameAs(a), "the oldest goes first");
        }

        [Test]
        public void NextEvaluationAtTakesTheEarliestBandBoundaryAcrossTheQueue()
        {
            MatchmakingTicket nearBoundary = Ticket(rating: 1000, powerScore: 20, waitedSeconds: 9);
            MatchmakingTicket farBoundary   = Ticket(rating: 4000, powerScore: 120, waitedSeconds: 1);

            MatchmakingPolicy.Wait wait = (MatchmakingPolicy.Wait)MatchmakingPolicy.Evaluate(Queue(nearBoundary, farBoundary), Now, Schedule);

            Assert.That(wait.NextEvaluationAt, Is.EqualTo(nearBoundary.ArrivedAt + MetaDuration.FromSeconds(10)));
        }

        [Test]
        public void NextEvaluationAtTakesTheFillDeadlineWhenEveryBandStepIsSpent()
        {
            // Past the last step there is no boundary left for this ticket, so the only instant at which the
            // answer can change on its own is the fill wait.
            MatchmakingTicket lateWaiter = Ticket(rating: 1000, powerScore: 20, waitedSeconds: 30);
            MatchmakingTicket other      = Ticket(rating: 4000, powerScore: 120, waitedSeconds: 26);

            MatchmakingPolicy.Wait wait = (MatchmakingPolicy.Wait)MatchmakingPolicy.Evaluate(Queue(lateWaiter, other), Now, Schedule);

            Assert.That(wait.NextEvaluationAt, Is.EqualTo(lateWaiter.ArrivedAt + MetaDuration.FromSeconds(45)));
        }

        [Test]
        public void EvaluateDoesNotReorderTheCallersQueue()
        {
            // The host holds the queue in arrival order and removes by identity; a policy that sorted in place
            // would be quietly rewriting the caller's list on every wake-up.
            MatchmakingTicket young = Ticket(rating: 1000, powerScore: 60, waitedSeconds: 1);
            MatchmakingTicket old   = Ticket(rating: 4000, powerScore: 20, waitedSeconds: 30);

            List<MatchmakingTicket> queue = Queue(young, old);
            MatchmakingPolicy.Evaluate(queue, Now, Schedule);

            Assert.That(queue[0], Is.SameAs(young));
            Assert.That(queue[1], Is.SameAs(old));
        }

        [Test]
        public void OneAccountIsNeverPairedWithItself()
        {
            // Two tickets for one account are compatible with each other at a gap of zero, so nothing about
            // the bands stops this — an actor that restarted while a requeue was in flight is the way a queue
            // ends up holding both. The seat reservation would decline the second ask and unwind it; the
            // pairing invariant should not have to rest on the unwinding.
            MatchmakingTicket first = Ticket(rating: 1000, powerScore: 60, waitedSeconds: 30);
            MatchmakingTicket ghost = TicketFor(first.PlayerId, first.Rating, first.PowerScore, first.ArrivedAt + MetaDuration.FromSeconds(1));

            MatchmakingPolicy.Verdict verdict = MatchmakingPolicy.Evaluate(Queue(first, ghost), Now, Schedule);

            Assert.That(verdict, Is.Not.InstanceOf<MatchmakingPolicy.FormPair>(),
                "a queue holding one account twice must not pair it with itself");

            // And with a real partner present, that is who it takes.
            MatchmakingTicket partner = Ticket(rating: 1005, powerScore: 61, waitedSeconds: 5);
            MatchmakingPolicy.FormPair pair = (MatchmakingPolicy.FormPair)MatchmakingPolicy.Evaluate(Queue(first, ghost, partner), Now, Schedule);

            Assert.That(pair.A.PlayerId, Is.Not.EqualTo(pair.B.PlayerId));
            Assert.That(pair.B, Is.SameAs(partner));
        }

        [Test]
        public void AnArrivalStampInTheFutureIsTreatedAsNoWaitAtAll()
        {
            // Clock skew between the actor and whatever stamped the ticket must not make a negative wait, which
            // would put a ticket in no band step at all.
            MatchmakingTicket future = Ticket(rating: 1000, powerScore: 60, waitedSeconds: -5);

            Assert.That(MatchmakingPolicy.Waited(future, Now), Is.EqualTo(MetaDuration.Zero));
            Assert.That(MatchmakingPolicy.BandFor(future, Now, Schedule).PowerScoreBand, Is.EqualTo(10));
        }
    }
}
