using WebClient.Meta;
using WebClient.Meta.Fixtures;

namespace WebClient.Tests;

/// <summary>
/// Tests for <see cref="FeatureOrdering"/>, the Events hub sort: actionable features first, then the nearest
/// deadline, then the declaration order of <see cref="MetaFeature"/>. A feature with nothing to do stays in the
/// list, and ties sort the same way every time.
/// </summary>
[TestFixture]
public class FeatureOrderingTests
{
    private static readonly RewardView Nothing = RewardView.Empty;

    /// <summary>
    /// A spin wheel view. The wheel has no deadline, because its availability depends only on the token balance.
    /// </summary>
    private static SpinWheelView Spin(ActivityState state) =>
        new SpinWheelView(state, state == ActivityState.Actionable ? 1 : 0, "", Nothing);

    private static WeeklyEventView Weekly(ActivityState state, TimeSpan? remaining) =>
        new WeeklyEventView(state, "Weekly", "", "", 0, 1_000, remaining, Nothing, false);

    private static MissionsView Missions(ActivityState state, TimeSpan? reset) =>
        new MissionsView(state, Array.Empty<GoalView>(), reset);

    [Test]
    public void AnythingActionableSortsAboveEverythingThatIsNot()
    {
        IReadOnlyList<IFeatureView> sorted = FeatureOrdering.Sort(new IFeatureView[]
        {
            Weekly(ActivityState.InProgress, TimeSpan.FromMinutes(5)),
            Missions(ActivityState.Ready, TimeSpan.FromMinutes(1)),
            Spin(ActivityState.Actionable),
        });

        Assert.That(sorted[0].Feature, Is.EqualTo(MetaFeature.SpinWheel),
            "an actionable feature outranks a more urgent one that cannot be acted on");
    }

    [Test]
    public void AmongEquallyActionableFeaturesTheNearerDeadlineIsFirst()
    {
        IReadOnlyList<IFeatureView> sorted = FeatureOrdering.Sort(new IFeatureView[]
        {
            Weekly(ActivityState.InProgress, TimeSpan.FromHours(9)),
            Missions(ActivityState.InProgress, TimeSpan.FromHours(2)),
        });

        Assert.That(sorted[0].Feature, Is.EqualTo(MetaFeature.Missions));
    }

    /// <summary>A feature with no deadline sorts after every feature that has one.</summary>
    [Test]
    public void AFeatureWithNoDeadlineSortsAfterOnesThatHaveOne()
    {
        IReadOnlyList<IFeatureView> sorted = FeatureOrdering.Sort(new IFeatureView[]
        {
            Spin(ActivityState.Ready),
            Weekly(ActivityState.Ready, TimeSpan.FromDays(3)),
        });

        Assert.That(sorted[0].Feature, Is.EqualTo(MetaFeature.WeeklyEvent));
    }

    /// <summary>
    /// A tie is broken by <see cref="MetaFeature"/> declaration order regardless of input order, so cards do not
    /// swap places between renders.
    /// </summary>
    [Test]
    public void ADeadHeatIsBrokenStablyByTheFeatureOrder()
    {
        IFeatureView[] input =
        {
            Weekly(ActivityState.InProgress, TimeSpan.FromHours(3)),
            Missions(ActivityState.InProgress, TimeSpan.FromHours(3)),
        };

        IReadOnlyList<IFeatureView> first  = FeatureOrdering.Sort(input);
        IReadOnlyList<IFeatureView> again  = FeatureOrdering.Sort(input.Reverse());

        Assert.That(first.Select(v => v.Feature), Is.EqualTo(new[] { MetaFeature.Missions, MetaFeature.WeeklyEvent }),
            "missions are declared before the weekly event");
        Assert.That(again.Select(v => v.Feature), Is.EqualTo(first.Select(v => v.Feature)),
            "the input order must not change the result");
    }

    /// <summary>
    /// The sort never drops a feature, including one with nothing to claim.
    /// </summary>
    [Test]
    public void NoFeatureIsEverHiddenForHavingNothingToDo()
    {
        foreach (string scenario in MetaFixtures.Scenarios.Keys)
        {
            MetaSnapshot snapshot = MetaFixtures.Build(scenario, TimeSpan.Zero);
            IReadOnlyList<IFeatureView> sorted = FeatureOrdering.Sort(snapshot.EventsFeatures);

            Assert.That(sorted.Count, Is.EqualTo(snapshot.EventsFeatures.Count), scenario);
            Assert.That(sorted.Select(v => v.Feature), Is.EquivalentTo(snapshot.EventsFeatures.Select(v => v.Feature)), scenario);
        }
    }

    /// <summary>The Events hub carries exactly the features listed in the test.</summary>
    [Test]
    public void TheEventsHubCarriesItsFiveFeatures()
    {
        MetaSnapshot snapshot = MetaFixtures.Build(MetaFixtures.Default, TimeSpan.Zero);

        Assert.That(snapshot.EventsFeatures.Select(v => v.Feature), Is.EquivalentTo(new[]
        {
            MetaFeature.FirstWeekEvent, MetaFeature.DailyReward, MetaFeature.Missions,
            MetaFeature.SpinWheel, MetaFeature.WeeklyEvent,
        }));
    }
}
