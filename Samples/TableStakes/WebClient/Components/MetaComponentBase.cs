using System;
using Microsoft.AspNetCore.Components;
using WebClient.Services;

namespace WebClient.Components;

/// <summary>
/// A component that re-renders when the meta state changes. It subscribes to <see cref="MetaStateService.OnStateChanged"/>
/// in <see cref="OnInitialized"/> and unsubscribes in <see cref="Dispose"/>, so no screen can leave a disposed
/// component subscribed to the long-lived service.
/// <para>
/// A component that overrides <see cref="OnInitialized"/> calls the base method where it subscribes. A component
/// with other subscriptions removes them in <see cref="OnDispose"/>.
/// </para>
/// </summary>
public abstract class MetaComponentBase : ComponentBase, IDisposable
{
    [Inject] protected MetaStateService Meta { get; set; } = default!;

    protected override void OnInitialized() => Meta.OnStateChanged += OnMetaStateChanged;

    /// <summary>Called when the meta state changes. Re-renders the component.</summary>
    protected virtual void OnMetaStateChanged() => InvokeAsync(StateHasChanged);

    /// <summary>Called by <see cref="Dispose"/> after the meta state subscription is removed.</summary>
    protected virtual void OnDispose()
    {
    }

    public void Dispose()
    {
        Meta.OnStateChanged -= OnMetaStateChanged;
        OnDispose();
    }
}
