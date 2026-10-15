using System;
using System.Threading.Tasks;

namespace WebClient.Gameplay;

/// <summary>Where the screen curtain is in one pass.</summary>
public enum ScreenTransitionPhase
{
    Hidden,
    Covering,
    Covered,
    Revealing,
}

/// <summary>
/// The screen curtain every view of the main page is swapped behind (C0 continuity): a pass is begun, covers
/// the portrait area, holds it covered while the views are swapped (and while a run's art is waited for) and
/// uncovers it again. Only one pass runs at a time. <c>Components/ScreenCurtain.razor</c> draws it, and the
/// page that owns it is told to render whenever it moves.
/// </summary>
public sealed class ScreenTransition
{
    /// <summary>
    /// How long the curtain takes to cross, each way. Enough time to read as foreground scenery crossing the
    /// compact portrait stage, without holding the player up on the way into a run. Both durations also feed
    /// the curtain's CSS custom properties, keeping these waits in lockstep with css/transition.css.
    /// </summary>
    public const int DurationMs = 480;

    /// <summary>The same crossing under reduced motion.</summary>
    public const int ReducedDurationMs = 130;

    /// <summary>
    /// Not part of the crossing: the beat the view swap happens in, behind solid black. A run's art is waited
    /// for after it, behind the same cover, for as long as it takes.
    /// </summary>
    const int CoveredHoldMs = 120;

    readonly Func<Task<bool>> _prefersReducedMotion;
    readonly Action _changed;

    /// <summary>Whether a pass has been begun and has not yet uncovered the screen.</summary>
    bool _passPending;

    /// <summary>The browser's reduced-motion preference as the pass under way read it, so a pass asks once.</summary>
    bool? _reducedMotion;

    /// <param name="prefersReducedMotion">Asks the browser whether it wants reduced motion.</param>
    /// <param name="changed">Renders the page that draws the curtain.</param>
    public ScreenTransition(Func<Task<bool>> prefersReducedMotion, Action changed)
    {
        _prefersReducedMotion = prefersReducedMotion;
        _changed              = changed;
    }

    public ScreenTransitionPhase Phase { get; private set; }

    /// <summary>Whether a pass is under way: begun, or with the curtain anywhere but out of sight.</summary>
    public bool IsTransitioning => _passPending || Phase != ScreenTransitionPhase.Hidden;

    /// <summary>
    /// Whether a run's art is being waited for behind the covered curtain, which is what puts the loading
    /// notice on it, and how many of its sheets are in out of how many.
    /// </summary>
    public bool RunArtLoading { get; private set; }
    public int  RunArtSettled { get; private set; }
    public int  RunArtTotal   { get; private set; }

    /// <summary>Whether the loading notice is on the curtain: the run's art is being waited for while it is covered.</summary>
    public bool ShowsLoadingNotice => RunArtLoading && Phase == ScreenTransitionPhase.Covered;

    public string CssClass => Phase switch
    {
        ScreenTransitionPhase.Covering  => "screen-transition-covering",
        ScreenTransitionPhase.Covered   => "screen-transition-covered",
        ScreenTransitionPhase.Revealing => "screen-transition-revealing",
        _                               => "screen-transition-hidden",
    };

    public string StateName => Phase switch
    {
        ScreenTransitionPhase.Covering  => "covering",
        ScreenTransitionPhase.Covered   => "covered",
        ScreenTransitionPhase.Revealing => "revealing",
        _                               => "hidden",
    };

    /// <summary>Begin a pass, unless one is already under way. Returns whether this one may go ahead.</summary>
    public bool TryBegin()
    {
        if (IsTransitioning)
            return false;

        _passPending   = true;
        _reducedMotion = null;
        return true;
    }

    /// <summary>
    /// Whether the browser asks for reduced motion. A pass under way asks once and keeps the answer for the
    /// rest of it; outside a pass the browser is asked every time.
    /// </summary>
    public async Task<bool> PrefersReducedMotionAsync()
    {
        if (IsTransitioning && _reducedMotion is bool known)
            return known;

        bool reduced = await _prefersReducedMotion();
        if (IsTransitioning)
            _reducedMotion = reduced;
        return reduced;
    }

    /// <summary>How long one crossing of the curtain takes, under the browser's motion preference.</summary>
    public async Task<int> GetDurationAsync() => await PrefersReducedMotionAsync() ? ReducedDurationMs : DurationMs;

    /// <summary>Draw the curtain across the portrait area, then hold it covered for the beat the views are swapped in.</summary>
    public async Task CoverAsync(int durationMs)
    {
        Phase = ScreenTransitionPhase.Covering;
        _changed();
        await Task.Delay(durationMs);

        Phase = ScreenTransitionPhase.Covered;
        _changed();
        await Task.Delay(CoveredHoldMs);
    }

    /// <summary>
    /// Draw the curtain off the portrait area and end the pass. <paramref name="uncovered"/> runs as it comes
    /// to rest, before the page renders the uncovered screen.
    /// </summary>
    public async Task RevealAsync(int durationMs, Action? uncovered = null)
    {
        Phase = ScreenTransitionPhase.Revealing;
        _changed();
        await Task.Delay(durationMs);

        Phase        = ScreenTransitionPhase.Hidden;
        _passPending = false;
        uncovered?.Invoke();
        _changed();
    }

    /// <summary>A run's art starts being waited for behind the curtain; the notice goes on it while it is covered.</summary>
    public void BeginRunArtWait()
    {
        RunArtLoading = true;
        RunArtSettled = 0;
        RunArtTotal   = 0;
        _changed();
    }

    /// <summary>How many of the run's sheets are in, out of how many, while they are waited for.</summary>
    public void ReportRunArtProgress(int settled, int total)
    {
        RunArtSettled = settled;
        RunArtTotal   = total;
        if (RunArtLoading)
            _changed();
    }

    /// <summary>The run's art is in, or the wait for it has run out.</summary>
    public void EndRunArtWait() => RunArtLoading = false;
}
