using Metaplay.Core;
using Metaplay.Core.Model;
using System.Collections.Generic;

namespace Game.Logic
{
    /// <summary>Why a first-week claim was refused. The screen has a message for each result.</summary>
    public static partial class ActionResults
    {
        /// <summary>The player's schedule has no day with that ID, or the event has not started.</summary>
        public static readonly MetaActionResult NoSuchFirstWeekDay = new MetaActionResult(nameof(NoSuchFirstWeekDay));

        /// <summary>The day's goal was not met within its window, so there is no reward to pay.</summary>
        public static readonly MetaActionResult FirstWeekDayNotComplete = new MetaActionResult(nameof(FirstWeekDayNotComplete));

        /// <summary>The reward was already paid, for example on a replayed or duplicate claim.</summary>
        public static readonly MetaActionResult FirstWeekAlreadyClaimed = new MetaActionResult(nameof(FirstWeekAlreadyClaimed));

        /// <summary>The player's assigned schedule, or the day's reward, is missing from the active game config.</summary>
        public static readonly MetaActionResult FirstWeekConfigMissing = new MetaActionResult(nameof(FirstWeekConfigMissing));
    }

    /// <summary>The day a claim pays and its reward. Computed on both action passes and applied on the commit pass.</summary>
    public readonly struct FirstWeekClaim
    {
        public FirstWeekDayInfo     Day      { get; }
        public FirstWeekDayProgress Progress { get; }

        internal FirstWeekClaim(FirstWeekDayInfo day, FirstWeekDayProgress progress)
        {
            Day      = day;
            Progress = progress;
        }

        public FirstWeekDayId DayId  => Day.Id;
        public RewardBundle   Reward => Day.Reward;
    }

    /// <summary>
    /// One player's first-week event: its start, the assigned schedule, and progress per personal day
    /// (<c>docs/first-week-event.md</c>).
    /// <para>
    /// It observes match completions, which arrive in an unsynchronized server action, so it is a
    /// <c>[NoChecksum]</c> member of <see cref="PlayerModel"/> and writes no other state. Completing a day only
    /// marks its reward ready, and <see cref="PlayerClaimFirstWeekReward"/> pays it. Nothing that can be computed
    /// is stored: <see cref="DayIndexAt"/> computes the active day from the start time.
    /// </para>
    /// </summary>
    [MetaSerializable]
    public class PlayerFirstWeekState : IMatchCompletionObserver
    {
        /// <summary>
        /// When the player's first week began, or <see cref="MetaTime.Epoch"/> if it has not. Set once by
        /// <see cref="Start"/> and never changed.
        /// </summary>
        [MetaMember(1)] public MetaTime StartedAt { get; private set; }

        /// <summary>The schedule assigned to the player at start. It never changes.</summary>
        [MetaMember(2)] public FirstWeekScheduleId ScheduleId { get; private set; }

        /// <summary>
        /// Progress on each day that has counted at least one match, in the order the days were first counted.
        /// A day with no counted match has no entry, which means zero progress.
        /// </summary>
        [MetaMember(3)] List<FirstWeekDayProgress> _days = new List<FirstWeekDayProgress>();

        /// <summary>
        /// How many first-week rewards the player has been paid. Changes only on a committed claim. Reported in
        /// analytics, and used by the client after a reconnect to tell whether a claim went through.
        /// </summary>
        [MetaMember(4)] public int ClaimCount { get; private set; }

        /// <summary>
        /// The last day paid, and when. A client that reconnects during the reward animation uses these to tell
        /// whether the claim was paid.
        /// </summary>
        [MetaMember(5)] public FirstWeekDayId LastClaimedDay { get; private set; }
        [MetaMember(6)] public MetaTime       LastClaimedAt  { get; private set; }

