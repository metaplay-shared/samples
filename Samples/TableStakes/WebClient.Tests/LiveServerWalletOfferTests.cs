using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

namespace WebClient.Tests;

/// <summary>
/// E2E tests for the Featured offer slot and the shop catalogue against a <b>live game server</b>
/// (<c>docs/offers.md</c>, "Resolving the featured offer"). Segment membership, the personalisation preference and
/// the SDK's offer-group activation state live on the server's player model, and offline mode accepts every action
/// without checking it. No test taps PLAY, so these tests need no <see cref="LiveServerLocks"/> lock.
/// </summary>
[TestFixture]
public class LiveServerWalletOfferTests : PlaywrightPageTest
{
    private async Task OpenShopAsync()
    {
        await Page.GotoAsync(ShopUrl);
        await WaitForLiveSessionAsync();
    }

    /// <summary>
    /// Buying a wallet-priced offer spends its price and grants its contents in one action, with no server round
    /// trip to wait for. The test uses Daily Spin Deal because it has no offer-level segment, so every guest can
    /// buy it from the catalogue.
    /// </summary>
    [Test]
    public async Task BuyingADailySpinDealSettlesTheWalletAtomically()
    {
        await OpenShopAsync();
        await AssertWalletAsync("3,000", "100", "1");

        ILocator offer = Page.GetByTestId("offer-daily-spin-deal");

        // The price is shown on the card before the tap, because the Buy button does not show it.
        await Expect(offer.GetByTestId("offer-price")).ToContainTextAsync("750");

        await offer.GetByTestId("offer-buy").ClickAsync();
        await Expect(Page.GetByTestId("confirm")).ToBeVisibleAsync();
        await Page.GetByTestId("confirm-accept").ClickAsync();

        await Expect(Page.GetByTestId("reward-title")).ToHaveTextAsync("Purchased");
        await Page.GetByTestId("reward-continue").ClickAsync();

        await AssertWalletAsync("2,250", "100", "2");

        // The card stays in the catalogue and shows sold out until the next daily activation (docs/offers.md).
        await Expect(Page.GetByTestId("offer-daily-spin-deal").GetByTestId("offer-soldout")).ToBeVisibleAsync();
    }

    /// <summary>
    /// A new guest is in their first week and has bought nothing, so Starter Pack takes the Featured slot. Turning
    /// personalisation off replaces the segment-gated Starter Pack with the untargeted fallback offer. Turning it
    /// back on restores Starter Pack (docs/offers.md, "Resolving the featured offer").
    /// </summary>
    [Test]
    public async Task TurningPersonalizationOffSwitchesFeaturedToTheFallbackAndBackAgain()
    {
        await OpenShopAsync();

        // Locate the Featured card by its class, not by offer id. Once shown, Starter Pack stays in the catalogue
        // for the rest of the day's activation (docs/offers.md), so its id is still on the page after it leaves
        // the Featured slot.
        ILocator featuredCard = Page.Locator(".m-card--featured");

        // ToBeCheckedAsync reads the switch's aria-checked attribute.
        ILocator toggle = Page.GetByTestId("personalization-toggle");
        await Expect(toggle).ToBeCheckedAsync();
        await Expect(featuredCard).ToHaveAttributeAsync("data-testid", "offer-starter-pack-1");

        await Page.GetByTestId("personalization-toggle").ClickAsync();

        await Expect(toggle).Not.ToBeCheckedAsync();
        await Expect(featuredCard).ToHaveAttributeAsync("data-testid", "offer-daily-spin-deal");

        // Clicking the label text also toggles the switch. The switch remains the only focusable control.
        await Page.GetByText("Personalized featured offers").ClickAsync();

        await Expect(toggle).ToBeCheckedAsync();
        await Expect(featuredCard).ToHaveAttributeAsync("data-testid", "offer-starter-pack-1");
    }

