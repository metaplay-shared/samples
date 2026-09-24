using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;
using System.Text.RegularExpressions;

namespace WebClient.Tests;

/// <summary>
/// Edge cases of mouse drag scrolling (<c>WebClient/wwwroot/drag-scroll.js</c>), where the script must decide
/// between a scroll and a press. <see cref="ShellPageTests"/> covers the basic cases. This fixture covers a press
/// under an open modal, a drag that moves the pointer but not the surface, and a gesture that the browser
/// interrupts or ends without an event.
/// <para>
/// The Home tests need the live game server, because offline mode opens a table and never shows Home. The
/// odds-sheet tests run offline. No test taps PLAY, so the fixture needs no <see cref="LiveServerLocks"/> lock.
/// </para>
/// </summary>
[TestFixture]
public class DragScrollPageTests : PlaywrightPageTest
{
    private static readonly string HomeUrl  = ClientUrl();
    // Offline, because the odds sheet is client-only and no test here depends on the game server.
    private static readonly string WheelUrl = ClientUrl("/events/spin", "env=offline");

    /// <summary>The shell's scroll container, which every meta screen is drawn inside.</summary>
    private const string MainSelector = ".m-shell__main";

    private async Task OpenHomeAsync()
    {
        await Page.GotoAsync(HomeUrl);
        await Expect(Page.GetByTestId("primary-nav")).ToBeVisibleAsync(new() { Timeout = BootTimeoutMs });
        await WaitForLiveSessionAsync();
    }

    /// <summary>
    /// Opens the wheel page offline and opens the odds sheet over it.
    /// <para>
    /// It waits for the offline session before opening the sheet. The shell draws fixture data until a session
    /// arrives, and the re-render when it arrives would change the layout while a test measures it.
    /// </para>
    /// </summary>
    private async Task OpenOddsSheetAsync()
    {
        await Page.GotoAsync(WheelUrl);
        await WaitForLiveSessionAsync();

        await Page.GetByTestId("prize-details").ClickAsync();
        await Expect(Page.GetByTestId("odds-sheet")).ToBeVisibleAsync();

        // The panel opens with the animate-pop-in animation and its scrim with a fade (ModalPanel.razor). A box
        // measured while they run is off by a few pixels, by an amount that varies between runs, so wait until
        // both have finished.
        await Page.WaitForFunctionAsync(
            "() => { const sheet = document.querySelector('[data-testid=\"odds-sheet\"]');" +
            " const panel = sheet?.querySelector('.pnl__panel');" +
            " return !!panel && [sheet, panel].every(el => el.getAnimations().every(a => a.playState === 'finished')); }");
    }

    /// <summary>The <c>scrollTop</c> of the element matching <paramref name="selector"/>, rounded to whole pixels.</summary>
    private Task<int> ScrollTopAsync(string selector) => Page.EvaluateAsync<int>(
        $"() => Math.round(document.querySelector('{selector}').scrollTop)");

    /// <summary>The scrollable distance of the element matching <paramref name="selector"/>, in pixels.</summary>
    private Task<int> OverflowAsync(string selector) => Page.EvaluateAsync<int>(
        $"() => {{ const el = document.querySelector('{selector}'); return el.scrollHeight - el.clientHeight; }}");

    /// <summary>The computed cursor of the element matching <paramref name="selector"/>, including inherited values.</summary>
    private Task<string> CursorAsync(string selector) => Page.EvaluateAsync<string>(
        $"() => getComputedStyle(document.querySelector('{selector}')).cursor");

    private async Task<LocatorBoundingBoxResult> BoxOfAsync(ILocator locator, string what)
    {
        LocatorBoundingBoxResult? box = await locator.BoundingBoxAsync();
        Assert.That(box, Is.Not.Null, $"the {what} is not on screen");
        return box!;
    }

