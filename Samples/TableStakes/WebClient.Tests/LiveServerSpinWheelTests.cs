using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;
using System.Text.RegularExpressions;
using WebClient.Meta.Ui;

namespace WebClient.Tests;

/// <summary>
/// The spin wheel against a <b>live game server</b>: a fresh player spends their starting token, the wheel stops
/// on the sector the server drew, the wallet changes once, and the result survives a reload. The server decides the
/// sector, prize and balance, so a fixture-driven version would pass with the server off. <c>SpinWheelRenderTests</c>
/// covers the odds sheet's layout and the unpublished wheel. The <c>spinMs</c> query parameter shortens the spin
/// animation (<c>docs/testing.md</c>, "Forcing timers").
/// No test here taps Play, so these tests need no <see cref="LiveServerLocks"/> lock. Every Playwright test gets a
/// fresh guest account with its own token.
/// </summary>
[TestFixture]
public class LiveServerSpinWheelTests : PlaywrightPageTest
{
    /// <summary>The wheel page, with the spin animation shortened so tests do not wait for it.</summary>
    private static string WheelUrl => ClientUrl("/events/spin", "spinMs=1");

    /// <summary>
    /// The <c>data-hit</c> value the wheel shows for a landed sector's <paramref name="tier"/>. The test repeats the
    /// wheel's mapping so the assertion does not depend on the wheel's CSS class names.
    /// </summary>
    private static string HitOfTier(string tier) => tier switch
    {
        "nothing"   => "blank",
        "spinagain" => "respin",
        _           => "reward",
    };

    /// <summary>
    /// Open the wheel and wait until the session is live, not only until the screen has drawn. The fixture wheel
    /// looks the same before the session connects, and a tap on it sends no request, so the test would time out.
    /// </summary>
    /// <param name="query">The page's query. The default shortens the spin animation.</param>
    private async Task OpenWheelAsync(string query = "spinMs=1")
    {
        await Page.GotoAsync(ClientUrl("/events/spin", query));
        await WaitForLiveSessionAsync();

        await Expect(Page.GetByTestId("spin")).ToHaveTextAsync(ButtonLabels.Spin, new() { Timeout = BootTimeoutMs });
        await Expect(Page.GetByTestId("spin-cost")).ToHaveTextAsync("1 token");
        await Expect(Page.GetByTestId("spin-available")).ToHaveTextAsync("1 spin available");
    }

    /// <summary>
    /// The angle the wheel face is drawn at, in degrees, read from the computed transform instead of the element's
    /// custom properties. The inline properties keep their value even when the stylesheet fails to apply them.
    /// <para>
    /// The angle comes from the transform matrix, so full turns cancel out. The range is (-180, 180]. A face with no
    /// transform reads as zero, so a stylesheet that never applied fails the angle assertion instead of throwing.
    /// </para>
    /// </summary>
    private async Task<double> ReadFaceAngleAsync() =>
        await Page.GetByTestId("wheel-face").EvaluateAsync<double>(
            "el => { const t = getComputedStyle(el).transform; if (t === 'none') return 0; " +
            "const m = new DOMMatrixReadOnly(t); return Math.atan2(m.b, m.a) * 180 / Math.PI; }");

    /// <summary>
    /// The face angle, in degrees, that puts the centre of <paramref name="sector"/> under the fixed pointer.
    /// Using the centre keeps the check away from sector boundaries.
    /// </summary>
    private static double AngleOfSector(int sector, int numSectors)
    {
        double sectorDegrees = 360.0 / numSectors;
        return -(sector * sectorDegrees + sectorDegrees / 2.0);
    }

    /// <summary>
    /// The shortest distance between two angles, in degrees, in the range [0, 180]. A direct subtraction would fail
    /// for two angles on opposite sides of the ±180 wrap.
    /// </summary>
    private static double AngularDistanceDegrees(double a, double b) =>
        Math.Abs(((a - b) % 360.0 + 540.0) % 360.0 - 180.0);

    /// <summary>Spin once and dismiss the reward reveal.</summary>
    private async Task SpinAndAcknowledgeAsync()
    {
        await Page.GetByTestId("spin").ClickAsync();
        await Expect(Page.GetByTestId("reward-continue")).ToBeVisibleAsync(new() { Timeout = BootTimeoutMs });
        await Page.GetByTestId("reward-continue").ClickAsync();
    }

