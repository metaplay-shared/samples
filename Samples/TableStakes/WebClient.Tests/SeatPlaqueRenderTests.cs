using AngleSharp.Dom;
using Bunit;
using Game.Logic;
using WebClient.Components.TableUI;
using WebClient.Meta;

namespace WebClient.Tests;

/// <summary>
/// What <see cref="SeatPlaque"/> draws for the seated player (<c>docs/cosmetics.md</c>, "At the table"): the
/// avatar in its frame, the name with its effect, and the defaults when no cosmetics are equipped.
/// <para>
/// The plaque receives an <see cref="IdentityView"/> whose catalogue ids are already mapped to style tokens by
/// <see cref="CosmeticsPolicy.ViewOf"/>, which has its own tests. These tests check that the tokens become the
/// shell's own CSS classes. If the table used its own classes instead, cosmetics at the table could look different
/// from the shop without any test failing.
/// </para>
/// </summary>
[TestFixture]
public class SeatPlaqueRenderTests : Bunit.TestContext
{
    private static MatchSeat Seat(int index = 1) =>
        new MatchSeat(index, PlayerPublicIdentity.ForSeat(MatchTestSeatPlayer, "Avery"), MatchSeatOccupancy.Human, hasArrived: true);

    private static readonly Metaplay.Core.EntityId MatchTestSeatPlayer =
        Metaplay.Core.EntityId.Create(Metaplay.Core.EntityKindCore.Player, 4242UL);

    private static IdentityView Wearing(string avatar, string frame, string effect) =>
        new IdentityView("Avery", avatar, frame, effect, Level: 0, CompetitionName: "", CompetitionRank: 0);

    private IRenderedComponent<SeatPlaque> Render(IdentityView identity, int seat = 1) =>
        RenderComponent<SeatPlaque>(parameters => parameters
            .Add(p => p.Seat, Seat(seat))
            .Add(p => p.Identity, identity)
            .Add(p => p.Position, TableScreenPosition.West));

    /// <summary>
    /// The avatar, frame and name effect become the shell's classes <c>m-avatar</c>, <c>m-frame--*</c> and
    /// <c>m-name--*</c>, the same classes the standings rows use.
    /// </summary>
    [Test]
    public void APlaqueWearsTheAvatarTheFrameAndTheNameEffect()
    {
        IRenderedComponent<SeatPlaque> cut = Render(Wearing("avatar-crown", "frame-sapphire", "name-prism"));

        IElement avatar = cut.Find("[data-testid=seat-1-avatar]");
        Assert.That(avatar.ClassList, Does.Contain("m-avatar"), "the portrait is not the shell's own avatar");
        Assert.That(avatar.ClassList, Does.Contain("medallion"), "the portrait left the one medallion drawing");
        Assert.That(avatar.ClassList, Does.Contain("m-frame--frame-sapphire"));
        Assert.That(avatar.QuerySelector("svg[data-token=avatar-crown]"), Is.Not.Null, "the worn face was not drawn");

        IElement name = cut.Find("[data-testid=seat-1-name]");
        Assert.That(name.ClassList, Does.Contain("m-name--name-prism"));
        Assert.That(name.TextContent, Is.EqualTo("Avery"));
    }

    /// <summary>
    /// With no cosmetics equipped, the avatar draws its default face on a plain ring and the name is plain text.
    /// The plaque must not emit a modifier class with an empty token, such as <c>m-frame--</c>.
    /// </summary>
    [Test]
    public void APlaqueWearingNothingDrawsTheDefaultsAndNoEmptyModifier()
    {
        IRenderedComponent<SeatPlaque> cut = Render(Wearing("", "", ""));

        IElement avatar = cut.Find("[data-testid=seat-1-avatar]");
        Assert.That(avatar.ClassList, Does.Not.Contain("m-frame--"));
        foreach (string cls in avatar.ClassList)
            Assert.That(cls, Does.Not.EndWith("--"), $"the plaque emitted a dangling modifier class '{cls}'");

        Assert.That(avatar.QuerySelector("svg[data-token='']"), Is.Not.Null, "an empty slot did not reach the avatar's default arm");

        IElement name = cut.Find("[data-testid=seat-1-name]");
        foreach (string cls in name.ClassList)
            Assert.That(cls, Does.Not.EndWith("--"), $"the plaque emitted a dangling modifier class '{cls}'");
    }

    /// <summary>
    /// The name is rendered as text, never as markup, even with a name effect applied. The effect is only a CSS
    /// class (<c>docs/cosmetics.md</c>, "Styles").
    /// </summary>
    [Test]
    public void ANameIsDrawnAsTextEvenUnderAnEffect()
    {
        IdentityView shouting = Wearing("", "", "name-prism") with { DisplayName = "<b>Avery</b>" };

        IElement name = Render(shouting).Find("[data-testid=seat-1-name]");

        Assert.That(name.TextContent, Is.EqualTo("<b>Avery</b>"));
        Assert.That(name.QuerySelector("b"), Is.Null, "the name reached the plaque as markup");
        Assert.That(name.GetAttribute("data-text"), Is.EqualTo("<b>Avery</b>"),
            "the animated tier's second layer reads the name off data-text, so it has to carry the same text");
    }

    /// <summary>
    /// The full name is kept in the <c>title</c> attribute as a tooltip, because a long name is truncated with an
    /// ellipsis on the side seats.
    /// </summary>
    [Test]
    public void ATruncatedNameKeepsItsWholeSelfInTheTooltip()
    {
        IdentityView overlong = Wearing("", "", "") with { DisplayName = "Bartholomew Fitzwilliam III" };

        IElement name = Render(overlong).Find("[data-testid=seat-1-name]");

        Assert.That(name.GetAttribute("title"), Is.EqualTo("Bartholomew Fitzwilliam III"));
    }
}
