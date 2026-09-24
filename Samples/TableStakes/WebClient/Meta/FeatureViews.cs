namespace WebClient.Meta;

/// <summary>
/// The state common to every feature view, which the shell uses to sort features, show badges and choose the
/// next action.
/// <para>
/// The shell reads only this interface and has no knowledge of feature rules, such as how a streak grows or what
/// unlocks a mission. Feature rules stay out of components so that a different client, such as a Unity client,
/// only needs to rewrite the presentation (docs/meta-shell.md, "How screens get state").
/// </para>
/// </summary>
public interface IFeatureView
{
    MetaFeature Feature { get; }

    ActivityState State { get; }

    /// <summary>
    /// The time until this feature's soonest deadline, or null if it has none. <see cref="FeatureOrdering"/> and
    /// <see cref="NextActionPolicy"/> sort features without a deadline after features with one.
    /// </summary>
    TimeSpan? UntilDeadline { get; }

    /// <summary>
    /// The progress of the feature's most advanced goal, from 0 to 1, or null if the feature has no progress to
    /// show. <see cref="NextActionPolicy"/> compares it in its ending-soon and nearest-progress rungs.
    /// </summary>
    double? Progress { get; }

    /// <summary>
    /// The remaining time at which this feature's countdown counts as ending soon. Each feature sets its own
    /// threshold because its time windows have different lengths (see <see cref="Countdown.EndingSoonThreshold"/>).
    /// </summary>
    TimeSpan EndingSoonWithin { get; }
}

public static class FeatureClaims
{
    /// <summary>
    /// Whether an Events feature has something to claim right now: a reward, or for the spin wheel a spin. The
    /// Events hub's badge and claim style and <see cref="Ui.ButtonLabels.For"/> all read it.
    /// </summary>
    public static bool HasClaimable(IFeatureView view) => view switch
    {
        DailyRewardView d => d.HasClaimableReward,
        MissionsView m    => m.ClaimableCount > 0,
        SpinWheelView s   => s.SpinsAvailable > 0,
        FirstWeekView f   => f.ClaimableDays > 0,
        WeeklyEventView w => w.HasClaimableReward,
        _                 => false,
    };
}

/// <summary>
/// The daily login reward and streak. Unlike the first-week event, it is earned by logging in, and rewards grow
/// with the streak (docs/daily-rewards.md).
/// </summary>
public sealed record DailyRewardView(
    ActivityState                 State,
    int                           StreakDays,
    bool                          HasClaimableReward,
    TimeSpan?                     UntilNextAvailable,
    RewardView                    TodayReward,
    RewardView                    TomorrowReward,
    IReadOnlyList<CycleStepView>  Cycle,
    int                           StepsClaimedInCycle,
    bool                          SkipDayAvailable,
    bool                          WillUseSkipDay,
    bool                          WillResetStreak) : IFeatureView
{
    public MetaFeature Feature       => MetaFeature.DailyReward;
    public TimeSpan?   UntilDeadline => HasClaimableReward ? null : UntilNextAvailable;
    public double?     Progress      => null;

    /// <summary>
    /// Zero, so the daily reward is never ending soon. Missing a day resets the streak but loses no reward.
    /// </summary>
    public TimeSpan EndingSoonWithin => TimeSpan.Zero;
}

/// <summary>
/// The state of one day of the first-week event. The state is computed from the player model, not by the tile
/// component.
/// </summary>
public enum FirstWeekDayState
{
    /// <summary>The day has not started. Its goal and reward are still shown.</summary>
    Future,

    /// <summary>The current day, with its goal not yet completed.</summary>
    InProgress,

    /// <summary>The goal was completed during the day. The reward stays claimable after the day ends.</summary>
    RewardReady,

    /// <summary>The reward has been paid.</summary>
    Claimed,

    /// <summary>The day ended with the goal not completed. Other days are not affected.</summary>
    Missed,
}

/// <summary>
/// One day of the first-week event: its number, goal, progress, reward and state.
/// <para>
/// <see cref="Id"/> identifies the day in the claim action. The shell passes it through without interpreting it.
/// </para>
/// </summary>
public sealed record FirstWeekDayView(
    int               Day,
    string            Title,
    int               Progress,
    int               Target,
    RewardView        Reward,
    FirstWeekDayState State,
    string            Id = "")
{
    public bool IsClaimable => State == FirstWeekDayState.RewardReady;
}

