using Game.Logic;

namespace WebClient.Meta;

/// <summary>The font size step for a display name on the fixed-width Profile name plate.</summary>
public enum NameFit
{
    /// <summary>Short enough for the largest size.</summary>
    Roomy,

    /// <summary>Needs one size smaller to stay on one line.</summary>
    Tight,

    /// <summary>At or near the maximum name length, and needs the smallest size.</summary>
    Cramped,
}

/// <summary>
/// Chooses the font size for a display name on the Profile screen.
/// <para>
/// The name plate has a fixed width and names vary in length. Generated names are a single word with no spaces,
/// so a long name at the largest size would wrap mid-word. The size therefore steps down as the name gets longer.
/// The thresholds assume the maximum length from the display name policy (docs/player.md, "Name rules").
/// </para>
/// </summary>
public static class NamePlate
{
    /// <summary>The name length, in characters, from which the name uses <see cref="NameFit.Tight"/>.</summary>
    public const int TightFrom = 10;

    /// <summary>The name length, in characters, from which the name uses <see cref="NameFit.Cramped"/>.</summary>
    public const int CrampedFrom = 14;

    /// <summary>
    /// The size step for a name. Length is counted with <see cref="DisplayNamePolicy.CountCharacters"/>, the same
    /// count the name length limit uses, so a letter with combining marks counts as one character.
    /// </summary>
    public static NameFit Fit(string name)
    {
        int characters = DisplayNamePolicy.CountCharacters(name);

        if (characters >= CrampedFrom)
            return NameFit.Cramped;
        if (characters >= TightFrom)
            return NameFit.Tight;
        return NameFit.Roomy;
    }

    /// <summary>
    /// The CSS modifier class for a name's size step, or null for <see cref="NameFit.Roomy"/>, which uses the base
    /// style.
    /// </summary>
    public static string? ClassFor(string name) => Fit(name) switch
    {
        NameFit.Cramped => "m-nameplate--cramped",
        NameFit.Tight   => "m-nameplate--tight",
        _               => null,
    };
}
