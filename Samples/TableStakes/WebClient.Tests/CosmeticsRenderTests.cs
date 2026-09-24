using System.Text.RegularExpressions;
using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using WebClient.Components.Pages.Meta;
using WebClient.Meta;

namespace WebClient.Tests;

/// <summary>
/// Render tests for the Cosmetics page and the Shop's link to it, drawn from the fixture view.
/// <c>LiveServerCosmeticsTests</c> covers purchases, equipping, reload persistence and the published slots. The
/// tile tests read the default fixture's Frames tab, the only tab with a tile in every ownership state
/// (<see cref="MetaFixturesTests"/> checks this).
/// </summary>
[TestFixture]
public class CosmeticsRenderTests : BunitPageTest
{
    /// <summary>
    /// The page draws one tab bar with one tab per slot in the view. <c>LiveServerCosmeticsTests</c> checks which
    /// slots the published catalogue provides.
    /// </summary>
    [Test]
    public void TheGridDrawsOneTabPerSlot()
    {
        IRenderedComponent<CosmeticsPage> page = RenderWardrobe();

        Assert.That(page.FindAll("[data-testid=\"cosmetic-slots\"]"), Has.Count.EqualTo(1));
        foreach (string slot in new[] { "slot-avatar", "slot-frame", "slot-nameeffect" })
            Assert.That(page.FindAll($"[data-testid=\"{slot}\"]"), Has.Count.EqualTo(1), slot);
    }

    /// <summary>
    /// The page draws the preview card first, then the slot tabs, then the slot's grid. A grid above the card
    /// would push the preview below the first screen on a phone.
    /// </summary>
    [Test]
    public void ThePreviewCardLeadsThePage()
    {
        IRenderedComponent<CosmeticsPage> page = RenderWardrobe();

        string markup = page.Markup;
        int preview = markup.IndexOf("data-testid=\"cosmetic-preview\"", StringComparison.Ordinal);
        int tabs    = markup.IndexOf("data-testid=\"cosmetic-slots\"", StringComparison.Ordinal);
        int grid    = markup.IndexOf("m-cosmetics-grid m-cosmetics-grid--", StringComparison.Ordinal);

        Assert.That(preview, Is.GreaterThanOrEqualTo(0), "the preview card is drawn at all");
        Assert.That(tabs,    Is.GreaterThanOrEqualTo(0), "the slot tabs are drawn");
        Assert.That(grid,    Is.GreaterThanOrEqualTo(0), "the slot grid is drawn");
        Assert.That(preview, Is.LessThan(tabs), "the preview card renders before the tabs");
        Assert.That(tabs,    Is.LessThan(grid), "the tabs render before the grid");
    }

    /// <summary>
    /// Each slot's grid has its own modifier class, which sets that slot's fixed column count.
    /// </summary>
    [TestCase("Avatar",     "m-cosmetics-grid--avatar")]
    [TestCase("Frame",      "m-cosmetics-grid--frame")]
    [TestCase("NameEffect", "m-cosmetics-grid--nameeffect")]
    public void EachSlotGridsAtItsOwnFixedWidth(string initialSlot, string expectedClass)
    {
        IRenderedComponent<CosmeticsPage> page = RenderSlot(initialSlot);

        Assert.That(page.FindAll(".m-cosmetics-grid"), Has.Count.EqualTo(1), "the slot's one grid");
        Assert.That(page.Find(".m-cosmetics-grid").GetAttribute("class"), Does.Contain(expectedClass));
    }