    /// <summary>
    /// A wallet offer the player cannot afford shows the price and the shortfall and has no Buy button. A tap
    /// would fail against the server's wallet and a retry could not succeed (<c>docs/economy.md</c>, "Config
    /// build checks"). A new guest cannot afford Lucky Spin Bundle.
    /// </summary>
    [Test]
    public async Task AnOfferPricedBeyondTheWalletExplainsTheShortfallInsteadOfOfferingATap()
    {
        await OpenShopAsync();
        await AssertWalletAsync("3,000", "100", "1");

        // The expected shortfall is the offer's price minus the starting gem balance asserted above.
        ILocator offer = Page.GetByTestId("offer-lucky-spin-bundle");
        await Expect(offer).ToBeVisibleAsync();

        // The price stays on the card, and the Buy button's place shows the shortfall instead.
        await Expect(offer.GetByTestId("offer-price")).ToContainTextAsync("500");
        await Expect(offer.GetByTestId("offer-unaffordable")).ToContainTextAsync("Not enough Gems");
        await Expect(offer.GetByTestId("offer-shortfall")).ToContainTextAsync("You need 400 more Gems");
        await Expect(offer.GetByTestId("offer-buy")).Not.ToBeVisibleAsync();

        // The card links to a page where the player can earn the currency (docs/economy.md). The config build
        // validates that the link target exists.
        ILocator earn = offer.GetByTestId("offer-earn");
        await Expect(earn).ToContainTextAsync(WebClient.Meta.Ui.ButtonLabels.Earn);
        await Expect(earn).ToHaveAttributeAsync("href", "/events");
    }

    /// <summary>
    /// The confirmation dialog for a wallet-priced offer shows the price and the current balance of that currency,
    /// both in full digits.
    /// </summary>
    [Test]
    public async Task AWalletConfirmationShowsTheBalanceThePriceComesOutOf()
    {
        await OpenShopAsync();
        await AssertWalletAsync("3,000", "100", "1");

        await Page.GetByTestId("offer-daily-spin-deal").GetByTestId("offer-buy").ClickAsync();

        ILocator confirm = Page.GetByTestId("confirm");
        await Expect(confirm).ToContainTextAsync("750 Coins");
        await Expect(confirm).ToContainTextAsync("You have 3,000 Coins");

        await Page.GetByTestId("confirm-cancel").ClickAsync();
        await AssertWalletAsync("3,000", "100", "1");
    }

    /// <summary>
    /// Starter Pack II has Starter Pack as its precursor, so the catalogue shows it locked with the reason, instead of
    /// hiding it (<c>docs/offers.md</c>, "Precursors").
    /// </summary>
    [Test]
    public async Task StarterPackTwoShowsAsLockedInTheCatalogue()
    {
        await OpenShopAsync();

        // Not.ToBeVisible also passes when nothing rendered, so first assert that the card and its lock are visible.
        ILocator offer = Page.GetByTestId("offer-starter-pack-2");
        await Expect(offer).ToBeVisibleAsync();
        await Expect(offer.GetByTestId("offer-locked")).ToBeVisibleAsync();
        await Expect(offer.GetByTestId("offer-buy")).Not.ToBeVisibleAsync();

        // The lock reason is the offer's description. It is shown in the lock chip only, not also under the title.
        const string Reason = "Unlocked once your Starter Pack has run its course.";
        string text = await offer.InnerTextAsync();
        await Expect(offer.GetByTestId("offer-locked")).ToContainTextAsync(Reason);
        Assert.That(text.Split(Reason).Length - 1, Is.EqualTo(1), $"the lock reason is printed once, in:\n{text}");
    }

    /// <summary>
    /// "Just for you" is shown only on offers whose segment the player is in. A new guest is outside Gem Booster
    /// Pack's segment, so it shows locked without the label. The Featured Starter Pack is segment-gated, so it
    /// shows the label.
    /// </summary>
    [Test]
    public async Task AnOfferTheGuestIsNotEligibleForIsNotStampedJustForThem()
    {
        await OpenShopAsync();

        ILocator ineligible = Page.GetByTestId("offer-gem-booster-pack");
        await Expect(ineligible).ToBeVisibleAsync();
        await Expect(ineligible.GetByTestId("offer-locked")).ToBeVisibleAsync();
        await Expect(ineligible).Not.ToContainTextAsync("Just for you");

        await Expect(Page.GetByTestId("offer-starter-pack-1")).ToContainTextAsync("Just for you");
    }
}
