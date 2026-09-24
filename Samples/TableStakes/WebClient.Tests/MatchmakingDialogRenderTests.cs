using AngleSharp.Dom;
using Bunit;
using Game.Logic;
using WebClient.Components;

namespace WebClient.Tests;

/// <summary>
/// The Cancel button of <see cref="MatchmakingDialog"/> for each matchmaking status. When the player is seated,
/// Cancel is disabled rather than removed, so the dialog keeps its layout until the table opens
/// (docs/matchmaking.md, "What the client sees").
/// </summary>
[TestFixture]
public class MatchmakingDialogRenderTests : Bunit.TestContext
{
    private NoSessionClientService Setup(MatchmakingStatus status)
    {
        NoSessionClientService client = Services.AddNoSessionClient(new NoSessionClientService());
        client.SetMatchmakingStatus(status);
        return client;
    }

    [Test]
    public void WhileSearchingCancelIsEnabled()
    {
        Setup(MatchmakingStatus.Searching);
        IRenderedComponent<MatchmakingDialog> dialog = RenderComponent<MatchmakingDialog>();

        IElement cancel = dialog.Find("[data-testid=\"cancel-search\"]");
        Assert.That(cancel.HasAttribute("disabled"), Is.False);
    }

    [Test]
    public void CommittingTheSeatDisablesCancelAndKeepsItOnScreen()
    {
        NoSessionClientService client = Setup(MatchmakingStatus.Searching);
        IRenderedComponent<MatchmakingDialog> dialog = RenderComponent<MatchmakingDialog>();

        client.SetMatchmakingStatus(MatchmakingStatus.SeatReserved);

        dialog.WaitForAssertion(() =>
        {
            Assert.That(dialog.FindAll("[data-testid=\"cancel-search\"]"), Has.Exactly(1).Items);
            Assert.That(dialog.Find("[data-testid=\"cancel-search\"]").HasAttribute("disabled"), Is.True);
        });
    }
}
