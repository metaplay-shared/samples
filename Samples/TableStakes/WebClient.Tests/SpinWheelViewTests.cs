using WebClient.Meta;
using WebClient.Meta.Fixtures;

namespace WebClient.Tests;

/// <summary>
/// Tests for <see cref="SpinWheelView"/>, the data the Spin Wheel screen renders. They need no browser and no
/// server.
/// <para>
/// The main property checked is that the drawn sectors and the published odds sheet are derived from the same
/// sector list, so they cannot disagree (<c>docs/spin-wheel.md</c>).
/// </para>
/// </summary>
[TestFixture]
public class SpinWheelViewTests
{
    private static SpinWheelView Wheel(string scenario = "spin") =>
        MetaFixtures.Build(scenario, TimeSpan.Zero).SpinWheel;

    /// <summary>
    /// A spin is refused when any prize on the wheel would overflow a balance. The view names the full currency
    /// in <c>BlockedBy</c>, so the screen can tell the player which balance blocks the spin.
    /// </summary>
    [Test]
    public void AFullBalanceIsNamedRatherThanOnlyReported()
    {
        SpinWheelView roomy = Wheel();

        Assert.That(roomy.BlockedBy, Is.Null);
        Assert.That(roomy.EveryPrizeFits, Is.True);

        SpinWheelView blocked = roomy with { BlockedBy = CurrencyKind.Coins };

        Assert.That(blocked.EveryPrizeFits, Is.False);
        Assert.That(blocked.BlockedBy, Is.EqualTo(CurrencyKind.Coins));
    }

    [Test]
    public void TheWheelIsTenEqualSectors()
    {
        SpinWheelView view = Wheel();

        Assert.That(view.Sectors, Has.Count.EqualTo(10));
        Assert.That(view.Sectors.Select(s => s.Index), Is.EqualTo(Enumerable.Range(0, 10)));

        // Every sector except the blank grants a positive amount. The blank has a null currency, not a zero amount.
        Assert.That(view.Sectors.Count(s => s.Currency == null), Is.EqualTo(1), "the wheel carries exactly one blank");
        Assert.That(view.Sectors.Where(s => s.Currency != null).All(s => s.Amount > 0), Is.True, "a paying sector on the wheel pays nothing");
    }

    /// <summary>
    /// The set of distinct drawn sectors equals the set of odds sheet rows. The blank sector appears as the
    /// null-currency row.
    /// </summary>
    [Test]
    public void EverySectorIsOnTheOddsSheetAndEveryRowIsOnTheWheel()
    {
        SpinWheelView view = Wheel();

        IEnumerable<(CurrencyKind? Currency, long Amount)> drawn     = view.Sectors.Select(s => (s.Currency, s.Amount)).Distinct().OrderBy(x => x.Currency).ThenBy(x => x.Amount);
        IEnumerable<(CurrencyKind? Currency, long Amount)> published = view.PublishedOdds.Select(r => (r.Currency, r.Amount)).OrderBy(x => x.Currency).ThenBy(x => x.Amount);

        Assert.That(published, Is.EqualTo(drawn));
    }

    /// <summary>
    /// Each row's percentage is computed from the number of sectors that pay it. No odds figure is authored
    /// separately.
    /// </summary>
    [Test]
    public void EachPercentageIsTheCountOfTheSectorsThatPayIt()
    {
        SpinWheelView view = Wheel();

        foreach (WheelOddsRow row in view.PublishedOdds)
        {
            int sectors = view.Sectors.Count(s => s.Currency == row.Currency && s.Amount == row.Amount);
            Assert.That(row.ChancePercent, Is.EqualTo(sectors * 100 / view.Sectors.Count), $"{row.Label} at {row.Amount}");
        }

        Assert.That(view.PublishedOdds.Sum(row => row.ChancePercent), Is.EqualTo(100),
            "the published odds do not describe the whole wheel");
    }

    /// <summary>The blank row is labelled "Nothing" and has a zero amount.</summary>
    [Test]
    public void ABlankRowIsLabelledNothing()
    {
        WheelOddsRow blank = Wheel().PublishedOdds.Single(row => row.Currency == null);

        Assert.That(blank.Label, Is.EqualTo("Nothing"));
        Assert.That(blank.Amount, Is.Zero);
    }

