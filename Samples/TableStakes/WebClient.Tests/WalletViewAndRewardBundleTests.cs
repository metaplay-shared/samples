using WebClient.Meta;
using WebClient.Meta.Fixtures;

namespace WebClient.Tests;

/// <summary>
/// <see cref="WalletView"/> and <see cref="RewardView"/>: balance formatting and how a bundle changes a wallet.
/// </summary>
[TestFixture]
public class WalletViewAndRewardBundleTests
{
    /// <summary>
    /// Balances use grouped digits, not abbreviations such as "1.2K", because prices are shown in full digits and
    /// the player compares the two.
    /// </summary>
    [TestCase(0L, "0")]
    [TestCase(8L, "8")]
    [TestCase(1_250L, "1,250")]
    [TestCase(24_350L, "24,350")]
    [TestCase(1_000_000L, "1,000,000")]
    public void BalancesAreWrittenInFullWithGroupedDigits(long amount, string expected)
    {
        Assert.That(Currencies.Format(amount), Is.EqualTo(expected));
    }

    /// <summary>
    /// Every economy currency has a shell kind. <see cref="Currencies.KindOf"/> returns null for an unmapped
    /// currency, and the Shop and the wheel read null as "no balance is full". An unmapped currency would therefore
    /// offer a Buy or a spin that the server refuses, and a price in it would be drawn as coins.
    /// </summary>
    [Test]
    public void EveryCurrencyExceptNoneHasAShellKind()
    {
        foreach (Game.Logic.CurrencyType currency in Enum.GetValues<Game.Logic.CurrencyType>())
        {
            if (currency == Game.Logic.CurrencyType.None)
                Assert.That(Currencies.KindOf(currency), Is.Null);
            else
                Assert.That(Currencies.KindOf(currency), Is.Not.Null, $"{currency} has no CurrencyKind. Add it to Currencies.KindOf.");
        }
    }

    [TestCase(1L, "Spin Token")]
    [TestCase(3L, "Spin Tokens")]
    [TestCase(0L, "Spin Tokens")]
    public void ACurrencyIsSingularWhenThereIsOneOfIt(long amount, string expected)
    {
        Assert.That(Currencies.NameOf(CurrencyKind.SpinTokens, amount), Is.EqualTo(expected));
    }

    [Test]
    public void EachBalanceIsReadAndWrittenIndependently()
    {
        WalletView wallet = new WalletView(100, 20, 3);

        Assert.That(wallet.AmountOf(CurrencyKind.Coins), Is.EqualTo(100));
        Assert.That(wallet.AmountOf(CurrencyKind.Gems), Is.EqualTo(20));
        Assert.That(wallet.AmountOf(CurrencyKind.SpinTokens), Is.EqualTo(3));

        Assert.That(wallet.With(CurrencyKind.Gems, 99), Is.EqualTo(new WalletView(100, 99, 3)));
    }

    [Test]
    public void AffordabilityIsInclusiveOfTheExactPrice()
    {
        WalletView wallet = new WalletView(2_500, 0, 0);

        Assert.That(wallet.CanAfford(CurrencyKind.Coins, 2_500), Is.True, "exactly enough is enough");
        Assert.That(wallet.CanAfford(CurrencyKind.Coins, 2_501), Is.False);
        Assert.That(wallet.CanAfford(CurrencyKind.Gems, 1), Is.False);
    }

    /// <summary>A bundle credits each currency it contains and leaves the other balances unchanged.</summary>
    [Test]
    public void ABundleCreditsOnlyTheBalancesItNames()
    {
        WalletView before = new WalletView(1_000, 100, 2);
        RewardView bundle = RewardView.Of(
            RewardViewItem.Of(CurrencyKind.Coins, 300),
            RewardViewItem.Of(CurrencyKind.SpinTokens, 1));

        Assert.That(bundle.ApplyTo(before), Is.EqualTo(new WalletView(1_300, 100, 3)));
    }

    /// <summary>A non-currency item, such as a cosmetic, changes no balance and does not fly to the wallet.</summary>
    [Test]
    public void ANonCurrencyItemChangesNoBalance()
    {
        WalletView before = new WalletView(1_000, 100, 2);
        RewardView bundle = RewardView.Of(RewardViewItem.Item("Sapphire Frame", "frame"));

        Assert.That(bundle.ApplyTo(before), Is.EqualTo(before));
        Assert.That(bundle.Items.Single().TravelsToWallet, Is.False,
            "only a currency flies to a balance during the reveal");
    }

    [Test]
    public void ACurrencyItemTravelsToItsBalance()
    {
        Assert.That(RewardViewItem.Of(CurrencyKind.Coins, 300).TravelsToWallet, Is.True);
    }

    [Test]
    public void AnEmptyBundleIsSafeToApply()
    {
        WalletView before = new WalletView(1, 2, 3);

        Assert.That(RewardView.Empty.IsEmpty, Is.True);
        Assert.That(RewardView.Empty.ApplyTo(before), Is.EqualTo(before));
    }

    /// <summary>
    /// The fixture scenarios together must contain every currency, a bundle with more than one item and a
    /// non-currency item, so the reveal can be seen with each of them.
    /// </summary>
    [Test]
    public void TheFixturesCoverEveryCurrencyAndAMultiItemReward()
    {
        IEnumerable<RewardView> bundles = MetaFixtures.Scenarios.Keys.SelectMany(BundlesIn);

        HashSet<CurrencyKind> currencies = bundles
            .SelectMany(b => b.Items)
            .Where(i => i.Currency.HasValue)
            .Select(i => i.Currency!.Value)
            .ToHashSet();

        foreach (CurrencyKind kind in Enum.GetValues<CurrencyKind>())
            Assert.That(currencies, Does.Contain(kind), $"no fixture reward ever awards {kind}");

        Assert.That(bundles.Any(b => b.Items.Count > 1), Is.True, "no fixture reward has more than one item in it");
        Assert.That(bundles.Any(b => b.Items.Any(i => !i.TravelsToWallet)), Is.True,
            "no fixture reward contains a non-currency item");
    }

    private static IEnumerable<RewardView> BundlesIn(string scenario)
    {
        MetaSnapshot s = MetaFixtures.Build(scenario, TimeSpan.Zero);

        yield return s.DailyReward.TodayReward;
        yield return s.DailyReward.TomorrowReward;
        yield return s.FirstWeek.DayReward;
        yield return s.FirstWeek.GrandPrize;
        yield return s.SpinWheel.TopPrize;
        yield return s.WeeklyEvent.Reward;

        foreach (PlacementBandView band in s.Tournament.PlacementBands)
            yield return band.Reward;

        foreach (GoalView goal in s.FirstWeek.TodaysGoals)
            yield return goal.Reward;
        foreach (GoalView goal in s.Missions.DailyMissions)
            yield return goal.Reward;
        foreach (OfferView offer in s.Shop.All())
            yield return offer.Contents;
    }
}
