using System.Runtime.InteropServices.JavaScript;

namespace WebClientBase.Startup;

/// <summary>
/// Reads parameters from the page URL's query string, e.g. <c>?env=offline&amp;course=...</c>.
/// </summary>
/// <remarks>
/// Reads <c>location.search</c> directly, so it works before the host is built, when there is no
/// <c>NavigationManager</c> yet. A Blazor WebAssembly app has no real argv; the query string stands in for it.
/// </remarks>
public static class PageQuery
{
    /// <summary>The unescaped value of the first <paramref name="key"/> parameter, or null when the page has none.</summary>
    public static string? Get(string key)
    {
        using JSObject? location = JSHost.GlobalThis.GetPropertyAsJSObject("location");
        string? search = location?.GetPropertyAsString("search");
        if (string.IsNullOrEmpty(search))
            return null;

        foreach (string pair in search.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int equals = pair.IndexOf('=');
            if (equals > 0 && pair[..equals] == key)
                return Uri.UnescapeDataString(pair[(equals + 1)..]);
        }

        return null;
    }
}
