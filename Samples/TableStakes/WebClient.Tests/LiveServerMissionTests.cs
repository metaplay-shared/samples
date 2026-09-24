using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

namespace WebClient.Tests;

/// <summary>
/// Missions, end to end against a <b>live game server</b>: a fresh player sees their missions, a real match
/// advances them, a finished mission is claimed, and the balance changes. It is one test because only a whole run
/// catches failures between server and client: progress that never reaches the client, a claim that pays only the
/// client's copy, or a reward that a reload pays again. <c>MissionTests</c> and <c>MissionViewTests</c> cover the
/// mission state and its view. The PLAY tap runs inside <see cref="LiveServerLocks.QueueAsync"/> until seated.
/// </summary>
[TestFixture]
public class LiveServerMissionTests : PlaywrightPageTest
{
    /// <summary>The mission card whose goal is <paramref name="title"/>, in the daily list.</summary>
    private ILocator DailyMission(string title) =>
        Page.GetByTestId("mission").Filter(new LocatorFilterOptions { HasText = title });

    /// <summary>
    /// A fresh player's missions, before and after a game. The test also checks that the claim did not trigger the
    /// SDK's consistency checker. The reward grant writes checksummed state, which is valid only because the claim
    /// is a player action that the server executes at the same timeline position as the client.
    /// </summary>
    [Test]
    public async Task AGameAdvancesAMissionAndItsRewardIsPaidOnce()
    {
        // ---- A fresh player, before anything has been played ----
        await Page.GotoAsync(ClientUrl("/events/missions"));

        // The shell draws fixture data while the session connects. The fixture's first mission is already
        // claimed, so an unstarted mission appears only once the real player state has arrived.
        await Expect(DailyMission("Play 1 game")).ToContainTextAsync("0 / 1", new() { Timeout = BootTimeoutMs });
        await Expect(DailyMission("Play 1 game").GetByTestId("mission-play")).ToBeVisibleAsync();

        Assert.That(await Page.GetByTestId("mission").CountAsync(), Is.EqualTo(3), "a fresh player has three daily missions");
        Assert.That(await Page.GetByTestId("weekly-mission").CountAsync(), Is.EqualTo(2), "and two weekly ones");

        int coinsBefore = await ReadBalanceAsync("coins");

        // ---- One real game against the live server ----
        await PlayFromTheNavigationBarAsync("the mission event's game");
        await PlayWholeHandAsync();
        await LeaveTheResultsAsync();

        // ---- The mission moved, and it moved on the server ----
        // Navigate through the Events hub instead of loading the page. The progress shown is the server's either
        // way, because the server records the match with the PlayerRecordMatchResult server action, which reaches
        // the client only after the server executes it. The wait for the claim button waits for that action. The
        // reload further down checks that the claim persisted.
        await Page.GetByTestId("nav-events").ClickAsync();
        await Page.GetByTestId("feature-missions-action").ClickAsync();
        await Expect(DailyMission("Play 1 game").GetByTestId("mission-claim")).ToBeVisibleAsync(new() { Timeout = BootTimeoutMs });
        await Expect(DailyMission("Play 1 game")).ToContainTextAsync("1 / 1");
        await Expect(DailyMission("Play 3 games")).ToContainTextAsync("1 / 3");
        // One game advances the weekly set as well as the daily one.
        await Expect(Page.GetByTestId("weekly-mission").First).ToContainTextAsync("1 / 10");

        Assert.That(await ReadBalanceAsync("coins"), Is.EqualTo(coinsBefore), "finishing a mission pays nothing; the claim does");

        // ---- Claim it ----
        await DailyMission("Play 1 game").GetByTestId("mission-claim").ClickAsync();
        await Expect(Page.GetByTestId("reward-reveal")).ToBeVisibleAsync(new() { Timeout = TurnTimeoutMs });
        await Expect(Page.GetByTestId("reward-reveal")).ToContainTextAsync("100");
        await Page.GetByTestId("reward-continue").ClickAsync();

        await Expect(DailyMission("Play 1 game").GetByTestId("mission-claimed")).ToBeVisibleAsync();
        Assert.That(await ReadBalanceAsync("coins"), Is.EqualTo(coinsBefore + 100), "the balance did not move by the reward");

        // ---- The reward is paid once. A reload rebuilds the client from the server's copy of the player, so
        //      the page shows committed state instead of the client's prediction.
        await ReloadOnStackAsync(Page);
        await Expect(DailyMission("Play 3 games")).ToContainTextAsync("1 / 3", new() { Timeout = BootTimeoutMs });

        await Expect(DailyMission("Play 1 game").GetByTestId("mission-claimed")).ToBeVisibleAsync();
        await Expect(DailyMission("Play 1 game").GetByTestId("mission-claim")).ToHaveCountAsync(0);
        Assert.That(await ReadBalanceAsync("coins"), Is.EqualTo(coinsBefore + 100), "the reward was paid twice, or not at all");

        AssertTheTimelinesNeverDiverged();
        AssertTheSessionRanConsistencyChecks();
    }
}
