using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using WebClient.Components.Pages;
using WebClient.Components.Shell;

namespace WebClient.Tests;

/// <summary>
/// bUnit render tests for Home, the wallet HUD and the primary navigation, drawn from the fixture data.
/// <para>
/// Tests that need a real browser stay in <c>ShellPageTests</c> (Playwright): CSS, layout and viewport checks,
/// focus and tab order, drag-scroll gestures, refresh and restore, and the router that turns a route into a
/// screen. A tap and the URL it navigates to can be checked with bUnit, so those tests belong in render tests
/// such as this one, <c>CosmeticsRenderTests</c> and <c>EventsHubRenderTests</c>. Which content the Next-up card
/// shows is tested in <see cref="NextActionPolicyTests"/>.
/// </para>
/// </summary>
[TestFixture]
public class ShellRenderTests : BunitPageTest
{
    /// <summary>
    /// Home draws the hero banner and exactly one Next-up card with one action, so the screen has a single
    /// suggested next step (docs/meta-shell.md, "MetaSnapshot").
    /// <para>
    /// This test does not check that no popup opens over Home, because no component in Home's tree can open a
    /// sheet, a confirmation or a reward reveal. <c>ShellPageTests</c> checks that against the full layout with a
    /// session.
    /// </para>
    /// </summary>
    [Test]
    public void HomeShowsExactlyOneNextUpCard()
    {
        Setup("/", "default");
        IRenderedComponent<Home> page = RenderComponent<Home>();

        Assert.That(page.FindAll("[data-testid=\"play-hero\"]"), Has.Count.EqualTo(1));
        Assert.That(page.FindAll("[data-testid=\"next-up\"]"), Has.Count.EqualTo(1));
        Assert.That(page.Find("[data-testid=\"next-up-headline\"]").TextContent, Is.Not.Empty);
        Assert.That(page.FindAll("[data-testid=\"next-up-action\"]"), Has.Count.EqualTo(1));

        // The hero banner has exactly one PLAY button and one help button.
        Assert.That(page.FindAll("[data-testid=\"play\"]"), Has.Count.EqualTo(1));
        Assert.That(page.FindAll("[data-testid=\"help\"]"), Has.Count.EqualTo(1));

        // The card itself is not a control: its root is a div with no button role, and it contains exactly one
        // button, the action. Text inside a <button> cannot be selected and copied.
        IElement card = page.Find("[data-testid=\"next-up\"]");
        Assert.That(card.TagName, Is.EqualTo("DIV"), "the next-up card must not be a control");
        Assert.That(card.GetAttribute("role"), Is.Null,
            "a div[role=button] root would be a control just as much as a <button> root");
        Assert.That(card.QuerySelectorAll("button"), Has.Exactly(1).Items);
    }

    /// <summary>
    /// The HUD shows only the wallet balances and no avatar. The player's identity is on Home's identity row
    /// (docs/meta-shell.md, "Layouts"). <c>ShellPageTests</c> checks the row's height at phone widths.
    /// </summary>
    [Test]
    public void TheHudIsTheThreeBalancesAndCarriesNoIdentity()
    {
        Setup("/", "default");
        IRenderedComponent<WalletHud> hud = RenderComponent<WalletHud>();

        Assert.That(hud.FindAll("[data-testid=\"hud-wallet\"]"), Has.Count.EqualTo(1));
        foreach (string balance in new[] { "balance-coins", "balance-gems", "balance-spintokens" })
            Assert.That(hud.FindAll($"[data-testid=\"{balance}\"]"), Has.Count.EqualTo(1), balance);

        Assert.That(hud.FindAll("[data-testid=\"hud-avatar\"]"), Is.Empty,
            "the identity moved to Home's identity row; a second one on the bar would be two of them");
    }

    /// <summary>
    /// Home's identity row shows the avatar and one control that navigates to <c>/profile</c>. This row is how the
    /// player reaches Profile, which has no navigation tab. <c>ShellPageTests</c> checks that the router then draws
    /// the Profile screen.
    /// </summary>
    [Test]
    public void HomeCarriesTheIdentityRowAndItsRouteToProfile()
    {
        Setup("/", "default");
        IRenderedComponent<Home> page = RenderComponent<Home>();

        IElement row = page.Find("[data-testid=\"home-profile\"]");
        Assert.That(row.QuerySelectorAll("[data-testid=\"avatar\"]"), Has.Exactly(1).Items);
        Assert.That(row.QuerySelectorAll("[data-testid=\"home-profile-action\"]"), Has.Exactly(1).Items);

        page.Find("[data-testid=\"home-profile-action\"]").Click();

        NavigationManager nav = Services.GetRequiredService<NavigationManager>();
        Assert.That(nav.Uri, Is.EqualTo("http://localhost/profile"));
    }

    /// <summary>
    /// Every navigation item shows a text label next to its icon, because an icon alone is not an accessible
    /// label. There is no Profile tab. Play is an action with no route, so it never gets <c>aria-current</c>.
    /// </summary>
    [Test]
    public void TheNavigationHasFiveLabelledDestinationsAndNoProfileTab()
    {
        Setup("/", "default");
        IRenderedComponent<PrimaryNav> nav = RenderComponent<PrimaryNav>();

        IElement bar = nav.Find("[data-testid=\"primary-nav\"]");
        Assert.That(bar.QuerySelectorAll("[data-testid^=\"nav-\"]"), Has.Exactly(5).Items);

        foreach (string destination in new[] { "home", "events", "play", "compete", "shop" })
        {
            IElement item = nav.Find($"[data-testid=\"nav-{destination}\"]");
            Assert.That(item.TextContent, Does.Contain(destination).IgnoreCase,
                $"nav-{destination} does not carry its own word");
        }

        Assert.That(nav.Find("[data-testid=\"nav-play\"]").GetAttribute("aria-current"), Is.Null,
            "Play is an action, not a destination; nothing can select it");
        Assert.That(nav.FindAll("[data-testid=\"nav-profile\"]"), Is.Empty);
    }
}
