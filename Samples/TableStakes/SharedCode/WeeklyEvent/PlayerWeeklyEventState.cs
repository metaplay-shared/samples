using Metaplay.Core;
using Metaplay.Core.LiveOpsEvent;
using Metaplay.Core.Model;
using System.Collections.Generic;
using System.Linq;

namespace Game.Logic
{
    /// <summary>Why a weekly-event claim was refused. The screen has a message for each result.</summary>
    public static partial class ActionResults
    {
        /// <summary>The player has no weekly event with that ID, for example because it concluded, or the event has no reward.</summary>
        public static readonly MetaActionResult NoSuchWeeklyEvent = new MetaActionResult(nameof(NoSuchWeeklyEvent));

        /// <summary>The points target has not been reached, so there is no reward to pay.</summary>
        public static readonly MetaActionResult WeeklyEventTargetNotReached = new MetaActionResult(nameof(WeeklyEventTargetNotReached));

        /// <summary>The reward was already paid, for example on a replayed or duplicate claim.</summary>
        public static readonly MetaActionResult WeeklyEventAlreadyClaimed = new MetaActionResult(nameof(WeeklyEventAlreadyClaimed));
    }

    /// <summary>
    /// The player's progress in one weekly event, keyed by the event ID.
    /// <para>
    /// Progress is stored per event ID, not in a single slot, because consecutive weeks overlap: the next week's
    /// Preview and the previous week's Review run at the same time as a scoring week, so a player can have two
    /// weekly events at once, one of which may still have an unclaimed reward.
    /// </para>
    /// </summary>
    [MetaSerializable]
    public class WeeklyEventProgress
    {
        /// <summary>The event's SDK occurrence ID.</summary>
        [MetaMember(1)] public MetaGuid EventId { get; private set; }

        /// <summary>Points scored so far. Only increases.</summary>
        [MetaMember(2)] public int Points { get; private set; }

        /// <summary>
        /// The match IDs already counted into this event, so a re-delivered match is counted only once. It
        /// keeps all IDs for the event, not only the last one, because other matches can finish between the two
        /// deliveries.
        /// <para>
        /// A match that scores nothing is not recorded, and nothing is recorded after the target is reached.
        /// Each recorded match scores at least one point, so the list holds <b>at most one ID per point of the
        /// target</b>. The config build limits the target
        /// (see <see cref="WeeklyEventScoring.MaxTargetInPerfectMatches"/>).
        /// </para>
        /// </summary>
        [MetaMember(3)] List<EntityId> _countedMatches = new List<EntityId>();

        /// <summary>
        /// Whether the target has been reached. <b>It is stored, not recomputed from the points</b>, so reaching
        /// the target is reported only once.
        /// </summary>
        [MetaMember(4)] public bool TargetReached { get; private set; }

        /// <summary>Whether the reward has been paid. Set only by the player's claim.</summary>
        [MetaMember(5)] public bool IsClaimed { get; private set; }

        /// <summary>When the reward was paid, so a client that reconnects during the reward animation can tell it was paid.</summary>
        [MetaMember(6)] public MetaTime ClaimedAt { get; private set; }

        public WeeklyEventProgress() { }

        public WeeklyEventProgress(MetaGuid eventId)
        {
            EventId = eventId;
        }

        /// <summary>The match IDs counted into this event.</summary>
        public IReadOnlyList<EntityId> CountedMatches => _countedMatches ?? NoMatches;

        static readonly List<EntityId> NoMatches = new List<EntityId>();

        public bool HasCounted(EntityId matchId) => CountedMatches.Contains(matchId);

        /// <summary>
        /// Counts one scoring match. Returns true only for the match that reaches <paramref name="target"/>.
        /// </summary>
        internal bool TryCountMatch(EntityId matchId, int points, int target)
        {
            _countedMatches ??= new List<EntityId>();
            _countedMatches.Add(matchId);
            Points += points;

            if (TargetReached || target <= 0 || Points < target)
                return false;

            TargetReached = true;
            return true;
        }

        internal void MarkClaimed(MetaTime at)
        {
            IsClaimed = true;
            ClaimedAt = at;
        }

        public override string ToString() =>
            $"{EventId}: {Points} points{(TargetReached ? ", target reached" : "")}{(IsClaimed ? ", claimed" : "")}";
    }

