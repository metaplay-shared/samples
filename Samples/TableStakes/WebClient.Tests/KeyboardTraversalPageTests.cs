using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

namespace WebClient.Tests;

/// <summary>
/// Keyboard focus in dialogs (<c>WebClient/wwwroot/modal-focus.js</c>): opening a dialog moves focus into it,
/// Tab cannot leave it while it is open, and closing it puts focus where the keyboard can continue. Focus left on
/// <c>&lt;body&gt;</c> is a dead end, because the document itself never scrolls (<c>app.css</c>). The tests cover
/// the three ways a <c>Panel</c> is opened: <c>MetaSheet</c>, <c>RewardReveal</c> and the card table's help dialog.
/// Two tests need the live game server: offline mode has no claim handler for the reward reveal, and it redirects
/// Home, where the help dialog opens, to a table (<c>docs/web-client.md</c>, "Offline mode"). No test taps PLAY,
/// so the fixture needs no <see cref="LiveServerLocks"/> lock. Each test's fresh guest account has a claimable reward.
/// </summary>
[TestFixture]
public class KeyboardTraversalPageTests : PlaywrightPageTest
{
    /// <summary>The focused element's <c>data-testid</c>, or its tag name if it has none.</summary>
    private Task<string> FocusedAsync() =>
        Page.EvaluateAsync<string>("document.activeElement.getAttribute('data-testid') || document.activeElement.tagName");

    /// <summary>
    /// How long <see cref="AssertFocusReachesAsync"/> waits for focus to move. <c>modal-focus.js</c> updates focus
    /// once per animation frame, so focus moves one frame after the DOM change that caused it.
    /// </summary>
    private const int FocusSettleTimeoutMs = 5000;

    /// <summary>
    /// Waits until focus is on <paramref name="expected"/>. If it does not get there, fails with
    /// <paramref name="because"/> and the element that has focus.
    /// <para>
    /// The tests wait for a DOM change before calling this, and focus moves one frame after that change. Reading
    /// <c>document.activeElement</c> directly after the DOM wait would race with the focus move.
    /// </para>
    /// </summary>
    private async Task AssertFocusReachesAsync(string expected, string because)
    {
        try
        {
            await Page.WaitForFunctionAsync(
                "expected => (document.activeElement?.getAttribute('data-testid') || document.activeElement?.tagName) === expected",
                expected,
                new() { Timeout = FocusSettleTimeoutMs });
        }
        catch (PlaywrightException)
        {
            // A timeout or a closed page both mean focus did not arrive, so report where focus is.
            Assert.Fail($"{because} — focus is on '{await FocusedAsync()}', expected '{expected}'");
        }
    }

    /// <summary>
    /// Presses Tab <paramref name="presses"/> times and asserts after each press that focus is still on
    /// <paramref name="expected"/>. The dialogs tested have one control, so every press must return to it.
    /// <para>
    /// Focus is read immediately instead of waited for. The trap runs in the document's keydown listener, which
    /// has moved focus before Playwright's key press returns. A retrying wait could also not tell "never left"
    /// from "left and came back".
    /// </para>
    /// </summary>
    private async Task AssertTabStaysOnAsync(string expected, int presses = 4)
    {
        for (int i = 0; i < presses; i++)
        {
            await Page.Keyboard.PressAsync("Tab");
            Assert.That(await FocusedAsync(), Is.EqualTo(expected), $"Tab {i + 1} escaped the open dialog");
        }
    }

