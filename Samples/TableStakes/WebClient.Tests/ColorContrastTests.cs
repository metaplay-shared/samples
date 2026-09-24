using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace WebClient.Tests;

/// <summary>
/// Checks the WCAG 2 contrast ratio (<see href="https://www.w3.org/TR/WCAG21/#contrast-minimum"/>) of the
/// colour pairs the app draws: 4.5:1 for normal text, 3:1 for large text and non-text objects such as icons. Token
/// values are read from <c>WebClient/wwwroot/app.css</c> and <c>meta-shell.css</c> when the tests run
/// (<c>docs/meta-shell.md</c>, "Styles and design tokens"). The <c>--m-*</c> names are <c>var()</c> aliases, so
/// every pair names the underlying token. A colour that is not a <c>:root</c> token is written as a hex copy.
/// <para>
/// A regex cannot evaluate the cascade, so each <see cref="Pairs"/> entry is written by hand from the selector
/// that draws it. When a selector starts using a different token, update the pair.
/// </para>
/// </summary>
[TestFixture]
public class ColorContrastTests
{
    public enum Kind { Text, LargeText }

    public readonly record struct Pair(string Label, string Foreground, string Background, Kind Kind);

    /// <summary>
    /// Foreground/background pairs that a player sees on screen, each labelled with the element that draws it.
    /// The list does not cover every combination the palette allows.
    /// </summary>
    private static readonly Pair[] Pairs =
    {
        // The shell's default body text (.m-shell), on the app background and on the surfaces cards and sheets
        // draw.
        new("Body text (--ink) on the shell ground (--ground)",                 "--ink",   "--ground",       Kind.Text),
        new("Body text (--ink) on a plain card's lightest stop (--surface)",    "--ink",   "--surface",      Kind.Text),
        new("Body text (--ink) on a raised plate (--surface-lift)",             "--ink",   "--surface-lift", Kind.Text),

        // --ink-2: card notes, sheet body copy and quiet-button labels. Every card draws the shared panel
        // gradient, so the backgrounds to check are the plain surfaces and the gradient's darkest stop.
        new("Card note (--ink-2) on a plain card (--surface)",       "--ink-2", "--surface",   Kind.Text),
        new("Card note (--ink-2) on the shell ground (--ground)",    "--ink-2", "--ground",    Kind.Text),
        new("Card note (--ink-2) on a card's darkest stop (--surface-deep)", "--ink-2", "--surface-deep", Kind.Text),

        // --ink-3: the dimmest text colour, used for nav labels, card eyebrows and captions. It passes 4.5:1 with
        // the smallest margin of the text pairs, so it is the first to fail when the palette is darkened.
        new("Nav label / default eyebrow (--ink-3) on the shell ground (--ground)", "--ink-3", "--ground",    Kind.Text),
        new("Default card eyebrow (--ink-3) on a plain card (--surface)",              "--ink-3", "--surface", Kind.Text),
        new("Default card eyebrow (--ink-3) on a plain card's darkest stop (--surface-deep)", "--ink-3", "--surface-deep", Kind.Text),

        // --gold as text: eyebrows, prices, the HUD greeting and the wheel's odds total.
        new("Gold text (--gold) on the shell ground (--ground)",       "--gold", "--ground",     Kind.Text),
        new("Gold text (--gold) on a plain card (--surface)",            "--gold", "--surface",  Kind.Text),
        new("Gold text (--gold) on a card's darkest stop (--surface-deep)", "--gold", "--surface-deep", Kind.Text),

        // The primary action button: --on-gold text on the gold key's gradient. The label spans the whole
        // gradient, so each of its three stops is checked.
        new("Gold key label (--on-gold) on the key's lightest stop (--gold-light)", "--on-gold", "--gold-light", Kind.LargeText),
        new("Gold key label (--on-gold) on the key's middle stop (--gold)",         "--on-gold", "--gold",       Kind.LargeText),
        new("Gold key label (--on-gold) on the key's darkest stop (--gold-deep)",   "--on-gold", "--gold-deep",  Kind.LargeText),

        // The semantic accent colours as text, on the two backgrounds they most often appear on.
        new("Claim accent (--claim) on the shell ground (--ground)",     "--claim",    "--ground",     Kind.Text),
        new("Claim accent (--claim) on a plain card's darkest stop (--surface-deep)",   "--claim",    "--surface-deep", Kind.Text),
        new("Live accent (--live) on the shell ground (--ground)",       "--live",     "--ground",     Kind.Text),
        new("Live accent (--live) on a plain card's darkest stop (--surface-deep)",     "--live",     "--surface-deep", Kind.Text),
        new("Progress accent (--progress) on the shell ground (--ground)", "--progress", "--ground",   Kind.Text),
        new("Progress accent (--progress) on a plain card's darkest stop (--surface-deep)", "--progress", "--surface-deep", Kind.Text),
        new("Danger accent (--danger) on the shell ground (--ground)",   "--danger",   "--ground",     Kind.Text),
        new("Danger accent (--danger) on a plain card's darkest stop (--surface-deep)", "--danger",   "--surface-deep", Kind.Text),

        // Chip and tag labels on the dark strip gradient (.strip in app.css). The background is a copy of the
        // gradient's lightest stop, the worst case for a light label. The table's strip labels (nameplate, banner,
        // dark control) use --gilt-light through .ts-engraved and are checked against the same stop.
        new("Table strip label (--gilt-light) on the strip's lightest stop (#141310)", "--gilt-light", "#141310", Kind.Text),
        new("Default chip or quiet tag (--ink-2) on the strip's lightest stop (#141310)", "--ink-2",   "#141310", Kind.Text),
        new("Claim chip (--claim) on the strip's lightest stop (#141310)",              "--claim",      "#141310", Kind.Text),
        new("Live chip (--live-light) on the strip's lightest stop (#141310)",          "--live-light", "#141310", Kind.Text),
        new("Gold chip or featured tag (--gold) on the strip's lightest stop (#141310)", "--gold",      "#141310", Kind.Text),

        // Prize wheel labels: cream art on the two darkest non-jackpot wedge fills, dark ink on the jackpot wedge's
        // gradient, and the blank wedge's grey. The jackpot gradient runs from light gold at the hub to dark gold at
        // the rim, and the labels sit in a band between the two. The darkest gold under a label is therefore an
        // interpolated colour, not one of the named stops, and has its own pair. The labels are small bold text, so
        // the normal-text threshold applies. The colours are rules in meta-shell.css, not :root tokens, so the pairs
        // hold copies.
        new("Wheel: cream wedge art on the gems' teal",                "#fff6d8", "#1b4d5a", Kind.Text),
        new("Wheel: cream wedge art on a spin-again's plum",           "#fff6d8", "#5a2340", Kind.Text),
        new("Wheel: jackpot ink on the gradient's lightest stop",      "#2a1a05", "#ffe9a8", Kind.Text),
        new("Wheel: jackpot ink on the gradient's mid stop",           "#2a1a05", "#f5c542", Kind.Text),
        new("Wheel: jackpot ink on the darkest gold the labels reach", "#2a1a05", "#c58f25", Kind.Text),
        new("Wheel: the blank's skull grey on its near-black",         "#9a9aa6", "#050308", Kind.Text),
    };

