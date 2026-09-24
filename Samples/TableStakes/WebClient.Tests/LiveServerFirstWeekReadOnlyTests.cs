using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

namespace WebClient.Tests;

/// <summary>
/// The first-week event's screens in a live session, without playing a game (<c>docs/first-week-event.md</c>):
/// the hub card shows the player's own week, and a <c>?meta=</c> scenario keeps its slice after the session
/// connects. Markup is tested in <c>FirstWeekRenderTests</c>, so these tests check only what needs a live session.
/// They never tap PLAY or join a league, so they take no <see cref="LiveServerLocks"/> lock. A test that does
/// belongs in <see cref="LiveServerFirstWeekTests"/>.
/// </summary>
[TestFixture]
public class LiveServerFirstWeekReadOnlyTests : PlaywrightPageTest
{
    /// <summary>
    /// The Events hub card shows the player's own week rather than the fixture's. A fresh player is on day one,
    /// while the fixture puts the player on day four.
    /// </summary>
    [Test]
    public async Task TheEventsCardReadsThePlayersOwnWeek()
    {
        await Page.GotoAsync(ClientUrl("/events"));

        ILocator card = Page.GetByTestId("feature-firstweekevent");
        await Expect(card).ToContainTextAsync("Day 1 of 7", new() { Timeout = BootTimeoutMs });
        await Expect(card).ToContainTextAsync("Complete 1 match · 0/1");
    }

    /// <summary>
    /// A scenario that declares the first-week slice keeps showing it after the live session connects, and the
    /// screen shows the authored notice.
    /// <para>
    /// The owed-reward screen itself is tested in <c>FirstWeekRenderTests</c>. This test checks that live state
    /// does not replace the scenario's slice when the session connects, which only a live session can show.
    /// </para>
    /// </summary>
    [Test]
    public async Task TheOwedRewardScenarioIsStillOnScreenWithASession()
    {
        await Page.GotoAsync(ClientUrl("/events/first-week", "meta=first-week-reward"));

        // Live state would replace the scenario when the session connects. Before that, the shell shows the
        // fixture anyway, so the assertions below must run after the session is live.
        await WaitForLiveSessionAsync();
        ILocator session = Page.GetByTestId("session");
        await Expect(session).ToHaveAttributeAsync("data-authored", "dailyreward firstweek");

        // The screen tells the player that it shows authored data.
        await Expect(Page.GetByTestId("authored-notice")).ToBeVisibleAsync();

        // Day two's reward is still unclaimed after day two ended, and day three ended unfinished. A real player
        // cannot reach these states in a test, because a day lasts 24 hours.
        await Expect(FirstWeekDay(2).GetByTestId("first-week-day-state")).ToHaveTextAsync("Reward ready");
        await Expect(FirstWeekDay(2).GetByTestId("first-week-day-claim")).ToBeVisibleAsync();
        await Expect(FirstWeekDay(3).GetByTestId("first-week-day-state")).ToHaveTextAsync("Missed · This day has ended");

        // The Events badge on the navigation bar in a live session. BadgePolicyTests covers whether the state
        // earns a badge, and FirstWeekRenderTests covers PrimaryNav drawing it.
        //
        // Uses a page load rather than a navigation tap, because the scenario is read from the query string only
        // at boot and a tap would drop it.
        await Page.GotoAsync(ClientUrl("/events", "meta=first-week-reward"));
        await WaitForLiveSessionAsync();
        await Expect(Page.GetByTestId("nav-events").GetByTestId("badge")).ToBeVisibleAsync();
    }
}
