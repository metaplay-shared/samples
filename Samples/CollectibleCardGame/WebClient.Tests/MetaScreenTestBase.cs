using Microsoft.Playwright;

namespace WebClient.Tests;

/// <summary>
/// Locators and interactions for the meta screens, shared by the offline suites and the live-server one. The
/// screens are the same either way — only where the session runs differs — so the helpers that drive them are
/// stated once here.
/// </summary>
public abstract class MetaScreenTestBase : GamePageTest
{
    /// <summary>The browser contexts <see cref="NewPageAsync"/> opened, closed after the pages are checked.</summary>
    readonly List<IBrowserContext> _contexts = new List<IBrowserContext>();

    /// <summary>
    /// A page in a browser context of its own, for the suites that drive two accounts at once: its own stored
    /// credentials, so its own guest account. Opened with the fixture's own context options unless told otherwise,
    /// checked after the test like the fixture's own page, and closed after that check.
    /// </summary>
    protected async Task<IPage> NewPageAsync(BrowserNewContextOptions? options = null)
    {
        IBrowserContext context = await Browser.NewContextAsync(options ?? ContextOptions());
        lock (_contexts)
            _contexts.Add(context);

        IPage page = Watch(await context.NewPageAsync());
        OnPageOpened(page);
        return page;
    }

    /// <summary>A page <see cref="NewPageAsync"/> opened, for a fixture that watches more of it.</summary>
    protected virtual void OnPageOpened(IPage page) { }

    protected override async Task CloseAfterTestAsync()
    {
        IBrowserContext[] contexts;
        lock (_contexts)
        {
            contexts = _contexts.ToArray();
            _contexts.Clear();
        }

        foreach (IBrowserContext context in contexts)
            await context.CloseAsync();
    }

    /// <summary>
    /// The query this fixture's pages are loaded with besides the stack's own ports, such as <c>offline</c>, or null for
    /// none.
    /// </summary>
    protected virtual string? StartQuery => null;

    /// <summary>
    /// Reload the current route as a player's refresh would, on this test's stack, with <paramref name="query"/> added.
    /// In-app navigation does not keep the query the page was loaded with in the address, so Playwright's
    /// <c>ReloadAsync</c> would start a client on another server, or online instead of offline. The rest of the
    /// current query is kept.
    /// </summary>
    protected Task ReloadOnStackAsync(IPage page, string? query = null)
    {
        Uri current = new Uri(page.Url);
        List<string> pairs = current.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(pair => !pair.StartsWith("wsPort=", StringComparison.Ordinal) && !pair.StartsWith("cdnPort=", StringComparison.Ordinal))
            .ToList();

        // ClientUrl adds the stack's ports again.
        if (StartQuery != null && !pairs.Contains(StartQuery))
            pairs.Insert(0, StartQuery);
        if (!string.IsNullOrEmpty(query))
            pairs.Add(query);

        return page.GotoAsync(ClientUrl(current.AbsolutePath, string.Join('&', pairs)));
    }

    /// <summary> A cold WebAssembly boot plus a session start; generous, because it only ever waits once. </summary>
    protected const int BootTimeout = 30000;

    // The locators and the two interactions below are defined against a NAMED page as well as against this
    // fixture's own, because the two-seat suites drive two accounts at once and a vocabulary bound to one
    // Page cannot reach the second.

    /// <summary>
    /// One card's tile. Scoped to tiles on purpose: a locked card also carries its id on the lock-slot panel,
    /// and a bare data-card-id lookup would find that instead.
    /// </summary>
    protected ILocator Card(string cardId) => Card(cardId, Page);

    protected static ILocator Card(string cardId, IPage page)
        => page.Locator($"[data-testid='card-tile'][data-card-id='{cardId}']");

    /// <summary> The deckbuilder's picker tile for one card. </summary>
    protected ILocator PickCard(string cardId)
        => Page.Locator($"[data-testid='pick-card'][data-card-id='{cardId}']");

    /// <summary> The clan filter chip, shared by the collection and the picker. </summary>
    protected ILocator ClanFilter(string clanId)
        => Page.Locator($"[data-testid='filter-clan'][data-clan='{clanId}']");

    /// <summary> Open a card and use its detail-only lock control. </summary>
    protected Task ToggleCardLockAsync(string cardId) => ToggleCardLockAsync(cardId, Page);