    /// <summary>
    /// One test case per <see cref="Pairs"/> entry, named by its label, so a failure names the pair instead of an
    /// array index.
    /// </summary>
    private static IEnumerable<TestCaseData> PairCases()
    {
        foreach (Pair pair in Pairs)
            yield return new TestCaseData(pair).SetName(pair.Label);
    }

    [TestCaseSource(nameof(PairCases))]
    public void MeetsItsWcagAaThreshold(Pair pair)
    {
        double ratio     = Contrast.Ratio(Tokens.Resolve(pair.Foreground), Tokens.Resolve(pair.Background));
        double threshold = pair.Kind == Kind.Text ? 4.5 : 3.0;

        TestContext.Out.WriteLine($"{pair.Label}: {ratio:0.00}:1 (needs {threshold:0.0}:1)");
        Assert.That(ratio, Is.GreaterThanOrEqualTo(threshold),
            $"{pair.Label} is {ratio:0.00}:1, short of the {threshold:0.0}:1 WCAG AA floor for " +
            (pair.Kind == Kind.Text ? "normal text" : "large text or a non-text graphical object") + ".");
    }

    /// <summary>
    /// The <c>MetaEmblem</c> plaque: an icon drawn in <c>currentColor</c> over a two-stop radial gradient
    /// (<c>WebClient/Components/Meta/MetaEmblem.razor</c>). The icon covers the whole face, so it is checked against
    /// both stops at the 3:1 non-text threshold, because every plaque has a text label beside it.
    /// <para>
    /// The stop colours are per-variant custom properties, not <c>:root</c> tokens, so the test cases hold copies
    /// that must be updated when a variant's rule changes. The default variant copies <c>--surface-lift</c> and
    /// <c>--surface-deep</c>, which the gradient reads through <c>var()</c> fallbacks the parser cannot resolve.
    /// </para>
    /// </summary>
    [TestCase("default (green)",  "#13291f", "#060f0c", "#f5c542")]
    [TestCase("claim",            "#17605a", "#06231f", "#2ee6c5")]
    [TestCase("live",              "#58256f", "#1c0b2c", "#f0abfc")]
    [TestCase("progress",         "#1d3f86", "#0a1533", "#93c5fd")]
    [TestCase("gold",              "#8f6a24", "#2c1c07", "#ffe9a8")]
    [TestCase("locked",           "#2a3350", "#0c1122", "#8a948a")]
    public void EmblemIconMeetsTheNonTextThreshold(string variant, string hi, string lo, string ink)
    {
        double ratioAtHi = Contrast.Ratio(ink, hi);
        double ratioAtLo = Contrast.Ratio(ink, lo);
        double worst     = Math.Min(ratioAtHi, ratioAtLo);

        TestContext.Out.WriteLine($"emblem--{variant}: {ratioAtHi:0.00}:1 at the lit stop, {ratioAtLo:0.00}:1 at the shadowed stop");
        Assert.That(worst, Is.GreaterThanOrEqualTo(3.0),
            $"the {variant} emblem's icon is {worst:0.00}:1 against its own plaque, short of the 3:1 floor " +
            "for a non-text graphical object.");
    }

