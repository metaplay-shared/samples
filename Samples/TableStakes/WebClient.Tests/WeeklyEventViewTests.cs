using Game.Logic;
using Metaplay.Core;
using Metaplay.Core.LiveOpsEvent;
using WebClient.Meta;

namespace WebClient.Tests;

/// <summary>
/// Tests for <see cref="WeeklyEventViewBuilder"/>, which maps a <see cref="WeeklyEventOutlook"/> to the Weekly Event
/// card: the phase label, which countdown runs, and whether the card offers a claim.
/// <para>
/// The tests pass a <see cref="WeeklyEventOutlook"/> directly instead of a player model, because building a
/// player model needs Metaplay's type registry and this project runs without Metaplay core initialised.
/// <c>WeeklyEventTests</c> in shared code tests how the outlook is read from the model, and
/// <c>LiveServerWeeklyEventTests</c> tests the whole path.
/// </para>
/// </summary>
[TestFixture]
public class WeeklyEventViewTests
{
    private static MetaTime At(int day, int hour = 12) =>
        MetaTime.FromDateTime(new DateTime(2026, 9, day, hour, 0, 0, DateTimeKind.Utc));

    private static readonly MetaTime OpensAt     = At(7);
    private static readonly MetaTime ClosesAt    = At(14);
    private static readonly MetaTime ConcludesAt = At(16);

    private static WeeklyEventContent Content(int target = 100) =>
        new WeeklyEventContent("Trickster's Week", "Every trick counts.", target, 5,
            new Game.Logic.RewardBundle(CurrencyAmount.Coins(2000), CurrencyAmount.Gems(100)));

