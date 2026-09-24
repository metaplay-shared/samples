namespace WebClient.Meta;

using Game.Logic;
using Metaplay.Core;
using Metaplay.Core.Player;

/// <summary>
/// Builds the <see cref="MissionsView"/> from the player's mission state.
/// <para>
/// The client decides only presentation here: goal titles and time remaining. Everything else comes from the
/// player model and the game config (<c>docs/meta-shell.md</c>, "How screens get state"). It is separate from
/// <c>MetaStateService</c> so that tests can run it without a browser or a server.
/// </para>
/// </summary>
public static class MissionsViewBuilder
{
    /// <summary>The view used when the player state or config is missing or cannot be read.</summary>
    public static readonly MissionsView Unavailable =
        new MissionsView(ActivityState.Unavailable, Array.Empty<GoalView>(), null);

    /// <summary>
    /// Builds the view at <paramref name="now"/>. <paramref name="utcOffset"/> is the player's time zone offset,
    /// which decides the local day that the daily missions belong to.
    /// </summary>
    public static MissionsView From(PlayerMissionState state, SharedGameConfig config, MetaTime now, MetaDuration utcOffset)
    {
        if (state == null || config == null)
            return Unavailable;

        MissionRollover rollover;
        try
        {
            rollover = state.RolloverAt(config, new PlayerLocalTime(now, utcOffset));
        }
        catch (Exception)
        {
            // Catch every exception type. Every meta screen reads the snapshot built from this view, so an exception
            // here, for example from a mission schedule with gaps, would break rendering of the whole shell
            // (docs/missions.md).
            return Unavailable;
        }

        IReadOnlyList<GoalView> daily     = GoalsOf(rollover.Daily, config);
        IReadOnlyList<GoalView> weekly    = GoalsOf(rollover.Weekly, config);
        IReadOnlyList<GoalView> lateClaim = Concat(
            StillClaimable(rollover.DailyLateClaim, now) ? GoalsOf(rollover.DailyLateClaim, config) : Array.Empty<GoalView>(),
            StillClaimable(rollover.WeeklyLateClaim, now) ? GoalsOf(rollover.WeeklyLateClaim, config) : Array.Empty<GoalView>());

        return new MissionsView(
            State:              StateOf(daily, weekly, lateClaim),
            DailyMissions:      daily,
            UntilDailyReset:    TimeLeft(rollover.Daily.EndsAt, now),
            Weekly:             weekly,
            UntilWeeklyReset:   TimeLeft(rollover.Weekly.EndsAt, now),
            LateClaim:          lateClaim,
            UntilLateClaimEnds: SoonestLateClaimDeadline(rollover, now));
    }

    /// <summary>
    /// The missions' activity state. It is <see cref="ActivityState.InProgress"/> while any mission is unclaimed,
    /// including a completed one, and <see cref="ActivityState.Completed"/> only when every mission is claimed.
    /// <see cref="BadgePolicy"/> checks claimable rewards separately.
    /// </summary>
    static ActivityState StateOf(IReadOnlyList<GoalView> daily, IReadOnlyList<GoalView> weekly, IReadOnlyList<GoalView> lateClaim)
    {
        if (daily.Count == 0 && weekly.Count == 0)
            return ActivityState.Unavailable;

        bool anythingLeft = daily.Concat(weekly).Concat(lateClaim).Any(goal => !goal.IsClaimed);
        return anythingLeft ? ActivityState.InProgress : ActivityState.Completed;
    }

    static IReadOnlyList<GoalView> GoalsOf(MissionActivation activation, SharedGameConfig config)
    {
        if (activation == null)
            return Array.Empty<GoalView>();

        List<GoalView> goals = new List<GoalView>();
        foreach (MissionProgress progress in activation.Missions)
        {
            MissionInfo mission = config.Missions?.GetValueOrDefault(progress.Id);
            if (mission == null)
                continue;

            goals.Add(new GoalView(
                Title:     TitleOf(mission),
                Progress:  progress.Count,
                Target:    mission.TargetCount,
                Reward:    RewardOf(mission.Reward),
                IsClaimed: progress.IsClaimed,
                Id:        activation.InstanceOf(progress.Id).Value));
        }

        return goals;
    }

    /// <summary>
    /// The mission title, built from the objective and the target count. It is not stored in config because this
    /// sample has no localisation.
    /// </summary>
    public static string TitleOf(MissionInfo mission)
    {
        string verb = mission.Objective == MissionObjective.MatchesWon ? "Win" : "Play";
        return mission.TargetCount == 1 ? $"{verb} 1 game" : $"{verb} {mission.TargetCount} games";
    }

    /// <summary>
    /// Converts a game config reward bundle with <see cref="RewardView.From"/>. Missions grant only currencies.
    /// </summary>
    public static RewardView RewardOf(Game.Logic.RewardBundle bundle) => RewardView.From(bundle);

    static IReadOnlyList<GoalView> Concat(IReadOnlyList<GoalView> first, IReadOnlyList<GoalView> second)
    {
        if (first.Count == 0)
            return second;
        if (second.Count == 0)
            return first;
        return first.Concat(second).ToList();
    }

    /// <summary>
    /// Whether a previous activation kept for its late-claim window can still be claimed at <paramref name="now"/>.
    /// <para>
    /// Mission rollover keeps an activation while it has unclaimed rewards and does not check its claim deadline,
    /// so that a stale timestamp, such as one from a re-delivered game result, cannot expire or restore a reward.
    /// The deadline is checked here and in the claim action, both against the player model's time.
    /// </para>
    /// </summary>
    static bool StillClaimable(MissionActivation activation, MetaTime now) =>
        activation != null && now < activation.ClaimDeadline;

    /// <summary>
    /// The time until the sooner of the daily and weekly late-claim deadlines, or null if neither is claimable.
    /// </summary>
    static TimeSpan? SoonestLateClaimDeadline(MissionRollover rollover, MetaTime now)
    {
        TimeSpan? daily  = StillClaimable(rollover.DailyLateClaim, now) ? TimeLeft(rollover.DailyLateClaim.ClaimDeadline, now) : null;
        TimeSpan? weekly = StillClaimable(rollover.WeeklyLateClaim, now) ? TimeLeft(rollover.WeeklyLateClaim.ClaimDeadline, now) : null;

        if (daily == null)
            return weekly;
        if (weekly == null)
            return daily;
        return daily < weekly ? daily : weekly;
    }

    /// <summary>
    /// The time from <paramref name="now"/> to <paramref name="deadline"/>, or zero if the deadline has passed.
    /// </summary>
    public static TimeSpan TimeLeft(MetaTime deadline, MetaTime now) => Countdown.NonNegative(deadline - now);
}
