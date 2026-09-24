using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using WebClient.Services;

namespace WebClient.Tests;

/// <summary>
/// Base class for the bUnit render tests. It registers the app's shell services with
/// <see cref="NoSessionClientService"/>, a client that never opens a session, so <see cref="MetaStateService"/>
/// serves the fixture scenario named by the <c>?meta=</c> parameter of the navigation URI, as in the app.
/// <para>
/// A page that the app gates on a session renders its gate here, not its content. <c>AddWebClientBase</c> is not
/// compiled into this project, so a component that injects <c>IMetaplayConnectionService</c> or
/// <c>IMetaplayClientService</c> fails to resolve instead of rendering.
/// </para>
/// </summary>
public abstract class BunitPageTest : Bunit.TestContext
{
    /// <summary>The shell services that run a timer. Kept so that teardown stops the ones this test resolved.</summary>
    private MetaStateService? _metaState;
    private WalletBurstService? _walletBurst;

    /// <summary>
    /// Sets up the test context to serve the given fixture scenario (the default scenario when null) at the given
    /// app route. Call it once per test: bUnit builds its service provider on the first resolution, and a
    /// registration after that throws. To assert several scenarios, write one test per scenario.
    /// <para>
    /// The navigation URI is set last because resolving <see cref="NavigationManager"/> builds the provider.
    /// </para>
    /// </summary>
    protected void Setup(string route, string? scenario = null)
    {
        Services.AddNoSessionClient(new NoSessionClientService());
        Services.AddScoped<MetaStateService>(sp =>
            _metaState = new MetaStateService(sp.GetRequiredService<NavigationManager>(), sp.GetRequiredService<NoSessionClientService>()));
        Services.AddScoped<WalletBurstService>(sp =>
            _walletBurst = new WalletBurstService(sp.GetRequiredService<NavigationManager>()));

        string query = scenario is null ? "" : $"?meta={scenario}";
        FakeNavigationManager nav = (FakeNavigationManager)Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo($"http://localhost{route}{query}");
    }

    /// <summary>
    /// Stops the timers of <see cref="MetaStateService"/> and <see cref="WalletBurstService"/> before the renderer is
    /// disposed. Both timers call <c>InvokeAsync(StateHasChanged)</c> from a thread-pool thread, and a tick after
    /// the renderer is gone would fail. Only services the test resolved are stopped, so a test that failed before
    /// its first render reports its own failure instead of a teardown failure.
    /// </summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _metaState?.Dispose();
            _walletBurst?.Dispose();
        }

        base.Dispose(disposing);
    }
}
