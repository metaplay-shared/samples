using Metaplay.Core;
using Metaplay.Core.Analytics;
using Metaplay.Core.LiveOpsEvent;
using Metaplay.Core.Player;
using System.Collections.Generic;

namespace Game.Logic
{
    /// <summary>
    /// One game this player finished, as passed to <see cref="IMatchCompletionObserver"/>. It holds only what the
    /// observers' objectives need. It is created only after <see cref="PlayerModel.TryRecordMatch"/> has added the
    /// game to the player's record, so an observer never sees a game that was abandoned, refused or already recorded.
    /// </summary>
    public readonly struct MatchCompletion
    {
        /// <summary>
        /// The table's entity id.
        /// <para>
        /// This is the only value an observer may deduplicate on. A completion is normally delivered once. It is
        /// delivered again only when a table re-sends its result after the game has dropped out of the player's
        /// bounded match history, and then only this id identifies the repeat (<c>docs/player.md</c>,
        /// "Observer rules").
        /// </para>
        /// </summary>
        public EntityId MatchId { get; }

        /// <summary>
        /// When the table finished, as recorded by the table.
        /// <para>
        /// This is the only time an observer may use, in UTC here or in the player's local time as
        /// <see cref="MatchCompletionContext.CompletedAtLocal"/>. The completion runs in an unsynchronized server
        /// action, which the client and the server execute at different ticks, so the model's
        /// <c>CurrentTime</c> differs between them. A decision based on this value is the same on both.
        /// </para>
        /// </summary>
        public MetaTime CompletedAt { get; }

        /// <summary>
        /// The player's zero-based position in the final standings, 0 for the winner. It is the table's ranking
        /// after tie-breaks, not one derived from trick counts (<c>docs/game-rules.md</c>, "End of the game and
        /// standings").
        /// </summary>
        public int Position { get; }

        /// <summary>The number of tricks this player took.</summary>
        public int TricksWon { get; }

        /// <summary>Whether the player finished first (<see cref="Position"/> is 0).</summary>
        public bool IsWin => Position == 0;

        internal MatchCompletion(EntityId matchId, MetaTime completedAt, int position, int tricksWon)
        {
            MatchId     = matchId;
            CompletedAt = completedAt;
            Position    = position;
            TricksWon   = tricksWon;
        }

        public override string ToString() => $"{MatchId} at {CompletedAt}: #{Position + 1}, {TricksWon} tricks";
    }

    /// <summary>
    /// A copy of one LiveOps event this player holds, for match-completion observers. Observers do not get the SDK's
    /// <see cref="PlayerLiveOpsEventInfo"/>, because its get-only properties lead to mutable checksummed state, such
    /// as the schedule's phase table, and a write to it from the unsynchronized action would cause a checksum
    /// mismatch. <see cref="Content"/> is still a shared reference, because copying it would need a serialization
    /// round trip.
    /// </summary>
    public readonly struct LiveOpsEventSnapshot
    {
        /// <summary>The event's id. Observers key their progress for the event by it.</summary>
        public MetaGuid Id { get; }

        /// <summary>The event's phase for this player, as the SDK last set it.</summary>
        public LiveOpsEventPhase Phase { get; }

        /// <summary>
        /// The content copied into the event at creation. Never write to it: it is part of the player's
        /// checksummed state (<see cref="IMatchCompletionObserver"/>, rule 1).
        /// <para>
        /// This type cannot prevent writes through a content subclass. Content types must therefore keep their
        /// setters private, as <see cref="WeeklyEventContent"/> does.
        /// </para>
        /// </summary>
        public LiveOpsEventContent Content { get; }

        /// <summary>Whether this event has a schedule. An operator can create an event without one.</summary>
        public bool HasSchedule { get; }

        /// <summary>The start and end of the event's enabled window, and when it concludes. <see cref="MetaTime.Epoch"/> when <see cref="HasSchedule"/> is false.</summary>
        public MetaTime EnabledStartsAt { get; }
        public MetaTime EnabledEndsAt   { get; }
        public MetaTime ConcludesAt     { get; }

        internal LiveOpsEventSnapshot(PlayerLiveOpsEventModel model)
        {
            LiveOpsEventScheduleInfo schedule = model.ScheduleMaybe;

            Id              = model.Id;
            Phase           = model.Phase;
            Content         = model.Content;
            HasSchedule     = schedule != null;
            EnabledStartsAt = schedule?.GetEnabledStartTime() ?? MetaTime.Epoch;
            EnabledEndsAt   = schedule?.GetEnabledEndTime() ?? MetaTime.Epoch;
            ConcludesAt     = schedule?.GetConcludedTime() ?? MetaTime.Epoch;
        }

        /// <summary>
        /// Whether <paramref name="at"/> is at or after <see cref="EnabledStartsAt"/> and before
        /// <see cref="EnabledEndsAt"/>. Always false for an event with no schedule. Observers use this method so
        /// they all treat the window boundaries the same way.
        /// </summary>
        public bool WindowContains(MetaTime at) => HasSchedule && at >= EnabledStartsAt && at < EnabledEndsAt;

        public override string ToString() => $"{Id} in {Phase}";
    }

