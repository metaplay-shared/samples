using System.Text.RegularExpressions;

namespace WebClient.Tests;

/// <summary>
/// The trump foil rules in app.css, read as text as <see cref="StandingsRowCssTests"/> does. The markup is covered by
/// <see cref="PlayingCardRenderTests"/>. These tests check that the foil animations change only properties the
/// browser can animate without repainting, and that the foil flash, the trump pulse and the label share the flip
/// delay.
/// </summary>
[TestFixture]
public class TrumpFoilCssTests
{
    /// <summary>
    /// The sheen and pulse keyframes animate only <c>transform</c> and <c>opacity</c>, which the browser can
    /// animate without repainting. The sheen loops while a trump is on the table, and the pulse restarts on every
    /// trump play.
    /// </summary>
    [Test]
    public void TheFoilLoopsMoveOnlyWhatTheCompositorCanMove()
    {
        string css = CssSource.AppCss();

        foreach (string keyframes in new[] { "ts-foil-sheen", "ts-trump-pulse" })
        {
            string block = CssSource.BlockFollowing(css, css.IndexOf($"@keyframes {keyframes}", StringComparison.Ordinal));

            Assert.That(block, Is.Not.Empty, $"the {keyframes} keyframes are gone from app.css");

            List<string> properties = new List<string>();
            foreach (Match match in Regex.Matches(block, @"([a-z-]+)\s*:"))
            {
                if (!properties.Contains(match.Groups[1].Value))
                    properties.Add(match.Groups[1].Value);
            }

            Assert.That(properties, Is.SubsetOf(new[] { "transform", "opacity" }),
                $"{keyframes} animates a property the compositor cannot move, so the loop repaints every frame it runs");
        }
    }

    /// <summary>
    /// A card that arrives face down flips for <c>--ts-flip</c>, and <c>.ts-slot--turning</c> sets
    /// <c>--ts-foil-delay</c> to that value so the foil flash starts after the flip. Table.razor repeats the flip
    /// duration in milliseconds (<c>FlipDurationMs</c>) for the pulse's inline delay and the label, so the test
    /// checks both values.
    /// <para>
    /// Under reduced motion the foil's static look stays, but the flash, the sheen and the pulse stop: the sheen is
    /// hidden and the pulse is held at its final opacity of zero.
    /// </para>
    /// </summary>
    [Test]
    public void TheFlashWaitsForTheFlip_AndReducedMotionKeepsTheStateWithoutTheMotion()
    {
        string css = CssSource.AppCss();

        string slot = CssSource.RuleBlock(css, ".ts-slot");
        Assert.That(slot, Does.Contain("--ts-flip: 0.44s"), "the flip's span no longer matches the delay Table.razor restates in milliseconds");

        string tableRazor = File.ReadAllText(CssSource.RepoPath("WebClient/Components/Pages/Table.razor"));
        Assert.That(tableRazor, Does.Contain("FlipDurationMs = 440"),
            "Table.razor's restatement of the flip's span no longer matches the stylesheet's --ts-flip");

        string turning = CssSource.RuleBlock(css, ".ts-slot--turning");
        Assert.That(turning, Does.Contain("--ts-foil-delay: var(--ts-flip)"),
            "a turning card's foil no longer waits for the flip, so the flash would run over its back");

        string flash = CssSource.RuleBlock(css, ".ts-slot .ts-card__foil");
        Assert.That(flash, Does.Contain("var(--ts-foil-delay"), "the flash does not take its delay from the custom property");

        List<string> blocks = CssSource.ReducedMotionBlocks(css);
        string reduced = string.Join("\n", blocks);
        Assert.That(blocks, Is.Not.Empty, "app.css carries no prefers-reduced-motion block at all");
        Assert.That(reduced, Does.Contain(".ts-card__sheen"), "the sheen is not stopped under reduced motion");
        Assert.That(reduced, Does.Contain(".ts-trumpmark__pulse"), "the boss's pulse is not stopped under reduced motion");
        Assert.That(reduced, Does.Contain(".ts-slot .ts-card__foil"), "the flash itself is not stopped under reduced motion");

        Assert.That(CssSource.RuleBlock(reduced, ".ts-card__sheen"), Does.Contain("display: none"),
            "reduced motion leaves the sheen as a static white streak across the foil instead of removing it");

        Assert.That(CssSource.RuleBlock(reduced, ".ts-trumpmark__pulse"), Does.Contain("animation: none").And.Contain("opacity: 0"),
            "reduced motion leaves the pulse's ring drawn at full strength instead of held at the transparency its run ends in");
    }
}
