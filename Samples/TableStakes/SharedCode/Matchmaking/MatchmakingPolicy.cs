using Metaplay.Core;
using System;
using System.Collections.Generic;

namespace Game.Logic
{
    /// <summary>
    /// One player waiting for a table: their identity and when they joined the queue. It is not
    /// <c>[MetaSerializable]</c> because the queue exists only in the matchmaker's memory. A waiting player has a
    /// live connection, so the queue does not need to survive a restart
    /// (<c>docs/matchmaking.md</c>, "The matchmaker entity").
    /// </summary>
    public readonly struct MatchmakingWaiter
    {
        /// <summary>
        /// The player's identity when they joined the queue, used in logs and to name the waiter in a formation.
        /// The seat uses the identity the player actor returns when it takes the seat, so a rename or cosmetic
        /// change during the search still appears at the table.
        /// </summary>
        public readonly PlayerPublicIdentity Identity;

        /// <summary>When this player joined the queue. The fill wait is measured from the oldest waiter.</summary>
        public readonly MetaTime EnqueuedAt;

        public MatchmakingWaiter(PlayerPublicIdentity identity, MetaTime enqueuedAt)
        {
            Identity   = identity;
            EnqueuedAt = enqueuedAt;
        }

        public EntityId PlayerId    => Identity?.PlayerId ?? EntityId.None;
        public string   DisplayName => Identity?.DisplayName;

        public override string ToString() => $"{PlayerId} ({DisplayName}) since {EnqueuedAt}";
    }

    /// <summary>
    /// The player actor's answer when a formation offers the player a seat, with the reason for a refusal. The
    /// actor handles each refusal differently: only <see cref="AlreadyAtTable"/> and <see cref="NotLive"/> mean
    /// the player is no longer a candidate.
    /// </summary>
    public enum SeatReservationAnswer
    {
        /// <summary>Take the seat. After this, the player's cancel is refused.</summary>
        Take = 0,

        /// <summary>The player is not searching: they cancelled or never asked.</summary>
        NotSearching = 1,

        /// <summary>The player is already at a table, and a second seat would leave one of the two unplayed.</summary>
        AlreadyAtTable = 2,

        /// <summary>The player has no session, or no client connected to it, to play the seat.</summary>
        NotLive = 3,
    }

    /// <summary>
    /// What the matchmaker does now.
    /// </summary>
    public enum MatchmakingAction
    {
        /// <summary>Nothing to form yet. <see cref="MatchmakingDecision.NextLookAt"/> says when to look again.</summary>
        Wait = 0,

        /// <summary>Form a table from the head of the queue.</summary>
        Form = 1,
    }

    /// <summary>
    /// The result of <see cref="MatchmakingPolicy.Decide"/>: form a table now from the head of the queue, or wait
    /// until a given time. It is a pure function of the queue and the time, so it can be tested without an actor
    /// (<c>docs/matchmaking.md</c>, "The matchmaker entity").
    /// </summary>
    public readonly struct MatchmakingDecision
    {
        public readonly MatchmakingAction Action;

        /// <summary>How many waiters from the head of the queue the formation seats. Zero unless forming.</summary>
        public readonly int NumWaitersToSeat;

        /// <summary>
        /// When the matchmaker must check the queue again, or <see cref="MetaTime.Epoch"/> for an empty queue or
        /// a <see cref="MatchmakingAction.Form"/> decision. A waiting decision always has a time, so the actor
        /// always sets a timer while the queue has waiters.
        /// </summary>
        public readonly MetaTime NextLookAt;

        MatchmakingDecision(MatchmakingAction action, int numWaitersToSeat, MetaTime nextLookAt)
        {
            Action           = action;
            NumWaitersToSeat = numWaitersToSeat;
            NextLookAt       = nextLookAt;
        }

        public static MatchmakingDecision Form(int numWaitersToSeat) => new MatchmakingDecision(MatchmakingAction.Form, numWaitersToSeat, MetaTime.Epoch);
        public static MatchmakingDecision Wait(MetaTime nextLookAt)  => new MatchmakingDecision(MatchmakingAction.Wait, 0, nextLookAt);
        public static MatchmakingDecision Idle()                     => new MatchmakingDecision(MatchmakingAction.Wait, 0, MetaTime.Epoch);

        public override string ToString() =>
            Action == MatchmakingAction.Form ? $"form a table from {NumWaitersToSeat} waiters"
            : NextLookAt == MetaTime.Epoch   ? "wait for an arrival"
            :                                  $"wait until {NextLookAt}";
    }

    /// <summary>
    /// The matchmaker's queue: waiting players in arrival order, with at most one entry per player. It is a separate
    /// type so two rules can be tested without an actor: a second entry for a waiting player is refused
    /// (<c>docs/matchmaking.md</c>, "Queue and fill wait"), and waiters from a failed formation return to the head of
    /// the queue. The queue has no maximum size and can hold a full table or more, for example during a formation.
    /// </summary>
    public sealed class MatchmakingQueue
    {
        readonly List<MatchmakingWaiter> _waiters = new List<MatchmakingWaiter>();

        /// <summary>The waiters, oldest first.</summary>
        public IReadOnlyList<MatchmakingWaiter> Waiters => _waiters;

        public int Count => _waiters.Count;

        public bool Contains(EntityId playerId) => IndexOf(playerId) >= 0;

