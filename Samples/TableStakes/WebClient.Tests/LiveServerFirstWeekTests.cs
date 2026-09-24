using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

namespace WebClient.Tests;

/// <summary>
/// The first-week event end to end against a live game server: a fresh player sees every day with its reward,
/// a real match completes day one, the reward is claimed, and the balance increases once. One test covers the
/// flow because the failures span client and server: progress the client never receives, a claim that changes
/// only the client's balance, or a reward claimable again after a reload. Day arithmetic and expiry are tested in
/// <c>FirstWeekTests</c> and <c>FirstWeekViewTests</c>, because days here are 24 hours long and cannot be moved.
/// The PLAY tap runs inside <see cref="LiveServerLocks.QueueAsync"/>.
/// </summary>
[TestFixture]
public class LiveServerFirstWeekTests : PlaywrightPageTest
{
    /// <summary>
    /// A fresh player's first week, before and after a game. The test also checks that the claim did not trigger
    /// the SDK's consistency checker. The claim writes checksummed state, which is allowed only because it is a
    /// player action that the server runs at the same timeline position as the client.
    /// </summary>
    [Test]
    public async Task AGameFinishesTheFirstDayAndItsRewardIsPaidOnce()
    {
        // ---- A fresh player, before anything has been played ----
        await Page.GotoAsync(ClientUrl("/events/first-week"));

        // The shell shows fixture data while the session connects. The fixture puts the player on day four, so
        // day one in progress shows that the player's own state has arrived.
        await Expect(Page.GetByTestId("first-week-summary")).ToHaveTextAsync("Complete 1 match · 0/1", new() { Timeout = BootTimeoutMs });
        await Expect(FirstWeekDay(1).GetByTestId("first-week-day-state")).ToHaveTextAsync("In progress");

        // Every day and its reward is shown from the first visit, including the last day's reward.
        Assert.That(await Page.GetByTestId("first-week-day").CountAsync(), Is.EqualTo(7), "all seven days are on the screen");
        await Expect(FirstWeekDay(1)).ToContainTextAsync("Complete 1 match");
        await Expect(FirstWeekDay(6)).ToContainTextAsync("Complete 3 matches");
        await Expect(FirstWeekDay(7)).ToContainTextAsync("Complete 1 match");
        await Expect(FirstWeekDay(7)).ToContainTextAsync("1,500");
        await Expect(FirstWeekDay(7).GetByTestId("first-week-day-state")).ToHaveTextAsync("Opens on day 7");

        // Without a ?meta= scenario, no slice is authored and every slice shows the player's own state.
        await WaitForLiveSessionAsync();
        await Expect(Page.GetByTestId("session")).ToHaveAttributeAsync("data-authored", "");
        await Expect(Page.GetByTestId("authored-notice")).ToHaveCountAsync(0);

        int coinsBefore = await ReadBalanceAsync("coins");

        // ---- One real game against the live server ----
        await PlayFromTheNavigationBarAsync("the first-week event's game");
        await PlayWholeHandAsync();
        await LeaveTheResultsAsync();

        // ---- Day one is finished, and it is finished on the server ----
        // Navigate in-app instead of loading the page. The match result is recorded by a server action
        // (PlayerRecordMatchResult), so the progress shown below already comes from the server. The reload
        // further down checks persistence.
        await Page.GetByTestId("nav-events").ClickAsync();
        await Page.GetByTestId("feature-firstweekevent-action").ClickAsync();
        await Expect(FirstWeekDay(1).GetByTestId("first-week-day-claim")).ToBeVisibleAsync(new() { Timeout = BootTimeoutMs });
        await Expect(FirstWeekDay(1).GetByTestId("first-week-day-state")).ToHaveTextAsync("Reward ready");
        await Expect(FirstWeekDay(1)).ToContainTextAsync("1 / 1");
        // Progress counts only towards the active day, not the next one.
        await Expect(FirstWeekDay(2).GetByTestId("first-week-day-state")).ToHaveTextAsync("Opens on day 2");
        await Expect(FirstWeekDay(2)).ToContainTextAsync("0 / 1");

        Assert.That(await ReadBalanceAsync("coins"), Is.EqualTo(coinsBefore), "finishing a day pays nothing; the claim does");

        // ---- Claim it ----
        await FirstWeekDay(1).GetByTestId("first-week-day-claim").ClickAsync();
        await Expect(Page.GetByTestId("reward-reveal")).ToBeVisibleAsync(new() { Timeout = TurnTimeoutMs });
        await Expect(Page.GetByTestId("reward-reveal")).ToContainTextAsync("250");
        await Page.GetByTestId("reward-continue").ClickAsync();

        await Expect(FirstWeekDay(1).GetByTestId("first-week-day-claimed")).ToBeVisibleAsync();
        Assert.That(await ReadBalanceAsync("coins"), Is.EqualTo(coinsBefore + 250), "the balance did not move by the reward");

        // ---- The reward is paid once. A reload rebuilds the client from the server's state, not from this
        //      page's prediction.
        await ReloadOnStackAsync(Page);

        // Wait on the summary, not the tile. The fixture shown while the session reconnects also shows day one
        // claimed, so waiting on the tile could read the fixture's wallet.
        await Expect(Page.GetByTestId("first-week-summary")).ToHaveTextAsync("Complete 1 match · 1/1", new() { Timeout = BootTimeoutMs });

        await Expect(FirstWeekDay(1).GetByTestId("first-week-day-claimed")).ToBeVisibleAsync();
        await Expect(FirstWeekDay(1).GetByTestId("first-week-day-claim")).ToHaveCountAsync(0);
        Assert.That(await ReadBalanceAsync("coins"), Is.EqualTo(coinsBefore + 250), "the reward was paid twice, or not at all");

        AssertTheTimelinesNeverDiverged();
        AssertTheSessionRanConsistencyChecks();
    }
}
