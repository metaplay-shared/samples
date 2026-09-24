// ---------------------------------------------------------------------------------------------------------
// VENDORED SDK SOURCE — NOT SAMPLE CODE.
//
// Copied from MetaplaySDK/Backend/Server/MultiplayerEntity/ at SDK 38.0.0 + SDK-263 (per-member timeline
// operations). Present only because SDK-263 ships in R39 and this sample targets released R38. Delete this
// folder on the R39 upgrade and point MatchActor back at the SDK's base. Differences from the original are
// marked `VENDORED CHANGE:`. See README.md in this folder.
// ---------------------------------------------------------------------------------------------------------

// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

#nullable enable

using Metaplay.Cloud.Application;
using Metaplay.Cloud.Entity;
using Metaplay.Cloud.RuntimeOptions;
using Metaplay.Core;
using Metaplay.Core.IO;
using Metaplay.Core.Memory;
using Metaplay.Core.Model;
using Metaplay.Core.Model.JournalCheckers;
using Metaplay.Core.MultiplayerEntity;
using Metaplay.Core.MultiplayerEntity.Messages;
using Metaplay.Core.Serialization;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using static System.FormattableString;

using Metaplay.Server;
using Metaplay.Server.MultiplayerEntity;

namespace Game.Server.SdkPreview
{
    // VENDORED CHANGE: EntityChecksumMode and EntityConsistencyChecks are NOT copied here. They are
    // plain public types a game already names (MatchActor's DebugOptions builds one of each), and a
    // second declaration would be a distinct type that the SDK's own signatures reject. The SDK's are
    // used instead, via `using Metaplay.Server.MultiplayerEntity`.

    internal sealed class MultiplayerEntityTimelineOverrideBuffer
    {
        MetaDictionary<int, ModelAction> _overrides = new MetaDictionary<int, ModelAction>();

        public bool IsEmpty => _overrides.Count == 0;

        /// <summary>
        /// Makes the subscriber receive <paramref name="action"/> in place of the pending operation at <paramref name="pendingOpIndex"/>.
        /// Replaces an earlier override at the same index.
        /// </summary>
        public void Set(int pendingOpIndex, ModelAction action)
        {
            _overrides[pendingOpIndex] = action;
        }

        /// <summary>
        /// Returns the update the subscriber receives in place of the shared <paramref name="update"/>: the overridden operations
        /// are substituted, everything else and both checksum fields are carried over. The overrides within the update are consumed,
        /// the rest are re-keyed relative to the next flush. <paramref name="update"/> itself is not modified.
        /// </summary>
        public EntityTimelineUpdateMessage Consume(EntityTimelineUpdateMessage update)
        {
            int numFlushed = update.Operations.Count;
            List<ModelAction> operations = new List<ModelAction>(update.Operations);
            MetaDictionary<int, ModelAction> remaining = new MetaDictionary<int, ModelAction>();
            foreach ((int index, ModelAction action) in _overrides)
            {
                if (index < numFlushed)
                    operations[index] = action;
                else
                    remaining.Add(index - numFlushed, action);
            }
            _overrides = remaining;
            return new EntityTimelineUpdateMessage(operations, update.FinalChecksum, update.DebugChecksums);
        }

        public void Clear()
        {
            _overrides.Clear();
        }
    }

    public abstract partial class MultiplayerEntityActorBase<TModel, TAction>
    {
        internal sealed class FlushActionsCommand { public static FlushActionsCommand Instance = new FlushActionsCommand(); }
        internal sealed class PruneDesyncSnapshotsCommand { public static PruneDesyncSnapshotsCommand Instance = new PruneDesyncSnapshotsCommand(); }

        partial class SessionUserData
        {
            public readonly List<uint> PingMarkersWaitingForTick = new List<uint>();
            public readonly List<uint> PingMarkersWaitingForFlush = new List<uint>();

            /// <summary>
            /// Operations this session receives in place of the shared ones on the next flush.
            /// </summary>
            public readonly MultiplayerEntityTimelineOverrideBuffer FlushActionOverrides = new MultiplayerEntityTimelineOverrideBuffer();
        }

        /// <summary>
        /// The shared and persisted entity state. This should not be modified directly and changes should be done via ModelActions instead in most cases.
        /// See Documentation for more information.
        /// <para>
        /// This field is <c>null</c> until Model is set up and Entity Initialization is completed, i.e. earliest in and after <see cref="OnEntityInitialized"/>.
        /// </para>
        /// </summary>
        protected TModel Model { get; private set; }

        public class TickRateSetting
        {
            public TimeSpan WhenNoClientsConnected;
            public TimeSpan WithClientsConnected;

            /// <param name="whenNoClientsConnected">Tick interval when there are no clients connected</param>
            /// <param name="withClientsConnected">Tick interval when there are clients connected. If zero (default), the tick rate is set to the Models tick rate.</param>
            public TickRateSetting(TimeSpan whenNoClientsConnected, TimeSpan withClientsConnected = default)
            {
                WhenNoClientsConnected = whenNoClientsConnected;
                WithClientsConnected = withClientsConnected;
            }
        }

        /// <summary>
        /// Time interval determining how often the model is updated to run any pending Ticks(). If TicksPerSecond is 10 and
        /// this is 1s, the model (if idle) will be woken once a second to run on average 10 ticks. Defaults to 5 seconds.
        /// </summary>
        protected virtual TickRateSetting TickRate => new TickRateSetting(whenNoClientsConnected: TimeSpan.FromSeconds(5), withClientsConnected: TimeSpan.Zero);

