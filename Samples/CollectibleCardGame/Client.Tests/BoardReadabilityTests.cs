using Microsoft.Playwright;

namespace Game.Client.Tests;

[TestFixture]
public sealed class BoardReadabilityTests : OfflineTestBase
{
    [TestCase(1280, 720)]
    [TestCase(640, 360)]
    public async Task CardDetailsAreReadableAndStayInsideTheBoard(int width, int height)
    {
        await Page.SetViewportSizeAsync(width, height);
        await Page.GotoAsync($"{BaseUrl}/dev/board?scene=inspect&env=offline");
        // The catalogue-derived scene can deal a plain critter first; inspect one with a keyword.
        ILocator card = Page.Locator("[data-testid=board-critter]:has(.card-tooltip-keyword)").First;
        await Expect(card).ToBeVisibleAsync(new() { Timeout = BootTimeout });
        await card.HoverAsync();
        ILocator tooltip = card.GetByTestId("card-tooltip");
        await Expect(tooltip).ToBeVisibleAsync();
        await Expect(tooltip.Locator(".card-tooltip-title")).ToHaveCSSAsync("font-size", "19px");
        await Expect(tooltip.Locator(".card-tooltip-keyword small").First).ToHaveCSSAsync("font-size", "14px");
        await Page.WaitForFunctionAsync("""
            () => {
                const tip = [...document.querySelectorAll('.card-tooltip')].find(node => getComputedStyle(node).visibility === 'visible');
                if (!tip) return false;
                const a = tip.getBoundingClientRect(), b = document.querySelector('[data-testid="match-board"]').getBoundingClientRect();
                return a.left >= b.left && a.right <= b.right && a.top >= b.top && a.bottom <= b.bottom;
            }
            """);
        await card.EvaluateAsync("node => node.classList.add('critter-beat-attacker')");
        await Expect(tooltip).ToBeHiddenAsync();
    }

    /// <summary>
    /// Both Dens' hit-point lines, measured on the real board in the font the app really draws them in —
    /// Inter, which arrives as a web font, and which the stylesheet fixtures therefore cannot measure. Three
    /// digits over three digits is the widest the stat domain produces and the line never wraps, so a line
    /// wider than the Den's panel spills over the door instead of shrinking. Asserted with 8 % of the panel
    /// in hand, at the board's own size and at the laptop window the 2026-09-09 playtest was played on.
    /// </summary>
    [TestCase(1280, 720)]
    [TestCase(1440, 900)]
    public async Task DenHitPointsFitBothDensPanels(int width, int height)
    {
        await Page.SetViewportSizeAsync(width, height);
        await Page.GotoAsync($"{BaseUrl}/dev/board?scene=mulligan&env=offline&motion=reduced");
        await Expect(Page.GetByTestId("den-mine")).ToBeVisibleAsync(new() { Timeout = BootTimeout });
        await Expect(Page.GetByTestId("den-hp").First).ToHaveTextAsync("125");

        // The web font decides the width, so this fixture is worth nothing until the face the number is drawn in
        // has actually loaded. `document.fonts.check` cannot say so — it answers "can this be drawn without
        // waiting", which is *true* when no matching face exists at all, because then the fallback is drawn
        // immediately, and that is how this fixture used to measure a substitute in a few milliseconds and pass.
        // So the condition is the face's own status, polled rather than waited on, because running out has to
        // read as this fixture's own sentence rather than as a bare timeout from the harness.
        bool loaded = false;
        for (int attempt = 0; attempt < 60 && !loaded; attempt++)
        {
            loaded = await Page.EvaluateAsync<bool>(
                """
                () => [...document.fonts].some(face =>
                    face.family === 'Inter' && face.weight === '800' && face.status === 'loaded')
                """);
            if (!loaded)
                await Page.WaitForTimeoutAsync(250);
        }

        if (!loaded)
        {
            string[] faces = await Page.EvaluateAsync<string[]>(
                "() => [...document.fonts].map(face => `${face.family} ${face.weight} ${face.status}`)");
            Assert.Fail("the board's Inter 800 face never loaded, so this fixture would have measured whatever "
                + "substitute the platform supplies instead of the font the app ships — "
                + (faces.Length == 0
                    ? "no @font-face is declared at all (is wwwroot/fonts.css still linked from index.html?)"
                    : $"the faces the page declares are: {string.Join(", ", faces.Distinct())}"));
        }

        // And that the line really is drawn in it: the family is declared on `*` in ClientBase's App.razor, and a
        // loaded face nothing uses would satisfy the poll above on its own.
        await Expect(Page.Locator(".den-hp b").First).ToHaveCSSAsync("font-family", "Inter, system-ui, sans-serif");

        string[] tight = await Page.EvaluateAsync<string[]>(
            """
            () => [...document.querySelectorAll('.den')].flatMap(den => {
                const panel = den.querySelector('.den-panel').getBoundingClientRect();
                const hp = den.querySelector('.den-hp');
                const line = hp.getBoundingClientRect();
                const spare = panel.width - line.width;
                const icon = hp.querySelector('img').getBoundingClientRect().width;
                if (spare >= panel.width * 0.08 && hp.scrollWidth <= hp.clientWidth && icon > 1) return [];
                return [`${den.dataset.testid}: the ${hp.textContent.replace(/\s+/g, ' ').trim()} line is `
                    + `${line.width.toFixed(1)}px with a ${icon.toFixed(1)}px icon in a `
                    + `${panel.width.toFixed(1)}px panel, ${spare.toFixed(1)}px spare`];
            })
            """);

        Assert.That(tight, Is.Empty, string.Join("; ", tight));
    }
}
