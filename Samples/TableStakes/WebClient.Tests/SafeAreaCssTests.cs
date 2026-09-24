using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace WebClient.Tests;

/// <summary>
/// The bottom safe area is declared once, as the capped <c>--safe-b</c> token in app.css, and every other rule uses
/// that token instead of <c>env(safe-area-inset-bottom)</c>. The top inset is reserved in full, because the status
/// bar covers it. No rendering test can catch a rule that reserves the raw bottom inset: a browser tab and headless
/// Chromium report zero, and only the installed web app on a phone with a home indicator shows the empty band under
/// the navigation. So these tests read the stylesheets as text.
/// </summary>
[TestFixture]
public class SafeAreaCssTests
{
    /// <summary>The bottom inset an iPhone with a home indicator reports, in CSS pixels.</summary>
    private const double HomeIndicatorInsetPx = 34.0;

    /// <summary>The root font size at phone widths, in CSS pixels, for converting rem to px.</summary>
    private const double RootFontSizePx = 16.0;

    /// <summary><c>--safe-b</c> caps the device's bottom inset below <see cref="HomeIndicatorInsetPx"/>.</summary>
    [Test]
    public void TheBottomInsetIsDeclaredOnceAsACap()
    {
        string declaration = Declaration(CssSource.AppCss(), "--safe-b");

        Assert.That(declaration, Is.Not.Empty, "app.css declares no --safe-b: the one place the bottom safe area is decided is gone");
        Assert.That(declaration, Does.Contain("env(safe-area-inset-bottom)"),
            "--safe-b no longer reads the device's own bottom inset, so it reserves the same strip on every device");
        Assert.That(declaration, Does.Contain("min("),
            "--safe-b is no longer a cap: reserving the whole inset is what leaves an empty band under the navigation in the installed web app");

        Match cap = Regex.Match(declaration, @"min\(\s*env\(safe-area-inset-bottom\)\s*,\s*([0-9.]+)(rem|px)\s*\)");
        Assert.That(cap.Success, Is.True, $"--safe-b's cap is not a plain length: '{declaration}'");

        double pixels = double.Parse(cap.Groups[1].Value, CultureInfo.InvariantCulture)
            * (cap.Groups[2].Value == "rem" ? RootFontSizePx : 1.0);
        Assert.That(pixels, Is.LessThan(HomeIndicatorInsetPx),
            $"--safe-b caps at {pixels}px, which is not below the {HomeIndicatorInsetPx}px a phone with a home indicator reports — "
            + "a cap that does not cut reserves the whole strip and the empty band is back");
    }

    /// <summary>
    /// No rule other than the <c>--safe-b</c> declaration reads <c>env(safe-area-inset-bottom)</c>. Such a rule would
    /// reserve a larger strip than the rest of the app, and only on a phone.
    /// </summary>
    [Test]
    public void NoRuleReadsTheRawBottomInset()
    {
        foreach ((string name, string css) in CssSource.Stylesheets())
        {
            List<string> offenders = LinesContaining(CssSource.WithoutComments(css), "env(safe-area-inset-bottom)")
                .Where(line => !line.StartsWith("--safe-b:"))
                .ToList();

            Assert.That(offenders, Is.Empty,
                $"{name} reads env(safe-area-inset-bottom) directly; reserve var(--safe-b) instead: {string.Join(" | ", offenders)}");
        }
    }

    /// <summary>The navigation bar, the shell's scroll area and the table all use the capped strip.</summary>
    [Test]
    public void TheChromeAndTheTableReserveTheSameStrip()
    {
        string meta = CssSource.MetaCss();
        string app = CssSource.AppCss();

        Assert.That(Declaration(meta, "--m-safe-b"), Does.Contain("var(--safe-b)"),
            "the shell's alias no longer points at app.css's token, so the shell and the table can reserve different strips");
        Assert.That(Rule(meta, ".m-nav"), Does.Contain("var(--m-safe-b)"),
            "the navigation no longer reserves the capped strip");
        Assert.That(Rule(meta, ".m-shell__main"), Does.Contain("var(--m-safe-b)"),
            "the shell's scroll surface no longer clears the navigation by the capped strip, so its last card sits under the bar or above a gap");
        Assert.That(Rule(app, ".ts-table"), Does.Contain("var(--safe-b)"),
            "the table no longer reserves the capped strip, so the hand and the chrome disagree about where the screen ends");
    }

    /// <summary>
    /// The HUD and the table reserve the full top inset. A cap would draw the wallet under the status bar's clock.
    /// </summary>
    [Test]
    public void TheTopInsetIsReservedInFull()
    {
        Assert.That(Rule(CssSource.MetaCss(), ".m-hud"), Does.Contain("env(safe-area-inset-top)"),
            "the HUD no longer clears the status bar's own inset");
        Assert.That(Rule(CssSource.AppCss(), ".ts-table"), Does.Contain("env(safe-area-inset-top)"),
            "the table no longer clears the status bar's own inset");
    }

    /// <summary>The declaration of <paramref name="property"/>, or the empty string when it is not declared.</summary>
    private static string Declaration(string css, string property)
    {
        Match match = Regex.Match(css, Regex.Escape(property) + @"\s*:([^;]*);");
        return match.Success ? match.Groups[1].Value.Trim() : "";
    }

    /// <summary>
    /// Every brace-balanced block for <paramref name="selector"/>, joined. A media query that repeats the selector
    /// creates a second block, and a declaration there counts too. Unlike <see cref="CssSource.RuleBlocks"/>, the
    /// selector must start its line, so a descendant rule such as <c>.m-shell--x .m-nav</c> cannot satisfy an
    /// assertion about the rule for <c>.m-nav</c> itself.
    /// </summary>
    private static string Rule(string css, string selector)
    {
        MatchCollection matches = Regex.Matches(css, @"(?m)^\s*" + Regex.Escape(selector) + @"\s*\{");
        Assert.That(matches.Count, Is.GreaterThan(0), $"no rule for '{selector}'");

        return string.Join("\n", matches.Select(match => CssSource.BalancedBlock(css, match.Index + match.Value.Length - 1)));
    }

    private static List<string> LinesContaining(string css, string needle)
    {
        return css.Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Contains(needle))
            .ToList();
    }
}
