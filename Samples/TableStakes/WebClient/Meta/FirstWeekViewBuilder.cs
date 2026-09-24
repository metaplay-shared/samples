namespace WebClient.Meta;

using Game.Logic;
using Metaplay.Core;

/// <summary>
/// Builds the <see cref="FirstWeekView"/> from the player's first-week event state.
/// <para>
/// The client decides only presentation here: goal titles and the time left in the active day. Everything else
/// comes from the player model and the assigned schedule (<c>docs/meta-shell.md</c>, "How screens get state").
/// It is separate from <c>MetaStateService</c> so that tests can run it without a browser or a server.
/// </para>
/// </summary>
public static class FirstWeekViewBuilder
{
    /// <summary>The event's title on the hub and on its screen.</summary>
    public const string Title = "First-Week Event";

    /// <summary>The view used when the player state or config is missing or cannot be read.</summary>
    public static readonly FirstWeekView Unavailable = new FirstWeekView(
        State:                 ActivityState.Unavailable,
        Title:                 Title,
        CurrentDay:            0,
        TotalDays:             FirstWeekScheduleInfo.NumDays,
        TodaysGoals:           Array.Empty<GoalView>(),
        UntilDayExpires:       null,
        HasClaimableDayReward: false,
        DayReward:             RewardView.Empty,
        GrandPrize:            RewardView.Empty);

    /// <summary>
    /// Builds the view at <paramref name="now"/>. Callers pass the player model's current time, not the browser's,
    /// because the model's time decides when a day ends.
    /// </summary>
    public static FirstWeekView From(PlayerFirstWeekState state, SharedGameConfig config, MetaTime now)
    {
        if (state == null || config == null)
            return Unavailable;

        FirstWeekOutlook outlook;
        try
        {
            outlook = state.OutlookAt(config, now);
        }
        catch (Exception)
        {
            // Catch every exception type. Every meta screen reads the snapshot built from this view, so an exception
            // here would break rendering of the whole shell instead of one card.
            return Unavailable;
        }

        if (!outlook.IsResolved)
            return Unavailable;

        List<FirstWeekDayView> days = new List<FirstWeekDayView>(outlook.Days.Count);
        foreach (FirstWeekDayStatus day in outlook.Days)
        {
            days.Add(new FirstWeekDayView(
                Day:      day.Day,
                Title:    TitleOf(day.Target),
                Progress: day.Count,
                Target:   day.Target,
                Reward:   RewardOf(day.Info.Reward),
                State:    StateOf(day),
                Id:       day.Info.Id.Value));
        }

        FirstWeekDayStatus? active = outlook.ActiveDay;

        return new FirstWeekView(
            State:                 StateOf(outlook),
            Title:                 Title,
            CurrentDay:            active?.Day ?? outlook.Days.Count,
            TotalDays:             outlook.Days.Count,
            TodaysGoals:           active == null ? Array.Empty<GoalView>() : new[] { GoalOf(active.Value) },
            UntilDayExpires:       outlook.UntilActiveDayEnds(now)?.ToTimeSpan(),
            HasClaimableDayReward: active?.IsRewardReady ?? false,
            DayReward:             active == null ? RewardView.Empty : RewardOf(active.Value.Info.Reward),
            GrandPrize:            RewardOf(outlook.Days[^1].Info.Reward),
            Week:                  days,
            HasEnded:              outlook.HasEnded);
    }

    /// <summary>
    /// The event's activity state.
    /// <para>
    /// <see cref="ActivityState.Expired"/> is never returned, because a missed day does not end the event.
    /// </para>
    /// </summary>
    static ActivityState StateOf(in FirstWeekOutlook outlook)
    {
        if (outlook.ClaimableCount > 0)
            return ActivityState.Actionable;
        if (outlook.HasEnded)
            return ActivityState.Completed;

        FirstWeekDayStatus? active = outlook.ActiveDay;
        return active is { IsComplete: true } ? ActivityState.Completed : ActivityState.InProgress;
    }

    static FirstWeekDayState StateOf(in FirstWeekDayStatus day)
    {
        if (day.IsClaimed)
            return FirstWeekDayState.Claimed;
        if (day.IsRewardReady)
            return FirstWeekDayState.RewardReady;
        if (day.IsMissed)
            return FirstWeekDayState.Missed;
        return day.IsFuture ? FirstWeekDayState.Future : FirstWeekDayState.InProgress;
    }

    /// <summary>Today's goal as a <see cref="GoalView"/>, the same type the mission list uses.</summary>
    static GoalView GoalOf(in FirstWeekDayStatus day) => new GoalView(
        Title:     TitleOf(day.Target),
        Progress:  day.Count,
        Target:    day.Target,
        Reward:    RewardOf(day.Info.Reward),
        IsClaimed: day.IsClaimed,
        Id:        day.Info.Id.Value);

    /// <summary>
    /// The goal title, built from the target count. It is not stored in config because this sample has no
    /// localisation (the same as <see cref="MissionsViewBuilder.TitleOf"/>).
    /// </summary>
    public static string TitleOf(int target) => target == 1 ? "Complete 1 match" : $"Complete {target} matches";

    /// <summary>Converts a game config reward bundle with <see cref="RewardView.From"/>.</summary>
    public static RewardView RewardOf(Game.Logic.RewardBundle bundle) => RewardView.From(bundle);
}