        /// <summary>
        /// Determines if the Model should be ticked forward automatically.
        /// <para>
        /// Generally, there are only two reasons to set this to <c>false</c>:
        /// <list type="bullet">
        /// <item>Keeping this <c>false</c> until first player joins this Multiplayer Entity. This removes the need to specially handle Model before it has any Participants.</item>
        /// <item>Setting this <c>false</c> after last player leaves the Multiplayer Entity, or Multiplayer game is concluded. This saves resources as ticking is no longer useful.</item>
        /// </list>
        /// Defaults to true.
        /// </para>
        /// <para>
        /// If the value is changed from <c>false</c> to <c>true</c> at runtime, <see cref="StartTickTimer"/> must be called in order for SDK
        /// to observe the change.
        /// </para>
        /// </summary>
        protected virtual bool IsTicking => true;

        public struct EntityDebugOptions
        {
            /// <inheritdoc cref="EntityChecksumMode"/>
            public EntityChecksumMode Checksumming;

            /// <inheritdoc cref="EntityConsistencyChecks"/>
            public EntityConsistencyChecks ConsistencyChecks;

            /// <summary>
            /// If true, server computes a checksum of the initial entity model state and includes it in
            /// the session start message. The client verifies this checksum to detect model data format incompatibilities.
            /// </summary>
            public bool CheckInitialModelChecksum;

            public EntityDebugOptions(EntityChecksumMode checksumming, EntityConsistencyChecks consistencyChecks, bool checkInitialModelChecksum = false)
            {
                Checksumming = checksumming;
                ConsistencyChecks = consistencyChecks;
                CheckInitialModelChecksum = checkInitialModelChecksum;
            }
        }

        /// <summary>
        /// Sets the debugging mode of the entity
        /// <para>
        /// <b>Note: </b> This value is read once in actor startup and any changes during actor runtime have no effect.
        /// </para>
        /// </summary>
        protected virtual EntityDebugOptions DebugOptions => new EntityDebugOptions(default, EntityConsistencyChecks.None, checkInitialModelChecksum: RuntimeOptionsBase.IsLocalEnvironment || RuntimeOptionsBase.IsDevelopmentEnvironment);
        EntityDebugOptions _debugOptions;

        TimeSpan _tickIntervalWhenNoClientsConnected;
        TimeSpan _tickIntervalWithClientsConnected;

        bool _startedTickTimer;
        bool _hadClientsForTickTimer;
        CancellationTokenSource _tickTimerCts;
        bool _tickTimerTerminated;

        bool _isUpdatingTicks;
        bool _isRunningAction;
        bool _isInPostTick;

        /// <summary>
        /// null if empty
        /// </summary>
        Queue<TAction>? _pendingActionAfterJournalChange;

        struct PendingTimelineOp
        {
            /// <summary>
            /// null if tick.
            /// </summary>
            public readonly ModelAction? Action;

            /// <summary>
            /// Set depending on checksum mode and rate, 0 otherwise.
            /// </summary>
            public readonly uint Checksum;

            public PendingTimelineOp(ModelAction? action, uint checksum)
            {
                Action = action;
                Checksum = checksum;
            }
        }

        List<PendingTimelineOp> _timelinePendingActions = new List<PendingTimelineOp>();
        JournalPosition _timelineCurrentPosition;
        SegmentedIOBuffer _timelineChecksumBuffer = new SegmentedIOBuffer(segmentSize: 65536);
        SegmentedIOBuffer _timelineConsistencyCheckPreOpChecksumBuffer = new SegmentedIOBuffer(segmentSize: 65536);
        DateTime _timelineNextPeriodicChecksumAt;
        TimeSpan _timelinePeriodicChecksumPeriod;
        List<IJournalCheckerDataSourceListener> _timelineConsistencyCheckListeners = new List<IJournalCheckerDataSourceListener>();

        readonly struct DesyncTraceEntry
        {
            public readonly DateTime        CreatedAt;
            public readonly JournalPosition StartPosition;
            public readonly JournalPosition EndPosition;
            public readonly byte[]          ChecksumBuffer;

            public DesyncTraceEntry(DateTime createdAt, JournalPosition startPosition, JournalPosition endPosition, byte[] checksumBuffer)
            {
                CreatedAt = createdAt;
                StartPosition = startPosition;
                EndPosition = endPosition;
                ChecksumBuffer = checksumBuffer;
            }
        }
        Queue<DesyncTraceEntry> _desyncDebugTrace = new Queue<DesyncTraceEntry>();

        class JournalCheckerDataSource : IJournalCheckerDataSource
        {
            MultiplayerEntityActorBase<TModel, TAction> _owner;
            public JournalCheckerDataSource(MultiplayerEntityActorBase<TModel, TAction> owner)
            {
                _owner = owner;
            }

            JournalPosition IJournalCheckerDataSource.StagedPosition => _owner._timelineCurrentPosition;
            IModel IJournalCheckerDataSource.StagedModel => _owner.Model;

            uint IJournalCheckerDataSource.StagedChecksum => throw new NotImplementedException();
            IModel IJournalCheckerDataSource.CheckpointModel => throw new NotImplementedException();
            uint IJournalCheckerDataSource.CheckpointChecksum => throw new NotImplementedException();
        }

        void InitializeTimeline()
        {
            TickRateSetting tickRate = TickRate;
            _tickIntervalWhenNoClientsConnected = tickRate.WhenNoClientsConnected;
            _tickIntervalWithClientsConnected  = tickRate.WithClientsConnected;

            _debugOptions = DebugOptions;
            if (_debugOptions.Checksumming.Mode == EntityChecksumMode.ModeCode.Default)
                _debugOptions.Checksumming = EntityChecksumMode.Periodic(TimeSpan.FromSeconds(10));
            if (_debugOptions.Checksumming.Mode == EntityChecksumMode.ModeCode.PerOperation)
            {
                // In per-operation, we keep trace. Set pruning timer
                StartPeriodicTimer(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5), PruneDesyncSnapshotsCommand.Instance);
            }
        }

