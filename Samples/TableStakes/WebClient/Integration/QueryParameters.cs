using System;
using System.Runtime.InteropServices.JavaScript;

namespace WebClient.Integration;

/// <summary>
/// Reads parameters from a URL query string. The test settings and the fixture scenario are all read through it, so
/// every parameter is parsed with the same rules.
/// </summary>
public static class QueryParameters
{
    /// <summary>
    /// The query string the page was loaded with, which <see cref="RememberAtStart"/> keeps, or null when it was not
    /// called, as off the browser. In-app navigation goes to plain paths, so the query is no longer in the address
    /// after it, and settings read later read this instead.
    /// </summary>
    public static string AtStart { get; private set; }

    /// <summary>Keep the page's query string for <see cref="AtStart"/>. Program.cs calls this once, at startup.</summary>
    public static void RememberAtStart() => AtStart = ReadLocationSearch();

    /// <summary>The browser's current query string (<c>location.search</c>), or null off the browser.</summary>
    static string ReadLocationSearch()
    {
        using JSObject location = JSHost.GlobalThis.GetPropertyAsJSObject("location");
        return location?.GetPropertyAsString("search");
    }

    /// <summary>
    /// The unescaped value of <paramref name="name"/> in <paramref name="query"/>, with or without its leading
    /// <c>?</c>, or null if it is absent. <paramref name="comparison"/> decides how names are matched.
    /// </summary>
    public static string Get(string query, string name, StringComparison comparison = StringComparison.Ordinal)
    {
        if (string.IsNullOrEmpty(query))
            return null;

        foreach (string pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int equals = pair.IndexOf('=');
            if (equals > 0 && string.Equals(pair.Substring(0, equals), name, comparison))
                return Uri.UnescapeDataString(pair.Substring(equals + 1));
        }

        return null;
    }
}
