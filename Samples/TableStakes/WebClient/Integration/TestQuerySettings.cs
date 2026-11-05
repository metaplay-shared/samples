using System.Globalization;

namespace WebClient.Integration;

/// <summary>
/// Parses test settings from a query string, such as <c>moveDeadlineMs</c> or <c>botThinkMs</c>. Tests use them to
/// shorten timers instead of waiting for them (<c>docs/testing.md</c>, "Forcing timers"). All callers parse through
/// this class so that every setting is read with the same rules.
/// <para>
/// Callers read the settings from the query the page was loaded with (<see cref="StartQuery"/>), so a setting holds
/// on every screen the test reaches by in-app navigation.
/// </para>
/// </summary>
public static class TestQuerySettings
{
    /// <summary>The query string the page was loaded with (<see cref="QueryParameters.AtStart"/>).</summary>
    public static string StartQuery => QueryParameters.AtStart;

    /// <summary>The raw value of <paramref name="key"/> in <paramref name="query"/>, or null if it is absent.</summary>
    public static string TryGet(string query, string key) => QueryParameters.Get(query, key);

    /// <summary>
    /// The value of <paramref name="key"/> as a non-negative whole number, or null if it is absent or is not
    /// one. Negative values return null because the values are durations.
    /// </summary>
    public static int? TryGetInt(string query, string key)
    {
        string value = TryGet(query, key);
        if (value != null
            && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
            && parsed >= 0)
            return parsed;
        return null;
    }

    /// <summary>
    /// The value of <paramref name="key"/> as an unsigned 64-bit number, or null if it is absent or is not
    /// one. Random seeds use this parser because they can take any 64-bit value, which
    /// <see cref="TryGetInt"/> would reject.
    /// </summary>
    public static ulong? TryGetULong(string query, string key)
    {
        string value = TryGet(query, key);
        if (value != null
            && ulong.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong parsed))
            return parsed;
        return null;
    }
}
