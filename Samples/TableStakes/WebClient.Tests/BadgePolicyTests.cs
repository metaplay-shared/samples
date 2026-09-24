using WebClient.Meta;
using WebClient.Meta.Fixtures;

namespace WebClient.Tests;

/// <summary>
/// Tests for <see cref="BadgePolicy"/>. A badge means the target is <b>actionable or unseen</b> and nothing else.
/// A badge that never clears teaches the player to ignore every badge, so each rule is tested in both directions.
/// </summary>
[TestFixture]
public class BadgePolicyTests
{
    /// <summary>
    /// A snapshot where no target is badged. Each test adds the state it needs with a <c>with</c> expression. The
    /// snapshot is an immutable record, so the tests share one instance.
    /// </summary>
    private static readonly MetaSnapshot NothingPending = BuildNothingPending();

    private static MetaSnapshot BuildNothingPending()
    {
        MetaSnapshot s = MetaFixtures.Build("claimed", TimeSpan.Zero);

        return s with
        {
            FirstWeek   = s.FirstWeek   with { TodaysGoals = Array.Empty<GoalView>(), UntilDayExpires = TimeSpan.FromHours(20) },
            WeeklyEvent = s.WeeklyEvent with { IsNewlyAvailable = false },
            Tournament  = s.Tournament  with { HasClaimableReward = false, HasMaterialChange = false },
            Shop        = s.Shop        with { Featured = null, Catalogue = Array.Empty<OfferView>() },
        };
    }


    /// <summary>Whether the Events tab is badged for <paramref name="snapshot"/>, for a player who has seen nothing.</summary>
    private static bool BadgesEvents(MetaSnapshot snapshot) =>
        BadgePolicy.ShouldBadge(BadgeTarget.Events, snapshot, SeenState.Nothing);

    /// <summary>
    /// A weekly event with a reward waiting to be collected badges Events. A weekly event that is only running does
    /// not. The player has no record of which LiveOps events they have seen, so an "unseen" badge could never clear.
    /// </summary>
    [Test]
    public void AWeeklyEventBadgesForARewardWaitingRatherThanForRunning()
    {
        MetaSnapshot running = NothingPending with
        {
            WeeklyEvent = NothingPending.WeeklyEvent with { State = ActivityState.InProgress, HasClaimableReward = false },
        };

        MetaSnapshot owed = NothingPending with
        {
            WeeklyEvent = NothingPending.WeeklyEvent with { State = ActivityState.Actionable, HasClaimableReward = true },
        };

        Assert.That(BadgesEvents(running), Is.False);
        Assert.That(BadgesEvents(owed), Is.True);
    }

    /// <summary>Home is where the player already is, and Play is always available, so neither needs a badge.</summary>
    [Test]
    public void HomeAndPlayNeverCarryABadge()
    {
        MetaSnapshot loud = MetaFixtures.Build(MetaFixtures.Default, TimeSpan.Zero);

        Assert.That(BadgePolicy.ShouldBadge(BadgeTarget.Home, loud, SeenState.Nothing), Is.False);
        Assert.That(BadgePolicy.ShouldBadge(BadgeTarget.Play, loud, SeenState.Nothing), Is.False);
    }

    [Test]
    public void WithNothingPendingNoTargetIsBadged()
    {
        foreach (BadgeTarget target in Enum.GetValues<BadgeTarget>())
            Assert.That(BadgePolicy.ShouldBadge(target, NothingPending, SeenState.Nothing), Is.False, target.ToString());
    }

    #region Events

    [Test]
    public void AClaimableLoginRewardBadgesEvents()
    {
        MetaSnapshot snapshot = NothingPending with
        {
            DailyReward = NothingPending.DailyReward with { State = ActivityState.Actionable, HasClaimableReward = true },
        };

        Assert.That(BadgesEvents(snapshot), Is.True);
    }

    [Test]
    public void AnUncollectedMissionBadgesEvents()
    {
        MetaSnapshot snapshot = NothingPending with
        {
            Missions = NothingPending.Missions with
            {
                State = ActivityState.InProgress,
                DailyMissions = new[] { new GoalView("Win 2", 2, 2, RewardView.Empty, IsClaimed: false) },
            },
        };

        Assert.That(BadgesEvents(snapshot), Is.True);
    }

    [Test]
    public void AnExpiringDailyGoalBadgesEvents()
    {
        MetaSnapshot snapshot = NothingPending with
        {
            FirstWeek = NothingPending.FirstWeek with
            {
                State           = ActivityState.InProgress,
                TodaysGoals     = new[] { new GoalView("Score 500", 410, 500, RewardView.Empty, IsClaimed: false) },
                UntilDayExpires = TimeSpan.FromMinutes(47),
            },
        };

        Assert.That(BadgesEvents(snapshot), Is.True);
    }

    /// <summary>
    /// A first-week day with hours left is not expiring. Without this rule the Events tab would carry a badge for
    /// the player's whole first week.
    /// </summary>
    [Test]
    public void ADailyGoalWithHoursLeftDoesNotBadgeEvents()
    {
        MetaSnapshot snapshot = NothingPending with
        {
            FirstWeek = NothingPending.FirstWeek with
            {
                State           = ActivityState.InProgress,
                TodaysGoals     = new[] { new GoalView("Score 500", 0, 500, RewardView.Empty, IsClaimed: false) },
                UntilDayExpires = TimeSpan.FromHours(20),
            },
        };

        Assert.That(BadgesEvents(snapshot), Is.False);
    }