/// <summary>
/// A per-player event with one gameplay goal per day. A day expires if its goal is not completed in time, so
/// <see cref="NextActionPolicy"/> checks this feature first.
/// <para>
/// A completed day's reward does not expire. <see cref="Days"/> holds every day of the event, and the other
/// members describe the current day.
/// </para>
/// </summary>
public sealed record FirstWeekView(
    ActivityState                 State,
    string                        Title,
    int                           CurrentDay,
    int                           TotalDays,
    IReadOnlyList<GoalView>       TodaysGoals,
    TimeSpan?                     UntilDayExpires,
    bool                          HasClaimableDayReward,
    RewardView                    DayReward,
    RewardView                    GrandPrize,
    IReadOnlyList<FirstWeekDayView>? Week = null,
    bool                          HasEnded = false) : IFeatureView
{
    public MetaFeature Feature => MetaFeature.FirstWeekEvent;

    /// <summary>Every day of the event, or an empty list if <c>Week</c> is null.</summary>
    public IReadOnlyList<FirstWeekDayView> Days => Week ?? Array.Empty<FirstWeekDayView>();

    public int GoalsComplete => TodaysGoals.Count(g => g.IsComplete);
    public int GoalsTotal    => TodaysGoals.Count;

    /// <summary>
    /// The number of claimable day rewards across all days, because a reward stays claimable after its day ends.
    /// <see cref="BadgePolicy"/> and <see cref="NextActionPolicy"/> read it.
    /// </summary>
    public int ClaimableDays => Days.Count(d => d.IsClaimable);

    public int ClaimedDays => Days.Count(d => d.State == FirstWeekDayState.Claimed);
    public int MissedDays  => Days.Count(d => d.State == FirstWeekDayState.Missed);

    /// <summary>
    /// The time until the current day ends, or null once today's goals are complete. A completed goal's reward is
    /// kept when the day ends, so there is nothing to lose. The Home card, the Events hub entry and the event
    /// screen all read this property.
    /// </summary>
    public TimeSpan? UntilDeadline => GoalsComplete < GoalsTotal ? UntilDayExpires : null;

    public double? Progress => GoalsTotal == 0 ? null : (double)GoalsComplete / GoalsTotal;

    /// <summary>
    /// A short threshold, because each day lasts one day. The default threshold would make every day ending soon
    /// from its start and keep this card on Home for the whole event.
    /// </summary>
    public TimeSpan EndingSoonWithin => TimeSpan.FromHours(1);

    /// <summary>
    /// Whether the live event has something to claim or a day whose unfinished goals are ending soon.
    /// <see cref="BadgePolicy"/> and <see cref="NextActionPolicy"/> both read it, so the Events badge and the Home
    /// card agree.
    /// </summary>
    public bool NeedsAttention
    {
        get
        {
            if (!State.IsLive() || State == ActivityState.Expired)
                return false;

            // Check every day's reward, not only today's, because an earned reward stays claimable after its day ends.
            bool claimable = ClaimableDays > 0 || HasClaimableDayReward || TodaysGoals.Any(g => g.IsClaimable);

            bool expiring = GoalsComplete < GoalsTotal &&
                            UntilDayExpires is TimeSpan left &&
                            Countdown.PhaseOf(left, EndingSoonWithin) == CountdownPhase.EndingSoon;

            return claimable || expiring;
        }
    }

    /// <summary>
    /// The hub card's title: the current day, or a completion title once the event has ended.
    /// </summary>
    public string Headline => HasEnded ? "First week complete" : $"Day {CurrentDay} of {TotalDays}";

    /// <summary>
    /// Today's first goal and its progress, or an empty string if there is none. It excludes the deadline, which
    /// each surface shows as its own countdown.
    /// </summary>
    public string GoalLine => TodaysGoals.Count == 0
        ? string.Empty
        : $"{TodaysGoals[0].Title} · {TodaysGoals[0].Progress}/{TodaysGoals[0].Target}";

    /// <summary>
    /// The instruction for the current day. An unfinished goal comes first because it has a deadline. A claimable
    /// reward from any day comes next (see <see cref="ClaimableDays"/>). Otherwise the text says that today's goal is
    /// done.
    /// </summary>
    public string Instruction =>
        GoalsTotal == 0 || GoalsComplete < GoalsTotal ? "Complete today's goal before the day runs out."
      : ClaimableDays > 0                                 ? "Your reward is waiting below."
      :                                                 "Today's goal is done. The next one opens when the day turns.";

    /// <summary>
    /// The line under the headline: the number of claimable rewards if any, the claimed and missed counts after the
    /// event ends, and otherwise <see cref="GoalLine"/>.
    /// </summary>
    public string Summary =>
        ClaimableDays > 0 ? (ClaimableDays == 1 ? "1 reward ready" : $"{ClaimableDays} rewards ready")
      : HasEnded      ? $"{ClaimedDays} claimed · {MissedDays} missed"
      :                 GoalLine;
}