    /// <summary>
    /// The odds sheet: opening it moves focus to its close button, Tab cannot leave it, and closing it returns
    /// focus to the button that opened it.
    /// <para>
    /// This test runs offline, because the client opens and closes the sheet on its own and the button that
    /// opened it is still there afterwards.
    /// </para>
    /// </summary>
    [Test]
    public async Task TheOddsSheetTrapsFocusAndReturnsItOnClose()
    {
        await Page.GotoAsync(ClientUrl("/events/spin", "env=offline"));
        await WaitForLiveSessionAsync();
        await Expect(Page.GetByTestId("prize-details")).ToBeVisibleAsync(new() { Timeout = BootTimeoutMs });

        await Page.GetByTestId("prize-details").ClickAsync();
        await Expect(Page.GetByTestId("odds-sheet")).ToBeVisibleAsync();

        await AssertFocusReachesAsync("sheet-close", "opening the sheet did not move focus into it");

        await AssertTabStaysOnAsync("sheet-close");

        await Page.GetByTestId("sheet-close").ClickAsync();
        await Expect(Page.GetByTestId("odds-sheet")).ToHaveCountAsync(0);

        await AssertFocusReachesAsync("prize-details",
            "closing the sheet did not give focus back to the control that opened it");
    }

    /// <summary>
    /// The reward reveal, against the live server. It opens on a waiting stage with nothing to focus, and Continue
    /// is added to the same dialog when the grant commits, so focus on Continue shows that the script reacts to a
    /// control added after opening. The waiting stage is not asserted, because a fast server can finish it between
    /// two polls. Claiming removes the Claim button that opened the reveal, so on close focus must move to the
    /// page's <c>main</c> element.
    /// </summary>
    [Test]
    public async Task TheRewardRevealTakesFocusWhenItsControlArrivesAndLandsSomewhereOnClose()
    {
        await Page.GotoAsync(ClientUrl("/events/daily"));
        await WaitForLiveSessionAsync();
        await Expect(Page.GetByTestId("daily-claim")).ToBeVisibleAsync(new() { Timeout = BootTimeoutMs });

        await Page.GetByTestId("daily-claim").ClickAsync();
        await Expect(Page.GetByTestId("reward-continue")).ToBeVisibleAsync(new() { Timeout = BootTimeoutMs });

        await AssertFocusReachesAsync("reward-continue",
            "focus never reached Continue, so it was left on the panel it landed on when the reveal opened");

        await AssertTabStaysOnAsync("reward-continue");

        await Page.GetByTestId("reward-continue").ClickAsync();
        await Expect(Page.GetByTestId("reward-reveal")).ToHaveCountAsync(0);

        // The claim removed the Claim button, so focus must use the fallback.
        await Expect(Page.GetByTestId("daily-claimed")).ToBeVisibleAsync();
        await Expect(Page.GetByTestId("daily-claim")).ToHaveCountAsync(0);

        await AssertFocusReachesAsync("MAIN",
            "closing the reveal left focus at the document, which the shell never scrolls, rather than on the "
            + "page the player is still looking at");
    }

    /// <summary>
    /// The hero banner's help dialog, which uses the card table's panel: opening it moves focus in, Tab cannot
    /// leave it, and closing it returns focus to the help button. What the dialog says is asserted in
    /// <c>HeroBannerRenderTests</c>.
    /// <para>
    /// It is the only table dialog that can be opened without tapping PLAY. The other table dialogs use the same
    /// <c>Panel</c> component with the same <c>aria-modal</c> and <c>tabindex="-1"</c>, but reaching them requires
    /// entering the matchmaking queue and waiting for a seat.
    /// </para>
    /// </summary>
    [Test]
    public async Task TheHelpDialogTrapsFocusAndReturnsItOnClose()
    {
        await Page.GotoAsync(ClientUrl("/"));
        await WaitForLiveSessionAsync();
        await Expect(Page.GetByTestId("help")).ToBeVisibleAsync(new() { Timeout = BootTimeoutMs });

        await Page.GetByTestId("help").ClickAsync();
        await Expect(Page.GetByTestId("help-dialog")).ToBeVisibleAsync();

        await AssertFocusReachesAsync("help-close", "opening the help dialog did not move focus into it");

        // The dialog opens over the hero without replacing it, so PLAY is still visible.
        await Expect(Page.GetByTestId("play")).ToBeVisibleAsync();

        await AssertTabStaysOnAsync("help-close");

        await Page.GetByTestId("help-close").ClickAsync();
        await Expect(Page.GetByTestId("help-dialog")).ToHaveCountAsync(0);

        await AssertFocusReachesAsync("help",
            "closing the help dialog did not give focus back to the control that opened it");
    }
}
