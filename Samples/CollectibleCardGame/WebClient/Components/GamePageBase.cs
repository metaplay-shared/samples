using Microsoft.AspNetCore.Components;
using WebClient.Services;

namespace WebClient.Components;

/// <summary>
/// The base of the game's routed pages (<c>@inherits GamePageBase</c>). It starts the client service when the page
/// opens, so a page opened by its own URL connects too, and renders the page again on the client service's
/// <c>OnStateChanged</c>.
/// </summary>
public abstract class GamePageBase : ComponentBase, IAsyncDisposable
{
    [Inject] protected MetaplayClientService MetaplayService { get; set; } = default!;

    protected override void OnInitialized()
    {
        MetaplayService.OnStateChanged += OnServiceStateChanged;
        MetaplayService.OnFrame += OnFrame;
        MetaplayService.Start();
    }

    /// <summary>The client service raised <c>OnStateChanged</c>. Renders the page; a page with more to do on it overrides this.</summary>
    protected virtual void OnServiceStateChanged() => InvokeAsync(StateHasChanged);

    /// <summary>
    /// The page's own work in each frame, between the SDK's frame start and end, such as advancing what it shows with
    /// time. It renders nothing by itself: a page whose per-frame work changes what it shows calls
    /// <c>StateHasChanged</c>.
    /// </summary>
    protected virtual void OnFrame() { }

    /// <summary>Release what the page holds besides its subscription to the client service, such as a JS module.</summary>
    protected virtual ValueTask DisposePageAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        MetaplayService.OnStateChanged -= OnServiceStateChanged;
        MetaplayService.OnFrame -= OnFrame;
        await DisposePageAsync();
    }
}