    /// <summary>
    /// Scrolls Home to the bottom and returns the centre of the Spin wheel shortcut, which is in the last block on
    /// the page and so is visible there. Also returns the scroll position.
    /// <para>
    /// At the bottom, a drag in one direction scrolls the surface and a drag in the other direction cannot. Tests
    /// use this to separate pointer movement from surface movement.
    /// </para>
    /// </summary>
    private async Task<(float X, float Y, int MaxScroll)> ParkAtTheBottomOnAShortcutAsync()
    {
        int maxScrollTop = await Page.EvaluateAsync<int>(
            $"() => {{ const m = document.querySelector('{MainSelector}'); m.scrollTop = m.scrollHeight;"
            + " return Math.round(m.scrollTop); }");

        Assert.That(maxScrollTop, Is.GreaterThan(100),
            "Home has almost nothing to scroll at this size, so a drag could not tell a scroll from a press");

        LocatorBoundingBoxResult box = await BoxOfAsync(Page.GetByTestId("shortcut-spinwheel"), "Spin wheel shortcut");
        return (box.X + box.Width / 2, box.Y + box.Height / 2, maxScrollTop);
    }

    // -----------------------------------------------------------------------------------------------------
    // A modal blocks drag scrolling of the surface behind it
    // -----------------------------------------------------------------------------------------------------

    /// <summary>
    /// A drag on a sheet's scrim scrolls nothing and still closes the sheet. The scrim is a DOM descendant of the
    /// shell's scroll container, placed over it only by <c>position: fixed</c>, so a script that scrolled the first
    /// scrollable ancestor would scroll the page behind the dialog. The test checks three failures: the page
    /// scrolls, the dragging cursor shows, and the drag suppresses the click that closes the sheet. The viewport is
    /// short enough that the page can scroll, so the no-scroll assertion is not trivially true.
    /// </summary>
    [Test]
    public async Task APressOnASheetsScrimScrollsNothingAndStillDismissesIt()
    {
        await Page.SetViewportSizeAsync(1440, 800);
        await OpenOddsSheetAsync();

        Assert.That(await OverflowAsync(MainSelector), Is.GreaterThan(100),
            "the wheel page fits at this size, so nothing behind the sheet could have scrolled either way");

        LocatorBoundingBoxResult scrim = await BoxOfAsync(Page.GetByTestId("odds-sheet"), "scrim");
        LocatorBoundingBoxResult panel = await BoxOfAsync(Page.Locator(".pnl__panel"), "sheet panel");

        float gap = panel.Y - scrim.Y;
        Assert.That(gap, Is.GreaterThan(240),
            "the panel leaves too little scrim above it to press on at this size");

        float x = scrim.X + scrim.Width / 2;
        float y = panel.Y - gap / 2;
        int before = await ScrollTopAsync(MainSelector);

        // The page behind the sheet cannot be dragged, so the scrim must not show the grab cursor.
        await Page.Mouse.MoveAsync(x, y);
        Assert.That(await CursorAsync(".pnl"), Is.EqualTo("default"),
            "the scrim offers the grab hand for a drag it will not perform");
        Assert.That(await Page.EvaluateAsync<bool>(
                $"() => document.querySelector('{MainSelector}').classList.contains('is-drag-scrollable')"),
            Is.False, "the surface behind the sheet still counts itself draggable");

        await Page.Mouse.DownAsync();
        for (int step = 1; step <= 5; step++)
            await Page.Mouse.MoveAsync(x, y - 20 * step);
        await Page.Mouse.UpAsync();

        Assert.That(await ScrollTopAsync(MainSelector), Is.EqualTo(before),
            "a drag on the scrim scrolled the page under the open sheet");
        Assert.That(await Page.EvaluateAsync<bool>("() => document.body.classList.contains('is-drag-scrolling')"),
            Is.False, "a drag on the scrim put the whole app into its dragging state");

        await Expect(Page.GetByTestId("odds-sheet")).ToHaveCountAsync(0);
    }

