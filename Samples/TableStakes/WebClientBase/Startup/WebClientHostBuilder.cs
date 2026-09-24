using System.Net.Http;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Metaplay.Core.Player;
using WebClientBase.Configuration;
using WebClientBase.Extensions;
using WebClientBase.Services;
using WebClientBase.Utilities;

namespace WebClientBase.Startup;

/// <summary>
/// Configures and runs a Blazor WebAssembly web client, so that the game's Program.cs contains only the
/// game-specific configuration.
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
    private readonly string _logoEmoji;
    private readonly ThemeColors _theme;
    private Action<WebAssemblyHostBuilder>? _builderConfigurer;

    private WebClientHostBuilder(string[] args, string appTitle, string logoEmoji, ThemeColors theme)
    {
        _args = args;
        _appTitle = appTitle;
        _logoEmoji = logoEmoji;
        _theme = theme;
    }

    /// <summary>
    /// Creates a new WebClientHostBuilder.
    /// </summary>
    /// <param name="args">Command-line arguments. Only <c>--env</c> is read, and the browser passes none.</param>
    /// <param name="appTitle">The application title, used as the document title and in UI text.</param>
    /// <param name="logoEmoji">The emoji used as the game's logo.</param>
    /// <param name="theme">The theme colors, or null for the <see cref="ThemeColors"/> defaults.</param>
    public static WebClientHostBuilder<TApp, TPlayerModel, TClientService> Create(
        string[] args,
        string appTitle,
        string logoEmoji,
        ThemeColors? theme = null)
    {
        return new WebClientHostBuilder<TApp, TPlayerModel, TClientService>(args, appTitle, logoEmoji, theme ?? new ThemeColors());
    }

    /// <summary>
    /// Sets an action that configures the WebAssemblyHostBuilder further, for example to register more services.
    /// It runs after the WebClientBase services are registered. A second call replaces the first action.
    /// </summary>
    /// <param name="configure">Action to configure the builder.</param>
    public WebClientHostBuilder<TApp, TPlayerModel, TClientService> ConfigureBuilder(Action<WebAssemblyHostBuilder> configure)
    {
        _builderConfigurer = configure;
        return this;
    }

    /// <summary>
    /// Builds and runs the application.
    /// </summary>
    public async Task RunAsync()
    {
        WebAssemblyHostBuilder builder = WebAssemblyHostBuilder.CreateDefault(_args);

        // An explicit environment from the arguments or the page's "?env=" query parameter takes precedence. It is
        // the only way to select an environment that is never detected from the page host, such as "offline".
        // Otherwise the environment is chosen from the page host (see ConfigureActiveEnvironmentFromPageHost).
        string? explicitEnv = ParseEnvironmentOverrideFromArgs(_args) ?? ParseEnvironmentOverrideFromQuery();
        StaticEnvironmentConfigProvider.ActiveEnvironmentId =
            explicitEnv ?? StaticEnvironmentConfigProvider.ConfigureActiveEnvironmentFromPageHost(builder.HostEnvironment.BaseAddress);

        // An explicit "localhost" still gets the page host, so that access from another device on the LAN works.
        if (explicitEnv == StaticEnvironmentConfigProvider.LocalId)
            StaticEnvironmentConfigProvider.UsePageHostForLocalEnvironment(builder.HostEnvironment.BaseAddress);

        // "?wsPort=" and "?cdnPort=" point the local environment at a game server on other than the default ports,
        // such as the per-worktree servers that tools/run-e2e.py starts.
        StaticEnvironmentConfigProvider.UseEndpointPortsForLocalEnvironment(
            ParsePortOverrideFromQuery("wsPort"),
            ParsePortOverrideFromQuery("cdnPort"));

        // HeadOutlet lets components render <PageTitle> and <HeadContent> into the document head. App.razor uses it
        // for the title and the theme variables.
        builder.RootComponents.Add<TApp>("#app");
        builder.RootComponents.Add<HeadOutlet>("head::after");

        // An HttpClient for the app's own origin, for loading static assets.
        builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

        WebClientConfig config = new(_appTitle, _logoEmoji, _theme);
        builder.Services.AddWebClientBase<TPlayerModel, TClientService>(config);

        _builderConfigurer?.Invoke(builder);

        await builder.Build().RunAsync();
    }

    /// <summary>
    /// Returns the environment ID from the command-line arguments (<c>--env value</c>, <c>-e value</c> or
    /// <c>--env=value</c>), or null when there is none. In the browser there are no arguments.
    /// </summary>
    private static string? ParseEnvironmentOverrideFromArgs(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if ((args[i] == "--env" || args[i] == "-e") && i + 1 < args.Length)
                return args[i + 1];
            if (args[i].StartsWith("--env="))
                return args[i]["--env=".Length..];
        }
        return null;
    }

    /// <summary>
    /// Returns the environment ID from the page URL's query string, for example <c>?env=offline</c>, or null
    /// when the parameter is absent. A Blazor WebAssembly app gets no command-line arguments, so this replaces
    /// <c>--env</c> in the browser.
    /// </summary>
    private static string? ParseEnvironmentOverrideFromQuery() => ParseQueryParameter(EnvironmentLink.QueryParameterName);

    /// <summary>
    /// Returns a port from the page URL's query string, for example <c>?wsPort=</c>. Returns null when the
    /// parameter is absent or is not a valid port number, so the environment keeps its configured port.
    /// </summary>
    private static int? ParsePortOverrideFromQuery(string name)
    {
        string? value = ParseQueryParameter(name);
        return int.TryParse(value, out int port) && port > 0 && port <= 65535 ? port : null;
    }

    /// <summary>
    /// Returns one parameter from the page URL's query string, or null when it is absent.
    /// </summary>
    private static string? ParseQueryParameter(string name)
    {
        // Reads location.search directly, because this runs before the host is built and NavigationManager exists.
        return QueryParameters.Get(QueryParameters.ReadLocationSearch(), name);
    }
}
