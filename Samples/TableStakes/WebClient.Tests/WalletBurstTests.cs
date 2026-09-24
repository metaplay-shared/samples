using WebClient.Meta;

namespace WebClient.Tests;

/// <summary>
/// Tests for <see cref="WalletBurstPlan"/>, the currency sprites that fly from a reward to the HUD, and
/// <see cref="WalletBurstBalances"/>, the HUD balance that counts up as they land.
/// <para>
/// Sprite counts, timings and scatter are computed in C# rather than in CSS so that these tests can check them
/// without a browser.
/// </para>
/// </summary>
[TestFixture]
public class WalletBurstTests
{
    // ---------------------------------------------------------------------
    // Which currencies get sprites
    // ---------------------------------------------------------------------

    /// <summary>A cosmetic item has no HUD balance, so it gets no sprites.</summary>
    [Test]
    public void OnlyCurrenciesFly()
    {
        RewardView bundle = RewardView.Of(
            RewardViewItem.Of(CurrencyKind.Coins, 300),
            RewardViewItem.Item("Sapphire Frame", "frame"));

        WalletBurstPlan plan = WalletBurstPlan.For(bundle);

        Assert.That(plan.Bursts.Select(burst => burst.Currency), Is.EqualTo(new[] { CurrencyKind.Coins }));
    }

    [Test]
    public void ARewardOfNothingButCosmeticsMakesNoBurstAndNoRoll()
    {
        RewardView bundle = RewardView.Of(RewardViewItem.Item("Sapphire Frame", "frame"));

        Assert.That(WalletBurstPlan.For(bundle).IsEmpty, Is.True);
        Assert.That(WalletBurstBalances.Begin(bundle), Is.Null, "there is no gap to hold the HUD back across");
    }

    [Test]
    public void AnEmptyBundleMakesNoBurst()
    {
        Assert.That(WalletBurstPlan.For(RewardView.Empty).IsEmpty, Is.True);
        Assert.That(WalletBurstPlan.For(RewardView.Empty).SpriteCount, Is.Zero);
    }

    /// <summary>
    /// Two items of the same currency make one burst for their total, because two overlapping bursts at one chip look
    /// like a rendering fault.
    /// </summary>
    [Test]
    public void TwoItemsOfOneCurrencyAreOneSprayOfTheirTotal()
    {
        RewardView bundle = RewardView.Of(
            RewardViewItem.Of(CurrencyKind.Coins, 200),
            RewardViewItem.Of(CurrencyKind.Coins, 130));

        WalletBurstPlan plan = WalletBurstPlan.For(bundle);

        Assert.That(plan.Bursts, Has.Count.EqualTo(1));
        Assert.That(plan.Bursts[0].Amount, Is.EqualTo(330));
        Assert.That(plan.Bursts[0].Sprites, Has.Count.EqualTo(WalletBurstPlan.SpriteCountFor(CurrencyKind.Coins, 330)));
    }

    /// <summary>
    /// Bursts always run in the order coins, gems, spin tokens, regardless of the bundle's item order, so the same
    /// reward always animates the same way.
    /// </summary>
    [Test]
    public void TheSpraysAlwaysRunInTheSameCurrencyOrder()
    {
        RewardView bundle = RewardView.Of(
            RewardViewItem.Of(CurrencyKind.SpinTokens, 1),
            RewardViewItem.Of(CurrencyKind.Coins, 330),
            RewardViewItem.Of(CurrencyKind.Gems, 25));

        Assert.That(
            WalletBurstPlan.For(bundle).Bursts.Select(burst => burst.Currency),
            Is.EqualTo(new[] { CurrencyKind.Coins, CurrencyKind.Gems, CurrencyKind.SpinTokens }));
    }

    // ---------------------------------------------------------------------
    // Sprite counts
    // ---------------------------------------------------------------------

