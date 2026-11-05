using WebClientBase.Configuration;

namespace WebClient;

/// <summary>
/// The game's colours for the CSS variables that the shared UI components read.
/// <para>
/// Each value is a CSS <c>var()</c> reference to a design token in <c>wwwroot/app.css</c> instead of a literal colour,
/// so each colour is defined only once. WebClientBase's components draw their accent in gold, as the game draws an
/// action that asks for the player's attention.
/// </para>
/// </summary>
public static class GameTheme
{
    public static ThemeColors Colors => new ThemeColors
    {
        BgPrimary     = "var(--ground)",
        BgSecondary   = "var(--surface-deep)",
        BgCard        = "var(--surface)",
        BgHover       = "var(--surface-lift)",
        BorderColor   = "var(--hairline)",
        TextPrimary   = "var(--ink)",
        TextSecondary = "var(--ink-2)",
        TextMuted     = "var(--ink-3)",
        AccentBlue    = "var(--gold)",
        AccentGreen   = "var(--claim)",
        AccentAmber   = "var(--gold)",
        AccentRed     = "var(--danger)",
    };
}
