using Microsoft.Playwright;
using System.Text.RegularExpressions;

namespace Game.Client.Tests;

/// <summary>
/// The collection browser under <c>?env=offline</c>: the starter grant a fresh player receives, the filters,
/// and the card-detail overlay. Needs the Client dev server only — no game server, which is the whole point of
/// the mode and of doing the collection's proof here.
/// </summary>
[Parallelizable(ParallelScope.Self)]
[TestFixture]
public class CollectionTests : OfflineTestBase
{
    // Two cards from different clans, named so a clan filter can be checked from the outside. Their ids are
    // pinned by GameConfigContentTests, so a content edit that removed them would fail there first.
    const string KitsuneCard  = "EmberKit";
    const string TidepoolCard = "TideScholar";

    // A trick, for the keyword rules that turn on the card type. Its id is pinned by GameConfigContentTests
    // too, and a trick binding anything but Hello fails the config build.
    const string TrickCard = "Foxfire";

    [Test]
    public async Task OfflineMode_Collection_GrantsStarterCollection_OnFirstLogin()
    {
        await GotoOfflineAsync("/collection", "collection-grid");

        ILocator tiles = Page.GetByTestId("card-tile");
        int shown = await tiles.CountAsync();
        Assert.That(shown, Is.GreaterThan(0), "the catalogue should draw the whole collectible pool");

        // Every card the built-in config marks as a starter card is owned, at the rank floor.
        int unowned = await Page.Locator("[data-testid='card-tile'][data-owned='false']").CountAsync();
        int atRankOne = await Page.Locator("[data-testid='card-tile'][data-rank='1']").CountAsync();

        Assert.That(atRankOne, Is.EqualTo(shown - unowned), "every owned card should sit at rank 1");

        Assert.That(shown, Is.EqualTo(65));
        Assert.That(unowned, Is.EqualTo(20));
        Assert.That(atRankOne, Is.EqualTo(45));
        await Expect(Page.GetByTestId("collection-owned-count")).ToContainTextAsync("45 / 65 owned");
        await Page.ReloadAsync();
        await Expect(Page.GetByTestId("collection-owned-count")).ToContainTextAsync("45 / 65 owned");

    }

    [TestCase("LongdogLookout")]
    [TestCase("MapShellTurtle")]
    [TestCase("PocketShovelMole")]
    [TestCase("DeepdelverMole")]
    [TestCase("HillRaiserMole")]
    public async Task ExpansionCardsHaveUnownedDetailsAndAreExcludedFromDecks(string id)
    {
        await GotoOfflineAsync("/collection", "collection-grid");
        await Expect(Card(id)).ToHaveAttributeAsync("data-owned", "false");
        await Expect(Card(id).Locator(".card-face-name")).ToHaveTextAsync(new Regex(".+"));
        Assert.That(await Card(id).Locator(".card-face-name").EvaluateAsync<bool>(
            "e => e.scrollWidth <= e.clientWidth + 1 && e.scrollHeight <= e.clientHeight + 1"), Is.True);
        await Card(id).ClickAsync();
        await Expect(Page.GetByTestId("card-detail-rank")).ToHaveAttributeAsync("data-rank", "0");
        await Expect(Page.GetByTestId("card-detail").GetByTestId("lock-affordance")).ToHaveCountAsync(0);
        await Page.GetByTestId("card-detail").EvaluateAsync("e => Promise.all([...e.querySelectorAll('img')].map(i => i.decode()))");
        await CloseOverlayAsync();
        await Page.GetByTestId("nav-decks").ClickAsync();
        await Page.GetByTestId("new-deck").ClickAsync();
        await DeckClan(id == "LongdogLookout" ? "Sunny" : id == "MapShellTurtle" ? "Tidepool" : "Mossback").ClickAsync();
        await Expect(PickCard(id)).ToHaveCountAsync(0);
    }