        /// <summary>
        /// The highest day index any counted match has reached. A match whose completion time falls in an
        /// earlier day is not counted.
        /// <para>
        /// A match completion carries the table's completion time, and a re-delivered completion can arrive much
        /// later with that original time. Counting it would add progress to an earlier day, which could turn a
        /// missed day into a completed one after later days were already played. Counting only forward prevents
        /// that (<c>docs/player.md</c>, "Observer rules").
        /// </para>
        /// </summary>
        [MetaMember(7)] public int FurthestDayIndex { get; private set; }

        /// <summary>
        /// The match IDs (<see cref="MatchCompletion.MatchId"/>) already counted into the day at
        /// <see cref="FurthestDayIndex"/>, so a re-delivered match is counted only once. It keeps all IDs for
        /// the current day, not only the last one, because other matches can finish between the two deliveries.
        /// <para>
        /// No IDs are added after the day's target is met, so the list never holds more IDs than the target. It
        /// is cleared when a later day starts counting.
        /// </para>
        /// </summary>
        [MetaMember(8)] List<EntityId> _countedMatches = new List<EntityId>();

        public PlayerFirstWeekState() { }

        /// <summary>Whether the player's first week has started.</summary>
        public bool HasStarted => StartedAt != MetaTime.Epoch;

        /// <summary>The days with at least one counted match, for the LiveOps Dashboard's model view and for tests.</summary>
        public IReadOnlyList<FirstWeekDayProgress> StoredDays => _days ?? NoProgress;

        /// <summary>The match IDs counted into the day at <see cref="FurthestDayIndex"/>.</summary>
        public IReadOnlyList<EntityId> CountedMatches => _countedMatches ?? NoMatches;

        static readonly List<FirstWeekDayProgress> NoProgress = new List<FirstWeekDayProgress>();
        static readonly List<EntityId>             NoMatches  = new List<EntityId>();

        #region Starting

        /// <summary>
        /// Starts the first week: records the start time and assigns the schedule the published config names
        /// as active.
        /// <para>
        /// Idempotent: it never changes a start time that is already set, so it is safe to call from account
        /// creation, a schema migration and <see cref="IMatchCompletionObserver.OnMatchCompleted"/>. Returns false
        /// when the published config names no schedule, and the player then stays unstarted.
        /// </para>
        /// </summary>
        internal bool Start(SharedGameConfig config, MetaTime at)
        {
            if (HasStarted)
                return ScheduleId != null;

            FirstWeekScheduleInfo schedule = ActiveSchedule(config);
            if (schedule == null)
                return false;

            StartedAt  = at;
            ScheduleId = schedule.Id;
            return true;
        }

        /// <summary>The schedule the published config assigns to a player who starts now, or null.</summary>
        public static FirstWeekScheduleInfo ActiveSchedule(SharedGameConfig config) =>
            ConfigRefs.Resolve(config?.Global?.ActiveFirstWeekSchedule, config?.FirstWeekSchedules);

        /// <summary>
        /// The schedule assigned to <b>this player</b>, not the currently active one. Returns null when the
        /// assigned schedule is no longer in the config. Claims are then refused until the schedule is published
        /// again, which is safer than paying rewards from a different schedule.
        /// </summary>
        public FirstWeekScheduleInfo AssignedSchedule(SharedGameConfig config)
        {
            if (ScheduleId == null || config?.FirstWeekSchedules == null)
                return null;

            return config.FirstWeekSchedules.TryGetValue(ScheduleId, out FirstWeekScheduleInfo schedule) ? schedule : null;
        }

        #endregion

        #region Reading the week

