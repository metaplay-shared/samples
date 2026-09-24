using Microsoft.AspNetCore.Components;
using System;
using System.Runtime.InteropServices.JavaScript;

namespace WebClientBase.Utilities;

/// <summary>
/// Keeps the page's <c>?env=</c> override on in-app navigation. <c>WebClientHostBuilder</c> selects the environment
/// once at startup from the page URL. A link that drops the query leaves a URL that boots into a different
/// environment on the next reload, for example a connection attempt to a local game server instead of offline
/// mode. When the current URL has no override, the path is returned unchanged.
/// </summary>
public static class EnvironmentLink
{
    public const string QueryParameterName = "env";

    /// <summary><paramref name="path"/> with the current page's environment override appended, if it has one.</summary>
    public static string Href(NavigationManager navigation, string path) => Href(navigation.Uri, path);

    /// <summary><paramref name="path"/> with the environment override of <paramref name="currentUri"/> appended, if it has one.</summary>
    public static string Href(string currentUri, string path)
    {
        string env = CurrentOverride(currentUri);
        if (env == null)
            return path;

        char separator = path.Contains('?') ? '&' : '?';
        return $"{path}{separator}{QueryParameterName}={Uri.EscapeDataString(env)}";
    }

    /// <summary>Navigate to <paramref name="path"/>, keeping the environment override.</summary>
    public static void NavigateTo(NavigationManager navigation, string path, bool forceLoad = false)
        => navigation.NavigateTo(Href(navigation, path), forceLoad);

    /// <summary>The <c>env</c> query parameter of <paramref name="currentUri"/>, or null when it carries none.</summary>
    public static string CurrentOverride(string currentUri)
    {
        if (!Uri.TryCreate(currentUri, UriKind.Absolute, out Uri uri))
            return null;

        return QueryParameters.Get(uri.Query, QueryParameterName);
    }
}

/// <summary>
/// Reads parameters from a URL query string. The environment override, the test settings and the fixture scenario
/// are all read through it, so every parameter is parsed with the same rules.
/// </summary>
public static class QueryParameters
{
    /// <summary>The browser's current query string (<c>location.search</c>), or null off the browser.</summary>
    public static string ReadLocationSearch()
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
