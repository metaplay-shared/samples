namespace WebClient.Gameplay;

/// <summary>
/// The altitude bands every course is drawn in, by the names the canvas renderer keys them on
/// (<c>ALTITUDE_BANDS</c> in <c>wwwroot/js/renderer/artSpecs.js</c>): a run starts in the first and climbs one
/// band at each climb, so a map's first part is the foothills and its last the summit. A course view names the
/// band it starts in only for a part previewed or played alone from the part editor, which has no climb to carry
/// it up; the editor calls that choice the draft's theme.
/// </summary>
public static class TimeTrialAltitudeBands
{
    public const string Foothills = "foothills";
    public const string MidRidge  = "mid-ridge";
    public const string Summit    = "summit";

    /// <summary>Every band, lowest first.</summary>
    public static readonly string[] All = { Foothills, MidRidge, Summit };

    /// <summary>The band's name as the part editor shows it.</summary>
    public static string DisplayName(string band) => band switch
    {
        Foothills => "Foothills",
        MidRidge  => "Mid-ridge",
        Summit    => "Summit",
        _         => band,
    };
}