    [Test]
    public void ANewlyAvailableActivityBadgesEvents()
    {
        MetaSnapshot snapshot = NothingPending with
        {
            WeeklyEvent = NothingPending.WeeklyEvent with { State = ActivityState.Ready, IsNewlyAvailable = true },
        };

        Assert.That(BadgesEvents(snapshot), Is.True);
    }

    #endregion

    #region Compete

    /// <summary>A reward waiting to be collected, or a material rank or season change, badges Compete.</summary>
    [TestCase(true,  false)]
    [TestCase(false, true)]
    public void ARewardWaitingOrAMaterialChangeBadgesCompete(bool hasClaimableReward, bool hasMaterialChange)
    {
        MetaSnapshot snapshot = NothingPending with
        {
            Tournament = NothingPending.Tournament with { HasClaimableReward = hasClaimableReward, HasMaterialChange = hasMaterialChange },
        };

        Assert.That(BadgePolicy.ShouldBadge(BadgeTarget.Compete, snapshot, SeenState.Nothing), Is.True);
    }

    #endregion

    #region Shop

    /// <summary>An unseen targeted offer is the only reason the Shop tab carries a badge.</summary>
    [Test]
    public void AnUnseenTargetedOfferBadgesTheShop()
    {
        MetaSnapshot snapshot = MetaFixtures.Build(MetaFixtures.Default, TimeSpan.Zero);

        Assert.That(BadgePolicy.ShouldBadge(BadgeTarget.Shop, snapshot, SeenState.Nothing), Is.True);
    }

    /// <summary>
    /// The Shop badge for a targeted offer clears once the player has seen the offer.
    /// </summary>
    [Test]
    public void ViewingTheOfferClearsTheShopBadge()
    {
        MetaSnapshot snapshot = MetaFixtures.Build(MetaFixtures.Default, TimeSpan.Zero);
        SeenState    seen     = new SeenState(new[] { "gem-booster" }, CosmeticAcknowledged: false);

        Assert.That(BadgePolicy.ShouldBadge(BadgeTarget.Shop, snapshot, seen), Is.False);
    }

    /// <summary>An untargeted catalogue offer does not badge the Shop tab.</summary>
    [Test]
    public void AnOrdinaryCatalogueOfferDoesNotBadgeTheShop()
    {
        MetaSnapshot snapshot = MetaFixtures.Build(MetaFixtures.Default, TimeSpan.Zero);
        snapshot = snapshot with { Shop = snapshot.Shop with { Featured = null } };

        Assert.That(BadgePolicy.ShouldBadge(BadgeTarget.Shop, snapshot, SeenState.Nothing), Is.False);
    }

    #endregion

    #region Profile

    [Test]
    public void AnUnacknowledgedCosmeticBadgesProfileAndAcknowledgingItClears()
    {
        MetaSnapshot snapshot = NothingPending;
        snapshot = snapshot with
        {
            Profile = snapshot.Profile with
            {
                Cosmetics = snapshot.Cosmetics with { HasUnacknowledgedAcquisition = true },
            },
        };

        Assert.That(BadgePolicy.ShouldBadge(BadgeTarget.Profile, snapshot, SeenState.Nothing), Is.True);
        Assert.That(BadgePolicy.ShouldBadge(BadgeTarget.Profile, snapshot,
            new SeenState(Array.Empty<string>(), CosmeticAcknowledged: true)), Is.False);
    }

    #endregion

    /// <summary>
    /// A surface in the Loading, Offline or Error state does not badge, because its view holds default values
    /// rather than data from the server.
    /// </summary>
    [TestCase(ActivityState.Loading)]
    [TestCase(ActivityState.Offline)]
    [TestCase(ActivityState.Error)]
    public void AnUnhealthySurfaceDoesNotBadge(ActivityState unhealthy)
    {
        MetaSnapshot snapshot = NothingPending with
        {
            DailyReward = NothingPending.DailyReward with { State = unhealthy, HasClaimableReward = true },
        };

        Assert.That(BadgesEvents(snapshot), Is.False);
    }

    /// <summary>
    /// The <see cref="NavDestination"/> overload gives the same answer as the <see cref="BadgeTarget"/> overload for
    /// every tab.
    /// </summary>
    [Test]
    public void TheTabOverloadAgreesWithTheTargetOverload()
    {
        MetaSnapshot snapshot = MetaFixtures.Build(MetaFixtures.Default, TimeSpan.Zero);

        foreach (NavDestination destination in Enum.GetValues<NavDestination>())
        {
            BadgeTarget target = Enum.Parse<BadgeTarget>(destination.ToString());

            Assert.That(BadgePolicy.ShouldBadge(destination, snapshot, SeenState.Nothing),
                Is.EqualTo(BadgePolicy.ShouldBadge(target, snapshot, SeenState.Nothing)), destination.ToString());
        }
    }
}