    /// <summary>
    /// Each slot grid sets a fixed column count instead of <c>auto-fit</c>, and <c>.m-grid--tight</c> is not used.
    /// The preview card is not sticky, because a sticky card would take screen space on every tab. A bUnit render
    /// applies no CSS, so the test reads the rules from meta-shell.css.
    /// </summary>
    [Test]
    public void TheGridsAreFixedColumnsAndThePreviewIsntSticky()
    {
        string css = CssSource.MetaCss();

        Assert.That(css, Does.Not.Contain("m-grid--tight"), "the auto-fit shape the page grew up with is gone");

        foreach ((string slot, string columns) in new[]
                 {
                     ("avatar",     "repeat(4, 1fr)"),
                     ("frame",      "repeat(3, 1fr)"),
                     ("nameeffect", "1fr"),
                 })
        {
            Match rule = Regex.Match(css, $@"\.m-cosmetics-grid--{slot}\s*\{{([^}}]*)\}}");
            Assert.That(rule.Success, Is.True, $".m-cosmetics-grid--{slot} has a rule");
            Assert.That(rule.Groups[1].Value, Does.Contain(columns), $"the {slot} grid's fixed count");
            Assert.That(rule.Groups[1].Value, Does.Not.Contain("auto-fit"), $"the {slot} grid's shape is a decision");
        }

        Match preview = Regex.Match(css, @"\.m-cosmetic-preview\s*\{([^}]*)\}");
        Assert.That(preview.Success, Is.True, ".m-cosmetic-preview has a rule");
        Assert.That(preview.Groups[1].Value, Does.Not.Contain("position: sticky"), "the preview card is not sticky");
    }

    /// <summary>
    /// The tile badge is centred in the tile (its own rule overrides the tag's <c>align-self</c>), the name effect
    /// row centres its parts vertically, and the preview card is a centred column. A bUnit render applies no CSS,
    /// so the test reads the rules from meta-shell.css.
    /// </summary>
    [Test]
    public void TheTileBadgeTheEffectRowAndThePreviewCardAreCentred()
    {
        string css = CssSource.MetaCss();

        Match badge = Regex.Match(css, @"\.m-cosmetic \.m-cosmetic__badge\s*\{([^}]*)\}");
        Assert.That(badge.Success, Is.True, ".m-cosmetic .m-cosmetic__badge has a rule");
        Assert.That(badge.Groups[1].Value, Does.Contain("align-self: center"), "the tile badge sits on the tile's centre line");

        Match row = Regex.Match(css, @"\.m-cosmetic--row\s*\{([^}]*)\}");
        Assert.That(row.Success, Is.True, ".m-cosmetic--row has a rule");
        Assert.That(row.Groups[1].Value, Does.Contain("align-items: center"), "the effect row's four parts share one mid-line");

        Match preview = Regex.Match(css, @"\.m-cosmetic-preview\s*\{([^}]*)\}");
        Assert.That(preview.Success, Is.True, ".m-cosmetic-preview has a rule");
        Assert.That(preview.Groups[1].Value, Does.Contain("align-items: center"), "the preview card is a centred column");
    }

    /// <summary>
    /// The Shop's Cosmetics card links to the Cosmetics page, which previews an item on the player before they buy
    /// it. The Shop does not sell cosmetics itself.
    /// </summary>
    [Test]
    public void TheShopLinksToCosmetics()
    {
        Setup("/shop");
        IRenderedComponent<ShopPage> page = RenderComponent<ShopPage>();

        IElement card = page.Find("[data-testid=\"shop-cosmetics\"]");
        Assert.That(card.TextContent, Does.Contain("Cosmetics"));

        page.Find("[data-testid=\"shop-cosmetics-action\"]").Click();

        NavigationManager nav = Services.GetRequiredService<NavigationManager>();
        Assert.That(nav.Uri, Is.EqualTo("http://localhost/profile/cosmetics"));
    }

    /// <summary>Renders the Cosmetics page on its default tab, which holds a tile in every ownership state.</summary>
    private IRenderedComponent<CosmeticsPage> RenderWardrobe(string? scenario = null)
    {
        Setup("/profile/cosmetics", scenario);
        return RenderComponent<CosmeticsPage>();
    }

    /// <summary>Renders the Cosmetics page on the tab for <paramref name="slot"/>.</summary>
    private IRenderedComponent<CosmeticsPage> RenderSlot(string slot)
    {
        Setup("/profile/cosmetics");
        return RenderComponent<CosmeticsPage>(parameters => parameters.Add(p => p.InitialSlot, slot));
    }

    /// <summary>Finds a tile by its catalogue id.</summary>
    private static IElement Tile(IRenderedComponent<CosmeticsPage> page, string id) =>
        page.Find($"[data-testid=\"cosmetic-{id}\"]");

