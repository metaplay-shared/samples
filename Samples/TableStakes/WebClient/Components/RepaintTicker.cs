using System;
using System.Threading;
using System.Threading.Tasks;

namespace WebClient.Components;

/// <summary>
/// Wakes on a fixed interval and re-renders a component when the caller's check says the display changed.
/// <para>
/// The table and the matchmaking dialog use it for countdowns against absolute timestamps, which change on
/// screen without any server update.
/// </para>
/// </summary>
public sealed class RepaintTicker : IDisposable
{
    readonly PeriodicTimer _timer;

    RepaintTicker(PeriodicTimer timer)
    {
        _timer = timer;
    }

    /// <summary>
    /// Starts ticking. The loop runs until the ticker is disposed.
    /// </summary>
    /// <param name="interval">How often to wake.</param>
    /// <param name="needsRepaint">Called on every tick. Returns true when the component must re-render.</param>
    /// <param name="repaint">Re-renders the component, usually <c>InvokeAsync(StateHasChanged)</c>.</param>
    public static RepaintTicker Start(TimeSpan interval, Func<bool> needsRepaint, Func<Task> repaint)
    {
        if (needsRepaint == null)
            throw new ArgumentNullException(nameof(needsRepaint));
        if (repaint == null)
            throw new ArgumentNullException(nameof(repaint));

        RepaintTicker ticker = new RepaintTicker(new PeriodicTimer(interval));
        _ = ticker.RunAsync(needsRepaint, repaint);
        return ticker;
    }

    async Task RunAsync(Func<bool> needsRepaint, Func<Task> repaint)
    {
        try
        {
            while (await _timer.WaitForNextTickAsync())
            {
                if (needsRepaint())
                    await repaint();
            }
        }
        catch (ObjectDisposedException)
        {
            // Dispose disposes the timer while the loop is waiting. This is the normal way the loop stops.
        }
        catch (OperationCanceledException)
        {
        }
    }

    public void Dispose() => _timer.Dispose();
}
