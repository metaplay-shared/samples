using Game.Logic;
using Metaplay.Core;

namespace WebClient.Meta;

/// <summary>
/// Builds the <see cref="RailStep"/> lists for step rails from view models.
/// <para>
/// This logic is here instead of in the pages so that tests can check it: <c>WebClient/Meta</c> compiles into the
/// test project and <c>Components</c> does not. The steps, their rewards and the number of days come from the
/// view. These methods only compute each step's <see cref="RailStepState"/>.
/// </para>
/// </summary>
public static class StepRailBuilder
{
    /// <summary>
    /// The daily reward cycle's steps. Steps claimed in this cycle are <see cref="RailStepState.Done"/>, the next
    /// step is <see cref="RailStepState.Current"/>, and a later step that awards a spin token is
    /// <see cref="RailStepState.Prize"/>. No step is <see cref="RailStepState.Locked"/>.
    /// </summary>
    public static IReadOnlyList<RailStep> ForCycle(DailyRewardView view) => view.Cycle
        .Select(step => new RailStep(
            Label:    $"Day {step.Step}",
            State:    step.Step <= view.StepsClaimedInCycle      ? RailStepState.Done
                    : step.Step == view.StepsClaimedInCycle + 1  ? RailStepState.Current
                    : step.HasSpinToken                          ? RailStepState.Prize
                    :                                              RailStepState.Future,
            NodeText: step.Step.ToString(),
            Caption:  Currencies.Format(step.Coins)))
        .ToList();

    /// <summary>
    /// The first-week event's days. Days after today are <see cref="RailStepState.Locked"/> because the event
    /// opens one day at a time, except the last day, which is <see cref="RailStepState.Prize"/>.
    /// <para>
    /// Each step uses the day's own <see cref="FirstWeekDayState"/>, so the rail matches the day tiles: a missed
    /// day is <see cref="RailStepState.Missed"/>. After the event ends, no step is
    /// <see cref="RailStepState.Current"/>.
    /// </para>
    /// </summary>
    public static IReadOnlyList<RailStep> ForFirstWeekDays(FirstWeekView view) => view.Days
        .Select(day => new RailStep(
            Label:    $"Day {day.Day}",
            State:    StateOf(day, view),
            NodeText: day.Day.ToString()))
        .ToList();

    private static RailStepState StateOf(FirstWeekDayView day, FirstWeekView view)
    {
        // Today is Current whatever its day state is, including a completed day with an unclaimed reward.
        if (!view.HasEnded && day.Day == view.CurrentDay)
            return RailStepState.Current;

        return day.State switch
        {
            FirstWeekDayState.Claimed     => RailStepState.Done,
            FirstWeekDayState.RewardReady => RailStepState.Done,
            FirstWeekDayState.Missed      => RailStepState.Missed,
            _ when day.Day == view.TotalDays => RailStepState.Prize,
            _                                => RailStepState.Locked,
        };
    }

    /// <summary>
    /// The standings rows for a tournament group, in the order of <paramref name="ranked"/>. Bot rows are marked
    /// with <see cref="StandingRow.IsBot"/>, and the player's own row with <see cref="StandingRow.IsSelf"/> so the
    /// page can pin it.
    /// <para>
    /// Each row's cosmetics are converted from the seat's catalogue ids to style tokens with
    /// <paramref name="styleTokenOf"/> (<c>docs/cosmetics.md</c>). The converter is passed in instead of a game
    /// config so that tests do not need a config.
    /// </para>
    /// </summary>
    public static IReadOnlyList<StandingRow> StandingsOf(
        IReadOnlyList<TournamentEntrant> ranked,
        EntityId                         self,
        Func<CosmeticId?, string>        styleTokenOf)
    {
        List<StandingRow> rows = new List<StandingRow>(ranked.Count);

        for (int index = 0; index < ranked.Count; index++)
        {
            TournamentEntrant     seat     = ranked[index];
            PlayerPublicIdentity? identity = seat.Identity;

            rows.Add(new StandingRow(
                Rank:         index + 1,
                DisplayName:  identity?.DisplayName ?? "",
                Score:        seat.Wins,
                RankDelta:    0,
                // Require a known player id, so that a session without one does not mark another row as the player's.
                IsSelf:       !seat.IsBot && self != EntityId.None && identity?.PlayerId == self,
                IsBot:        seat.IsBot,

                // Convert catalogue ids to the style tokens that the avatar component uses.
                AvatarToken:     styleTokenOf(identity?.AvatarId),
                FrameToken:      styleTokenOf(identity?.FrameId),
                NameEffectToken: styleTokenOf(identity?.NameEffectId)));
        }

        return rows;
    }
}