        void ResetTimelineTo(TModel model)
        {
            // Reset from the current position in time
            model.ResetTime(MetaTime.Now);

            _timelinePendingActions.Clear();
            _timelineCurrentPosition = JournalPosition.AfterTick(model.CurrentTick);

            Model = model;

            _desyncDebugTrace.Clear();
            _pendingActionAfterJournalChange = null;

            foreach (EntitySubscriber subscriber in _subscribers.Values)
            {
                if (!subscriber.TryGetUserData<SessionUserData>(out SessionUserData sessionData))
                    continue;

                sessionData.PingMarkersWaitingForTick.Clear();
                sessionData.PingMarkersWaitingForFlush.Clear();
                sessionData.FlushActionOverrides.Clear();
            }

            // Add consistency checkers.
            _timelineConsistencyCheckListeners.Clear();
            if (_debugOptions.ConsistencyChecks != EntityConsistencyChecks.None)
            {
                _timelineConsistencyCheckListeners.Add(new JournalModelOutsideModificationChecker<TModel>(_modelLogChannel));
                _timelineConsistencyCheckListeners.Add(new JournalModelActionImmutabilityChecker<TModel>(_modelLogChannel));
            }

            if (_debugOptions.ConsistencyChecks != EntityConsistencyChecks.None)
            {
                uint checksum = JournalUtil.ComputeChecksum(_timelineChecksumBuffer, Model);
                foreach (IJournalCheckerDataSourceListener listener in _timelineConsistencyCheckListeners)
                {
                    listener.OnAttach(new JournalCheckerDataSource(this));
                    listener.AfterSetup(checksum, _timelineChecksumBuffer);
                }

                _timelineChecksumBuffer.Clear();
            }
            if (_debugOptions.Checksumming.Mode == EntityChecksumMode.ModeCode.Periodic)
            {
                _timelineNextPeriodicChecksumAt = DateTime.UtcNow;
                _timelinePeriodicChecksumPeriod = _debugOptions.Checksumming.Period;
            }
        }

        #region Action & Tick Logic

        /// <summary>
        /// Starts tick timer if it wasn't started already. This must be called if <see cref="IsTicking"/> is set to true.
        /// </summary>
        protected void StartTickTimer()
        {
            if (!_startedTickTimer)
            {
                _startedTickTimer     = true;
                _tickTimerCts         = new CancellationTokenSource();
            }
            else
            {
                // If there is already a timer, and conditions haven't changed, ignore.
                if (!_tickTimerTerminated && _hadClientsForTickTimer == HasTimelineClientsConnected())
                    return;

                // Conditions have changed. Cancel previous timer and schedule a new.
                _tickTimerCts.Cancel();
                _tickTimerCts = new CancellationTokenSource();
                _tickTimerTerminated = false;
            }

            ReScheduleNextTickUpdate();
        }

        void ReScheduleNextTickUpdate()
        {
            bool hasClients = HasTimelineClientsConnected();
            TimeSpan interval = hasClients ? _tickIntervalWithClientsConnected : _tickIntervalWhenNoClientsConnected;
            DateTime nextUpdateAt;
            if (interval != TimeSpan.Zero)
                nextUpdateAt = DateTime.UtcNow + interval;
            else
                nextUpdateAt = ModelUtil.TimeAtTick(Model.CurrentTick + 1, Model.TimeAtFirstTick, Model.TicksPerSecond).ToDateTime() - MetaTime.DebugTimeOffset.ToTimeSpan();

            _hadClientsForTickTimer = hasClients;
            ScheduleExecuteOnActorContext(nextUpdateAt, TimelineTickTimerCallback, _tickTimerCts.Token);
        }

        void TimelineTickTimerCallback()
        {
            UpdateTicks();
            FlushActions();

            if (IsShutdownEnqueued)
                return;

            if (IsTicking)
                ReScheduleNextTickUpdate();
            else
                _tickTimerTerminated = true;
        }

