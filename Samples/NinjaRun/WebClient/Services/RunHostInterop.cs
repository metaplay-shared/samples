using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using WebClient.Gameplay;

namespace WebClient.Services;

/// <summary>
/// The run's host module (<c>wwwroot/js/timeTrialHost.js</c>): the frame loop that steps and draws a run, its
/// input, the run's sound and the mute switch, the menu's tick, and the browser's reduced-motion preference.
/// It is imported on the main page's first render rather than with the first run, so the menu's own sound and
/// its mute switch work before anything has been played.
/// </summary>
public sealed class RunHostInterop : JsModuleInterop
{
    public RunHostInterop(IJSRuntime js) : base(js, "./js/timeTrialHost.js")
    {
    }

    /// <summary>
    /// Hand <paramref name="trial"/> to the frame loop on <paramref name="canvas"/>. Returns once the run's art
    /// is in, or the wait for it has run out, reporting its progress to <paramref name="page"/>'s
    /// <c>OnRunArtProgress</c> meanwhile. Returns whether the loop was started.
    /// </summary>
    public Task<bool> StartAsync(ElementReference canvas, object trial, TimeTrialCourseView course, string assetBaseUrl, string audioBaseUrl, object page)
        => SafeInvokeVoidAsync("start", canvas, trial, course, assetBaseUrl, audioBaseUrl, page);

    /// <summary>Stop the frame loop. Nothing is imported for it: a module never loaded has no loop to stop.</summary>
    public Task StopAsync() => IsLoaded ? SafeInvokeVoidAsync("stop") : Task.CompletedTask;

    /// <summary>Let the button reach the run, or hold it off while the curtain moves.</summary>
    public Task SetInputEnabledAsync(bool enabled) => SafeInvokeVoidAsync("setInputEnabled", enabled);

    /// <summary>The mute switch as the browser remembers it, or <paramref name="fallback"/> without the module.</summary>
    public async Task<bool> IsMutedAsync(bool fallback)
    {
        (bool succeeded, bool muted) = await SafeInvokeAsync<bool>("isMuted");
        return succeeded ? muted : fallback;
    }

    /// <summary>Flip the mute switch, which the module owns and remembers. Returns the new state, or <paramref name="current"/> without the module.</summary>
    public async Task<bool> ToggleMutedAsync(bool current)
    {
        (bool succeeded, bool muted) = await SafeInvokeAsync<bool>("toggleMuted");
        return succeeded ? muted : current;
    }

    /// <summary>The menu's own tick. Best effort: a client that cannot make a noise still navigates.</summary>
    public Task PlayMenuTapAsync() => SafeInvokeVoidAsync("playMenuTap");

    /// <summary>Whether the browser asks for reduced motion. Without the module it is assumed not to.</summary>
    public async Task<bool> PrefersReducedMotionAsync() => (await SafeInvokeAsync<bool>("prefersReducedMotion")).Result;
}
