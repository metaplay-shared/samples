using WebClientBase.Configuration;

namespace WebClient;

/// <summary>
/// The game's colours for the CSS variables that the shared UI components read.
/// <para>
/// Each value is a CSS <c>var()</c> reference to a design token in <c>wwwroot/app.css</c> instead of a literal colour,
/// so each colour is defined only once.
/// </para>
/// </summary>
public static class GameTheme
{
    public static ThemeColors Colors => new ThemeColors
    {
        BgPrimary   = "var(--ground)",
        BgCard      = "var(--surface)",
        BgHover     = "var(--surface-lift)",
        BorderColor = "var(--hairline)",
    };
}