    private static WeeklyEventOutlook Outlook(
        LiveOpsEventPhase phase,
        int               points        = 0,
        bool              targetReached = false,
        bool              isClaimed     = false,
        int               target        = 100) =>
        new WeeklyEventOutlook(
            MetaGuid.FromTimeAndValue(new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc), 42),
            Content(target), phase, points, targetReached, isClaimed,
            hasSchedule: true, startsAt: OpensAt, endsAt: ClosesAt, concludesAt: ConcludesAt);

    /// <summary>
    /// With no week, the view is the Unavailable empty state with no countdown, and its tagline says why.
    /// </summary>
    [Test]
    public void APlayerHoldingNoWeekGetsTheDesignedEmptyStateWithNoClock()
    {
        WeeklyEventView view = WeeklyEventViewBuilder.Of(default, At(8));

        Assert.That(view.State, Is.EqualTo(ActivityState.Unavailable));
        Assert.That(view.UntilEnd, Is.Null);
        Assert.That(view.UntilStart, Is.Null);
        Assert.That(view.Tagline, Is.Not.Empty, "the empty state has to say why it is empty");
    }

    [Test]
    public void ANullStateIsTheSameEmptyStateRatherThanAThrow()
    {
        Assert.That(WeeklyEventViewBuilder.From(null!, null!, At(8)).State, Is.EqualTo(ActivityState.Unavailable));
    }

    /// <summary>
    /// A week in preview counts down to its start and has no scoring countdown. A card shows at most one
    /// countdown.
    /// </summary>
    [Test]
    public void APreviewedWeekCountsDownToItsStartAndIsNotScoring()
    {
        WeeklyEventView view = WeeklyEventViewBuilder.Of(Outlook(LiveOpsEventPhase.Preview), At(6));

        Assert.That(view.State, Is.EqualTo(ActivityState.Ready));
        Assert.That(view.PhaseLabel, Is.EqualTo("Starts soon"));
        Assert.That(view.UntilStart, Is.EqualTo(TimeSpan.FromHours(24)));
        Assert.That(view.UntilEnd, Is.Null);
        Assert.That(view.IsScoring, Is.False);
        Assert.That(view.TargetPoints, Is.EqualTo(100), "the target is shown before the week opens");
        Assert.That(view.Reward.Items, Is.Not.Empty, "so is the reward");
    }

    [Test]
    public void ARunningWeekCountsDownToTheEndOfScoring()
    {
        WeeklyEventView view = WeeklyEventViewBuilder.Of(Outlook(LiveOpsEventPhase.NormalActive, points: 40), At(13));

        Assert.That(view.State, Is.EqualTo(ActivityState.InProgress));
        Assert.That(view.PhaseLabel, Is.EqualTo("This week"));
        Assert.That(view.UntilEnd, Is.EqualTo(TimeSpan.FromHours(24)));
        Assert.That(view.UntilStart, Is.Null);
        Assert.That(view.Points, Is.EqualTo(40));
        Assert.That(view.Progress, Is.EqualTo(0.4).Within(0.001));
    }

    [Test]
    public void TheEndingSoonPhaseGetsItsOwnWord()
    {
        WeeklyEventView view = WeeklyEventViewBuilder.Of(Outlook(LiveOpsEventPhase.EndingSoon), At(13));

        Assert.That(view.PhaseLabel, Is.EqualTo("Final hours"));
        Assert.That(view.IsScoring, Is.True);
    }

    /// <summary>
    /// After scoring closes, the countdown is to the end of the late-claim window, because that is the deadline
    /// the player can still act on.
    /// </summary>
    [Test]
    public void InReviewTheClockIsTheLateClaimWindow()
    {
        WeeklyEventView view = WeeklyEventViewBuilder.Of(Outlook(LiveOpsEventPhase.Review, points: 104, targetReached: true), At(15));

        Assert.That(view.UntilEnd, Is.EqualTo(TimeSpan.FromHours(24)), "the countdown is to the event concluding");
        Assert.That(view.IsScoring, Is.False);
        Assert.That(view.HasClaimableReward, Is.True);
        Assert.That(view.State, Is.EqualTo(ActivityState.Actionable));
    }

    [Test]
    public void AFinishedWeekWithNothingOwedReadsAsCompleted()
    {
        WeeklyEventView view = WeeklyEventViewBuilder.Of(Outlook(LiveOpsEventPhase.Review, points: 40), At(15));

        Assert.That(view.State, Is.EqualTo(ActivityState.Completed));
        Assert.That(view.HasClaimableReward, Is.False);
    }

    [Test]
    public void ACrossedTargetIsActionableUntilItIsCollected()
    {
        WeeklyEventView earned = WeeklyEventViewBuilder.Of(
            Outlook(LiveOpsEventPhase.NormalActive, points: 104, targetReached: true), At(10));
        WeeklyEventView collected = WeeklyEventViewBuilder.Of(
            Outlook(LiveOpsEventPhase.NormalActive, points: 104, targetReached: true, isClaimed: true), At(10));

        Assert.That(earned.State, Is.EqualTo(ActivityState.Actionable));
        Assert.That(earned.HasClaimableReward, Is.True);

        Assert.That(collected.State, Is.EqualTo(ActivityState.InProgress));
        Assert.That(collected.HasClaimableReward, Is.False);
        Assert.That(collected.RewardClaimed, Is.True);
    }

    /// <summary>
    /// The claim action takes only the event id, so the view must carry the id for the Claim button to use.
    /// </summary>
    [Test]
    public void TheViewCarriesTheEventIdTheClaimIsAddressedTo()
    {
        WeeklyEventOutlook outlook = Outlook(LiveOpsEventPhase.NormalActive, points: 104, targetReached: true);

        Assert.That(WeeklyEventViewBuilder.Of(outlook, At(10)).EventId, Is.EqualTo(outlook.EventId.ToString()));
    }

    /// <summary>
    /// A countdown past its deadline is zero, never negative. This can happen because the client's clock and the
    /// phase the SDK last assigned are updated independently.
    /// </summary>
    [Test]
    public void AClockPastItsDeadlineIsZeroRatherThanNegative()
    {
        Assert.That(WeeklyEventViewBuilder.Of(Outlook(LiveOpsEventPhase.NormalActive), At(20)).UntilEnd, Is.EqualTo(TimeSpan.Zero));
    }

    /// <summary>
    /// A week with no authored theme shows the feature's title as its headline, and an empty reward.
    /// </summary>
    [Test]
    public void AWeekWithNoThemeFallsBackToTheFeaturesName()
    {
        WeeklyEventOutlook outlook = new WeeklyEventOutlook(
            MetaGuid.None, new WeeklyEventContent(null, null, 100, 5, null),
            LiveOpsEventPhase.NormalActive, 0, false, false, true, OpensAt, ClosesAt, ConcludesAt);

        WeeklyEventView view = WeeklyEventViewBuilder.Of(outlook, At(8));

        Assert.That(view.Theme, Is.EqualTo(WeeklyEventViewBuilder.Title));
        Assert.That(view.Reward, Is.EqualTo(RewardView.Empty));
    }

    /// <summary>
    /// The view never reports the week as newly available. A LiveOps event does not record that a player has seen
    /// it, so nothing would clear an "unseen" badge for the whole week. The weekly event badges only for a
    /// claimable reward (see <c>BadgePolicyTests</c>).
    /// </summary>
    [Test]
    public void AWeekIsNeverReportedAsNewlyAvailable()
    {
        Assert.That(WeeklyEventViewBuilder.Of(Outlook(LiveOpsEventPhase.NormalActive), At(8)).IsNewlyAvailable, Is.False);
        Assert.That(WeeklyEventViewBuilder.Of(Outlook(LiveOpsEventPhase.Preview), At(6)).IsNewlyAvailable, Is.False);
    }

    /// <summary>
    /// The weekly event's ending-soon window is its last day. Each feature declares its own window.
    /// </summary>
    [Test]
    public void TheWeeksOwnEndingSoonWindowIsItsLastDay()
    {
        WeeklyEventView view = WeeklyEventViewBuilder.Of(Outlook(LiveOpsEventPhase.NormalActive), At(8));

        Assert.That(view.EndingSoonWithin, Is.EqualTo(TimeSpan.FromHours(24)));
    }
}