        /// <summary>
        /// Append a waiter. A player already in the queue, or with an invalid id, is refused. Otherwise a double tap
        /// on Play, or a second browser tab on the same account, could seat one player twice.
        /// </summary>
        /// <returns>Whether the waiter was appended.</returns>
        public bool TryEnqueue(MatchmakingWaiter waiter)
        {
            if (!waiter.PlayerId.IsValid)
                return false;
            if (Contains(waiter.PlayerId))
                return false;

            _waiters.Add(waiter);
            return true;
        }

        /// <summary>Remove a player's entry, if they have one.</summary>
        /// <returns>Whether an entry was removed.</returns>
        public bool Remove(EntityId playerId)
        {
            int index = IndexOf(playerId);
            if (index < 0)
                return false;

            _waiters.RemoveAt(index);
            return true;
        }

        /// <summary>
        /// Remove up to <paramref name="count"/> waiters from the head of the queue and return them. A formation
        /// does this first, so a cancel that arrives during the formation finds no entry and cannot leave a seat
        /// without a player.
        /// </summary>
        public List<MatchmakingWaiter> TakeFromHead(int count)
        {
            if (count < 0)
                throw new ArgumentOutOfRangeException(nameof(count));

            int taken = Math.Min(count, _waiters.Count);
            List<MatchmakingWaiter> head = _waiters.GetRange(0, taken);
            _waiters.RemoveRange(0, taken);
            return head;
        }

        /// <summary>
        /// Put waiters back at the head of the queue in their original order, with their original
        /// <see cref="MatchmakingWaiter.EnqueuedAt"/>. A formation that failed to create a table calls this, so the
        /// fill wait is still measured from when the waiters joined.
        /// <para>
        /// A waiter already in the queue, or with an invalid id, is skipped. The matchmaker does not produce this
        /// case, because the actor handles one message at a time and nothing joins during a formation, but the
        /// check keeps the one-entry rule true for any input.
        /// </para>
        /// </summary>
        public void ReturnToHead(IReadOnlyList<MatchmakingWaiter> waiters)
        {
            if (waiters == null)
                throw new ArgumentNullException(nameof(waiters));

            int insertAt = 0;
            foreach (MatchmakingWaiter waiter in waiters)
            {
                if (!waiter.PlayerId.IsValid || Contains(waiter.PlayerId))
                    continue;

                _waiters.Insert(insertAt, waiter);
                insertAt++;
            }
        }

        int IndexOf(EntityId playerId)
        {
            for (int index = 0; index < _waiters.Count; index++)
            {
                if (_waiters[index].PlayerId == playerId)
                    return index;
            }
            return -1;
        }
    }

    /// <summary>
    /// The matchmaking rules as pure functions.
    /// <para>
    /// There are no game modes, skill brackets or regions, so matchmaking only decides when to stop waiting and
    /// seat everyone in the queue (<c>docs/matchmaking.md</c>).
    /// </para>
    /// </summary>
    public static class MatchmakingPolicy
    {
        /// <summary>
        /// Form a table when a full table is waiting, or <paramref name="fillWait"/> after the oldest waiter joined,
        /// whichever comes first. A later arrival does not restart the wait. A queue with waiters always gets a time to
        /// check again, including the queue left after a formation. Setting the timer only when the queue becomes
        /// non-empty would leave a player who joined while a full table formed waiting with no timer.
        /// </summary>
        public static MatchmakingDecision Decide(IReadOnlyList<MatchmakingWaiter> waiters, MetaTime now, MetaDuration fillWait)
        {
            if (waiters == null)
                throw new ArgumentNullException(nameof(waiters));

            if (waiters.Count == 0)
                return MatchmakingDecision.Idle();

            if (waiters.Count >= MatchRules.NumSeats)
                return MatchmakingDecision.Form(MatchRules.NumSeats);

            MetaTime formAt = waiters[0].EnqueuedAt + fillWait;
            if (now >= formAt)
                return MatchmakingDecision.Form(waiters.Count);

            return MatchmakingDecision.Wait(formAt);
        }

        /// <summary>
        /// Whether a queued player takes the seat a formation offers, and if not, why. The player actor calls this to
        /// check the player again before taking the seat, so no table is created with a seat whose player is gone
        /// (<c>docs/matchmaking.md</c>, "Seat reservation"). The player needs a session to receive the table and a
        /// connected client to play it. A session alone is not enough, because the SDK keeps a session alive for a
        /// linger period after its connection closes, and that period is longer than the fill wait.
        /// </summary>
        public static SeatReservationAnswer AnswerSeatReservation(bool isSearching, bool isAtTable, bool hasSession, bool isClientConnected)
        {
            if (!isSearching)
                return SeatReservationAnswer.NotSearching;

            if (isAtTable)
                return SeatReservationAnswer.AlreadyAtTable;

            if (!hasSession || !isClientConnected)
                return SeatReservationAnswer.NotLive;

            return SeatReservationAnswer.Take;
        }

        /// <summary>
        /// Draw the seat order for one table: a permutation of the seat indices. The waiter at position <c>i</c> of
        /// the formation takes seat <c>result[i]</c>, and bots fill the remaining seats.
        /// <para>
        /// Every waiting human gets a seat before any bot, but which seat is random, so no player gets a fixed
        /// position advantage and nobody can predict the seating (<c>docs/matchmaking.md</c>, "Bot fill").
        /// </para>
        /// </summary>
        public static int[] DrawSeatOrder(ulong seed)
        {
            int[] order = new int[MatchRules.NumSeats];
            for (int seat = 0; seat < MatchRules.NumSeats; seat++)
                order[seat] = seat;

            RandomPCG.CreateFromSeed(seed).ShuffleInPlace(order);
            return order;
        }
    }
}