    [Test]
    public async Task OfflineMode_Collection_StarterGrant_IsIdempotent_AcrossReloads()
    {
        // With the development-only rank control on, because a lock needs a card above the rank floor
        // and growing one is part of the mutation this test rests on.
        await Page.GotoAsync($"{Offline("/collection")}&dev=ranks");
        await Expect(Page.GetByTestId("collection-grid")).ToBeVisibleAsync(new() { Timeout = BootTimeout });

        // Change something first. Without a mutation this test cannot tell a restored player from a freshly
        // granted one — both show the same counts — and it is the restored player the grant must not touch.
        await SetCardRankAsync(KitsuneCard, 2);
        await ToggleCardLockAsync(KitsuneCard);
        await Expect(LockSlot(0)).ToHaveAttributeAsync("data-card-id", KitsuneCard);

        string before = await Page.GetByTestId("collection-owned-count").InnerTextAsync();
        int tilesBefore = await Page.GetByTestId("card-tile").CountAsync();
        int rankOneBefore = await Page.Locator("[data-testid='card-tile'][data-rank='1']").CountAsync();

        await Page.ReloadAsync();
        await Expect(Page.GetByTestId("collection-grid")).ToBeVisibleAsync(new() { Timeout = BootTimeout });

        // The same player came back: the lock and the rank it needed are still there.
        await Expect(LockSlot(0)).ToHaveAttributeAsync("data-card-id", KitsuneCard);
        await Expect(Card(KitsuneCard)).ToHaveAttributeAsync("data-rank", "2");

        // And the grant did not run over it: nothing was added and no rank moved.
        Assert.That(await Page.GetByTestId("collection-owned-count").InnerTextAsync(), Is.EqualTo(before));
        Assert.That(await Page.GetByTestId("card-tile").CountAsync(), Is.EqualTo(tilesBefore));
        Assert.That(await Page.Locator("[data-testid='card-tile'][data-rank='1']").CountAsync(), Is.EqualTo(rankOneBefore));
    }

    [Test]
    public async Task OfflineMode_Reset_StartsOverAsAFreshPlayer()
    {
        await GotoOfflineAsync("/", "display-name");

        await OpenChangeNameAsync();
        await Page.GetByTestId("display-name-input").FillAsync("Whiskers");
        await Page.GetByTestId("save-name").ClickAsync();
        await Expect(Page.GetByTestId("display-name")).ToHaveTextAsync("Whiskers");

        // A reload keeps the account — that is the whole point of offline persistence.
        await Page.ReloadAsync();
        await Expect(Page.GetByTestId("display-name")).ToHaveTextAsync("Whiskers", new() { Timeout = BootTimeout });

        // Reset is the other half of that bargain, and the documented way back to a new player: it has to
        // throw away the persisted account too, not just the credentials.
        await Page.Locator(".session-menu summary").ClickAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Reset" }).ClickAsync();

        ILocator displayName = Page.GetByTestId("display-name");
        await Expect(displayName).ToBeVisibleAsync(new() { Timeout = BootTimeout });
        await Expect(displayName).Not.ToHaveTextAsync("Whiskers");
        await Expect(displayName).ToHaveTextAsync(new Regex(@"^Guest \d+$"));

        // The reset survived the reload rather than being written straight back on the way out.
        await Page.ReloadAsync();
        await Expect(Page.GetByTestId("display-name")).Not.ToHaveTextAsync("Whiskers", new() { Timeout = BootTimeout });
    }

    [Test]
    public async Task OfflineMode_Collection_FiltersByClan()
    {
        await GotoOfflineAsync("/collection", "collection-grid");

        await Expect(Card(KitsuneCard)).ToBeVisibleAsync();
        await Expect(Card(TidepoolCard)).ToBeVisibleAsync();

        await ClanFilter("Kitsune").ClickAsync();

        await Expect(Card(KitsuneCard)).ToBeVisibleAsync();
        await Expect(Card(TidepoolCard)).ToHaveCountAsync(0);

        // Back to the whole catalogue.
        await ClanFilter("").ClickAsync();
        await Expect(Card(TidepoolCard)).ToBeVisibleAsync();
    }

    [Test]
    public async Task OfflineMode_Collection_FiltersByType()
    {
        await GotoOfflineAsync("/collection", "collection-grid");

        int all = await Page.GetByTestId("card-tile").CountAsync();

        await Page.GetByTestId("filter-type").SelectOptionAsync("Trick");
        int tricks = await Page.GetByTestId("card-tile").CountAsync();

        Assert.That(tricks, Is.GreaterThan(0));
        Assert.That(tricks, Is.LessThan(all));

        // Ember Kit is a critter, so the trick filter must not offer it.
        await Expect(Card(KitsuneCard)).ToHaveCountAsync(0);
    }

