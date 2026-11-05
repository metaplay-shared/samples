using Microsoft.AspNetCore.Components;

namespace WebClientBase.Services;

/// <summary>
/// The address a reload starts the client over at: the app root, with the query the page was loaded with
/// (<c>?offline</c>, the game server's ports). The SDK reads that query once, at startup, and in-app
/// navigation does not keep it in the address bar. A reload of the current address could then start the client in
/// another environment, and CDN hosting, which serves only the root, has nothing at any other route.
/// </summary>
public static class StartPage
{
    private static string _url;

    /// <summary>Remember the address the page was loaded at. The host builder calls this once, at startup.</summary>
    internal static void Remember(NavigationManager navigation) =>
        _url = navigation.BaseUri + new Uri(navigation.Uri).Query;

    /// <summary>Reload the client at the app root, with the query the page was loaded with.</summary>
    public static void Reload(NavigationManager navigation) =>
        navigation.NavigateTo(_url ?? navigation.BaseUri, forceLoad: true);
}
