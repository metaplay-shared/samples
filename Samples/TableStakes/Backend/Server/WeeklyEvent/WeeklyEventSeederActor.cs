using Game.Logic;
using Metaplay.Cloud.Entity;
using Metaplay.Cloud.RuntimeOptions;
using Metaplay.Cloud.Sharding;
using Metaplay.Core;
using Metaplay.Core.Config;
using Metaplay.Server;
using Metaplay.Server.LiveOpsEvent;
using Metaplay.Server.LiveOpsTimeline;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Game.Server.WeeklyEvent
{
    [EntityConfig]
    public class WeeklyEventSeederEntityConfig : EphemeralEntityConfig
    {
        public override EntityKind        EntityKind           => EntityKindGame.WeeklyEventSeeder;
        public override Type              EntityActorType      => typeof(WeeklyEventSeederActor);
        public override NodeSetPlacement  NodeSetPlacement     => NodeSetPlacement.Service;
        public override IShardingStrategy ShardingStrategy     => ShardingStrategies.CreateSingletonService();
        public override TimeSpan          ShardShutdownTimeout => TimeSpan.FromSeconds(30);
    }

    /// <summary>
    /// Keeps weekly themed events on the LiveOps timeline for a rolling number of weeks ahead
    /// (<c>docs/weekly-event.md</c>). A LiveOps event cannot come from game config, so this singleton service
    /// entity creates them. It reaches the timeline with an in-cluster <c>EntityAsk</c>, which needs no Admin API
    /// credentials, and repeats the pass because the horizon is measured from the time of each pass.
    /// <b>Running a pass twice is harmless.</b> The pass does not read the timeline. It sends every week that should
    /// exist, with an id derived from its calendar week, to the SDK's import with
    /// <see cref="LiveOpsEventExportImport.ImportConflictPolicy.KeepOld"/>, which ignores a week already there.
    /// </summary>
    public sealed class WeeklyEventSeederActor : EphemeralEntityActor
    {
        /// <summary>
        /// The id of the only seeder entity. The singleton strategy creates only the id with value zero.
        /// </summary>
        public static readonly EntityId SingletonId = EntityId.Create(EntityKindGame.WeeklyEventSeeder, 0);

        static WeeklyEventSeedingOptions Options => RuntimeOptionsRegistry.Instance.GetCurrent<WeeklyEventSeedingOptions>();

        // The SDK's default policy shuts down an entity without subscribers, and this entity never has any.
        protected override AutoShutdownPolicy ShutdownPolicy => AutoShutdownPolicy.ShutdownNever();

        /// <summary>
        /// The number of completed passes and the result of the last pass. Kept in memory only, because it is
        /// used only for reporting. A pass reads the calendar and the timeline, never these fields.
        /// </summary>
        int                         _passesRun;
        WeeklyEventSeedPassResponse _lastPass;

        protected override Task Initialize()
        {
            WeeklyEventSeedingOptions options = Options;

            if (options.Enabled)
                _log.Info("Weekly-event seeder up. Keeping {Horizon} weeks seeded, re-checking every {Interval}.", options.HorizonWeeks, options.PassInterval);
            else
                _log.Info("Weekly-event seeding is off. Weekly events are whatever an operator puts on the timeline, and a player holding none sees the designed empty state. Turning the option back on resumes seeding at the next interval; no restart is needed.");

            // Schedule the timer even when seeding is off, and let each pass check the option. The option can be
            // changed at run time, and an actor that stopped scheduling while seeding was off would need a
            // restart to resume.
            ScheduleExecuteOnActorContext(options.FirstPassDelay, RunScheduledPassAsync);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Runs one scheduled pass and always schedules the next one.
        /// <para>
        /// A pass that could not run, usually because the active game config has not reached this node yet after
        /// a start, is retried after <see cref="WeeklyEventSeedingOptions.RetryDelay"/> instead of
        /// <see cref="WeeklyEventSeedingOptions.PassInterval"/>, so a new environment gets its first week quickly.
        /// </para>
        /// </summary>
        async Task RunScheduledPassAsync()
        {
            // Read the options once for the whole pass, because runtime options can change at any moment.
            WeeklyEventSeedingOptions options       = Options;
            TimeSpan                  nextPassDelay = options.PassInterval;

            try
            {
                if (!options.Enabled)
                    return;

                WeeklyEventSeedPassResponse pass = await RunPassAsync();

                if (!pass.Ran)
                {
                    _log.Warning("A weekly-event seeding pass did not run: {Problems}. Retrying in {Delay}.", string.Join("; ", pass.Problems), options.RetryDelay);
                    nextPassDelay = options.RetryDelay;
                }
                else if (pass.NumWeeksCreated > 0)
                    _log.Info("Seeded {Created} weekly events; {Kept} were already on the timeline. Weekly events now run through {SeededThrough}.", pass.NumWeeksCreated, pass.NumWeeksKept, pass.SeededThrough);
                else
                    _log.Info("Weekly events already run through {SeededThrough}; {Kept} weeks were on the timeline and none were added.", pass.SeededThrough, pass.NumWeeksKept);
            }
            catch (Exception ex)
            {
                _log.Error("A weekly-event seeding pass failed: {Error}", ex);
                nextPassDelay = options.RetryDelay;
            }
            finally
            {
                ScheduleExecuteOnActorContext(nextPassDelay, RunScheduledPassAsync);
            }
        }

        /// <summary>
        /// Test only, reachable only through the test endpoint. Reports the last pass, and first runs a pass if
        /// asked. It runs the same pass as the schedule, so tests observe the real behavior.
        /// </summary>
        [EntityAskHandler]
        async Task<WeeklyEventSeedPassResponse> HandleSeedPassRequest(WeeklyEventSeedPassRequest request)
        {
            if (!request.RunNow)
            {
                return (_lastPass ?? WeeklyEventSeedPassResponse.Failed("no seeding pass has run yet")).WithPassesRun(_passesRun);
            }

            if (!Options.Enabled)
                return WeeklyEventSeedPassResponse.Failed("weekly-event seeding is turned off (WeeklyEventSeeding:Enabled)").WithPassesRun(_passesRun);

            return await RunPassAsync();
        }

        /// <summary>
        /// Computes the weeks that should exist now, validates their content, and imports them with the SDK's
        /// import. Fails the whole pass if any week is invalid.
        /// </summary>
        async Task<WeeklyEventSeedPassResponse> RunPassAsync()
        {
            ActiveGameConfig active = GlobalStateProxyActor.ActiveGameConfig.Get();
            if (active == null)
                return StoreLastPass(WeeklyEventSeedPassResponse.Failed("no active game config has reached this node yet"));

            FullGameConfig   fullConfig = active.BaselineGameConfig;
            SharedGameConfig shared     = fullConfig?.SharedConfig as SharedGameConfig;

            if (shared?.WeeklyEventTemplates == null || shared.WeeklyEventTemplates.Count == 0)
                return StoreLastPass(WeeklyEventSeedPassResponse.Failed("the active game config holds no weekly-event templates to create weeks from"));

            List<WeeklyEventSeedWeek> plan = WeeklyEventSeeding.Plan(MetaTime.Now, Options.HorizonWeeks, shared.WeeklyEventTemplates.Values);
            if (plan.Count == 0)
                return StoreLastPass(WeeklyEventSeedPassResponse.Failed("the seed calendar produced no weeks"));

            // Validate the content before the import. The import validates only the settings, not the game's
            // content rules, so an invalid week would otherwise reach players instead of the log.
            List<string> problems = WeeklyEventSeedPackage.ProblemsWith(plan, fullConfig);
            if (problems.Count > 0)
                return StoreLastPass(WeeklyEventSeedPassResponse.Failed(problems.ToArray()));

            ImportLiveOpsEventsResponse response;
            try
            {
                response = await EntityAskAsync(
                    LiveOpsTimelineManager.EntityId,
                    new ImportLiveOpsEventsRequest(
                        validateOnly:   false,
                        conflictPolicy: LiveOpsEventExportImport.ImportConflictPolicy.KeepOld,
                        package:        WeeklyEventSeedPackage.Build(plan)));
            }
            catch (Exception ex)
            {
                return StoreLastPass(WeeklyEventSeedPassResponse.Failed($"the LiveOps timeline did not answer: {ex.Message}"));
            }

            if (!response.IsValid)
            {
                List<string> refusals = new List<string>();
                foreach (LiveOpsEventDiagnostic diagnostic in response.GeneralDiagnostics)
                    refusals.Add(diagnostic.Message);
                foreach (ImportLiveOpsEventsResponse.EventResult result in response.EventResults)
                {
                    if (!result.IsValid)
                        refusals.Add($"{result.OccurrenceId}: {result.Outcome}");
                }

                // The import is all or nothing, so a refusal leaves the timeline unchanged. A partial import
                // could leave a gap in the horizon that a later pass could not tell apart from a week an
                // operator removed.
                return StoreLastPass(WeeklyEventSeedPassResponse.Failed(refusals.ToArray()));
            }

            int created = 0;
            int kept    = 0;
            foreach (ImportLiveOpsEventsResponse.EventResult result in response.EventResults)
            {
                if (result.Outcome == LiveOpsEventExportImport.EventImportOutcome.CreateNew)
                    created++;
                else if (result.Outcome == LiveOpsEventExportImport.EventImportOutcome.IgnoreDueToExisting)
                    kept++;
            }

            _passesRun++;
            return StoreLastPass(new WeeklyEventSeedPassResponse(ran: true, created, kept, WeeklyEventSeeding.SeededThrough(plan), problems: null));
        }

        /// <summary>
        /// Stores the result of a pass, <b>whether or not it ran</b>, so that the report shows a failing pass
        /// instead of the last success. <see cref="_passesRun"/> counts only completed passes, so it shows
        /// whether the server has seeded yet.
        /// </summary>
        WeeklyEventSeedPassResponse StoreLastPass(WeeklyEventSeedPassResponse pass)
        {
            _lastPass = pass.WithPassesRun(_passesRun);
            return _lastPass;
        }
    }
}