    /// <summary>The weekly event the screen shows, at a given time. Computing it writes nothing.</summary>
    public readonly struct WeeklyEventOutlook
    {
        /// <summary>Whether the player has a weekly event. False is a normal empty state, not an error.</summary>
        public bool IsResolved { get; }

        public MetaGuid           EventId { get; }
        public WeeklyEventContent Content { get; }
        public LiveOpsEventPhase  Phase   { get; }

        /// <summary>The points scored, whether the target is reached, and whether the reward is paid.</summary>
        public int  Points        { get; }
        public bool TargetReached { get; }
        public bool IsClaimed     { get; }

        /// <summary>
        /// The enabled window and the time the event concludes. The conclusion time is the claim deadline,
        /// because the SDK removes the event then and an unclaimed reward is lost. All three times are
        /// <see cref="MetaTime.Epoch"/> for an event with no schedule.
        /// </summary>
        public bool     HasSchedule { get; }
        public MetaTime StartsAt    { get; }
        public MetaTime EndsAt      { get; }
        public MetaTime ConcludesAt { get; }

        /// <summary>
        /// Public so that tests can build any phase and progress state for the client's view code without a
        /// server. The struct is read-only and grants no access to anything, so a public constructor is safe.
        /// </summary>
        public WeeklyEventOutlook(
            MetaGuid           eventId,
            WeeklyEventContent content,
            LiveOpsEventPhase  phase,
            int                points,
            bool               targetReached,
            bool               isClaimed,
            bool               hasSchedule,
            MetaTime           startsAt,
            MetaTime           endsAt,
            MetaTime           concludesAt)
        {
            IsResolved    = true;
            EventId       = eventId;
            Content       = content;
            Phase         = phase;
            Points        = points;
            TargetReached = targetReached;
            IsClaimed     = isClaimed;
            HasSchedule   = hasSchedule;
            StartsAt      = startsAt;
            EndsAt        = endsAt;
            ConcludesAt   = concludesAt;
        }

        /// <summary>The points target, or zero when there is no content.</summary>
        public int TargetPoints => Content?.TargetPoints ?? 0;

        /// <summary>The reward that reaching the target makes claimable.</summary>
        public RewardBundle Reward => Content?.Reward;

        /// <summary>Whether matches score points in the current phase. Only the active phases score, not Preview or Review.</summary>
        public bool IsScoring => IsResolved && Phase != null && Phase.IsActivePhase();

        /// <summary>Whether the week has not started yet.</summary>
        public bool IsPreview => IsResolved && Phase == LiveOpsEventPhase.Preview;

        /// <summary>Whether scoring has ended and only the claim period remains.</summary>
        public bool IsReview => IsResolved && Phase == LiveOpsEventPhase.Review;

        /// <summary>Whether the reward is earned and not yet claimed. Home's next-action card reads this.</summary>
        public bool IsClaimable => IsResolved && TargetReached && !IsClaimed;
    }

    /// <summary>The event a claim pays and its reward. Computed on both action passes and applied on the commit pass.</summary>
    public readonly struct WeeklyEventClaim
    {
        public MetaGuid            EventId  { get; }
        public WeeklyEventContent  Content  { get; }
        public WeeklyEventProgress Progress { get; }

        internal WeeklyEventClaim(MetaGuid eventId, WeeklyEventContent content, WeeklyEventProgress progress)
        {
            EventId  = eventId;
            Content  = content;
            Progress = progress;
        }

        public RewardBundle Reward => Content?.Reward;
    }

    /// <summary>
    /// The player's progress in their weekly themed events (<c>docs/weekly-event.md</c>). The SDK owns the
    /// schedule, phase, audience and content through <see cref="LiveOpsEventSnapshot"/>. This class owns only the
    /// progress.
    /// <para>
    /// It must be its own checksum-excluded member on <see cref="PlayerModel"/>, because match completions arrive
    /// in an unsynchronized server action, which may write only checksum-excluded state. For the same reason the
    /// progress is not stored on the checksummed <c>PlayerModelBase.LiveOpsEvents</c>: a write there is an illegal
    /// modification that turns off the client's journal checker for the session (<c>docs/player.md</c>).
    /// </para>
    /// </summary>
    [MetaSerializable]
    public class PlayerWeeklyEventState : IMatchCompletionObserver
    {
        /// <summary>
        /// Progress in each event that has counted at least one scoring match, in the order they were first
        /// counted. An event with no entry has zero progress.
        /// </summary>
        [MetaMember(1)] List<WeeklyEventProgress> _eventProgress = new List<WeeklyEventProgress>();

        /// <summary>How many weekly-event rewards the player has been paid. Changes only on a committed claim.</summary>
        [MetaMember(2)] public int ClaimCount { get; private set; }

        /// <summary>When the last weekly-event reward was paid.</summary>
        [MetaMember(3)] public MetaTime LastClaimedAt { get; private set; }

        public PlayerWeeklyEventState() { }

        /// <summary>The events with at least one counted scoring match, for the Dashboard's model view and for tests.</summary>
        public IReadOnlyList<WeeklyEventProgress> StoredEvents => _eventProgress ?? NoProgress;

        static readonly List<WeeklyEventProgress> NoProgress = new List<WeeklyEventProgress>();