    /// <summary>
    /// A drag inside the sheet's panel scrolls the panel, and the surface behind it does not move.
    /// <para>
    /// The panel is itself a drag-scroll surface, so the modal check must not block presses inside the dialog.
    /// The viewport is short enough that the odds table overflows the panel, so the panel has something to scroll.
    /// </para>
    /// </summary>
    [Test]
    public async Task TheSheetsOwnPanelStillDragsAndTheSurfaceBehindDoesNot()
    {
        await Page.SetViewportSizeAsync(390, 400);
        await OpenOddsSheetAsync();

        int panelOverflow = await OverflowAsync(".pnl__panel");
        Assert.That(panelOverflow, Is.GreaterThan(40),
            "the odds table fits the panel at this size, so there was nothing here to drag");

        LocatorBoundingBoxResult panel = await BoxOfAsync(Page.Locator(".pnl__panel"), "sheet panel");
        float x = panel.X + panel.Width / 2;
        float y = panel.Y + panel.Height / 2;

        int behind = await ScrollTopAsync(MainSelector);
        // A whole number of pixels per step, so the 1:1 assertion below is exact.
        int distance = Math.Min(panelOverflow, 60) / 6 * 6;

        await Page.Mouse.MoveAsync(x, y);
        Assert.That(await CursorAsync(".pnl__panel"), Is.EqualTo("grab"),
            "the panel has more content than room and does not offer the drag");

        await Page.Mouse.DownAsync();
        for (int step = 1; step <= 6; step++)
            await Page.Mouse.MoveAsync(x, y - distance * step / 6f);
        await Page.Mouse.UpAsync();

        Assert.That(await ScrollTopAsync(".pnl__panel"), Is.EqualTo(distance),
            "a drag inside the sheet did not move the panel one for one");
        Assert.That(await ScrollTopAsync(MainSelector), Is.EqualTo(behind),
            "a drag inside the sheet also scrolled the page behind it");
    }

    /// <summary>
    /// A drag-scroll surface whose content fits does not show the grab cursor.
    /// <para>
    /// The script does not scroll a surface whose content fits, so a grab cursor there would promise a drag that
    /// does nothing. At a full-height viewport the odds table fits inside the sheet's panel.
    /// </para>
    /// </summary>
    [Test]
    public async Task ASurfaceWithNothingToScrollOffersNoGrabHand()
    {
        await Page.SetViewportSizeAsync(1440, 900);
        await OpenOddsSheetAsync();

        Assert.That(await OverflowAsync(".pnl__panel"), Is.LessThanOrEqualTo(2),
            "the odds table overflows the panel at this size; this test needs one that fits");

        LocatorBoundingBoxResult panel = await BoxOfAsync(Page.Locator(".pnl__panel"), "sheet panel");
        await Page.Mouse.MoveAsync(panel.X + panel.Width / 2, panel.Y + panel.Height / 2);

        Assert.That(await CursorAsync(".pnl__panel"), Is.EqualTo("default"),
            "a panel that has nothing to scroll still offers the drag");
    }

    // -----------------------------------------------------------------------------------------------------
    // Deciding between a scroll and a press
    // -----------------------------------------------------------------------------------------------------

    /// <summary>
    /// A drag that moves the pointer but not the surface counts as a press, and activates the control it started
    /// and ended on.
    /// <para>
    /// The surface is at the bottom of its range and the drag moves the pointer up, which would scroll further
    /// down, so the surface cannot move. If the script decided by pointer distance alone, a shortcut pressed
    /// with a small wobble at the end of a screen would do nothing.
    /// </para>
    /// </summary>
    [Test]
    public async Task ADragThatMovesNothingStillActivatesTheControlUnderIt()
    {
        await Page.SetViewportSizeAsync(1440, 900);
        await OpenHomeAsync();

        (float x, float y, int maxScrollTop) = await ParkAtTheBottomOnAShortcutAsync();

        // Dragging up would scroll further down, which is not possible at the bottom of the range.
        await Page.Mouse.MoveAsync(x, y);
        await Page.Mouse.DownAsync();
        for (int step = 1; step <= 6; step++)
            await Page.Mouse.MoveAsync(x, y - 10 * step);
        for (int step = 6; step >= 0; step--)
            await Page.Mouse.MoveAsync(x, y - 10 * step);

        Assert.That(await ScrollTopAsync(MainSelector), Is.EqualTo(maxScrollTop),
            "the surface moved, so this gesture was a scroll and the test proves nothing");

        await Page.Mouse.UpAsync();

        await Expect(Page).ToHaveURLAsync(new Regex(@"/events/spin"), new() { Timeout = BootTimeoutMs });
    }

