using Game.Logic;
using Game.Server.Community;
using Metaplay.Cloud.Entity;
using Metaplay.Cloud.RuntimeOptions;
using Metaplay.Cloud.Sharding;
using Metaplay.Core;
using Metaplay.Server;
using System;
using System.Collections.Generic;

namespace Game.Server.Matchmaking
{
    [EntityConfig]
    public class MatchmakerEntityConfig : EphemeralEntityConfig
    {
        public override EntityKind        EntityKind           => EntityKindGame.Matchmaker;
        public override Type              EntityActorType      => typeof(MatchmakerActor);
        public override NodeSetPlacement  NodeSetPlacement     => NodeSetPlacement.Service;
        public override IShardingStrategy ShardingStrategy     => ShardingStrategies.CreateSingletonService();
        public override TimeSpan          ShardShutdownTimeout => TimeSpan.FromSeconds(60);
    }

    /// <summary>
    /// The ranked queue: one always-running service entity holding the waiting list, <b>in memory only</b>
    /// (<c>Docs/matchmaking.md</c>, "The queue service"). A restart loses every waiter's spot, and each
    /// waiter's own <c>MatchmakingOptions.SearchingBound</c> turns that into "tap Play again".
    /// <para>
    /// Every judgement is a pure function it calls; what is left here is the mailbox, the timer and the casts.
    /// </para>
    /// </summary>
    public sealed partial class MatchmakerActor : EphemeralEntityActor
    {
        /// <summary> The one address. A singleton service lives at ordinal zero of its own kind. </summary>
        public static EntityId EntityId { get; } = EntityId.Create(EntityKindGame.Matchmaker, 0);

        /// <summary>
        /// Explicit, because the default would let the always-on service stop and take the queue with it
        /// (<c>Docs/protocol.md</c>, "Services and entity minting").
        /// </summary>
        protected override AutoShutdownPolicy ShutdownPolicy => AutoShutdownPolicy.ShutdownNever();

        /// <summary> Who is waiting, in no particular order: the policy sorts its own copy. </summary>
        readonly List<MatchmakingTicket> _queue = new List<MatchmakingTicket>();

        /// <summary>
        /// The instant the one wake-up is armed for, so re-arming the same instant adds no second timer. A stale
        /// callback just re-derives the verdict, because <see cref="MatchmakingPolicy.Evaluate"/> is stateless.
        /// </summary>
        MetaTime? _armedFor;

        public MatchmakerActor()
        {
            _log.Info("The ranked queue is open");
        }

        /// <summary>
        /// The queue's timings, read fresh on every use: this actor never shuts down, and the options may change
        /// at runtime.
        /// </summary>
        static MatchmakingOptions Options => RuntimeOptionsRegistry.Instance.GetCurrent<MatchmakingOptions>();

        /// <summary> The content formation reads, re-read per formation so a config update reaches the next match. </summary>
        static SharedGameConfig SharedConfig
            => (SharedGameConfig)GlobalStateProxyActor.ActiveGameConfig.Get().BaselineGameConfig.SharedConfig;

        [EntityAskHandler]
        InternalQueuePopulationResponse HandleQueuePopulation(InternalQueuePopulationRequest request)
            => new InternalQueuePopulationResponse(_queue.Count);

        // ---------------------------------------------------------------- the mailbox

        /// <summary> A player wants a game. A duplicate entry replaces the old one. </summary>
        [MessageHandler]
        void HandleInternalMatchmakingEnqueue(InternalMatchmakingEnqueue message)
        {
            MatchmakingTicket ticket = message.Ticket;
            RemoveFromQueue(ticket.PlayerId);
            _queue.Add(ticket);

            _log.Debug("{PlayerId} joined the queue at rating {Rating}, Power Score {PowerScore} ({Waiting} waiting)",
                ticket.PlayerId, ticket.Rating, ticket.PowerScore, _queue.Count);

            RearmFromQueue();
        }

        /// <summary> A player left. Idempotent, which is what lets the cancel be a cast. </summary>
        [MessageHandler]
        void HandleInternalMatchmakingCancel(InternalMatchmakingCancel message)
        {
            if (!RemoveFromQueue(message.PlayerId))
                return;

            _log.Debug("{PlayerId} left the queue ({Waiting} waiting)", message.PlayerId, _queue.Count);

            RearmFromQueue();
        }

        bool RemoveFromQueue(EntityId playerId)
        {
            for (int ndx = 0; ndx < _queue.Count; ndx++)
            {
                if (_queue[ndx].PlayerId == playerId)
                {
                    _queue.RemoveAt(ndx);
                    return true;
                }
            }

            return false;
        }

        // ---------------------------------------------------------------- the one wake-up

        /// <summary> How many formations one pass may start, so a runaway policy cannot spin the singleton. </summary>
        const int MaxFormationsPerPass = 16;

        /// <summary>
        /// Act on the queue, then arm the next wake-up from the same verdict. The loop re-evaluates whatever a
        /// formation leaves, so a remainder is never left without a timer.
        /// </summary>
        void RearmFromQueue()
        {
            for (int pass = 0; pass < MaxFormationsPerPass; pass++)
            {
                MatchmakingPolicy.Verdict verdict = MatchmakingPolicy.Evaluate(_queue, MetaTime.Now, Options.ToSchedule());

                if (verdict is MatchmakingPolicy.Wait wait)
                {
                    ArmFor(wait.NextEvaluationAt);
                    return;
                }

                ApplyVerdict(verdict);
            }

            _log.Warning("The queue proposed {Count} formations in one pass; looking again shortly rather than spinning", MaxFormationsPerPass);
            ArmFor(MetaTime.Now + MetaDuration.FromSeconds(1));
        }

        void ApplyVerdict(MatchmakingPolicy.Verdict verdict)
        {
            // Formation removes its waiters first, so a cancel mid-formation is a no-op here; the player actor
            // is the arbiter of its own seat.
            if (verdict is MatchmakingPolicy.FormPair pair)
            {
                RemoveFromQueue(pair.A.PlayerId);
                RemoveFromQueue(pair.B.PlayerId);
                StartPairFormation(pair.A, pair.B);
            }
            else if (verdict is MatchmakingPolicy.FormBotMatch bot)
            {
                RemoveFromQueue(bot.Waiter.PlayerId);
                StartBotFormation(bot.Waiter);
            }
        }

        void ArmFor(MetaTime? next)
        {
            if (!next.HasValue)
            {
                _armedFor = null;
                return;
            }

            if (_armedFor.HasValue && _armedFor.Value == next.Value)
                return;

            _armedFor = next.Value;

            // Padded, because a punctual timer fires a hair early and re-derives the same "wait".
            ScheduleExecuteOnActorContext(next.Value.ToDateTime() + Options.TimerPadding, () => OnQueueTimerFired(next.Value));
        }

        /// <summary> The scheduled wake-up. Re-running the stateless policy is always correct. </summary>
        void OnQueueTimerFired(MetaTime armedFor)
        {
            if (_armedFor.HasValue && _armedFor.Value == armedFor)
                _armedFor = null;

            RearmFromQueue();
        }
    }
}