    /// <summary>Reference amounts and their expected sprite counts. The count grows with the amount.</summary>
    [TestCase(CurrencyKind.Coins, 10L, 18)]
    [TestCase(CurrencyKind.Coins, 50L, 18)]
    [TestCase(CurrencyKind.Coins, 330L, 29)]
    [TestCase(CurrencyKind.Coins, 1_200L, 36)]
    [TestCase(CurrencyKind.Coins, 9_999L, 36)]
    [TestCase(CurrencyKind.SpinTokens, 1L, 14)]
    [TestCase(CurrencyKind.SpinTokens, 5L, 22)]
    [TestCase(CurrencyKind.SpinTokens, 50L, 22)]
    public void TheSprayIsAsBigAsTheReward(CurrencyKind kind, long amount, int expected)
    {
        Assert.That(WalletBurstPlan.SpriteCountFor(kind, amount), Is.EqualTo(expected));
    }

    /// <summary>
    /// Each currency has its own amount scale. Spin tokens are granted one or two at a time, so one token makes fewer
    /// sprites than one coin, and 25 gems makes more sprites than 25 coins.
    /// </summary>
    [Test]
    public void EachCurrencyIsReadAgainstItsOwnRange()
    {
        Assert.That(WalletBurstPlan.SpriteCountFor(CurrencyKind.SpinTokens, 1),
            Is.LessThan(WalletBurstPlan.SpriteCountFor(CurrencyKind.Coins, 1)),
            "one token and one coin are not the same event");

        Assert.That(WalletBurstPlan.SpriteCountFor(CurrencyKind.Gems, 25),
            Is.GreaterThan(WalletBurstPlan.SpriteCountFor(CurrencyKind.Coins, 25)),
            "25 gems is a large reward and 25 coins is not");
    }

    [Test]
    public void MoreOfACurrencyIsNeverFewerSprites()
    {
        foreach (CurrencyKind kind in Enum.GetValues<CurrencyKind>())
        {
            int previousSpriteCount = 0;
            for (long amount = 1; amount <= 20_000; amount = amount * 3 / 2 + 1)
            {
                int count = WalletBurstPlan.SpriteCountFor(kind, amount);
                Assert.That(count, Is.GreaterThanOrEqualTo(previousSpriteCount), $"{kind} fell back at {amount}");
                previousSpriteCount = count;
            }
        }
    }

    /// <summary>
    /// An amount of one in any currency still makes enough sprites to read as a burst.
    /// <para>
    /// The test loops over every <see cref="CurrencyKind"/> value, so a new currency without its own scale is
    /// checked through the fallback scale.
    /// </para>
    /// </summary>
    [Test]
    public void EvenTheSmallestRewardIsABurstRatherThanATrickle()
    {
        foreach (CurrencyKind kind in Enum.GetValues<CurrencyKind>())
            Assert.That(WalletBurstPlan.SpriteCountFor(kind, 1), Is.GreaterThanOrEqualTo(14), $"{kind} trickles");
    }

    [Test]
    public void NothingAwardedIsNothingLaunched()
    {
        Assert.That(WalletBurstPlan.SpriteCountFor(CurrencyKind.Coins, 0), Is.Zero);
        Assert.That(WalletBurstPlan.For(RewardView.Of(RewardViewItem.Of(CurrencyKind.Coins, 0))).IsEmpty, Is.True);
    }

    // ---------------------------------------------------------------------
    // Timing and scatter
    // ---------------------------------------------------------------------

    /// <summary>
    /// A bigger reward makes more sprites, not a longer sequence. The largest three-currency reward lands its last
    /// sprite before <see cref="WalletBurstBalances.MaxAnimationMs"/>, so the cap never cuts a burst short.
    /// </summary>
    [Test]
    public void EvenTheBiggestRewardIsOverInAboutASecond()
    {
        WalletBurstPlan coins = WalletBurstPlan.For(RewardView.Of(RewardViewItem.Of(CurrencyKind.Coins, 9_999)));
        Assert.That(coins.DurationMs, Is.LessThan(1_000));

        WalletBurstPlan everything = WalletBurstPlan.For(RewardView.Of(
            RewardViewItem.Of(CurrencyKind.Coins, 9_999),
            RewardViewItem.Of(CurrencyKind.Gems, 500),
            RewardViewItem.Of(CurrencyKind.SpinTokens, 5)));

        Assert.That(everything.DurationMs, Is.LessThan(WalletBurstBalances.MaxAnimationMs),
            "a burst longer than the cap would be cut off by it rather than bounded by it");
    }