    /// <summary>Finds the state badge of the tile with catalogue id <paramref name="id"/>.</summary>
    private static IElement StateBadge(IRenderedComponent<CosmeticsPage> page, string id) =>
        FindIn(Tile(page, id), "[data-testid=\"cosmetic-state\"]");

    /// <summary>Finds an element inside <paramref name="scope"/>, or fails with a message that names the selector.</summary>
    private static IElement FindIn(IElement scope, string selector) =>
        scope.QuerySelector(selector) ?? throw new AssertionException($"No element matching {selector} inside {scope.GetAttribute("data-testid")}.");

    // -------------------------------------------------------------------------------------------
    // State badges: one chip per state, each with a word or icon in addition to its colour.
    // -------------------------------------------------------------------------------------------

    /// <summary>
    /// The Equipped badge uses the claim chip style and the word "Equipped". The Owned badge uses the quiet chip
    /// style and the word "Owned".
    /// </summary>
    [TestCase("frame.sapphire", "equipped", "m-tag--claim", "Equipped")]
    [TestCase("frame.brass",    "owned",    "m-tag--quiet", "Owned")]
    public void AnInventoryBadgeIsItsChipAndItsWord(string id, string state, string chipClass, string word)
    {
        IRenderedComponent<CosmeticsPage> page = RenderWardrobe();

        IElement badge = StateBadge(page, id);
        Assert.Multiple(() =>
        {
            Assert.That(badge.GetAttribute("data-state"), Is.EqualTo(state));
            Assert.That(badge.ClassList, Does.Contain(chipClass));
            Assert.That(badge.TextContent, Does.Contain(word));
        });
    }

    /// <summary>The Affordable badge shows the currency icon and the price, not struck through.</summary>
    [Test]
    public void TheAffordableBadgeCarriesTheCurrencyAndAmount()
    {
        IRenderedComponent<CosmeticsPage> page = RenderWardrobe();

        IElement badge = StateBadge(page, "frame.silver");
        Assert.Multiple(() =>
        {
            Assert.That(badge.GetAttribute("data-state"), Is.EqualTo("affordable"));
            Assert.That(badge.QuerySelector(".m-icon"), Is.Not.Null, "the currency icon");
            Assert.That(badge.TextContent, Does.Contain("500"));
            Assert.That(badge.QuerySelector("s"), Is.Null, "an affordable price is not struck");
        });
    }

    /// <summary>The Locked badge says only "Locked", because a sentence does not fit on a tile. The unlock
    /// requirement is shown on the preview card.</summary>
    [Test]
    public void TheLockedBadgeNamesItselfAndNotTheRequirement()
    {
        IRenderedComponent<CosmeticsPage> page = RenderWardrobe();

        IElement badge = StateBadge(page, "frame.champion");
        Assert.Multiple(() =>
        {
            Assert.That(badge.GetAttribute("data-state"), Is.EqualTo("locked"));
            Assert.That(badge.QuerySelector(".m-icon"), Is.Not.Null, "the lock icon");
            Assert.That(badge.TextContent, Does.Contain("Locked"));
            Assert.That(badge.TextContent, Does.Not.Contain("Win a tournament season"));
        });

        FindIn(Tile(page, "frame.champion"), ".m-cosmetic__body").Click();
        Assert.That(page.Find("[data-testid=\"cosmetic-preview\"]").TextContent, Does.Contain("Win a tournament season"),
            "the requirement sentence lives on the preview card");
    }

    /// <summary>
    /// The Unaffordable badge differs from the Affordable badge only by striking through the amount. The tile is too
    /// small to use a second colour for the difference. The Unavailable badge is struck through the same way.
    /// </summary>
    [TestCase("frame.emerald", "unaffordable", "150")]
    [TestCase("frame.retired", "unavailable",  "Unavailable")]
    public void AnOutOfReachBadgeIsStruckThrough(string id, string state, string struckText)
    {
        IRenderedComponent<CosmeticsPage> page = RenderWardrobe();

        IElement badge = StateBadge(page, id);
        Assert.Multiple(() =>
        {
            Assert.That(badge.GetAttribute("data-state"), Is.EqualTo(state));
            Assert.That(badge.QuerySelector("s"), Is.Not.Null, "the struck text");
            Assert.That(badge.QuerySelector("s")?.TextContent, Does.Contain(struckText));
        });
    }

