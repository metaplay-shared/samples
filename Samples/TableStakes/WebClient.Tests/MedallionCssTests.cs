using System.Text.RegularExpressions;

namespace WebClient.Tests;

/// <summary>
/// Tests for the <c>.medallion</c> style in app.css (docs/meta-shell.md, "Styles and design tokens"). The
/// medallion (disc, ring stack and light) is drawn only by <c>.medallion</c>, and the components in
/// <see cref="Wearers"/> carry the class. A component rule that declares its own <c>background</c> or
/// <c>box-shadow</c> overrides the shared drawing, and no browser reports it. These tests catch that by reading
/// the stylesheet and component sources as text, with each rule read as the brace-balanced block at its exact
/// selector.
/// </summary>
[TestFixture]
public class MedallionCssTests
{
    /// <summary>The shared medallion class that every component in <see cref="Wearers"/> carries.</summary>
    private const string RecipeClass = "medallion";

    /// <summary>The components that carry the medallion class, as (selector, component source, class string).</summary>
    private static readonly (string Selector, string SourcePath, string ClassEmitted)[] Wearers =
    {
        (".m-emblem",                "WebClient/Components/Meta/MetaEmblem.razor",     "medallion m-emblem"),
        (".m-avatar",                "WebClient/Components/Meta/PlayerAvatar.razor",   "medallion m-avatar"),
        (".m-profile__medallion",    "WebClient/Components/Pages/Meta/ProfilePage.razor", "medallion m-profile__medallion"),
    };

    /// <summary>
    /// Selectors that only <i>tune</i> an element that already carries the medallion class. They must not
    /// declare paint either.
    /// </summary>
    private static readonly string[] TuningSelectors = { ".ts-plaque__avatar" };

    /// <summary>The seat plaque component, whose portrait is a <c>PlayerAvatar</c>.</summary>
    private const string PlaqueSource = "WebClient/Components/TableUI/SeatPlaque.razor";

    /// <summary>
    /// Each component emits the medallion class next to its own class, and the seat plaque draws its portrait with
    /// <c>PlayerAvatar</c>.
    /// </summary>
    [Test]
    public void EveryWearerCarriesTheMedallion()
    {
        foreach ((string selector, string sourcePath, string classEmitted) in Wearers)
        {
            string source = File.ReadAllText(CssSource.RepoPath(sourcePath));

            Assert.That(Regex.IsMatch(source, Regex.Escape(classEmitted)), Is.True,
                $"{sourcePath} no longer carries '{classEmitted}', so its medallion draws from the wearer's own " +
                "rule instead of the one recipe");
        }

        // If the seat plaque drew its own disc, the portrait at the table would not match the cosmetics the player
        // bought (docs/cosmetics.md).
        string plaque = File.ReadAllText(CssSource.RepoPath(PlaqueSource));
        Assert.That(plaque, Does.Contain("<PlayerAvatar"),
            $"{PlaqueSource} no longer draws its portrait with the shell's avatar");
        Assert.That(plaque, Does.Contain("Class=\"ts-plaque__avatar\""),
            $"{PlaqueSource} no longer tunes the avatar's medallion with the table's own class");
    }

    /// <summary>
    /// <c>.medallion</c> declares the disc gradient, the ring stack and the light, with custom properties that
    /// components can tune. The light is behind the component's content. No component or tuning rule declares
    /// <c>background</c> or <c>box-shadow</c>.
    /// </summary>
    [Test]
    public void OnlyTheMedallionRuleSetsTheDiscBackgroundAndShadow()
    {
        string appCss = CssSource.AppCss();

        string recipe = CssSource.RuleBlock(appCss, "." + RecipeClass);
        Assert.That(recipe, Is.Not.Empty, "app.css has no .medallion rule — the recipe is gone");

        Assert.That(recipe, Does.Contain("var(--med-hi"), "the disc's lit stop is not a tunable slot");
        Assert.That(recipe, Does.Contain("var(--med-lo"), "the disc's shadowed stop is not a tunable slot");
        foreach (string slot in new[] { "--med-ring", "--med-counter", "--med-struck", "--med-shade", "--med-halo" })
            Assert.That(recipe, Does.Contain($"var({slot}"), $"the ring stack's {slot} slot is gone");

        string light = CssSource.RuleBlock(appCss, "." + RecipeClass + "::after");
        Assert.That(light, Is.Not.Empty, "the medallion's table light (::after) is gone from app.css");
        Assert.That(light, Does.Contain("z-index: -1"), "the light no longer sits under the wearer's content");

        string metaCss = CssSource.MetaCss();

        List<string> tuned = new List<string>();
        foreach ((string selector, string _, string _) in Wearers)
            tuned.Add(selector);
        tuned.AddRange(TuningSelectors);

        foreach (string selector in tuned)
        {
            List<string> blocks = CssSource.RuleBlocks(appCss, selector);
            blocks.AddRange(CssSource.RuleBlocks(metaCss, selector));

            Assert.That(blocks, Is.Not.Empty, $"no stylesheet carries a {selector} rule any more");

            foreach (string block in blocks)
            {
                Assert.That(block, Does.Not.Match(@"(^|[;{])\s*background\s*:"),
                    $"{selector} re-declares the disc, which is the recipe's to draw");
                Assert.That(block, Does.Not.Match(@"(^|[;{])\s*box-shadow\s*:"),
                    $"{selector} re-declares the ring stack, which is the recipe's to draw");
            }
        }
    }

    /// <summary>
    /// The default disc uses the green surface tokens and the default ring uses the gilt token. The plum colour
    /// literals checked below must not appear in either stylesheet.
    /// </summary>
    [Test]
    public void TheDefaultsAreGreenDiscAndGiltRing()
    {
        string recipe = CssSource.RuleBlock(CssSource.AppCss(), "." + RecipeClass);

        Assert.That(recipe, Does.Contain("var(--med-hi, var(--surface-lift))"),
            "the disc's default lit stop is not the green family's raised surface");
        Assert.That(recipe, Does.Contain("var(--med-lo, var(--surface-deep))"),
            "the disc's default shadowed stop is not the green family's deep surface");
        Assert.That(recipe, Does.Contain("0 0 0 2px var(--gilt)"),
            "the default ring is not the gilt band the plaque wore");

        string css = CssSource.AppCss() + CssSource.MetaCss();
        Assert.That(css, Does.Not.Contain("#3b2a63"), "the emblem's plum lit stop is still in a stylesheet");
        Assert.That(css, Does.Not.Contain("#150e28"), "the emblem's plum shadowed stop is still in a stylesheet");
    }

    /// <summary>
    /// The avatar glyph colour is the <c>--champagne</c> token, used directly. meta-shell.css declares no
    /// <c>--m-champagne</c> alias.
    /// </summary>
    [Test]
    public void TheFaceInkIsTheChampagneToken()
    {
        string metaCss = CssSource.MetaCss();

        Assert.That(metaCss, Does.Not.Contain("--m-champagne"), "the --m-champagne alias is still declared");

        string face = CssSource.RuleBlock(metaCss, ".m-avatar__face");
        Assert.That(face, Does.Contain("color: var(--champagne)"),
            ".m-avatar__face is not inked with the champagne token");
    }
}
