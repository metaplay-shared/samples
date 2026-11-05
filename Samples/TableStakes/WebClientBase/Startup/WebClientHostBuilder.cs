using System.Net.Http;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Metaplay.Core.Player;
using WebClientBase.Configuration;
using WebClientBase.Extensions;
using WebClientBase.Services;

namespace WebClientBase.Startup;

/// <summary>
/// Builder for configuring and running a Blazor WebAssembly WebClient application.
/// Simplifies Program.cs to minimal game-specific configuration.
/// </summary>
/// <typeparam name="TApp">The root App component type (mounted at #app).</typeparam>
/// <typeparam name="TPlayerModel">The game-specific PlayerModel type.</typeparam>
/// <typeparam name="TClientService">The game-specific client service type.</typeparam>
public class WebClientHostBuilder<TApp, TPlayerModel, TClientService>
    where TApp : IComponent
    where TPlayerModel : class, IPlayerModelBase
    where TClientService : class, IMetaplayClientService<TPlayerModel>
{
    private readonly string[] _args;
    private readonly string _appTitle;
    private readonly string? _logoEmoji;
    private readonly ThemeColors _theme;
    private Action<WebAssemblyHostBuilder>? _builderConfigurer;

    private WebClientHostBuilder(string[] args, string appTitle, string? logoEmoji, ThemeColors theme)
    {
        _args = args;
        _appTitle = appTitle;
        _logoEmoji = logoEmoji;
        _theme = theme;
    }

    /// <summary>
    /// Create a new WebClientHostBuilder.
    /// </summary>
    /// <param name="args">Command-line arguments (unused in the browser, kept for API symmetry).</param>
    /// <param name="appTitle">The application title displayed in the header.</param>
    /// <param name="logoEmoji">Optional emoji the game's pages can show as its logo.</param>
    /// <param name="theme">Optional theme color configuration. Uses defaults if not specified.</param>
    public static WebClientHostBuilder<TApp, TPlayerModel, TClientService> Create(
        string[] args,
        string appTitle,
        string? logoEmoji = null,
        ThemeColors? theme = null)
    {
        return new WebClientHostBuilder<TApp, TPlayerModel, TClientService>(args, appTitle, logoEmoji, theme ?? new ThemeColors());
    }

    /// <summary>
    /// Additional configuration for the WebAssemblyHostBuilder (e.g. registering extra services).
    /// </summary>
    /// <param name="configure">Action to configure the builder.</param>
    public WebClientHostBuilder<TApp, TPlayerModel, TClientService> ConfigureBuilder(Action<WebAssemblyHostBuilder> configure)
    {
        _builderConfigurer = configure;
        return this;
    }

    /// <summary>
    /// Build and run the application.
    /// </summary>
    public async Task RunAsync()
    {
        WebAssemblyHostBuilder builder = WebAssemblyHostBuilder.CreateDefault(_args);

        // Mount the root app component and the head outlet (the latter lets components render <PageTitle>/<HeadContent>
        // into the document head — used for the dynamic theme variables and title).
        builder.RootComponents.Add<TApp>("#app");
        builder.RootComponents.Add<HeadOutlet>("head::after");

        // HttpClient pointed at the app's own origin, for loading static assets (e.g. globe/world-meta.json).
        builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

        // WebClientBase services (config + the typed client service). DI registration is container-agnostic.
        WebClientConfig config = new(_appTitle, _logoEmoji, _theme);
        builder.Services.AddWebClientBase<TPlayerModel, TClientService>(config);

        _builderConfigurer?.Invoke(builder);

        WebAssemblyHost host = builder.Build();
        StartPage.Remember(host.Services.GetRequiredService<NavigationManager>());
        await host.RunAsync();
    }
}