    /// <summary>
    /// The odds a live player sees come from the published config archive, not from the fixture.
    /// <para>
    /// <c>SpinWheelRenderTests</c> checks the odds sheet's layout against the fixture's table. This test checks only
    /// that a live session draws the percentages from the table the server serves.
    /// <c>Backend/Server.Tests.GameConfigBuildTests.ThePublishedWheelIsTheApprovedBaselineTable</c> checks the
    /// archive's table itself, so changing the wheel's odds requires updating both tests.
    /// </para>
    /// </summary>
    [Test]
    public async Task ThePublishedOddsAreOnTheSheet()
    {
        await OpenWheelAsync();

        await Page.GetByTestId("prize-details").ClickAsync();
        await Expect(Page.GetByTestId("odds-percent")).ToHaveTextAsync(
            new[] { "10%", "20%", "20%", "10%", "10%", "20%", "10%" }, new() { Timeout = BootTimeoutMs });
        await Expect(Page.GetByTestId("odds-total")).ToHaveTextAsync("100%");
    }

    /// <summary>
    /// With reduced motion, the spin result appears almost at once instead of after the full spin animation.
    /// The page ends the spin on its own timer (<c>SpinWheelPage.TurnMs</c>), not on <c>animationend</c>
    /// (<c>PrizeWheel.razor</c> explains why), so the timer must read the motion preference itself. The test sets no
    /// <c>spinMs</c>, so it checks the shipped default, with a limit loose enough for CI scheduling delays.
    /// </summary>
    [Test]
    public async Task ReducedMotionResolvesTheTurnNearInstantly()
    {
        await Page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.Reduce });
        await OpenWheelAsync(query: "");

        System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
        await Page.GetByTestId("spin").ClickAsync();
        await Expect(Page.GetByTestId("reward-title")).ToBeVisibleAsync(new() { Timeout = BootTimeoutMs });
        watch.Stop();

        Assert.That(watch.ElapsedMilliseconds, Is.LessThan(2000),
            "the result took long enough that reduced motion is still waiting out the collapsed animation's " +
            "own clock rather than the browser's own end-of-turn event");

        AssertTheTimelinesNeverDiverged();
    }

    /// <summary>
    /// One spin spends one token, the wheel stops on the sector the server drew, and the balance changes by that
    /// sector's prize. The test also lengthens the hit animation with <c>hitMs</c>, so it can check that the wheel
    /// plays the hit that matches the landed sector's tier before the reveal opens.
    /// </summary>
    [Test]
    public async Task ASpinSpendsTheTokenAndPaysTheSectorTheServerDrew()
    {
        await OpenWheelAsync("spinMs=1&hitMs=4000");

        Assert.That(await ReadBalanceAsync("spintokens"), Is.EqualTo(1), "a fresh player's published starting wallet");
        int coinsBefore = await ReadBalanceAsync("coins");
        int gemsBefore  = await ReadBalanceAsync("gems");

        await Page.GetByTestId("spin").ClickAsync();

        // The spin ends at once, and the landed sector plays its hit animation for the hitMs duration before the
        // reveal opens. The data-hit value must match the landed sector's tier, and the reveal's Continue button
        // must not be on screen while the hit plays.
        ILocator wheel = Page.GetByTestId("prize-wheel");
        await Expect(wheel).ToHaveAttributeAsync("data-hit", new Regex("^(reward|respin|blank)$"),
            new() { Timeout = BootTimeoutMs });

        string hitWinner = await Page.GetByTestId("wheel-face").GetAttributeAsync("data-winner") ?? "-1";
        string hitTier   = await Page.GetByTestId("wheel-sector").Nth(int.Parse(hitWinner)).GetAttributeAsync("data-tier") ?? "";
        await Expect(wheel).ToHaveAttributeAsync("data-hit", HitOfTier(hitTier),
            new() { Timeout = BootTimeoutMs });

        Assert.That(await Page.GetByTestId("reward-continue").CountAsync(), Is.EqualTo(0),
            "the reveal opened while the hit was still playing");

        // The reveal shows its items only after the server's action has executed on this client's timeline. Wait for
        // the Continue button, not the dialog, because the dialog is also shown while the request is in flight.
        // The button appears only after the hit animation ends.
        await Expect(Page.GetByTestId("reward-continue")).ToBeVisibleAsync(new() { Timeout = BootTimeoutMs });

        // The winning sector index is a valid index of the published wheel.
        string winner = await Page.GetByTestId("wheel-face").GetAttributeAsync("data-winner") ?? "-1";
        Assert.That(int.Parse(winner), Is.InRange(0, 9), "the wheel stopped somewhere the table cannot name");

        // The face stopped with the winning sector under the pointer, so the drawn sector, the highlighted sector
        // and the paid prize are the same result.
        int sectors = await Page.GetByTestId("wheel-sector").CountAsync();
        double landed  = await ReadFaceAngleAsync();
        Assert.That(AngularDistanceDegrees(landed, AngleOfSector(int.Parse(winner), sectors)), Is.LessThan(0.5),
            $"the face is not turned to the sector it says it landed on ({winner} of {sectors})");

        // The drawn wheel matches the published table's blank and spin-again sectors, identified by data-tier.
        Assert.That(await Page.Locator("[data-testid=wheel-sector][data-tier=nothing]").CountAsync(),
            Is.EqualTo(1), "the drawn wheel does not carry the published table's single blank");
        Assert.That(await Page.Locator("[data-testid=wheel-sector][data-tier=spinagain]").CountAsync(),
            Is.EqualTo(2), "the drawn wheel does not carry the published table's two spin-agains");

        await Page.GetByTestId("reward-continue").ClickAsync();
        await Expect(Page.GetByTestId("reward-reveal")).ToHaveCountAsync(0);

        // Dismissing the result must not turn the wheel. Only a spin turns it.
        Assert.That(AngularDistanceDegrees(await ReadFaceAngleAsync(), landed), Is.LessThan(0.5),
            "the wheel jumped when the result was dismissed");

        int coinsAfter  = await ReadBalanceAsync("coins");
        int gemsAfter   = await ReadBalanceAsync("gems");
        int tokensAfter = await ReadBalanceAsync("spintokens");

        // The server picks the outcome, so the test checks that the result is one valid outcome with a published
        // amount instead of checking a specific value.
        int coinsWon  = coinsAfter - coinsBefore;
        int gemsWon   = gemsAfter - gemsBefore;

        if (tokensAfter == 1)
        {
            Assert.That(coinsWon, Is.Zero, "the replacement-token sector also paid coins");
            Assert.That(gemsWon, Is.Zero);
        }
        else
        {
            Assert.That(tokensAfter, Is.Zero, "the token was not spent");

            // The blank sector spends the token and grants nothing. The landed sector's tier decides which
            // assertion applies, so a paying sector that grants nothing still fails.
            string winnerTier = await Page.GetByTestId("wheel-sector").Nth(int.Parse(winner)).GetAttributeAsync("data-tier") ?? "";
            if (winnerTier == "nothing")
            {
                Assert.That(coinsWon + gemsWon, Is.Zero, "the blank paid something");
            }
            else
            {
                Assert.That(coinsWon + gemsWon, Is.GreaterThan(0), "a paying sector paid nothing");
                Assert.That(coinsWon is 0 or 100 or 250 or 500 or 1000, Is.True, $"an unpublished coin prize: {coinsWon}");
                Assert.That(gemsWon is 0 or 30, Is.True, $"an unpublished gem prize: {gemsWon}");
                Assert.That(coinsWon == 0 || gemsWon == 0, Is.True, "one sector paid two currencies");
            }
        }

        Assert.That(await Page.GetByTestId("journal-checks").InnerTextAsync(), Is.EqualTo("on"),
            "the SDK's journal checkers were off, so this test cannot prove the timelines agreed");
        AssertTheTimelinesNeverDiverged();
    }

    /// <summary>
    /// The test leaves the reveal by navigating away instead of tapping Done, so the result is committed but not
    /// acknowledged, the same state a connection lost during the reveal leaves. On return, the wheel must show the
    /// result again without paying it again.
    /// </summary>
    [Test]
    public async Task AnInterruptedRevealIsPresentedOnReturnAndPaysNothingExtra()
    {
        await OpenWheelAsync();

        await Page.GetByTestId("spin").ClickAsync();
        await Expect(Page.GetByTestId("reward-continue")).ToBeVisibleAsync(new() { Timeout = BootTimeoutMs });

        int coinsAfterSpin  = await ReadBalanceAsync("coins");
        int gemsAfterSpin   = await ReadBalanceAsync("gems");
        int tokensAfterSpin = await ReadBalanceAsync("spintokens");

        // Leave without acknowledging. The reward is already in the wallet, and only the reveal is pending.
        await Page.GotoAsync(ClientUrl("/events"));
        await WaitForLiveSessionAsync();
        await Expect(Page.GetByTestId("feature-spinwheel")).ToContainTextAsync("Your last spin is waiting");

        // A full page load rebuilds the client from the server's copy of the player model.
        await Page.GotoAsync(WheelUrl);
        await WaitForLiveSessionAsync();

        await Expect(Page.GetByTestId("reward-continue")).ToBeVisibleAsync(new() { Timeout = BootTimeoutMs });

        // A pending receipt shown on arrival plays no hit animation, because the spin happened earlier. The wheel
        // is also drawn at its landed angle without turning, for the same reason.
        Assert.That(await Page.GetByTestId("prize-wheel").GetAttributeAsync("data-hit"), Is.Null,
            "a receipt presented on arrival played a hit");
        Assert.That(await ReadBalanceAsync("coins"), Is.EqualTo(coinsAfterSpin), "the result was paid a second time");
        Assert.That(await ReadBalanceAsync("gems"), Is.EqualTo(gemsAfterSpin));
        Assert.That(await ReadBalanceAsync("spintokens"), Is.EqualTo(tokensAfterSpin));

        // Done acknowledges the result, and it is not shown again.
        await Page.GetByTestId("reward-continue").ClickAsync();
        await Expect(Page.GetByTestId("reward-reveal")).ToHaveCountAsync(0);

        // The acknowledgement is a client action, which reaches the server on the next flush. A page load before
        // that flush would discard the action, and the reveal would correctly appear again.
        await Page.WaitForTimeoutAsync(2000);

        await Page.GotoAsync(WheelUrl);
        await WaitForLiveSessionAsync();
        await Expect(Page.GetByTestId("spin-available")).ToBeVisibleAsync();
        await Expect(Page.GetByTestId("reward-reveal")).ToHaveCountAsync(0);

        Assert.That(await ReadBalanceAsync("coins"), Is.EqualTo(coinsAfterSpin), "an acknowledged result replayed and paid again");
        AssertTheTimelinesNeverDiverged();
    }

    /// <summary>
    /// A double tap spins once. The disabled Spin button absorbs the second tap. The server's guards against a
    /// second request are tested in Server.Tests (WheelSpinGateTests, WheelSpinThrottleTests).
    /// <para>
    /// Both taps are clicks on the Spin button element, sent in one browser task, so no settlement can be handled
    /// between them. A second Playwright click would be sent later and at screen coordinates, and after the
    /// settlement it could land on whatever the page drew there instead.
    /// </para>
    /// </summary>
    [Test]
    public async Task ADoubleTapSpinsExactlyOnce()
    {
        await OpenWheelAsync();

        ILocator spin = Page.GetByTestId("spin");
        await Expect(spin).ToBeEnabledAsync();

        await spin.EvaluateAsync("el => { el.click(); el.click(); }");

        await Expect(Page.GetByTestId("reward-continue")).ToBeVisibleAsync(new() { Timeout = BootTimeoutMs });

        int coins  = await ReadBalanceAsync("coins");
        int gems   = await ReadBalanceAsync("gems");
        int tokens = await ReadBalanceAsync("spintokens");

        await Page.GetByTestId("reward-continue").ClickAsync();

        // Reload, so the page shows committed state instead of the client's prediction. There is no wait before
        // the reload: the Spin button unlocks only on a settlement or a refusal, and the player actor answers every
        // request, including a throttled one (Server.Tests/WheelSpinThrottleTests).
        await ReloadOnStackAsync(Page);
        await WaitForLiveSessionAsync();
        await Expect(Page.GetByTestId("spin-available")).ToBeVisibleAsync();

        Assert.That(await ReadBalanceAsync("coins"), Is.EqualTo(coins), "a second tap paid a second prize");
        Assert.That(await ReadBalanceAsync("gems"), Is.EqualTo(gems));
        Assert.That(await ReadBalanceAsync("spintokens"), Is.EqualTo(tokens), "a second token was spent");

        AssertTheTimelinesNeverDiverged();
    }

    /// <summary>
    /// While a spin plays, the Spin button stays in place and disabled, even when the spin used the last token. If the
    /// earn links replaced it, the second tap of a double tap would open one and leave the wheel mid-spin.
    /// <para>
    /// A long <c>hitMs</c> holds the hit animation open after the settlement, which is when the token is spent. A
    /// spin-again result pays the token back, so the test spins again until a result leaves the wallet empty. The
    /// iteration limit is the same bound as in <see cref="WithNoTokensTheScreenPointsAtRealEarningRoutes"/>.
    /// </para>
    /// </summary>
    [Test]
    public async Task TheSpinButtonStaysInPlaceWhileTheLastTokensSpinPlays()
    {
        await OpenWheelAsync("spinMs=1&hitMs=4000");

        const int maxReplacementSpinsInARow = 20;
        for (int spinsInARow = 0; ; spinsInARow++)
        {
            Assert.That(spinsInARow, Is.LessThan(maxReplacementSpinsInARow),
                $"the wheel kept paying replacement tokens {maxReplacementSpinsInARow} spins in a row");

            await Page.GetByTestId("spin").ClickAsync();
            await Expect(Page.GetByTestId("prize-wheel")).ToHaveAttributeAsync("data-hit", new Regex("^(reward|respin|blank)$"),
                new() { Timeout = BootTimeoutMs });

            // The headline reads the model, which the settlement has already changed. The HUD balance may still be
            // animating towards it.
            if (await Page.GetByTestId("spin-available").TextContentAsync() == "No spin tokens")
                break;

            await Expect(Page.GetByTestId("reward-continue")).ToBeVisibleAsync(new() { Timeout = BootTimeoutMs });
            await Page.GetByTestId("reward-continue").ClickAsync();
            await Expect(Page.GetByTestId("reward-reveal")).ToHaveCountAsync(0);
        }

        // The hit is still playing, so the reveal has not opened over the button.
        await Expect(Page.GetByTestId("spin")).ToBeDisabledAsync();
        await Expect(Page.GetByTestId("spin-earn")).ToHaveCountAsync(0);
        AssertTheTimelinesNeverDiverged();
    }

    /// <summary>
    /// With no tokens, the screen hides the Spin button, links to the features that grant tokens, and opens no
    /// purchase.
    /// </summary>
    [Test]
    public async Task WithNoTokensTheScreenPointsAtRealEarningRoutes()
    {
        await OpenWheelAsync();

        // Spend the starting token, and keep spinning until the balance is zero. The spin-again sectors pay back the
        // token they cost, so one spin does not guarantee an empty wallet. Landing on spin-again
        // maxReplacementSpinsInARow times in a row is so unlikely at the published odds that it means the table or
        // the token spend is broken.
        const int maxReplacementSpinsInARow = 20;
        int spinsInARow = 0;
        do
        {
            Assert.That(spinsInARow, Is.LessThan(maxReplacementSpinsInARow),
                $"the wheel kept paying replacement tokens {maxReplacementSpinsInARow} spins in a row, far " +
                "beyond what the configured 20% pair can plausibly produce -- the table or the spend is wrong");
            await SpinAndAcknowledgeAsync();
            spinsInARow++;
        } while (await ReadBalanceAsync("spintokens") > 0);

        await Expect(Page.GetByTestId("spin")).ToHaveCountAsync(0, new() { Timeout = BootTimeoutMs });
        await Expect(Page.GetByTestId("spin-available")).ToHaveTextAsync("No spin tokens");
        await Expect(Page.GetByTestId("spin-none")).ToContainTextAsync("Earn spin tokens");

        ILocator routes = Page.GetByTestId("spin-earn").Locator("a");
        await Expect(routes).ToHaveCountAsync(3);
        await Expect(routes).ToHaveTextAsync(new[] { "Daily Reward", "Missions", "Shop" });

        // Neither the reward reveal nor the odds sheet is open.
        await Expect(Page.GetByTestId("reward-reveal")).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("odds-sheet")).ToHaveCountAsync(0);
    }

    /// <summary>
    /// The Events hub card shows the token count and the top coin and gem prizes. It shows no timer and no
    /// "daily" text, because spin tokens do not expire.
    /// </summary>
    [Test]
    public async Task TheHubCardShowsTheTokenStateAndTheHighTierTeaser()
    {
        await Page.GotoAsync(ClientUrl("/events"));
        await WaitForLiveSessionAsync();

        ILocator card = Page.GetByTestId("feature-spinwheel");
        await Expect(card).ToBeVisibleAsync(new() { Timeout = BootTimeoutMs });

        await Expect(card).ToContainTextAsync("1 spin available");
        await Expect(card).ToContainTextAsync("Up to 1,000 coins or 30 gems");
        await Expect(card).Not.ToContainTextAsync("daily");
    }

    /// <summary>
    /// The <c>meta=unpublished</c> scenario keeps showing the closed wheel after the live session connects, and
    /// the authored notice says the scenario's data is drawn over a live session. <c>SpinWheelRenderTests</c> checks
    /// what the closed state renders.
    /// <para>
    /// The shipped config archive always has a complete wheel table, so only a fixture can show the closed state.
    /// </para>
    /// </summary>
    [Test]
    public async Task AWheelWithNoPublishedTableSurvivesASession()
    {
        await Page.GotoAsync(ClientUrl("/events/spin", "meta=unpublished"));
        await WaitForLiveSessionAsync();

        await Expect(Page.GetByTestId("state-unavailable")).ToContainTextAsync("The wheel is closed",
            new() { Timeout = BootTimeoutMs });
        await Expect(Page.GetByTestId("authored-notice")).ToBeVisibleAsync();
    }
}