        /// <summary>The player's progress in one event, or null if nothing has been counted into it.</summary>
        public WeeklyEventProgress Find(MetaGuid eventId)
        {
            foreach (WeeklyEventProgress progress in StoredEvents)
            {
                if (progress != null && progress.EventId == eventId)
                    return progress;
            }
            return null;
        }

        #region Reading the week

        /// <summary>
        /// Returns the weekly event the screen shows at <paramref name="now"/>, chosen from the player's events.
        /// It writes nothing, so the screen can call it freely.
        /// </summary>
        public WeeklyEventOutlook OutlookAt(PlayerLiveOpsEventsModel events, MetaTime now) =>
            OutlookAt(HeldWeeklyEvents(events), now);

        /// <summary>
        /// <inheritdoc cref="OutlookAt(PlayerLiveOpsEventsModel, MetaTime)"/>
        /// <para>
        /// The priority is: a claimable reward, then a scoring week, then a week in Review, then a week in
        /// Preview. Seeded weeks' enabled windows do not overlap (<c>docs/weekly-event.md</c>), so at most one
        /// event is scoring at a time.
        /// </para>
        /// </summary>
        internal WeeklyEventOutlook OutlookAt(IEnumerable<LiveOpsEventSnapshot> events, MetaTime now)
        {
            WeeklyEventOutlook best = default;
            int                bestRank = int.MaxValue;

            foreach (LiveOpsEventSnapshot held in events)
            {
                if (held.Content is not WeeklyEventContent content)
                    continue;

                WeeklyEventOutlook outlook = OutlookOf(held, content, Find(held.Id));

                int rank = RankOf(outlook);
                if (rank < bestRank)
                {
                    best     = outlook;
                    bestRank = rank;
                }
            }

            return best;
        }

        static WeeklyEventOutlook OutlookOf(in LiveOpsEventSnapshot held, WeeklyEventContent content, WeeklyEventProgress progress)
        {
            return new WeeklyEventOutlook(
                held.Id,
                content,
                held.Phase,
                points: progress?.Points ?? 0,
                targetReached: progress?.TargetReached ?? false,
                isClaimed: progress?.IsClaimed ?? false,
                hasSchedule: held.HasSchedule,
                startsAt: held.EnabledStartsAt,
                endsAt: held.EnabledEndsAt,
                concludesAt: held.ConcludesAt);
        }

        static int RankOf(in WeeklyEventOutlook outlook)
        {
            if (outlook.IsClaimable)
                return 0;
            if (outlook.IsScoring)
                return 1;
            if (outlook.IsReview)
                return 2;
            if (outlook.IsPreview)
                return 3;
            return 4;
        }

        /// <summary>
        /// Returns the player's current weekly events as <see cref="LiveOpsEventSnapshot"/> values, the same type the
        /// match-completion observer receives, so the screen and the observer use the same window rules.
        /// </summary>
        public static IEnumerable<LiveOpsEventSnapshot> HeldWeeklyEvents(PlayerLiveOpsEventsModel events)
        {
            if (events?.EventModels == null)
                yield break;

            foreach ((MetaGuid _, PlayerLiveOpsEventModel model) in events.EventModels)
            {
                if (model?.Content is WeeklyEventContent)
                    yield return new LiveOpsEventSnapshot(model);
            }
        }

        #endregion

        #region A finished game

        /// <summary>
        /// Scores a finished match into every weekly event of the player whose enabled window contains the
        /// match's completion time: points for tricks taken, plus the week's win bonus. The window comes from the
        /// SDK's schedule, so a match finished inside the window counts even when delivered after it closed.
        /// <para>
        /// Entries are created only for events in <see cref="MatchCompletionContext.LiveOpsEvents"/>, so a late
        /// match cannot recreate a removed entry with its claimed flag cleared. A match that scores nothing is not
        /// recorded, so the list of counted matches is limited by the target, not by the number of matches played.
        /// </para>
        /// </summary>
        void IMatchCompletionObserver.OnMatchCompleted(in MatchCompletionContext context)
        {
            MatchCompletion completion = context.Completion;

            foreach (LiveOpsEventSnapshot held in context.LiveOpsEvents)
            {
                if (held.Content is not WeeklyEventContent content)
                    continue;

                // The SDK removes concluded events from the player, so this should not happen. Skip anyway,
                // because an entry created for a concluded event would never be removed by RemoveEventProgress.
                if (held.Phase == null || held.Phase == LiveOpsEventPhase.Concluded)
                    continue;

                // An event with no schedule has no window, so it never scores.
                if (!held.WindowContains(completion.CompletedAt))
                    continue;

                int points = WeeklyEventScoring.PointsFor(completion, content);
                if (points <= 0)
                    continue;

                WeeklyEventProgress progress = Find(held.Id);
                if (progress != null && progress.TargetReached)
                    continue; // The target is reached. Later matches change nothing and are not recorded.

                if (progress != null && progress.HasCounted(completion.MatchId))
                    continue;

                if (progress == null)
                {
                    progress = new WeeklyEventProgress(held.Id);
                    _eventProgress ??= new List<WeeklyEventProgress>();
                    _eventProgress.Add(progress);
                }

                int pointsBefore = progress.Points;
                if (!progress.TryCountMatch(completion.MatchId, points, content.TargetPoints))
                    continue;

                context.Emit(new PlayerEventWeeklyEventTargetReached(
                    held.Id, content.TargetPoints, pointsBefore, progress.Points, progress.CountedMatches.Count));
            }
        }

