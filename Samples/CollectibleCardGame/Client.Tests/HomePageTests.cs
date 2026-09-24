using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

namespace Game.Client.Tests;

/// <summary>
/// E2E tests against a live game server. Requires both the game server and the Client dev server to be
/// running:
/// <code>
/// dotnet run --project Backend/Server
/// dotnet run --project Client/Client.csproj
/// </code>
/// <para>
/// The offline suites cover the meta screens' behavior; what needs a real server is anything whose point is
/// that state survived the server's own persistence rather than the browser's.
/// </para>
/// </summary>
[Parallelizable(ParallelScope.Self)]
[TestFixture]
public class HomePageTests : MetaScreenTestBase
{

    [Test]
    public async Task HomePage_LoadsSuccessfully()
    {
        await Page.GotoAsync(BaseUrl);
        await Expect(Page).ToHaveTitleAsync("Sticky Paws");
    }

    [Test]
    public async Task HomePage_ConnectsToServer()
    {
        await Page.GotoAsync(BaseUrl);

        // The header shows "Connected" once a session has started. Use an exact match so it doesn't also
        // match the hidden Blazor "Browser Disconnected" reconnect banner.
        ILocator connected = Page.GetByText("Connected", new() { Exact = true });
        await Expect(connected).ToBeVisibleAsync(new() { Timeout = 20000 });

        // The connecting spinner message should be gone.
        ILocator connecting = Page.GetByText("Connecting to game server...");
        await Expect(connecting).Not.ToBeVisibleAsync();
    }

    [Test]
    public async Task HomePage_ShowsTheMenu_WhenConnected()
    {
        await Page.GotoAsync(BaseUrl);

        // Once connected, the menu shows the player's display name, which comes from the player model.
        ILocator displayName = Page.GetByTestId("display-name");
        await Expect(displayName).ToBeVisibleAsync(new() { Timeout = 20000 });
        await Expect(displayName).Not.ToBeEmptyAsync();
    }

    [Test]
    public async Task RenamingThePlayer_RoundTripsThroughTheServer()
    {
        await Page.GotoAsync(BaseUrl);
        await Expect(Page.GetByTestId("display-name")).ToBeVisibleAsync(new() { Timeout = 20000 });

        // The rename is a modal behind the pencil beside the name, so nothing about it is on the page until
        // it is asked for.
        await Expect(Page.GetByTestId("change-name-modal")).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("display-name-input")).ToHaveCountAsync(0);

        await OpenChangeNameAsync();
        ILocator input = Page.GetByTestId("display-name-input");

        // It opens on the name as it stands, so a small edit is an edit rather than a retype.
        await Expect(input).Not.ToBeEmptyAsync();

        // Typing a new name and saving runs the PlayerSetDisplayName action: the client predicts it and the
        // server re-runs it authoritatively, so a mismatch would surface as a checksum desync rather than as
        // a stale label.
        await input.FillAsync("Whiskers");
        await Page.GetByTestId("save-name").ClickAsync();

