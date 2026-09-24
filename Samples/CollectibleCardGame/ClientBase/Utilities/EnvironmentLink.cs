using Microsoft.AspNetCore.Components;
using System;

namespace Game.ClientBase.Utilities;

/// <summary>
/// Keeps the environment override on in-app navigation. The environment is selected once at startup from the
/// page's <c>?env=</c> parameter (a WebAssembly app has no argv), so a link that drops the query leaves the
/// address bar pointing at a URL that would boot into a different environment on the next reload — offline
/// mode silently becoming "connect to a local game server that is not running".
/// <para>
/// Screens build their hrefs through <see cref="Href"/> and navigate through <see cref="NavigateTo"/> so the
/// override rides along. A URL with no override is returned unchanged.
/// </para>
/// </summary>
public static class EnvironmentLink
{
    const string EnvParameter = "env";

    /// <summary> <paramref name="path"/> with the current environment override appended, if there is one. </summary>
    public static string Href(NavigationManager navigation, string path)
    {
        string? env = CurrentOverride(navigation);
        if (env == null)
            return path;

        char separator = path.Contains('?') ? '&' : '?';
        return $"{path}{separator}{EnvParameter}={Uri.EscapeDataString(env)}";
    }

    /// <summary> Navigate to <paramref name="path"/>, keeping the environment override. </summary>
    public static void NavigateTo(NavigationManager navigation, string path, bool forceLoad = false)
        => navigation.NavigateTo(Href(navigation, path), forceLoad);

    /// <summary> The active <c>?env=</c> value, or null when the page carries none. </summary>
    public static string? CurrentOverride(NavigationManager navigation)
    {
        if (!Uri.TryCreate(navigation.Uri, UriKind.Absolute, out Uri? uri))
            return null;

        foreach (string pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int equals = pair.IndexOf('=');
            if (equals > 0 && pair[..equals] == EnvParameter)
                return Uri.UnescapeDataString(pair[(equals + 1)..]);
        }

        return null;
    }
}