        #endregion

        #region Claiming

        /// <summary>
        /// Looks up <paramref name="eventId"/> in the player's events and checks whether its reward can be paid.
        /// <para>
        /// The only deadline is that the player must still have the event. A reached target stays claimable
        /// through the Review phase and is lost when the event concludes. The screen warns about this
        /// beforehand.
        /// </para>
        /// </summary>
        public MetaActionResult ResolveClaim(PlayerLiveOpsEventsModel events, MetaGuid eventId, out WeeklyEventClaim claim)
        {
            claim = default;

            PlayerLiveOpsEventModel model = null;
            if (events?.EventModels != null)
                events.EventModels.TryGetValue(eventId, out model);

            if (model?.Content is not WeeklyEventContent content)
                return ActionResults.NoSuchWeeklyEvent;
            if (content.Reward == null)
                return ActionResults.NoSuchWeeklyEvent;

            WeeklyEventProgress progress = Find(eventId);
            if (progress == null || !progress.TargetReached)
                return ActionResults.WeeklyEventTargetNotReached;
            if (progress.IsClaimed)
                return ActionResults.WeeklyEventAlreadyClaimed;

            claim = new WeeklyEventClaim(eventId, content, progress);
            return MetaActionResult.Success;
        }

        /// <summary>
        /// Records a paid claim. Called only from the commit pass of <see cref="PlayerClaimWeeklyEventReward"/>,
        /// after the wallet accepted the grant, so <see cref="ClaimCount"/> counts only paid rewards.
        /// </summary>
        internal void ApplyClaim(in WeeklyEventClaim claim, MetaTime at)
        {
            claim.Progress.MarkClaimed(at);
            ClaimCount   += 1;
            LastClaimedAt = at;
        }

        #endregion

        /// <summary>
        /// Removes one event's progress because the event has ended.
        /// <para>
        /// Called from <see cref="WeeklyEventPlayerModel.OnPhaseChanged"/> when the event reaches
        /// <see cref="LiveOpsEventPhase.Concluded"/>, the phase at which the SDK removes the event model from the
        /// player. Without this, the state would keep one entry per week forever.
        /// </para>
        /// </summary>
        internal void RemoveEventProgress(MetaGuid eventId)
        {
            if (_eventProgress == null)
                return;

            for (int index = 0; index < _eventProgress.Count; index++)
            {
                if (_eventProgress[index] != null && _eventProgress[index].EventId == eventId)
                {
                    _eventProgress.RemoveAt(index);
                    return;
                }
            }
        }

        public override string ToString() => $"{StoredEvents.Count} weekly event(s), {ClaimCount} claimed";
    }

    /// <summary>
    /// The per-player model the SDK creates for a weekly event. <b>It intentionally stores no state.</b>
    /// <para>
    /// The SDK requires one <see cref="PlayerLiveOpsEventModel"/> subclass per content type. The schedule,
    /// phase, audience and content are on the SDK base class. The game's only data is progress, which cannot
    /// be stored here because this model is checksummed and match completions arrive in an unsynchronized
    /// action (see <see cref="PlayerWeeklyEventState"/>).
    /// </para>
    /// </summary>
    [MetaSerializableDerived(100)]
    public class WeeklyEventPlayerModel : PlayerLiveOpsEventModel<WeeklyEventContent, PlayerModel>
    {
        public WeeklyEventPlayerModel() { }
        public WeeklyEventPlayerModel(PlayerLiveOpsEventInfo info) : base(info) { }

        /// <summary>
        /// Removes the game's progress entry when the week concludes. This runs in the SDK's
        /// <i>synchronized</i> action, at the same tick on client and server, just before the SDK removes this
        /// model from the player.
        /// </summary>
        protected override void OnPhaseChanged(PlayerModel player, LiveOpsEventPhase oldPhase, LiveOpsEventPhase[] fastForwardedPhases, LiveOpsEventPhase newPhase)
        {
            if (newPhase == LiveOpsEventPhase.Concluded)
                player.WeeklyEvent.RemoveEventProgress(Id);
        }
    }
}
