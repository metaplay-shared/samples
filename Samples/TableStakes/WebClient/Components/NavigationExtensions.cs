using Microsoft.AspNetCore.Components;

namespace WebClient.Components;

public static class NavigationExtensions
{
    /// <summary>
    /// The current page as a route in the form <c>MetaRoutes</c> uses: a leading slash followed by the path and
    /// query relative to the app's base URI.
    /// </summary>
    public static string CurrentRoute(this NavigationManager navigation) => "/" + navigation.ToBaseRelativePath(navigation.Uri);
}