    /// <summary>
    /// A single-currency burst lasts exactly <see cref="WalletBurstPlan.DefaultBurstMs"/>. The last sprite lands at
    /// the end of the sequence, no sprite lands before the emit and travel stages have run, and each sprite lands at
    /// its delay plus its emit and travel times.
    /// </summary>
    [Test]
    public void AOneCurrencySequenceRunsExactlyItsClock()
    {
        WalletBurstPlan plan = WalletBurstPlan.For(RewardView.Of(RewardViewItem.Of(CurrencyKind.Coins, 330)));
        CurrencyBurst   burst = plan.Bursts[0];

        Assert.That(plan.DurationMs, Is.EqualTo(WalletBurstPlan.DefaultBurstMs),
            "the last sprite lands at exactly T, not past it");
        Assert.That(burst.LandsAtMs, Is.EqualTo(WalletBurstPlan.DefaultBurstMs));

        Assert.That(burst.Sprites.Min(sprite => sprite.LandsAtMs),
            Is.GreaterThanOrEqualTo(0.6 * WalletBurstPlan.DefaultBurstMs),
            "the emit and travel stages own the first part of the clock before anything can land");

        Assert.That(
            burst.Sprites,
            Has.All.Matches<BurstSprite>(sprite => sprite.LandsAtMs == sprite.DelayMs + sprite.EmitMs + sprite.TravelMs),
            "a landing is its wait plus emit plus travel, exactly");
    }

    /// <summary>
    /// The first sprite has no delay. The burst starts on the tap that dismisses the reveal, and a delayed first
    /// sprite would leave an empty screen while the reveal fades out.
    /// </summary>
    [Test]
    public void TheFirstSpriteLeavesOnTheBeat()
    {
        WalletBurstPlan plan = WalletBurstPlan.For(RewardView.Of(RewardViewItem.Of(CurrencyKind.Coins, 330)));

        Assert.That(plan.Bursts[0].Sprites[0].DelayMs, Is.Zero);
    }

    /// <summary>
    /// Each currency's burst starts after the previous one, because simultaneous bursts to two chips are hard to tell
    /// apart.
    /// </summary>
    [Test]
    public void OneCurrencyStartsAfterTheOneBeforeIt()
    {
        WalletBurstPlan plan = WalletBurstPlan.For(RewardView.Of(
            RewardViewItem.Of(CurrencyKind.Coins, 330),
            RewardViewItem.Of(CurrencyKind.SpinTokens, 1)));

        int firstCoin  = plan.Of(CurrencyKind.Coins)!.Sprites.Min(sprite => sprite.DelayMs);
        int firstToken = plan.Of(CurrencyKind.SpinTokens)!.Sprites.Min(sprite => sprite.DelayMs);

        Assert.That(firstToken, Is.GreaterThan(firstCoin));
    }

    /// <summary>
    /// A shorter <c>burstMs</c> scales the emit, travel and delay stages with it, because each is a fraction of the
    /// sequence. The sprite count does not change.
    /// </summary>
    [Test]
    public void ForcingTheBurstLengthForcesTheWholePlan()
    {
        RewardView bundle = RewardView.Of(RewardViewItem.Of(CurrencyKind.Coins, 9_999));

        WalletBurstPlan forced = WalletBurstPlan.For(bundle, burstMs: 100);

        Assert.That(forced.SpriteCount, Is.EqualTo(WalletBurstPlan.For(bundle).SpriteCount), "the spray is unchanged");
        Assert.That(forced.DurationMs, Is.EqualTo(100), "the sequence is the clock, exactly");

        foreach (BurstSprite sprite in forced.Bursts.SelectMany(burst => burst.Sprites))
        {
            Assert.That(sprite.EmitMs,   Is.EqualTo(22), "the emit stage scales with the clock");
            Assert.That(sprite.TravelMs, Is.EqualTo(40), "so does the travel stage");
            Assert.That(sprite.DelayMs,  Is.InRange(0, 38), "and so does the delay span");
        }
    }

