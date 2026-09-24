using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;
using WebClient.Meta;

namespace WebClient.Tests;

/// <summary>
/// The reward animation to the HUD, against a <b>live game server</b>: the HUD balance keeps its old value until
/// the coin sprites reach it, the number of sprites matches the reward, and the balance ends at the server's value.
/// A fixture has no committed grant, so an offline version would pass whether or not the balance was held back.
/// The <c>burstMs</c> query parameter lengthens the animation so the held balance can be observed reliably
/// (<c>docs/testing.md</c>, "Forcing timers"). No test here taps Play, so these tests need no
/// <see cref="LiveServerLocks"/> lock. Each fresh guest account has an unclaimed first daily reward.
/// </summary>
[TestFixture]
public class LiveServerWalletBurstTests : PlaywrightPageTest
{
    /// <summary>
    /// The forced burst duration, in milliseconds. It is long enough for a test to observe the held
    /// balance, and short enough that the whole burst ends before <see cref="WalletBurstBalances.MaxAnimationMs"/> cuts it off.
    /// The sprite stagger also scales with it, so <c>WalletBurstTests.TheBurstTheLiveSuiteForcesIsOverBeforeTheCap</c>
    /// checks the total length.
    /// </summary>
    public const int ForcedBurstMs = 1_000;

    /// <summary>A fresh account's starting coins, and the coins after claiming the first daily reward.</summary>
    private const string StartingCoins   = "3,000";
    private const string CoinsAfterClaim = "3,150";
    public  const long   RewardCoins     = 150;

    private ILocator Coins => Page.GetByTestId("balance-coins").Locator(".m-balance__amount");

    /// <summary>Open the Daily Reward screen and wait until it is showing real server state.</summary>
    private async Task OpenDailyAsync()
    {
        await Page.GotoAsync(ClientUrl("/events/daily", $"burstMs={ForcedBurstMs}"));

        // Wait for the Claim label and the reward amount in the preview, which appear only with the published
        // daily reward cycle.
        await Expect(Page.GetByTestId("daily-claim"))
            .ToHaveTextAsync(WebClient.Meta.Ui.ButtonLabels.Claim, new() { Timeout = BootTimeoutMs });
        await Expect(Page.GetByTestId("reward-preview").First).ToContainTextAsync($"{RewardCoins}");
    }

    /// <summary>
    /// In the order a player sees it: the grant commits and the HUD balance stays the same, the coin sprites fly to
    /// the coin chip, and only then does the balance count up to the server's value.
    /// <para>
    /// The key assertion runs while the reveal is open. The reveal is drawn only after the grant commits, so a HUD
    /// that already shows the new balance then has updated before the animation.
    /// </para>
    /// </summary>
    [Test]
    public async Task TheBalanceWaitsForTheCoinsToReachIt()
    {
        await OpenDailyAsync();
        await Expect(Coins).ToHaveTextAsync(StartingCoins);

        await Page.GetByTestId("daily-claim").ClickAsync();

        // The reveal is drawn only after the server's action has executed on this client's timeline, so the
        // wallet in the model already holds the new balance.
        await Expect(Page.GetByTestId("reward-reveal")).ToBeVisibleAsync(new() { Timeout = BootTimeoutMs });
        await Expect(Coins).ToHaveTextAsync(StartingCoins, new() { Timeout = 2_000 });

        await Page.GetByTestId("reward-continue").ClickAsync();

        // The balance is still held while the sprites fly to the chip.
        await Expect(Coins).ToHaveTextAsync(StartingCoins, new() { Timeout = 2_000 });

        ILocator burst = Page.GetByTestId("wallet-burst");
        await Expect(burst).ToBeVisibleAsync();

        // The sprite count must match WalletBurstPlan.SpriteCountFor for the reward. WalletBurstTests covers the
        // policy, and this check confirms the page passes it the reward amount.
        await Expect(Page.GetByTestId("wallet-burst-coins")).ToHaveAttributeAsync(
            "data-sprites",
            WalletBurstPlan.SpriteCountFor(CurrencyKind.Coins, RewardCoins).ToString());

        // The reward has no gems or spin tokens, so no sprites fly to those chips.
        await Expect(Page.GetByTestId("wallet-burst-gems")).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("wallet-burst-spintokens")).ToHaveCountAsync(0);

        // The sprites fly to the coin chip's position, which wallet-burst.js measures and sets on the app frame as
        // the --m-fx-coins-x custom property. No other test reads it. If the script cannot find the frame, the
        // burst still draws and the balance still lands, but every sprite flies to the wrong place.
        string coinTargetX = await Page.EvaluateAsync<string>(
            "() => { const f = document.querySelector('[data-app-frame]');" +
            "        return f ? f.style.getPropertyValue('--m-fx-coins-x') : ''; }");

        Assert.That(coinTargetX, Does.EndWith("px"),
            "wallet-burst.js publishes the coin chip's position on the frame it was measured in");

        // The balance ends at the server's exact value, and the burst is removed.
        await Expect(Coins).ToHaveTextAsync(CoinsAfterClaim, new() { Timeout = BootTimeoutMs });
        await Expect(burst).ToHaveCountAsync(0);
    }

    /// <summary>
    /// With reduced motion, the reward is neither animated nor delayed. The HUD shows the new balance as soon as
    /// the grant commits (docs/meta-shell.md, "The shared reward flow").
    /// <para>
    /// The page still sets <c>burstMs</c>, so with motion on it would hold the old balance for the forced burst.
    /// The immediate balance check therefore proves that reduced motion skips the hold.
    /// </para>
    /// </summary>
    [Test]
    public async Task ReducedMotionSkipsTheBurstAndNotTheReward()
    {
        await Page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.Reduce });

        await OpenDailyAsync();
        await Expect(Coins).ToHaveTextAsync(StartingCoins);

        await Page.GetByTestId("daily-claim").ClickAsync();
        await Expect(Page.GetByTestId("reward-reveal")).ToBeVisibleAsync(new() { Timeout = BootTimeoutMs });

        // No hold, so the balance already shows the granted amount.
        await Expect(Coins).ToHaveTextAsync(CoinsAfterClaim, new() { Timeout = 2_000 });

        await Page.GetByTestId("reward-continue").ClickAsync();

        await Expect(Page.GetByTestId("wallet-burst")).ToHaveCountAsync(0);
        await Expect(Coins).ToHaveTextAsync(CoinsAfterClaim);

        // The reveal closed and the screen shows the claimed state. Reduced motion skips only the animation.
        await Expect(Page.GetByTestId("reward-reveal")).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("daily-claimed")).ToBeVisibleAsync();
    }
}
