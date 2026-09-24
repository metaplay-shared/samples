using Microsoft.Playwright;

namespace Game.Client.Tests;

/// <summary> The browser driver itself, on a tiny DOM contract with no application or game server. </summary>
[TestFixture]
public sealed class MatchDriverTests : MatchTestBase
{
    [Test]
    public async Task TargetedHealIsAimedWithoutAProseBanner()
    {
        await Page.SetContentAsync("""
            <button data-testid="hand-card" data-playable="true">Heal</button>
            <button data-testid="den-mine" data-targetable="false">Den</button>
            """);
        await Page.EvaluateAsync("""
            () => {
                const card = document.querySelector('[data-testid="hand-card"]');
                const den = document.querySelector('[data-testid="den-mine"]');
                card.onclick = () => {
                    card.dataset.playable = 'false';
                    den.dataset.targetable = 'true';
                    const selection = document.createElement('div');
                    selection.dataset.testid = 'targeting-layer';
                    selection.dataset.kind = 'Heal';
                    selection.textContent = 'Cancel';
                    document.body.append(selection);
                };
                den.onclick = () => {
                    den.dataset.aimed = 'true';
                    den.dataset.targetable = 'false';
                    document.querySelector('[data-testid="targeting-layer"]').remove();
                };
            }
            """);

        Assert.That(await TakeOneActionAsync(), Is.True);
        await Expect(Page.GetByTestId("den-mine")).ToHaveAttributeAsync("data-aimed", "true");
        await Expect(Page.GetByTestId("targeting-layer")).ToHaveCountAsync(0);
    }
}