        bool HasTimelineClientsConnected()
        {
            // \todo: Replace this with EnumerateClients().Any() and remove this copy-pasta pattern everywhere.
            foreach (EntitySubscriber subscriber in _subscribers.Values)
            {
                if (subscriber.Topic != EntityTopic.Participant)
                    continue;
                if (subscriber.EntityId.Kind != EntityKindCore.Session)
                    continue;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Runs any pending ticks, i.e ticks accured from from last tick flush point to the current point in time,
        /// and then flush pending timeline updates to all clients.
        /// </summary>
        protected void UpdateTicksAndFlush()
        {
            UpdateTicks();
            FlushActions();
        }

        [CommandHandler]
        void HandleFlushActionsCommand(FlushActionsCommand _)
        {
            FlushActions();
        }

        void EnqueueFlush()
        {
            _self.Tell(FlushActionsCommand.Instance, null);
        }

        /// <summary>
        /// Executes given action and informs clients.
        /// </summary>
        /// <param name="action">Action to execute.</param>
        /// <param name="runPendingTicksFirst">True, if any pending Ticks should be enqueued first. True by default.</param>
        protected void ExecuteAction(TAction action, bool runPendingTicksFirst = true)
        {
            RequireCurrentThreadOnActorContext();
            ThrowIfCannotExecuteActionNow(action);

            // If we are in PostTick, we are already flushing ticks. Don't continue recursively.
            if (runPendingTicksFirst && !_isInPostTick)
                UpdateTicks();

            MetaActionResult runResult = StageActionOnJournal(action);
            if (!runResult.IsSuccess)
                _log.Warning("Failed to execute action. Action: {Action}. Result: {Result}", PrettyPrint.Compact(action), runResult);

            EnqueueFlush();
        }

        /// <summary>
        /// Executes the given action and informs clients. If there is an ongoing action or tick, i.e. this method
        /// is being called from a Server Listener, the action is run after it. Otherwise, the action is run immediately.
        /// </summary>
        protected void ExecuteActionAfterPendingActions(TAction action)
        {
            RequireCurrentThreadOnActorContext();

            if (_isUpdatingTicks || _isRunningAction)
            {
                if (_pendingActionAfterJournalChange == null)
                    _pendingActionAfterJournalChange = new();
                _pendingActionAfterJournalChange.Enqueue(action);
            }
            else
                ExecuteAction(action, runPendingTicksFirst: true);
        }

        /// <summary>
        /// Executes an action that differs per member. The follower of each member named in <paramref name="actionsByMember"/> executes
        /// that member's action; the server and every other follower execute <paramref name="defaultAction"/>. All of them see the operation
        /// at the same timeline position, under the same checksums, in the same flush.
        /// <para>
        /// When <paramref name="defaultAction"/> is <c>null</c>, the default, the server and the unnamed followers execute nothing for this
        /// operation, so it changes state only on the named members' followers. This is how a member's private state is modified: with an
        /// action that only that member's follower executes. For a single member, see <see cref="ExecuteActionPerMember(EntityId, TAction, bool)"/>.
        /// </para>
        /// <para>
        /// The per-member actions are never executed on the server. <see cref="OnBeforeAction"/> and <see cref="OnAfterAction"/> observe
        /// what the server executes. Each per-member action must leave the checksummed model exactly as the server's execution does; the
        /// recipient's checksum verification catches a violation. In practice a per-member action modifies only state that is excluded from
        /// checksums, such as members marked <see cref="MetaMemberFlags.ServerOnly"/>.
        /// </para>
        /// <para>
        /// A member is whatever the model has as a participant, a player or another kind of entity, and the action reaches the follower
        /// of that member, identified by <see cref="ClientPeerState.MemberEntityId"/>. An action for a member with no connected follower is dropped.
        /// </para>
        /// </summary>
        /// <param name="actionsByMember">The action each named member's follower executes, keyed by member id.</param>
        /// <param name="defaultAction">Action the server and every unnamed follower execute. <c>null</c>, the default, to execute nothing there.</param>
        /// <param name="runPendingTicksFirst">True, if any pending Ticks should be enqueued first. True by default.</param>
        protected void ExecuteActionPerMember(IReadOnlyDictionary<EntityId, TAction> actionsByMember, TAction? defaultAction = null, bool runPendingTicksFirst = true)
        {
            if (actionsByMember == null)
                throw new ArgumentNullException(nameof(actionsByMember));
            foreach ((EntityId memberId, TAction action) in actionsByMember)
            {
                if (action == null)
                    throw new ArgumentException($"Action for member {memberId} is null.", nameof(actionsByMember));
            }

            ModelAction executedAction = defaultAction != null ? defaultAction : new NoopAction();
            ExecuteActionPerMemberCore(executedAction, actionsByMember, runPendingTicksFirst);
        }

        /// <summary>
        /// Executes <paramref name="action"/> on the follower of <paramref name="member"/> only. The server and every other follower execute
        /// nothing for this operation, but all of them see it at the same timeline position, under the same checksums, in the same flush.
        /// Use this to modify a member's private state.
        /// <para>
        /// Equivalent to <see cref="ExecuteActionPerMember(IReadOnlyDictionary{EntityId, TAction}, TAction, bool)"/> with a single member and
        /// no default action; see there for the requirements on <paramref name="action"/>.
        /// </para>
        /// </summary>
        /// <param name="member">Member whose follower executes the action, see <see cref="ClientPeerState.MemberEntityId"/>.</param>
        /// <param name="action">Action executed only by the follower of <paramref name="member"/>.</param>
        /// <param name="runPendingTicksFirst">True, if any pending Ticks should be enqueued first. True by default.</param>
        protected void ExecuteActionPerMember(EntityId member, TAction action, bool runPendingTicksFirst = true)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));

