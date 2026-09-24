using Microsoft.Playwright;

namespace Game.Client.Tests;

/// <summary>
/// The deckbuilder under <c>?env=offline</c>: live legality and Power Score, the refusals that happen at the
/// moment of the press rather than at save, and the save round trip through the offline server's own
/// persistence. Client dev server only.
/// </summary>
[Parallelizable(ParallelScope.Self)]
[TestFixture]
public class DeckbuilderTests : OfflineTestBase
{
    // Cards from three different clans, so a third-clan refusal can be provoked deliberately. Their ids are
    // pinned by GameConfigContentTests.
    const string KitsuneCard  = "EmberKit";
    const string TidepoolCard = "TideScholar";
    const string MossbackCard = "MossyYearling";

    [Test]
    public async Task OfflineMode_Deckbuilder_PreventsSelectingAThirdClan()
    {
        await GotoOfflineAsync("/decks/new", "deck-card-count");
        await DeckClan("Kitsune").ClickAsync();

        await PickCard(KitsuneCard).ClickAsync();
        await DeckClan("Tidepool").ClickAsync();
        await PickCard(TidepoolCard).ClickAsync();
        await Expect(Page.GetByTestId("deck-card-count")).ToHaveTextAsync("2");

        await Expect(DeckClan("Mossback")).ToBeDisabledAsync();
        await Expect(PickCard(MossbackCard)).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("deck-card-count")).ToHaveTextAsync("2");
        await Expect(Page.GetByTestId("deck-clan-count")).ToHaveTextAsync("2");
    }

    [Test]
    public async Task OfflineMode_Deckbuilder_RefusesADuplicateCard()
    {
        await GotoOfflineAsync("/decks/new", "deck-card-count");
        await DeckClan("Kitsune").ClickAsync();

        await PickCard(KitsuneCard).ClickAsync();
        await Expect(Page.GetByTestId("deck-card-count")).ToHaveTextAsync("1");

        // The picker marks the card as already taken rather than offering a second copy.
        await Expect(PickCard(KitsuneCard)).ToContainTextAsync("In deck");

        await PickCard(KitsuneCard).ClickAsync();

        await Expect(Page.GetByTestId("pick-refusal")).ToHaveAttributeAsync("data-reason", "DuplicateCard");
        await Expect(Page.GetByTestId("deck-card-count")).ToHaveTextAsync("1");
        await Expect(Page.Locator("[data-testid='deck-card']")).ToHaveCountAsync(1);
    }

    [Test]
    public async Task OfflineMode_Deckbuilder_CardCount_GatesSave()
    {
        await GotoOfflineAsync("/decks/new", "deck-card-count");
        await DeckClan("Kitsune").ClickAsync();

        int target = await DeckSizeTargetAsync();

        await PickCard(KitsuneCard).ClickAsync();
        await Expect(Page.GetByTestId("deck-card-count")).ToHaveTextAsync("1");
        Assert.That(target, Is.GreaterThan(1), "the deck is deliberately short of the target");

        // Deck size is exact, not a maximum, so a short deck cannot be saved and the readout says why.
        await Expect(Page.GetByTestId("save-deck")).ToBeDisabledAsync();
        await Expect(Page.GetByTestId("deck-legality")).ToHaveAttributeAsync("data-error", "WrongSize");
    }

    [Test]
    public async Task OfflineMode_Deckbuilder_PowerScoreUpdatesLive()
    {
        await GotoOfflineAsync("/decks/new", "deck-card-count");
        await DeckClan("Kitsune").ClickAsync();

        int before = await PowerScoreAsync();

        await PickCard(KitsuneCard).ClickAsync();
        await Expect(Page.GetByTestId("deck-card-count")).ToHaveTextAsync("1");
        int withCard = await PowerScoreAsync();

        // Every starter card is at rank 1, so the score moves by exactly that card's rank each way.
        Assert.That(withCard, Is.EqualTo(before + 1));

        await Page.Locator("[data-testid='deck-card'][data-card-id='" + KitsuneCard + "']").ClickAsync();
        await Expect(Page.GetByTestId("deck-card-count")).ToHaveTextAsync("0");
        Assert.That(await PowerScoreAsync(), Is.EqualTo(before));
    }

    [Test]
    public async Task OfflineMode_Deckbuilder_SaveDeck_RoundTripsThroughTheServer()
    {
        await GotoOfflineAsync("/decks/new", "deck-card-count");
        await DeckClan("Kitsune").ClickAsync();

        int target = await DeckSizeTargetAsync();
        await FillLegalDeckAsync();

        await Expect(Page.GetByTestId("deck-legality")).ToHaveAttributeAsync("data-error", "None");

        await Page.GetByTestId("deck-name-input").FillAsync("Sticky Fingers");
        await Expect(Page.GetByTestId("save-deck")).ToBeEnabledAsync();
        await Page.GetByTestId("save-deck").ClickAsync();

        // The save navigates back to the list, where the deck is now one of the account's.
        await Expect(Page.GetByTestId("deck-entry")).ToHaveCountAsync(1);
        await Expect(Page.GetByTestId("deck-name")).ToHaveTextAsync("Sticky Fingers");

        // Reload: offline mode's own persistence, which is what makes the account survive a page refresh.
        await Page.ReloadAsync();
        await Expect(Page.GetByTestId("deck-entry")).ToHaveCountAsync(1, new() { Timeout = BootTimeout });
        await Expect(Page.GetByTestId("deck-name")).ToHaveTextAsync("Sticky Fingers");
        await Expect(Page.GetByTestId("deck-size")).ToContainTextAsync($"{target} cards");
        await Expect(Page.GetByTestId("deck-power-score")).ToHaveTextAsync($"Power {target}");
    }

    /// <summary>
    /// The seeded "New deck" is provisional: it makes a fresh deck savable without
    /// typing, and it gets out of the way of the first keystroke instead of having to be deleted first.
    /// </summary>
    [Test]
    public async Task OfflineMode_Deckbuilder_TheDefaultName_ClearsOnFocusAndComesBackUntouched()
    {
        await GotoOfflineAsync("/decks/new", "deck-card-count");
        await DeckClan("Kitsune").ClickAsync();

        ILocator name = Page.GetByTestId("deck-name-input");
        await Expect(name).ToHaveValueAsync("New deck");

        // Focusing empties the field, which is what lets the placeholder show.
        await name.FocusAsync();
        await Expect(name).ToHaveValueAsync("");
        await Expect(name).ToHaveAttributeAsync("placeholder", "Name this deck");

        // Leaving it untouched puts the default back, so a player who never names the deck can still save it.
        await name.BlurAsync();
        await Expect(name).ToHaveValueAsync("New deck");

        // And a real name still saves.
        await FillLegalDeckAsync();
        await name.FillAsync("Named By Hand");
        await Page.GetByTestId("save-deck").ClickAsync();

        await Expect(Page.GetByTestId("deck-entry")).ToHaveCountAsync(1);
        await Expect(Page.GetByTestId("deck-name")).ToHaveTextAsync("Named By Hand");
    }

    [Test]
    public async Task OfflineMode_Deckbuilder_TrimsTheDeckName()
    {
        await GotoOfflineAsync("/decks/new", "deck-card-count");
        await DeckClan("Kitsune").ClickAsync();

        await FillLegalDeckAsync();
        await Page.GetByTestId("deck-name-input").FillAsync("  Zoomies  ");
        await Page.GetByTestId("save-deck").ClickAsync();

        // Stored the way the shared action stores it, with no leading or trailing space.
        await Expect(Page.GetByTestId("deck-name")).ToHaveTextAsync("Zoomies");
    }

    [Test]
    public async Task OfflineMode_Deckbuilder_DeleteDeck_RemovesItFromTheList()
    {
        await GotoOfflineAsync("/decks/new", "deck-card-count");
        await DeckClan("Kitsune").ClickAsync();

        await FillLegalDeckAsync();
        await Page.GetByTestId("deck-name-input").FillAsync("Doomed");
        await Page.GetByTestId("save-deck").ClickAsync();
        await Expect(Page.GetByTestId("deck-entry")).ToHaveCountAsync(1);

        await Page.GetByTestId("delete-deck").ClickAsync();
        await Page.GetByTestId("confirm-delete-deck").ClickAsync();

        // It was the only deck, so the empty state is what a player sees next.
        await Expect(Page.GetByTestId("deck-entry")).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("decks-empty")).ToBeVisibleAsync();

        // Delete sits outside the row's anchor, so deleting does not also open the deck.
        Assert.That(Page.Url, Does.Contain("/decks"));
        Assert.That(Page.Url, Does.Not.Contain("/decks/"));
    }

    [Test]
    public async Task RemovingAClanRemovesItsCardsAndUndoRestoresThem()
    {
        await GotoOfflineAsync("/decks/new", "deck-card-count");
        await DeckClan("Kitsune").ClickAsync();
        await DeckClan("Tidepool").ClickAsync();
        await PickCard(KitsuneCard).ClickAsync();
        await PickCard(TidepoolCard).ClickAsync();
        await DeckClan("Kitsune").ClickAsync();
        await Expect(Page.GetByTestId("deck-card-count")).ToHaveTextAsync("1");
        await Expect(PickCard(KitsuneCard)).ToHaveCountAsync(0);
        await Page.GetByTestId("clan-undo").GetByRole(AriaRole.Button).ClickAsync();
        await Expect(Page.GetByTestId("deck-card-count")).ToHaveTextAsync("2");
        await Expect(DeckClan("Kitsune")).ToHaveAttributeAsync("aria-pressed", "true");
        await Expect(PickCard(KitsuneCard)).ToContainTextAsync("In deck");
    }

    /// <summary>
    /// Home's picker names a deck on every entry. The defect this pins was an empty
    /// picker on every entry after the first: the seeding ran only from a change handler, and by the time a
    /// player comes back from Decks the collection has long since arrived and raises nothing — leaving a
    /// <c>select</c> whose value matched no option, which renders blank.
    /// <para>
    /// The value is asserted as a present option rather than against a hard-coded id, because Home is a fresh
    /// component on every entry and the fallback lands on the first <em>starter</em> deck rather than on the
    /// saved one. What matters is that the value names an option that exists.
    /// </para>
    /// </summary>
    [Test]
    public async Task OfflineMode_Decks_HomesPracticePicker_NamesADeckOnEveryEntry()
    {
        await GotoOfflineAsync("/decks/new", "deck-card-count");
        await DeckClan("Kitsune").ClickAsync();

        await FillLegalDeckAsync();
        await Page.GetByTestId("deck-name-input").FillAsync("Picker");
        await Page.GetByTestId("save-deck").ClickAsync();
        await Expect(Page.GetByTestId("deck-entry")).ToHaveCountAsync(1);

        // First entry to Home in this session: the collection is already there, so nothing re-raises.
        await Page.GetByTestId("back-home").ClickAsync();
        await AssertPickerNamesAPresentOptionAsync();

        // And again after leaving and coming back, which is where a player meets it.
        await Page.GetByTestId("nav-decks").ClickAsync();
        await Expect(Page.GetByTestId("deck-entry")).ToHaveCountAsync(1);
        await Page.GetByTestId("back-home").ClickAsync();
        await AssertPickerNamesAPresentOptionAsync();

        // And the saved deck really is reachable through it, which is the other half of "names a deck".
        await Page.GetByTestId("practice-deck").SelectOptionAsync("saved:1");
        await AssertPickerNamesAPresentOptionAsync();
        Assert.That(await SelectedOptionKindAsync(), Is.EqualTo("saved"));
    }

    /// <summary>
    /// The picker's value names one of its own options. A <c>select</c> whose value matches nothing renders
    /// blank; this is that property, without pinning which deck wins the fallback.
    /// </summary>
    async Task AssertPickerNamesAPresentOptionAsync()
    {
        ILocator picker = Page.GetByTestId("practice-deck");
        await Expect(picker).ToBeVisibleAsync();

        string value = await picker.InputValueAsync();
        Assert.That(value, Is.Not.Empty, "the picker's value is empty, so it renders blank");
        await Expect(Page.Locator($"[data-testid='deck-option'][value='{value}']")).ToHaveCountAsync(1);
    }

    /// <summary> Whether the selected option is a starter deck or a saved one. </summary>
    async Task<string?> SelectedOptionKindAsync()
    {
        string value = await Page.GetByTestId("practice-deck").InputValueAsync();
        return await Page.Locator($"[data-testid='deck-option'][value='{value}']").GetAttributeAsync("data-kind");
    }

    /// <summary>
    /// The starter decks sit in the same picker as the player's own, in their own group, and the saved group
    /// appears only once there is something in it. This is the fresh-account default: six options and no gate.
    /// </summary>
    [Test]
    public async Task OfflineMode_Home_OffersStarterDecksBesideTheSavedOnes()
    {
        await GotoOfflineAsync("/", "practice-deck");

        // The gate is gone: a fresh account has no saved deck and can still play.
        await Expect(Page.GetByTestId("practice-needs-deck")).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("queue-practice")).ToBeEnabledAsync();

        await Expect(Page.GetByTestId("starter-deck-group")).ToHaveCountAsync(1);
        await Expect(Page.Locator("[data-testid='deck-option'][data-kind='starter']")).ToHaveCountAsync(6);
        await Expect(Page.GetByTestId("saved-deck-group")).ToHaveCountAsync(0);

        // The default is a starter deck, and its one line is on screen beside it.
        Assert.That(await SelectedOptionKindAsync(), Is.EqualTo("starter"));
        await Expect(Page.GetByTestId("deck-description")).ToBeVisibleAsync();

        // Save one, and the second group appears with exactly it in it.
        await GotoOfflineAsync("/decks/new", "deck-card-count");
        await DeckClan("Kitsune").ClickAsync();
        await FillLegalDeckAsync();
        await Page.GetByTestId("deck-name-input").FillAsync("Mine");
        await Page.GetByTestId("save-deck").ClickAsync();
        await Expect(Page.GetByTestId("deck-entry")).ToHaveCountAsync(1);
        await Page.GetByTestId("back-home").ClickAsync();

        await Expect(Page.GetByTestId("saved-deck-group")).ToHaveCountAsync(1);
        await Expect(Page.Locator("[data-testid='deck-option'][data-kind='saved']")).ToHaveCountAsync(1);
        await Expect(Page.Locator("[data-testid='deck-option'][data-kind='starter']")).ToHaveCountAsync(6);
    }

    [Test]
    public async Task OfflineMode_Deckbuilder_ReopensASavedDeckForEditing()
    {
        await GotoOfflineAsync("/decks/new", "deck-card-count");
        await DeckClan("Kitsune").ClickAsync();

        int target = await DeckSizeTargetAsync();
        await FillLegalDeckAsync();
        await Page.GetByTestId("deck-name-input").FillAsync("Round Trip");
        await Page.GetByTestId("save-deck").ClickAsync();
        await Expect(Page.GetByTestId("deck-entry")).ToHaveCountAsync(1);

        // The row itself is the link: there is no separate Edit control.
        await Expect(Page.GetByTestId("open-deck")).ToBeVisibleAsync();
        await Page.GetByTestId("open-deck").ClickAsync();

        // The editor opens on the saved deck, name and cards as they were saved.
        await Expect(Page.GetByTestId("deck-name-input")).ToHaveValueAsync("Round Trip");
        await Expect(Page.GetByTestId("deck-card-count")).ToHaveTextAsync(target.ToString());
        await Expect(Page.GetByTestId("deck-legality")).ToHaveAttributeAsync("data-error", "None");
    }
}
