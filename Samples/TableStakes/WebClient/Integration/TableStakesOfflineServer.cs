using Game.Logic;
using Metaplay.Core;
using Metaplay.Core.Config;
using Metaplay.Core.Message;
using Metaplay.Core.Model;
using Metaplay.Core.MultiplayerEntity;
using Metaplay.Core.MultiplayerEntity.Messages;
using Metaplay.Core.Player;
using Metaplay.Unity;
using System;
using System.Collections.Generic;
using WebClientBase.Integration;

namespace WebClient.Integration;

/// <summary>
/// Hosts a match in the browser in offline mode, without a game server. It creates a table at session start,
/// runs the bots and the resolve pause, and answers the client's moves over the SDK's multiplayer entity
/// protocol.
/// <para>
/// It hosts the match as a multiplayer entity instead of calling the shared engine directly, so that offline
/// mode also exercises the SDK's replication and serialization (<c>docs/web-client.md</c>, "Offline mode"). It
/// shares the turn-flow driver (<see cref="MatchHost"/>), the engine and the bots with the server's
/// <c>MatchActor</c>, and only the timers, sessions and transport are its own.
/// </para>
/// </summary>
public class TableStakesOfflineServer : BlazorOfflineServer
{
    /// <summary>
    /// The match timings this host passes to the engine. Runtime options exist only on the server, so the browser
    /// host uses defaults that tests can override from the query string (<c>docs/match.md</c>, "Timings come from
    /// the host").
    /// </summary>
    MatchTimings _timings;

    /// <summary>
    /// Random source for host decisions, such as bot names, bot strengths and bot moves. It is separate from the
    /// deal seed, which comes from a cryptographic source and is not used for anything else.
    /// </summary>
    RandomPCG _hostRng = RandomPCG.CreateNew();

    /// <summary>
    /// The <c>matchSeed</c> query parameter, or null if not set. When set, the deal, the opponents and their moves
    /// are the same on every run, so a recorded game can be reproduced exactly.
    /// <para>
    /// Only the offline host supports this. On the server the deal seed must be unpredictable, because a known
    /// seed reveals every hand (<c>docs/match.md</c>). Offline, the browser already computes every seat's cards to
    /// play the bots, so a fixed seed reveals nothing new.
    /// </para>
    /// </summary>
    ulong? _matchSeed;

    /// <summary>
    /// Mixed into <see cref="_matchSeed"/> for the deal, so that the deal and <see cref="_hostRng"/> use different
    /// random sequences, as they do without a fixed seed.
    /// </summary>
    const ulong DealSeedSalt = 0x5EA1_D0D0_1234_ABCDul;

    /// <summary>
    /// The shared driver's state, including the bot move waiting for its think delay. Offline mode has one table,
    /// so one instance is enough. The server's match actor holds the same type.
    /// </summary>
    readonly MatchPendingBotMove _pendingBotMove = new MatchPendingBotMove();

    public override async System.Threading.Tasks.Task InitializeAsync(MetaplayOfflineOptions offlineOptions)
    {
        await base.InitializeAsync(offlineOptions);
        _timings   = ReadTimingsFromQuery();
        _matchSeed = TestQuerySettings.TryGetULong(TestQuerySettings.ReadLocationSearch(), "matchSeed");
        if (_matchSeed.HasValue)
            _hostRng = RandomPCG.CreateFromSeed(_matchSeed.Value);
    }

    /// <summary>
    /// Creates a new table for the session and drops any table restored from the persisted offline state. A match
    /// plays one game, and resuming a table after a page reload is supported only on the server.
    /// </summary>
    protected override SessionStartState CreatePlayerModelForSession(ISharedGameConfig gameConfig)
    {
        SessionStartState state = base.CreatePlayerModelForSession(gameConfig);

        List<SessionStartState.EntityState> entities = new List<SessionStartState.EntityState>();
        foreach (SessionStartState.EntityState entity in state.Entities ?? new List<SessionStartState.EntityState>())
        {
            if (entity.Slot != ClientSlotGame.Match)
                entities.Add(entity);
        }

        EntityId   playerId = state.PlayerModel.PlayerId;
        MatchModel match    = CreateMatch(gameConfig, playerId, ((PlayerModel)state.PlayerModel).BuildPublicIdentity());

        // Passing the member id makes the SDK call GetMemberPrivateState and send the result with the public state,
        // so the player's hand arrives with the subscribe instead of in a separate message.
        entities.Add(new SessionStartState.EntityState(match, ClientSlotGame.Match, playerId));

        return new SessionStartState(state.IsFirstLogin, state.PlayerModel, entities);
    }

