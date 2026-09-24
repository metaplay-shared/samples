using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;
using WebClient.Meta.Ui;

namespace WebClient.Tests;

/// <summary>
/// The daily reward against a live game server: a fresh player claims the first step, the coin balance
/// increases, and the screen shows the claimed state. The streak, the open window and the balance are all server
/// state. Nothing here taps PLAY, so these tests need no <see cref="LiveServerLocks"/> lock. Each Playwright test
/// gets a fresh browser context and therefore a fresh guest account on its first day.
/// </summary>
[TestFixture]
public class LiveServerDailyRewardTests : PlaywrightPageTest
{
    private static readonly string DailyUrl = ClientUrl("/events/daily");

    /// <summary>Open the Daily Reward screen and wait until it is showing real server state.</summary>
    private async Task OpenDailyAsync()
    {
        await Page.GotoAsync(DailyUrl);
        await ExpectTodaysRewardIsClaimableAsync();
    }

    /// <summary>
    /// Wait until the Daily Reward screen offers today's reward. The claim button and the reward preview render
    /// only from a published cycle, so they show that the screen has server state. Today's preview is the first one
    /// on the page, before tomorrow's.
    /// </summary>
    private async Task ExpectTodaysRewardIsClaimableAsync()
    {
        await Expect(Page.GetByTestId("daily-claim")).ToHaveTextAsync(ButtonLabels.Claim, new() { Timeout = BootTimeoutMs });
        await Expect(Page.GetByTestId("reward-preview").First).ToContainTextAsync("150");
    }

    /// <summary>
    /// Every step of the cycle, including the last step's spin token, is shown before the first claim. Claiming
    /// shows the reward reveal, adds the coins to the HUD, and switches the screen to its claimed state.
    /// </summary>
    [Test]
    public async Task ClaimingMovesTheWalletAndLeavesTheScreenClaimed()
    {
        await OpenDailyAsync();

        await Expect(Page.GetByTestId("rail-step")).ToHaveCountAsync(7);

        await Expect(Page.Locator(".m-rail-value")).ToHaveCountAsync(7);
        await Expect(Page.Locator(".m-rail-value").Nth(6)).ToContainTextAsync("330");

        // The screen states the missed-day protection the cycle starts with.
        await Expect(Page.GetByTestId("daily-skip-day")).ToHaveTextAsync("One missed day protected");
        await Expect(Page.GetByTestId("daily-status")).ToHaveTextAsync("Today's reward is ready");

        int before = await ReadBalanceAsync("coins");
        Assert.That(before, Is.EqualTo(3000), "a fresh player's published starting wallet");

        await Page.GetByTestId("daily-claim").ClickAsync();

        // The reveal appears only after the server action has been committed on the client's timeline.
        await Expect(Page.GetByTestId("reward-reveal")).ToBeVisibleAsync(new() { Timeout = BootTimeoutMs });

        // Asserts the button label as well. RewardReveal is shared by every reward screen, so this also covers
        // them. Uses the boot timeout, because the dialog shows a placeholder until the grant commits and this
        // button exists only after that.
        await Expect(Page.GetByTestId("reward-continue"))
            .ToHaveTextAsync("Claim", new() { Timeout = BootTimeoutMs });

        await Page.GetByTestId("reward-continue").ClickAsync();

        await Expect(Page.GetByTestId("balance-coins").Locator(".m-balance__amount"))
            .ToHaveTextAsync("3,150", new() { Timeout = BootTimeoutMs });

        // The claimed state replaces the claim button rather than disabling it.
        await Expect(Page.GetByTestId("daily-claimed")).ToBeVisibleAsync();
        await Expect(Page.GetByTestId("daily-status")).ToHaveTextAsync("Today's reward is claimed");
        await Expect(Page.GetByTestId("daily-claim")).ToHaveCountAsync(0);

        // Check that the journal checkers were on, or an empty console proves nothing.
        Assert.That(await Page.GetByTestId("journal-checks").InnerTextAsync(), Is.EqualTo("on"),
            "the SDK's journal checkers were off, so this test cannot prove the timelines agreed");
        AssertTheTimelinesNeverDiverged();
    }

