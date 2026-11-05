using Game.Logic;
using Metaplay.Core;

namespace Game.Server.Match
{
    /// <summary>
    /// The retry state that <see cref="MatchActor"/> keeps beside the model: the retry after a refused publish and
    /// the retry of undelivered results. It also decides the actor's next wake from them and the model's own stamps.
    /// It is kept out of the actor so tests can drive it without a live actor.
    /// <para>
    /// None of it is persisted. A refused publish does not change the model, a restored table runs from its model,
    /// and a restored table that owes results sends them on its first run.
    /// </para>
    /// </summary>
    public sealed class MatchActorRetries
    {
        /// <summary>
        /// How long the table waits before running again after a refused publish. <see cref="MaxPublishRetries"/>
        /// is how many retries it makes before it stops running itself. Together they prevent a repeated refusal
        /// from causing a busy loop or retrying forever.
        /// </summary>
        public static readonly MetaDuration PublishRetryDelay = MetaDuration.FromSeconds(1);

        public const int MaxPublishRetries = 5;

        /// <summary>
        /// How long a finished table waits before sending an unacknowledged result again. A player's actor that
        /// did not answer is usually still waking up, so the delay is short.
        /// </summary>
        public static readonly MetaDuration ResultRetryDelay = MetaDuration.FromSeconds(5);

        /// <summary>
        /// After this many result rounds in a row with a failed delivery, the actor logs an error. It keeps retrying,
        /// because the player's result must not be lost.
        /// </summary>
        public const int FailedResultRoundsBeforeError = 12;

        /// <summary>When to run the table again after a refused publish, or <see cref="MetaTime.Epoch"/>.</summary>
        public MetaTime PublishRetryAt { get; private set; } = MetaTime.Epoch;

        public int PublishRetries { get; private set; }

        /// <summary>
        /// True after <see cref="MaxPublishRetries"/> refused publishes. While set, no wakes are scheduled, so only a
        /// client message runs the table. A successful publish clears it.
        /// </summary>
        public bool PublishRetriesExhausted { get; private set; }

        /// <summary>When to send undelivered results again, or <see cref="MetaTime.Epoch"/> when none is due.</summary>
        public MetaTime ResultRetryAt { get; private set; } = MetaTime.Epoch;

        /// <summary>How many result rounds in a row had a failed delivery.</summary>
        public int FailedResultRounds { get; private set; }

        /// <summary>
        /// Update the publish retry after a pass of the table. <paramref name="anySucceeded"/> and
        /// <paramref name="anyRefused"/> can both be true after one pass.
        /// </summary>
        /// <returns>True when this pass used up the last retry, so the caller logs it once.</returns>
        public bool AfterPass(bool anySucceeded, bool anyRefused, MetaTime now)
        {
            // A successful publish means the table advanced, so reset the retry count and resume running the table.
            if (anySucceeded)
            {
                PublishRetries          = 0;
                PublishRetriesExhausted = false;
            }

            if (!anyRefused)
            {
                PublishRetryAt = MetaTime.Epoch;
                return false;
            }

            if (PublishRetries >= MaxPublishRetries)
            {
                bool justExhausted = !PublishRetriesExhausted;

                PublishRetriesExhausted = true;
                return justExhausted;
            }

            PublishRetries++;
            PublishRetryAt = now + PublishRetryDelay;
            return false;
        }

        /// <summary>
        /// A round of result deliveries started, or nothing is owed. No retry is due while a round is in flight,
        /// because an ask can take longer than <see cref="ResultRetryDelay"/>.
        /// </summary>
        public void ClearResultRetry()
        {
            ResultRetryAt = MetaTime.Epoch;
        }

        /// <summary>A round of result deliveries finished with <paramref name="numFailed"/> failed deliveries.</summary>
        /// <returns>True when the failures have reached <see cref="FailedResultRoundsBeforeError"/> rounds in a row.</returns>
        public bool AfterResultRound(int numFailed, MetaTime now)
        {
            if (numFailed == 0)
            {
                FailedResultRounds = 0;
                ResultRetryAt      = MetaTime.Epoch;
                return false;
            }

            FailedResultRounds++;
            ResultRetryAt = now + ResultRetryDelay;
            return FailedResultRounds == FailedResultRoundsBeforeError;
        }

        /// <summary>
        /// When the table next needs to run: the earliest of the model's stamps, the held bot move, the publish retry
        /// and the result retry, or <see cref="MetaTime.Epoch"/> for none. Nothing is scheduled while
        /// <see cref="PublishRetriesExhausted"/> is set, because the pending stamps are ones the table just failed to
        /// act on, and scheduling them would wake the table every <see cref="DeadlineWakeup.Padding"/>.
        /// </summary>
        public MetaTime NextWakeAt(MatchModel model, MatchPendingBotMove pendingBotMove)
        {
            if (PublishRetriesExhausted)
                return MetaTime.Epoch;

            // Include the result retry. Without it, a table that ended while a player's actor was unreachable
            // would never send the result again (docs/match.md, "Results").
            return MatchHost.Earlier(MatchHost.Earlier(MatchHost.GetNextWakeAt(model, pendingBotMove), PublishRetryAt), ResultRetryAt);
        }
    }
}