    [Test]
    public void AnEmptyRewardIsNoBurstAtAll()
    {
        Assert.That(WalletBurstPlan.For(RewardView.Of(RewardViewItem.Of(CurrencyKind.Coins, 330)), burstMs: 0).IsEmpty,
            Is.True);
    }

    /// <summary>
    /// The same reward always produces the same sprites. The shell re-renders during the burst, for example when a
    /// countdown ticks, and random scatter would move the sprites on every render.
    /// </summary>
    [Test]
    public void TheSameRewardAlwaysMakesTheSameSpray()
    {
        RewardView bundle = RewardView.Of(
            RewardViewItem.Of(CurrencyKind.Coins, 330),
            RewardViewItem.Of(CurrencyKind.SpinTokens, 1));

        IEnumerable<BurstSprite> first  = WalletBurstPlan.For(bundle).Bursts.SelectMany(burst => burst.Sprites);
        IEnumerable<BurstSprite> second = WalletBurstPlan.For(bundle).Bursts.SelectMany(burst => burst.Sprites);

        Assert.That(second, Is.EqualTo(first));
    }

    /// <summary>
    /// Every scattered sprite value stays within its designed range. The emit point is a unit direction scaled by a
    /// radius with a nonzero minimum, so sprites spread out in all directions.
    /// </summary>
    [Test]
    public void EverySpriteIsScatteredWithinItsBounds()
    {
        WalletBurstPlan plan = WalletBurstPlan.For(RewardView.Of(RewardViewItem.Of(CurrencyKind.Coins, 9_999)));
        IReadOnlyList<BurstSprite> sprites = plan.Bursts[0].Sprites;

        Assert.Multiple(() =>
        {
            // The squared distance of the emit point is the squared radius, so it lies between the squared minimum
            // radius and 1.
            Assert.That(sprites.Select(sprite => sprite.EmitX * sprite.EmitX + sprite.EmitY * sprite.EmitY),
                Is.All.InRange(0.55 * 0.55, 1.0));
            Assert.That(sprites.Select(sprite => sprite.Spin), Is.All.InRange(-540.0, 540.0));
            Assert.That(sprites.Select(sprite => sprite.Scale), Is.All.InRange(0.7, 1.15));
            // The upper bound is the delay span times DefaultBurstMs.
            Assert.That(sprites.Select(sprite => sprite.DelayMs), Is.All.InRange(0, 304));
            Assert.That(sprites.Select(sprite => sprite.Index), Is.EqualTo(Enumerable.Range(0, sprites.Count)));
        });

        Assert.That(sprites.Any(sprite => sprite.EmitX < 0), Is.True, "the burst emits left of its icon");
        Assert.That(sprites.Any(sprite => sprite.EmitX > 0), Is.True, "and right of it");
        Assert.That(sprites.Any(sprite => sprite.EmitY < 0), Is.True, "and above it");
        Assert.That(sprites.Any(sprite => sprite.EmitY > 0), Is.True, "and below it");
    }

    /// <summary>
    /// Gem sprites vary less in scale than coin sprites, because gems with large size differences look like broken
    /// glass.
    /// </summary>
    [Test]
    public void AGemVariesInSizeLessThanACoinDoes()
    {
        (double gemsFrom,  double gemsTo)  = WalletBurstPlan.ScaleRangeOf(CurrencyKind.Gems);
        (double coinsFrom, double coinsTo) = WalletBurstPlan.ScaleRangeOf(CurrencyKind.Coins);

        Assert.That(gemsTo - gemsFrom, Is.LessThan(coinsTo - coinsFrom));

        IReadOnlyList<BurstSprite> gems =
            WalletBurstPlan.For(RewardView.Of(RewardViewItem.Of(CurrencyKind.Gems, 300))).Bursts[0].Sprites;

        Assert.That(gems.Select(sprite => sprite.Scale), Is.All.InRange(gemsFrom, gemsTo));
    }

