using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace WebClient.Tests;

/// <summary>
/// The <c>.strip</c> style (docs/meta-shell.md, "Styles and design tokens"): the dark brass label background is
/// defined once, by the <c>.strip</c> rule in app.css, and every element that uses it takes its background from that
/// rule. Those elements are the table's nameplate, turn banner and dark button, and the shell's chips and tags.
/// The tests fail if another rule writes the gradient itself or sets its own background.
/// <para>
/// Like <see cref="ColorContrastTests"/>, the tests read the stylesheets from the app's wwwroot, so they check the files
/// the app serves.
/// </para>
/// </summary>
[TestFixture]
public class StripCssTests
{
    /// <summary>
    /// The <c>.strip</c> rule uses the tokens for the gradient background, the brass edge ring and the top highlight.
    /// </summary>
    [Test]
    public void TheStripRuleTakesItsColoursFromTheTokens()
    {
        string block = RuleBlock(CssSource.AppCss(), @"\.strip");

        Assert.That(block, Does.Contain("background: var(--grad-strip);"),
            "the strip rule pours the shared gradient, not a pour of its own.");
        Assert.That(block, Does.Contain("var(--shadow-brass-ring)"),
            "the strip rule rings itself with the shared brass ring token.");
        Assert.That(block, Does.Contain("var(--shadow-brass-lit)"),
            "the strip rule carries the shared struck-top token.");
    }

    /// <summary>
    /// The strip gradient's color stops appear only in the <c>--grad-strip</c> declaration in app.css. A second copy
    /// of the stops could drift to different colors. The test also checks that an old, slightly different top stop
    /// color is absent.
    /// </summary>
    [Test]
    public void TheGradientStripIsDeclaredOnce()
    {
        List<string> pourSites = new();
        foreach ((string name, string css) in CssSource.Stylesheets())
        {
            foreach (Match site in Regex.Matches(css, @"linear-gradient\(180deg,\s*#141310[^;]*;"))
                pourSites.Add($"{name}: {site.Value}");
        }

        Assert.That(pourSites, Has.Count.EqualTo(1),
            "the strip gradient's stops are written once, in the --grad-strip token declaration; found: " +
            string.Join(" | ", pourSites));
        Assert.That(CssSource.AppCss(), Does.Contain("--grad-strip:      linear-gradient(180deg, #141310, #0a0a08);"),
            "the one pour site is the token declaration itself.");

        Assert.That(CssSource.AppCss(), Does.Not.Contain("#171511"),
            "the turn banner's drifted top stop is gone; the banner pours from the one token like every strip.");
    }

    /// <summary>
    /// Each element that uses <c>.strip</c> sets its own layout, radius and text styles but no background. An accent
    /// color goes on the ring and the label, never as a background fill. Each chip variant has its own test case,
    /// because a variant is a separate rule block that the base <c>.m-chip</c> case does not read.
    /// </summary>
    [TestCase("app.css", @"\.ts-plaque__nameplate")]
    [TestCase("app.css", @"\.ts-turn")]
    [TestCase("app.css", @"\.ts-btn--dark")]
    [TestCase("meta-shell.css", @"\.m-chip")]
    [TestCase("meta-shell.css", @"\.m-chip--claim")]
    [TestCase("meta-shell.css", @"\.m-chip--live")]
    [TestCase("meta-shell.css", @"\.m-chip--gold")]
    [TestCase("meta-shell.css", @"\.m-tag")]
    public void AStripUserSetsItsLayoutButNoBackground(string stylesheet, string selectorPattern)
    {
        string block = RuleBlock(Stylesheet(stylesheet), selectorPattern);

        Assert.That(block, Does.Not.Match(@"background\s*:"),
            $"{selectorPattern} in {stylesheet} leaves the pour to the strip recipe; a fill here is a second drawing.");
    }

    /// <summary>
    /// The brace-balanced block of the first rule whose selector starts a line and matches
    /// <paramref name="selectorPattern"/> (a regex). The selector must start its line, so a descendant rule such as
    /// <c>.m-card__foot .m-chip</c> is not taken for the <c>.m-chip</c> rule.
    /// </summary>
    private static string RuleBlock(string css, string selectorPattern)
    {
        Match match = Regex.Match(css, @"(?:^|\n)\s*" + selectorPattern + @"\s*\{");
        Assert.That(match.Success, Is.True,
            $"a rule matching {selectorPattern} was expected at the top level of the stylesheet.");
        return CssSource.BalancedBlock(css, match.Index + match.Length - 1);
    }

    private static string Stylesheet(string fileName) => fileName switch
    {
        "app.css"        => CssSource.AppCss(),
        "meta-shell.css" => CssSource.MetaCss(),
        _                => throw new ArgumentException($"{fileName} is not one of the stylesheets.", nameof(fileName)),
    };
}