    // -------------------------------------------------------------------------------------------
    // Tile actions: Owned and Affordable tiles have a button, and tiles in the other states have none.
    // -------------------------------------------------------------------------------------------

    [Test]
    public void TheOwnedTileCarriesEquip()
    {
        IRenderedComponent<CosmeticsPage> page = RenderWardrobe();

        IElement equip = FindIn(Tile(page, "frame.brass"), "[data-testid=\"cosmetic-equip\"]");
        Assert.Multiple(() =>
        {
            Assert.That(equip, Is.InstanceOf<IElement>().And.Property("TagName").EqualTo("BUTTON"));
            Assert.That(equip.ClassList, Does.Contain("m-btn--sm"), "the tile action is the compact control");
            Assert.That(equip.TextContent, Does.Contain("Equip"));
        });
    }

    [Test]
    public void TheAffordableTileCarriesBuyWithItsPrice()
    {
        IRenderedComponent<CosmeticsPage> page = RenderWardrobe();

        IElement buy = FindIn(Tile(page, "frame.silver"), "[data-testid=\"cosmetic-buy\"]");
        Assert.Multiple(() =>
        {
            Assert.That(buy.TextContent, Does.Contain("Buy"), "the verb");
            Assert.That(buy.TextContent, Does.Contain("500"), "the value, in the one control");
            Assert.That(buy.QuerySelector(".m-icon"), Is.Not.Null, "the currency icon");
        });
    }

    /// <summary>The tile's Buy button opens the same confirmation dialog as the preview card's Buy button.</summary>
    [Test]
    public void BuyingFromATileAsksForConfirmation()
    {
        IRenderedComponent<CosmeticsPage> page = RenderWardrobe();

        FindIn(Tile(page, "frame.silver"), "[data-testid=\"cosmetic-buy\"]").Click();

        IElement confirm = page.Find("[data-testid=\"confirm\"]");
        Assert.Multiple(() =>
        {
            Assert.That(confirm.TextContent, Does.Contain("Silver Frame"));
            Assert.That(confirm.TextContent, Does.Contain("500"));
        });
    }

    /// <summary>Equipping from a tile with no session shows no refusal, because
    /// <see cref="CosmeticsPolicy.RefusalMessage"/> returns null when no session sent the action.</summary>
    [Test]
    public void EquippingFromATileWithoutASessionDrawsNoRefusal()
    {
        IRenderedComponent<CosmeticsPage> page = RenderWardrobe();

        FindIn(Tile(page, "frame.brass"), "[data-testid=\"cosmetic-equip\"]").Click();

        Assert.That(page.FindAll("[data-testid=\"cosmetic-refusal\"]"), Has.Count.EqualTo(0));
    }

    [Test]
    public void TheTilesWithoutAnActionCarryNoButton()
    {
        IRenderedComponent<CosmeticsPage> page = RenderWardrobe();

        foreach (string id in new[] { "frame.sapphire", "frame.emerald", "frame.champion", "frame.retired" })
        {
            IElement tile = Tile(page, id);
            Assert.That(tile.QuerySelectorAll(".m-btn"), Has.Length.EqualTo(0), $"{id} carries no action");
        }
    }

    // -------------------------------------------------------------------------------------------
    // Tile previews: every tile draws its own item, and no tile draws a generic slot icon.
    // -------------------------------------------------------------------------------------------

