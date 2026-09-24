using Microsoft.Playwright.NUnit;

namespace Game.Client.Tests;

/// <summary> Exercises the shipped motion styles in Chromium without starting either application server. </summary>
[TestFixture]
public sealed class BoardMotionCssTests : PageTest
{
    [TestCase("den-beat-damage")]
    [TestCase("den-beat-heal")]
    public async Task DenAnimationKeepsItsShellCentred(string animationClass)
    {
        string cssPath = Path.GetFullPath(Path.Combine(
            TestContext.CurrentContext.TestDirectory, "../../../../Client/wwwroot/board.css"));
        await Page.SetContentAsync(
            "<div style='position:relative;width:1280px;height:720px;container-type:size'>"
            + "<div class='den'><div class='den-shell'></div></div></div>");
        await Page.AddStyleTagAsync(new() { Content = await File.ReadAllTextAsync(cssPath) });

        double displacement = await Page.Locator(".den-shell").EvaluateAsync<double>(
            """
            (shell, animationClass) => {
                const before = shell.getBoundingClientRect();
                shell.classList.add(animationClass);
                const animation = shell.getAnimations()[0];
                animation.pause();
                let error = 0;
                for (const progress of [0, .35, .45, .6, 1]) {
                    animation.currentTime = animation.effect.getTiming().duration * progress;
                    const current = shell.getBoundingClientRect();
                    error = Math.max(error,
                        Math.abs(current.x + current.width / 2 - before.x - before.width / 2),
                        Math.abs(current.y + current.height / 2 - before.y - before.height / 2));
                }
                return error;
            }
            """, animationClass);
        Assert.That(displacement, Is.LessThan(1), "damage and healing must scale around the Den's existing centre");
    }

    [TestCase("den-beat-damage")]
    [TestCase("den-beat-heal")]
    public async Task DenShellReplacementAndTargetHoverKeepTheSocketCentre(string animationClass)
    {
        string cssPath = Path.GetFullPath(Path.Combine(
            TestContext.CurrentContext.TestDirectory, "../../../../Client/wwwroot/board.css"));
        await Page.SetContentAsync(
            "<div style='position:relative;width:1280px;height:720px;container-type:size'>"
            + "<div class='den den-targetable'><div class='den-shell'></div></div></div>");
        await Page.AddStyleTagAsync(new() { Content = await File.ReadAllTextAsync(cssPath) });
        await Page.Locator(".den").HoverAsync();

        double error = await Page.Locator(".den").EvaluateAsync<double>(
            """
            async (den, animationClass) => {
                await Promise.all(den.getAnimations().map(animation => animation.finished));
                const centre = element => {
                    const rect = element.getBoundingClientRect();
                    return { x: rect.x + rect.width / 2, y: rect.y + rect.height / 2 };
                };
                const socketX = centre(den).x;
                let error = 0;
                for (const targetable of [false, true, false]) {
                    den.classList.toggle('den-targetable', targetable);
                    const shell = document.createElement('div');
                    shell.className = `den-shell ${animationClass}`;
                    den.replaceChildren(shell);
                    const animation = shell.getAnimations()[0];
                    animation.pause();
                    for (const progress of [0, .2, .35, .45, .6, .8, 1]) {
                        animation.currentTime = animation.effect.getTiming().duration * progress;
                        const root = centre(den), art = centre(shell);
                        error = Math.max(error, Math.abs(root.x - socketX),
                            Math.abs(art.x - root.x), Math.abs(art.y - root.y));
                    }
                }
                return error;
            }
            """, animationClass);
        Assert.That(error, Is.LessThan(1), "selection changes and keyed damage shells must retain their socket");
    }
}