    [Test]
    public async Task OfflineMode_CardDetail_ShowsRankTrackAndCurrentRank()
    {
        await GotoOfflineAsync("/collection", "collection-grid");

        await Card(KitsuneCard).ClickAsync();

        await Expect(Page.GetByTestId("card-detail")).ToBeVisibleAsync();
        await Expect(Page.GetByTestId("card-detail-name")).ToHaveTextAsync("Ember Kit");

        // A starter card is owned at the floor, so its numbers are the printed ones and nothing says
        // otherwise. The rank is asserted from its data attribute rather than the sentence around it, which
        // is copy; the numbers are asserted with their labels, since naming them is the line's whole job.
        await Expect(Page.GetByTestId("card-detail-rank")).ToHaveAttributeAsync("data-rank", "1");

        // Ember Kit is a 10/5 for 1: health is in the widened stat domain and the cost is not, which is
        // the distinction this line is the only browser-side pin of.
        ILocator stats = Page.GetByTestId("card-detail-stats");
        await Expect(stats).ToContainTextAsync("Attack 10");
        await Expect(stats).ToContainTextAsync("Health 5");
        await Expect(stats).ToContainTextAsync("Cost 1");
        await Expect(stats).Not.ToContainTextAsync("printed");

        // The printed track, whether or not this copy has reached its milestones: CritterDefault is +0/+1 at
        // rank 2 and +1/+0 at rank 4.
        ILocator track = Page.GetByTestId("card-detail-rank-track");
        await Expect(track).ToContainTextAsync("Rank 2: +1 health");
        await Expect(track).ToContainTextAsync("Rank 4: +1 attack (additional)");
    }

    [Test]
    public async Task OfflineMode_CardDetail_KeywordsCarryTheirGlossaryText()
    {
        await GotoOfflineAsync("/collection", "collection-grid");

        await Card(KitsuneCard).ClickAsync();
        await Expect(Page.GetByTestId("card-detail")).ToBeVisibleAsync();

        // Ember Kit is printed with Zoomies and binds nothing, so the line is that one keyword — carrying the
        // glossary text from the Keywords config row rather than leaving a bare word on screen.
        ILocator zoomies = KeywordChip("Zoomies");
        await Expect(zoomies).ToHaveTextAsync("Zoomies");
        await Expect(zoomies).ToHaveAttributeAsync("title", "Can attack the turn it's played.");
    }

    [Test]
    public async Task OfflineMode_CardDetail_ShowsHelloOnCrittersAndNotOnTricks()
    {
        await GotoOfflineAsync("/collection", "collection-grid");

        // On a critter, Hello marks the battlecry-like minority, and its help text is the config row's too.
        await Card(TidepoolCard).ClickAsync();
        await Expect(Page.GetByTestId("card-detail")).ToBeVisibleAsync();
        await Expect(KeywordChip("Hello")).ToHaveAttributeAsync("title", "Effect when played from hand.");

        await CloseOverlayAsync();
        await Expect(Page.GetByTestId("card-detail")).ToHaveCountAsync(0);

        // A trick's whole body is its Hello and it may bind nothing else, so the label is structurally
        // redundant there. Foxfire is a trick with a Hello and no keywords, so it has no keyword line at all.
        await Card(TrickCard).ClickAsync();
        await Expect(Page.GetByTestId("card-detail")).ToBeVisibleAsync();
        await Expect(KeywordChip("Hello")).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("card-detail-keywords")).ToHaveCountAsync(0);
    }

    [Test]
    public async Task OfflineMode_Home_NavigatesToCollectionAndDeckbuilder()
    {
        await GotoOfflineAsync("/", "display-name");

        await Page.GetByTestId("nav-collection").ClickAsync();
        await Expect(Page.GetByTestId("collection-grid")).ToBeVisibleAsync();

        // Back returns to Home with its state intact — the session is not restarted by in-app navigation.
        await Page.GoBackAsync();
        await Expect(Page.GetByTestId("display-name")).ToBeVisibleAsync();

        await Page.GetByTestId("nav-decks").ClickAsync();
        await Expect(Page.GetByTestId("decks-empty")).ToBeVisibleAsync();

        await Page.GoBackAsync();
        await Expect(Page.GetByTestId("display-name")).ToBeVisibleAsync();
    }
}