    /// <summary>
    /// Fails when the token stylesheets do not parse to one value per token. A separate test reports a parse
    /// problem once, by name, instead of through every pair test.
    /// </summary>
    [Test]
    public void ThePaletteReadsBackUnambiguously()
    {
        Assert.That(Tokens.Problems, Is.Empty, string.Join(" ", Tokens.Problems));
    }

    /// <summary>Colour token values, read from the <c>:root</c> blocks of the token stylesheets.</summary>
    private static class Tokens
    {
        /// <summary>
        /// Problems found while parsing. They are collected instead of asserted because <see cref="Load"/> runs
        /// from a static field initializer, where a failed assertion would reach every test as a
        /// <see cref="TypeInitializationException"/>. <see cref="ColorContrastTests.ThePaletteReadsBackUnambiguously"/>
        /// reports them.
        /// </summary>
        public static readonly List<string> Problems = new();

        private static readonly Dictionary<string, string> Values = Load();

        /// <summary>
        /// The hex value of <paramref name="token"/>. A value that is already a hex colour is returned as it is, for
        /// pairs whose colour is not a <c>:root</c> token.
        /// </summary>
        public static string Resolve(string token) =>
            token.StartsWith('#') ? token
            : Values.TryGetValue(token, out string? hex)
                ? hex
                : throw new KeyNotFoundException($"'{token}' is not a colour custom property at :root in app.css or meta-shell.css.");

        private static Dictionary<string, string> Load()
        {
            Dictionary<string, string> values = new(StringComparer.Ordinal);
            foreach ((string name, string css) in CssSource.Stylesheets())
            {

                // Read only the :root blocks. Colour properties under other selectors, such as an emblem variant's
                // --med-hi, apply only within that selector, and a regex cannot evaluate the cascade. A token
                // declared at :root more than once, as a theme override would do, has no single value, so it is
                // reported as a problem. Only hex values are read, under any name, because the tokens share no
                // prefix. Aliases such as --m-gold: var(--gold) are skipped.
                foreach (Match block in Regex.Matches(css, @":root\s*\{([^}]*)\}"))
                {
                    foreach (Match declaration in Regex.Matches(block.Groups[1].Value, @"(--[a-z0-9-]+)\s*:\s*(#[0-9a-fA-F]{3,8})\s*;"))
                    {
                        string token = declaration.Groups[1].Value;

                        if (values.ContainsKey(token))
                        {
                            // Keep the first declaration, so the pair tests still report ratios, and let
                            // ThePaletteReadsBackUnambiguously name the ambiguous token.
                            Problems.Add(
                                $"'{token}' is declared at :root more than once across the token stylesheets (also in {name}). Which one wins is a cascade " +
                                "question this parser cannot answer, so the pairing checks would be measuring a " +
                                "colour nobody sees.");
                            continue;
                        }

                        values[token] = declaration.Groups[2].Value;
                    }
                }
            }

            if (values.Count == 0)
                Problems.Add("found no colour custom properties at :root in the token stylesheets — the parser or the files moved.");

            return values;
        }
    }

    /// <summary>WCAG 2's relative luminance and contrast ratio, over plain sRGB hex colours (no alpha).</summary>
    private static class Contrast
    {
        public static double Ratio(string hexA, string hexB)
        {
            double lA = RelativeLuminance(hexA);
            double lB = RelativeLuminance(hexB);
            double lighter = Math.Max(lA, lB);
            double darker  = Math.Min(lA, lB);
            return (lighter + 0.05) / (darker + 0.05);
        }

        private static double RelativeLuminance(string hex)
        {
            (double r, double g, double b) = ParseHex(hex);
            return 0.2126 * Linearize(r) + 0.7152 * Linearize(g) + 0.0722 * Linearize(b);
        }

        private static double Linearize(double channel) =>
            channel <= 0.03928 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);

        private static (double R, double G, double B) ParseHex(string hex)
        {
            string h = hex.TrimStart('#');
            if (h.Length == 3)
                h = string.Concat(h[0], h[0], h[1], h[1], h[2], h[2]);

            int r = Convert.ToInt32(h.Substring(0, 2), 16);
            int g = Convert.ToInt32(h.Substring(2, 2), 16);
            int b = Convert.ToInt32(h.Substring(4, 2), 16);
            return (r / 255.0, g / 255.0, b / 255.0);
        }
    }
}
