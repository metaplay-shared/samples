using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

namespace WebClient.Tests;

/// <summary>
/// Buying a cosmetic end to end against a live game server (<c>docs/cosmetics.md</c>): the price leaves the
/// wallet, the item is equipped without a second tap, Home and the Profile preview draw it, and it survives a
/// reload. Prices, refusals and slot rules are tested in <c>SharedCode.Tests</c>, and the grid's per-slot tabs in
/// <c>CosmeticsRenderTests</c>. The slots in the published catalogue are checked here, because only a live server
/// serves that archive. Nothing here taps PLAY, so these tests need no <see cref="LiveServerLocks"/> lock.
/// </summary>
[TestFixture]
public class LiveServerCosmeticsTests : PlaywrightPageTest
{
    private static readonly string CosmeticsUrl = ClientUrl("/profile/cosmetics");

    /// <summary>
    /// The cheapest coin frame in the shipped catalogue, and a fresh guest's coin balance after buying it. The
    /// balance is asserted as exact text, because "2,500" is a substring of "12,500".
    /// </summary>
    private const string SilverFrame      = "frame.silver";
    private const string SilverFrameClass = "m-frame--frame-silver";
    private const string CoinsAfterSilver = "2,500";

    private async Task OpenCosmeticsAsync()
    {
        await Page.GotoAsync(CosmeticsUrl);

        // Required. The grid renders fixture data before the session connects, and a tap on that data sends
        // nothing, so the test would time out.
        await WaitForLiveSessionAsync();
    }

    /// <summary>
    /// One purchase and everything it changes. The balance is asserted exactly before and after, so a purchase
    /// that spends the wrong amount fails.
    /// </summary>
    [Test]
    public async Task BuyingAFrameSpendsItsPriceWearsItAndSurvivesAReload()
    {
        // Start on Home to read the player name for the Admin API lookup and to check the baseline: a fresh
        // guest does not wear the silver frame, so the class assertion after the purchase is meaningful.
        await Page.GotoAsync(ClientUrl());
        string playerName = await ReadPlayerNameAsync();
        await Expect(Page.GetByTestId("home-profile").GetByTestId("avatar")).Not.ToHaveClassAsync(new Regex(SilverFrameClass));

        await Page.GotoAsync(CosmeticsUrl);
        await WaitForLiveSessionAsync();

        await Expect(Page.GetByTestId("balance-coins").Locator(".m-balance__amount")).ToHaveTextAsync("3,000");

        await Page.GetByTestId($"cosmetic-{SilverFrame}").ClickAsync();
        await Page.GetByTestId($"cosmetic-{SilverFrame}").GetByTestId("cosmetic-buy").ClickAsync();

        // The confirm dialog shows the price and the remaining balance before anything is spent.
        await Expect(Page.GetByTestId("confirm")).ToBeVisibleAsync();
        await Page.GetByTestId("confirm-accept").ClickAsync();

        // Buying equips the item without a second tap. A fresh guest has exactly one equipped tile on this tab.
        await Expect(Page.GetByTestId("cosmetic-equipped")).ToBeVisibleAsync();
        await Expect(Page.GetByTestId("balance-coins").Locator(".m-balance__amount")).ToHaveTextAsync(CoinsAfterSilver);

        // Home's identity row uses the same projection as the standings. In-app navigation keeps the session,
        // so this reads the client's prediction of the purchase, before the server has committed it.
        await Page.GetByTestId("nav-home").ClickAsync();
        await Expect(Page.GetByTestId("home-profile").GetByTestId("avatar")).ToHaveClassAsync(new Regex(SilverFrameClass));

        // The server commits the action one flush after the client predicts it. A reload before the commit
        // discards the action, so wait for the server's event before reloading below.
        await WaitForServerToHaveEventAsync(playerName, "PlayerEventCosmeticPurchased");

        // The Profile preview ("how others see you") must draw the same projection as Home.
        await Page.GetByTestId("home-profile-action").ClickAsync();
        await Expect(Page.GetByTestId("page-title")).ToHaveTextAsync("Profile");
        await Expect(Page.GetByTestId("identity-preview").GetByTestId("avatar")).ToHaveClassAsync(new Regex(SilverFrameClass));

        // A reload rebuilds the page from server state. Reload on Home, which shows both the balance and the
        // frame.
        await Page.GetByTestId("nav-home").ClickAsync();
        await ReloadOnStackAsync(Page);
        await WaitForLiveSessionAsync();
        await Expect(Page.GetByTestId("balance-coins").Locator(".m-balance__amount")).ToHaveTextAsync(CoinsAfterSilver);
        await Expect(Page.GetByTestId("home-profile").GetByTestId("avatar")).ToHaveClassAsync(new Regex(SilverFrameClass));
    }