    /// <summary>
    /// The Events hub's daily reward card shows the streak, whether today's reward is ready, the current step,
    /// and the last step's reward.
    /// </summary>
    [Test]
    public async Task TheHubCardShowsTheStreakTheStepAndTheSeventhStepsValue()
    {
        await Page.GotoAsync(ClientUrl("/events"));

        ILocator card = Page.GetByTestId("feature-dailyreward");
        await Expect(card).ToBeVisibleAsync(new() { Timeout = BootTimeoutMs });

        await Expect(card).ToContainTextAsync("Your first reward");
        await Expect(card).ToContainTextAsync("Ready to claim");
        await Expect(card).ToContainTextAsync("Day 1 of 7");
        await Expect(card).ToContainTextAsync("Day 7 pays 330 coins + a spin");
    }

    /// <summary>
    /// Home promotes the claim in the next-up card instead of opening a popup, and the card's action opens a
    /// claimable Daily Reward screen.
    /// </summary>
    [Test]
    public async Task HomePromotesTheClaimWithoutAPopup()
    {
        await Page.GotoAsync(ClientUrl());
        await Expect(Page.GetByTestId("next-up")).ToBeVisibleAsync(new() { Timeout = BootTimeoutMs });

        // No reward dialog opens on launch.
        await Expect(Page.GetByTestId("reward-reveal")).ToHaveCountAsync(0);

        await Page.GetByTestId("next-up-action").ClickAsync();

        Assert.That(Page.Url, Does.Contain("/events/daily"));
        await ExpectTodaysRewardIsClaimableAsync();
    }

    /// <summary>
    /// A double tap grants the reward once. A reload starts a new session on the same account: the claim and the
    /// balance persist, and the day cannot be claimed again, because the claimed state is stored in the player
    /// model.
    /// </summary>
    [Test]
    public async Task ADoubleTapGrantsExactlyOnce()
    {
        await OpenDailyAsync();

        ILocator claim = Page.GetByTestId("daily-claim");

        // Two quick taps. The actor's request throttle usually drops the second, so this part checks the outcome,
        // not which guard prevented the second grant.
        await claim.ClickAsync();
        await claim.ClickAsync(new() { Force = true, Timeout = 2000 }).ContinueWith(_ => Task.CompletedTask);

        await Expect(Page.GetByTestId("reward-reveal")).ToBeVisibleAsync(new() { Timeout = BootTimeoutMs });
        await Page.GetByTestId("reward-continue").ClickAsync();

        await Expect(Page.GetByTestId("daily-claimed")).ToBeVisibleAsync(new() { Timeout = BootTimeoutMs });

        // Wait past the throttle and reload, so the next session reads the claimed state from the server. The
        // server's in-flight claim tracking is tested in Server.Tests (DailyRewardClaimInFlightTests).
        await Page.WaitForTimeoutAsync(1500);
        await ReloadOnStackAsync(Page);
        await Expect(Page.GetByTestId("daily-claimed")).ToBeVisibleAsync(new() { Timeout = BootTimeoutMs });
        await Expect(Page.GetByTestId("daily-claim")).ToHaveCountAsync(0);

        Assert.That(await ReadBalanceAsync("coins"), Is.EqualTo(3150), "a second tap or the reload granted a second reward");

        // The streak shows one day, not a fresh account, so the state was loaded from the server.
        await Expect(Page.GetByTestId("rail-step").First).ToContainTextAsync("Day 1");
        AssertTheTimelinesNeverDiverged();
    }

    /// <summary>
    /// A <c>?meta=</c> scenario that shows a closed daily reward keeps showing it after the live session connects,
    /// instead of being replaced by the live streak card. A live player only reaches the ready and claimed states, so
    /// the closed states are shown through scenarios, and <c>DailyRewardRenderTests</c> asserts what each one says.
    /// The test waits for the live session, because before it connects the shell shows the fixture anyway.
    /// </summary>
    [TestCase("daily-closed", "state-ready")]
    [TestCase("daily-ended",  "state-expired")]
    [TestCase("unpublished",  "state-unavailable")]
    public async Task AClosedDailyRewardSurvivesASession(string scenario, string block)
    {
        await Page.GotoAsync(ClientUrl("/events/daily", $"meta={scenario}"));
        await WaitForLiveSessionAsync();

        // If live state had replaced the scenario, the streak card would show instead of this block.
        await Expect(Page.GetByTestId(block)).ToBeVisibleAsync(new() { Timeout = BootTimeoutMs });
        await Expect(Page.GetByTestId("authored-notice")).ToBeVisibleAsync();
    }
}