    MatchModel CreateMatch(ISharedGameConfig gameConfig, EntityId playerId, PlayerPublicIdentity identity)
    {
        MatchModel match = new MatchModel();
        match.EntityId     = EntityId.CreateRandom(EntityKindGame.Match);
        match.CreatedAt    = MetaTime.Now;
        match.LogicVersion = _logicVersion;
        match.GameConfig   = gameConfig;
        match.Log          = CreateLogForEntity(match);
        ((IMultiplayerModel)match).ResetTime(MetaTime.Now);

        // The human seat starts as arrived, because the table is created for the session's own player. This ends
        // the join window immediately so play starts without waiting.
        //
        // The `seatArrives=false` query parameter seats the human as never arrived. Tests use it to reach an
        // abandoned table, where the join window ends with no human present.
        bool seatArrives = TestQuerySettings.TryGet(TestQuerySettings.ReadLocationSearch(), "seatArrives") != "false";

        // Bot names and strengths come from the session's game config, which offline is the config built into the
        // client. Names are drawn without replacement, so every bot has a different name. MatchHost.SetupNewMatch
        // chooses the bots' cosmetics, as on the server.
        SharedGameConfig config   = (SharedGameConfig)gameConfig;
        List<string>     botNames = BotConfig.ReservedNames(config).Draw(_hostRng.NextULong(), MatchRules.NumSeats - 1);

        // The human seat uses the player's public identity, the same one the Profile preview uses, so both show the
        // same cosmetics (docs/cosmetics.md).
        List<MatchSeat> seats = new List<MatchSeat>(MatchRules.NumSeats);
        seats.Add(new MatchSeat(0, identity, MatchSeatOccupancy.Human, hasArrived: seatArrives));
        for (int seat = 1; seat < MatchRules.NumSeats; seat++)
            seats.Add(new MatchSeat(seat, PlayerPublicIdentity.ForBot(EntityId.None, botNames[seat - 1], avatarId: null), MatchSeatOccupancy.Bot, hasArrived: true));

        ulong dealSeed = _matchSeed.HasValue
                         ? RandomPCG.CreateFromSeed(_matchSeed.Value ^ DealSeedSalt).NextULong()
                         : MatchHost.CreateDealSeed();

        MatchHost.SetupNewMatch(match, dealSeed, _hostRng.NextULong(), seats, _timings, BotConfig.DrawableProfiles(config), MetaTime.Now);
        return match;
    }

    /// <summary>
    /// Handles client messages on the match channel.
    /// <list type="bullet">
    /// <item>A move request always gets an answer. A refused move gets an explicit <c>MatchMoveRefused</c>,
    /// because without it the client's hand would stay locked for the rest of the game.</item>
    /// <item>A client-enqueued action is always refused. Match actions are leader-synchronized, which stops a
    /// well-behaved client from enqueuing one, but the SDK's validation hook allows client actions by default,
    /// so the host must refuse them too (<c>docs/match.md</c>, "Only the host writes the timeline").</item>
    /// </list>
    /// </summary>
    protected override void HandleEntityMessage(ChannelEntity manager, MetaMessage message)
    {
        if (manager.Slot != ClientSlotGame.Match)
        {
            base.HandleEntityMessage(manager, message);
            return;
        }

        switch (message)
        {
            case EntityEnqueueActionsRequest _:
                _log.Warning("Refused a client-originating action on the match timeline: the host is its only writer.");
                return;

            case MatchPlayCardRequest request:
                HandlePlayCardRequest(manager, request);
                return;

            case MatchLeaveRequest _:
                HandleLeaveRequest(manager);
                return;

            case MatchClockSyncRequest request:
                // Clock sync request. Offline, the host clock is the device clock, so the measured offset is zero.
                // The server answers the same request, and the client handles both the same way.
                SendEntityMessage(manager, new MatchClockSyncResponse(request.Nonce, MetaTime.Now));
                return;

            default:
                base.HandleEntityMessage(manager, message);
                return;
        }
    }

