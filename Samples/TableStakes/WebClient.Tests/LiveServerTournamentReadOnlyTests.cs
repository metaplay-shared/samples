using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

namespace WebClient.Tests;

/// <summary>
/// The seasonal tournament's screens as a player who has <b>not entered</b> sees them, against a live game server
/// (<c>docs/seasonal-tournament.md</c>): the invitation a fresh player sees, and scenarios that keep showing after
/// the session connects. <c>CompeteRenderTests</c> checks the markup of those states. No test here taps Play or
/// joins the league, so none takes a <see cref="LiveServerLocks"/> lock. A test that does belongs in
/// <see cref="LiveServerTournamentTests"/>.
/// </summary>
[TestFixture]
public class LiveServerTournamentReadOnlyTests : PlaywrightPageTest
{
    /// <summary>
    /// The error and loading scenarios keep showing after the live session connects. <c>CompeteRenderTests</c>
    /// checks what the two states render. This test checks that the real tournament state does not replace a
    /// scenario that claims the tournament slice (<c>ScenarioPinTests</c>). These scenarios do not claim the player
    /// identity, so the test first waits for Home to show the server-generated name, which proves the scenario is
    /// drawn over a live session.
    /// </summary>
    [TestCase("error", "state-error")]
    [TestCase("loading", "state-loading")]
    public async Task TheErrorAndLoadingScenariosSurviveASession(string scenario, string expected)
    {
        // The identity row shows the fixture's placeholder name until the session replaces it, so a
        // server-generated name proves the session is live. WaitForRealPlayerNameAsync explains how the two
        // names are told apart.
        await Page.GotoAsync(ClientUrl());
        await WaitForRealPlayerNameAsync();

        // The client reads the scenario from the query string once at boot, so this must be a page load and not
        // an in-app navigation.
        await Page.GotoAsync(ClientUrl("/compete", $"meta={scenario}"));

        await WaitForLiveSessionAsync();
        await Expect(Page.GetByTestId("hud-wallet")).ToBeVisibleAsync(new() { Timeout = BootTimeoutMs });

        await Expect(Page.GetByTestId(expected)).ToBeVisibleAsync(new() { Timeout = BootTimeoutMs });
        await Expect(Page.GetByTestId("tournament-join")).ToHaveCountAsync(0);
    }

    /// <summary>
    /// Before entering, Compete shows the invitation and <b>no empty standings table</b>. An empty table suggests
    /// that nobody is playing, and the standings cannot be drawn before joining because the server picks the group.
    /// <para>
    /// No clock says the season ended. Having no season is different from having a finished one, and "Season
    /// ended" next to the invitation would contradict it. Unit tests cover the pure function that decides the
    /// phase. This test covers the component, which must pass an absent deadline to that function as absent and
    /// not as zero.
    /// </para>
    /// <para>
    /// The test reads Compete and Profile in <b>one session</b> and checks that they agree: Profile must not show a
    /// rank while Compete invites the player to enter. <c>ProfileViewTests</c> covers how Profile describes the
    /// standing.
    /// </para>
    /// </summary>
    [Test]
    public async Task BeforeJoining_ThereIsNoEmptyTable()
    {
        await OpenCompeteAsync();

        await Expect(Page.GetByTestId("tournament-rules")).ToBeVisibleAsync();
        await Expect(Page.GetByTestId("tournament-join")).ToBeVisibleAsync();

        // With no standings before entering, the invitation names the first-place prize.
        await Expect(Page.GetByTestId("tournament-first-prize")).ToBeVisibleAsync();

        // Neither the standings nor the placement block is shown.
        await Expect(Page.GetByTestId("standings")).ToHaveCountAsync(0, new() { Timeout = 5000 });
        await Expect(Page.GetByTestId("tournament-placement")).ToHaveCountAsync(0);

        await Expect(Page.GetByTestId("countdown-expired")).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("tournament-summary")).Not.ToContainTextAsync("Season ended");

        // Open Profile through in-app navigation, which keeps the session, so both screens describe the same
        // player.
        await Page.GetByTestId("nav-home").ClickAsync();
        await Page.GetByTestId("home-profile-action").ClickAsync();
        await Expect(Page.GetByTestId("page-title")).ToHaveTextAsync("Profile");

        await Expect(Page.GetByTestId("competition-state")).ToHaveTextAsync("Not in a competition");
        await Expect(Page.GetByTestId("preview-standing")).ToHaveTextAsync("Unranked");
    }
}