        await Expect(Page.GetByTestId("display-name")).ToHaveTextAsync("Whiskers");
        await Expect(Page.GetByTestId("change-name-modal")).ToHaveCountAsync(0);
    }

    [Test]
    public async Task SaveButton_IsDisabledForAnEmptyName()
    {
        await Page.GotoAsync(BaseUrl);
        await OpenChangeNameAsync();

        // The same rule the shared action enforces, applied up front so the player is not offered an action
        // the server would refuse.
        await Page.GetByTestId("display-name-input").FillAsync("");
        await Expect(Page.GetByTestId("save-name")).ToBeDisabledAsync();
    }

    /// <summary>
    /// A second entry the server refuses must cost the player nothing — not the session, and not the deck the
    /// account is remembered as having played (wave 2a review, C1).
    /// <para>
    /// The refusal is decided by <c>CurrentMatch</c>, which is <c>ServerOnly</c> and reads default on the
    /// client, so the client's predicted run of the entry action always succeeds where the server's may not.
    /// While that action recorded the chosen deck in its own commit body, the difference was a checksum
    /// mismatch on a public member — which the SDK answers by <em>ending the session</em>. The deck is now
    /// recorded by a server action where the pointer is assigned, so a predicted run records nothing at all.
    /// </para>
    /// <para>
    /// Reaching Home with a live table needs no race: the router takes the page back without restarting the
    /// session, so the account is still at the match it is in, and Home navigates only on an attachment
    /// <em>change</em>.
    /// </para>
    /// </summary>
    [Test]
    public async Task ARefusedSecondEntry_KeepsTheSessionAndTheRememberedDeck()
    {
        await SaveADeckAsync("Alpha");

        await Page.GotoAsync(BaseUrl);
        ILocator picker = Page.GetByTestId("practice-deck");
        await Expect(picker).ToBeVisibleAsync(new() { Timeout = BootTimeout });

        // Enter with a starter deck, so the round trip this proves is the one the change adds:
        // PlayerNoteDeckPlayed → PlayerModel.LastPlayedDeck → the picker, for a deck the account never saved.
        await picker.SelectOptionAsync("starter:SunlitThicket");
        await Page.GetByTestId("queue-practice").ClickAsync();
        await Expect(Page.GetByTestId("match-board")).ToBeVisibleAsync(new() { Timeout = BootTimeout });

        // Back to Home, session and table both alive.
        await Page.GoBackAsync();
        await Expect(picker).ToBeVisibleAsync(new() { Timeout = BootTimeout });

        // The seat the server recorded reached the client, and a freshly built Home opens on it.
        await Expect(picker).ToHaveValueAsync("starter:SunlitThicket", new() { Timeout = BootTimeout });

        // The refused press: the saved deck, while this account is still at the first table. The in-flight
        // lock has long since lifted, so the action really is sent.
        await picker.SelectOptionAsync("saved:1");
        await Expect(Page.GetByTestId("queue-practice")).ToBeEnabledAsync(new() { Timeout = BootTimeout });
        await Page.GetByTestId("queue-practice").ClickAsync();

        // The session is still up — a checksum mismatch would have dropped it into the connection-trouble
        // shell — and Home is still Home, because the server minted no second table.
        await Expect(Page.GetByText("Connected", new() { Exact = true })).ToBeVisibleAsync(new() { Timeout = BootTimeout });
        await Expect(Page.GetByTestId("display-name")).ToBeVisibleAsync();
        await Expect(Page.GetByTestId("match-board")).ToHaveCountAsync(0);

        // And the account still remembers the deck it was seated with rather than the one the refused press
        // named: Decks and back rebuilds Home, which re-seeds the picker from the model.
        await Page.GetByTestId("nav-decks").ClickAsync();
        await Expect(Page.GetByTestId("deck-list")).ToBeVisibleAsync(new() { Timeout = BootTimeout });
        await Page.GetByTestId("back-home").ClickAsync();
        await Expect(Page.GetByTestId("practice-deck")).ToHaveValueAsync("starter:SunlitThicket", new() { Timeout = BootTimeout });
    }

    /// <summary>
    /// A ranked entry the server refuses is refused <b>visibly</b>, which is the thing no other refusal on the
    /// meta screens manages.
    /// <para>
    /// Every other one is invisible: the model simply does not change, and the SDK has no route for an
    /// action's result. Matchmaking has a directed server-to-client channel on the player timeline, and this is
    /// what it buys — the searching dialog closes on an answer rather than on its own guard, and the player is
    /// told why.
    /// </para>
    /// </summary>
    [Test]
    public async Task ARefusedRankedEntry_ClosesTheDialogWithAnAnswer()
    {
        await Page.GotoAsync(BaseUrl);
        await Expect(Page.GetByTestId("practice-deck")).ToBeVisibleAsync(new() { Timeout = BootTimeout });

        // Get the account onto a table by the path that does not touch the queue, so the refusal below is
        // about the pointer rather than about a second search.
        await Page.GetByTestId("queue-practice").ClickAsync();
        await Expect(Page.GetByTestId("match-board")).ToBeVisibleAsync(new() { Timeout = BootTimeout });

        // Back to Home past the router, with the table still live: Home navigates only on an attachment
        // change, so this needs no race.
        await Page.GoBackAsync();
        await Expect(Page.GetByTestId("queue-ranked")).ToBeEnabledAsync(new() { Timeout = BootTimeout });

        await Page.GetByTestId("queue-ranked").ClickAsync();

        // The dialog goes up optimistically, on the tap, before the server has answered anything.
        await Expect(Page.GetByTestId("searching-dialog")).ToBeVisibleAsync(new() { Timeout = BootTimeout });

        // And comes down on the server's answer — well inside the client's own five-second guard, which is
        // what would otherwise close it. The notice is how the refusal is visible at all.
        await Expect(Page.GetByTestId("searching-dialog")).Not.ToBeVisibleAsync(new() { Timeout = 4000 });
        await Expect(Page.GetByTestId("search-ended-notice")).ToBeVisibleAsync(new() { Timeout = BootTimeout });

        // The session survived: a refusal is an answer, not a checksum mismatch.
        await Expect(Page.GetByText("Connected", new() { Exact = true })).ToBeVisibleAsync(new() { Timeout = BootTimeout });
        await Expect(Page.GetByTestId("match-board")).ToHaveCountAsync(0);

        // And the notice is read once and dismissed, like the match-gone one.
        await Page.GetByTestId("search-ended-dismiss").ClickAsync();
        await Expect(Page.GetByTestId("search-ended-notice")).ToHaveCountAsync(0);
    }

    /// <summary>
    /// A ticket in the queue is a claim on this account's next seat, so a practice table must not be minted
    /// under it: the matchmaker would go on to pair a player it can no longer seat, and the other human would
    /// play a <em>ranked</em> game against a seat nobody ever subscribes to.
    /// <para>
    /// The press is dispatched rather than clicked, on purpose. Home covers Practice with the searching
    /// dialog's own backdrop while a search is up, so a real pointer cannot reach it — which is exactly why the
    /// rule has to be the server's: a UI that happens to hide a control is not a guard, and the reconciliation
    /// already signposts moving that dialog to the shell, at which point the covering stops.
    /// </para>
    /// </summary>
    [Test]
    public async Task APracticeEntryDuringASearch_SeatsNothing()
    {
        await Page.GotoAsync(BaseUrl);
        await Expect(Page.GetByTestId("queue-ranked")).ToBeEnabledAsync(new() { Timeout = BootTimeout });
        await Page.GetByTestId("queue-ranked").ClickAsync();
        await Expect(Page.GetByTestId("searching-dialog")).ToBeVisibleAsync(new() { Timeout = BootTimeout });

        // Straight past the backdrop, at the handler the covered control carries.
        await Page.GetByTestId("queue-practice").DispatchEventAsync("click");

        // A practice mint lands in well under a second when it is accepted, so a board that is not here after
        // this settle is a board the server refused. The end-to-end profile's fill wait is eight seconds, which
        // is what bounds the whole assertion.
        await Page.WaitForTimeoutAsync(2000);
        await Expect(Page.GetByTestId("match-board")).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("searching-dialog")).ToBeVisibleAsync();

        // And the search is still the account's own to leave, which is what says nothing else took it over.
        await Page.GetByTestId("searching-cancel").ClickAsync();
        await Expect(Page.GetByTestId("searching-dialog")).Not.ToBeVisibleAsync(new() { Timeout = BootTimeout });
        await Expect(Page.GetByTestId("match-board")).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("queue-ranked")).ToBeEnabledAsync(new() { Timeout = BootTimeout });
    }

    /// <summary>
    /// <b>This is the feature.</b> A brand-new account plays without visiting the deckbuilder, because the
    /// starter decks are content the account already owns every card of — so there is no gate to pass and
    /// nothing to build first.
    /// </summary>
    [Test]
    public async Task AFreshAccountCanPlayWithoutBuildingADeck()
    {
        await Page.GotoAsync(BaseUrl);
        await Expect(Page.GetByTestId("display-name")).ToBeVisibleAsync(new() { Timeout = BootTimeout });

        // No saved decks, and no "build a deck to play" anywhere: that testid no longer exists.
        await Expect(Page.GetByTestId("practice-needs-deck")).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("saved-deck-group")).ToHaveCountAsync(0);
        await Expect(Page.Locator("[data-testid='deck-option'][data-kind='starter']")).ToHaveCountAsync(6);

        // Both entries are live on the account's first visit.
        await Expect(Page.GetByTestId("queue-practice")).ToBeEnabledAsync(new() { Timeout = BootTimeout });
        await Expect(Page.GetByTestId("queue-ranked")).ToBeEnabledAsync(new() { Timeout = BootTimeout });

        // And Practice really seats it.
        await Page.GetByTestId("queue-practice").ClickAsync();
        await Expect(Page.GetByTestId("match-board")).ToBeVisibleAsync(new() { Timeout = BootTimeout });
    }

    /// <summary>
    /// A starter deck is seated at the account's <em>own</em> ranks. It is a card list, not a power level, so
    /// growing one of its cards moves the Power Score the reveal states — which is what keeps the stakes
    /// honest for a veteran who picks one.
    /// </summary>
    [Test]
    public async Task TheStarterDeckIsSeatedAtTheAccountsOwnRanks()
    {
        // EmberKit is in Fire &amp; Foam (Kitsune + Tidepool), pinned by GameConfigContentTests.
        await Page.GotoAsync($"{BaseUrl}/collection?dev=ranks");
        await Expect(Page.GetByTestId("collection-grid")).ToBeVisibleAsync(new() { Timeout = BootTimeout });
        await SetCardRankAsync("EmberKit", 3);

        await Page.GotoAsync(BaseUrl);
        ILocator picker = Page.GetByTestId("practice-deck");
        await Expect(picker).ToBeVisibleAsync(new() { Timeout = BootTimeout });
        await picker.SelectOptionAsync("starter:FireAndFoam");

        await Page.GetByTestId("queue-practice").ClickAsync();
        await Expect(Page.GetByTestId("match-board")).ToBeVisibleAsync(new() { Timeout = BootTimeout });

        // Twenty-five cards at rank 1, one of them grown to 3: the reveal states 27, not 25.
        ILocator localScore = Page.Locator("[data-testid='reveal-power-score'][data-seat='0']");
        await Expect(localScore).ToBeVisibleAsync(new() { Timeout = BootTimeout });
        await Expect(localScore).ToHaveAttributeAsync("data-power-score", "27");
    }

    /// <summary> The account's rating is on Home, and nowhere else. </summary>
    [Test]
    public async Task HomeShowsTheAccountsRating()
    {
        await Page.GotoAsync(BaseUrl);

        ILocator record = Page.GetByTestId("record-summary");
        await Expect(record).ToBeVisibleAsync(new() { Timeout = BootTimeout });

        // Seeded from the config at account creation rather than defaulted to zero, because the queue bands
        // on it and an account at zero would sit a whole band away from every other new one.
        string rating = await record.GetAttributeAsync("data-rating") ?? "0";
        Assert.That(int.Parse(rating), Is.GreaterThan(0), "a fresh account should carry the seeded rating");
        await Expect(Page.GetByTestId("ranked-rating")).ToHaveTextAsync(rating);
    }

    /// <summary> Save one legal deck under a name, the way a player builds one. </summary>
    async Task SaveADeckAsync(string name)
    {
        await Page.GotoAsync($"{BaseUrl}/decks/new");
        await Expect(Page.GetByTestId("deck-card-count")).ToBeVisibleAsync(new() { Timeout = BootTimeout });

        await FillLegalDeckAsync();
        await Page.GetByTestId("deck-name-input").FillAsync(name);
        await Page.GetByTestId("save-deck").ClickAsync();
        await Expect(Page.GetByTestId("deck-name").Last).ToHaveTextAsync(name, new() { Timeout = BootTimeout });
    }

    /// <summary>
    /// The deck and the lock survive the session that made them, against real server-side persistence.
    /// <para>
    /// This also happens to exercise the shell's flush-on-page-hide — the lock is set and then navigated away
    /// from with a full page load, which before that fix lost the action before it ever reached the server —
    /// but it does not <em>isolate</em> it: a failure here could equally be persistence, the reload, or the
    /// session start. Isolating the flush would mean observing that no action message left the client, which
    /// is below what a Playwright fixture can see, so it is deliberately not claimed as coverage of it. The
    /// behavior itself is stated in <c>MetaplayClientServiceBase.OnPageLeavingScreen</c>.
    /// </para>
    /// </summary>
    [Test]
    public async Task SavedDeckAndLock_PersistAcrossAReconnect_WithLiveServer()
    {
        // Lock a card, then build and save a deck, both against the real server. The lock needs a card above
        // the rank floor, and the rank itself is server state that has to survive the
        // reload below — so the development-only rank control is on, and the account's grown rank is a second
        // thing this fixture proves the server persisted.
        await Page.GotoAsync($"{BaseUrl}/collection?dev=ranks");
        await Expect(Page.GetByTestId("collection-grid")).ToBeVisibleAsync(new() { Timeout = BootTimeout });

        await SetCardRankAsync("EmberKit", 2);
        await ToggleCardLockAsync("EmberKit");
        await Expect(LockSlot(0)).ToHaveAttributeAsync("data-card-id", "EmberKit");

        await Page.GotoAsync($"{BaseUrl}/decks/new");
        await Expect(Page.GetByTestId("deck-card-count")).ToBeVisibleAsync(new() { Timeout = BootTimeout });

        int target = await DeckSizeTargetAsync();
        await FillLegalDeckAsync();
        await Page.GetByTestId("deck-name-input").FillAsync("Server Side");
        await Page.GetByTestId("save-deck").ClickAsync();

        // Generous: this is the heaviest fixture in the suite and the first live session after a server start
        // pays for the server's own warm-up.
        await Expect(Page.GetByTestId("deck-entry")).ToHaveCountAsync(1, new() { Timeout = BootTimeout });

        // Reload: the session is torn down and started again, so everything below came back from the server's
        // own persistence rather than from anything the browser kept.
        await Page.ReloadAsync();
        await Expect(Page.GetByTestId("deck-entry")).ToHaveCountAsync(1, new() { Timeout = BootTimeout });
        await Expect(Page.GetByTestId("deck-name")).ToHaveTextAsync("Server Side");
        await Expect(Page.GetByTestId("deck-size")).ToContainTextAsync($"{target} cards");

        await Page.GotoAsync($"{BaseUrl}/collection");
        await Expect(Page.GetByTestId("collection-grid")).ToBeVisibleAsync(new() { Timeout = BootTimeout });
        await Expect(LockSlot(0)).ToHaveAttributeAsync("data-card-id", "EmberKit");
        await Expect(Card("EmberKit")).ToHaveAttributeAsync("data-locked", "true");
        await Expect(Card("EmberKit")).ToHaveAttributeAsync("data-rank", "2");
    }
}
