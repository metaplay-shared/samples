using Microsoft.JSInterop;

namespace WebClient.Services;

/// <summary>
/// The views of the main page that sit on a history entry of their own, so browser back leaves them: a run,
/// with its score screen, and the customize view.
/// </summary>
public enum HistoryView
{
    Run,
    Customize,
}

/// <summary>
/// The main page's browser-level controls (<c>wwwroot/js/homeControls.js</c>): the window's keys, the history
/// entries the views sit on, and fullscreen. Without the module the keys, browser back and the fullscreen
/// button are simply not there; every button on the page still works.
/// </summary>
public sealed class HomeControlsInterop : JsModuleInterop
{
    /// <summary>The key mode the module was last told, so it is only told again on a change.</summary>
    string? _keyModeSent;

    public HomeControlsInterop(IJSRuntime js) : base(js, "./js/homeControls.js")
    {
    }

    /// <summary>The name the module and its callbacks know <paramref name="view"/> by.</summary>
    public static string NameOf(HistoryView view) => view == HistoryView.Run ? "run" : "customize";

    /// <summary>The view a callback from the module names, or null for a name that is none of them.</summary>
    public static HistoryView? ParseView(string? name) => name switch
    {
        "run"       => HistoryView.Run,
        "customize" => HistoryView.Customize,
        _           => null,
    };

    /// <summary>
    /// Start the module listening for <paramref name="owner"/>, and read whether fullscreen is possible and
    /// already on. A module that cannot attach is dropped, so the keys stay off and the next call imports it
    /// again.
    /// </summary>
    public async Task<(bool Available, bool On)> AttachAsync(object owner)
    {
        (bool attached, bool available) = await SafeInvokeAsync<bool>("attach", owner);
        if (!attached)
        {
            ForgetModule();
            return (false, false);
        }

        bool on = available && (await SafeInvokeAsync<bool>("isFullscreen")).Result;
        return (available, on);
    }

    /// <summary>
    /// Tell the module what the window's keys do now (<c>menu</c>, <c>result</c> or <c>none</c>) when it has
    /// changed. The module ignores any key that was already held when the mode changed until it is released.
    /// Nothing is said before the module is attached.
    /// </summary>
    public async Task SyncKeyModeAsync(string mode)
    {
        if (!IsLoaded || mode == _keyModeSent)
            return;

        _keyModeSent = mode;
        await SafeInvokeVoidAsync("setKeyMode", mode);
    }

    /// <summary>
    /// Enter or leave fullscreen, inside the click that asked for it, which is the gesture the browser wants.
    /// Returns whether the document is fullscreen afterwards, or <paramref name="current"/> when it was refused.
    /// </summary>
    public async Task<bool> ToggleFullscreenAsync(bool current)
    {
        (bool succeeded, bool on) = await SafeInvokeAsync<bool>("toggleFullscreen");
        return succeeded ? on : current;
    }

    /// <summary>
    /// Put a history entry under <paramref name="view"/> as it starts opening, so browser back leaves it. Best
    /// effort: without one the view's own way out still leaves it directly.
    /// </summary>
    public Task PushViewEntryAsync(HistoryView view) => SafeInvokeVoidAsync("pushViewEntry", NameOf(view));

    /// <summary>
    /// Step back off <paramref name="view"/>'s history entry when the page is on it. Returns whether it did,
    /// in which case the module's <c>OnHistoryLeftView</c> that follows is what leaves the view; otherwise the
    /// page leaves it directly.
    /// </summary>
    public async Task<bool> LeaveViewEntryAsync(HistoryView view)
        => (await SafeInvokeAsync<bool>("leaveViewEntry", NameOf(view))).Result;

    protected override Task DetachAsync() => SafeInvokeVoidAsync("detach");
}
