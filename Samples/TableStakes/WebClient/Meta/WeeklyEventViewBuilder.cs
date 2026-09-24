namespace WebClient.Meta;

using Game.Logic;
using Metaplay.Core;
using Metaplay.Core.LiveOpsEvent;

/// <summary>
/// Builds the <see cref="WeeklyEventView"/> from the player's weekly event state (<c>docs/weekly-event.md</c>).
/// The client decides only presentation: the phase text, which countdown to show, and whether the card offers a
/// claim. The event, its target, its reward and the player's points come from the player model and the event
/// content. It is separate from <c>MetaStateService</c> so that tests can run it without a browser or a server.
/// </summary>
public static class WeeklyEventViewBuilder
{
    /// <summary>The title used when the event has no theme.</summary>
    public const string Title = "Weekly Event";

    /// <summary>
    /// The view used when the player has no weekly event, or its state cannot be read.
    /// <para>
    /// It is an empty state with no countdown, not an error. Scheduled events normally follow each other without a
    /// gap, and this view covers the case where one occurs.
    /// </para>
    /// </summary>
    public static readonly WeeklyEventView Unavailable = new WeeklyEventView(
        State:            ActivityState.Unavailable,
        Theme:            Title,
        Tagline:          "The next event has not been scheduled yet.",
        PhaseLabel:       "",
        Points:           0,
        TargetPoints:     0,
        UntilEnd:         null,
        Reward:           RewardView.Empty,
        IsNewlyAvailable: false,
        IsScoring:        false);

    /// <summary>
    /// Builds the view at <paramref name="now"/>. Callers pass the player model's current time, not the browser's,
    /// because the model's time decides the event phases.
    /// </summary>
    public static WeeklyEventView From(PlayerWeeklyEventState state, PlayerLiveOpsEventsModel events, MetaTime now)
    {
        if (state == null || events == null)
            return Unavailable;

        WeeklyEventOutlook outlook;
        try
        {
            outlook = state.OutlookAt(events, now);
        }
        catch (Exception)
        {
            // Catch every exception type. Every meta screen reads the snapshot built from this view, so an exception
            // here would break rendering of the whole shell instead of one card.
            return Unavailable;
        }

        return Of(outlook, now);
    }

    /// <summary>
    /// Builds the view from an already resolved outlook. It is separate from
    /// <see cref="From(PlayerWeeklyEventState, PlayerLiveOpsEventsModel, MetaTime)"/> so that tests can pass any
    /// phase and progress state directly.
    /// </summary>
    public static WeeklyEventView Of(in WeeklyEventOutlook outlook, MetaTime now)
    {
        if (!outlook.IsResolved || outlook.Content == null)
            return Unavailable;

        return new WeeklyEventView(
            State:        StateOf(outlook),
            Theme:        outlook.Content.Theme ?? Title,
            Tagline:      outlook.Content.Tagline ?? "",
            PhaseLabel:   PhaseOf(outlook),
            Points:       outlook.Points,
            TargetPoints: outlook.TargetPoints,
            UntilEnd:     UntilEndOf(outlook, now),
            Reward:       FirstWeekViewBuilder.RewardOf(outlook.Reward),
            // Always false: a LiveOps event does not record whether the player has seen it, so nothing would clear a
            // badge set from this flag. BadgePolicy badges the weekly event for a claimable reward instead.
            IsNewlyAvailable:   false,
            UntilStart:         outlook.IsPreview ? Countdown.NonNegative(outlook.StartsAt - now) : null,
            EventId:            outlook.EventId.ToString(),
            HasClaimableReward: outlook.IsClaimable,
            RewardClaimed:      outlook.IsClaimed,
            IsScoring:          outlook.IsScoring);
    }

    /// <summary>
    /// The event's activity state. <see cref="ActivityState.Expired"/> is never returned, because the SDK removes
    /// an event from the player after its review phase. After that the view is <see cref="Unavailable"/> or the
    /// next event's preview.
    /// </summary>
    static ActivityState StateOf(in WeeklyEventOutlook outlook)
    {
        if (outlook.IsClaimable)
            return ActivityState.Actionable;
        if (outlook.IsPreview)
            return ActivityState.Ready;
        if (outlook.IsScoring)
            return ActivityState.InProgress;

        // Review phase with no claimable reward.
        return ActivityState.Completed;
    }

    /// <summary>The label above the card title, based on the SDK's LiveOps event phase.</summary>
    static string PhaseOf(in WeeklyEventOutlook outlook)
    {
        if (outlook.IsPreview)
            return "Starts soon";
        if (outlook.Phase == LiveOpsEventPhase.EndingSoon)
            return "Final hours";
        if (outlook.IsScoring)
            return "This week";
        return "Collect by";
    }

    /// <summary>
    /// The time until scoring ends while the event is scoring, and after that the time until the claim period
    /// ends. Returns null in preview, where <see cref="WeeklyEventView.UntilStart"/> is the countdown, so that a
    /// surface never shows two countdowns.
    /// </summary>
    static TimeSpan? UntilEndOf(in WeeklyEventOutlook outlook, MetaTime now)
    {
        if (!outlook.HasSchedule || outlook.IsPreview)
            return null;

        return Countdown.NonNegative((outlook.IsScoring ? outlook.EndsAt : outlook.ConcludesAt) - now);
    }
}