            ExecuteActionPerMemberCore(new NoopAction(), new Dictionary<EntityId, TAction>(capacity: 1) { [member] = action }, runPendingTicksFirst);
        }

        void ExecuteActionPerMemberCore(ModelAction executedAction, IReadOnlyDictionary<EntityId, TAction> actionsByMember, bool runPendingTicksFirst)
        {
            RequireCurrentThreadOnActorContext();
            ThrowIfCannotExecuteActionNow(executedAction);

            // If we are in PostTick, we are already flushing ticks. Don't continue recursively.
            if (runPendingTicksFirst && !_isInPostTick)
                UpdateTicks();

            // Execute the shared operation. The per-member actions replace it for their recipients only, by its index in
            // the pending operation list, the same way a failed client action is reported back to its author alone
            // (see HandleEntityEnqueueActionsRequest). Reactive actions run inside StageActionOnJournal land after
            // this index, and nothing in there can flush, so the index stays valid.
            int stagedOpIndex = _timelinePendingActions.Count;
            MetaActionResult runResult = StageActionOnJournal(executedAction);
            if (!runResult.IsSuccess)
                _log.Warning("Failed to execute action. Action: {Action}. Result: {Result}", PrettyPrint.Compact(executedAction), runResult);

            HashSet<EntityId>? membersWithSession = _log.IsDebugEnabled ? new HashSet<EntityId>() : null;
            foreach (EntitySubscriber subscriber in _subscribers.Values)
            {
                if (!subscriber.TryGetUserData<SessionUserData>(out SessionUserData sessionData))
                    continue;

                EntityId memberId = sessionData.PeerState.MemberEntityId;
                if (!actionsByMember.TryGetValue(memberId, out TAction? memberAction))
                    continue;

                sessionData.FlushActionOverrides.Set(stagedOpIndex, memberAction);
                membersWithSession?.Add(memberId);
            }

            if (membersWithSession != null && membersWithSession.Count < actionsByMember.Count)
            {
                foreach ((EntityId memberId, TAction memberAction) in actionsByMember)
                {
                    if (!membersWithSession.Contains(memberId))
                        _log.Debug("Dropped {MemberActionType} for member {MemberId} at {Action}: no connected follower.", memberAction.GetType().ToGenericTypeString(), memberId, PrettyPrint.Compact(executedAction));
                }
            }

            EnqueueFlush();
        }

        /// <summary>
        /// Catches recursive calls. Actions in Tick are illegal except in the specific PostTick phase.
        /// </summary>
        void ThrowIfCannotExecuteActionNow(ModelAction action)
        {
            if (_isUpdatingTicks && !_isInPostTick)
                throw new InvalidOperationException($"Cannot ExecuteAction({action.GetType().ToGenericTypeString()}) while running model Tick. Use ExecuteActionAfterPendingActions instead.");
            if (_isRunningAction)
                throw new InvalidOperationException($"Cannot ExecuteAction({action.GetType().ToGenericTypeString()}) while running another Action. Use ExecuteActionAfterPendingActions instead.");
        }

        MetaActionResult StageActionOnJournal(ModelAction action)
        {
            if (_log.IsVerboseEnabled)
                _log.Verbose("Execute action (tick {Tick}): {Action}", Model.CurrentTick, PrettyPrint.Compact(action));

            OnPreActionCore(action);
            JournalPosition positionBefore = _timelineCurrentPosition;

            // Consistency checks
            uint consistencyChecksumBefore = 0;
            if (_debugOptions.ConsistencyChecks != EntityConsistencyChecks.None)
            {
                foreach (IJournalCheckerDataSourceListener listener in _timelineConsistencyCheckListeners)
                    listener.BeforeAction(action);

                // Mutation check for failing actions.
                consistencyChecksumBefore = JournalUtil.ComputeChecksum(_timelineConsistencyCheckPreOpChecksumBuffer, Model);
            }

            MetaActionResult result;
            try
            {
                result = ModelUtil.RunAction(Model, action);
            }
            catch (Exception ex)
            {
                // \note: This should be fatal, state is not cleared.
                throw UnhandledModelExecutionException.ForAction(ex, action);
            }

            JournalPosition positionAfter = JournalPosition.AfterAction(positionBefore);
            _timelineCurrentPosition = positionAfter;

            // Checksum, depending on checksum mode.
            bool computeChecksum = (_debugOptions.Checksumming.Mode == EntityChecksumMode.ModeCode.PerOperation)
                                || (_debugOptions.ConsistencyChecks != EntityConsistencyChecks.None);

            uint checksum = computeChecksum ? JournalUtil.ComputeChecksum(_timelineChecksumBuffer, Model) : 0;
            _timelinePendingActions.Add(new PendingTimelineOp(action, checksum));

            // Consistency checks
            if (_debugOptions.ConsistencyChecks != EntityConsistencyChecks.None)
            {
                foreach (IJournalCheckerDataSourceListener listener in _timelineConsistencyCheckListeners)
                    listener.AfterAction(action, result, checksum, _timelineChecksumBuffer);

                // Mutation check for failing actions
                if (!result.IsSuccess && checksum != consistencyChecksumBefore)
                {
                    SerializedObjectComparer comparer = new SerializedObjectComparer();
                    comparer.FirstName = "Before";
                    comparer.SecondName = "After";
                    comparer.Type = typeof(IModel);
                    string report = comparer.Compare(_timelineConsistencyCheckPreOpChecksumBuffer, _timelineChecksumBuffer).Description;

                    _log.Error("Illegal state modification in a failing action. Action {Action} failed with {Result}. Failing action may not change Model. Got:\n {DiffReport}", action.GetType().Name, result.Name, report);
                }
            }

            // In per-op debugging mode, keep a trace of model snapshots
            if (_debugOptions.Checksumming.Mode == EntityChecksumMode.ModeCode.PerOperation)
                _desyncDebugTrace.Enqueue(new DesyncTraceEntry(DateTime.UtcNow, positionBefore, positionAfter, _timelineChecksumBuffer.ToArray()));

            // Release memory
            _timelineChecksumBuffer.Clear();
            _timelineConsistencyCheckPreOpChecksumBuffer.Clear();

            OnPostActionCore(action, result);
            return result;
        }

        void UpdateTicks()
        {
            // Catch recursive tick update (in case ExecuteAction was called during tick update)
            if (_isUpdatingTicks)
                throw new InvalidOperationException("UpdateTicks called recursively, likely through ExecuteAction(action, true).");
            if (_isRunningAction)
                throw new InvalidOperationException("UpdateTicks called while processing another Action, likely through ExecuteAction(action, true).");

            if (!IsTicking)
                return;

            // Ticking was turned on during runtime (i.e. not during actor initialization).
            if (!_startedTickTimer)
                StartTickTimer();

            long lastTotalTicks    = Model.CurrentTick;
            long currentTotalTicks = ModelUtil.TotalNumTicksElapsedAt(MetaTime.Now, Model.TimeAtFirstTick, Model.TicksPerSecond);
            long newTicks          = currentTotalTicks - lastTotalTicks;

            if (newTicks == 0)
                return;

            // Sanity check. If we are over a minute late with the update, the actor is already in
            // such bad a state we shouldn't let it continue. Kill actor.
            //
            // Except if the debugger is present. Debugging is likely to have caused this in the first
            // place, so let's try to tolerate more.
            if (newTicks > 60 * Model.TicksPerSecond)
            {
                if (Debugger.IsAttached)
                    _log.Warning("Too many pending ticks on timeline: {Ticks}. Tolerating since debugger is present (would kill the actor otherwise).", newTicks);
                else
                    throw new InvalidOperationException(Invariant($"Too many pending ticks on timeline: {newTicks}."));
            }

            _log.Verbose("Simulating {NumTicks} ticks (from {FromTick} to {ToTick})", newTicks, lastTotalTicks, currentTotalTicks);

            _isUpdatingTicks = true;

            // Execute ticks
            for (long tick = 0; tick < newTicks; tick++)
            {
                JournalPosition positionBefore = JournalPosition.NextTick(_timelineCurrentPosition);

                OnPreTickCore();

                // Consistency checks
                long executingTick = _timelineCurrentPosition.Tick + 1;
                if (_debugOptions.ConsistencyChecks != EntityConsistencyChecks.None)
                {
                    foreach (IJournalCheckerDataSourceListener listener in _timelineConsistencyCheckListeners)
                        listener.BeforeTick(executingTick);
                }

                Model.Tick(checksumCtx: null);

                JournalPosition positionAfter = JournalPosition.AfterTick(positionBefore);
                _timelineCurrentPosition = positionAfter;

                // Checksum, depending on checksum mode.
                bool computeChecksum = (_debugOptions.Checksumming.Mode == EntityChecksumMode.ModeCode.PerOperation)
                                    || (_debugOptions.ConsistencyChecks != EntityConsistencyChecks.None);

                uint checksum  = computeChecksum ? JournalUtil.ComputeChecksum(_timelineChecksumBuffer, Model) : 0;
                _timelinePendingActions.Add(new PendingTimelineOp(null, checksum));

                // Consistency checks
                if (_debugOptions.ConsistencyChecks != EntityConsistencyChecks.None)
                {
                    foreach (IJournalCheckerDataSourceListener listener in _timelineConsistencyCheckListeners)
                        listener.AfterTick(executingTick, MetaActionResult.Success, checksum, _timelineChecksumBuffer);
                }

                // In per-op debugging mode, keep a trace of model snapshots
                if (_debugOptions.Checksumming.Mode == EntityChecksumMode.ModeCode.PerOperation)
                    _desyncDebugTrace.Enqueue(new DesyncTraceEntry(DateTime.UtcNow, positionBefore, positionAfter, _timelineChecksumBuffer.ToArray()));

                // Release memory
                _timelineChecksumBuffer.Clear();

                OnPostTickCore(positionBefore.Tick);
            }

            _isUpdatingTicks = false;

            // Flush any actions after _isUpdatingTicks is over
            OnPostTicksOrActionCore();

            // Promote all post-tick pending markers to wait for the flush
            foreach (EntitySubscriber subscriber in _subscribers.Values)
            {
                if (!subscriber.TryGetUserData<SessionUserData>(out SessionUserData sessionData))
                    continue;

                sessionData.PingMarkersWaitingForFlush.AddRange(sessionData.PingMarkersWaitingForTick);
                sessionData.PingMarkersWaitingForTick.Clear();
            }
        }

        void OnPreTickCore()
        {
            OnBeforeTick();
        }

        void OnPostTickCore(long tick)
        {
            _isInPostTick = true;

#pragma warning disable CS0618 // Type or member is obsolete
            OnPostTick(tick);
#pragma warning restore CS0618 // Type or member is obsolete
            OnAfterTick();

            _isInPostTick = false;
        }

        void OnPreActionCore(ModelAction action)
        {
            _isRunningAction = true;
            OnBeforeAction(action);
        }

        void OnPostActionCore(ModelAction action, MetaActionResult result)
        {
            OnAfterAction(action, result);
            _isRunningAction = false;
            OnPostTicksOrActionCore();
        }

        void OnPostTicksOrActionCore()
        {
            // Running an action will trigger this method again.
            // In case the action adds new actions, we want to run those first. Hence we
            // steal the list to get the expected DFS order. It also keeps the call stack
            // similar to the action->reactive-action stack.
            Queue<TAction>? pendingActions = _pendingActionAfterJournalChange;
            _pendingActionAfterJournalChange = null;
            if (pendingActions == null)
                return;

            for (;;)
            {
                if (!pendingActions.TryDequeue(out TAction? action))
                    return;

                // No need to run ticks. Those were already run (or not) when the initial action
                // was enqueued.
                ExecuteAction(action, runPendingTicksFirst: false);
            }
        }

        void FlushActions()
        {
            EntityTimelineUpdateMessage? update = TryGatherFlushActions();
            if (update != null)
            {
                // Send to every client. Override for overridden users
                foreach (EntitySubscriber subscriber in _subscribers.Values)
                {
                    if (!subscriber.TryGetUserData<SessionUserData>(out SessionUserData sessionData))
                        continue;

                    // A session with per-member operations pending receives them in place of the shared ones; the checksums stay.
                    SendToClient(subscriber, sessionData.FlushActionOverrides.IsEmpty ? update : sessionData.FlushActionOverrides.Consume(update));
                }

                // Post flush markers
                foreach (EntitySubscriber subscriber in _subscribers.Values)
                {
                    if (!subscriber.TryGetUserData<SessionUserData>(out SessionUserData sessionData))
                        continue;

                    foreach (uint marker in sessionData.PingMarkersWaitingForFlush)
                        SendToClient(subscriber, new EntityTimelinePingTraceMarker(marker, EntityTimelinePingTraceMarker.TracePosition.AfterNextTick));
                    sessionData.PingMarkersWaitingForFlush.Clear();
                }
            }
        }

        EntityTimelineUpdateMessage? TryGatherFlushActions()
        {
            if (_timelinePendingActions.Count == 0)
                return null;

            if (_timelinePendingActions.Count > 1000)
            {
                // Sanity check. Too much work on queue. This suggest there is something wrong,
                // and pushing the most likely bogus timeline to clients isn't good way to resolve
                // it. Kill actor.
                throw new InvalidOperationException(Invariant($"Too many pending timeline operations to flush: {_timelinePendingActions.Count}."));
            }

            List<ModelAction?> operations = new List<ModelAction?>();

            // In per-operation mode, we have per operation checksums
            List<uint>? opChecksums = null;
            if (_debugOptions.Checksumming.Mode == EntityChecksumMode.ModeCode.PerOperation)
                opChecksums = new List<uint>();

            foreach (PendingTimelineOp op in _timelinePendingActions)
            {
                operations.Add(op.Action);
                opChecksums?.Add(op.Checksum);
            }
            _timelinePendingActions.Clear();

            uint finalChecksum;
            if (_debugOptions.Checksumming.Mode == EntityChecksumMode.ModeCode.None)
            {
                finalChecksum = 0;
            }
            else if (_debugOptions.Checksumming.Mode == EntityChecksumMode.ModeCode.Periodic)
            {
                // In periodic mode we compute the checksum here, every now and then
                if (DateTime.UtcNow >= _timelineNextPeriodicChecksumAt)
                {
                    finalChecksum = JournalUtil.ComputeChecksum(_timelineChecksumBuffer, Model);
                    _timelineNextPeriodicChecksumAt = DateTime.UtcNow + _timelinePeriodicChecksumPeriod;
                }
                else
                {
                    finalChecksum = 0;
                }
            }
            else if (_debugOptions.Checksumming.Mode == EntityChecksumMode.ModeCode.PerOperation)
            {
                // In per-operation debugging mode, we already have checksums
                finalChecksum = opChecksums![opChecksums.Count - 1];
            }
            else
                throw new InvalidOperationException("invalid debug mode");

            // Release memory
            _timelineChecksumBuffer.Clear();

            uint[]? debugPerOperationChecksums = opChecksums == null ? null : opChecksums.ToArray();
            return new EntityTimelineUpdateMessage(operations, finalChecksum, debugPerOperationChecksums);
        }

        [PubSubMessageHandler]
        void HandleEntityEnqueueActionsRequest(EntitySubscriber session, EntityEnqueueActionsRequest request)
        {
            if (!session.TryGetUserData<SessionUserData>(out SessionUserData userData))
            {
                _log.Warning("Got EntityEnqueueActionsRequest from non-session entity {Source}. Ignoring.", session.EntityId);
                return;
            }

            ClientPeerState client = userData.PeerState;
            EntityId playerId = client.PlayerId;

            UpdateTicks();

            foreach (ModelAction untypedAction in request.Actions)
            {
                // not allowed, but don't trust input
                if (untypedAction == null)
                {
                    _log.Warning("Got EntityEnqueueActionsRequest from {PlayerId} with null action. Ignoring.", playerId);
                    continue;
                }

                TAction? typedAction = untypedAction as TAction;
                if (typedAction == null)
                {
                    _log.Warning("Got EntityEnqueueActionsRequest from {PlayerId} with unsupported action type {ActionType} (expected {ExpectedType}). Ignoring.", playerId, untypedAction.GetType().ToGenericTypeString(), typeof(TAction).ToGenericTypeString());
                    continue;
                }

                // Validation
                if (!ValidateClientOriginatingActionCore(client, typedAction))
                {
                    _log.Warning("Validation failed for action by {PlayerId}, ignored. Action: {Action}", playerId, PrettyPrint.Compact(typedAction));

                    // skip
                    continue;
                }

                // Run action.
                int stagedOpIndex = _timelinePendingActions.Count;
                MetaActionResult runResult = StageActionOnJournal(typedAction);
                bool isHiddenAction = false;

                // If action was a failure, we mark it to be reported back only to the caller
                if (!runResult.IsSuccess)
                {
                    // only on debug level. This is expected due to races.
                    if (_log.IsDebugEnabled)
                        _log.Debug("Failed to execute action by {PlayerId}, ignored. Action: {Action}. Result: {Result}", playerId, PrettyPrint.Compact(typedAction), runResult);

                    isHiddenAction = true;
                }

                // Hidden actions are only reported back to the caller
                if (isHiddenAction)
                {
                    // Switch the executed action to a no-op action
                    _timelinePendingActions[stagedOpIndex] = new PendingTimelineOp(new NoopAction(), checksum: _timelinePendingActions[stagedOpIndex].Checksum);

                    // Add flush rule to send result back to the initiator
                    userData.FlushActionOverrides.Set(stagedOpIndex, typedAction);
                }
            }
            EnqueueFlush();
        }

        bool ValidateClientOriginatingActionCore(ClientPeerState client, TAction action)
        {
            ModelActionSpec actionSpec = ModelActionRepository.Instance.SpecFromType[action.GetType()];

            // Must be client-issuable and enqueable.
            if (!actionSpec.ExecuteFlags.HasFlag(ModelActionExecuteFlags.FollowerSynchronized))
            {
                _log.Warning("Client tried to enqueue non-client Action {Action} (it does not have FollowerSynchronized flag, ExecuteFlags={ExecuteFlags}). "
                    + "The action is probably derived from a server-only action base class (e.g. {MyEntityServerAction}). "
                    + "To allow the client to enqueue it, derive it from the client action base class instead (e.g. {MyEntityClientAction}). "
                    + "You should have something like: [MetaSerializable] public abstract class MyEntityAction : ModelAction<MyEntityModel> {} "
                    + "and [ModelActionExecuteFlags(ModelActionExecuteFlags.FollowerSynchronized)] public abstract class MyEntityClientAction : MyEntityAction {}",
                    action.GetType().ToGenericTypeString(), actionSpec.ExecuteFlags);
                return false;
            }

            bool isDevelopmentOnlyAction = actionSpec.IsDevelopmentOnlyAction;
            if (isDevelopmentOnlyAction)
            {
                _log.Info("Executing development-only action: {Action}", action.GetType().ToGenericTypeString());

                EnvironmentOptions envOpts = RuntimeOptionsRegistry.Instance.GetCurrent<EnvironmentOptions>();
                if (!envOpts.EnableDevelopmentFeatures && !GlobalStateProxyActor.ActiveDevelopers.Get().IsPlayerDeveloper(client.PlayerId))
                {
                    _log.Warning("Client tried to run development-only action {Action}, but Development-Only actions are not enabled.", action.GetType().ToGenericTypeString());
                    return false;
                }
            }

            return ValidateClientOriginatingAction(client, action);
        }

        #endregion

        #region Desync Debugging

        [CommandHandler]
        void HandlePruneDesyncSnapshotsCommand(PruneDesyncSnapshotsCommand _)
        {
            DateTime pruneOlderThan = DateTime.UtcNow - TimeSpan.FromSeconds(5);
            for (;;)
            {
                if (!_desyncDebugTrace.TryPeek(out DesyncTraceEntry entry))
                    break;
                if (entry.CreatedAt >= pruneOlderThan)
                    break;
                _desyncDebugTrace.Dequeue();
            }
        }

        [MessageHandler]
        void HandleEntityChecksumMismatchDetails(EntityId sessionId, EntityChecksumMismatchDetails details)
        {
            EntityId playerId = SessionIdUtil.ToPlayerId(sessionId);
            if (_debugOptions.Checksumming.Mode == EntityChecksumMode.ModeCode.None)
            {
                _log.Debug("Client {PlayerId} reported checksum mismatch, but mismatch debugging is disabled. Ignored.", playerId);
                return;
            }

            JournalPosition detailsPosition = JournalPosition.FromTickOperationStep(details.Tick, details.Operation, 0);
            foreach (DesyncTraceEntry entry in _desyncDebugTrace)
            {
                if (entry.StartPosition > detailsPosition)
                    break;
                if (entry.EndPosition <= detailsPosition)
                    continue;

                _log.Warning("Client {PlayerId} reported checksum mismatch", playerId);
                if (entry.StartPosition != detailsPosition || entry.EndPosition.Operation != entry.StartPosition.Operation + 1)
                    _log.Warning("Warning. Match in debug trace is not exact and contains additional changes");

                SerializedObjectComparer comparer = new SerializedObjectComparer();
                comparer.FirstName = "Expected";
                comparer.SecondName = $"Client (desynced, {playerId})";
                comparer.Type = typeof(IModel);
                string result = comparer.Compare(entry.ChecksumBuffer, details.ChecksumBuffer).Description;

                _log.Warning("{0}", result);
                return;
            }
            _log.Debug("Client {PlayerId} reported checksum mismatch at position {Position} but could not find the position in debug trace. Trace could have been already pruned.", playerId, detailsPosition);
        }

        #endregion

        #region Ping Measurement

        [PubSubMessageHandler]
        void HandleEntityTimelinePingTraceQuery(EntitySubscriber session, EntityTimelinePingTraceQuery request)
        {
            if (!session.TryGetUserData<SessionUserData>(out SessionUserData sessionData))
            {
                _log.Warning("Received EntityTimelinePingTraceQuery from a non-session peer: {Source}", session.EntityId);
                return;
            }

            // Reply immediately
            SendToClient(session, new EntityTimelinePingTraceMarker(request.Id, EntityTimelinePingTraceMarker.TracePosition.MessageReceivedOnEntity));

            // Enqueue reply after next tick
            sessionData.PingMarkersWaitingForTick.Add(request.Id);
        }

        #endregion

        #region Callbacks to userland

        [Obsolete("Use OnAfterTick instead")]
        protected virtual void OnPostTick(long tick) { }

        /// <summary>
        /// Called before each model tick.
        /// <para>
        /// You should not modify Model state directly here but instead enqueue
        /// an operation or action to perform the mutations. See <see cref="ExecuteActionAfterPendingActions"/> and <see cref="EntityActor.EnqueueOnActorContext"/>.
        /// </para>
        /// </summary>
        protected virtual void OnBeforeTick() { }

        /// <summary>
        /// Called after each model tick.
        /// <para>
        /// You should not modify Model state directly here but instead enqueue
        /// an operation or action to perform the mutations. See <see cref="ExecuteActionAfterPendingActions"/> and <see cref="EntityActor.EnqueueOnActorContext"/>.
        /// </para>
        /// </summary>
        protected virtual void OnAfterTick() { }

        /// <summary>
        /// Called before the action is about to be executed.
        /// <para>
        /// You should not modify Model state or Action directly here but instead enqueue
        /// an operation or action to perform the mutations. See <see cref="ExecuteActionAfterPendingActions"/> and <see cref="EntityActor.EnqueueOnActorContext"/>.
        /// </para>
        /// </summary>
        protected virtual void OnBeforeAction(ModelAction action) { }

        /// <summary>
        /// Called after the action has been executed.
        /// <para>
        /// You should not modify Model state or Action directly here but instead enqueue
        /// an operation or action to perform the mutations. See <see cref="ExecuteActionAfterPendingActions"/> and <see cref="EntityActor.EnqueueOnActorContext"/>.
        /// </para>
        /// </summary>
        protected virtual void OnAfterAction(ModelAction action, MetaActionResult result) { }

        /// <summary>
        /// Validates the client submitted actions is legal for execution. If an action is not legal, the implementation should log a warning message and return false. Actions that fail the validation
        /// are not executed.
        ///
        /// <para>
        /// SDK checks for <see cref="ModelActionExecuteFlags"/> and <see cref="DevelopmentOnlyActionAttribute"/> requirements automatically.
        /// </para>
        /// </summary>
        protected virtual bool ValidateClientOriginatingAction(ClientPeerState client, TAction action) => true;

        #endregion
    }
}