    /// <summary>
    /// What a match-completion observer receives. It has no wallet, because the player's claim action grants rewards
    /// (<see cref="IMatchCompletionObserver"/>, rule 3). It has no player model, so an observer can write only its own
    /// state. It has no clock other than <see cref="MatchCompletion.CompletedAt"/> (rule 2).
    /// <c>MatchCompletionTests</c> asserts the exact member list.
    /// </summary>
    public readonly struct MatchCompletionContext
    {
        readonly PlayerModel _player;

        /// <summary>The finished game.</summary>
        public MatchCompletion Completion { get; }

        internal MatchCompletionContext(PlayerModel player, MatchCompletion completion)
        {
            _player    = player;
            Completion = completion;
        }

        /// <summary>The player's entity id, for correlation keys and log messages.</summary>
        public EntityId PlayerId => _player.PlayerId;

        /// <summary>The player's active game config, for reading the observer's own config libraries.</summary>
        public SharedGameConfig GameConfig => _player.GameConfig;

        /// <summary>
        /// Copies of the LiveOps events this player still holds (<see cref="LiveOpsEventSnapshot"/> says why they are
        /// copies). Only the SDK's per-player schedule tells whether the completion time falls inside an event's
        /// enabled window. An observer that writes progress only for events in this list cannot recreate progress
        /// for an event the SDK has removed.
        /// </summary>
        public IEnumerable<LiveOpsEventSnapshot> LiveOpsEvents
        {
            get
            {
                List<LiveOpsEventSnapshot> events = new List<LiveOpsEventSnapshot>();
                foreach ((MetaGuid _, PlayerLiveOpsEventModel model) in _player.LiveOpsEvents.EventModels)
                {
                    if (model != null)
                        events.Add(new LiveOpsEventSnapshot(model));
                }
                return events;
            }
        }

        /// <summary>
        /// <see cref="MatchCompletion.CompletedAt"/> in the player's local time, for deciding which player-local day
        /// or week the game belongs to. The SDK sets the UTC offset at login, outside the timeline, and it does not
        /// change during a session, so the client and the server read the same offset.
        /// </summary>
        public PlayerLocalTime CompletedAtLocal =>
            new PlayerLocalTime(Completion.CompletedAt, _player.TimeZoneInfo.CurrentUtcOffset);

        /// <summary>
        /// Emits one of the observer's analytics events on this player's event stream, for example when a
        /// mission is completed.
        /// <para>
        /// Emit on a meaningful state change, not on every increment. The match completion itself emits no event,
        /// because the game already emits <c>match_finished</c> for the same game.
        /// </para>
        /// </summary>
        public void Emit(PlayerEventBase payload) => _player.EventStream.Event(payload);

        /// <summary>
        /// A correlation id for the analytics events caused by this finished game. It is derived from the player
        /// id and <see cref="MatchCompletion.CompletedAt"/>, so the client and the server compute the same id
        /// (<c>docs/analytics.md</c>, "Correlation id").
        /// </summary>
        public AnalyticsCorrelationId CorrelationIdFor(string cause) =>
            AnalyticsCorrelationId.Create(PlayerId, Completion.CompletedAt, cause);
    }

    /// <summary>
    /// A meta feature that advances when the player finishes a game, implemented by the feature's player-model
    /// state and registered in <see cref="PlayerModel.CollectMatchCompletionObservers"/> (<c>docs/player.md</c>):
    /// <list type="number">
    /// <item>Write only the observer's own <c>[NoChecksum]</c> member. The completion runs in an unsynchronized
    /// server action, so writing checksummed state, even <c>PlayerModelBase.LiveOpsEvents</c>, ends the session.</item>
    /// <item>Decide only from <see cref="MatchCompletion.CompletedAt"/> or its local form,
    /// <see cref="MatchCompletionContext.CompletedAtLocal"/>.</item>
    /// <item>Never grant rewards. Mark a reward as ready, and the player's claim action grants it.</item>
    /// <item>Never move a window, step or phase backwards. A re-delivered completion keeps its original, older time,
    /// and no checksum mismatch would reveal a double grant or lost progress. Deduplicate with a set of
    /// <see cref="MatchCompletion.MatchId"/> values scoped to the current window.</item>
    /// </list>
    /// </summary>
    public interface IMatchCompletionObserver
    {
        /// <summary>
        /// Called once per completed match, when <see cref="PlayerModel.TryRecordMatch"/> adds the game to the
        /// record. Not called for a table abandoned before it started, or for a result the record already
        /// holds. It is called again if a table re-sends its result after the game has dropped out of the
        /// bounded match history, with the original completion time. See rule 4.
        /// </summary>
        void OnMatchCompleted(in MatchCompletionContext context);
    }
}
