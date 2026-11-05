using Metaplay.Core;
using NUnit.Framework;
using System;
using System.Collections.Generic;

namespace Game.Logic.Tests
{
    /// <summary>
    /// Tests for <see cref="FormationOutcomePolicy"/>: what the matchmaker owes each player of a four-seat formation
    /// once its seat reservations and mint have returned (<c>docs/matchmaking.md</c>, "Forming a table").
    /// </summary>
    [TestFixture]
    public class FormationOutcomePolicyTests
    {
        static readonly MetaTime T0 = MatchTestDeals.T0;

        static EntityId PlayerId(int index) => EntityId.Create(EntityKindCore.Player, (ulong)(2000 + index));

        static List<MatchmakingWaiter> FourWaiters()
        {
            List<MatchmakingWaiter> formation = new List<MatchmakingWaiter>();
            for (int index = 0; index < MatchRules.NumSeats; index++)
                formation.Add(new MatchmakingWaiter(PlayerPublicIdentity.ForSeat(PlayerId(index), $"Queued {index}"), T0 + MetaDuration.FromSeconds(index)));
            return formation;
        }

        static SeatAskReply Accept(int index) => SeatAskReply.Accepted(PlayerPublicIdentity.ForSeat(PlayerId(index), $"Renamed {index}"));

        static List<EntityId> Ids(List<MatchmakingWaiter> waiters) => waiters.ConvertAll(waiter => waiter.PlayerId);

        static FormationLateChange[] NoLateChanges() => new FormationLateChange[MatchRules.NumSeats];

        static FormationOutcome Decide(List<MatchmakingWaiter> formation, SeatAskReply[] replies, FormationMintResult mint) => FormationOutcomePolicy.Decide(formation, replies, mint, NoLateChanges());

        static List<(EntityId, bool)> Releases(FormationOutcome outcome) => outcome.Releases.ConvertAll(release => (release.PlayerId, release.IsStillQueued));

        static MatchmakingWaiter QueuedAgain(int index) => new MatchmakingWaiter(PlayerPublicIdentity.ForSeat(PlayerId(index), $"Again {index}"), T0 + MetaDuration.FromSeconds(100));

        [Test]
        public void AMintedTableSeatsTheAcceptedAndReleasesTheUnanswered()
        {
            List<MatchmakingWaiter> formation = FourWaiters();
            SeatAskReply[]          replies   = { Accept(0), SeatAskReply.Declined(), SeatAskReply.Unanswered(), Accept(3) };

            FormationOutcome outcome = Decide(formation, replies, FormationMintResult.Succeeded);

            Assert.That(Ids(outcome.Seated), Is.EqualTo(new[] { PlayerId(0), PlayerId(3) }), "in queue order");
            Assert.That(Releases(outcome), Is.EqualTo(new[] { (PlayerId(2), false) }), "an unanswered player may have committed, so they are told");
            Assert.That(outcome.ReturnToHead, Is.Empty, "an unanswered actor at the head of the queue would stall the next formation");
            Assert.That(outcome.Enqueue, Is.Empty);
        }

        [Test]
        public void ASeatUsesTheIdentityTheActorReturnedAndKeepsTheEnqueueTime()
        {
            List<MatchmakingWaiter> formation = FourWaiters();
            SeatAskReply[]          replies   = { SeatAskReply.Declined(), Accept(1), SeatAskReply.Declined(), SeatAskReply.Declined() };

            FormationOutcome outcome = Decide(formation, replies, FormationMintResult.Succeeded);

            Assert.That(outcome.Seated[0].DisplayName, Is.EqualTo("Renamed 1"), "a rename during the search reaches the table");
            Assert.That(outcome.Seated[0].EnqueuedAt, Is.EqualTo(formation[1].EnqueuedAt));
            Assert.That(FormationOutcomePolicy.Accepted(formation, replies)[0].DisplayName, Is.EqualTo("Renamed 1"), "the seats are built from the same identities");
        }

        [Test]
        public void AFailedMintRequeuesEveryoneWhoDidNotDeclineWithTheirOriginalEnqueueTimes()
        {
            List<MatchmakingWaiter> formation = FourWaiters();
            SeatAskReply[]          replies   = { SeatAskReply.Unanswered(), Accept(1), SeatAskReply.Declined(), Accept(3) };

            FormationOutcome outcome = Decide(formation, replies, FormationMintResult.Failed);

            Assert.That(outcome.Seated, Is.Empty);
            Assert.That(Ids(outcome.ReturnToHead), Is.EqualTo(new[] { PlayerId(0), PlayerId(1), PlayerId(3) }), "in queue order, for the head of the queue");
            Assert.That(outcome.ReturnToHead[0].DisplayName, Is.EqualTo("Queued 0"), "a player who did not answer keeps their queue identity");
            Assert.That(outcome.ReturnToHead[1].DisplayName, Is.EqualTo("Renamed 1"));
            Assert.That(Releases(outcome), Is.EqualTo(new[] { (PlayerId(0), true), (PlayerId(1), true), (PlayerId(3), true) }));

            for (int index = 0; index < outcome.ReturnToHead.Count; index++)
                Assert.That(outcome.ReturnToHead[index].EnqueuedAt, Is.EqualTo(formation[formation.FindIndex(waiter => waiter.PlayerId == outcome.ReturnToHead[index].PlayerId)].EnqueuedAt), "the fill wait still runs from when they joined");
        }

