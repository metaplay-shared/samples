using Game.Logic;
using Game.Logic.Tests;
using Game.Server.Match;
using Metaplay.Core;
using NUnit.Framework;

namespace Game.Server.Tests
{
    /// <summary>
    /// Tests the retry state that <see cref="MatchActor"/> keeps beside the model, and the next wake it schedules
    /// from it. A wrong wake either stops a table for good or wakes it in a busy loop, and neither shows in a test of
    /// the model alone.
    /// </summary>
    [TestFixture]
    public class MatchActorRetriesTests
    {
        const ulong Seed = 4242UL;

        static readonly MetaTime T0 = MatchTestDeals.T0;

        /// <summary>A table in its join window. Its own next wake is the end of the window.</summary>
        static MatchModel TableInJoinWindow() => MatchTestDeals.NewTable(Seed, MatchTestDeals.SeatTimings());

        static MetaTime JoinWindowEndsAt => T0 + MatchTestDeals.SeatTimings().JoinWindow;

        static MatchModel FinishedTable()
        {
            MatchModel model = MatchTestDeals.StartedTable(Seed, MatchTestDeals.SeatTimings(), out TestMatchHost host);
            MatchTestDeals.PlayWholeGame(model, host, Seed);
            return model;
        }

        static MatchActorRetries Exhausted(MetaTime now)
        {
            MatchActorRetries retries = new MatchActorRetries();
            for (int pass = 0; pass <= MatchActorRetries.MaxPublishRetries; pass++)
                retries.AfterPass(anySucceeded: false, anyRefused: true, now);
            return retries;
        }

        [Test]
        public void WithNoRefusalTheTableWakesOnItsOwnStamps()
        {
            MatchActorRetries retries = new MatchActorRetries();

            Assert.That(retries.AfterPass(anySucceeded: true, anyRefused: false, T0), Is.False);
            Assert.That(retries.NextWakeAt(TableInJoinWindow(), null), Is.EqualTo(JoinWindowEndsAt));
        }

        [Test]
        public void ARefusedPublishWakesTheTableAfterTheRetryDelay()
        {
            MatchActorRetries retries = new MatchActorRetries();

            retries.AfterPass(anySucceeded: false, anyRefused: true, T0);

            Assert.That(retries.NextWakeAt(TableInJoinWindow(), null), Is.EqualTo(T0 + MatchActorRetries.PublishRetryDelay));
        }

        [Test]
        public void RetriesStopAfterTheLimit_ReportedOnce_AndNothingIsScheduled()
        {
            MatchActorRetries retries = new MatchActorRetries();
            for (int pass = 0; pass < MatchActorRetries.MaxPublishRetries; pass++)
                Assert.That(retries.AfterPass(anySucceeded: false, anyRefused: true, T0), Is.False, $"pass {pass}");

            Assert.That(retries.AfterPass(anySucceeded: false, anyRefused: true, T0), Is.True, "the pass that uses up the retries reports it");
            Assert.That(retries.AfterPass(anySucceeded: false, anyRefused: true, T0), Is.False, "a later refusal does not report it again");
            Assert.That(retries.NextWakeAt(TableInJoinWindow(), null), Is.EqualTo(MetaTime.Epoch), "an exhausted table must not wake in a loop");
        }

        [Test]
        public void ASuccessfulPublishResumesAnExhaustedTable()
        {
            MatchActorRetries retries = Exhausted(T0);

            retries.AfterPass(anySucceeded: true, anyRefused: false, T0);

            Assert.That(retries.PublishRetriesExhausted, Is.False);
            Assert.That(retries.NextWakeAt(TableInJoinWindow(), null), Is.EqualTo(JoinWindowEndsAt));
        }

        [Test]
        public void AFinishedTableOwingAResultStillWakes_UntilTheResultIsDelivered()
        {
            MatchModel        model   = FinishedTable();
            MatchActorRetries retries = new MatchActorRetries();
            Assert.That(retries.NextWakeAt(model, null), Is.EqualTo(MetaTime.Epoch), "a finished table has no stamps of its own");

            retries.AfterResultRound(numFailed: 1, T0);
            Assert.That(retries.NextWakeAt(model, null), Is.EqualTo(T0 + MatchActorRetries.ResultRetryDelay));

            retries.AfterResultRound(numFailed: 0, T0);
            Assert.That(retries.NextWakeAt(model, null), Is.EqualTo(MetaTime.Epoch));
        }

        [Test]
        public void RepeatedFailedResultRoundsAreReportedOnce_AndADeliveryResetsTheCount()
        {
            MatchActorRetries retries = new MatchActorRetries();
            for (int round = 1; round < MatchActorRetries.FailedResultRoundsBeforeError; round++)
                Assert.That(retries.AfterResultRound(numFailed: 1, T0), Is.False, $"round {round}");

            Assert.That(retries.AfterResultRound(numFailed: 1, T0), Is.True, "the round that reaches the limit reports it");
            Assert.That(retries.AfterResultRound(numFailed: 1, T0), Is.False, "later rounds do not report it again");
            Assert.That(retries.ResultRetryAt, Is.EqualTo(T0 + MatchActorRetries.ResultRetryDelay), "the table keeps retrying after the report");

            retries.AfterResultRound(numFailed: 0, T0);
            Assert.That(retries.FailedResultRounds, Is.Zero);
        }
    }
}
