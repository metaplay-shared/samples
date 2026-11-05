using Metaplay.Core;
using System;
using System.Collections.Generic;

namespace Game.Logic
{
    /// <summary>What a formation's seat reservation ask to one player's actor came back with.</summary>
    public enum SeatAskResult
    {
        /// <summary>The answer never arrived. The actor may or may not have committed.</summary>
        Unanswered,

        /// <summary>The actor answered and committed to the seat.</summary>
        Accepted,

        /// <summary>The actor answered and did not commit: cancelled, already at a table, or not connected.</summary>
        Declined,
    }

    /// <summary>One player's answer to a seat reservation ask, with the identity an accepting actor returned.</summary>
    public readonly struct SeatAskReply
    {
        public readonly SeatAskResult Result;

        /// <summary>The player's identity when they accepted. Null unless <see cref="Result"/> is <see cref="SeatAskResult.Accepted"/>.</summary>
        public readonly PlayerPublicIdentity Identity;

        SeatAskReply(SeatAskResult result, PlayerPublicIdentity identity)
        {
            Result   = result;
            Identity = identity;
        }

        public static SeatAskReply Accepted(PlayerPublicIdentity identity) => new SeatAskReply(SeatAskResult.Accepted, identity ?? throw new ArgumentNullException(nameof(identity)));
        public static SeatAskReply Declined()                              => new SeatAskReply(SeatAskResult.Declined, identity: null);
        public static SeatAskReply Unanswered()                            => new SeatAskReply(SeatAskResult.Unanswered, identity: null);

        public override string ToString() => Result.ToString();
    }

    /// <summary>
    /// What a player did with the queue while their formation was waiting on its asks and mint. The matchmaker holds
    /// these until the formation is applied, so a player is in at most one formation at a time.
    /// </summary>
    public readonly struct FormationLateChange
    {
        /// <summary>The player cancelled and has not pressed Play again.</summary>
        public readonly bool IsCancelled;

        /// <summary>The player pressed Play again. <see cref="QueuedAgain"/> is their new entry.</summary>
        public readonly bool HasQueuedAgain;

        public readonly MatchmakingWaiter QueuedAgain;

        FormationLateChange(bool isCancelled, bool hasQueuedAgain, MatchmakingWaiter queuedAgain)
        {
            IsCancelled    = isCancelled;
            HasQueuedAgain = hasQueuedAgain;
            QueuedAgain    = queuedAgain;
        }

        public static readonly FormationLateChange None = default;

        public static FormationLateChange Cancelled()                          => new FormationLateChange(isCancelled: true, hasQueuedAgain: false, default);
        public static FormationLateChange PlayAgain(MatchmakingWaiter waiter) => new FormationLateChange(isCancelled: false, hasQueuedAgain: true, waiter);
    }

    /// <summary>Whether a formation minted its table.</summary>
    public enum FormationMintResult
    {
        /// <summary>No player accepted, so no mint was attempted.</summary>
        NotAttempted,

        Succeeded,

        Failed,
    }

    /// <summary>What the matchmaker owes one player once their formation has finished, from their answer and the mint.</summary>
    public enum FormationSeatAction
    {
        /// <summary>The player declined. Their actor already left the search, so nothing is owed.</summary>
        Nothing,

        /// <summary>The table exists with this player seated. Tell them their table.</summary>
        Seated,

        /// <summary>Tell the player their seat will not be filled. They are not put back in the queue.</summary>
        Released,

        /// <summary>Put the player back at the head of the queue with their original enqueue time, and release their seat.</summary>
        Requeued,
    }

    /// <summary>A seat release to send to one player.</summary>
    public readonly struct FormationRelease
    {
        public readonly EntityId PlayerId;

        /// <summary>Whether the queue holds the player once the formation is applied.</summary>
        public readonly bool IsStillQueued;

        public FormationRelease(EntityId playerId, bool isStillQueued)
        {
            PlayerId      = playerId;
            IsStillQueued = isStillQueued;
        }
    }

    /// <summary>
    /// What the matchmaker does once a formation has finished. Each list is in queue order.
    /// </summary>
    public sealed class FormationOutcome
    {
        /// <summary>The players at the table, with the identities their actors returned.</summary>
        public readonly List<MatchmakingWaiter> Seated;

        /// <summary>The players to return to the head of the queue, with their original enqueue times.</summary>
        public readonly List<MatchmakingWaiter> ReturnToHead;

        /// <summary>The entries of players who pressed Play again during the formation, to add to the queue.</summary>
        public readonly List<MatchmakingWaiter> Enqueue;

        /// <summary>The seat releases to send.</summary>
        public readonly List<FormationRelease> Releases;

        public FormationOutcome(List<MatchmakingWaiter> seated, List<MatchmakingWaiter> returnToHead, List<MatchmakingWaiter> enqueue, List<FormationRelease> releases)
        {
            Seated       = seated;
            ReturnToHead = returnToHead;
            Enqueue      = enqueue;
            Releases     = releases;
        }
    }