    protected async Task ToggleCardLockAsync(string cardId, IPage page)
    {
        await Card(cardId, page).ClickAsync();
        await page.GetByTestId("card-detail").GetByTestId("lock-affordance").ClickAsync();
        if (await page.GetByTestId("lock-slot-picker").CountAsync() == 0)
            await CloseOverlayAsync(page);
    }

    protected ILocator DeckClan(string clanId)
        => Page.Locator($"[data-testid='deck-clan'][data-clan='{clanId}']");

    protected ILocator LockSlot(int index)
        => Page.Locator($"[data-testid='lock-slot'][data-slot='{index}']");

    /// <summary>
    /// Grow a card to a rank, through the collection's development-only rank control — the screen must have
    /// been entered with <c>dev=ranks</c>. Nothing in the game moves a rank yet (ranks move through the Heist,
    /// and practice stakes move nothing), so this is how a fixture reaches a rule that turns on rank, such as
    /// the lock threshold every case below needs.
    /// </summary>
    protected Task SetCardRankAsync(string cardId, int rank) => SetCardRankAsync(cardId, rank, Page);

    /// <summary> The same growing, on a named page. </summary>
    protected async Task SetCardRankAsync(string cardId, int rank, IPage page)
    {
        await Card(cardId, page).ClickAsync();
        await Expect(page.GetByTestId("dev-rank-row")).ToBeVisibleAsync();
        await page.Locator($"[data-testid='dev-set-rank'][data-rank='{rank}']").ClickAsync();
        await Expect(page.Locator("[data-testid='card-detail-rank']")).ToHaveAttributeAsync("data-rank", rank.ToString());
        await CloseOverlayAsync(page);
        await Expect(Card(cardId, page)).ToHaveAttributeAsync("data-rank", rank.ToString());
    }

    /// <summary>
    /// One keyword chip, wherever a card is drawn. Named by the keyword's config id (or the trigger's name)
    /// rather than its display copy, so the locator survives a wording edit.
    /// </summary>
    protected ILocator KeywordChip(string keyword)
        => Page.Locator($"[data-testid='keyword-chip'][data-keyword='{keyword}']");

    /// <summary>
    /// Open Home's change-name modal, which is where the rename lives: the pencil beside the displayed name,
    /// then the input inside the modal it opens. Every fixture that renames goes through here.
    /// </summary>
    protected async Task OpenChangeNameAsync()
    {
        await Expect(Page.GetByTestId("display-name-edit")).ToBeVisibleAsync(new() { Timeout = BootTimeout });
        await Page.GetByTestId("display-name-edit").ClickAsync();
        await Expect(Page.GetByTestId("change-name-modal")).ToBeVisibleAsync();
        await Expect(Page.GetByTestId("display-name-input")).ToBeVisibleAsync();
    }

    /// <summary> Dismiss the open overlay through its header close button, as a player does. </summary>
    protected Task CloseOverlayAsync() => CloseOverlayAsync(Page);

    protected static Task CloseOverlayAsync(IPage page)
        => page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "✕" }).ClickAsync();

    protected async Task<int> DeckCardCountAsync()
        => int.Parse((await Page.GetByTestId("deck-card-count").InnerTextAsync()).Trim());

    protected async Task<int> DeckSizeTargetAsync()
        => int.Parse((await Page.GetByTestId("deck-size-target").InnerTextAsync()).Trim());

    protected async Task<int> PowerScoreAsync()
        => int.Parse((await Page.GetByTestId("deck-power-score").First.InnerTextAsync()).Trim());

    /// <summary>
    /// Fill the open editor with a legal deck: Wanderers first, since they never spend a clan slot, then whole
    /// clans until the deck is full. The pool is sized so this reaches the exact deck size inside the clan
    /// limit, which is a rule the config build itself proves.
    /// </summary>
    protected async Task FillLegalDeckAsync()
    {
        int target = await DeckSizeTargetAsync();
        int added = await DeckCardCountAsync();

        foreach (string clanId in new[] { "Kitsune", "Tidepool" })
        {
            if (await DeckClan(clanId).GetAttributeAsync("aria-pressed") != "true")
                await DeckClan(clanId).ClickAsync();
        }
        ILocator tiles = Page.Locator("[data-testid='pick-card']:not(:has([data-testid='card-badge']))");
        while (added < target && await tiles.CountAsync() > 0)
        {
            await tiles.First.ClickAsync();
            added++;
        }

        await Expect(Page.GetByTestId("deck-card-count")).ToHaveTextAsync(target.ToString());
    }
}