    /// <summary>
    /// A right-button press during a drag does not let the following left release activate the control under
    /// the pointer.
    /// <para>
    /// Only the primary button starts and ends a drag. If a right press ended the drag, the script would stop
    /// suppressing the click, and the left release over the shortcut would open a screen the player was only
    /// scrolling past.
    /// </para>
    /// </summary>
    [Test]
    public async Task ASecondaryPressDuringADragDoesNotLetTheReleaseActivateAControl()
    {
        await Page.SetViewportSizeAsync(1440, 900);
        await OpenHomeAsync();

        (float x, float y, int maxScrollTop) = await ParkAtTheBottomOnAShortcutAsync();

        // Dragging down scrolls up, which is possible from the bottom of the range.
        await Page.Mouse.MoveAsync(x, y);
        await Page.Mouse.DownAsync();
        for (int step = 1; step <= 6; step++)
            await Page.Mouse.MoveAsync(x, y + 10 * step);

        Assert.That(await ScrollTopAsync(MainSelector), Is.LessThan(maxScrollTop),
            "the drag did not scroll, so it was never the kind of gesture this test is about");

        // Return to the start point, so the release is over the shortcut the press started on.
        for (int step = 6; step >= 0; step--)
            await Page.Mouse.MoveAsync(x, y + 10 * step);

        await Page.Mouse.DownAsync(new() { Button = MouseButton.Right });
        await Page.Mouse.UpAsync(new() { Button = MouseButton.Right });
        await Page.Mouse.UpAsync();

        await Page.WaitForTimeoutAsync(500);
        Assert.That(RouteOf(Page.Url), Is.EqualTo("/"),
            "a right press during the drag let the left release open the shortcut");
    }

    /// <summary>
    /// When the page gets no release event, the surface does not keep following the pointer.
    /// <para>
    /// The script captures the pointer only after the drag threshold is passed. Before that, a button released
    /// outside the window, or while another application has focus, produces no <c>pointerup</c> and no
    /// <c>pointercancel</c>. The next event the page gets is a <c>pointermove</c> with no buttons pressed. The test
    /// dispatches that event directly, because a browser test cannot release the button outside the page.
    /// </para>
    /// </summary>
    [Test]
    public async Task AReleaseThePageNeverHearsDoesNotGlueTheSurfaceToThePointer()
    {
        await Page.AddInitScriptAsync(
            "window.addEventListener('pointerdown', e => { window.__pointerId = e.pointerId; }, true);");

        await Page.SetViewportSizeAsync(1440, 900);
        await OpenHomeAsync();

        LocatorBoundingBoxResult main = await BoxOfAsync(Page.Locator(MainSelector), "scroll surface");
        float x = main.X + main.Width / 2;
        float y = main.Y + main.Height / 2;

        Assert.That(await ScrollTopAsync(MainSelector), Is.Zero, "Home did not start at the top");

        // Press and move less than the threshold, so the script tracks the pointer but has not captured it.
        await Page.Mouse.MoveAsync(x, y);
        await Page.Mouse.DownAsync();
        await Page.Mouse.MoveAsync(x, y - 2);

        await Page.EvaluateAsync(
            $"() => document.querySelector('{MainSelector}').dispatchEvent(new PointerEvent('pointermove', " +
            "{ pointerId: window.__pointerId, pointerType: 'mouse', buttons: 0, bubbles: true }))");

        // Move the pointer well past the threshold.
        for (int step = 1; step <= 6; step++)
            await Page.Mouse.MoveAsync(x, y - 20 * step);

        Assert.That(await ScrollTopAsync(MainSelector), Is.Zero,
            "the surface followed a pointer with no button down");
        Assert.That(await Page.EvaluateAsync<bool>("() => document.body.classList.contains('is-drag-scrolling')"),
            Is.False, "the app is in its dragging state with no button down");

        await Page.Mouse.UpAsync();
    }
}
