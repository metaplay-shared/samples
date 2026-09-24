using WebClient.Meta;
using WebClient.Meta.Fixtures;

namespace WebClient.Tests;

/// <summary>
/// Tests for scenario pins, the rule that decides what a meta screen shows: real state replaces fixture state,
/// except in the slices the <c>?meta=</c> scenario pins.
/// <para>
/// <c>MetaFixturesTests</c> checks what <c>MetaFixtures</c> builds. Whether a player sees it depends on which slices
/// <c>MetaStateService</c> replaces with real state, and that depends on the declared pins tested here.
/// </para>
/// </summary>
[TestFixture]
public class ScenarioPinTests
{
    /// <summary>
    /// The default scenario pins nothing, so a player who opens the app without <c>?meta=</c> sees real state in
    /// every slice that has a real feature behind it.
    /// </summary>
    [Test]
    public void TheDefaultScenarioClaimsNothing()
    {
        Assert.That(MetaFixtures.PinnedBy(MetaFixtures.Default), Is.Empty);
        Assert.That(MetaFixtures.PinnedSliceNames(MetaFixtures.Default), Is.Empty);
    }

    /// <summary>
    /// An unknown scenario name builds the default scenario and pins nothing, so a typo in the URL does not replace
    /// the player's real state with fixture state.
    /// </summary>
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("no-such-scenario")]
    public void AnUnknownScenarioClaimsNothing(string scenario)
    {
        Assert.That(MetaFixtures.PinnedBy(scenario), Is.Empty);
    }

    /// <summary>
    /// <see cref="MetaFixtures.PinnedBy"/> matches the name the same way the builder does: trimmed and case-
    /// insensitive.
    /// </summary>
    [TestCase("first-week-reward")]
    [TestCase("  First-Week-Reward  ")]
    public void TheClaimIsReadTheSameWayTheScenarioNameIs(string scenario)
    {
        Assert.That(MetaFixtures.PinnedBy(scenario), Does.Contain(FixtureSlice.FirstWeek));
    }

    /// <summary>Every scenario has a non-empty description and a non-null pin set.</summary>
    [Test]
    public void EveryScenarioSaysWhatItIsFor()
    {
        foreach ((string name, ScenarioInfo info) in MetaFixtures.Scenarios)
        {
            Assert.That(info.Description, Is.Not.Empty, name);
            Assert.That(info.Pinned, Is.Not.Null, name);
        }
    }

    /// <summary>
    /// A scenario that demonstrates one next-action kind still shows it once a session exists: putting a real
    /// player's highest-priority state into any slice the scenario does not pin leaves Home's next-action card on
    /// the same kind. The test asks <see cref="NextActionPolicy.Choose"/> instead of restating the priority order, so
    /// it also fails when the order changes under a scenario. The default scenario is not listed, because it pins
    /// nothing and shows the kind a real player with an unclaimed daily reward already sees.
    /// </summary>
    [TestCase("first-week", NextActionKind.FirstWeekGoal)]
    [TestCase("first-week-reward", NextActionKind.FirstWeekGoal)]
    [TestCase("missions", NextActionKind.MissionReward)]
    [TestCase("spin", NextActionKind.Spin)]
    [TestCase("weekly-reward", NextActionKind.WeeklyEventReward)]
    [TestCase("ending", NextActionKind.EndingSoon)]
    [TestCase("progress", NextActionKind.NearestProgress)]
    public void AnUnclaimedSliceCannotOutrankTheRungAScenarioShows(string scenario, NextActionKind expected)
    {
        MetaSnapshot snapshot = MetaFixtures.Build(scenario, TimeSpan.Zero);
        Assert.That(NextActionPolicy.Choose(snapshot).Kind, Is.EqualTo(expected));

        IReadOnlySet<FixtureSlice> pinned = MetaFixtures.PinnedBy(scenario);
        foreach ((FixtureSlice slice, MetaSnapshot loud) in WithEachSliceAtItsLoudest(snapshot))
        {
            if (pinned.Contains(slice))
                continue;

            Assert.That(NextActionPolicy.Choose(loud).Kind, Is.EqualTo(expected),
                $"{scenario} leaves {slice} to the player, and a player who has one there takes Home's card off the rung this scenario exists to show");
        }
    }

    /// <summary>
    /// A scenario that demonstrates a next-action kind pins the slice that kind's card is drawn from. Without the
    /// pin, the player's real state would replace the scenario's.
    /// <para>
    /// In <c>progress</c> the card comes from missions, the feature nearest to completion. That scenario also pins
    /// the weekly event, because a real weekly event reward has higher priority and would take the card.
    /// </para>
    /// </summary>
    [TestCase("first-week", FixtureSlice.FirstWeek)]
    [TestCase("first-week-reward", FixtureSlice.FirstWeek)]
    [TestCase("missions", FixtureSlice.Missions)]
    [TestCase("spin", FixtureSlice.SpinWheel)]
    [TestCase("weekly-reward", FixtureSlice.WeeklyEvent)]
    [TestCase("ending", FixtureSlice.WeeklyEvent)]
    [TestCase("progress", FixtureSlice.Missions)]
    public void ARungScenarioClaimsTheSliceItsCardIsDrawnFrom(string scenario, FixtureSlice slice)
    {
        Assert.That(MetaFixtures.PinnedBy(scenario), Does.Contain(slice));
    }

    /// <summary>
    /// A joined tournament season inside the ending-soon window, built by <see cref="ClosingSeason"/>, takes the card
    /// as <see cref="NextActionKind.EndingSoon"/>. No scenario shows this state, so this test confirms that the
    /// tournament state used by <see cref="WithEachSliceAtItsLoudest"/> has high enough priority to matter.
    /// </summary>
    [Test]
    public void TheLoudTournamentTakesTheCardAtItsOwnRung()
    {
        MetaSnapshot     quieter = MetaFixtures.Build("progress", TimeSpan.Zero);
        NextActionChoice chosen  = NextActionPolicy.Choose(quieter with { Tournament = ClosingSeason(quieter.Tournament) });

        Assert.That(chosen.Kind, Is.EqualTo(NextActionKind.EndingSoon));
        Assert.That(chosen.Feature, Is.EqualTo(MetaFeature.Tournament));
    }

    /// <summary>
    /// The <c>loading</c>, <c>offline</c> and <c>error</c> scenarios put every feature in that state, so they pin
    /// every feature slice. They do not pin identity, wallet or record, which are not features. Leaving identity
    /// unclaimed also lets a browser test detect a live session by the server-generated player name.
    /// </summary>
    [TestCase("loading")]
    [TestCase("offline")]
    [TestCase("error")]
    public void AnUnhealthyScenarioClaimsEveryFeatureAndNoneOfTheHud(string scenario)
    {
        IReadOnlySet<FixtureSlice> pinned = MetaFixtures.PinnedBy(scenario);

        foreach (FixtureSlice slice in new[] { FixtureSlice.DailyReward, FixtureSlice.FirstWeek, FixtureSlice.Missions,
                                            FixtureSlice.SpinWheel, FixtureSlice.Tournament, FixtureSlice.Shop,
                                            FixtureSlice.WeeklyEvent })
            Assert.That(pinned, Does.Contain(slice), $"{scenario} does not keep {slice}");

        Assert.That(pinned, Does.Not.Contain(FixtureSlice.Identity));
        Assert.That(pinned, Does.Not.Contain(FixtureSlice.Wallet));
        Assert.That(pinned, Does.Not.Contain(FixtureSlice.Record));
    }

    /// <summary>
    /// The <c>fresh</c> scenario pins every slice, including identity, wallet and record, because a zeroed record
    /// next to a real player's balances would show neither a new player nor the real one.
    /// </summary>
    [Test]
    public void TheEmptyStatesScenarioClaimsEverySlice()
    {
        IReadOnlySet<FixtureSlice> pinned = MetaFixtures.PinnedBy("fresh");

        foreach (FixtureSlice slice in Enum.GetValues<FixtureSlice>())
            Assert.That(pinned, Does.Contain(slice), $"fresh does not keep {slice}");
    }

    /// <summary>
    /// The <c>expired</c> scenario pins the first-week, tournament and weekly event slices it shows as expired. A
    /// real player reaches these states only after the feature's time window ends, so the scenario is the practical
    /// way to see them.
    /// </summary>
    [Test]
    public void TheLapsedScenarioKeepsTheWindowsThatClosed()
    {
        IReadOnlySet<FixtureSlice> pinned = MetaFixtures.PinnedBy("expired");

        Assert.That(pinned, Does.Contain(FixtureSlice.FirstWeek));
        Assert.That(pinned, Does.Contain(FixtureSlice.Tournament));
        Assert.That(pinned, Does.Contain(FixtureSlice.WeeklyEvent),
            "the weekly event is half of what this scenario draws, so a session would overwrite it");
    }

    /// <summary>
    /// A scenario must also pin slices whose real state would contradict it. The <c>claimed</c> scenario shows that
    /// everything is done for today, so a real claimable weekly reward would add a badge and a Claim button to that
    /// screen unless the scenario pins the weekly event slice.
    /// </summary>
    [Test]
    public void TheEverythingDoneScenarioSilencesEverythingThatCouldStillBeClaimed()
    {
        MetaSnapshot               done   = MetaFixtures.Build("claimed", TimeSpan.Zero);
        IReadOnlySet<FixtureSlice> pinned = MetaFixtures.PinnedBy("claimed");

        Assert.That(BadgePolicy.ShouldBadge(BadgeTarget.Events, done, SeenState.Nothing), Is.False,
            "the scenario itself puts something collectable on the Events tab");

        // The scenario only promises that nothing on Events is collectable. An ending tournament season badges
        // Compete, not Events, so it does not break the promise.
        foreach ((FixtureSlice slice, MetaSnapshot loud) in WithEachSliceAtItsLoudest(done))
        {
            if (pinned.Contains(slice))
                continue;

            Assert.That(BadgePolicy.ShouldBadge(BadgeTarget.Events, loud, SeenState.Nothing), Is.False,
                $"claimed leaves {slice} to the player, and a player who has one there is not 'done for today'");
        }
    }

    /// <summary>
    /// A scenario that shows feature states pins exactly the slices whose state differs from the default scenario.
    /// The pin, not the state value, decides whether fixture state is kept, so a scenario showing Ready, Expired or
    /// Unavailable must still pin it. A missing pin lets real state replace the scenario's state once a session
    /// exists. An extra pin shows a
    /// live player fixture state on a screen the scenario does not need. Scenarios that demonstrate a next-action
    /// kind pin extra slices to keep higher-priority real state off Home's card, and none of these do.
    /// </summary>
    [TestCase("daily-closed", "dailyreward")]
    [TestCase("daily-ended", "dailyreward")]
    [TestCase("unpublished", "dailyreward spinwheel")]
    public void AStateScenarioClaimsExactlyTheSurfacesItDrawsDifferently(string scenario, string expected)
    {
        Assert.That(MetaFixtures.PinnedSliceNames(scenario), Is.EqualTo(expected));

        MetaSnapshot               authored = MetaFixtures.Build(scenario, TimeSpan.Zero);
        MetaSnapshot               landing  = MetaFixtures.Build(MetaFixtures.Default, TimeSpan.Zero);
        IReadOnlySet<FixtureSlice> pinned   = MetaFixtures.PinnedBy(scenario);

        foreach ((FixtureSlice slice, Func<MetaSnapshot, ActivityState> readState) in StateOfSlice)
        {
            if (readState(authored) != readState(landing))
                Assert.That(pinned, Does.Contain(slice),
                    $"{scenario} draws {slice} in a state the landing screen does not, and a slice it does not pin is the player's own");
        }
    }

    /// <summary>
    /// Reads the activity state of each slice whose whole view real state replaces when the slice is unclaimed.
    /// <para>
    /// <see cref="AStateScenarioClaimsExactlyTheSurfacesItDrawsDifferently"/> compares only these states. A scenario
    /// that differs from the default only in another field, such as a teaser or a count, is not checked.
    /// </para>
    /// </summary>
    private static readonly IReadOnlyDictionary<FixtureSlice, Func<MetaSnapshot, ActivityState>> StateOfSlice =
        new Dictionary<FixtureSlice, Func<MetaSnapshot, ActivityState>>
        {
            [FixtureSlice.DailyReward] = snapshot => snapshot.DailyReward.State,
            [FixtureSlice.FirstWeek]   = snapshot => snapshot.FirstWeek.State,
            [FixtureSlice.Missions]    = snapshot => snapshot.Missions.State,
            [FixtureSlice.SpinWheel]   = snapshot => snapshot.SpinWheel.State,
            [FixtureSlice.Tournament]  = snapshot => snapshot.Tournament.State,
            [FixtureSlice.WeeklyEvent] = snapshot => snapshot.WeeklyEvent.State,
        };

    /// <summary>
    /// <see cref="MetaFixtures.PinnedSliceNames"/> returns lowercase slice names separated by spaces, in enum
    /// order. The shell writes this string into a marker element that browser tests read to tell fixture state from
    /// real state.
    /// </summary>
    [Test]
    public void TheClaimIsReportedAsStableNames()
    {
        Assert.That(MetaFixtures.PinnedSliceNames("first-week-reward"), Is.EqualTo("dailyreward firstweek"));
        Assert.That(MetaFixtures.PinnedSliceNames("missions"), Is.EqualTo("dailyreward firstweek missions"));

        // Enum order, not declaration order, so two scenarios pinning the same slices report the same string.
        Assert.That(MetaFixtures.PinnedSliceNames("spin"), Is.EqualTo(MetaFixtures.PinnedSliceNames("spin-pending")));
    }

    /// <summary>
    /// Returns <paramref name="snapshot"/> once per slice that <see cref="NextActionPolicy"/> reads, each time with
    /// that slice replaced by the highest-priority state a real player can have in it. Most states come from the
    /// scenario that demonstrates the slice's next-action kind, and
    /// <see cref="AnUnclaimedSliceCannotOutrankTheRungAScenarioShows"/> checks that each takes the card. No scenario
    /// shows a tournament in the ending-soon window, so <see cref="ClosingSeason"/> builds that state and
    /// <see cref="TheLoudTournamentTakesTheCardAtItsOwnRung"/> checks it.
    /// </summary>
    private static IEnumerable<(FixtureSlice Slice, MetaSnapshot Snapshot)> WithEachSliceAtItsLoudest(MetaSnapshot snapshot)
    {
        yield return (FixtureSlice.FirstWeek,   snapshot with { FirstWeek   = BuildScenario("first-week").FirstWeek });
        yield return (FixtureSlice.DailyReward, snapshot with { DailyReward = BuildScenario(MetaFixtures.Default).DailyReward });
        yield return (FixtureSlice.Missions,    snapshot with { Missions    = BuildScenario("missions").Missions });
        yield return (FixtureSlice.SpinWheel,   snapshot with { SpinWheel   = BuildScenario("spin").SpinWheel });
        yield return (FixtureSlice.WeeklyEvent, snapshot with { WeeklyEvent = BuildScenario("weekly-reward").WeeklyEvent });
        yield return (FixtureSlice.Tournament,  snapshot with { Tournament  = ClosingSeason(snapshot.Tournament) });
    }

    private static MetaSnapshot BuildScenario(string scenario) => MetaFixtures.Build(scenario, TimeSpan.Zero);

    /// <summary>
    /// Returns <paramref name="tournament"/> as a joined, running season inside the ending-soon window.
    /// </summary>
    private static TournamentView ClosingSeason(TournamentView tournament) => tournament with
    {
        State      = ActivityState.InProgress,
        IsInSeason = true,
        UntilStart = null,
        UntilEnd   = TimeSpan.FromMinutes(30),
    };
}