    // ---------------------------------------------------------------------
    // The HUD balance balances
    // ---------------------------------------------------------------------

    /// <summary>The balances for a reward of 330 coins, the reward most balances tests use.</summary>
    private static WalletBurstBalances CoinRoll() => WalletBurstBalances.Begin(RewardView.Of(RewardViewItem.Of(CurrencyKind.Coins, 330)))!;

    /// <summary>
    /// <see cref="WalletBurstBalances"/> stores no balances. The shown value is the current wallet balance minus the part of
    /// the reward whose sprites have not landed.
    /// <para>
    /// This lets the balances start when the grant commits. The HUD stays visible behind the reveal's scrim, so starting
    /// the balances any later would show the balance rise, drop back, and count up again.
    /// </para>
    /// </summary>
    [Test]
    public void TheHeldNumberIsTheModelsOwnLessWhatHasNotArrivedYet()
    {
        WalletBurstBalances balances = CoinRoll();

        Assert.That(balances.ValueAt(CurrencyKind.Coins, new WalletView(13_000, 40, 2), 0), Is.EqualTo(12_670));

        // The same balances against a later wallet balance follows the new balance, because the balances stores none.
        Assert.That(balances.ValueAt(CurrencyKind.Coins, new WalletView(41_330, 40, 2), 0), Is.EqualTo(41_000));
    }

    /// <summary>
    /// The shown balance rises with the number of landed sprites rather than with elapsed time, so the digits and the
    /// sprites stay in step. It never decreases and ends on the exact wallet balance.
    /// </summary>
    [Test]
    public void TheNumberClimbsWithTheArrivalsAndEndsExact()
    {
        WalletView after = new WalletView(13_000, 40, 2);
        WalletBurstBalances balances = CoinRoll();

        long previous = 12_670;
        bool moved = false;

        for (int elapsed = 0; elapsed <= balances.DurationMs; elapsed += 10)
        {
            long value = balances.ValueAt(CurrencyKind.Coins, after, elapsed);

            Assert.That(value, Is.GreaterThanOrEqualTo(previous), $"the number went backwards at {elapsed}ms");
            Assert.That(value, Is.InRange(12_670, 13_000), $"out of range at {elapsed}ms");

            moved |= value > 12_670 && value < 13_000;
            previous = value;
        }

        Assert.That(moved, Is.True, "the number jumped rather than rolled");
        Assert.That(balances.ValueAt(CurrencyKind.Coins, after, balances.DurationMs), Is.EqualTo(13_000));
    }

    /// <summary>
    /// A currency the reward does not include always shows the wallet balance. So a shop purchase that spends gems and
    /// grants coins holds back only the coins, and the gem spend shows at once.
    /// </summary>
    [Test]
    public void ABalanceTheRewardDoesNotTouchIsAlwaysLive()
    {
        WalletView after = new WalletView(13_000, 40, 2);
        WalletBurstBalances balances = CoinRoll();

        for (int elapsed = 0; elapsed <= balances.DurationMs; elapsed += 50)
            Assert.That(balances.ValueAt(CurrencyKind.Gems, after, elapsed), Is.EqualTo(40));
    }

    /// <summary>
    /// The forced burst in <see cref="LiveServerWalletBurstTests"/> ends before <see cref="WalletBurstBalances.MaxAnimationMs"/>.
    /// Otherwise the cap would jump the balance to its final value, which that test cannot detect because it reads
    /// only the final balance.
    /// </summary>
    [Test]
    public void TheBurstTheLiveSuiteForcesIsOverBeforeTheCap()
    {
        WalletBurstPlan plan = WalletBurstPlan.For(
            RewardView.Of(RewardViewItem.Of(CurrencyKind.Coins, LiveServerWalletBurstTests.RewardCoins)),
            LiveServerWalletBurstTests.ForcedBurstMs);

        Assert.That(plan.DurationMs, Is.LessThan(WalletBurstBalances.MaxAnimationMs));
    }

