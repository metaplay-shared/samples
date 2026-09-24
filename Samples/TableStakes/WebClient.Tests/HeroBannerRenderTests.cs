using AngleSharp.Dom;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using WebClient.Components.Meta;
using WebClientBase.Configuration;
using Game.Logic;

namespace WebClient.Tests;

/// <summary>
/// bUnit render tests for Home's <see cref="HeroBanner"/>: the game's title and logo, the play button and the help
/// button. The parameters stand in for session state, covering the connecting, ready and matchmaking-unavailable
/// states.
/// </summary>
[TestFixture]
public class HeroBannerRenderTests : BunitPageTest
{
    /// <summary>Before a session exists, the play button is disabled and says it is connecting.</summary>
    [Test]
    public void BeforeTheSessionArrivesTheKeyIsDisabledAndSaysWhatIsHappening()
    {
        IRenderedComponent<HeroBanner> hero = RenderHero(hasSession: false);

        IElement key = hero.Find("[data-testid=\"play\"]");
        Assert.Multiple(() =>
        {
            Assert.That(key.GetAttribute("aria-disabled"), Is.EqualTo("true"));
            Assert.That(key.GetAttribute("disabled"), Is.Not.Null);
            Assert.That(key.TextContent, Does.Contain("Connecting"),
                "the caption is the whole of what the connection state used to draw for the lobby");
        });
    }

    /// <summary>When ready, the play button is enabled, and the banner has exactly one help button.</summary>
    [Test]
    public void TheReadyHeroInvitesTheGameAndCarriesItsTwoControls()
    {
        IRenderedComponent<HeroBanner> hero = RenderHero(hasSession: true);

        Assert.Multiple(() =>
        {
            Assert.That(hero.Find("[data-testid=\"play\"]").GetAttribute("aria-disabled"), Is.Null);
            Assert.That(hero.Find("[data-testid=\"play\"]").TextContent, Does.Contain("Jump into a game"));
            Assert.That(hero.FindAll("[data-testid=\"help\"]"), Has.Exactly(1).Items);
            Assert.That(hero.Find("[data-testid=\"help\"]").GetAttribute("aria-label"), Is.EqualTo("How to play"));
        });
    }

    /// <summary>The wordmark and logo come from <see cref="WebClientConfig"/>.</summary>
    [Test]
    public void TheWordmarkAndTheCardAreTheGamesIdentity()
    {
        IRenderedComponent<HeroBanner> hero = RenderHero(hasSession: true);

        WebClientConfig config = Services.GetRequiredService<WebClientConfig>();

        Assert.Multiple(() =>
        {
            Assert.That(hero.Find(".m-hero__wordmark").TextContent, Is.EqualTo(config.AppTitle.ToUpperInvariant()));
            Assert.That(hero.Find(".m-hero__mark").TextContent, Is.EqualTo(config.LogoEmoji));
        });
    }

    /// <summary>When matchmaking is unavailable, the banner shows the unavailable message exactly once.</summary>
    [Test]
    public void ARefusedSearchIsSaidUnderTheKey()
    {
        IRenderedComponent<HeroBanner> hero = RenderHero(hasSession: true, MatchmakingStatus.Unavailable);

        Assert.That(hero.FindAll("[data-testid=\"matchmaking-unavailable\"]"), Has.Exactly(1).Items);
    }

    /// <summary>The help button opens the rules summary, and the summary's close button closes it.</summary>
    [Test]
    public void TheHelpControlOpensTheRulesSummaryAndClosesIt()
    {
        IRenderedComponent<HeroBanner> hero = RenderHero(hasSession: true);

        hero.Find("[data-testid=\"help\"]").Click();

        IElement summary = hero.Find("[data-testid=\"rules-summary\"]");
        Assert.That(summary.TextContent, Does.Contain("You have five cards."));

        hero.Find("[data-testid=\"help-close\"]").Click();

        Assert.That(hero.FindAll("[data-testid=\"rules-summary\"]"), Is.Empty);
    }

    private IRenderedComponent<HeroBanner> RenderHero(bool hasSession, MatchmakingStatus status = MatchmakingStatus.NotSearching)
    {
        Setup("/", "default");
        return RenderComponent<HeroBanner>(parameters => parameters
            .Add(p => p.HasSession, hasSession)
            .Add(p => p.Status, status));
    }
}
