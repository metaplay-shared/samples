using Microsoft.Playwright;

namespace Game.Client.Tests;

/// <summary> Replays a real legal play through the live board renderer, including its final DOM handoff. </summary>
[TestFixture]
public sealed class BoardPlaySequenceTests : OfflineTestBase
{
    [Test]
    public async Task LegalPlayKeepsOneVisibleOwnerUntilTheSettledCardTakesOver()
    {
        await Page.GotoAsync($"{BaseUrl}/dev/board?scene=play&env=offline");
        await Expect(Page.GetByTestId("preview-play")).ToBeEnabledAsync(new() { Timeout = BootTimeout });
        int handBefore = await Page.GetByTestId("hand-card").CountAsync();
        int boardBefore = await Page.GetByTestId("board-critter").CountAsync();
        await Page.EvaluateAsync(
            """
            () => {
                window.playSamples = [];
                window.playInstance = null;
                window.playWatching = true;
                function sample() {
                    const moving = document.querySelector('[data-testid="board-motion-card"]');
                    if (moving) window.playInstance = moving.dataset.instance;
                    if (window.playInstance) {
                        const owners = [...document.querySelectorAll('[data-instance]')].filter(node =>
                            node.dataset.instance === window.playInstance &&
                            ['hand-card', 'board-critter', 'board-motion-card'].includes(node.dataset.testid));
                        const visible = owners.filter(node => getComputedStyle(node).visibility !== 'hidden'
                            && Number(getComputedStyle(node).opacity) > .01);
                        window.playSamples.push({ owners: visible.length,
                            beat: document.querySelector('[data-testid="match-board"]').dataset.beat,
                            flying: !!moving });
                    }
                    if (window.playWatching) requestAnimationFrame(sample);
                }
                requestAnimationFrame(sample);
            }
            """);

        await Page.GetByTestId("preview-play").ClickAsync();
        await Expect(Page.GetByTestId("board-motion-card")).ToBeAttachedAsync();
        await Expect(Page.GetByTestId("board-motion-card")).ToHaveCountAsync(0);
        await Expect(Page.GetByTestId("hand-card")).ToHaveCountAsync(handBefore - 1);
        await Expect(Page.GetByTestId("board-critter")).ToHaveCountAsync(boardBefore + 1);
        string[] failures = await Page.EvaluateAsync<string[]>(
            """
            () => {
                window.playWatching = false;
                const samples = window.playSamples;
                const failures = samples.filter(frame => frame.owners !== 1).map(frame => JSON.stringify(frame));
                if (!samples.some(frame => frame.flying)) failures.push('No flight frames recorded');
                if (samples.some(frame => frame.beat === 'resource')) failures.push('Mana consumed a separate idle beat');
                return failures;
            }
            """);
        Assert.That(failures, Is.Empty, string.Join("; ", failures));
    }
}
