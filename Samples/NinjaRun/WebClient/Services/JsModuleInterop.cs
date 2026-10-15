using Microsoft.JSInterop;
using Microsoft.JSInterop.Infrastructure;

namespace WebClient.Services;

/// <summary>
/// One JavaScript module a page drives, imported the first time it is called. Every call goes through
/// <see cref="SafeInvokeAsync{T}"/>, which treats the page going away and a module that is missing or throws
/// alike: the call reports that it did not succeed and the caller carries on with what it had, since nothing
/// these modules do is worth failing the page over.
/// </summary>
public abstract class JsModuleInterop : IAsyncDisposable
{
    readonly IJSRuntime _js;
    readonly string _path;
    IJSObjectReference? _module;
    bool _disposed;

    protected JsModuleInterop(IJSRuntime js, string path)
    {
        _js   = js;
        _path = path;
    }

    /// <summary>Whether the module has been imported, so a call made now reaches it without importing it first.</summary>
    public bool IsLoaded => _module != null;

    /// <summary>
    /// Call <paramref name="identifier"/> on the module, importing it first when it is not yet. Returns whether
    /// the call returned, and its result, or the default when it did not: the page is going away, the module
    /// could not be imported, or the call threw. After <see cref="DisposeAsync"/> nothing is called.
    /// </summary>
    protected async Task<(bool Succeeded, T Result)> SafeInvokeAsync<T>(string identifier, params object?[] args)
    {
        if (_disposed)
            return (false, default);

        try
        {
            _module ??= await _js.InvokeAsync<IJSObjectReference>("import", _path);
            T result = await _module.InvokeAsync<T>(identifier, args);
            return (true, result);
        }
        catch (JSDisconnectedException)
        {
            return (false, default);
        }
        catch (JSException error)
        {
            Console.WriteLine($"[{GetType().Name}] {identifier} failed: {error.Message}");
            return (false, default);
        }
    }

    /// <summary><see cref="SafeInvokeAsync{T}"/> for a call that returns nothing. Returns whether it returned.</summary>
    protected async Task<bool> SafeInvokeVoidAsync(string identifier, params object?[] args)
        => (await SafeInvokeAsync<IJSVoidResult>(identifier, args)).Succeeded;

    /// <summary>Drop the imported module without disposing it, so the next call imports it again.</summary>
    protected void ForgetModule() => _module = null;

    /// <summary>Take down whatever the module set up for this page before it is released. Called once, from <see cref="DisposeAsync"/>.</summary>
    protected virtual Task DetachAsync() => Task.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        if (_module != null)
            await DetachAsync();

        _disposed = true;

        if (_module == null)
            return;

        try
        {
            await _module.DisposeAsync();
        }
        catch (JSDisconnectedException)
        {
        }

        _module = null;
    }
}