    /// <summary>
    /// The bookkeeping after a formation's seat reservations and mint, as pure functions
    /// (<c>docs/matchmaking.md</c>, "Forming a table"). The matchmaker computes the outcome on its own actor context
    /// after the formation's asks and mint have returned, and then carries it out.
    /// <para>
    /// A player who declined is owed nothing. A player whose answer was lost is released and not re-queued, because
    /// an actor that could not answer once would stall the next formation too. When the mint fails, every player who
    /// did not decline goes back to the head of the queue, because the failure is not theirs.
    /// </para>
    /// <para>
    /// A player who pressed Play again during the formation is queued with their new entry unless they were seated,
    /// and a failed mint returns them to the head with their new identity. A player who cancelled is not returned to
    /// the queue.
    /// </para>
    /// </summary>
    public static class FormationOutcomePolicy
    {
        /// <summary>
        /// The players who accepted, in queue order, with the identities their actors returned and their original
        /// enqueue times. The seats are built from these, so a rename or cosmetic change made during the search
        /// reaches the table. A table is minted only if this is not empty, so a table is never created without a
        /// live human.
        /// </summary>
        public static List<MatchmakingWaiter> Accepted(IReadOnlyList<MatchmakingWaiter> formation, IReadOnlyList<SeatAskReply> replies)
        {
            CheckSameLength(formation, replies.Count);

            List<MatchmakingWaiter> accepted = new List<MatchmakingWaiter>(formation.Count);
            for (int index = 0; index < formation.Count; index++)
            {
                if (replies[index].Result == SeatAskResult.Accepted)
                    accepted.Add(Current(formation[index], replies[index]));
            }
            return accepted;
        }

        /// <summary>What one player is owed, from their own answer and the mint result.</summary>
        public static FormationSeatAction ForSeat(SeatAskResult own, FormationMintResult mint)
        {
            if (own == SeatAskResult.Declined)
                return FormationSeatAction.Nothing;

            if (mint == FormationMintResult.Failed)
                return FormationSeatAction.Requeued;

            if (own == SeatAskResult.Unanswered)
                return FormationSeatAction.Released;

            if (mint == FormationMintResult.Succeeded)
                return FormationSeatAction.Seated;

            throw new ArgumentException("A player accepted, so a mint must have been attempted", nameof(mint));
        }

        /// <summary>
        /// The outcome of a formation. <paramref name="replies"/> and <paramref name="lateChanges"/> hold one entry
        /// per waiter in <paramref name="formation"/>, in the same order.
        /// </summary>
        public static FormationOutcome Decide(IReadOnlyList<MatchmakingWaiter> formation, IReadOnlyList<SeatAskReply> replies, FormationMintResult mint, IReadOnlyList<FormationLateChange> lateChanges)
        {
            CheckSameLength(formation, replies.Count);
            CheckSameLength(formation, lateChanges.Count);

            List<MatchmakingWaiter> seated       = new List<MatchmakingWaiter>(formation.Count);
            List<MatchmakingWaiter> returnToHead = new List<MatchmakingWaiter>();
            List<MatchmakingWaiter> enqueue      = new List<MatchmakingWaiter>();
            List<FormationRelease>  releases     = new List<FormationRelease>();

            for (int index = 0; index < formation.Count; index++)
            {
                MatchmakingWaiter   waiter = Current(formation[index], replies[index]);
                FormationLateChange late   = lateChanges[index];

                switch (ForSeat(replies[index].Result, mint))
                {
                    case FormationSeatAction.Seated:
                        // The table holds the player, so an entry made since is dropped.
                        seated.Add(waiter);
                        break;

                    case FormationSeatAction.Nothing:
                    case FormationSeatAction.Released:
                        if (late.HasQueuedAgain)
                            enqueue.Add(late.QueuedAgain);
                        if (replies[index].Result != SeatAskResult.Declined)
                            releases.Add(new FormationRelease(waiter.PlayerId, isStillQueued: late.HasQueuedAgain));
                        break;

                    case FormationSeatAction.Requeued:
                        if (late.IsCancelled)
                        {
                            releases.Add(new FormationRelease(waiter.PlayerId, isStillQueued: false));
                            break;
                        }

                        returnToHead.Add(late.HasQueuedAgain ? new MatchmakingWaiter(late.QueuedAgain.Identity, waiter.EnqueuedAt) : waiter);
                        releases.Add(new FormationRelease(waiter.PlayerId, isStillQueued: true));
                        break;
                }
            }

            return new FormationOutcome(seated, returnToHead, enqueue, releases);
        }

        /// <summary>
        /// A waiter with the identity an accepting actor returned. A player who did not accept keeps the identity
        /// from their queue entry.
        /// </summary>
        static MatchmakingWaiter Current(MatchmakingWaiter queued, SeatAskReply reply) =>
            reply.Result == SeatAskResult.Accepted ? new MatchmakingWaiter(reply.Identity, queued.EnqueuedAt) : queued;

        static void CheckSameLength(IReadOnlyList<MatchmakingWaiter> formation, int count)
        {
            if (formation == null)
                throw new ArgumentNullException(nameof(formation));
            if (formation.Count != count)
                throw new ArgumentException($"{formation.Count} waiters but {count} entries");
        }
    }
}
