using Metaplay.Core.Player;
using Microsoft.Extensions.DependencyInjection;
using WebClientBase.Configuration;
using WebClientBase.Services;

namespace WebClientBase.Extensions;

/// <summary>
/// Extension methods for configuring WebClientBase services.
/// </summary>
public static class ServiceExtensions
{
    /// <summary>
    /// Registers the web client config and the client service as singletons.
    /// </summary>
    /// <typeparam name="TPlayerModel">The game-specific PlayerModel type.</typeparam>
    /// <typeparam name="TClientService">The game-specific client service type.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="config">Configuration for the web client.</param>
    /// <returns>The same service collection.</returns>
    public static IServiceCollection AddWebClientBase<TPlayerModel, TClientService>(
        this IServiceCollection services,
        WebClientConfig config)
        where TPlayerModel : class, IPlayerModelBase
        where TClientService : class, IMetaplayClientService<TPlayerModel>
    {
        services.AddSingleton(config);

        services.AddSingleton<TClientService>();

        // Both interfaces resolve to the same TClientService instance.
        services.AddSingleton<IMetaplayClientService<TPlayerModel>>(sp => sp.GetRequiredService<TClientService>());

        services.AddSingleton<IMetaplayConnectionService>(sp => sp.GetRequiredService<TClientService>());

        return services;
    }
}
