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
    /// Add WebClientBase services to the service collection.
    /// </summary>
    /// <typeparam name="TPlayerModel">The game-specific PlayerModel type.</typeparam>
    /// <typeparam name="TClientService">The game-specific client service type.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="config">Configuration for the web client.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddWebClientBase<TPlayerModel, TClientService>(
        this IServiceCollection services,
        WebClientConfig config)
        where TPlayerModel : class, IPlayerModelBase
        where TClientService : class, IMetaplayClientService<TPlayerModel>
    {
        services.AddSingleton(config);

        // Register the concrete service as a singleton
        services.AddSingleton<TClientService>();

        // Register the generic interface pointing to the same instance
        services.AddSingleton<IMetaplayClientService<TPlayerModel>>(sp => sp.GetRequiredService<TClientService>());

        // Register the non-generic interface for components that don't need typed access
        services.AddSingleton<IMetaplayConnectionService>(sp => sp.GetRequiredService<TClientService>());

        return services;
    }
}