/// <summary>
/// One step of the daily reward's repeating cycle: its number, its coin reward, and whether it also awards a spin
/// token.
/// <para>
/// The daily reward feature decides the steps and their rewards. The shell only renders them
/// (docs/meta-shell.md, "How screens get state").
/// </para>
/// </summary>
public sealed record CycleStepView(int Step, long Coins, bool HasSpinToken);

/// <summary>
/// One goal of a first-week day or a mission list, with numeric progress toward a target.
/// <para>
/// <see cref="Id"/> identifies the goal in the owning feature's claim action. The shell passes it through without
/// interpreting it. It is empty for a goal that cannot be claimed.
/// </para>
/// </summary>
public sealed record GoalView(string Title, int Progress, int Target, RewardView Reward, bool IsClaimed, string Id = "")
{
    public bool   IsComplete  => Progress >= Target;
    public bool   IsClaimable => IsComplete && !IsClaimed;
    public double Fraction    => Target <= 0 ? 1.0 : Math.Clamp((double)Progress / Target, 0.0, 1.0);
}

/// <summary>
/// The missions feature. The hub card shows a summary and the missions screen shows the goals.
/// <para>
/// The hub, the badge and <see cref="NextActionPolicy"/> read the daily missions. The weekly missions and the
/// late-claim missions are shown on the missions screen. <see cref="ClaimableCount"/> also counts late-claim missions.
/// </para>
/// </summary>
public sealed record MissionsView(
    ActivityState            State,
    IReadOnlyList<GoalView>  DailyMissions,
    TimeSpan?                UntilDailyReset,
    IReadOnlyList<GoalView>? Weekly = null,
    TimeSpan?                UntilWeeklyReset = null,
    IReadOnlyList<GoalView>? LateClaim = null,
    TimeSpan?                UntilLateClaimEnds = null) : IFeatureView
{
    public MetaFeature Feature => MetaFeature.Missions;

    /// <summary>This week's missions, or an empty list if <c>Weekly</c> is null.</summary>
    public IReadOnlyList<GoalView> WeeklyMissions => Weekly ?? Array.Empty<GoalView>();

    /// <summary>Missions from the previous activation that are still claimable during its late-claim window.</summary>
    public IReadOnlyList<GoalView> LateClaimMissions => LateClaim ?? Array.Empty<GoalView>();

    public int DailyMissionsComplete  => DailyMissions.Count(m => m.IsComplete);
    public int DailyMissionsTotal     => DailyMissions.Count;

    /// <summary>
    /// The number of claimable daily and late-claim missions. Late-claim missions count so that the Events badge shows a
    /// reward that is about to expire.
    /// </summary>
    public int ClaimableCount => DailyMissions.Count(m => m.IsClaimable) + LateClaimMissions.Count(m => m.IsClaimable);

    public TimeSpan? UntilDeadline => UntilDailyReset;

    /// <summary>A short threshold, because daily missions reset every day.</summary>
    public TimeSpan EndingSoonWithin => TimeSpan.FromHours(2);

    /// <summary>
    /// The unfinished daily mission with the most progress, or null if none. Completed missions are excluded.
    /// </summary>
    public GoalView? Nearest => DailyMissions
        .Where(m => !m.IsComplete)
        .OrderByDescending(m => m.Fraction)
        .FirstOrDefault();

    public double? Progress => Nearest?.Fraction;
}

