using System.Text;

namespace WebClientBase.Configuration;

/// <summary>
/// Theme color configuration for the web client.
/// </summary>
public class ThemeColors
{
    /// <summary>
    /// Primary background color.
    /// </summary>
    public string BgPrimary { get; set; } = "#121218";

    /// <summary>
    /// Secondary background color.
    /// </summary>
    public string BgSecondary { get; set; } = "#1a1a23";

    /// <summary>
    /// Card background color.
    /// </summary>
    public string BgCard { get; set; } = "#22222d";

    /// <summary>
    /// Hover state background color.
    /// </summary>
    public string BgHover { get; set; } = "#2a2a38";

    /// <summary>
    /// Border color.
    /// </summary>
    public string BorderColor { get; set; } = "rgba(255, 255, 255, 0.06)";

    /// <summary>
    /// Primary text color.
    /// </summary>
    public string TextPrimary { get; set; } = "#f4f4f5";

    /// <summary>
    /// Secondary text color.
    /// </summary>
    public string TextSecondary { get; set; } = "#a1a1aa";

    /// <summary>
    /// Muted text color.
    /// </summary>
    public string TextMuted { get; set; } = "#71717a";

    /// <summary>
    /// Blue accent color.
    /// </summary>
    public string AccentBlue { get; set; } = "#3b82f6";

    /// <summary>
    /// Green accent color.
    /// </summary>
    public string AccentGreen { get; set; } = "#22c55e";

    /// <summary>
    /// Amber accent color.
    /// </summary>
    public string AccentAmber { get; set; } = "#f59e0b";

    /// <summary>
    /// Red accent color.
    /// </summary>
    public string AccentRed { get; set; } = "#ef4444";

    /// <summary>
    /// Generate CSS variables string from the theme colors.
    /// </summary>
    public string ToCssVariables()
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine(":root {");
        sb.AppendLine($"    --bg-primary: {BgPrimary};");
        sb.AppendLine($"    --bg-secondary: {BgSecondary};");
        sb.AppendLine($"    --bg-card: {BgCard};");
        sb.AppendLine($"    --bg-hover: {BgHover};");
        sb.AppendLine($"    --border-color: {BorderColor};");
        sb.AppendLine($"    --text-primary: {TextPrimary};");
        sb.AppendLine($"    --text-secondary: {TextSecondary};");
        sb.AppendLine($"    --text-muted: {TextMuted};");
        sb.AppendLine($"    --accent-blue: {AccentBlue};");
        sb.AppendLine($"    --accent-green: {AccentGreen};");
        sb.AppendLine($"    --accent-amber: {AccentAmber};");
        sb.AppendLine($"    --accent-red: {AccentRed};");
        sb.AppendLine("}");
        return sb.ToString();
    }
}
