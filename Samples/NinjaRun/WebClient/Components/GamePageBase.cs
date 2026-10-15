using Microsoft.AspNetCore.Components;
using WebClient.Services;

namespace WebClient.Components;

/// <summary>
/// The base of every routed page of the game: the main page and the developer tools. It connects the session
/// when the page opens and renders the page again whenever the service says the connection or the player
/// model changed. Each page draws its content inside a <see cref="GameConnectionGate"/>, which shows the
/// connection's spinner or error until the player model is in.
/// </summary>
public abstract class GamePageBase : ComponentBase, IAsyncDisposable
{
    /// <summary>Where the canvas renderer loads its art from, relative to the site root.</summary>
    protected const string AssetBaseUrl = "art/";

    [Inject] protected MetaplayClientService MetaplayService { get; set; } = default!;

    protected override void OnInitialized()
    {
        MetaplayService.OnStateChanged += OnServiceStateChanged;
    }

    protected override async Task OnInitializedAsync()
    {
        await MetaplayService.ConnectAsync();
    }

    /// <summary>The connection or the player model changed. Renders the page; a page with more to do on a change overrides it.</summary>
    protected virtual void OnServiceStateChanged() => InvokeAsync(StateHasChanged);

    /// <summary>Release what the page holds beyond its subscription to the service.</summary>
    protected virtual ValueTask DisposePageAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        MetaplayService.OnStateChanged -= OnServiceStateChanged;
        await DisposePageAsync();
    }
}