    /// <summary>
    /// The balances shows the final balance once <see cref="WalletBurstBalances.MaxAnimationMs"/> has elapsed, even if the animation
    /// never reports that it finished, for example in a background tab.
    /// <para>
    /// The cap applies to the burst only. The reveal before it stays open until the player dismisses
    /// <c>RewardReveal</c>.
    /// </para>
    /// </summary>
    [Test]
    public void TheBurstIsCappedHoweverTheClockBehaves()
    {
        WalletView after = new WalletView(13_000, 40, 2);
        WalletBurstBalances balances = CoinRoll();

        Assert.That(balances.DurationMs, Is.LessThanOrEqualTo(WalletBurstBalances.MaxAnimationMs));
        Assert.That(balances.ValueAt(CurrencyKind.Coins, after, WalletBurstBalances.MaxAnimationMs), Is.EqualTo(13_000));
        Assert.That(balances.ValueAt(CurrencyKind.Coins, after, int.MaxValue), Is.EqualTo(13_000));
    }

    /// <summary>
    /// When the reward is larger than the wallet balance, for example after a capped grant, the shown value starts at
    /// zero rather than a negative number.
    /// </summary>
    [Test]
    public void ARewardBiggerThanTheBalanceStillStartsAtZero()
    {
        WalletView after = new WalletView(100, 0, 0);
        WalletBurstBalances balances = WalletBurstBalances.Begin(RewardView.Of(RewardViewItem.Of(CurrencyKind.Coins, 5_000)))!;

        Assert.That(balances.ValueAt(CurrencyKind.Coins, after, 0), Is.Zero);
        Assert.That(balances.ValueAt(CurrencyKind.Coins, after, balances.DurationMs), Is.EqualTo(100));
    }

    /// <summary>A shorter <c>burstMs</c> also shortens the balances.</summary>
    [Test]
    public void ForcingTheBurstLengthForcesTheBalances()
    {
        WalletBurstBalances balances = WalletBurstBalances.Begin(RewardView.Of(RewardViewItem.Of(CurrencyKind.Coins, 330)), burstMs: 20)!;

        Assert.That(balances.DurationMs, Is.LessThan(60));
        Assert.That(balances.ValueAt(CurrencyKind.Coins, new WalletView(13_000, 0, 0), 60), Is.EqualTo(13_000));
    }

    /// <summary>
    /// <see cref="WalletBurstBalances.ProgressOf"/>, which drives the chip's rim glow, is the landed sprite count divided by
    /// the burst's sprite count. It is 0 before the first landing, 1 after the last, and never decreases. A currency
    /// the reward does not include reads 1.
    /// </summary>
    [Test]
    public void ProgressIsTheLandedSpriteShareAndNeverDecreases()
    {
        WalletBurstBalances balances = CoinRoll();
        CurrencyBurst burst = balances.Plan.Of(CurrencyKind.Coins)!;

        Assert.That(balances.ProgressOf(CurrencyKind.Coins, 0), Is.Zero, "nothing has landed at the start");
        Assert.That(balances.ProgressOf(CurrencyKind.Gems, 0), Is.EqualTo(1),
            "a currency the reward does not touch is not waiting for anything");

        double previous = 0;
        for (int elapsed = 0; elapsed <= balances.DurationMs; elapsed += 10)
        {
            double progress = balances.ProgressOf(CurrencyKind.Coins, elapsed);

            Assert.That(progress, Is.EqualTo(burst.LandedBy(elapsed) / (double)burst.Sprites.Count),
                $"at {elapsed}ms the fraction is the landed count over the spray's count");
            Assert.That(progress, Is.GreaterThanOrEqualTo(previous), $"progress went backwards at {elapsed}ms");

            previous = progress;
        }

        Assert.That(balances.ProgressOf(CurrencyKind.Coins, balances.DurationMs), Is.EqualTo(1),
            "the last arrival is the whole reward");
    }
}