    /// <summary>A frame tile draws the player's avatar inside the tile's frame, using the same components as the
    /// preview card and the standings.</summary>
    [Test]
    public void FrameTilesDrawTheirOwnSilhouetteAroundThePlayersFace()
    {
        IRenderedComponent<CosmeticsPage> page = RenderWardrobe();

        IElement figure = FindIn(Tile(page, "frame.emerald"), ".m-cosmetic__figure");
        Assert.Multiple(() =>
        {
            Assert.That(figure.QuerySelector("svg[data-token=\"frame-emerald\"]"), Is.Not.Null, "the item's own silhouette");
            Assert.That(figure.QuerySelector("svg[data-token=\"avatar-crown\"]"), Is.Not.Null, "the player's face, not the slot's glyph");
            Assert.That(figure.QuerySelectorAll(".m-icon"), Has.Length.EqualTo(0), "no slot glyph on the grid");
        });
    }

    [Test]
    public void AvatarTilesDrawTheirOwnFace()
    {
        IRenderedComponent<CosmeticsPage> page = RenderSlot("Avatar");

        IElement figure = FindIn(Tile(page, "avatar.queen"), ".m-cosmetic__figure");
        Assert.Multiple(() =>
        {
            Assert.That(figure.QuerySelector("svg[data-token=\"avatar-heart\"]"), Is.Not.Null);
            Assert.That(figure.QuerySelectorAll(".m-icon"), Has.Length.EqualTo(0), "no slot glyph on the grid");
        });
    }

    /// <summary>A name effect tile draws the player's name with the tile's effect, not the equipped one, using the
    /// same PlayerName component as the standings.</summary>
    [Test]
    public void NameEffectTilesDrawTheNameInTheEffect()
    {
        IRenderedComponent<CosmeticsPage> page = RenderSlot("NameEffect");

        IElement figure = FindIn(Tile(page, "name.frost"), ".m-cosmetic__figure");
        Assert.Multiple(() =>
        {
            Assert.That(figure.QuerySelector(".m-name--name-frost"), Is.Not.Null, "the previewed effect, not the worn one");
            Assert.That(figure.TextContent, Is.EqualTo("Avery"), "the player's own name");
        });
    }

    /// <summary>The tile previews do not carry the <c>avatar</c> and <c>avatar-name</c> test ids. Only the preview
    /// card carries them, so a locator for those ids finds the card and not the tiles.</summary>
    [Test]
    public void TheTilePreviewsCarryNoAvatarTestIds()
    {
        IRenderedComponent<CosmeticsPage> page = RenderWardrobe();

        Assert.Multiple(() =>
        {
            Assert.That(page.FindAll("[data-testid=\"avatar\"]"), Has.Count.EqualTo(1), "the preview card's");
            Assert.That(page.FindAll("[data-testid=\"avatar-name\"]"), Has.Count.EqualTo(1), "the preview card's");
        });
    }

    /// <summary>The default tab has one state chip per tile, and each per-state test id appears exactly once.</summary>
    [Test]
    public void TheMigratedIdsAreUniquePerPage()
    {
        IRenderedComponent<CosmeticsPage> page = RenderWardrobe();

        Assert.Multiple(() =>
        {
            Assert.That(page.FindAll("[data-testid=\"cosmetic-state\"]"), Has.Count.EqualTo(6), "one chip per tile");
            foreach (string id in new[] { "cosmetic-equipped", "cosmetic-equip", "cosmetic-buy", "cosmetic-unaffordable", "cosmetic-locked", "cosmetic-unavailable" })
                Assert.That(page.FindAll($"[data-testid=\"{id}\"]"), Has.Count.EqualTo(1), id);
        });
    }

    /// <summary>The preview card of an unaffordable item shows the shortfall and a link to ways of earning the
    /// currency. They are drawn only when the fixture's wallet also cannot afford the item,
    /// which is true in the "fresh" scenario.</summary>
    [Test]
    public void TheUnaffordablePreviewKeepsTheShortfallAndTheEarnRoute()
    {
        IRenderedComponent<CosmeticsPage> page = RenderWardrobe("fresh");

        FindIn(Tile(page, "frame.emerald"), ".m-cosmetic__body").Click();

        Assert.Multiple(() =>
        {
            Assert.That(page.Find("[data-testid=\"cosmetic-shortfall\"]").TextContent, Does.Contain("150"));
            Assert.That(page.Find("[data-testid=\"cosmetic-earn\"]"), Is.Not.Null);
        });
    }
}
