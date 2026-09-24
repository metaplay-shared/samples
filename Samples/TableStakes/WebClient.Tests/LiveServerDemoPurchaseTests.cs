using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

namespace WebClient.Tests;

/// <summary>
/// The demo purchase end to end against a live game server (<c>docs/offers.md</c>): a fresh guest, who is in the
/// first-week cohort, buys the Starter Pack from the Shop's Featured slot through the SDK's dynamic-content in-app
/// purchase flow, the server validates the receipt, and the contents reach the wallet. Offline mode accepts every
/// purchase unvalidated, so only a live server shows that the client's receipt passes the SDK's Development
/// platform and that the grant runs on the server-authoritative timeline.
/// <para>
/// Nothing here taps PLAY, so these tests need no <see cref="LiveServerLocks"/> lock.
/// </para>
/// </summary>
[TestFixture]
public class LiveServerDemoPurchaseTests : PlaywrightPageTest
{
    /// <summary>
    /// How long the dynamic-content flow may take. It needs two server round trips: the confirmation of the
    /// prepare action, then the receipt's validation.
    /// </summary>
    private const int PurchaseTimeoutMs = 30000;

    /// <summary>Open the Shop and wait until the session has loaded, so the featured offer is the real one.</summary>
    private async Task OpenShopAsync(string? query = null)
    {
        await Page.GotoAsync(query == null ? ShopUrl : ClientUrl("/shop", query));
        await Expect(Page.GetByTestId("offer-starter-pack-1")).ToBeVisibleAsync(new() { Timeout = BootTimeoutMs });

        // Until the session connects, the shop shows fixture data, including the same offer. Wait for the
        // starting wallet instead, which comes only from the server.
        await Expect(Page.GetByTestId("balance-coins").Locator(".m-balance__amount"))
            .ToHaveTextAsync("3,000", new() { Timeout = BootTimeoutMs });
    }

    /// <summary>Tap the featured offer's buy button and confirm the demo purchase.</summary>
    private async Task BuyFeaturedAsync()
    {
        await Page.GetByTestId("offer-starter-pack-1").GetByTestId("offer-buy").ClickAsync();

        await Expect(Page.GetByTestId("confirm")).ToBeVisibleAsync();
        await Expect(Page.GetByTestId("confirm")).ToContainTextAsync("no real charge");

        await Page.GetByTestId("confirm-accept").ClickAsync();
    }

    /// <summary>
    /// The full purchase flow: prepare, confirmation, purchase, server validation, grant, and updated balances.
    /// <para>
    /// The expected balances are the starting wallet plus the Starter Pack's contents. The purchase is granted
    /// by a server action, so the HUD shows the balance after the server applied the grant. Each demo offer can be
    /// bought once per player, so afterwards the card shows Purchased and has no Buy button (<c>docs/offers.md</c>).
    /// The grant persists across a reload, which uses the same guest account and reads the balances from the
    /// server.
    /// </para>
    /// </summary>
    [Test]
    public async Task ADemoPurchaseIsValidatedByTheServerAndGranted()
    {
        await OpenShopAsync();
        await BuyFeaturedAsync();

        // The reveal dialog opens at once, but shows the purchase's title only after the server has validated
        // the receipt.
        await Expect(Page.GetByTestId("reward-reveal")).ToBeVisibleAsync();

        await Expect(Page.GetByTestId("reward-title")).ToHaveTextAsync("Demo purchase complete", new() { Timeout = PurchaseTimeoutMs });

        await Page.GetByTestId("reward-continue").ClickAsync();

        await AssertWalletAsync("5,000", "400", "2");

        // Not.ToBeVisible also passes when the card never rendered, so first assert the Purchased badge in the
        // same card.
        ILocator featured = Page.GetByTestId("offer-starter-pack-1");
        await Expect(featured.GetByTestId("offer-purchased")).ToBeVisibleAsync();
        await Expect(featured.GetByTestId("offer-buy")).Not.ToBeVisibleAsync();

        await ReloadOnStackAsync(Page);

        await AssertWalletAsync("5,000", "400", "2", BootTimeoutMs);
    }