    void HandlePlayCardRequest(ChannelEntity manager, MatchPlayCardRequest request)
    {
        MatchModel match = (MatchModel)manager.Model;
        MetaTime   now   = MetaTime.Now;

        MoveRefusalReason refusal = MatchHost.TrySubmitMove(
            match, PlayerModel.PlayerId, request.Seat, request.PlayIndex, request.Card, now, HostEnvironment(manager));

        if (refusal != MoveRefusalReason.None)
            SendEntityMessage(manager, new MatchMoveRefused(request.PlayIndex, request.Card, refusal));

        // Run the table in both cases. After a refused move there is nothing to do. After an accepted move a bot
        // may need to respond.
        MatchHost.RunTable(match, _pendingBotMove, MetaTime.Now, HostEnvironment(manager));
    }

    /// <summary>
    /// Handles the player's Leave request as a disconnect without a grace period: a bot covers the seat
    /// immediately, and the client's match slot is emptied, which returns the player to the menu without a
    /// results screen (<c>docs/match.md</c>, "Leaving the table").
    /// <para>
    /// The server releases the player from the table. Offline, the only client has left and there is no record to
    /// write, so this host removes the match entity instead of playing the game out.
    /// </para>
    /// </summary>
    void HandleLeaveRequest(ChannelEntity manager)
    {
        MatchModel match = (MatchModel)manager.Model;
        int        seat  = match.FindSeatOfPlayer(PlayerModel.PlayerId);
        if (seat < 0)
            return;

        MatchHost.NoteSeatAbsent(match, seat, MetaTime.Now, skipGrace: true, HostEnvironment(manager));
        RemoveMultiplayerEntity(manager);
    }

    /// <summary>
    /// The <see cref="IMatchHostEnvironment"/> for the turn-flow driver: publishing to the offline timeline, host
    /// randomness, and messages to the single client.
    /// </summary>
    /// <remarks>
    /// Cached per channel, because the per-frame table update requests the environment on every frame and a browser
    /// session has only one match channel.
    /// </remarks>
    IMatchHostEnvironment HostEnvironment(ChannelEntity manager)
    {
        if (!ReferenceEquals(_shellManager, manager))
        {
            _shellManager = manager;
            _environment  = new OfflineHostEnvironment(this, manager);
        }
        return _environment;
    }

    ChannelEntity         _shellManager;
    IMatchHostEnvironment _environment;

    sealed class OfflineHostEnvironment : IMatchHostEnvironment
    {
        readonly TableStakesOfflineServer _server;
        readonly ChannelEntity           _manager;

        public OfflineHostEnvironment(TableStakesOfflineServer server, ChannelEntity manager)
        {
            _server  = server;
            _manager = manager;
        }

        public bool Publish(MatchAction action) => _server.PublishAction(_manager, action);

        public ulong NextSeed() => _server._hostRng.NextULong();

        /// <summary>
        /// Called when the host changed a seat's hand without the seat's player playing, such as a deadline
        /// auto-play or a covering bot's move. Sends the updated hand if the seat is the local player's.
        /// </summary>
        public void OnSeatHandChanged(int seat)
        {
            MatchModel match = (MatchModel)_manager.Model;
            if (match.FindSeatOfPlayer(_server.PlayerModel.PlayerId) != seat)
                return;

            _server.SendEntityMessage(_manager, MatchHost.BuildHandDelivery(match, seat));
        }

        /// <summary>
        /// Called when a seat stops being played by its owner. Does nothing, because offline mode has no analytics
        /// pipeline or player actor to notify.
        /// </summary>
        public void OnSeatLost(int seat, MatchSeatLossReason reason)
        {
        }
    }

    /// <summary>
    /// Publishes an action to the timeline and returns whether it succeeded. The turn-flow driver publishes before
    /// it updates the engine, so it must know about a rejected action: the engine is server-only state that is not
    /// checksummed, so no consistency check would catch an engine that got ahead of the timeline
    /// (<c>docs/match.md</c>).
    /// </summary>
    bool PublishAction(ChannelEntity manager, MatchAction action)
    {
        if (!MatchHost.PassesDryRun((MatchModel)manager.Model, action, _log))
            return false;

        ExecuteEntityAction(manager, action);
        return true;
    }

