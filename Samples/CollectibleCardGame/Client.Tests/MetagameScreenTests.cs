using Microsoft.Playwright;

namespace Game.Client.Tests;

/// <summary> Metagame layouts, filtering and keyboard access through the real offline client. </summary>
[TestFixture]
public sealed class MetagameScreenTests : OfflineTestBase
{
    [Test]
    public async Task DeckPowerUsesOwnedRanksAndChangesWithTheSelectedPack()
    {
        await Page.GotoAsync($"{Offline("/collection")}&dev=ranks");
        await Expect(Page.GetByTestId("collection-grid")).ToBeVisibleAsync(new() { Timeout = BootTimeout });
        await SetCardRankAsync("EmberKit", 3);
        await Page.GetByTestId("back-home").ClickAsync();
        await Page.GetByTestId("practice-deck").SelectOptionAsync("starter:FireAndFoam");
        await Expect(Page.GetByTestId("selected-deck-power")).ToHaveTextAsync("27");
        await Page.GetByTestId("practice-deck").SelectOptionAsync("starter:SunlitThicket");
        await Expect(Page.GetByTestId("selected-deck-power")).ToHaveTextAsync("25");
        await Page.GetByTestId("practice-deck").SelectOptionAsync("starter:FireAndFoam");
        await Expect(Page.GetByTestId("selected-deck-power")).ToHaveTextAsync("27");
    }

    [Test]
    public async Task DeckCrestsFollowTheSelectedPack()
    {
        await GotoOfflineAsync("/", "display-name");
        ILocator crests = Page.GetByTestId("selected-deck-clans").Locator("img");
        await Page.GetByTestId("practice-deck").SelectOptionAsync("starter:FireAndFoam");
        await Expect(crests).ToHaveCountAsync(2);
        await Expect(crests.Nth(0)).ToHaveAttributeAsync("src", "art/clan-symbol-split-flame.webp");
        await Expect(crests.Nth(1)).ToHaveAttributeAsync("src", "art/clan-symbol-tidal-eye.webp");
        await Page.GetByTestId("practice-deck").SelectOptionAsync("starter:SunlitThicket");
        await Expect(crests.Nth(0)).ToHaveAttributeAsync("src", "art/clan-symbol-rising-rings.webp");
        await Expect(crests.Nth(1)).ToHaveAttributeAsync("src", "art/clan-symbol-hearth-knot.webp");
    }

    [TestCase(1440, 900)]
    [TestCase(390, 844)]
    public async Task ScreensFitTheViewportAndLoadTheirArtwork(int width, int height)
    {
        await Page.SetViewportSizeAsync(width, height);
        foreach ((string route, string ready) in new[]
        {
            ("/", "display-name"), ("/collection", "collection-grid"),
            ("/decks", "decks-empty"), ("/decks/new", "deck-card-count"),
        })
        {
            await GotoOfflineAsync(route, ready);
            ILocator shell = Page.GetByTestId("metagame-shell");
            Assert.That(await shell.EvaluateAsync<bool>("e => e.scrollWidth <= e.clientWidth + 1"), Is.True, route);
            await shell.EvaluateAsync("e => Promise.all([...e.querySelectorAll('img')].map(image => image.decode()))");
            if (route == "/decks/new")
            {
                await Page.GetByTestId("pick-card").Last.ScrollIntoViewIfNeededAsync();
                await Expect(Page.GetByTestId("save-deck")).ToBeInViewportAsync();
            }
        }
    }

    [Test]
    public async Task CollectionSearchCombinesWithFiltersAndClearResetsTheControls()
    {
        await GotoOfflineAsync("/collection", "collection-grid");
        int total = await Page.GetByTestId("card-tile").CountAsync();
        await Page.GetByTestId("card-search").FillAsync("ember kit");
        await Expect(Page.GetByTestId("card-tile")).ToHaveCountAsync(1);
        await Expect(Card("EmberKit")).ToBeVisibleAsync();
        await Page.GetByTestId("filter-type").SelectOptionAsync("Trick");
        await Expect(Page.GetByTestId("collection-empty")).ToBeVisibleAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Clear filters" }).ClickAsync();
        await Expect(Page.GetByTestId("filter-type")).ToHaveValueAsync("");
        await Expect(Page.GetByTestId("card-search")).ToHaveValueAsync("");
        await Expect(Page.GetByTestId("card-tile")).ToHaveCountAsync(total);
        await Page.GetByTestId("filter-owned").SelectOptionAsync("unowned");
        await Expect(Page.GetByTestId("card-tile")).ToHaveCountAsync(20);
    }

    [Test]
    public async Task CardDetailTrapsFocusAndEscapeReturnsToItsCard()
    {
        await GotoOfflineAsync("/collection", "collection-grid");
        await Card("EmberKit").ClickAsync();
        ILocator dialog = Page.GetByRole(AriaRole.Dialog);
        await Expect(dialog).ToBeVisibleAsync();
        await Page.Keyboard.PressAsync("Tab");
        Assert.That(await dialog.EvaluateAsync<bool>("e => e.contains(document.activeElement)"), Is.True);
        await Page.Keyboard.PressAsync("Escape");
        await Expect(dialog).ToHaveCountAsync(0);
        await Expect(Card("EmberKit")).ToBeFocusedAsync();
        await Card("EmberKit").ClickAsync();
        await dialog.GetByRole(AriaRole.Button, new() { Name = "✕" }).ClickAsync();
        await Expect(dialog).ToHaveCountAsync(0);
        await Expect(Card("EmberKit")).ToBeFocusedAsync();
    }
}