    /// <summary>
    /// Negative control: the server refuses a receipt with a wrong signature, grants nothing, and the balances
    /// do not change. The failure panel can be dismissed without retrying.
    /// <para>
    /// The other tests would also pass against a server that granted every purchase without validating it. The
    /// query-string knob <c>demoReceipt=tampered</c> makes the client corrupt the signature and send everything
    /// else unchanged.
    /// </para>
    /// <para>
    /// The server refuses the same receipt every time, so a panel with only a retry button could be left only
    /// by reloading the app. Every reward screen uses the same reveal panel. The tampered receipt is the only
    /// failure this suite can trigger on demand, so the dismiss button is tested here.
    /// </para>
    /// </summary>
    [Test]
    public async Task AReceiptTheServerCannotVerifyGrantsNothing()
    {
        await OpenShopAsync("demoReceipt=tampered");
        await BuyFeaturedAsync();

        await Expect(Page.GetByTestId("reward-retry")).ToBeVisibleAsync(new() { Timeout = PurchaseTimeoutMs });
        await Expect(Page.GetByTestId("reward-reveal")).ToContainTextAsync("Nothing was awarded");

        await AssertWalletAsync("3,000", "100", "1");

        await Expect(Page.GetByTestId("reward-dismiss")).ToBeVisibleAsync();
        await Page.GetByTestId("reward-dismiss").ClickAsync();

        // Not.ToBeVisible also passes when nothing rendered, so also assert that the featured offer's Buy button
        // is visible. A refused purchase leaves the offer buyable.
        await Expect(Page.GetByTestId("reward-reveal")).Not.ToBeVisibleAsync();
        await Expect(Page.GetByTestId("offer-starter-pack-1").GetByTestId("offer-buy")).ToBeVisibleAsync();
        await Expect(Page.GetByTestId("primary-nav")).ToBeVisibleAsync();
        await AssertWalletAsync("3,000", "100", "1");
    }

    /// <summary>
    /// A purchase that the client abandoned after validation is completed by the next session. If the tab closes
    /// between validation and the claim, the contents are not granted and the purchase counts against the SDK's
    /// limit on pending purchases. The SDK does not re-deliver pending purchases, so the client looks for them when a
    /// session starts. <c>?demoClaim=skip</c> suppresses the claim. After the navigation the purchase completes
    /// through either the session-start check or the normal validation callback, depending on whether validation had
    /// finished, and the test accepts both.
    /// </summary>
    [Test]
    public async Task APurchaseAbandonedAfterValidationIsFinishedByTheNextSession()
    {
        await OpenShopAsync("demoClaim=skip");
        await BuyFeaturedAsync();

        // The reveal stays on "Confirming with the server", because this client does not claim.
        await Expect(Page.GetByTestId("reward-reveal")).ToContainTextAsync("Confirming with the server");

        // The "Confirming" text covers both steps: assigning the offer as pending content and sending the
        // receipt. The hidden demo-purchase-sent marker is True only once PendingInAppPurchases holds the
        // transaction, which means the receipt was sent. The DemoPurchase state's Validating value is set one
        // JS interop call earlier, so waiting on it could navigate away before the receipt was sent.
        await Expect(Page.GetByTestId("demo-purchase-sent")).ToHaveTextAsync("True", new() { Timeout = PurchaseTimeoutMs });
        await AssertWalletAsync("3,000", "100", "1");

        // Same guest account, without the demoClaim knob.
        await Page.GotoAsync(ShopUrl);

        await AssertWalletAsync("5,000", "400", "2", BootTimeoutMs);
        await Expect(Page.GetByTestId("offer-starter-pack-1").GetByTestId("offer-purchased")).ToBeVisibleAsync();
    }
}