/// <summary>The spin wheel. It has its own spin animation, and its result uses the shared reward reveal.</summary>
/// <param name="BlockedBy">
/// The currency whose balance a wheel prize would push over its cap, or null if every prize fits. The screen uses
/// it to tell the player which currency to spend (<c>docs/spin-wheel.md</c>).
/// </param>
public sealed record SpinWheelView(
    ActivityState                   State,
    int                             SpinsAvailable,
    string                          PrizeTeaser,
    RewardView                      TopPrize,
    IReadOnlyList<WheelSectorView>? Wheel = null,
    IReadOnlyList<WheelOddsRow>?    Odds = null,
    SpinReceiptView?                PendingReceipt = null,
    CurrencyKind?                   BlockedBy = null) : IFeatureView
{
    public MetaFeature Feature => MetaFeature.SpinWheel;

    /// <summary>The sectors, clockwise from the marker, or an empty list if <c>Wheel</c> is null.</summary>
    public IReadOnlyList<WheelSectorView> Sectors => Wheel ?? Array.Empty<WheelSectorView>();

    /// <summary>
    /// The published odds, computed from the same sector table as the wheel, or an empty list if <c>Odds</c> is null.
    /// </summary>
    public IReadOnlyList<WheelOddsRow> PublishedOdds => Odds ?? Array.Empty<WheelOddsRow>();

    /// <summary>Whether a resolved spin result has not been shown yet.</summary>
    public bool HasPendingReceipt => PendingReceipt != null;

    /// <summary>
    /// Whether every wheel prize fits in the player's wallet. The server refuses a spin if any prize would not fit.
    /// <see cref="BlockedBy"/> names the currency that does not fit.
    /// </summary>
    public bool EveryPrizeFits => BlockedBy == null;

    /// <summary>Null, because the wheel has no deadline.</summary>
    public TimeSpan? UntilDeadline => null;
    public double?   Progress      => null;

    /// <summary>Zero, because the wheel has no deadline.</summary>
    public TimeSpan EndingSoonWithin => TimeSpan.Zero;
}

/// <summary>
/// One sector of the wheel. The wheel and the published odds are both computed from the same sector table
/// (<c>docs/spin-wheel.md</c>).
/// <para>
/// <see cref="Currency"/> is null for the blank sector, which spends the token and grants nothing.
/// </para>
/// </summary>
public sealed record WheelSectorView(int Index, CurrencyKind? Currency, long Amount, WheelTier Tier);

/// <summary>
/// One row of the published odds: a result and its percentage of the wheel. The blank sector has a row too.
/// </summary>
public sealed record WheelOddsRow(CurrencyKind? Currency, long Amount, int ChancePercent)
{
    public string Label => Currency is CurrencyKind currency ? Currencies.NameOf(currency, Amount) : "Nothing";
}

/// <summary>
/// A sector's tier, which selects its visual treatment. It is the shell's copy of the game config tier, so that
/// components do not depend on the game's enum.
/// </summary>
public enum WheelTier
{
    Common,
    Uncommon,
    Rare,
    Premium,
    SpinAgain,
    Nothing,
}

/// <summary>
/// A resolved spin the player has not seen. The reward is already in the wallet. The screen shows this result
/// before offering another spin.
/// </summary>
public sealed record SpinReceiptView(int SectorIndex, RewardView Reward, WheelTier Tier);

/// <summary>
/// A themed, time-limited event with a points target and one reward tier (<c>docs/weekly-event.md</c>).
/// <para>
/// It is scheduled by operators, so it has two countdowns: <see cref="UntilStart"/> while the event is in
/// preview, and <see cref="UntilEnd"/> while it runs. After scoring closes, <see cref="UntilEnd"/> is the time
/// left to claim the reward.
/// </para>
/// </summary>
public sealed record WeeklyEventView(
    ActivityState State,
    string        Theme,
    string        Tagline,
    string        PhaseLabel,
    long          Points,
    long          TargetPoints,
    TimeSpan?     UntilEnd,
    RewardView    Reward,
    bool          IsNewlyAvailable,
    TimeSpan?     UntilStart         = null,
    string        EventId            = "",
    bool          HasClaimableReward = false,
    bool          RewardClaimed      = false,
    bool          IsScoring          = true) : IFeatureView
{
    public MetaFeature Feature       => MetaFeature.WeeklyEvent;
    public TimeSpan?   UntilDeadline => UntilEnd;

    /// <summary>
    /// The event's last day. It is set here instead of using <see cref="Countdown.EndingSoonThreshold"/> so that
    /// changing the shell default does not change this feature.
    /// </summary>
    public TimeSpan EndingSoonWithin => TimeSpan.FromHours(24);

    public double? Progress => TargetPoints <= 0 ? null : Math.Clamp((double)Points / TargetPoints, 0.0, 1.0);
}

/// <summary>One row of a standings table. <see cref="IsBot"/> marks bot players, as the game table does.</summary>
public sealed record StandingRow(
    int    Rank,
    string DisplayName,
    long   Score,
    int    RankDelta,
    bool   IsSelf,
    bool   IsBot,
    string AvatarToken,
    string FrameToken,
    string NameEffectToken);

