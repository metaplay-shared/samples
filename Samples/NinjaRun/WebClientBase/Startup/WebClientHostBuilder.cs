using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;
using WebClientBase.Configuration;
using WebClientBase.Extensions;
using WebClientBase.Services;

namespace WebClientBase.Startup;

/// <summary>
/// Builder for configuring and running a Blazor WebAssembly WebClient application.
/// Simplifies Program.cs to minimal game-specific configuration.
/// </summary>
/// <typeparam name="TApp">The root App component type (mounted at #app).</typeparam>
/// <typeparam name="TClientService">The game-specific client service type.</typeparam>
public class WebClientHostBuilder<TApp, TClientService>
    where TApp : IComponent
    where TClientService : class, IMetaplayConnectionService
{
    private readonly string[] _args;
    private readonly string _appTitle;
    private readonly ThemeColors _theme;

    private WebClientHostBuilder(string[] args, string appTitle, ThemeColors theme)
    {
        _args = args;
        _appTitle = appTitle;
        _theme = theme;
    }

    /// <summary>
    /// Create a new WebClientHostBuilder.
    /// </summary>
    /// <param name="args">Command-line arguments, handed to the WebAssembly host builder.</param>
    /// <param name="appTitle">The document title.</param>
    /// <param name="theme">Optional theme color configuration. Uses defaults if not specified.</param>
    public static WebClientHostBuilder<TApp, TClientService> Create(
        string[] args,
        string appTitle,
        ThemeColors? theme = null)
    {
        return new WebClientHostBuilder<TApp, TClientService>(args, appTitle, theme ?? new ThemeColors());
    }

    /// <summary>
    /// Build and run the application.
    /// </summary>
    public async Task RunAsync()
    {
        WebAssemblyHostBuilder builder = WebAssemblyHostBuilder.CreateDefault(_args);

        // Select the environment. An explicit override from the page's "?env=" query parameter wins, which is how
        // the browser reaches environments that are never auto-detected (notably "offline"). Otherwise auto-detect
        // from the host the page was served from: localhost/LAN IP → local dev, a real domain → cloud (connects
        // over wss).
        string? explicitEnv = PageQuery.Get("env");
        StaticEnvironmentConfigProvider.ActiveEnvironmentId =
            explicitEnv ?? StaticEnvironmentConfigProvider.ConfigureActiveEnvironmentFromPageHost(builder.HostEnvironment.BaseAddress);

        // With an explicit localhost override, still adapt it to the page host so LAN access works.
        if (explicitEnv == "localhost")
            StaticEnvironmentConfigProvider.UsePageHostForLocalEnvironment(builder.HostEnvironment.BaseAddress);

        // Mount the root app component and the head outlet (the latter lets components render <PageTitle>/<HeadContent>
        // into the document head, used for the dynamic theme variables and title).
        builder.RootComponents.Add<TApp>("#app");
        builder.RootComponents.Add<HeadOutlet>("head::after");

        // WebClientBase services (config + the typed client service). DI registration is container-agnostic.
        WebClientConfig config = new(_appTitle, _theme);
        builder.Services.AddWebClientBase<TClientService>(config);

        await builder.Build().RunAsync();
    }
}
