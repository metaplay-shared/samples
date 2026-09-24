namespace WebClient.Meta;

/// <summary>
/// The rule in <see cref="NextActionPolicy"/> that chose the Next-up card. The card's text and art depend on it.
/// </summary>
public enum NextActionKind
{
    FirstWeekGoal,
    DailyReward,
    MissionReward,
    Spin,
    WeeklyEventReward,
    EndingSoon,
    NearestProgress,
    PlayInvitation,
}

/// <summary>
/// The Next-up card chosen for Home by <see cref="NextActionPolicy"/>. The component turns it into text.
/// </summary>
public sealed record NextActionChoice(
    NextActionKind Kind,
    MetaFeature    Feature,
    TimeSpan?      UntilExpiry,
    double?        Progress)
{
    /// <summary>The route the card opens, or null for <see cref="MetaFeature.Play"/>, where the card starts
    /// matchmaking instead.</summary>
    public string? Route => MetaRoutes.For(Feature);
}

/// <summary>
/// Chooses Home's Next-up card from a <see cref="MetaSnapshot"/> by checking a fixed list of rules ("rungs") in
/// priority order. It is a pure function so that unit tests can check it.
/// <para>
/// <see cref="Choose"/> never returns null: the last rung is <see cref="NextActionKind.PlayInvitation"/>.
/// </para>
/// </summary>
public static class NextActionPolicy
{
    public static NextActionChoice Choose(MetaSnapshot snapshot)
    {
        // 1. A claimable first-week goal or reward, or a first-week day whose unfinished goals are ending soon.
        FirstWeekView firstWeek = snapshot.FirstWeek;
        if (firstWeek.NeedsAttention)
            return new NextActionChoice(NextActionKind.FirstWeekGoal, MetaFeature.FirstWeekEvent,
                firstWeek.UntilDeadline, firstWeek.Progress);

        // 2. Today's login reward is claimable.
        DailyRewardView daily = snapshot.DailyReward;
        if (daily.State.IsLive() && daily.HasClaimableReward)
            return new NextActionChoice(NextActionKind.DailyReward, MetaFeature.DailyReward, null, null);

        // 3. A mission that is finished but not collected.
        MissionsView missions = snapshot.Missions;
        if (missions.State.IsLive() && missions.ClaimableCount > 0)
            return new NextActionChoice(NextActionKind.MissionReward, MetaFeature.Missions,
                missions.UntilDailyReset, null);

        // 4. A spin token to use, or a paid spin result the player has not seen.
        SpinWheelView spin = snapshot.SpinWheel;
        if (spin.State.IsLive() && (spin.SpinsAvailable > 0 || spin.HasPendingReceipt))
            return new NextActionChoice(NextActionKind.Spin, MetaFeature.SpinWheel, null, null);

        // 5. A weekly event whose target is reached and whose reward is not claimed.
        //
        //    Rungs 6 and 7 exclude progress of 1.0, so without this rung a claimable weekly reward would match
        //    nothing and Home would show the Play invitation. It is below rungs 1 to 4 because the weekly
        //    reward stays claimable for longer than a first-week day or a daily reward.
        WeeklyEventView weekly = snapshot.WeeklyEvent;
        if (weekly.State.IsLive() && weekly.HasClaimableReward)
            return new NextActionChoice(NextActionKind.WeeklyEventReward, MetaFeature.WeeklyEvent,
                weekly.UntilEnd, weekly.Progress);

        // 6. A timed activity that is ending soon. The nearest deadline wins, and ties fall back to the
        //    MetaFeature order.
        //
        //    Progress of 1.0 is excluded so that a player who already reached the target is not urged to hurry.
        //    For example, a weekly event whose reward is claimed keeps its countdown until scoring ends. Because
        //    of this exclusion, rungs 5 and 6 never match the same state, so their relative order does not
        //    matter.
        IFeatureView? endingSoon = Candidates(snapshot)
            .Where(v => v.UntilDeadline is TimeSpan t &&
                        Countdown.PhaseOf(t, v.EndingSoonWithin) == CountdownPhase.EndingSoon)
            .Where(v => v.Progress is not double done || done < 1.0)
            .OrderBy(v => v.UntilDeadline!.Value)
            .ThenBy(v => (int)v.Feature)
            .FirstOrDefault();

        if (endingSoon != null)
            return new NextActionChoice(NextActionKind.EndingSoon, endingSoon.Feature,
                endingSoon.UntilDeadline, endingSoon.Progress);

        // 7. The feature with the most progress. Progress of 1.0 is excluded because it offers nothing to do.
        IFeatureView? nearest = Candidates(snapshot)
            .Where(v => v.Progress is double p && p > 0.0 && p < 1.0)
            .OrderByDescending(v => v.Progress!.Value)
            .ThenBy(v => (int)v.Feature)
            .FirstOrDefault();

        if (nearest != null)
            return new NextActionChoice(NextActionKind.NearestProgress, nearest.Feature,
                nearest.UntilDeadline, nearest.Progress);

        // 8. When no other rung matches, invite the player to play.
        return new NextActionChoice(NextActionKind.PlayInvitation, MetaFeature.Play, null, null);
    }

    /// <summary>
    /// The features that rungs 6 and 7 can choose from, in <see cref="MetaFeature"/> order. Features that are not
    /// <see cref="ActivityStates.IsLive"/> are excluded so that placeholder data never picks the Home card.
    /// <para>
    /// <see cref="ActivityState.Completed"/> is excluded as well as <see cref="ActivityState.Expired"/> and
    /// <see cref="ActivityState.Unavailable"/>, because a completed feature can still have a countdown. After a
    /// weekly event's scoring closes, its countdown shows the claim window. Without this exclusion, a player who
    /// missed the target would be urged to score in a week that no longer scores.
    /// </para>
    /// </summary>
    private static IEnumerable<IFeatureView> Candidates(MetaSnapshot snapshot)
    {
        IFeatureView[] all =
        {
            snapshot.FirstWeek, snapshot.DailyReward, snapshot.Missions, snapshot.SpinWheel,
            snapshot.WeeklyEvent, snapshot.Tournament,
        };

        return all.Where(v => v.State.IsLive() &&
                              v.State != ActivityState.Expired &&
                              v.State != ActivityState.Unavailable &&
                              v.State != ActivityState.Completed);
    }
}
