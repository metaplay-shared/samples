using Game.Logic;
using Game.Server.Match;
using Game.Server.Matchmaking;
using Metaplay.Cloud.Entity;
using Metaplay.Cloud.RuntimeOptions;
using Metaplay.Cloud.Sharding;
using Metaplay.Core;
using Metaplay.Core.Client;
using Metaplay.Core.Config;
using Metaplay.Core.League;
using Metaplay.Core.League.Player;
using Metaplay.Core.Model;
using Metaplay.Core.Player;
using Metaplay.Server;
using Metaplay.Server.League;
using Metaplay.Server.MultiplayerEntity.InternalMessages;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Game.Server.Player
{
    [EntityConfig]
    public class PlayerConfig : PlayerConfigBase
    {
        public override Type EntityActorType => typeof(PlayerActor);
    }

    /// <summary>
    /// Server-side entity actor representing a single player. Owns the authoritative <see cref="PlayerModel"/>
    /// and reacts to player actions via <see cref="IPlayerModelServerListener"/>. It handles the work the model
    /// cannot do itself: matchmaking, the pointer to the player's table, recording match results, the
    /// tournament's league calls, and server-decided rewards.
    /// </summary>
    public sealed class PlayerActor : PlayerActorBase<PlayerModel>, IPlayerModelServerListener
    {
        public PlayerActor()
        {
        }

        /// <summary>
        /// The specialized SharedGameConfig (with any A/B experiment overrides) for this player. Available only
        /// after PostLoad, so not in the constructor, Initialize, InitializePersisted or PostLoad.
        /// </summary>
        private SharedGameConfig SharedGameConfig => (SharedGameConfig)_specializedGameConfig.SharedConfig;

        /// <summary>
        /// The name the SDK gives a new model. It is only the <b>fallback</b> name. The displayed name is drawn
        /// from the game config, which is not available this early, so the model's initializer draws it and uses
        /// this name if it cannot (<c>docs/player.md</c>, "Generated names"). The name is derived from the entity
        /// id, so a name in a log can be traced to its account. The SDK's "Initialized new player" log line prints
        /// this fallback name, and the first-login path logs the stored name.
        /// </summary>
        protected override string RandomNewPlayerName()
        {
            return DisplayNameGenerator.FallbackName(_entityId);
        }

        protected override void OnSwitchedToModel(PlayerModel model)
        {
            model.ServerListener = this;
        }

        /// <summary>
        /// Called when the player's public identity changed, after a rename or a cosmetic change. The tournament
        /// division holds a copy of the identity, so this sends it a new avatar (<c>docs/player.md</c>, "Public
        /// identity").
        /// </summary>
        void IPlayerModelServerListener.OnPublicIdentityChanged()
        {
            RefreshTournamentAvatar();
        }

        #region The seasonal tournament

        /// <summary>
        /// The SDK's league integration for this player, with one league in one client slot: the tournament
        /// (<c>docs/seasonal-tournament.md</c>).
        /// <para>
        /// It uses the SDK's default handler, which re-creates the division association on every session start,
        /// adds a concluded division to the player's history, and keeps the player's division pointer in sync
        /// with the league manager. The game's own tournament logic is in this actor.
        /// </para>
        /// </summary>
        public class TournamentLeagueComponent : LeagueComponentBase
        {
            public TournamentLeagueComponent(PlayerActor playerActor) : base(playerActor)
            {
                CreateLeagueIntegrationHandler<DefaultPlayerLeagueIntegrationHandler<TournamentClientState>>(
                    ClientSlotGame.Tournament,
                    TournamentRules.LeagueId,
                    DefaultPlayerLeagueIntegrationHandler<TournamentClientState>.Create);
            }
        }

        protected override LeagueComponentBase CreateLeagueComponent() => new TournamentLeagueComponent(this);

        /// <summary>
        /// The player's avatar as other division members see it. It is built from the model's public identity, so
        /// a standings row shows the same as the Profile page's "how others see you" card (<c>docs/player.md</c>).
        /// </summary>
        protected override PlayerDivisionAvatarBase GetPlayerLeaguesDivisionAvatar(ClientSlot leagueSlot) =>
            new TournamentAvatar(Model.BuildPublicIdentity());

        ILeagueIntegrationHandler TournamentLeague => Leagues?[ClientSlotGame.Tournament];

        /// <summary>
        /// Sends the player's current avatar to their division. The division stores a <b>copy</b> of each player's
        /// identity, so without this, a player who renamed after their last scored match would keep the old name
        /// in the standings for the rest of the season.
        /// </summary>
        void RefreshTournamentAvatar()
        {
            ILeagueIntegrationHandler handler = TournamentLeague;
            EntityId                  division = handler?.DivisionClientState?.CurrentDivision ?? EntityId.None;
            if (!division.IsValid)
                return;

            ContinueTaskOnActorContext(
                handler.JoinOrUpdateDivision(division, subscribe: false),
                _ => { },
                error => _log.Warning("Could not refresh the tournament avatar in group {DivisionId}: {Error}", division, error.Message));
        }

        /// <summary>
        /// Sends the player's tournament score to their division. Called after a result is recorded and on every
        /// session start.
        /// <para>
        /// It sends <b>totals</b>, so calling it again is harmless. The call on session start fixes a division that
        /// missed an update because the server restarted between recording the result and sending it.
        /// </para>
        /// </summary>
        void PublishTournamentScore(EntityId newlyCountedMatchId)
        {
            PlayerTournamentState tournament = Model.Tournament;
            if (newlyCountedMatchId.IsValid && !tournament.CountedMatches.Contains(newlyCountedMatchId))
                return;

            ILeagueIntegrationHandler handler = TournamentLeague;
            if (handler == null || !IsTournamentRunInDivision(tournament, handler.DivisionClientState?.CurrentDivision ?? EntityId.None))
                return;

            handler.EmitDivisionScoreEvent(new TournamentScoreEvent(
                tournament.Wins, tournament.ScoredMatches, tournament.LastWinAt));
        }

        /// <summary>
        /// Whether the player's tournament state belongs to the division that the league integration points at,
        /// which is where score events go. A join points the integration at the new season's division
        /// immediately, but the state is reset only when <see cref="PlayerTournamentJoined"/> executes, so until
        /// then it still holds the previous season's totals.
        /// </summary>
        public static bool IsTournamentRunInDivision(PlayerTournamentState tournament, EntityId leagueDivision) =>
            tournament.HasJoined && leagueDivision.IsValid && leagueDivision == tournament.DivisionId;

        /// <summary>
        /// Sends the player's score to their division again. Sending it again is harmless.
        /// </summary>
        protected override Task OnSessionStartAsync(PlayerSessionParams sessionParams, bool isFirstLogin)
        {
            PublishTournamentScore(EntityId.None);

            return Task.CompletedTask;
        }

        /// <summary>
        /// Enqueues <see cref="PlayerTournamentSeasonConcluded"/> for every season that concluded while the player
        /// was away and has not been reported yet.
        /// <para>
        /// <b>This runs here, not in <see cref="OnSessionStartAsync"/>.</b> The SDK adds a concluded division's
        /// result to the history with a synchronized action that it executes after <see cref="OnSessionStartAsync"/>
        /// and before this method. Running it again is harmless.
        /// </para>
        /// </summary>
        protected override Task OnNewOwnerSession(EntitySubscriber subscriber)
        {
            foreach (TournamentHistoryEntry result in Model.TournamentResults)
            {
                if (!Model.Tournament.HasReported(result.DivisionId))
                    EnqueueServerAction(new PlayerTournamentSeasonConcluded(result.DivisionId));
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Joins the player to the running tournament season.
        /// <para>
        /// <b>Joining twice never creates a second participant.</b> The league answers a player who is already in
        /// it with their current division, and <see cref="PlayerTournamentJoined"/> refuses a season the player is
        /// already in. A double press, a reconnect, or a repeated message therefore leaves the player in the same
        /// division with the same state.
        /// </para>
        /// </summary>
        [MessageHandler]
        async Task HandleTournamentJoinRequest(TournamentJoinRequest request)
        {
            ILeagueIntegrationHandler handler = TournamentLeague;
            if (handler == null)
            {
                SendToClient(TournamentJoinResponse.Refused(request.RequestId, TournamentJoinRefusal.Unavailable));
                return;
            }

            LeagueProxySeasonState season = LeagueStateProxyActor.TryGetSeasonState(TournamentRules.LeagueId);
            if (season?.CurrentSeasonSchedule == null)
            {
                SendToClient(TournamentJoinResponse.Refused(request.RequestId, TournamentJoinRefusal.NoSeasonRunning));
                return;
            }

            // Joining the next season concludes the previous division for this player: the SDK enqueues its result
            // into the history during the join. Read the previous division before the join clears it.
            EntityId previousDivision = Model.TournamentDivision?.CurrentDivision ?? EntityId.None;

            DivisionIndex?          joinedDivision;
            LeagueJoinRefuseReason? refusal;
            try
            {
                (joinedDivision, refusal) = await handler.TryJoinPlayerLeague();
            }
            catch (Exception ex)
            {
                _log.Warning("Tournament join failed: {Error}", ex.Message);
                SendToClient(TournamentJoinResponse.Refused(request.RequestId, TournamentJoinRefusal.Unavailable));
                return;
            }

            if (joinedDivision == null)
            {
                SendToClient(TournamentJoinResponse.Refused(request.RequestId, RefusalOf(refusal)));
                return;
            }

            // This is enqueued after the SDK's history entry for the previous division, and pending synchronized
            // actions execute in order, so the observation finds the result. Without it, the result would be
            // reported only on the next session.
            if (previousDivision.IsValid && previousDivision != joinedDivision.Value.ToEntityId() && !Model.Tournament.HasReported(previousDivision))
                EnqueueServerAction(new PlayerTournamentSeasonConcluded(previousDivision));

            // Use the season's schedule rather than the division's. The division is created from it, and the
            // player's state needs the end time before any division state has reached this actor.
            LeagueSeasonSchedule schedule = season.CurrentSeasonSchedule.Value;

            EnqueueServerAction(new PlayerTournamentJoined(
                joinedDivision.Value.Season,
                joinedDivision.Value.ToEntityId(),
                joinedDivision.Value.Division,
                schedule.StartsAt,
                schedule.EndsAt,
                Model.GameConfig.Global?.ActiveTournamentRewardTable?.Ref?.Id));

            // Do not publish a score here. The join action is enqueued, not executed, so the player's tournament
            // state does not exist yet. The division starts the player at zero, which is correct for a new
            // participant. The first counted match and the next session start both publish the score.
            await PersistStateIntermediate();

            SendToClient(refusal == LeagueJoinRefuseReason.AlreadyInLeague
                ? TournamentJoinResponse.Refused(request.RequestId, TournamentJoinRefusal.AlreadyJoined)
                : TournamentJoinResponse.Success(request.RequestId));
        }

        static TournamentJoinRefusal RefusalOf(LeagueJoinRefuseReason? reason) => reason switch
        {
            LeagueJoinRefuseReason.AlreadyInLeague            => TournamentJoinRefusal.AlreadyJoined,
            LeagueJoinRefuseReason.LeagueNotStarted           => TournamentJoinRefusal.NoSeasonRunning,
            LeagueJoinRefuseReason.SeasonMigrationInProgress  => TournamentJoinRefusal.SeasonChanging,
            _                                                 => TournamentJoinRefusal.Unavailable,
        };

        /// <summary>
        /// Pays out a tournament milestone or placement reward as an <b>enqueued synchronized server action</b>. A
        /// claim changes the checksummed wallet, and <c>ExecuteServerActionImmediately</c> may by SDK contract modify
        /// only <c>[NoChecksum]</c> state. An enqueued action runs at the same model position on the client and the
        /// server, so both evaluate its guard alike. <b>The response is advisory:</b> it comes from a dry run, and the
        /// guard is checked again when the action executes. Nothing is persisted here, because the SDK persists an
        /// enqueued action with the model, so an unclean shutdown rolls back the claim and its grant together.
        /// </summary>
        [MessageHandler]
        void HandleTournamentClaimRequest(TournamentClaimRequest request)
        {
            bool isMilestone = request.Kind == TournamentClaimKind.Milestone;

            MetaActionResult dryRunResult = isMilestone
                ? Model.ClaimTournamentMilestone(request.MilestoneIndex, commit: false)
                : Model.ClaimTournamentPlacement(request.DivisionId, commit: false);

            if (dryRunResult == MetaActionResult.Success)
            {
                EnqueueServerAction(isMilestone
                    ? new PlayerTournamentMilestoneClaim(request.MilestoneIndex)
                    : (PlayerSynchronizedServerAction)new PlayerTournamentPlacementClaim(request.DivisionId));
            }

            SendToClient(new TournamentClaimResponse(
                request.RequestId,
                request.Kind,
                dryRunResult == MetaActionResult.Success,
                dryRunResult == MetaActionResult.Success ? null : dryRunResult.ToString()));
        }

        #endregion

        #region The table this player is at

        /// <summary>
        /// Re-creates the association with the player's current table in the match client slot, so a player who
        /// reloaded the page or lost their connection returns to the table where they are still seated.
        /// <para>
        /// The association is declared here rather than in <c>Initialize</c> so that it belongs to the session. It
        /// is removed when the session ends and declared again from <see cref="PlayerModel.CurrentMatchId"/> on
        /// the next session, so the actor and the session always agree about it.
        /// </para>
        /// </summary>
        protected override async Task OnClientSessionHandshakeAsync(PlayerSessionParams sessionParams)
        {
            if (!Model.CurrentMatchId.IsValid)
                return;

            if (!await ProbeMatchAsync(Model.CurrentMatchId))
                return;

            AddEntityAssociation(CreateMatchAssociation(Model.CurrentMatchId), removeOnSessionEnd: true);
        }

        /// <summary>
        /// Sends <see cref="MatchProbeRequest"/> to the table before re-attaching to it, and clears the pointer if
        /// the table failed.
        /// <para>
        /// A row that cannot be deserialized crashes the table's actor on wake, on every login. <b>A table that times
        /// out or cannot be reached is kept</b>, because a slow wake or a shard shutting down during a rolling deploy
        /// looks the same, and forgetting a live table would lose the game (<c>docs/match.md</c>, "Lock-out cases").
        /// </para>
        /// </summary>
        /// <returns>Whether the association should be declared.</returns>
        async Task<bool> ProbeMatchAsync(EntityId matchId)
        {
            try
            {
                await EntityAskAsync(matchId, MatchProbeRequest.Instance);
                return true;
            }
            catch (EntityAskExceptionBase ex) when (ProbeFailureForgetsTable(ex))
            {
                // The table was reached and failed: it crashed on wake or refused the probe. A later login would
                // fail the same way, so clear the pointer.
                _log.Warning("Table {MatchId} answered a probe by failing ({Error}). Forgetting it.", matchId, ex.Message);
                Model.CurrentMatchId = EntityId.None;
                await PersistStateIntermediate();
                return false;
            }
            catch (EntityAskExceptionBase ex)
            {
                // Keep the pointer. The ask never reached the table, so it says nothing about the table's row.
                // This session starts without the table, and the next session tries again.
                _log.Warning("Table {MatchId} could not be reached by a probe ({Error}). Keeping the pointer and starting without it.", matchId, ex.Message);
                return false;
            }
            catch (TimeoutException)
            {
                // Keep the pointer. A slow wake under load also times out, and the table is probably fine. This
                // session starts without the table, and the next session tries again.
                _log.Warning("Table {MatchId} did not answer a probe in time. Keeping the pointer and starting without it.", matchId);
                return false;
            }
        }

        /// <summary>
        /// Enqueues <see cref="PlayerTargetingFactsSynced"/> if an unsynchronized action changed one of the profile
        /// values that segments read. See <see cref="PlayerTargetingFacts"/> for why segments do not read the
        /// original values.
        /// </summary>
        void SettleTargetingFacts()
        {
            PlayerTargetingFacts facts = PlayerTargetingFacts.Of(Model);
            if (!facts.Equals(Model.TargetingFacts))
                EnqueueServerAction(new PlayerTargetingFactsSynced(facts));
        }

        /// <summary>
        /// Whether a failed table probe means the table's row cannot be used, so the pointer to it is forgotten.
        /// <para>
        /// A crash on wake reaches the asker as <see cref="EntityShard.EntityCrashedError"/>, a missing row as a
        /// refusal from <c>MatchActor.HandleProbeRequest</c>, and any other error the handler throws as
        /// <see cref="EntityShard.UnexpectedEntityAskError"/>. In all three cases the table was reached and failed.
        /// <see cref="EntityShard.EntityUnreachableError"/> means the ask was never delivered (the SDK answers
        /// pending asks with it while the target shard shuts down), so the table may be live and is kept.
        /// </para>
        /// </summary>
        public static bool ProbeFailureForgetsTable(EntityAskExceptionBase failure)
        {
            return failure is not EntityShard.EntityUnreachableError;
        }

        /// <summary>
        /// Called when a table this player sat at has reached a terminal phase. Records the result once, however
        /// many times the table re-sends it (<see cref="MatchHistoryRules.ShouldRecord"/>), and clears the pointer. The
        /// pointer is cleared only after the result is recorded and persisted, because a set pointer keeps the player
        /// out of matchmaking, so the result cannot drop out of the bounded history before it is recorded.
        /// <see cref="ProbeMatchAsync"/>, <see cref="OnAssociatedEntityRefusalAsync"/> and
        /// <see cref="HandleMatchReleaseNotification"/> clear it without recording (see
        /// <see cref="MatchHistoryRules.HasRecorded"/>). The association stays until the session ends, so the finished
        /// table stays on screen.
        /// </summary>
        [EntityAskHandler]
        async Task<MatchRecordResultResponse> HandleRecordResult(MatchRecordResultRequest request)
        {
            bool newlyRecorded = MatchHistoryRules.ShouldRecord(request.Phase, request.Result, Model.MatchHistory, Model.DeclinedMatches, request.MatchId);

            if (newlyRecorded)
            {
                ExecuteServerActionImmediately(new PlayerRecordMatchResult(
                    request.MatchId, request.EndedAt, request.Result.Position, request.Result.TricksWon,
                    request.Result.HumanOpponents, request.Result.FinishedByPlayer));
                SettleTargetingFacts();

                // PlayerRecordMatchResult updated the tournament totals if the player is in a running season and
                // had an attempt left. The model cannot send messages, so the actor sends the totals to the
                // division here. Sending totals rather than a delta means a repeat does not double-count, and a
                // message lost to a restart is corrected by the next one.
                PublishTournamentScore(request.MatchId);

                Model.EventStream.Event(new PlayerEventMatchFinished(
                    request.MatchId, request.Phase, request.Result.Position, request.Result.TricksWon,
                    request.Result.HumanOpponents, request.Result.FinishedByPlayer,
                    request.Result.SeatLossReason, request.MatchDuration));

                _log.Info("Recorded table {MatchId}: position {Position}, {Tricks} tricks. The record is now {Record}.",
                    request.MatchId, request.Result.Position, request.Result.TricksWon, Model.Record);
            }
            else if (request.Phase == MatchPhase.Abandoned && Model.CurrentMatchId == request.MatchId)
            {
                // Nothing to record: a table abandoned before it started has no game and no standings
                // (docs/match.md, "Phases"). The analytics event is still emitted, with rank -1 for no standings.
                //
                // The pointer check prevents emitting the event twice. An abandoned table adds nothing to the
                // history that a repeated request could be recognized by, but the pointer is cleared below, so
                // it matches this table only once.
                Model.EventStream.Event(new PlayerEventMatchFinished(
                    request.MatchId, request.Phase, rank: -1, tricksWon: 0, humanOpponents: 0,
                    finishedByPlayer: false, MatchSeatLossReason.None, request.MatchDuration));

                _log.Info("Table {MatchId} was abandoned before it started. Nothing to record.", request.MatchId);
            }

            if (Model.CurrentMatchId == request.MatchId)
                Model.CurrentMatchId = EntityId.None;

            // Persist before answering. The answer stops the table from sending the result again, so answering
            // before the recorded result is persisted could lose it.
            await PersistStateIntermediate();

            return new MatchRecordResultResponse(newlyRecorded);
        }

        /// <summary>
        /// Called when the player no longer plays their seat. Emits an analytics event only. The same information
        /// reaches the player's record through the match result, so nothing depends on this message arriving
        /// (<c>docs/match.md</c>, "Bot cover and seat reclaim").
        /// </summary>
        [MessageHandler]
        void HandleSeatLost(MatchSeatLostNotification message)
        {
            Model.EventStream.Event(new PlayerEventMatchSeatLost(message.MatchId, message.Reason));
        }

        /// <summary>
        /// Called when the table refused the association, for example because the player has no seat there.
        /// There is no crash and the probe passes, so without clearing the pointer here every later login would
        /// fail the same way (<c>docs/match.md</c>, "Lock-out cases").
        /// </summary>
        protected override async Task<bool> OnAssociatedEntityRefusalAsync(AssociatedEntityRefBase association, InternalEntitySubscribeRefusedBase refusal)
        {
            if (association.GetClientSlot() != ClientSlotGame.Match)
                return await base.OnAssociatedEntityRefusalAsync(association, refusal);

            _log.Warning("Table {MatchId} refused the association ({Refusal}). Forgetting it.", association.AssociatedEntity, refusal.Message);

            if (Model.CurrentMatchId == association.AssociatedEntity)
                Model.CurrentMatchId = EntityId.None;

            // The session retries session start with the associations this actor declares, so remove the refused
            // one or the retry declares it again. The session already knows the subscribe failed, so it is not
            // informed.
            RemoveEntityAssociation(ClientSlotGame.Match, informSession: false);

            // Do not persist here. The SDK persists this actor when this handler returns true
            // (PlayerActorBase.EntityAssociation).
            return true;
        }

        /// <summary>
        /// Called when a table released this player after they used the Leave control. The seat is covered by a
        /// bot and the game continues without them. The pointer and the association are removed now instead of
        /// when the result arrives. Removing the association unsubscribes the session, which takes the client off
        /// the table immediately without a results screen, and the player can enter matchmaking again at once.
        /// <para>
        /// The handler does nothing unless the pointer still matches the table, because otherwise the association in
        /// the match slot belongs to another table.
        /// </para>
        /// </summary>
        [MessageHandler]
        async Task HandleMatchReleaseNotification(MatchReleaseNotification message)
        {
            if (Model.CurrentMatchId != message.MatchId)
                return;

            Model.CurrentMatchId = EntityId.None;

            // Persist the cleared pointer before removing the association. Otherwise a crash between the two
            // could leave the player's state pointing at a table they have left.
            await PersistStateIntermediate();

            RemoveEntityAssociation(ClientSlotGame.Match, informSession: true);
        }

        /// <summary>
        /// Creates the association between this player and a table, in the match client slot.
        /// </summary>
        AssociatedEntityRefBase CreateMatchAssociation(EntityId matchId) =>
            new AssociatedEntityRefBase.Default(ClientSlotGame.Match, sourceEntity: _entityId, associatedEntity: matchId);

        #endregion

        #region The display name

        /// <summary>
        /// The minimum interval between rename <i>messages</i> that this actor processes. It is much shorter than
        /// <see cref="DisplayNamePolicy.RenameCooldown"/> because it limits something else. The cooldown limits how
        /// often the name can change, and each refusal is still answered and costs work on a client-chosen string.
        /// This interval limits how many messages are processed, so a client that floods refused renames cannot
        /// cause unbounded work. A person pressing a button never reaches this limit.
        /// </summary>
        static readonly MetaDuration RenameRequestMinInterval = MetaDuration.FromSeconds(1);

        /// <summary>Limits rename messages to one per <see cref="RenameRequestMinInterval"/>.</summary>
        RequestThrottle _renameThrottle = RequestThrottle.Open(RenameRequestMinInterval);

        /// <summary>
        /// Handles a request to change the player's display name.
        /// <para>
        /// <b>The server decides.</b> The client may run <see cref="DisplayNamePolicy"/> itself to warn the player
        /// early, but that check has no authority (<c>docs/player.md</c>, "Names"). A refusal is answered with its
        /// reason, so the client can tell the player why the name was not accepted.
        /// </para>
        /// </summary>
        [MessageHandler]
        async Task HandleRenameRequest(PlayerRenameRequest request)
        {
            MetaTime now = MetaTime.Now;

            // Drop the message without answering. Answering costs about as much as processing, so answering a
            // flood would not limit the work. A client that sends one rename per button press never gets here.
            if (!_renameThrottle.IsOpenAt(now))
                return;
            _renameThrottle = _renameThrottle.AfterRequestAt(now);

            DisplayNameRefusal refusal = DisplayNamePolicy.ValidateRename(
                request.NewName, Model.PlayerName, Model.LastRenamedAt, now, BotConfig.ReservedNames(SharedGameConfig));

            if (refusal != DisplayNameRefusal.None)
            {
                int submittedLength = DisplayNamePolicy.CountCharacters(DisplayNamePolicy.ToStoredForm(request.NewName));

                // Do not log the attempted name. The game writes no name text to logs or analytics, because a
                // structured log field goes to the same destinations as an event. The length and the refusal
                // reason are enough.
                _log.Info("Refused a rename of {Length} characters: {Refusal}.", submittedLength, refusal);

                // Emit the refusal as an analytics event so the refusal rate can be measured. The payload never
                // contains the name text (docs/analytics.md, "Payload rules").
                Model.EventStream.Event(new PlayerEventNameChangeRejected(refusal, submittedLength, Model.NameChangeCount));

                SendToClient(new PlayerRenameResponse(refusal, Model.PlayerName));
                return;
            }

            // Store the canonical form, so for example a double space is removed instead of causing a refusal
            // that the player cannot see the reason for.
            string newName = DisplayNamePolicy.ToStoredForm(request.NewName);

            // Read the previous name's origin and length before the rename action changes them.
            DisplayNameOrigin previousOrigin = Model.HasCustomizedName ? DisplayNameOrigin.Custom : DisplayNameOrigin.Generated;
            int               previousLength = DisplayNamePolicy.CountCharacters(Model.PlayerName);

            ExecuteServerActionImmediately(new PlayerRenamed(newName, now));
            SettleTargetingFacts();

            // Emit the game's own event, not the SDK's. The SDK emits its name-change event from
            // ValidateAndChangePlayerName, which is the LiveOps Dashboard's path, and its payload contains both
            // names as text, which this game's analytics does not allow (docs/analytics.md, "Payload rules").
            Model.EventStream.Event(new PlayerEventNameChangeAccepted(
                previousOrigin, previousLength, DisplayNamePolicy.CountCharacters(newName), Model.NameChangeCount));

            SendToClient(new PlayerRenameResponse(DisplayNameRefusal.None, newName));

            await PersistStateIntermediate();
        }

        #endregion

        #region The daily reward

        /// <summary>
        /// The minimum interval between daily reward claim <i>messages</i> that this actor processes, for the same
        /// reason as <see cref="RenameRequestMinInterval"/>.
        /// </summary>
        static readonly MetaDuration DailyRewardRequestMinInterval = MetaDuration.FromSeconds(1);

        /// <summary>Limits claim messages to one per <see cref="DailyRewardRequestMinInterval"/>.</summary>
        RequestThrottle _dailyRewardThrottle = RequestThrottle.Open(DailyRewardRequestMinInterval);

        /// <summary>
        /// Tracks whether a claim is enqueued for the current activation and has not run yet
        /// (<see cref="DailyRewardClaimInFlight"/>).
        /// </summary>
        DailyRewardClaimInFlight _dailyRewardClaimInFlight = DailyRewardClaimInFlight.Idle;

        /// <summary>
        /// Clears the in-flight daily-reward claim once it has run, whether the claim paid or was refused
        /// (<see cref="DailyRewardClaimInFlight"/>).
        /// </summary>
        protected override void OnAfterAction(ModelAction action, MetaActionResult result)
        {
            _dailyRewardClaimInFlight = _dailyRewardClaimInFlight.After(action);
        }

        /// <summary>
        /// Handles a claim of the daily login reward.
        /// <para>
        /// <b>The server decides everything, using its own clock.</b> The request has no fields, so a client that
        /// changed its clock or replayed an old request cannot make a claim available or claim twice
        /// (<c>docs/daily-rewards.md</c>). The grant is <b>enqueued</b>, because the wallet is checksummed state and
        /// the action must run at the same timeline position on the client and the server.
        /// </para>
        /// </summary>
        [MessageHandler]
        async Task HandleDailyRewardClaimRequest(PlayerDailyRewardClaimRequest request)
        {
            MetaTime now = MetaTime.Now;

            // Drop the message without answering, for the same reason as in HandleRenameRequest.
            if (!_dailyRewardThrottle.IsOpenAt(now))
                return;
            _dailyRewardThrottle = _dailyRewardThrottle.AfterRequestAt(now);

            PlayerLocalTime    localNow = DailyRewardPolicy.LocalTimeOf(Model, now);
            DailyRewardOutlook outlook  = DailyRewardPolicy.OutlookAt(
                Model.DailyReward, SharedGameConfig.Global, DailyRewardPolicy.ActiveTable(SharedGameConfig), localNow);

            if (!outlook.IsClaimable)
            {
                _log.Info("Refused a daily reward claim: {Refusal}.", outlook.Refusal);
                SendToClient(new PlayerDailyRewardClaimResponse(outlook.Refusal, outlook.Activation.Index));
                return;
            }

            if (_dailyRewardClaimInFlight.IsInFlight(outlook.Activation.Index))
            {
                // The first claim is enqueued but has not run. Answer "already claimed", because the reward is
                // on its way and a second one will not be granted.
                SendToClient(new PlayerDailyRewardClaimResponse(DailyRewardRefusal.AlreadyClaimed, outlook.Activation.Index));
                return;
            }

            // Check the grant against the wallet before enqueuing anything. A cap refusal indicates a config
            // defect, and checking now keeps the activation and streak unchanged instead of enqueuing an action
            // that would be refused.
            WalletSettlement settlement = Model.PreviewWallet(WalletTransaction.Grant(
                EconomyFeature.DailyReward, EconomyReason.DailyReward, outlook.Claim.Reward,
                EconomyContentId.FromString(outlook.Claim.StepId.Value)));

            if (!settlement.IsSuccess)
            {
                _log.Warning("Daily reward step {Step} cannot be granted: {Settlement}.", outlook.Claim.Step, settlement);
                SendToClient(new PlayerDailyRewardClaimResponse(DailyRewardRefusal.WalletRefused, outlook.Activation.Index));
                return;
            }

            _dailyRewardClaimInFlight = DailyRewardClaimInFlight.Enqueued(outlook.Activation.Index);
            EnqueueServerAction(new PlayerDailyRewardClaimed(outlook.Activation.Index, now));

            _log.Info("Daily reward step {Step} enqueued for activation {Activation}; streak {Before} -> {After}.",
                outlook.Claim.Step, outlook.Activation.Index, outlook.Claim.StreakBefore, outlook.Claim.StreakAfter);

            SendToClient(new PlayerDailyRewardClaimResponse(DailyRewardRefusal.None, outlook.Activation.Index));

            await PersistStateIntermediate();
        }

        #endregion

        #region The spin wheel

        /// <summary>
        /// Rate limit for spin messages from this client (<see cref="WheelSpinThrottle"/>). Kept in memory only.
        /// </summary>
        WheelSpinThrottle _wheelSpinThrottle = WheelSpinThrottle.Open;

        /// <summary>
        /// The sector already drawn for a spin that has not resolved yet, so a repeated request gets the same
        /// sector instead of a new draw (<see cref="PendingWheelDraw"/>).
        /// </summary>
        PendingWheelDraw _pendingWheelDraw = PendingWheelDraw.Idle;

        /// <summary>
        /// Random source for spin wheel sectors.
        /// <para>
        /// <b>Seeded from system entropy when the actor starts, never from client input.</b> The draw happens on
        /// the server and reaches the client only inside the action that commits it, so the client cannot predict
        /// the seed, request a new draw, or choose its prize (<c>docs/spin-wheel.md</c>).
        /// </para>
        /// </summary>
        readonly RandomPCG _wheelRng = RandomPCG.CreateNew();

        /// <summary>
        /// Handles a spin of the wheel.
        /// <para>
        /// <b>The server decides everything.</b> The request carries only the spin ordinal the client expects, and
        /// each spin is drawn once (<see cref="PendingWheelDraw"/>). Every sector of the table is checked against the
        /// wallet's caps before the draw, so a player one grant away from a cap keeps their token and no draw is
        /// ever discarded and redrawn. The payout is enqueued for the same reason as in
        /// <see cref="HandleDailyRewardClaimRequest"/>.
        /// </para>
        /// </summary>
        [MessageHandler]
        async Task HandleWheelSpinRequest(PlayerWheelSpinRequest request)
        {
            MetaTime now = MetaTime.Now;

            // Answer instead of dropping, unlike the rename and daily reward throttles. A player can spin
            // repeatedly, so a request inside the window can be a real one, and a request with no answer would
            // leave the client's spin control locked.
            SpinRefusal throttleRefusal = _wheelSpinThrottle.RefusalAt(now);
            if (throttleRefusal != SpinRefusal.None)
            {
                SendToClient(new PlayerWheelSpinResponse(throttleRefusal, Model.SpinWheel.NextOrdinal));
                return;
            }
            _wheelSpinThrottle = _wheelSpinThrottle.AfterRequestAt(now);

            SpinWheelOutlook outlook = SpinWheelPolicy.OutlookFor(Model.SpinWheel, Model.Wallet, SharedGameConfig, request.ExpectedOrdinal);

            if (!outlook.CanSpin)
            {
                _log.Info("Refused a wheel spin: {Refusal}.", outlook.Refusal);
                SendToClient(new PlayerWheelSpinResponse(outlook.Refusal, outlook.NextOrdinal));
                return;
            }

            if (_pendingWheelDraw.HasDrawFor(outlook.NextOrdinal))
            {
                // This spin has already been drawn. Every repeated request gets the same sector, however long the
                // client takes to run the action, so a client cannot delay and ask again to get a different
                // sector. The action is sent again only at the PendingWheelDraw interval, so repeated presses do not
                // queue up copies of it.
                if (_pendingWheelDraw.MayResend(now))
                {
                    EnqueueServerAction(new PlayerWheelSpinResolved(_pendingWheelDraw.Ordinal, _pendingWheelDraw.SectorIndex, now));
                    _pendingWheelDraw = _pendingWheelDraw.Resent(now);

                    _log.Info("Wheel spin {Ordinal} sent again on sector {Sector}.", _pendingWheelDraw.Ordinal, _pendingWheelDraw.SectorIndex + 1);
                }

                // Tell the player that their result is on its way and no second spin will happen.
                SendToClient(new PlayerWheelSpinResponse(SpinRefusal.ResultPending, outlook.NextOrdinal));
                return;
            }

            // Draw the sector uniformly from this actor's random source. This is the only place in the game where
            // a reward is drawn at random rather than looked up.
            int sectorIndex = SpinWheelPolicy.DrawSectorIndex(_wheelRng, outlook.Table);

            _pendingWheelDraw = PendingWheelDraw.Enqueued(outlook.NextOrdinal, sectorIndex, now);
            EnqueueServerAction(new PlayerWheelSpinResolved(outlook.NextOrdinal, sectorIndex, now));

            _log.Info("Wheel spin {Ordinal} enqueued on sector {Sector} of {Table}.",
                outlook.NextOrdinal, sectorIndex + 1, outlook.Table.Id);

            SendToClient(new PlayerWheelSpinResponse(SpinRefusal.None, outlook.NextOrdinal));

            await PersistStateIntermediate();
        }

        #endregion

        #region Matchmaking

        /// <summary>
        /// The player's matchmaking state. It is kept on the actor, not in the model, because it describes an
        /// in-memory queue entry in the matchmaker. A persisted "searching" flag could outlive that entry and leave
        /// the player on a searching screen that nothing would ever end.
        /// <para>
        /// Both waiting states have a timeout that returns the player to the menu, because this actor cannot see
        /// what it is waiting for: a queue entry in the matchmaker's memory, or a table the matchmaker is
        /// creating. Neither timeout fires when matchmaking works normally.
        /// </para>
        /// </summary>
        enum MatchmakingState
        {
            /// <summary>Not looking for a table.</summary>
            Idle = 0,

            /// <summary>In the matchmaker's queue, and free to cancel.</summary>
            Searching = 1,

            /// <summary>
            /// Committed to a seat at a table being created. A cancel is refused in this state.
            /// </summary>
            Reserved = 2,
        }

        MatchmakingState _matchmakingState = MatchmakingState.Idle;

        /// <summary>
        /// When this player pressed Play, or <see cref="MetaTime.Epoch"/> when they are not searching. The
        /// matchmaking wait analytics event is measured from it. The queue entry cannot be used, because it does
        /// not survive a matchmaker restart.
        /// </summary>
        MetaTime _searchStartedAt = MetaTime.Epoch;

        /// <summary>
        /// The latest time by which this player expects to be seated, or <see cref="MetaTime.Epoch"/> when they
        /// are not in the queue. It is the fill wait added to the time they joined. The queue's fill wait runs
        /// from its oldest waiter, so a table forms at or before this time. It is stored rather than recomputed,
        /// so a repeated Play request is answered with the countdown already running instead of a new one.
        /// </summary>
        MetaTime _seatingDeadlineAt = MetaTime.Epoch;

        /// <summary>
        /// Counter that identifies the currently scheduled matchmaking timeout. Timeouts are never cancelled.
        /// Instead, each state change increments the counter, and a timeout whose captured value no longer
        /// matches does nothing.
        /// </summary>
        int _matchmakingTimeoutGeneration;

        static MatchmakingOptions MatchmakingOptions => RuntimeOptionsRegistry.Instance.GetCurrent<MatchmakingOptions>();

        /// <summary>
        /// Whether the player has both a session and a connected client. A session alone is not enough: it stays
        /// alive for <c>Session:SessionLingerDuration</c> after the connection closes, which is much longer than
        /// the fill wait, so a player who closed the tab right after pressing Play still has a session.
        /// </summary>
        bool IsPlayerHere => TryGetSession() != null && Model.IsClientConnected;

        /// <summary>
        /// Handles the player pressing Play by adding them to the matchmaker's queue.
        /// <para>
        /// One queue entry per player is enforced here and again in the matchmaker. Both cases happen in normal
        /// use: a double press of Play, and a second browser tab, which logs in as the same player and terminates
        /// the older session. Without the check, a player could be seated twice at one table or at two tables.
        /// </para>
        /// </summary>
        [MessageHandler]
        void HandleMatchmakingEnterRequest(MatchmakingEnterRequest _)
        {
            if (Model.CurrentMatchId.IsValid)
            {
                _log.Warning("Refused matchmaking: this player is already at table {MatchId}.", Model.CurrentMatchId);
                SendToClient(StatusUpdate(MatchmakingStatus.Unavailable));
                return;
            }

            if (_matchmakingState != MatchmakingState.Idle)
            {
                // A repeated request, not a second entry. Answer with the current status, in case the client lost
                // the first answer.
                SendToClient(StatusUpdate(ToClientStatus(_matchmakingState)));
                return;
            }

            _matchmakingState  = MatchmakingState.Searching;
            _searchStartedAt   = MetaTime.Now;
            _seatingDeadlineAt = _searchStartedAt + MatchmakingOptions.FillWaitDuration;
            ArmSearchTimeout();
            CastMessage(MatchmakerActor.SingletonId, new MatchmakerEnterQueueMessage(Model.BuildPublicIdentity()));
            SendToClient(StatusUpdate(MatchmakingStatus.Searching));
        }

        /// <summary>
        /// Handles the player cancelling the search. A cancel that arrives after this actor committed to a seat is
        /// <b>refused</b>: the answer is <see cref="MatchmakingStatus.SeatReserved"/> and the table assignment follows.
        /// Because this actor alone decides, and the seat reservation is the commit point, the player ends up
        /// either cancelled or seated, never both and never neither.
        /// </summary>
        [MessageHandler]
        void HandleMatchmakingCancelRequest(MatchmakingCancelRequest _)
        {
            if (_matchmakingState == MatchmakingState.Reserved)
            {
                _log.Info("Refused a cancel: this player's seat at a forming table is already committed.");
                SendToClient(StatusUpdate(MatchmakingStatus.SeatReserved));
                return;
            }

            LeaveQueue();
            SendToClient(StatusUpdate(MatchmakingStatus.NotSearching));
        }

        /// <summary>
        /// Answers the matchmaker's request to reserve a seat at a table it is about to create. Accepting commits
        /// the player: after that a cancel is refused, and the player leaves the Reserved state only when the
        /// table assignment arrives, the seat is released, or the reservation times out.
        /// <para>
        /// The actor declines if no client is <b>connected</b>, not only if there is no session, for the reason
        /// given on <see cref="IsPlayerHere"/>. Declining keeps a table from being created with a seat nobody
        /// occupies, or at all when no player in the formation is connected.
        /// </para>
        /// </summary>
        [EntityAskHandler]
        MatchmakerReserveSeatResponse HandleReserveSeatRequest(MatchmakerReserveSeatRequest _)
        {
            SeatReservationAnswer answer = MatchmakingPolicy.AnswerSeatReservation(
                isSearching:       _matchmakingState == MatchmakingState.Searching,
                isAtTable:         Model.CurrentMatchId.IsValid,
                hasSession:        TryGetSession() != null,
                isClientConnected: Model.IsClientConnected);

            switch (answer)
            {
                case SeatReservationAnswer.NotSearching:
                    return MatchmakerReserveSeatResponse.Decline();

                case SeatReservationAnswer.AlreadyAtTable:
                    _log.Warning("Declining a seat: this player is already at table {MatchId}.", Model.CurrentMatchId);
                    EndSearch();
                    return MatchmakerReserveSeatResponse.Decline();

                case SeatReservationAnswer.NotLive:
                    _log.Info("Declining a seat: nobody is on the other end of this player's connection.");
                    EndSearch();
                    return MatchmakerReserveSeatResponse.Decline();
            }

            _matchmakingState = MatchmakingState.Reserved;
            ArmReservationTimeout();
            SendToClient(StatusUpdate(MatchmakingStatus.SeatReserved));
            return MatchmakerReserveSeatResponse.Accept(Model.BuildPublicIdentity());
        }

        /// <summary>
        /// Called when the table has been created with this player seated. The pointer is what re-attaches the
        /// player on later sessions, so it is set and persisted first.
        /// </summary>
        [MessageHandler]
        async Task HandleSeatAssigned(MatchmakerSeatAssignedMessage message)
        {
            if (Model.CurrentMatchId.IsValid)
            {
                // The reservation timed out, the player queued again and was seated elsewhere, and this older
                // assignment arrived late. Accepting it would lose the table the player is at. The older table
                // still holds the seat, covers it with a bot, and sends its result here, so remember it as declined
                // and do not record its result, because the player never saw the game.
                _log.Warning("Declining a seat at table {MatchId}: this player is already at table {CurrentMatchId}.", message.MatchId, Model.CurrentMatchId);
                MatchHistoryRules.NoteDeclined(Model.DeclinedMatches, message.MatchId);
                await PersistStateIntermediate();
                return;
            }

            _matchmakingState = MatchmakingState.Idle;
            _matchmakingTimeoutGeneration++;

            Model.CurrentMatchId = message.MatchId;
            AddEntityAssociation(CreateMatchAssociation(message.MatchId), removeOnSessionEnd: true);
            await PersistStateIntermediate();

            // A seating emits two analytics events, the match start and the matchmaking wait, linked by one
            // correlation id instead of being merged into one event (docs/analytics.md, "Correlation id").
            MetaDuration           searchDuration = WaitedSoFar();
            AnalyticsCorrelationId correlation    = AnalyticsCorrelationId.Create(_entityId, MetaTime.Now, cause: "seating");

            // The wait event is not always emitted. Its start time is in-memory actor state, so if the actor
            // restarted during the search, there is no wait to report. In that case the match start event gets
            // no correlation id, so it does not point to an event that does not exist.
            bool waitReported = TryReportSearchEnded(wasSeated: true, humansGathered: message.NumHumanSeats, correlation);

            Model.EventStream.Event(new PlayerEventMatchStarted(
                message.MatchId, MatchRules.NumSeats - message.NumHumanSeats, searchDuration,
                waitReported ? correlation : AnalyticsCorrelationId.None));

            _log.Info("Seated at table {MatchId}", message.MatchId);
        }

        /// <summary>
        /// Called when the formation this player committed to did not create a table. If the queue still holds
        /// the player's entry (the table could not be created), the player keeps searching. Otherwise the player
        /// returns to the menu and can press Play again, instead of waiting on a search that nothing will end.
        /// </summary>
        [MessageHandler]
        void HandleSeatReleased(MatchmakerSeatReleasedMessage message)
        {
            if (_matchmakingState != MatchmakingState.Reserved)
                return;

            if (!IsPlayerHere)
            {
                // The player left while the seat was committed, a state that OnOwnerSessionEnded does not handle.
                // Returning to searching would keep an absent player at the head of the queue, and nothing would
                // remove the entry later, because the session has already ended. Leave the queue instead.
                _log.Info("A released seat belongs to a player who is no longer here. Leaving the queue.");
                EndSearch();
                CastMessage(MatchmakerActor.SingletonId, new MatchmakerLeaveQueueMessage(_entityId));
                return;
            }

            if (!message.IsStillQueued)
            {
                EndSearch();
                SendToClient(StatusUpdate(MatchmakingStatus.Unavailable));
                return;
            }

            _matchmakingState  = MatchmakingState.Searching;
            _seatingDeadlineAt = MetaTime.Now + MatchmakingOptions.FillWaitDuration;
            ArmSearchTimeout();
            SendToClient(StatusUpdate(MatchmakingStatus.Searching));
        }

        /// <summary>
        /// Schedules the search timeout. The queue is in the matchmaker's memory, so a matchmaker restart (a crash
        /// or a rolling deploy) drops every entry and nothing adds them back. Without this timeout, those players
        /// would stay on the searching screen forever. It also covers a lost queue message.
        /// </summary>
        void ArmSearchTimeout() => ArmMatchmakingTimeout(MatchmakingState.Searching, MatchmakingOptions.SearchTimeout);

        /// <summary>
        /// Ends the search without a seat: this player declined one, or a released seat left nothing to wait for.
        /// Reports the wait like every other search end, and increments <see cref="_matchmakingTimeoutGeneration"/>
        /// so the scheduled timeout does nothing.
        /// </summary>
        void EndSearch()
        {
            _matchmakingState = MatchmakingState.Idle;
            _matchmakingTimeoutGeneration++;
            TryReportSearchEnded(wasSeated: false, humansGathered: 0);
        }

        /// <summary>
        /// Schedules the reservation timeout, for a committed seat whose table never arrives. It covers a
        /// matchmaker that stopped between the reservation and creating the table, which would otherwise leave
        /// a committed player waiting forever without a way to cancel.
        /// </summary>
        void ArmReservationTimeout() => ArmMatchmakingTimeout(MatchmakingState.Reserved, MatchmakingOptions.SeatReservationTimeout);

        /// <summary>
        /// Schedules a timeout for <paramref name="state"/> that returns the player to the menu. Each timeout is
        /// longer than the step it covers (see <see cref="Game.Server.Matchmaking.MatchmakingOptions"/>), so it
        /// does not fire when matchmaking works normally.
        /// </summary>
        void ArmMatchmakingTimeout(MatchmakingState state, TimeSpan timeout)
        {
            _matchmakingTimeoutGeneration++;
            int generation = _matchmakingTimeoutGeneration;

            ScheduleExecuteOnActorContext(timeout, () =>
            {
                if (_matchmakingTimeoutGeneration != generation || _matchmakingState != state)
                    return;

                _log.Warning("Matchmaking timed out while {State}. Putting this player back on the menu.", state);

                // Leave the queue before changing the state, because LeaveQueue acts only while the state is
                // Searching. If the matchmaker is still running (the timeout was caused by a lost message, not a
                // restart), it removes the stale entry instead of seating this player.
                LeaveQueue();

                _matchmakingState = MatchmakingState.Idle;
                TryReportSearchEnded(wasSeated: false, humansGathered: 0);
                SendToClient(StatusUpdate(MatchmakingStatus.Unavailable));
            });
        }

        /// <summary>
        /// Called when the player's session ended. A searching player is removed from the queue here, rather than
        /// only being declined at formation time, so the queue does not collect entries for players who left.
        /// <para>
        /// A player with a committed seat is left as is. The table is being created with that seat, and the
        /// pointer set when the assignment arrives re-attaches the player when they return.
        /// </para>
        /// </summary>
        protected override void OnOwnerSessionEnded(EntitySubscriber subscriber, bool wasKicked)
        {
            base.OnOwnerSessionEnded(subscriber, wasKicked);

            if (_matchmakingState == MatchmakingState.Searching)
                LeaveQueue();
        }

        /// <summary>Leaves the queue if this player is searching. Does nothing in any other state.</summary>
        void LeaveQueue()
        {
            if (_matchmakingState != MatchmakingState.Searching)
                return;

            _matchmakingState = MatchmakingState.Idle;
            TryReportSearchEnded(wasSeated: false, humansGathered: 0);
            CastMessage(MatchmakerActor.SingletonId, new MatchmakerLeaveQueueMessage(_entityId));
        }

        /// <summary>How long this player has been searching, or zero if they are not.</summary>
        MetaDuration WaitedSoFar() => _searchStartedAt > MetaTime.Epoch ? MetaTime.Now - _searchStartedAt : MetaDuration.Zero;

        /// <summary>
        /// Reports the end of a search as an analytics event, however it ended: seated, cancelled, timed out, or
        /// declined. Unsuccessful waits are reported too, because they are needed to tune the fill wait.
        /// <para>
        /// Returns whether an event was emitted. No event is emitted if this actor instance did not see the search
        /// start.
        /// </para>
        /// </summary>
        bool TryReportSearchEnded(bool wasSeated, int humansGathered, AnalyticsCorrelationId correlation = default)
        {
            if (_searchStartedAt == MetaTime.Epoch)
                return false;

            Model.EventStream.Event(new PlayerEventMatchmakingWait(WaitedSoFar(), humansGathered, wasSeated, correlation));
            _searchStartedAt   = MetaTime.Epoch;
            _seatingDeadlineAt = MetaTime.Epoch;
            return true;
        }

        /// <summary>
        /// Creates the status message for <paramref name="status"/>. Only <see cref="MatchmakingStatus.Searching"/>
        /// includes the seating deadline, so a leftover deadline from an ended search is never sent with another
        /// status.
        /// </summary>
        MatchmakingStatusUpdate StatusUpdate(MatchmakingStatus status) =>
            status == MatchmakingStatus.Searching
                ? new MatchmakingStatusUpdate(status, _seatingDeadlineAt)
                : new MatchmakingStatusUpdate(status);

        static MatchmakingStatus ToClientStatus(MatchmakingState state) => state switch
        {
            MatchmakingState.Searching => MatchmakingStatus.Searching,
            MatchmakingState.Reserved  => MatchmakingStatus.SeatReserved,
            _                          => MatchmakingStatus.NotSearching,
        };

        #endregion
    }
}