    /// <summary>
    /// A gem-priced item is unaffordable for a fresh guest, so its tile shows the price and has no Buy button.
    /// </summary>
    [Test]
    public async Task AGemPricedItemIsUnaffordableOnDayOneAndOffersNoPurchase()
    {
        await OpenCosmeticsAsync();

        // The slots in the published catalogue. CosmeticsRenderTests covers drawing one tab per slot. This
        // checks which slots the server's archive contains.
        await Expect(Page.GetByTestId("slot-avatar")).ToBeVisibleAsync();
        await Expect(Page.GetByTestId("slot-frame")).ToBeVisibleAsync();
        await Expect(Page.GetByTestId("slot-nameeffect")).ToBeVisibleAsync();

        await Page.GetByTestId("cosmetic-frame.emerald").ClickAsync();

        // Read on the tile itself, because affordable tiles on the same page have their own Buy buttons.
        await Expect(Page.GetByTestId("cosmetic-frame.emerald").GetByTestId("cosmetic-unaffordable")).ToBeVisibleAsync();
        await Expect(Page.GetByTestId("cosmetic-frame.emerald").GetByTestId("cosmetic-buy")).ToHaveCountAsync(0);

        // The card shows the shortfall and links to where gems are earned (docs/economy.md). The expected
        // shortfall is the emerald frame's gem price minus a starting wallet's gems.
        await Expect(Page.GetByTestId("cosmetic-shortfall")).ToContainTextAsync("You need 50 more Gems");

        ILocator earn = Page.GetByTestId("cosmetic-earn").GetByRole(AriaRole.Link);
        await Expect(earn).ToContainTextAsync(WebClient.Meta.Ui.ButtonLabels.Earn);
        await Expect(earn).ToHaveAttributeAsync("href", "/events");
    }

    /// <summary>
    /// Buying the Queen of Hearts avatar equips it, Home's identity row draws its icon, and the icon is still
    /// shown after a reload. The icon is identified by the svg's <c>data-token</c> attribute. The svg is
    /// selected through <c>.m-avatar__face</c>, because the avatar also contains the frame's svg.
    /// </summary>
    [Test]
    public async Task BuyingAnAvatarDrawsItsIconOnHomeAndSurvivesAReload()
    {
        await Page.GotoAsync(ClientUrl());
        string playerName = await ReadPlayerNameAsync();

        // Baseline: a fresh guest wears the spade avatar they start with (docs/cosmetics.md).
        await Expect(Page.GetByTestId("home-profile").GetByTestId("avatar").Locator(".m-avatar__face svg")).ToHaveAttributeAsync("data-token", "avatar-spade");

        await Page.GotoAsync(CosmeticsUrl);
        await WaitForLiveSessionAsync();
        await Page.GetByTestId("slot-avatar").ClickAsync();

        await Page.GetByTestId("cosmetic-avatar.queen").ClickAsync();
        await Page.GetByTestId("cosmetic-avatar.queen").GetByTestId("cosmetic-buy").ClickAsync();
        await Expect(Page.GetByTestId("confirm")).ToBeVisibleAsync();
        await Page.GetByTestId("confirm-accept").ClickAsync();

        await Expect(Page.GetByTestId("cosmetic-equipped")).ToBeVisibleAsync();

        // The starting coins minus the queen's price, asserted exactly.
        await Expect(Page.GetByTestId("balance-coins").Locator(".m-balance__amount")).ToHaveTextAsync("2,400");

        // Home draws the avatar from the client's prediction of the purchase.
        await Page.GetByTestId("nav-home").ClickAsync();
        await Expect(Page.GetByTestId("home-profile").GetByTestId("avatar").Locator(".m-avatar__face svg")).ToHaveAttributeAsync("data-token", "avatar-heart");

        // Wait for the server to commit the purchase before reloading, or the reload discards it.
        await WaitForServerToHaveEventAsync(playerName, "PlayerEventCosmeticPurchased");

        await ReloadOnStackAsync(Page);
        await WaitForLiveSessionAsync();
        await Expect(Page.GetByTestId("home-profile").GetByTestId("avatar").Locator(".m-avatar__face svg")).ToHaveAttributeAsync("data-token", "avatar-heart");
    }
}
