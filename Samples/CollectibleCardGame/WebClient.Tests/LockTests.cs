using Microsoft.Playwright;

namespace WebClient.Tests;

/// <summary>
/// Lock management on the collection screen under <c>?offline</c>: locking, unlocking, and what happens
/// when every slot is taken — the press asks which lock to move rather than doing nothing. Client dev server
/// only.
/// <para>
/// Every case grows its cards first. <c>Global.MinLockRank</c> is 2 and a fresh account is entirely at the
/// rank floor, so on the account these fixtures start with there is nothing lockable at
/// all — which is itself asserted below. The growing is done through the collection's development-only rank
/// control, because otherwise only a Heist moves a rank.
/// </para>
/// </summary>
[TestFixture]
public class LockTests : OfflineTestBase
{
    const string FirstCard  = "EmberKit";
    const string SecondCard = "Foxfire";
    const string ThirdCard  = "FlameDancer";

    /// <summary> The collection, with the development-only rank control on. </summary>
    async Task GotoCollectionAsync()
    {
        await Page.GotoAsync($"{Offline("/collection")}&dev=ranks");
        await Expect(Page.GetByTestId("collection-grid")).ToBeVisibleAsync(new() { Timeout = BootTimeout });
    }

    /// <summary> Grow the named cards to the lowest rank a lock accepts. </summary>
    async Task MakeLockableAsync(params string[] cardIds)
    {
        foreach (string cardId in cardIds)
            await SetCardRankAsync(cardId, 2);
    }

    [Test]
    public async Task OfflineMode_Collection_AFreshAccountOffersNoLockAtAll()
    {
        // The rule's whole point: at the rank floor a lock freezes nothing and only denies a winner their
        // Heist pick, so the grid advertises no padlock and the card detail says why rather than hiding it.
        await GotoCollectionAsync();

        await Expect(Page.GetByTestId("lock-affordance")).ToHaveCountAsync(0);

        await Card(FirstCard).ClickAsync();
        ILocator affordance = Page.GetByTestId("card-detail").GetByTestId("lock-affordance");
        await Expect(affordance).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("lock-hint")).ToContainTextAsync("Rank 2 and above can be locked");
    }

    /// <summary>
    /// The slot rules are <c>PlayerCollectionTests.PlayerSetLockSlot_*</c>; this plays them the way a player
    /// does. A lock takes the free slot first, a press with no slot free asks which lock to move rather than
    /// being refused, and clearing a slot unlocks its card.
    /// </summary>
    [Test]
    public async Task OfflineMode_Collection_LockManagement()
    {
        await GotoCollectionAsync();
        await MakeLockableAsync(FirstCard, SecondCard, ThirdCard);

        // Two empty slots to start, from Global.InitialLockSlots.
        await Expect(Page.GetByTestId("lock-slot")).ToHaveCountAsync(2);
        await Expect(LockSlot(0)).ToHaveAttributeAsync("data-card-id", "");

        // Lock and unlock.
        await ToggleCardLockAsync(FirstCard);
        await Expect(Card(FirstCard)).ToHaveAttributeAsync("data-locked", "true");
        await Expect(LockSlot(0)).ToHaveAttributeAsync("data-card-id", FirstCard);

        await ToggleCardLockAsync(FirstCard);
        await Expect(Card(FirstCard)).ToHaveAttributeAsync("data-locked", "false");
        await Expect(LockSlot(0)).ToHaveAttributeAsync("data-card-id", "");

        // The free slot fills first.
        await ToggleCardLockAsync(FirstCard);
        await ToggleCardLockAsync(SecondCard);
        await Expect(LockSlot(0)).ToHaveAttributeAsync("data-card-id", FirstCard);
        await Expect(LockSlot(1)).ToHaveAttributeAsync("data-card-id", SecondCard);

        // No slot free: the press asks which lock to move, and exactly one of the original two is unlocked.
        await ToggleCardLockAsync(ThirdCard);
        await Expect(Page.GetByTestId("lock-slot-picker")).ToBeVisibleAsync();
        await Page.Locator("[data-testid='lock-slot-pick'][data-slot='0']").ClickAsync();

        await Expect(Page.GetByTestId("lock-slot-picker")).ToHaveCountAsync(0);
        await Expect(LockSlot(0)).ToHaveAttributeAsync("data-card-id", ThirdCard);
        await Expect(LockSlot(1)).ToHaveAttributeAsync("data-card-id", SecondCard);
        await Expect(Card(FirstCard)).ToHaveAttributeAsync("data-locked", "false");
        await Expect(Card(SecondCard)).ToHaveAttributeAsync("data-locked", "true");
        await Expect(Card(ThirdCard)).ToHaveAttributeAsync("data-locked", "true");

        // Clearing a slot unlocks its card. The picker was answered from the card's detail, which is still open.
        await CloseOverlayAsync();
        await LockSlot(0).GetByTestId("lock-slot-clear").ClickAsync();
        await Expect(LockSlot(0)).ToHaveAttributeAsync("data-card-id", "");
        await Expect(Card(ThirdCard)).ToHaveAttributeAsync("data-locked", "false");
    }

    [Test]
    public async Task OfflineMode_CardDetail_LocksWithoutClosingTheOverlay()
    {
        await GotoCollectionAsync();
        await MakeLockableAsync(FirstCard);

        await Card(FirstCard).ClickAsync();
        await Expect(Page.GetByTestId("card-detail")).ToBeVisibleAsync();

        // The detail view carries the same affordance, so locking does not mean closing the card first.
        await Page.GetByTestId("card-detail").GetByTestId("lock-affordance").ClickAsync();

        await Expect(Page.GetByTestId("card-detail")).ToBeVisibleAsync();
        await Expect(Page.GetByTestId("card-detail").GetByTestId("lock-affordance"))
            .ToHaveAttributeAsync("data-locked", "true");
    }

    [Test]
    public async Task OfflineMode_Deckbuilder_ShowsLockedCardsAsLocked()
    {
        await GotoCollectionAsync();
        await MakeLockableAsync(FirstCard);

        await ToggleCardLockAsync(FirstCard);
        await Expect(LockSlot(0)).ToHaveAttributeAsync("data-card-id", FirstCard);

        // Locks are open information and the mark is the same wherever a card is drawn.
        await Page.GetByTestId("nav-decks").ClickAsync();
        await Page.GetByTestId("new-deck").ClickAsync();
        await Expect(Page.GetByTestId("deck-card-count")).ToBeVisibleAsync();

        await DeckClan("Kitsune").ClickAsync();
        await Expect(PickCard(FirstCard)).ToHaveAttributeAsync("data-locked", "true");
        await Expect(PickCard(SecondCard)).ToHaveAttributeAsync("data-locked", "false");
    }
}
