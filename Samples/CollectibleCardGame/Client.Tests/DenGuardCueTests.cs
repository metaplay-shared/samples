using Microsoft.Playwright;

namespace Game.Client.Tests;

/// <summary> A Den's guard cue agrees with the targetable Guards visible on its battlefield. </summary>
[TestFixture]
public sealed class DenGuardCueTests : OfflineTestBase
{
    [TestCase("guard-sneaky")]
    [TestCase("midturn")]
    public async Task OnlyVisibleGuardsCloseTheDen(string scene)
    {
        await Page.GotoAsync($"{BaseUrl}/dev/board?scene={scene}&env=offline&motion=reduced");
        await Expect(Page.GetByTestId("match-board")).ToBeVisibleAsync(new() { Timeout = BootTimeout });
        int sneakyGuards = 0;
        foreach (string seat in new[] { "mine", "enemy" })
        {
            ILocator row = Page.GetByTestId($"battlefield-{seat}");
            int gating = await row.Locator(".card-face.is-guard[data-sneaky='false']").CountAsync();
            sneakyGuards += await row.Locator(".card-face.is-guard[data-sneaky='true']").CountAsync();
            await Expect(row.GetByTestId("den-guarded")).ToHaveCountAsync(gating > 0 ? 1 : 0);
        }
        if (scene == "guard-sneaky")
            Assert.That(sneakyGuards, Is.GreaterThan(0), "the scene must exercise a Smoke-Bombed Guard which cannot gate the Den");
    }
}
