using System.Text;

namespace WebClientBase.Configuration;

/// <summary>
/// Theme color configuration for the web client.
/// </summary>
public class ThemeColors
{
    // The defaults copy the values of the design tokens defined in WebClient/wwwroot/app.css, which is the
    // source of truth (docs/meta-shell.md, "Styles and design tokens"). WebClient/GameTheme.cs replaces most of
    // them with var() references to the tokens. The literals apply when a host sets no other value.

    /// <summary>
    /// Primary background color. Matches the <c>--ground</c> token.
    /// </summary>
    public string BgPrimary { get; set; } = "#061711";

    /// <summary>
    /// Card background color. Matches the <c>--surface</c> token.
    /// </summary>
    public string BgCard { get; set; } = "#0e1b16";

    /// <summary>
    /// Hover state background color. Matches the <c>--surface-lift</c> token.
    /// </summary>
    public string BgHover { get; set; } = "#13291f";

    /// <summary>
    /// Border color. Matches the <c>--hairline</c> token.
    /// </summary>
    public string BorderColor { get; set; } = "rgba(255, 255, 255, 0.08)";

    /// <summary>
    /// Primary text color. Matches the <c>--ink</c> token.
    /// </summary>
    public string TextPrimary { get; set; } = "#f2efe6";

    /// <summary>
    /// Secondary text color. Matches the <c>--ink-2</c> token.
    /// </summary>
    public string TextSecondary { get; set; } = "#b9c2b6";

    /// <summary>
    /// Muted text color. Matches the <c>--ink-3</c> token.
    /// </summary>
    public string TextMuted { get; set; } = "#8a948a";

    /// <summary>
    /// Red accent color. Matches the <c>--danger</c> token.
    /// </summary>
    public string AccentRed { get; set; } = "#fb7185";

    /// <summary>
    /// Returns a CSS <c>:root</c> rule that declares the theme colors as CSS variables.
    /// </summary>
    public string ToCssVariables()
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine(":root {");
        sb.AppendLine($"    --bg-primary: {BgPrimary};");
        sb.AppendLine($"    --bg-card: {BgCard};");
        sb.AppendLine($"    --bg-hover: {BgHover};");
        sb.AppendLine($"    --border-color: {BorderColor};");
        sb.AppendLine($"    --text-primary: {TextPrimary};");
        sb.AppendLine($"    --text-secondary: {TextSecondary};");
        sb.AppendLine($"    --text-muted: {TextMuted};");
        sb.AppendLine($"    --accent-red: {AccentRed};");
        sb.AppendLine("}");
        return sb.ToString();
    }
}
