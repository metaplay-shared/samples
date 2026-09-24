using Game.Logic;
using Metaplay.Cloud.Entity;
using Metaplay.Cloud.RuntimeOptions;
using Metaplay.Cloud.Sharding;
using Metaplay.Core;
using Metaplay.Core.Model;
using Metaplay.Server.MultiplayerEntity;
using Metaplay.Server.MultiplayerEntity.InternalMessages;
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace Game.Server.Match
{
    [EntityConfig]
    public class MatchEntityConfig : EphemeralEntityConfig
    {
        public override EntityKind        EntityKind           => EntityKindGame.Match;
        public override Type              EntityActorType      => typeof(MatchActor);
        public override NodeSetPlacement  NodeSetPlacement     => NodeSetPlacement.Logic;
        public override IShardingStrategy ShardingStrategy     => ShardingStrategies.CreateStaticSharded();
        public override TimeSpan          ShardShutdownTimeout => TimeSpan.FromSeconds(30);
    }

    /// <summary>
    /// One table: the owner of everything the rules have no business knowing — seats and player identities,
    /// timers, disconnects, and delivering the result (<c>Docs/match.md</c>).
    /// <para>
    /// <b>A match lives exactly as long as this actor.</b> A table whose node goes away takes its game with it,
    /// and both players land on Home (<c>Docs/match.md</c>, "Actor lifetime"). That is what makes
    /// <see cref="ShutdownPolicy"/> and <see cref="_deliveryWakelock"/> load-bearing.
    /// </para>
    /// <para>
    /// <b>It is the only writer.</b> The rules are the actions this actor submits and every follower
    /// re-executes; a client's moves arrive as directed messages. <see cref="MatchAction"/> is
    /// leader-synchronized only, so the SDK refuses any action a client enqueues. Every
    /// action is prepared first with <c>MatchAction.ServerPrepare</c>, whose verdict is the refusal the client
    /// is sent.
    /// </para>
    /// </summary>
    // Derives from the vendored SDK base in Backend/Server/SdkPreview because addressed timeline operations
    // are an SDK change not yet in the R38 release. See SdkPreview/README.md.
    public sealed partial class MatchActor : SdkPreview.EphemeralMultiplayerEntityActorBase<MatchModel, MatchAction>
    {
        // The actor ticks, which is the SDK default: ticks advance the model's clock, which every deadline is
        // stamped from (MatchModel.TicksPerSecond).

        /// <summary>
        /// Both windows are <see cref="MatchOptions.ActorLinger"/>. The <b>initial</b> wait must clear
        /// <see cref="MatchOptions.JoinWindow"/>, because a table that dies before anybody arrives is lost; the
        /// SDK's 30 s default is inside a cold WebAssembly boot under load. The <b>linger</b> must exceed the
        /// disconnect grace, or a deserted table is evicted before its play-out runs. Both are validated in
        /// <see cref="MatchOptions.OnLoadedAsync"/>.
        /// </summary>
        protected override AutoShutdownPolicy ShutdownPolicy => AutoShutdownPolicy.ShutdownAfterSubscribersGone(
            lingerDuration:      _options.ActorLinger,
            initialWaitDuration: _options.ActorLinger);

        static bool IsDevelopmentOrLocal => RuntimeOptionsBase.IsLocalEnvironment || RuntimeOptionsBase.IsDevelopmentEnvironment;

        /// <summary>
        /// Per-operation checksums in local and development, which name the exact action a divergence came
        /// from; the SDK's periodic default elsewhere. Consistency checks stay off.
        /// </summary>
        protected override EntityDebugOptions DebugOptions => new EntityDebugOptions(
            IsDevelopmentOrLocal ? EntityChecksumMode.PerOperation() : default,
            EntityConsistencyChecks.None,
            checkInitialModelChecksum: IsDevelopmentOrLocal);

        protected override string LogChannelName => "match";

        /// <summary>
        /// The options as they were when this actor was created, kept for its whole life so a match cannot
        /// change its pacing mid-game (<c>Docs/protocol.md</c>, "Timings are host-supplied").
        /// </summary>
        readonly MatchOptions _options = RuntimeOptionsRegistry.Instance.GetCurrent<MatchOptions>();

        /// <summary>
        /// Per seat, when its player went away, or null while they are here. Used to give a returning seat
        /// back the time it spent away.
        /// </summary>
        readonly MetaTime?[] _awaySince = new MetaTime?[MatchSeats.Count];

        /// <summary>
        /// Per seat, the rating it entered on. Held off the replicated model, which must show no band state;
        /// read once, at delivery.
        /// </summary>
        readonly int[] _seatRatings = new int[MatchSeats.Count];

        /// <summary>
        /// The baseline config, captured at construction, so a match cannot change config version mid-game.
        /// </summary>
        SharedGameConfig SharedConfig => (SharedGameConfig)_baselineGameConfig.SharedConfig;

        // ---------------------------------------------------------------- creation

        /// <summary>
        /// The one-shot initialization: draw the seeds, read the decks out of the frozen setup, and deal.
        /// <para>
        /// The one place a public model member is assigned outside an action. The model is not yet on a
        /// timeline and has no subscribers, so direct assignment is correct here.
        /// </para>
        /// </summary>
        protected override Task SetUpModelAsync(MatchModel model, IMultiplayerEntitySetupParams setupParamsBase)
        {
            MatchSetupParams setup  = (MatchSetupParams)setupParamsBase;
            SharedGameConfig config = SharedConfig;

            if (setup.Seats == null || setup.Seats.Count != MatchSeats.Count)
                throw new InvalidOperationException($"A match is formed with exactly {MatchSeats.Count} seats; got {setup.Seats?.Count ?? 0}.");

            // Two independent draws from the cryptographic source. RandomPCG.CreateNew() must not be used: a deal
            // seed a client can bracket hands over both hands (Docs/hidden-information.md, "The seed is
            // part of the secret"). The bot seed is not derived from the deal seed, so it cannot lead back to it.
            ulong dealSeed = BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(8));
            ulong botSeed  = BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(8));

            model.Timings          = _options.ToMatchTimings();
            model.Stakes           = setup.Stakes;
            model.Phase            = MatchTablePhase.Playing;
            model.ResultAcked      = new List<bool> { false, false };
            model.HeistEligibility = new List<List<HeistEligibleCard>> { new List<HeistEligibleCard>(), new List<HeistEligibleCard>() };

            model.Seats = new List<MatchSeat>(MatchSeats.Count);
            for (int seat = 0; seat < setup.Seats.Count; seat++)
            {
                MatchSeatSetup seatSetup = setup.Seats[seat];

                // A table is minted with nobody at it: a seat says who owns it, not who has arrived.
                model.Seats.Add(new MatchSeat(seatSetup.PlayerId, seatSetup.DisplayName, seatSetup.Occupancy, seatSetup.BotProfile));

                // Beside the model rather than on it, so neither board shows a rating.
                _seatRatings[seat] = seatSetup.Rating;
            }

            // The decks come out of the frozen setup, never from the player actors: a rank that moved during a
            // search must not move the deal. The bot seed goes under the secret beside the deal's stream.
            Deal.Create(
                model,
                new MatchSetup(dealSeed, config, model.Timings,
                               setup.Seats[0].ResolveDeck(config),
                               setup.Seats[1].ResolveDeck(config)),
                botSeed);

            // The dealt hands are the baseline GetMemberPrivateState hands a subscriber, so nothing is owed.
            model.Outbox.Clear();

            // The join window: a human seat that has not subscribed when it expires is covered by a bot, and a
            // table nobody came to is abandoned (Docs/match.md, "The join window"). Zero arms nothing.
            if (_options.JoinWindow > TimeSpan.Zero)
                model.Pacing.ArmJoinWindow(model.CurrentTime + MetaDuration.FromTimeSpan(_options.JoinWindow));

            return Task.CompletedTask;
        }

        /// <summary> A freshly set-up table may have a bot seat on turn straight from the deal. </summary>
        protected override Task OnEntityInitialized()
        {
            ArmPopulationHeartbeat();

            MaybeDriveBotSeat();
            RearmFromModel();

            return Task.CompletedTask;
        }

        // ---------------------------------------------------------------- sessions and presence

        /// <summary> A subscriber who is not a seat of this table is refused. </summary>
        protected override Task OnClientSessionHandshake(EntityId sessionId, EntityId playerId, InternalEntitySubscribeRequestBase requestBase)
        {
            if (Model.SeatIndexOfPlayer(playerId) < 0)
            {
                _log.Warning("Player {PlayerId} tried to subscribe to a table they do not have a seat at", playerId);
                throw new InternalEntitySubscribeRefusedBase.Builtins.NotAParticipant();
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// A seat's client arrived. The base builds the initial state, with the seat's own hand as private
        /// state; the presence mark is enqueued so the timeline does not move while that state is serialized.
        /// </summary>
        protected override async Task<InternalEntitySubscribeResponseBase> OnClientSessionStart(EntityId sessionId, EntityId playerId, InternalEntitySubscribeRequestBase requestBase, List<AssociatedEntityRefBase> associatedEntities)
        {
            InternalEntitySubscribeResponseBase response = await base.OnClientSessionStart(sessionId, playerId, requestBase, associatedEntities);

            int seat = Model.SeatIndexOfPlayer(playerId);
            if (seat >= 0)
                EnqueueOnActorContext(() => OnSeatArrived(seat));

            return response;
        }

        /// <summary> A seat's client is here: connected, off grace, and — if covered — back from the next turn. </summary>
        void OnSeatArrived(int seat)
        {
            // First, because arriving lifts the deadline's suppression: offering the held stamp unchanged would
            // hand back a deadline that ran out while the player was away.
            GiveBackTimeSpentAway(seat);

            _awaySince[seat] = null;
            ExecuteMatchAction(new MatchSetGrace(seat, null));

            EditSeat(seat, s => s.IsConnected = true);

            RequestReclaimIfCovered(seat);
            RearmFromModel();

            _log.Info("Seat {Seat} ({PlayerId}) is at the table", seat, Model.Seats[seat].PlayerId);
        }

        /// <summary>
        /// Push the clocks forward by however long a returning seat was away, when that seat's absence is what
        /// was holding its own deadline out of the fold. Nothing happens for a seat that owed no deadline, for
        /// one whose deadline was another seat's, or for a seat that was never away.
        /// </summary>
        void GiveBackTimeSpentAway(int seat)
        {
            if (!_awaySince[seat].HasValue || Model.IsTerminal)
                return;

            // Only when this seat is why the deadline is suppressed, asked before the arrival is recorded.
            if (MatchDeadlinePolicy.DeadlineIsInForce(Model))
                return;

            MetaDuration away = MetaTime.Now - _awaySince[seat].Value;
            if (away <= MetaDuration.Zero)
                return;

            ExecuteMatchAction(new MatchPushClocks(away));
            _log.Info("Seat {Seat} was away for {Away}; its clocks were pushed forward by as much", seat, away);
        }

        /// <summary>
        /// A seat's session ended. The seat goes on grace rather than being covered at once, because a tab
        /// switch or a network blip is not a walk-away. A seat that owes a Heist pick gets no grace; its picks
        /// are defaulted (<see cref="ResolveHeistOnSeatGone"/>).
        /// </summary>
        protected override void OnParticipantSessionEnded(EntitySubscriber session)
        {
            ClientPeerState peer = TryGetClientPeer(session);
            if (peer == null)
                return;

            int seat = Model.SeatIndexOfPlayer(peer.PlayerId);
            if (seat < 0)
                return;

            EditSeat(seat, s => s.IsConnected = false);
            CancelReclaimIfPending(seat);

            _awaySince[seat] = MetaTime.Now;

            if (!ResolveHeistOnSeatGone(seat) && !Model.IsTerminal && Model.Seats[seat].Occupancy == SeatOccupancy.Human)
                ArmGraceFor(seat);

            RearmFromModel();
        }

        /// <summary> Put the roster back on the timeline with one seat changed. The roster is copied first. </summary>
        void EditSeat(int seat, Action<MatchSeat> edit)
        {
            List<MatchSeat> seats = new List<MatchSeat>(MatchSeats.Count);
            for (int ndx = 0; ndx < MatchSeats.Count; ndx++)
                seats.Add(CopyOfSeat(ndx));

            edit(seats[seat]);
            ExecuteMatchAction(new MatchSetSeats(seats));
        }

        /// <summary> A detached copy of one roster entry, safe to edit without touching the model. </summary>
        MatchSeat CopyOfSeat(int seat)
        {
            MatchSeat current = Model.Seats[seat];
            return new MatchSeat(current.PlayerId, current.DisplayName, current.Occupancy, current.BotProfile)
            {
                IsConnected    = current.IsConnected,
                Strikes        = current.Strikes,
                ReclaimPending = current.ReclaimPending,
            };
        }

        // ---------------------------------------------------------------- is this table there?

        [EntityAskHandler]
        InternalMatchProbeResponse HandleInternalMatchProbeRequest(InternalMatchProbeRequest _)
        {
            // An ask to an id nobody set up spawns a fresh actor with a null Model, and ask handlers run
            // regardless of setup state. "Not set up" is the answer for a table that is gone.
            if (Model == null)
                ShutDownIfStillEmpty();

            return new InternalMatchProbeResponse(isSetUp: Model != null, isTerminal: Model != null && Model.IsTerminal);
        }

        /// <summary>
        /// Let go of an actor an ask spawned for nothing, rather than hold a shard slot for the whole initial
        /// wait; after a rolling deploy every returning account probes a stale pointer. Deferred briefly,
        /// because a setup request could be queued behind the ask.
        /// </summary>
        void ShutDownIfStillEmpty()
        {
            ScheduleExecuteOnActorContext(DateTime.UtcNow + EmptyActorGrace, () =>
            {
                if (Model != null || IsShutdownEnqueued)
                    return;

                _log.Debug("Nothing was ever set up here; standing down rather than holding a slot for the initial wait");
                RequestShutdown();
            });
        }

        /// <summary> How long an actor an ask spawned waits to see whether a setup was queued behind it. </summary>
        static readonly TimeSpan EmptyActorGrace = TimeSpan.FromSeconds(5);

        /// <summary>
        /// A minter whose setup ask failed is asking this table to give itself up. Granted only for a table
        /// <b>nobody has joined and nothing has happened at</b>: "I never learned whether you exist" is not a
        /// claim about the game.
        /// </summary>
        [EntityAskHandler]
        InternalMatchAbandonResponse HandleInternalMatchAbandonRequest(InternalMatchAbandonRequest _)
        {
            if (Model == null)
            {
                ShutDownIfStillEmpty();
                return new InternalMatchAbandonResponse(abandoned: false, reason: "the table was never set up");
            }

            foreach (MatchSeat seat in Model.Seats)
            {
                if (seat.IsConnected)
                    return new InternalMatchAbandonResponse(abandoned: false, reason: "somebody is at the table");
            }

            if (Model.Result != null || Model.Rules.Turn > 1)
                return new InternalMatchAbandonResponse(abandoned: false, reason: "the game has already been played");

            // Nothing is recorded or delivered; the minter never assigned its pointer. The one place the actor
            // asks to shut down: RequestShutdown has no subscriber check, and the guards above found nobody.
            _log.Info("Gave the table up at the request of the account that minted it");
            RequestShutdown();

            return new InternalMatchAbandonResponse(abandoned: true, reason: null);
        }
    }
}