        /// <summary>
        /// Returns the zero-based personal day that <paramref name="at"/> falls in. Returns
        /// <see cref="FirstWeekScheduleInfo.NumDays"/> after the event has ended, and -1 before the start time
        /// or when the event has not started.
        /// <para>
        /// It is a single division, so its cost does not depend on how much time has passed.
        /// </para>
        /// </summary>
        public int DayIndexAt(MetaTime at)
        {
            if (!HasStarted)
                return -1;

            // A time before StartedAt does happen, so do not remove this check. A migrated account's start time
            // is later than all its earlier matches, so a re-delivered old match has an earlier time. An account
            // started by OnMatchCompleted can also receive an older re-delivered match afterwards. Returning -1
            // keeps such matches out of the first day. This is the only place that handles such times.
            MetaDuration elapsed = at - StartedAt;
            if (elapsed < MetaDuration.Zero)
                return -1;

            long index = elapsed.Milliseconds / FirstWeekScheduleInfo.DayLength.Milliseconds;
            return index >= FirstWeekScheduleInfo.NumDays ? FirstWeekScheduleInfo.NumDays : (int)index;
        }

        /// <summary>When the personal day at <paramref name="index"/> starts.</summary>
        public MetaTime DayStartsAt(int index) => StartedAt + FirstWeekScheduleInfo.DayLength * index;

        /// <summary>When the last day window ends.</summary>
        public MetaTime EndsAt => StartedAt + FirstWeekScheduleInfo.EventLength;

        /// <summary>
        /// Returns the state of the whole event at <paramref name="at"/>, computed from the assigned schedule
        /// and the start time. It writes nothing, so the screen can call it freely. Returns the default outlook
        /// when the event has not started or the schedule is missing a day.
        /// </summary>
        public FirstWeekOutlook OutlookAt(SharedGameConfig config, MetaTime at)
        {
            FirstWeekScheduleInfo schedule = AssignedSchedule(config);
            if (!HasStarted || schedule == null)
                return default;

            List<FirstWeekDayStatus> days = new List<FirstWeekDayStatus>(FirstWeekScheduleInfo.NumDays);
            for (int index = 0; index < FirstWeekScheduleInfo.NumDays; index++)
            {
                FirstWeekDayInfo info = schedule.DayAt(index + 1);
                if (info == null)
                    return default;

                days.Add(new FirstWeekDayStatus(info, Find(info.Id), DayStartsAt(index), DayStartsAt(index + 1), at));
            }

            return new FirstWeekOutlook(days, ScheduleId, DayIndexAt(at), StartedAt, EndsAt);
        }

        /// <summary>The player's progress on one published day, or null if no match has been counted for it.</summary>
        public FirstWeekDayProgress Find(FirstWeekDayId id)
        {
            if (id == null)
                return null;

            foreach (FirstWeekDayProgress progress in StoredDays)
            {
                if (progress != null && progress.Id == id)
                    return progress;
            }
            return null;
        }

        #endregion

        #region A finished game

        /// <summary>
        /// Counts a finished match toward the day its completion time falls in, up to that day's target, following
        /// the match-completion observer rules (<see cref="IMatchCompletionObserver"/>).
        /// <para>
        /// The completion time decides the day even when that day's window has closed, so a day is not missed only
        /// because the delivery was late. This applies only while no match has been counted into a later day (see
        /// <see cref="FurthestDayIndex"/>).
        /// </para>
        /// </summary>
        void IMatchCompletionObserver.OnMatchCompleted(in MatchCompletionContext context)
        {
            MatchCompletion completion = context.Completion;

            // An account that has not started yet starts at the completion time of this match. If the config
            // names no schedule, the player stays unstarted and nothing is written.
            if (!Start(context.GameConfig, completion.CompletedAt))
                return;

            FirstWeekScheduleInfo schedule = AssignedSchedule(context.GameConfig);
            if (schedule == null)
                return;

            int dayIndex = DayIndexAt(completion.CompletedAt);

            // The match finished before the start time or after the last day window. It is not counted.
            if (dayIndex < 0 || dayIndex >= FirstWeekScheduleInfo.NumDays)
                return;

            // Count only forward. See FurthestDayIndex.
            if (dayIndex < FurthestDayIndex)
                return;

            // Look up the day before writing anything, so a schedule with a missing day or reward leaves the
            // player's progress unchanged.
            FirstWeekDayInfo day = schedule.DayAt(dayIndex + 1);
            if (day == null || day.Reward == null)
                return;

            if (dayIndex > FurthestDayIndex)
            {
                // A later day starts counting. The counted match IDs belong to the previous day, so clear them.
                FurthestDayIndex = dayIndex;
                _countedMatches  = new List<EntityId>();
            }

            FirstWeekDayProgress progress = Find(day.Id);
            if (progress != null && progress.IsComplete)
                return; // The target is met; further matches change nothing.

            _countedMatches ??= new List<EntityId>();
            if (_countedMatches.Contains(completion.MatchId))
                return;
            _countedMatches.Add(completion.MatchId);

            if (progress == null)
            {
                progress = new FirstWeekDayProgress(day.Id);
                _days ??= new List<FirstWeekDayProgress>();
                _days.Add(progress);
            }

            if (!progress.Advance(day.MatchesRequired, completion.CompletedAt, out int progressBefore, out int progressAfter))
                return;

            context.Emit(new PlayerEventFirstWeekDayCompleted(
                ScheduleId, day.Id, day.Day, day.MatchesRequired, progressBefore, progressAfter, dayIndex, day.Day == FirstWeekScheduleInfo.NumDays));
        }

