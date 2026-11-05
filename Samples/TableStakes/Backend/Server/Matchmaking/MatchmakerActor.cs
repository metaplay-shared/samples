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
    /// The formation rule is in <see cref="MatchmakingPolicy"/> and the bookkeeping after a formation in
    /// <see cref="FormationOutcomePolicy"/>.
    /// </para>
    /// <para>
    /// A formation takes its waiters off the queue, then runs its seat reservation asks and its mint as a task
    /// through <c>ContinueTaskOnActorContext</c>, so the actor keeps handling arrivals, cancels and timers while
    /// it waits. The task reads no actor state. Its result is applied by a callback that the SDK runs on the actor
    /// context, which is the only place the queue changes. Up to <see cref="MaxFormationsInFlight"/> formations
    /// run at once.
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
        /// How long the queue waits before forming again after a table could not be created. The waiters are back
        /// in the queue and already past their fill wait, so without a delay the retry would be a busy loop.
        /// </summary>
        static readonly TimeSpan MintRetryDelay = TimeSpan.FromSeconds(1);

        /// <summary>How many entity ids <see cref="MintMatchAsync"/> tries before it gives up.</summary>
        const int MaxMintAttempts = 3;

        /// <summary>
        /// How many formations may wait on their asks and mint at once. It bounds the asks and table setups the
        /// matchmaker has outstanding when a large queue forms many tables at once. At the limit, the queue waits
        /// until a formation finishes.
        /// </summary>
        const int MaxFormationsInFlight = 16;

        static MatchmakingOptions Options => RuntimeOptionsRegistry.Instance.GetCurrent<MatchmakingOptions>();

        readonly MatchmakingQueue _queue = new MatchmakingQueue();

        /// <summary>Random source for each table's seat order and bot names. The deal is not drawn from it.</summary>
        readonly RandomPCG _rng = RandomPCG.CreateNew();

        /// <summary>
        /// The fill timer, armed by <see cref="ArmFillTimer"/> and run by <see cref="OnFillTimer"/>. Its padding
        /// keeps a timer that fires slightly early from finding the fill wait not yet passed, forming nothing, and
        /// scheduling itself for the same time again.
        /// </summary>
        readonly DeadlineWakeup _fillTimer = new DeadlineWakeup();

        /// <summary>How many formations have started and not yet been applied.</summary>
        int _numFormationsInFlight;

        /// <summary>
        /// The players in a formation that has not been applied yet, with what each did with the queue since. A player
        /// is in at most one formation, so a seat release or assignment that reaches their actor is always from the
        /// formation they answered.
        /// </summary>
        readonly Dictionary<EntityId, FormationLateChange> _inFormation = new Dictionary<EntityId, FormationLateChange>();

        /// <summary>
        /// No formation starts before this time. Set after a formation could not create a table, so that the
        /// retry waits <see cref="MintRetryDelay"/> whatever wakes the queue first.
        /// </summary>
        MetaTime _formNotBefore = MetaTime.Epoch;

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
        /// <para>
        /// A player who cancelled and pressed Play again may still be in a formation that is waiting on its asks.
        /// Their entry is held until that formation is applied, so the player is never in two formations at once.
        /// </para>
        /// </summary>
        [MessageHandler]
        void HandleEnterQueue(MatchmakerEnterQueueMessage message)
        {
            MatchmakingWaiter waiter = new MatchmakingWaiter(message.Identity, MetaTime.Now);
            if (_inFormation.ContainsKey(waiter.PlayerId))
            {
                _inFormation[waiter.PlayerId] = FormationLateChange.PlayAgain(waiter);
                return;
            }

            if (!_queue.TryEnqueue(waiter))
            {
                _log.Warning("Player {PlayerId} is already in the queue. Not queuing them twice.", message.Identity?.PlayerId);
                return;
            }

            FormTablesFromQueue();
        }

        /// <summary>
        /// Removes a player from the queue because they cancelled or their session ended. If a formation already
        /// took the player's entry, the seat reservation decides the outcome, and the cancel only keeps a failed mint from
        /// returning the player to the queue.
        /// </summary>
        [MessageHandler]
        void HandleLeaveQueue(MatchmakerLeaveQueueMessage message)
        {
            if (_inFormation.ContainsKey(message.PlayerId))
            {
                _inFormation[message.PlayerId] = FormationLateChange.Cancelled();
                return;
            }

            if (!_queue.Remove(message.PlayerId))
                return;

            FormTablesFromQueue();
        }

        /// <summary>
        /// Starts as many formations as the queue and <see cref="MaxFormationsInFlight"/> allow, then schedules the
        /// fill timer for the players left waiting.
        /// <para>
        /// After each formation the loop asks the policy again, and the policy returns a time whenever the queue is
        /// non-empty. So a player left over after a full table formed gets a fill timer instead of waiting for more
        /// players who may never arrive. At the formation limit the loop stops without a timer, and the next
        /// formation to finish calls this again.
        /// </para>
        /// </summary>
        void FormTablesFromQueue()
        {
            while (_numFormationsInFlight < MaxFormationsInFlight)
            {
                MetaTime            now      = MetaTime.Now;
                MatchmakingDecision decision = MatchmakingPolicy.Decide(_queue.Waiters, now, Options.FillWaitDuration);
                if (decision.Action != MatchmakingAction.Form)
                {
                    ArmFillTimer(decision.NextLookAt);
                    return;
                }

                if (now < _formNotBefore)
                {
                    ArmFillTimer(_formNotBefore);
                    return;
                }

                // Bot names come from the active game config. Check them before taking any waiter, so that if this
                // fails no player actor has been asked anything.
                //
                // Enough names for a full table are required even if every seat has a human, because the number of
                // players who accept is not known until the reservations return. The config build also refuses a
                // roster that cannot fill a table.
                BotNameRoster roster = ServerBotNames.FromActiveBaseline();
                if (roster.Count < MatchRules.NumSeats)
                {
                    _log.Error("The active game config offers {NumNames} computer-player names, which is fewer than a table's {NumSeats} seats. Forming nothing.", roster.Count, MatchRules.NumSeats);
                    _formNotBefore = now + MetaDuration.FromTimeSpan(MintRetryDelay);
                    ArmFillTimer(_formNotBefore);
                    return;
                }

                // Remove the waiters from the queue before the formation waits on anything, so a cancel handled
                // during the formation finds no entry and does nothing.
                StartFormation(_queue.TakeFromHead(decision.NumWaitersToSeat), roster);
            }
        }

        /// <summary>
        /// Schedules the fill timer for <paramref name="formAt"/>, or clears it when <paramref name="formAt"/> is
        /// <see cref="MetaTime.Epoch"/>. Does nothing if a timer is already scheduled for the same time, because two
        /// callbacks for one time would both pass the check in <see cref="OnFillTimer"/>.
        /// </summary>
        void ArmFillTimer(MetaTime formAt)
        {
            if (!_fillTimer.TryArm(formAt, MetaTime.Now, out TimeSpan delay))
                return;

            ScheduleExecuteOnActorContext(delay, () => OnFillTimer(formAt));
        }

        /// <summary>
        /// Runs the fill timer. Does nothing if <paramref name="scheduledFor"/> is no longer the scheduled time, which
        /// is how outdated callbacks are ignored instead of cancelled.
        /// </summary>
        void OnFillTimer(MetaTime scheduledFor)
        {
            if (!_fillTimer.TryConsume(scheduledFor))
                return;

            FormTablesFromQueue();
        }

        #endregion

        #region Forming a table

        /// <summary>
        /// What a formation's task came back with. It holds only the answers and the mint result, so the task
        /// needs no actor state and the bookkeeping happens in <see cref="ApplyFormation"/>.
        /// </summary>
        readonly struct FormationResult
        {
            public readonly SeatAskReply[]      Replies;
            public readonly FormationMintResult Mint;
            public readonly EntityId            MatchId;

            public FormationResult(SeatAskReply[] replies, FormationMintResult mint, EntityId matchId)
            {
                Replies = replies;
                Mint    = mint;
                MatchId = matchId;
            }
        }

        /// <summary>
        /// Starts forming a table from <paramref name="formation"/>, which has already left the queue. The random
        /// draws and option values the task needs are taken here, on the actor context.
        /// </summary>
        void StartFormation(List<MatchmakingWaiter> formation, BotNameRoster roster)
        {
            _numFormationsInFlight++;
            foreach (MatchmakingWaiter waiter in formation)
                _inFormation.Add(waiter.PlayerId, FormationLateChange.None);

            ulong    seatOrderSeed = _rng.NextULong();
            ulong    botNameSeed   = _rng.NextULong();
            TimeSpan askTimeout    = Options.SeatReservationAskTimeout;
            TimeSpan mintTimeout   = Options.MintAskTimeout;

            ContinueTaskOnActorContext(
                FormTableAsync(formation, roster, seatOrderSeed, botNameSeed, askTimeout, mintTimeout),
                result => ApplyFormation(formation, result),
                error =>
                {
                    // Unknown which players committed. Return everyone who has not cancelled to the queue and
                    // release them as still queued: an actor that committed goes back to searching, and one that
                    // did not ignores the release.
                    _log.Error("Forming a table for {NumPlayers} players failed ({Error}). They go back in the queue.", formation.Count, error.Message);

                    SeatAskReply[] unknown = new SeatAskReply[formation.Count];
                    for (int index = 0; index < unknown.Length; index++)
                        unknown[index] = SeatAskReply.Unanswered();

                    ApplyFormation(formation, new FormationResult(unknown, FormationMintResult.Failed, EntityId.None));
                });
        }

        /// <summary>
        /// Reserves a seat with each player's actor, and creates the table if at least one human accepted
        /// (<see cref="FormationOutcomePolicy.Accepted"/>). Runs off the actor's message loop, so it must not
        /// read or change actor state.
        /// </summary>
        async Task<FormationResult> FormTableAsync(List<MatchmakingWaiter> formation, BotNameRoster roster, ulong seatOrderSeed, ulong botNameSeed, TimeSpan askTimeout, TimeSpan mintTimeout)
        {
            // In parallel, so the reservation step takes at most one ask timeout.
            Task<SeatAskReply>[] asks = new Task<SeatAskReply>[formation.Count];
            for (int index = 0; index < formation.Count; index++)
                asks[index] = AskForSeatAsync(formation[index], askTimeout);

            SeatAskReply[] replies = await Task.WhenAll(asks);

            List<MatchmakingWaiter> accepted = FormationOutcomePolicy.Accepted(formation, replies);
            if (accepted.Count == 0)
                return new FormationResult(replies, FormationMintResult.NotAttempted, EntityId.None);

            List<MatchSeatSetup> seats = CreateSeats(accepted, roster, seatOrderSeed, botNameSeed);

            try
            {
                EntityId matchId = await MintMatchAsync(seats, mintTimeout);
                return new FormationResult(replies, FormationMintResult.Succeeded, matchId);
            }
            catch (Exception ex)
            {
                _log.Error("Could not mint a table for {NumPlayers} players ({Error}). They stay queued.", accepted.Count, ex.Message);
                return new FormationResult(replies, FormationMintResult.Failed, EntityId.None);
            }
        }

        /// <summary>
        /// Asks one player's actor to commit to a seat. A refusal proves the actor answered. A failure proves
        /// nothing, so it is <see cref="SeatAskResult.Unanswered"/>.
        /// </summary>
        async Task<SeatAskReply> AskForSeatAsync(MatchmakingWaiter waiter, TimeSpan askTimeout)
        {
            MatchmakerReserveSeatResponse response;
            try
            {
                response = await EntityAskAsync(waiter.PlayerId, new MatchmakerReserveSeatRequest(), askTimeout);
            }
            catch (Exception ex)
            {
                // The player's actor commits the reservation (status SeatReserved, timeout scheduled) before it
                // sends its answer. A lost or late answer therefore leaves the player holding a seat that this
                // table will not have, stuck on a searching screen without Cancel until the reservation times
                // out. The formation's outcome releases them explicitly.
                _log.Warning("Player {PlayerId} did not answer a seat reservation ({Error}). Releasing whatever seat they committed to.", waiter.PlayerId, ex.Message);
                return SeatAskReply.Unanswered();
            }

            if (!response.Accepted)
            {
                // A declining actor did not commit, so there is no seat to release and the player does not go
                // back in the queue.
                _log.Info("Player {PlayerId} declined a seat: they cancelled, are already at a table or reserved for another, or nobody is on the other end any more.", waiter.PlayerId);
                return SeatAskReply.Declined();
            }

            return SeatAskReply.Accepted(response.Identity);
        }

        /// <summary>
        /// Carries out a finished formation on the actor context: tells the seated players their table, releases
        /// the players whose answer was lost, and after a failed mint returns everyone who did not decline to the
        /// head of the queue. Then forms again from the queue.
        /// </summary>
        void ApplyFormation(List<MatchmakingWaiter> formation, FormationResult result)
        {
            _numFormationsInFlight--;

            FormationLateChange[] lateChanges = new FormationLateChange[formation.Count];
            for (int index = 0; index < formation.Count; index++)
                _inFormation.Remove(formation[index].PlayerId, out lateChanges[index]);

            FormationOutcome outcome = FormationOutcomePolicy.Decide(formation, result.Replies, result.Mint, lateChanges);

            foreach (MatchmakingWaiter waiter in outcome.Seated)
                CastMessage(waiter.PlayerId, new MatchmakerSeatAssignedMessage(result.MatchId, outcome.Seated.Count));

            _queue.ReturnToHead(outcome.ReturnToHead);
            foreach (MatchmakingWaiter waiter in outcome.Enqueue)
                _queue.TryEnqueue(waiter);

            // A player's actor that did not commit ignores the release, so it is safe to send to any player the
            // formation asked.
            foreach (FormationRelease release in outcome.Releases)
                CastMessage(release.PlayerId, new MatchmakerSeatReleasedMessage(release.IsStillQueued));

            if (result.Mint == FormationMintResult.Failed)
            {
                // The returned waiters are past their fill wait, so forming at once would fail the same way.
                _formNotBefore = MetaTime.Now + MetaDuration.FromTimeSpan(MintRetryDelay);
            }

            if (result.Mint == FormationMintResult.Succeeded)
                _log.Info("Formed table {MatchId} with {NumHumans} human seats.", result.MatchId, outcome.Seated.Count);
            else if (result.Mint == FormationMintResult.NotAttempted)
                _log.Info("No live human took a seat. Forming nothing.");

            FormTablesFromQueue();
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
        static List<MatchSeatSetup> CreateSeats(List<MatchmakingWaiter> seated, BotNameRoster roster, ulong seatOrderSeed, ulong botNameSeed)
        {
            int[] seatOrder = MatchmakingPolicy.DrawSeatOrder(seatOrderSeed);

            MatchSeatSetup[] seats = new MatchSeatSetup[MatchRules.NumSeats];
            for (int index = 0; index < seated.Count; index++)
                seats[seatOrder[index]] = new MatchSeatSetup(seated[index].Identity);

            // The matchmaker sets only a bot seat's name. The table draws the bot's cosmetics and strength from
            // its own seed and game config (BotSeatIdentity).
            List<string> botNames = roster.Draw(botNameSeed, MatchRules.NumSeats - seated.Count);
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
        async Task<EntityId> MintMatchAsync(List<MatchSeatSetup> seats, TimeSpan mintTimeout)
        {
            for (int attempt = 0; attempt < MaxMintAttempts; attempt++)
            {
                EntityId matchId = EntityId.CreateRandom(EntityKindGame.Match);
                try
                {
                    _ = await EntityAskAsync(matchId, new InternalEntitySetupRequest(new MatchSetupParams(seats)), mintTimeout);
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
