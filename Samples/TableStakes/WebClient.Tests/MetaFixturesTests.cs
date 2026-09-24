using WebClient.Meta;
using WebClient.Meta.Fixtures;

namespace WebClient.Tests;

/// <summary>
/// Tests for <see cref="MetaFixtures"/>. The main check is that the scenarios together reach every next-action kind
/// and every activity state, so every Home card and state can be seen rendered in a browser.
/// </summary>
[TestFixture]
public class MetaFixturesTests
{
    /// <summary>An unknown scenario name builds the default scenario.</summary>
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("no-such-scenario")]
    public void AnUnknownScenarioFallsBackToTheDefault(string scenario)
    {
        MetaSnapshot fallback = MetaFixtures.Build(scenario, TimeSpan.Zero);
        MetaSnapshot expected = MetaFixtures.Build(MetaFixtures.Default, TimeSpan.Zero);

        // Compare fields rather than whole records, because MetaSnapshot holds arrays and record equality
        // compares arrays by reference.
        Assert.That(fallback.Wallet, Is.EqualTo(expected.Wallet));
        Assert.That(fallback.Identity, Is.EqualTo(expected.Identity));
        Assert.That(NextActionPolicy.Choose(fallback).Kind, Is.EqualTo(NextActionPolicy.Choose(expected).Kind));

        Assert.That(MetaFixtures.IsKnown(scenario), Is.False);
    }

    [Test]
    public void TheScenarioNamesAreTheOnesTheBuilderKnows()
    {
        foreach (string scenario in MetaFixtures.Scenarios.Keys)
            Assert.That(MetaFixtures.IsKnown(scenario), Is.True, scenario);
    }

    /// <summary>
    /// Each next-action kind has a scenario for which <see cref="NextActionPolicy.Choose"/> returns that kind.
    /// </summary>
    [TestCase("first-week", NextActionKind.FirstWeekGoal)]
    [TestCase("default",    NextActionKind.DailyReward)]
    [TestCase("missions",   NextActionKind.MissionReward)]
    [TestCase("spin",       NextActionKind.Spin)]
    [TestCase("ending",     NextActionKind.EndingSoon)]
    [TestCase("progress",   NextActionKind.NearestProgress)]
    public void EachRungHasAScenarioThatReachesIt(string scenario, NextActionKind expected)
    {
        MetaSnapshot snapshot = MetaFixtures.Build(scenario, TimeSpan.Zero);

        Assert.That(NextActionPolicy.Choose(snapshot).Kind, Is.EqualTo(expected));
    }

    /// <summary>
    /// The default scenario matches the Home screen design, which shows the daily reward as the next-action card.
    /// An urgent first-week day would outrank it, so that state has its own scenario.
    /// </summary>
    [Test]
    public void TheDefaultScenarioIsTheApprovedHomeScreen()
    {
        MetaSnapshot snapshot = MetaFixtures.Build(MetaFixtures.Default, TimeSpan.Zero);

        Assert.That(NextActionPolicy.Choose(snapshot).Kind, Is.EqualTo(NextActionKind.DailyReward));
        Assert.That(snapshot.Wallet, Is.EqualTo(new WalletView(24_350, 1_250, 8)));
        Assert.That(snapshot.Identity.DisplayName, Is.EqualTo("Avery"));
        Assert.That(snapshot.Tournament.Name, Is.EqualTo("Seasonal Tournament"));
        Assert.That(snapshot.Tournament.Rank, Is.EqualTo(4));
    }

    /// <summary>
    /// Every activity state, including Loading, Offline and Error, is shown by some scenario that keeps showing it
    /// once a session exists.
    /// <para>
    /// A scenario that does not pin a slice has that slice replaced by the player's real state when the session
    /// connects, so a state it builds is never seen in a browser. <see cref="SurvivesASession"/> filters those out.
    /// </para>
    /// </summary>
    [Test]
    public void EveryActivityStateIsReachedBySomeScenarioTheShellKeeps()
    {
        HashSet<ActivityState> seen = new HashSet<ActivityState>();

        foreach (string scenario in MetaFixtures.Scenarios.Keys)
        {
            MetaSnapshot s = MetaFixtures.Build(scenario, TimeSpan.Zero);
            foreach (IFeatureView view in AllViews(s))
            {
                if (SurvivesASession(scenario, view.Feature))
                    seen.Add(view.State);
            }
        }

        foreach (ActivityState state in Enum.GetValues<ActivityState>())
            Assert.That(seen, Does.Contain(state), $"no scenario the shell keeps ever puts a surface in {state}");
    }

    /// <summary>
    /// Every state the daily reward and the spin wheel can be in is shown by some scenario that pins that feature's
    /// slice. <see cref="EveryActivityStateIsReachedBySomeScenarioTheShellKeeps"/> passes when any feature reaches a
    /// state, and this test checks each feature separately.
    /// </summary>
    [TestCaseSource(nameof(SurfaceVocabularies))]
    public void EveryStateASurfaceCanBeInIsDrawnBySomeScenarioTheShellKeeps(MetaFeature feature, ActivityState[] vocabulary)
    {
        HashSet<ActivityState> seen = new HashSet<ActivityState>();

        foreach (string scenario in MetaFixtures.Scenarios.Keys)
        {
            if (!SurvivesASession(scenario, feature))
                continue;

            seen.Add(ViewOf(MetaFixtures.Build(scenario, TimeSpan.Zero), feature).State);
        }

        foreach (ActivityState state in vocabulary)
            Assert.That(seen, Does.Contain(state), $"no scenario the shell keeps ever puts {feature} in {state}");
    }

    /// <summary>
    /// No scenario puts the daily reward or the spin wheel in a state that feature cannot produce, so every fixture
    /// shows a screen a real player could see.
    /// </summary>
    [TestCaseSource(nameof(SurfaceVocabularies))]
    public void NoScenarioPutsASurfaceInAStateItsFeatureDoesNotHave(MetaFeature feature, ActivityState[] vocabulary)
    {
        foreach (string scenario in MetaFixtures.Scenarios.Keys)
        {
            ActivityState state = ViewOf(MetaFixtures.Build(scenario, TimeSpan.Zero), feature).State;

            Assert.That(vocabulary, Does.Contain(state),
                $"{scenario} draws {feature} in {state}, which is not one of the states that surface has");
        }
    }

    /// <summary>
    /// The states the daily reward and the spin wheel can each be in. The daily reward is never InProgress, because
    /// a login either opens a reward or does not. It is Ready when no activation is open (<c>NoActivation</c>) and
    /// Expired when a finite schedule has no activations left. The spin wheel is never InProgress, Completed or
    /// Expired, because availability depends only on the token balance and the wheel has no deadline
    /// (<c>docs/spin-wheel.md</c>). Both are Unavailable when the published config is unusable
    /// (<c>ConfigUnavailable</c>).
    /// </summary>
    private static IEnumerable<TestCaseData> SurfaceVocabularies()
    {
        yield return new TestCaseData(MetaFeature.DailyReward, new[]
        {
            ActivityState.Loading, ActivityState.Offline, ActivityState.Error,
            ActivityState.Actionable, ActivityState.Completed,
            ActivityState.Ready, ActivityState.Expired, ActivityState.Unavailable,
        }).SetName("TheDailyRewardsStates");

        yield return new TestCaseData(MetaFeature.SpinWheel, new[]
        {
            ActivityState.Loading, ActivityState.Offline, ActivityState.Error,
            ActivityState.Actionable, ActivityState.Ready, ActivityState.Unavailable,
        }).SetName("TheSpinWheelsStates");
    }

    /// <summary>
    /// The <c>daily-closed</c> scenario shows a schedule with no activation open yet, and <c>daily-ended</c> a schedule
    /// with no activations left. The real daily reward view produces only Actionable or Completed, so these scenarios
    /// are the only way to see the other two states rendered.
    /// </summary>
    [Test]
    public void TheDailyRewardsScheduleHasAScenarioForEachOfItsClosedEnds()
    {
        MetaSnapshot closed = MetaFixtures.Build("daily-closed", TimeSpan.Zero);

        Assert.That(closed.DailyReward.State, Is.EqualTo(ActivityState.Ready));
        Assert.That(closed.DailyReward.HasClaimableReward, Is.False);
        Assert.That(closed.DailyReward.StreakDays, Is.GreaterThan(0), "nothing about the streak has lapsed");
        Assert.That(closed.DailyReward.UntilNextAvailable, Is.Not.Null,
            "an activation still to come is a clock, and the card draws it");
        Assert.That(NextActionPolicy.Choose(closed).Kind, Is.Not.EqualTo(NextActionKind.DailyReward),
            "Home must not offer a claim the schedule has not opened");

        MetaSnapshot ended = MetaFixtures.Build("daily-ended", TimeSpan.Zero);

        Assert.That(ended.DailyReward.State, Is.EqualTo(ActivityState.Expired));
        Assert.That(ended.DailyReward.HasClaimableReward, Is.False);
        Assert.That(ended.DailyReward.UntilNextAvailable, Is.Null,
            "nothing opens again, so there is no clock to draw — a surface with no deadline is not one at zero");
        Assert.That(NextActionPolicy.Choose(ended).Kind, Is.Not.EqualTo(NextActionKind.DailyReward));
    }

    /// <summary>
    /// The <c>unpublished</c> scenario models a config with neither the daily reward cycle nor the spin wheel table.
    /// Both features are Unavailable and show no content from the missing config.
    /// </summary>
    [Test]
    public void TheUnpublishedScenarioDrawsNeitherTableItDoesNotHave()
    {
        MetaSnapshot snapshot = MetaFixtures.Build("unpublished", TimeSpan.Zero);

        Assert.That(snapshot.DailyReward.State, Is.EqualTo(ActivityState.Unavailable));
        Assert.That(snapshot.DailyReward.Cycle, Is.Empty, "there is no published cycle to draw a rail from");
        Assert.That(snapshot.DailyReward.HasClaimableReward, Is.False);

        Assert.That(snapshot.SpinWheel.State, Is.EqualTo(ActivityState.Unavailable));
        Assert.That(snapshot.SpinWheel.Sectors, Is.Empty);
        Assert.That(snapshot.SpinWheel.PublishedOdds, Is.Empty);
        Assert.That(snapshot.SpinWheel.PrizeTeaser, Is.Empty,
            "the teaser is a reading of the table, so a missing table has none to offer");

        // The badge and the card's control check for a token, not for the wheel's state, so a token here would
        // direct the player to a wheel that cannot be spun.
        Assert.That(snapshot.SpinWheel.SpinsAvailable, Is.Zero);
        Assert.That(snapshot.SpinWheel.HasPendingReceipt, Is.False);
    }

    /// <summary>
    /// Every <see cref="FirstWeekDayState"/> is shown by some scenario that pins the first-week slice, so it stays
    /// on screen once a session exists. The reward-ready state matters most, because the Home card, the Events
    /// card's Claim button and the Events badge all depend on it.
    /// </summary>
    [Test]
    public void EveryFirstWeekDayStateIsReachedBySomeScenarioTheShellKeeps()
    {
        HashSet<FirstWeekDayState> seen = new HashSet<FirstWeekDayState>();

        foreach (string scenario in MetaFixtures.Scenarios.Keys)
        {
            if (!MetaFixtures.PinnedBy(scenario).Contains(FixtureSlice.FirstWeek))
                continue;

            foreach (FirstWeekDayView day in MetaFixtures.Build(scenario, TimeSpan.Zero).FirstWeek.Days)
                seen.Add(day.State);
        }

        foreach (FirstWeekDayState state in Enum.GetValues<FirstWeekDayState>())
            Assert.That(seen, Does.Contain(state), $"no scenario the shell keeps ever puts a first-week day in {state}");
    }

    /// <summary>
    /// The <c>first-week-reward</c> scenario has an unclaimed reward from an earlier day, which puts the first-week
    /// card on Home with a Claim button and badges the Events tab.
    /// </summary>
    [Test]
    public void AScenarioOwesAFirstWeekRewardFromAnEarlierDay()
    {
        MetaSnapshot snapshot = MetaFixtures.Build("first-week-reward", TimeSpan.Zero);

        Assert.That(MetaFixtures.PinnedBy("first-week-reward"), Does.Contain(FixtureSlice.FirstWeek),
            "the scenario exists to put these tiles on a screen, and a slice it does not pin is the player's own");

        Assert.That(snapshot.FirstWeek.ClaimableDays, Is.EqualTo(1));
        Assert.That(snapshot.FirstWeek.Days.Single(d => d.IsClaimable).Day, Is.LessThan(snapshot.FirstWeek.CurrentDay),
            "the point is a reward that outlived the day it was earned on");
        Assert.That(NextActionPolicy.Choose(snapshot).Kind, Is.EqualTo(NextActionKind.FirstWeekGoal));
        Assert.That(BadgePolicy.ShouldBadge(BadgeTarget.Events, snapshot, SeenState.Nothing), Is.True);
    }

    /// <summary>
    /// The daily reward and the first-week event keep separate state. Pinning the login reward does not complete
    /// the first-week day. <see cref="MetaRoutesTests"/> checks that they have separate routes.
    /// </summary>
    [Test]
    public void TheDailyRewardAndTheFirstWeekEventAreSeparateState()
    {
        MetaSnapshot snapshot = MetaFixtures.Build("first-week", TimeSpan.Zero);

        Assert.That(snapshot.DailyReward.HasClaimableReward, Is.False, "the login reward has been taken");
        Assert.That(snapshot.FirstWeek.GoalsComplete, Is.LessThan(snapshot.FirstWeek.GoalsTotal),
            "and the first-week day is still unfinished, because logging in did not finish it");
    }

    /// <summary>
    /// In every scenario the tournament standings are non-empty and contain both the player and at least one bot.
    /// </summary>
    [Test]
    public void CompetitiveStandingsAreNeverEmptyAndAlwaysContainThePlayer()
    {
        foreach (string scenario in MetaFixtures.Scenarios.Keys)
        {
            MetaSnapshot s = MetaFixtures.Build(scenario, TimeSpan.Zero);

            Assert.That(s.Tournament.Standings, Is.Not.Empty, $"{scenario}: empty tournament standings");
            Assert.That(s.Tournament.Standings.Any(r => r.IsSelf), Is.True, $"{scenario}: the player is not in the tournament standings");
            Assert.That(s.Tournament.Standings.Any(r => r.IsBot), Is.True, $"{scenario}: the tournament standings name no computer players");
        }
    }

    /// <summary>Deadlines run down as the session goes on, and none of them goes negative.</summary>
    [Test]
    public void DeadlinesRunDownAndStopAtZero()
    {
        MetaSnapshot start = MetaFixtures.Build("first-week", TimeSpan.Zero);
        MetaSnapshot later = MetaFixtures.Build("first-week", TimeSpan.FromMinutes(10));

        Assert.That(later.FirstWeek.UntilDayExpires!.Value, Is.LessThan(start.FirstWeek.UntilDayExpires!.Value));

        MetaSnapshot longAfter = MetaFixtures.Build("first-week", TimeSpan.FromDays(30));
        foreach (IFeatureView view in AllViews(longAfter))
        {
            if (view.UntilDeadline is TimeSpan left)
                Assert.That(left, Is.GreaterThanOrEqualTo(TimeSpan.Zero), $"{view.Feature} ran past zero");
        }
    }

    /// <summary>
    /// Every demo purchase shows demo text as its price, so it cannot be mistaken for a real purchase.
    /// </summary>
    [Test]
    public void EveryRealMoneyPriceIsMarkedAsADemo()
    {
        ShopView shop = MetaFixtures.Build(MetaFixtures.Default, TimeSpan.Zero).Shop;

        foreach (OfferView offer in shop.All())
        {
            if (offer.Price.Kind == OfferPriceKind.Demo)
                Assert.That(offer.Price.DemoText, Is.Not.Empty, $"{offer.Id} is a demo purchase with nothing to show as its price");
        }

        Assert.That(shop.All().Any(o => o.Price.Kind == OfferPriceKind.Demo), Is.True);
        Assert.That(shop.All().Any(o => o.Price.Kind == OfferPriceKind.Currency), Is.True,
            "the shop must demonstrate both an in-game-currency spend and a demo purchase");
    }

    /// <summary>Some scenario has an offer in each of Available, SoldOut and Locked.</summary>
    [Test]
    public void TheShopDemonstratesItsUnavailableStates()
    {
        IEnumerable<OfferView> offers = MetaFixtures.Scenarios.Keys
            .SelectMany(s => MetaFixtures.Build(s, TimeSpan.Zero).Shop.All());

        HashSet<OfferAvailability> seen = offers.Select(o => o.Availability).ToHashSet();

        Assert.That(seen, Does.Contain(OfferAvailability.Available));
        Assert.That(seen, Does.Contain(OfferAvailability.SoldOut));
        Assert.That(seen, Does.Contain(OfferAvailability.Locked));
    }

    /// <summary>Every locked offer has a non-empty lock reason.</summary>
    [Test]
    public void ALockedOfferSaysWhatUnlocksIt()
    {
        foreach (string scenario in MetaFixtures.Scenarios.Keys)
        {
            foreach (OfferView offer in MetaFixtures.Build(scenario, TimeSpan.Zero).Shop.All())
            {
                if (offer.Availability == OfferAvailability.Locked)
                    Assert.That(offer.LockReason, Is.Not.Empty, $"{offer.Id} is locked and does not say why");
            }
        }
    }

    /// <summary>
    /// The default fixture has an item in every cosmetic ownership state and exactly one equipped item per slot.
    /// <para>
    /// The one-equipped rule applies only to the fixture. A real player who owns nothing has nothing equipped.
    /// </para>
    /// <para>
    /// The slots come from <c>CosmeticsView.Slots</c> rather than every enum value, because a slot with no
    /// published items has no tab.
    /// </para>
    /// </summary>
    [Test]
    public void TheCosmeticsCatalogueCoversItsStatesAndEquipsOnePerSlot()
    {
        CosmeticsView cosmetics = MetaFixtures.Build(MetaFixtures.Default, TimeSpan.Zero).Cosmetics;

        HashSet<CosmeticOwnership> seen = cosmetics.Items.Select(i => i.Ownership).ToHashSet();
        Assert.That(seen, Does.Contain(CosmeticOwnership.Equipped));
        Assert.That(seen, Does.Contain(CosmeticOwnership.Owned));
        Assert.That(seen, Does.Contain(CosmeticOwnership.Affordable));
        Assert.That(seen, Does.Contain(CosmeticOwnership.Unaffordable));
        Assert.That(seen, Does.Contain(CosmeticOwnership.Locked));
        Assert.That(seen, Does.Contain(CosmeticOwnership.Unavailable));

        Assert.That(cosmetics.Slots, Does.Contain(CosmeticSlot.Avatar), "the fixture shows the Avatars tab first");
        Assert.That(cosmetics.Slots.First(), Is.EqualTo(CosmeticSlot.Avatar));

        foreach (CosmeticSlot slot in cosmetics.Slots)
        {
            Assert.That(cosmetics.InSlot(slot).Count(i => i.Ownership == CosmeticOwnership.Equipped), Is.EqualTo(1),
                $"{slot} must have exactly one equipped item");
        }
    }

    /// <summary>
    /// The default fixture's equipped avatar, frame and name effect match its identity. The preview card is drawn
    /// from the identity, so a mismatch would show a player state that cannot exist.
    /// </summary>
    [Test]
    public void TheFixturesEquippedItemsAreWhatItsIdentityWears()
    {
        MetaSnapshot snapshot = MetaFixtures.Build(MetaFixtures.Default, TimeSpan.Zero);

        Assert.That(snapshot.Cosmetics.EquippedIn(CosmeticSlot.Avatar)!.StyleToken, Is.EqualTo(snapshot.Identity.AvatarToken));
        Assert.That(snapshot.Cosmetics.EquippedIn(CosmeticSlot.Frame)!.StyleToken, Is.EqualTo(snapshot.Identity.FrameToken));
        Assert.That(snapshot.Cosmetics.EquippedIn(CosmeticSlot.NameEffect)!.StyleToken, Is.EqualTo(snapshot.Identity.NameEffectToken));
    }

    /// <summary>
    /// The player's own standings row shows the same name, cosmetics and rank as the player's identity.
    /// </summary>
    [Test]
    public void ThePlayersStandingsRowWearsWhatTheirProfileEquips()
    {
        MetaSnapshot snapshot = MetaFixtures.Build(MetaFixtures.Default, TimeSpan.Zero);
        StandingRow self = snapshot.Tournament.Standings.Single(r => r.IsSelf);

        Assert.That(self.DisplayName,     Is.EqualTo(snapshot.Identity.DisplayName));
        Assert.That(self.AvatarToken,     Is.EqualTo(snapshot.Identity.AvatarToken));
        Assert.That(self.FrameToken,      Is.EqualTo(snapshot.Identity.FrameToken));
        Assert.That(self.NameEffectToken, Is.EqualTo(snapshot.Identity.NameEffectToken));
        Assert.That(self.Rank,            Is.EqualTo(snapshot.Identity.CompetitionRank));
    }

    /// <summary>Returns the view in <paramref name="s"/> for <paramref name="feature"/>.</summary>
    private static IFeatureView ViewOf(MetaSnapshot s, MetaFeature feature) =>
        AllViews(s).Single(view => view.Feature == feature);

    private static IEnumerable<IFeatureView> AllViews(MetaSnapshot s) => new IFeatureView[]
    {
        s.FirstWeek, s.DailyReward, s.Missions, s.SpinWheel, s.WeeklyEvent,
        s.Tournament, s.Shop, s.Profile, s.Cosmetics,
    };

    /// <summary>
    /// For each feature whose whole view is replaced by real state when its slice is not pinned, the slice that
    /// controls it. Features not listed always show fixture state. The shop is not listed because
    /// <see cref="FixtureSlice.Shop"/> covers only the featured offer, and the rest of the shop screen is always fixture
    /// state.
    /// </summary>
    private static readonly IReadOnlyDictionary<MetaFeature, FixtureSlice> OverlaidBy = new Dictionary<MetaFeature, FixtureSlice>
    {
        [MetaFeature.FirstWeekEvent] = FixtureSlice.FirstWeek,
        [MetaFeature.DailyReward]    = FixtureSlice.DailyReward,
        [MetaFeature.Missions]       = FixtureSlice.Missions,
        [MetaFeature.SpinWheel]      = FixtureSlice.SpinWheel,
        [MetaFeature.Tournament]     = FixtureSlice.Tournament,
    };

    /// <summary>
    /// Whether a player with a live session still sees the scenario's fixture state for <paramref name="feature"/>.
    /// </summary>
    private static bool SurvivesASession(string scenario, MetaFeature feature) =>
        !OverlaidBy.TryGetValue(feature, out FixtureSlice slice) || MetaFixtures.PinnedBy(scenario).Contains(slice);
}