/// <summary>
/// One placement band of the season's reward table: a final rank of <see cref="MaxRank"/> or better earns
/// <see cref="Reward"/>. Bands are sorted by ascending <see cref="MaxRank"/>, so the first band that contains a
/// rank is the one that pays it.
/// </summary>
public sealed record PlacementBandView(int MaxRank, RewardView Reward)
{
    /// <summary>
    /// The reward for finishing at <paramref name="rank"/>, or <see cref="RewardView.Empty"/> if no band contains
    /// it. It uses the same first-match lookup as the shared code's <c>BandFor</c>, and every client surface that
    /// shows a rank's reward calls it.
    /// </summary>
    public static RewardView RewardFor(IReadOnlyList<PlacementBandView> bands, int rank)
    {
        if (rank <= 0)
            return RewardView.Empty;

        foreach (PlacementBandView band in bands)
        {
            if (rank <= band.MaxRank)
                return band.Reward;
        }

        return RewardView.Empty;
    }
}

/// <summary>One participation milestone of a tournament season.</summary>
public sealed record TournamentMilestoneView(
    int          Index,
    int          RequiredScoredMatches,
    RewardView   Reward,
    bool         IsReached,
    bool         IsClaimed)
{
    public bool IsClaimable => IsReached && !IsClaimed;
}

/// <summary>
/// Converts season indexes to the season numbers shown to players.
/// <para>
/// The SDK numbers league seasons from zero, and the client receives that index. Every surface that shows a
/// season number converts it here, so no screen shows "Season 0".
/// </para>
/// </summary>
public static class TournamentSeasons
{
    /// <summary>The player-facing number for a zero-based season index.</summary>
    public static int NumberOf(int seasonIndex) => seasonIndex + 1;
}

/// <summary>
/// The seasonal tournament: a time-limited points race within a group of players
/// (<c>docs/seasonal-tournament.md</c>). It has no promotion or relegation, so the group standings are the
/// competitive element. Standings include bots, marked as bots, so they are never empty.
/// </summary>
public sealed record TournamentView(
    ActivityState                       State,
    string                              Name,
    string                              PhaseLabel,

    /// <summary>
    /// The player-facing season number, already converted from the SDK's zero-based index with
    /// <see cref="TournamentSeasons.NumberOf"/>, or zero before the player joins a season. Screens show it as is.
    /// </summary>
    int                                 SeasonNumber,
    bool                                IsInSeason,
    TimeSpan?                           UntilStart,
    TimeSpan?                           UntilEnd,
    int                                 Rank,
    long                                Score,
    int                                 ScoredMatches,
    int                                 MatchCap,
    IReadOnlyList<StandingRow>          Standings,
    IReadOnlyList<TournamentMilestoneView> Milestones,
    /// <summary>
    /// The season's placement bands, sorted by ascending rank. Shown on the standings rows that earn them, and as a
    /// preview before standings exist.
    /// </summary>
    IReadOnlyList<PlacementBandView>    PlacementBands,
    bool                                HasClaimableReward,

    /// <summary>The player's final rank in the season whose reward is unclaimed, or zero if there is none.</summary>
    int                                 PendingRank,

    /// <summary>The unclaimed season reward, or empty if there is none.</summary>
    RewardView                          PendingReward,
    bool                                HasMaterialChange) : IFeatureView
{
    public MetaFeature Feature => MetaFeature.Tournament;

    /// <summary>The reward for finishing at <paramref name="rank"/>, or empty if no band contains it.</summary>
    public RewardView RewardFor(int rank) => PlacementBandView.RewardFor(PlacementBands, rank);

    /// <summary>The time until the season ends, or until it starts if it has not started.</summary>
    public TimeSpan? UntilDeadline => UntilEnd ?? UntilStart;

    /// <summary>
    /// The fraction of the scored-match cap that the player has used, or null if the player has not joined.
    /// </summary>
    public double? Progress => !IsInSeason || MatchCap <= 0 ? null : Math.Clamp((double)ScoredMatches / MatchCap, 0.0, 1.0);

    public TimeSpan EndingSoonWithin => Countdown.EndingSoonThreshold;

    /// <summary>Whether the player has reached the scored-match cap. Further matches are not scored.</summary>
    public bool IsCapReached => ScoredMatches >= MatchCap;

    /// <summary>The first claimable milestone, or null if none.</summary>
    public TournamentMilestoneView? NextClaimable
    {
        get
        {
            foreach (TournamentMilestoneView milestone in Milestones)
            {
                if (milestone.IsClaimable)
                    return milestone;
            }
            return null;
        }
    }
}
