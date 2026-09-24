using Game.Logic;
using Game.Server.Match;
using Metaplay.Cloud.Entity;
using Metaplay.Cloud.RuntimeOptions;
using Metaplay.Cloud.Sharding;
using Metaplay.Core;
using Metaplay.Server;
using Metaplay.Server.MultiplayerEntity.InternalMessages;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Game.Server.Matchmaking
{
    [EntityConfig]
    public class MatchmakerEntityConfig : EphemeralEntityConfig
    {
        public override EntityKind        EntityKind           => EntityKindGame.Matchmaker;
        public override Type              EntityActorType      => typeof(MatchmakerActor);
        public override NodeSetPlacement  NodeSetPlacement     => NodeSetPlacement.Service;
        public override IShardingStrategy ShardingStrategy     => ShardingStrategies.CreateSingletonService();
        public override TimeSpan          ShardShutdownTimeout => TimeSpan.FromSeconds(30);
    }

    /// <summary>
    /// The single queue of players waiting for a table. A table forms when a full table is queued or when the fill
    /// wait has passed, whichever comes first, and bots fill the empty seats.
    /// <para>
    /// The queue is in memory only. After a restart, each player's own search timeout returns them to the menu.
    /// The formation rule is in <see cref="MatchmakingPolicy"/>. Every handler and scheduled callback returns a
    /// task that the SDK awaits before the next message, so a formation finishes before any other arrival, cancel
    /// or timer is handled, and two formations never run at once.
    /// </para>
    /// </summary>
    public sealed class MatchmakerActor : EphemeralEntityActor
    {
        /// <summary>
        /// The id of the only matchmaker entity. <see cref="SingletonServiceShardingStrategy"/> creates only the
        /// entity whose id value is zero and refuses to route messages to any other id.
        /// </summary>
        public static readonly EntityId SingletonId = EntityId.Create(EntityKindGame.Matchmaker, 0);

        /// <summary>
        /// How long the queue waits before trying again after a table could not be created. The waiters are back in
        /// the queue and already past their fill wait, so without a delay the retry would be a busy loop.
        /// </summary>
        static readonly TimeSpan MintRetryDelay = TimeSpan.FromSeconds(1);

        /// <summary>How many entity ids <see cref="MintMatchAsync"/> tries before it gives up.</summary>
        const int MaxMintAttempts = 3;

        static MatchmakingOptions Options => RuntimeOptionsRegistry.Instance.GetCurrent<MatchmakingOptions>();

        readonly MatchmakingQueue _queue = new MatchmakingQueue();

        /// <summary>Random source for each table's seat order and bot names. The deal is not drawn from it.</summary>
        readonly RandomPCG _rng = RandomPCG.CreateNew();

        /// <summary>
        /// The fill timer, armed by <see cref="ArmFillTimer"/> and run by <see cref="OnFillTimerAsync"/>. Its padding
        /// keeps a timer that fires slightly early from finding the fill wait not yet passed, forming nothing, and
        /// scheduling itself for the same time again.
        /// </summary>
        readonly DeadlineWakeup _fillTimer = new DeadlineWakeup();

        // The SDK's default policy shuts down an entity without subscribers, and this entity never has any. A
        // shut-down matchmaker would lose the queue and the fill timer.
        protected override AutoShutdownPolicy ShutdownPolicy => AutoShutdownPolicy.ShutdownNever();

        protected override Task Initialize()
        {
            _log.Info("Matchmaker up. Fill wait is {FillWait}.", Options.FillWait);
            return Task.CompletedTask;
        }

        #region The queue

        /// <summary>
        /// Adds a player to the queue. A player already in the queue is not added again. The player's actor also
        /// refuses a second entry, and both checks are needed: the actor catches a double press of Play, and this
        /// handler catches an actor that restarted and forgot it was searching.
        /// </summary>
        [MessageHandler]
        async Task HandleEnterQueue(MatchmakerEnterQueueMessage message)
        {
            if (!_queue.TryEnqueue(new MatchmakingWaiter(message.Identity, MetaTime.Now)))
            {
                _log.Warning("Player {PlayerId} is already in the queue. Not queuing them twice.", message.Identity?.PlayerId);
                return;
            }

            await FormTablesFromQueueAsync();
        }

        /// <summary>
        /// Removes a player from the queue because they cancelled or their session ended. If a formation already
        /// took the player's entry, this does nothing, and the seat reservation decides the outcome instead.
        /// </summary>
        [MessageHandler]
        async Task HandleLeaveQueue(MatchmakerLeaveQueueMessage message)
        {
            if (!_queue.Remove(message.PlayerId))
                return;

            await FormTablesFromQueueAsync();
        }

        /// <summary>
        /// Forms as many tables as the queue allows, then schedules the fill timer for the players left waiting.
        /// <para>
        /// After each formation the loop asks the policy again, and the policy returns a time whenever the queue is
        /// non-empty. So a player left over after a full table formed gets a fill timer instead of waiting for more
        /// players who may never arrive.
        /// </para>
        /// </summary>
        async Task FormTablesFromQueueAsync()
        {
            while (true)
            {
                MatchmakingDecision decision = MatchmakingPolicy.Decide(_queue.Waiters, MetaTime.Now, Options.FillWaitDuration);
                if (decision.Action != MatchmakingAction.Form)
                {
                    ArmFillTimer(decision.NextLookAt);
                    return;
                }

                // Remove the waiters from the queue before the first await, so a cancel handled during the
                // formation finds no entry and does nothing.
                List<MatchmakingWaiter> formation = _queue.TakeFromHead(decision.NumWaitersToSeat);

                if (!await TryFormTableAsync(formation))
                {
                    // No table was created and the waiters are back at the head of the queue, all past their fill
                    // wait. Deciding again now would form the same table and fail the same way, so wait first.
                    ArmFillTimer(MetaTime.Now + MetaDuration.FromTimeSpan(MintRetryDelay));
                    return;
                }
            }
        }

        /// <summary>
        /// Schedules the fill timer for <paramref name="formAt"/>, or clears it when <paramref name="formAt"/> is
        /// <see cref="MetaTime.Epoch"/>. Does nothing if a timer is already scheduled for the same time, because two
        /// callbacks for one time would both pass the check in <see cref="OnFillTimerAsync"/>.
        /// </summary>
        void ArmFillTimer(MetaTime formAt)
        {
            if (!_fillTimer.TryArm(formAt, MetaTime.Now, out TimeSpan delay))
                return;

            // Declared as Func<Task> so that the overload that awaits the callback is chosen. The Action overload
            // would not wait for the formation, so it would run outside the actor's serialized message handling
            // and its exceptions would be lost.
            Func<Task> onTimer = () => OnFillTimerAsync(formAt);
            ScheduleExecuteOnActorContext(delay, onTimer);
        }

        /// <summary>
        /// Runs the fill timer. Does nothing if <paramref name="scheduledFor"/> is no longer the scheduled time, which
        /// is how outdated callbacks are ignored instead of cancelled.
        /// </summary>
        async Task OnFillTimerAsync(MetaTime scheduledFor)
        {
            if (!_fillTimer.TryConsume(scheduledFor))
                return;

            await FormTablesFromQueueAsync();
        }

        #endregion

        #region Forming a table

        /// <summary>
        /// Creates a table from a formation: reserves a seat with each player's actor, creates the table entity,
        /// and only then tells the players their table. A player whose actor declines (cancelled, or no live
        /// connection: <see cref="MatchmakingPolicy.AnswerSeatReservation"/>) is dropped. No table is created
        /// unless at least one human accepted. If creation fails, the players who accepted are re-queued with their
        /// original enqueue times.
        /// </summary>
        /// <returns>True if the formation is finished, false if the waiters were queued again.</returns>
        async Task<bool> TryFormTableAsync(List<MatchmakingWaiter> formation)
        {
            // Bot names come from the active game config. Check them before reserving any seat, so that if this
            // fails no player actor has been asked anything: the waiters go back to the head of the queue and the
            // retry delay applies.
            //
            // Enough names for a full table are required even if every seat has a human, because the number of
            // players who accept is not known until the reservations return. The config build also refuses a
            // roster that cannot fill a table.
            BotNameRoster roster = ServerBotNames.FromActiveBaseline();
            if (roster.Count < MatchRules.NumSeats)
            {
                _log.Error("The active game config offers {NumNames} computer-player names, which is fewer than a table's {NumSeats} seats. Forming nothing.", roster.Count, MatchRules.NumSeats);
                _queue.ReturnToHead(formation);
                return false;
            }

            // Send all reservation asks in parallel and collect the answers afterwards. The matchmaker handles no
            // other message while it waits, so sequential asks would block every other player's Play, cancel and
            // fill timer for one ask timeout per unresponsive player. In parallel, the wait is at most one
            // ask timeout.
            Task<MatchmakerReserveSeatResponse>[] reservations = new Task<MatchmakerReserveSeatResponse>[formation.Count];
            for (int index = 0; index < formation.Count; index++)
                reservations[index] = EntityAskAsync(formation[index].PlayerId, new MatchmakerReserveSeatRequest(), Options.SeatReservationAskTimeout);

            // seated: players who accepted. unanswered: players whose answer never arrived. retained: every player
            // who did not decline, in queue order, which is who goes back into the queue if the table cannot be
            // created.
            List<MatchmakingWaiter> seated     = new List<MatchmakingWaiter>(formation.Count);
            List<MatchmakingWaiter> unanswered = new List<MatchmakingWaiter>();
            List<MatchmakingWaiter> retained   = new List<MatchmakingWaiter>(formation.Count);

            for (int index = 0; index < formation.Count; index++)
            {
                MatchmakingWaiter waiter = formation[index];

                MatchmakerReserveSeatResponse response;
                try
                {
                    response = await reservations[index];
                }
                catch (Exception ex)
                {
                    // The player's actor commits the reservation (status SeatReserved, timeout scheduled) before it
                    // sends its answer. A lost or late answer therefore leaves the player holding a seat that this
                    // table will not have, stuck on a searching screen without Cancel until the reservation times
                    // out. ReleaseSeats below tells them explicitly.
                    _log.Warning("Player {PlayerId} did not answer a seat reservation ({Error}). Releasing whatever seat they committed to.", waiter.PlayerId, ex.Message);
                    unanswered.Add(waiter);
                    retained.Add(waiter);
                    continue;
                }

                if (!response.Accepted)
                {
                    // A declining actor did not commit, so there is no seat to release and the player does not go
                    // back in the queue.
                    _log.Info("Player {PlayerId} declined a seat: they cancelled, are already at a table, or nobody is on the other end any more.", waiter.PlayerId);
                    continue;
                }

                MatchmakingWaiter confirmed = new MatchmakingWaiter(response.Identity, waiter.EnqueuedAt);
                seated.Add(confirmed);
                retained.Add(confirmed);
            }

            if (seated.Count == 0)
            {
                _log.Info("No live human took a seat. Forming nothing.");
                ReleaseSeats(unanswered, isStillQueued: false);
                return true;
            }

            EntityId matchId;
            try
            {
                matchId = await MintMatchAsync(CreateSeats(seated, roster));
            }
            catch (Exception ex)
            {
                _log.Error("Could not mint a table for {NumPlayers} players ({Error}). They stay queued.", seated.Count, ex.Message);

                _queue.ReturnToHead(retained);
                ReleaseSeats(retained, isStillQueued: true);
                return false;
            }

            foreach (MatchmakingWaiter waiter in seated)
                CastMessage(waiter.PlayerId, new MatchmakerSeatAssignedMessage(matchId, seated.Count));

            ReleaseSeats(unanswered, isStillQueued: false);

            _log.Info("Formed table {MatchId} with {NumHumans} human seats.", matchId, seated.Count);
            return true;
        }

        /// <summary>
        /// Tells players that the seat they may have committed to will not be filled. A player's actor that did not
        /// commit ignores the message, so it is safe to send to any player the formation asked.
        /// </summary>
        void ReleaseSeats(List<MatchmakingWaiter> waiters, bool isStillQueued)
        {
            foreach (MatchmakingWaiter waiter in waiters)
                CastMessage(waiter.PlayerId, new MatchmakerSeatReleasedMessage(isStillQueued));
        }

        /// <summary>
        /// Assigns the accepted players to seats. The seat order is random instead of the arrival order, so no one
        /// can predict or choose a seat. Bots take the remaining seats.
        /// <para>
        /// Bot names are drawn from the game config's roster without replacement, so no table has two bots with
        /// the same name. Player display names may not match a roster name (<c>docs/player.md</c>, "Name rules").
        /// A human seat uses the identity returned with the player's acceptance.
        /// </para>
        /// </summary>
        List<MatchSeatSetup> CreateSeats(List<MatchmakingWaiter> seated, BotNameRoster roster)
        {
            int[] seatOrder = MatchmakingPolicy.DrawSeatOrder(_rng.NextULong());

            MatchSeatSetup[] seats = new MatchSeatSetup[MatchRules.NumSeats];
            for (int index = 0; index < seated.Count; index++)
                seats[seatOrder[index]] = new MatchSeatSetup(seated[index].Identity);

            // The matchmaker sets only a bot seat's name. The table draws the bot's cosmetics and strength from
            // its own seed and game config (BotSeatIdentity).
            List<string> botNames = roster.Draw(_rng.NextULong(), MatchRules.NumSeats - seated.Count);
            for (int index = seated.Count; index < MatchRules.NumSeats; index++)
                seats[seatOrder[index]] = new MatchSeatSetup(PlayerPublicIdentity.ForBot(EntityId.None, botNames[index - seated.Count], avatarId: null));

            return new List<MatchSeatSetup>(seats);
        }

        /// <summary>
        /// Creates a table by sending a setup request to a random entity id. The actor writes its database row on
        /// its first persist. Only <see cref="InternalEntitySetupRefusal"/> (the id is in use) is retried with another
        /// id. After any other failure, such as an ask timeout, the setup may have completed, and a retry could
        /// create a second table for the same formation, so the method rethrows. No player points at such a table.
        /// </summary>
        async Task<EntityId> MintMatchAsync(List<MatchSeatSetup> seats)
        {
            for (int attempt = 0; attempt < MaxMintAttempts; attempt++)
            {
                EntityId matchId = EntityId.CreateRandom(EntityKindGame.Match);
                try
                {
                    _ = await EntityAskAsync(matchId, new InternalEntitySetupRequest(new MatchSetupParams(seats)), Options.MintAskTimeout);
                    return matchId;
                }
                catch (InternalEntitySetupRefusal)
                {
                    _log.Warning("Table id {MatchId} is already in use. Trying another.", matchId);
                }
                catch (Exception ex)
                {
                    _log.Error("Setting up table {MatchId} failed ({Error}). If its setup completed, that table exists with nobody pointed at it.", matchId, ex.Message);
                    throw;
                }
            }

            throw new InvalidOperationException($"Could not mint a table in {MaxMintAttempts} attempts");
        }

        #endregion
    }
}