    /// <summary>
    /// The wheel has no deadline. Whether the player can spin depends only on the token balance, so the view
    /// reports no urgency and no ending-soon window in any scenario.
    /// </summary>
    [Test]
    public void TheWheelHasNoDeadline()
    {
        foreach (string scenario in MetaFixtures.Scenarios.Keys)
        {
            SpinWheelView view = Wheel(scenario);
            Assert.That(view.UntilDeadline, Is.Null, scenario);
            Assert.That(view.EndingSoonWithin, Is.EqualTo(TimeSpan.Zero), scenario);
        }
    }

    [Test]
    public void AWheelWithNoResultWaitingSaysSo()
    {
        SpinWheelView view = Wheel();

        Assert.That(view.HasPendingReceipt, Is.False);
        Assert.That(view.PendingReceipt, Is.Null);
    }

    /// <summary>
    /// An interrupted spin has a pending receipt with a sector index and a reward, and the player can still spin
    /// again. The screen shows the pending receipt before it offers the next spin.
    /// </summary>
    [Test]
    public void AnInterruptedSpinCarriesItsSectorAndItsReward()
    {
        SpinWheelView view = Wheel("spin-pending");

        Assert.That(view.HasPendingReceipt, Is.True);
        Assert.That(view.PendingReceipt!.SectorIndex, Is.EqualTo(5));
        Assert.That(view.Sectors[view.PendingReceipt.SectorIndex].Amount, Is.EqualTo(1_000),
            "the pending receipt points at a sector that pays something else");
        Assert.That(view.PendingReceipt.Reward.Describe(), Does.Contain("1,000"));
        Assert.That(view.SpinsAvailable, Is.GreaterThan(0), "spin again has to be offerable from this state");
    }

    /// <summary>
    /// A result that was paid but not yet shown makes the Spin Wheel the next action, the same as an unspent
    /// token does.
    /// </summary>
    [Test]
    public void APendingReceiptReachesTheNextActionLadder()
    {
        MetaSnapshot snapshot = MetaFixtures.Build("spin-pending", TimeSpan.Zero);

        Assert.That(NextActionPolicy.Choose(snapshot).Kind, Is.EqualTo(NextActionKind.Spin));
        Assert.That(NextActionPolicy.Choose(snapshot).Feature, Is.EqualTo(MetaFeature.SpinWheel));

        // The pending receipt alone is enough, with no token left.
        MetaSnapshot spent = snapshot with { SpinWheel = snapshot.SpinWheel with { SpinsAvailable = 0 } };
        Assert.That(NextActionPolicy.Choose(spent).Kind, Is.EqualTo(NextActionKind.Spin));
    }

    /// <summary>
    /// The Events badge is shown for an unspent token <b>or</b> for a result that was paid but not yet shown.
    /// </summary>
    [Test]
    public void TheEventsBadgeCountsAPendingReceiptAsWellAsAToken()
    {
        MetaSnapshot waiting = MetaFixtures.Build("spin-pending", TimeSpan.Zero);

        Assert.That(BadgePolicy.ShouldBadge(BadgeTarget.Events, waiting, SeenState.Nothing), Is.True);

        MetaSnapshot spent = waiting with { SpinWheel = waiting.SpinWheel with { SpinsAvailable = 0 } };
        Assert.That(BadgePolicy.ShouldBadge(BadgeTarget.Events, spent, SeenState.Nothing), Is.True,
            "the unseen result still owes the player a reveal");
    }

    /// <summary>
    /// A view built without a sector table, as before a session exists, has no sectors, no odds and no pending
    /// result.
    /// </summary>
    [Test]
    public void AWheelWithNoTableIsEmptyRatherThanWrong()
    {
        SpinWheelView view = new SpinWheelView(ActivityState.Loading, 0, "", RewardView.Empty);

        Assert.That(view.Sectors, Is.Empty);
        Assert.That(view.PublishedOdds, Is.Empty);
        Assert.That(view.HasPendingReceipt, Is.False);
    }
}
