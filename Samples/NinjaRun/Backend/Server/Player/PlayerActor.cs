using Game.Logic;
using Game.Server.CourseGhosts;
using Game.Server.MapRegistry;
using Metaplay.Cloud.Application;
using Metaplay.Cloud.Entity;
using Metaplay.Cloud.RuntimeOptions;
using Metaplay.Core;
using Metaplay.Core.Player;
using Metaplay.Server;
using System;
using System.Collections.Generic;
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
    /// and reacts to player actions via <see cref="IPlayerModelServerListener"/>. It is also where a finished
    /// run is checked: the recording arrives here as a message, and the actor recomputes the result before any
    /// of it reaches the model.
    /// </summary>
    public sealed class PlayerActor : PlayerActorBase<PlayerModel>, IPlayerModelServerListener
    {
        int _poolNotifiedForTicketId;

        /// <summary>
        /// Every player starts as a Masked Ninja, which <see cref="PlayerModel.HasName"/> reads as having no name.
        /// </summary>
        protected override string RandomNewPlayerName()
        {
            return TimeTrialSkills.MaskedNinjaName;
        }

        protected override void OnSwitchedToModel(PlayerModel model)
        {
            model.ServerListener = this;
        }

        /// <summary>
        /// Open a run on a map drawn from the registry's active set.
        /// </summary>
        [MessageHandler]
        async Task HandleTimeTrialStartRequest(TimeTrialStartRequest request)
        {
            List<CourseId> candidates = await TryGetActiveMapIdsAsync();
            if (candidates == null)
                return;

            (CourseId courseId, CourseDefinition course) = PickMap(candidates);
            if (course == null)
            {
                _log.Warning("Could not open a run: none of the {Count} candidate map(s) resolved against the published config", candidates.Count);
                return;
            }

            await OpenTimeTrialAsync(courseId, course, isPractice: false);
        }

        /// <summary>
        /// Open a practice run on a course a developer names, which need not be published or active. The race is
        /// replayed and shown but counts for nothing.
        /// </summary>
        [MessageHandler]
        async Task HandleTimeTrialStartOnMapRequest(TimeTrialStartOnMapRequest request)
        {
            // A message cannot be development-only the way an action is, so the handler applies the same check
            // the SDK applies to a development-only action.
            if (!Model.AllowsDeveloperActions)
            {
                _log.Warning("Refused a developer race on {Course}: the player is not a developer and the server's development features are off", request.CourseId);
                return;
            }

            CourseDefinition course = CourseResolver.TryResolve(request.CourseId, Model.GameConfig);
            if (course == null)
            {
                _log.Warning("Refused a developer race on {Course}: the published config does not resolve it", request.CourseId);
                return;
            }

            _log.Info("Opening a developer race on {Course}", request.CourseId);

            // Asking the course's pool for a lineup seeds a new pool, as the publication gate does for a map. A
            // practice ticket never writes a result to it.
            await OpenTimeTrialAsync(request.CourseId, course, isPractice: true);
        }

        /// <summary>
        /// Tell the client, at the start of every session, whether this server runs with its development
        /// features on. It is one of the two things that allow developer-only requests, and the only one the
        /// client cannot read off the model on its own.
        /// </summary>
        protected override Task OnSessionStartAsync(PlayerSessionParams sessionParams, bool isFirstLogin)
        {
            Model.ServerDevelopmentFeaturesEnabled = RuntimeOptionsRegistry.Instance.GetCurrent<EnvironmentOptions>().EnableDevelopmentFeatures;
            return Task.CompletedTask;
        }

        /// <summary>
        /// Select the field for <paramref name="courseId"/> and open the run on it.
        /// </summary>
        async Task OpenTimeTrialAsync(CourseId courseId, CourseDefinition course, bool isPractice)
        {
            // The course's pool paces its lineup against this player's best there. A pool that cannot be reached
            // gives no lineup, and the field is the course's scripted runners instead.
            TimeTrialResult      best   = Model.GetBestResult(courseId);
            List<TimeTrialGhost> selected = await TrySelectLineupAsync(courseId, best?.FinishTimeMs ?? 0);
            List<TimeTrialGhost> lineup   = TimeTrialSeedRunners.RunField(Model.HasCompletedARun, selected, course, Model.GameConfig.TimeTrialRules);

            // Enqueued rather than run here: the ticket is checksummed state, so the client picks the slot and
            // both sides run the action at the same point in the model's timeline.
            EnqueueServerAction(new PlayerOpenTimeTrial(courseId, lineup, isPractice));
        }

        /// <summary>
        /// The registry's active set, or null when the registry cannot be reached or holds nothing.
        /// </summary>
        async Task<List<CourseId>> TryGetActiveMapIdsAsync()
        {
            try
            {
                MapRegistryActiveSetResponse response = await EntityAskAsync(MapRegistries.EntityId, new MapRegistryActiveSetRequest());
                if (response.ActiveMapIds != null && response.ActiveMapIds.Count > 0)
                    return response.ActiveMapIds;

                _log.Warning("Could not open a run: the map registry holds no active map");
            }
            catch (Exception ex)
            {
                _log.Warning("Could not open a run: the map registry could not be reached: {Error}", ex.Message);
            }

            return null;
        }

        /// <summary>
        /// One map out of <paramref name="candidates"/>, with the course it composes into, or nulls when no
        /// candidate resolves against this config.
        /// </summary>
        (CourseId CourseId, CourseDefinition Course) PickMap(List<CourseId> candidates)
        {
            // The map the player last ran is left out: the set is small, and a second press that gives back the
            // same map reads as the button not having worked.
            List<CourseId> drawFrom = new List<CourseId>();
            foreach (CourseId mapId in candidates)
            {
                if (mapId != null && mapId != Model.LastTimeTrialCourseId)
                    drawFrom.Add(mapId);
            }

            // Nothing else on offer: the player is given the map they were just on rather than no run at all.
            if (drawFrom.Count == 0 && Model.LastTimeTrialCourseId != null && candidates.Contains(Model.LastTimeTrialCourseId))
                drawFrom.Add(Model.LastTimeTrialCourseId);

            RandomPCG rng = RandomPCG.CreateNew();

            while (drawFrom.Count > 0)
            {
                int              index  = rng.NextInt(drawFrom.Count);
                CourseId         mapId  = drawFrom[index];
                CourseDefinition course = CourseResolver.TryResolve(mapId, Model.GameConfig);

                if (course != null)
                    return (mapId, course);

                // A map this config cannot resolve would open a ticket nothing could replay.
                drawFrom.RemoveAt(index);
            }

            return (null, null);
        }

        async Task<List<TimeTrialGhost>> TrySelectLineupAsync(CourseId courseId, int playerBestMs)
        {
            try
            {
                CourseGhostLineupResponse response = await EntityAskAsync(
                    CourseGhostPools.EntityIdFor(courseId),
                    new CourseGhostLineupRequest(courseId, _entityId, playerBestMs, TimeTrialSeedRunners.GhostsPerRun));

                return response.Lineup;
            }
            catch (Exception ex)
            {
                _log.Warning("Could not get opponents for {Course}: {Error}", courseId, ex.Message);
                return null;
            }
        }

        /// <summary>
        /// A finished run submitted by the client. The server recomputes the result from the recording under the
        /// ticket the run was opened with, and accepts or rejects the run on that result alone.
        /// </summary>
        [MessageHandler]
        async Task HandleTimeTrialSubmitRequest(TimeTrialSubmitRequest request)
        {
            TimeTrialRejectReason reason = TimeTrialRejectReason.None;
            TimeTrialResult       result = null;

            // The ticket and the untrusted payload are checked before the replay, which is the costly step.
            // One run is short enough to replay inline.
            TimeTrialTicket ticket = Model.ActiveTimeTrial;
            if (ticket == null)
                reason = TimeTrialRejectReason.NoActiveTicket;
            else if (ticket.TicketId != request.TicketId)
                reason = TimeTrialRejectReason.TicketMismatch;
            else if (ticket.IssuedAt + MetaDuration.FromSeconds(Model.GameConfig.Global.TimeTrialTicketLifetimeSeconds) < Model.CurrentTime)
                reason = TimeTrialRejectReason.TicketExpired;
            else if (ticket.RulesetVersion != TimeTrialConstants.RulesetVersion)
                reason = TimeTrialRejectReason.UnsupportedRuleset;
            else if (!TimeTrialRecording.IsWellFormed(request.Recording))
                reason = TimeTrialRejectReason.MalformedRecording;
            else
            {
                // Resolved from the ticket rather than its id alone: a ticket opened on a part editor draft
                // carries the draft, and its id names nothing on its own.
                CourseDefinition course = CourseResolver.TryResolve(ticket, Model.GameConfig);
                if (course == null)
                    reason = TimeTrialRejectReason.UnknownCourse;
                else
                {
                    // The ticket names the Skill the run was opened with, so the recording is recomputed under
                    // the same movement values the client played it on and not under whatever is equipped now.
                    result = TimeTrialSimulation.Replay(course, TimeTrialSkills.EffectiveRules(Model.GameConfig, ticket.SkillId), request.Recording);
                    if (result == null)
                        reason = TimeTrialRejectReason.DidNotFinish;
                }
            }

            if (result == null)
            {
                _log.Info("Time trial submission for ticket {TicketId} not counted: {Reason}", request.TicketId, reason);
                EnqueueServerAction(new PlayerRejectTimeTrialResult(request.TicketId, reason));
                return;
            }

            // A practice run is replayed like any other so its time is shown as the server's own. The pool is
            // not asked.
            if (ticket.IsPractice)
            {
                _log.Info("Practice run on {Course} replayed: {Result}. Nothing is recorded for it", ticket.CourseId, result);
                EnqueueServerAction(new PlayerAcceptTimeTrialResult(request.TicketId, result, standing: null));
                return;
            }

            // The course's pool is told about the run before it is recorded, because where the run stands is
            // the pool's answer and is written onto the player together with the result. Whether it is the
            // player's best in that pool is decided here, from the same model state the action is about to
            // commit. It is the best recorded into the pool rather than the personal best, which can include
            // runs made without a name that the pool never saw.
            int               previousMs   = Model.GetPoolBestTimeMs(ticket.CourseId);
            bool              isNewBest    = Model.IsNewPoolBest(ticket.CourseId, result.FinishTimeMs);
            TimeTrialStanding standing     = await TryRecordInPoolAsync(ticket, result, isNewBest, previousMs, request.Recording);

            // Enqueued rather than run here: the action writes checksummed state, so the client picks the slot
            // and both sides run it at the same point in the model's timeline.
            EnqueueServerAction(new PlayerAcceptTimeTrialResult(request.TicketId, result, standing));
        }

        /// <summary>
        /// Count an accepted run into its course's pool and get back where it stands. Null when the pool is not
        /// asked or cannot be reached, which costs the run only its standing.
        /// </summary>
        async Task<TimeTrialStanding> TryRecordInPoolAsync(TimeTrialTicket ticket, TimeTrialResult result, bool isNewBest, int previousBestMs, TimeTrialRecording recording)
        {
            // A Masked Ninja is on nobody's board: without a name the run is neither stored as a ghost
            // other players can be raced against nor counted into the course's time distribution. The run is
            // still accepted and paid against its own lineup, which is how the player earns the Name Tag.
            if (!Model.HasName)
                return null;

            // One submission per ticket reaches the pool, so a client that sends the same run twice cannot
            // count it twice into the course's distribution.
            if (_poolNotifiedForTicketId == ticket.TicketId)
                return null;
            _poolNotifiedForTicketId = ticket.TicketId;

            try
            {
                CourseGhostRunAcceptedResult response = await EntityAskAsync(
                    CourseGhostPools.EntityIdFor(ticket.CourseId),
                    new CourseGhostRunAccepted(
                        ticket.CourseId,
                        _entityId,
                        Model.GhostDisplayName,
                        result.FinishTimeMs,
                        isNewBest,
                        previousBestMs,
                        recording,
                        result.GateTimesMs,
                        result.ClimbEntryTimesMs,
                        ticket.SkillId));

                return response.Standing;
            }
            catch (Exception ex)
            {
                _log.Warning("Could not record the run on {Course}: {Error}", ticket.CourseId, ex.Message);
                return null;
            }
        }
    }
}