        [Test]
        public void WithNoAcceptanceNothingIsMintedAndTheUnansweredAreReleased()
        {
            List<MatchmakingWaiter> formation = FourWaiters();
            SeatAskReply[]          replies   = { SeatAskReply.Declined(), SeatAskReply.Unanswered(), SeatAskReply.Declined(), SeatAskReply.Unanswered() };

            Assert.That(FormationOutcomePolicy.Accepted(formation, replies), Is.Empty, "a table is never created without a live human");

            FormationOutcome outcome = Decide(formation, replies, FormationMintResult.NotAttempted);

            Assert.That(outcome.Seated, Is.Empty);
            Assert.That(Releases(outcome), Is.EqualTo(new[] { (PlayerId(1), false), (PlayerId(3), false) }));
            Assert.That(outcome.ReturnToHead, Is.Empty);
        }

        [Test]
        public void OneAcceptanceIsEnoughToMint()
        {
            SeatAskReply[] replies = { SeatAskReply.Declined(), SeatAskReply.Unanswered(), Accept(2), SeatAskReply.Declined() };

            Assert.That(FormationOutcomePolicy.Accepted(FourWaiters(), replies), Has.Count.EqualTo(1), "bots fill the other seats");
        }

        [Test]
        public void AnEntryMadeDuringTheFormationIsQueuedUnlessThePlayerWasSeated()
        {
            List<MatchmakingWaiter> formation   = FourWaiters();
            SeatAskReply[]          replies     = { Accept(0), SeatAskReply.Unanswered(), SeatAskReply.Declined(), Accept(3) };
            FormationLateChange[]   lateChanges = { FormationLateChange.PlayAgain(QueuedAgain(0)), FormationLateChange.PlayAgain(QueuedAgain(1)), FormationLateChange.PlayAgain(QueuedAgain(2)), FormationLateChange.None };

            FormationOutcome outcome = FormationOutcomePolicy.Decide(formation, replies, FormationMintResult.Succeeded, lateChanges);

            Assert.That(Ids(outcome.Seated), Is.EqualTo(new[] { PlayerId(0), PlayerId(3) }));
            Assert.That(Ids(outcome.Enqueue), Is.EqualTo(new[] { PlayerId(1), PlayerId(2) }), "a seated player's new entry is dropped");
            Assert.That(Releases(outcome), Is.EqualTo(new[] { (PlayerId(1), true) }), "the released player is told the queue still holds them");
        }

        [Test]
        public void AFailedMintReturnsAPlayerWhoPressedPlayAgainWithTheNewIdentityAndSkipsOneWhoCancelled()
        {
            List<MatchmakingWaiter> formation   = FourWaiters();
            SeatAskReply[]          replies     = { SeatAskReply.Unanswered(), SeatAskReply.Unanswered(), Accept(2), SeatAskReply.Declined() };
            FormationLateChange[]   lateChanges = { FormationLateChange.PlayAgain(QueuedAgain(0)), FormationLateChange.Cancelled(), FormationLateChange.None, FormationLateChange.None };

            FormationOutcome outcome = FormationOutcomePolicy.Decide(formation, replies, FormationMintResult.Failed, lateChanges);

            Assert.That(Ids(outcome.ReturnToHead), Is.EqualTo(new[] { PlayerId(0), PlayerId(2) }));
            Assert.That(outcome.ReturnToHead[0].DisplayName, Is.EqualTo("Again 0"));
            Assert.That(outcome.ReturnToHead[0].EnqueuedAt, Is.EqualTo(formation[0].EnqueuedAt), "the player keeps their place in the queue");
            Assert.That(outcome.Enqueue, Is.Empty);
            Assert.That(Releases(outcome), Is.EqualTo(new[] { (PlayerId(0), true), (PlayerId(1), false), (PlayerId(2), true) }));
        }

        [Test]
        public void ADeclinedPlayerIsOwedNothingWhateverTheMint()
        {
            foreach (FormationMintResult mint in new[] { FormationMintResult.NotAttempted, FormationMintResult.Succeeded, FormationMintResult.Failed })
                Assert.That(FormationOutcomePolicy.ForSeat(SeatAskResult.Declined, mint), Is.EqualTo(FormationSeatAction.Nothing), $"mint {mint}");
        }

        [Test]
        public void AnAcceptedSeatWithoutAMintIsRejected()
        {
            Assert.Throws<ArgumentException>(() => FormationOutcomePolicy.ForSeat(SeatAskResult.Accepted, FormationMintResult.NotAttempted));
        }

        [Test]
        public void RepliesMustMatchTheFormation()
        {
            Assert.Throws<ArgumentException>(() => FormationOutcomePolicy.Decide(FourWaiters(), new[] { Accept(0) }, FormationMintResult.Succeeded, NoLateChanges()));
        }
    }
}