    /// <summary>
    /// Sends the player's hand as a directed message when a client attaches to the table during the session. A
    /// client attached at session start received its hand in the private state at subscribe time.
    /// </summary>
    protected override void HandleEntityActivatedOnClient(ChannelEntity manager)
    {
        if (manager.Slot != ClientSlotGame.Match)
        {
            base.HandleEntityActivatedOnClient(manager);
            return;
        }

        MatchModel match = (MatchModel)manager.Model;
        int        seat  = match.FindSeatOfPlayer(PlayerModel.PlayerId);
        if (seat >= 0)
            SendEntityMessage(manager, MatchHost.BuildHandDelivery(match, seat));
    }

    /// <summary>
    /// Runs the table each frame. The match model does not use ticks, because its phases are driven by messages
    /// and timers, so the base implementation's tick is not called for the match slot. Instead
    /// <c>MatchHost.RunTable</c> handles ended resolve pauses and bot think delays.
    /// </summary>
    protected override void UpdateChannelEntity(ChannelEntity manager)
    {
        if (manager.Slot != ClientSlotGame.Match)
        {
            base.UpdateChannelEntity(manager);
            return;
        }

        MatchHost.RunTable((MatchModel)manager.Model, _pendingBotMove, MetaTime.Now, HostEnvironment(manager));
    }

    /// <summary>
    /// The default match timings (<c>docs/match.md</c>), with overrides from the page's query string for tests.
    /// <para>
    /// The move deadline defaults to zero, which means no deadline, so an offline game never auto-plays for the
    /// player. Tests that need the deadline ring, auto-play or the strike-and-cover rule set <c>moveDeadlineMs</c>,
    /// and the shared driver then enforces it as on the server (<c>docs/web-client.md</c>, "Offline mode").
    /// </para>
    /// </summary>
    static MatchTimings ReadTimingsFromQuery()
    {
        string query = TestQuerySettings.ReadLocationSearch();

        MatchTimings timings = MatchTimings.Default;
        timings.MoveDeadline = MetaDuration.Zero;

        int? moveDeadlineMs = TestQuerySettings.TryGetInt(query, "moveDeadlineMs");
        if (moveDeadlineMs.HasValue)
            timings.MoveDeadline = MetaDuration.FromMilliseconds(moveDeadlineMs.Value);

        // Offline, the only way a seat becomes absent is the Leave request, which skips the disconnect grace.
        // Tests set a long grace so that they can tell a skipped grace from an expired one. Grace expiry is
        // tested against the server in the unit tests and in LiveServerRobustnessTests.
        int? disconnectGraceMs = TestQuerySettings.TryGetInt(query, "disconnectGraceMs");
        if (disconnectGraceMs.HasValue)
            timings.DisconnectGrace = MetaDuration.FromMilliseconds(disconnectGraceMs.Value);

        int? reclaimDelayMs = TestQuerySettings.TryGetInt(query, "reclaimDelayMs");
        if (reclaimDelayMs.HasValue)
            timings.CoveredSeatReclaimDelay = MetaDuration.FromMilliseconds(reclaimDelayMs.Value);

        int? joinWindowMs = TestQuerySettings.TryGetInt(query, "joinWindowMs");
        if (joinWindowMs.HasValue)
            timings.JoinWindow = MetaDuration.FromMilliseconds(joinWindowMs.Value);

        int? strikes = TestQuerySettings.TryGetInt(query, "strikes");
        if (strikes.HasValue)
            timings.StrikesBeforeCover = strikes.Value;

        int? botThinkMs = TestQuerySettings.TryGetInt(query, "botThinkMs");
        if (botThinkMs.HasValue)
        {
            timings.BotThinkDelayMin           = MetaDuration.FromMilliseconds(botThinkMs.Value);
            timings.BotThinkDelayMax           = MetaDuration.FromMilliseconds(botThinkMs.Value);
            timings.BotThinkDelayOccasionalMax = MetaDuration.FromMilliseconds(botThinkMs.Value);
        }

        int? resolvePauseMs = TestQuerySettings.TryGetInt(query, "resolvePauseMs");
        if (resolvePauseMs.HasValue)
            timings.ResolvePause = MetaDuration.FromMilliseconds(resolvePauseMs.Value);

        return timings;
    }

}