        #endregion

        #region Claiming

        /// <summary>
        /// Looks up <paramref name="dayId"/> in the assigned schedule and checks whether its reward can be paid.
        /// <para>
        /// There is intentionally no claim deadline. A day completed within its window stays claimable for the
        /// rest of the event and after it ends.
        /// </para>
        /// </summary>
        public MetaActionResult ResolveClaim(SharedGameConfig config, FirstWeekDayId dayId, out FirstWeekClaim claim)
        {
            claim = default;

            if (dayId == null)
                return ActionResults.NoSuchFirstWeekDay;
            if (!HasStarted)
                return ActionResults.NoSuchFirstWeekDay;

            FirstWeekScheduleInfo schedule = AssignedSchedule(config);
            if (schedule == null)
                return ActionResults.FirstWeekConfigMissing;

            FirstWeekDayInfo day = schedule.Find(dayId);
            if (day == null)
                return ActionResults.NoSuchFirstWeekDay;
            if (day.Reward == null)
                return ActionResults.FirstWeekConfigMissing;

            FirstWeekDayProgress progress = Find(dayId);
            if (progress == null || !progress.IsComplete)
                return ActionResults.FirstWeekDayNotComplete;
            if (progress.IsClaimed)
                return ActionResults.FirstWeekAlreadyClaimed;

            claim = new FirstWeekClaim(day, progress);
            return MetaActionResult.Success;
        }

        /// <summary>
        /// Records a paid claim. Called only from the commit pass of <see cref="PlayerClaimFirstWeekReward"/>,
        /// after the wallet accepted the grant, so <see cref="ClaimCount"/> counts only paid rewards.
        /// </summary>
        internal void ApplyClaim(in FirstWeekClaim claim, MetaTime at)
        {
            claim.Progress.MarkClaimed(at);
            ClaimCount    += 1;
            LastClaimedDay = claim.DayId;
            LastClaimedAt  = at;
        }

        /// <summary>How many day windows had closed without their goal being met at <paramref name="at"/>. Reported on a claim.</summary>
        public int MissedDaysAt(SharedGameConfig config, MetaTime at) => OutlookAt(config, at).MissedCount;

        /// <summary>How many days have been claimed. Reported on a claim.</summary>
        public int ClaimedDayCount
        {
            get
            {
                int count = 0;
                foreach (FirstWeekDayProgress progress in StoredDays)
                {
                    if (progress != null && progress.IsClaimed)
                        count++;
                }
                return count;
            }
        }

        #endregion

        public override string ToString() =>
            HasStarted
                ? $"first week from {StartedAt} on {ScheduleId}, {ClaimedDayCount} claimed"
                : "first week not started";
    }
}
