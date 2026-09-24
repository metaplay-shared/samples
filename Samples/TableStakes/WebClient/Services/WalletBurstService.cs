using System.Timers;
using Microsoft.AspNetCore.Components;
using WebClient.Integration;
using WebClient.Meta;

namespace WebClient.Services;

/// <summary>
/// Runs the reward animation that flies currency to the HUD and updates the HUD balances. One instance serves the
/// whole shell, because the HUD is on every meta route and the player can leave the granting screen mid-animation.
/// <para>
/// The grant is applied before the reveal is shown, so <see cref="Hold"/> holds back the HUD balance from then on.
/// Without the hold, the HUD behind the reveal would show the new balance and then drop back when the animation
/// starts. <see cref="Launch"/> starts the animation when the player dismisses the reveal. The animation logic is
/// in <see cref="WalletBurstPlan"/> and <see cref="WalletBurstBalances"/>, and this class only starts, ticks and stops it.
/// </para>
/// </summary>
public sealed class WalletBurstService : IDisposable
{
    /// <summary>
    /// The interval in milliseconds between HUD balance redraws while a reward animation runs.
    /// </summary>
    private const int FrameMs = 33;

    private readonly NavigationManager _navigation;
    private readonly System.Timers.Timer _frameTimer;

    private WalletBurstBalances? _balances;
    private bool _launched;
    private long _launchedAtTickMs;

    public WalletBurstService(NavigationManager navigation)
    {
        _navigation = navigation;

        _frameTimer = new System.Timers.Timer(FrameMs) { AutoReset = true };
        _frameTimer.Elapsed += OnFrame;
    }

    /// <summary>Raised on every animation frame, and when the hold or animation starts or ends.</summary>
    public event Action? OnChanged;

    /// <summary>
    /// The animation plan the burst layer draws, or null if no animation is running. It is null while a reward is
    /// only held back, that is, while the reveal is still open.
    /// </summary>
    public WalletBurstPlan? ActiveBurst => _launched ? _balances?.Plan : null;

    /// <summary>
    /// Increments on every <see cref="Launch"/>. The burst layer uses it as the sprite element key, so each launch
    /// creates new elements and their CSS animations restart.
    /// </summary>
    public int BurstId { get; private set; }

    /// <summary>
    /// Milliseconds since <see cref="Launch"/>, or zero while the reward is only held. Zero makes
    /// <see cref="WalletBurstBalances.ValueAt"/> return the balance without the reward, so the hold needs no separate logic.
    /// </summary>
    private int ElapsedMs => _launched ? (int)(Environment.TickCount64 - _launchedAtTickMs) : 0;

    /// <summary>
    /// The balance the HUD shows for <paramref name="kind"/>: the model balance, or the value from
    /// <see cref="WalletBurstBalances.ValueAt"/> during a hold or an animation.
    /// <para>
    /// It is called during rendering, so it changes no state and raises no event. The frame timer ends the
    /// animation.
    /// </para>
    /// </summary>
    public long Displayed(CurrencyKind kind, WalletView live) =>
        _balances == null ? live.AmountOf(kind) : _balances.ValueAt(kind, live, ElapsedMs);

    /// <summary>
    /// The fraction of this currency's sprites that have landed, from 0 to 1, which the HUD chip's glow uses
    /// (<see cref="WalletBurstBalances.ProgressOf"/>).
    /// <para>
    /// Returns 0 while the reward is held. Returns 1 when there is no reward, when the reward does not include this
    /// currency, and under reduced motion.
    /// </para>
    /// </summary>
    public double Progress(CurrencyKind kind)
    {
        if (_balances == null || PrefersReducedMotion)
            return 1;

        if (!_launched)
            return _balances.Plan.Of(kind) == null ? 1 : 0;

        return _balances.ProgressOf(kind, ElapsedMs);
    }

    /// <summary>
    /// Whether a held or animating reward includes this currency. The HUD chip is highlighted while this is true.
    /// </summary>
    public bool RewardIncludes(CurrencyKind kind) => _balances?.Plan.Of(kind) != null;

    /// <summary>
    /// Makes the HUD show the balances without <paramref name="bundle"/> until <see cref="Launch"/>. Called when
    /// the grant is applied, which is when the reveal opens and the model balance changes.
    /// <para>
    /// No sprites are drawn yet. The displayed value is computed from the current wallet minus the reward, so the
    /// exact call time relative to the balance change does not matter (<see cref="WalletBurstBalances.Begin"/>).
    /// </para>
    /// </summary>
    public void Hold(RewardView bundle)
    {
        Stop();

        // Under reduced motion there is no animation, so the balance is not held back either
        // (docs/meta-shell.md, "The shared reward flow").
        if (PrefersReducedMotion)
            return;

        WalletBurstBalances? balances = WalletBurstBalances.Begin(bundle, BurstMs);
        if (balances == null)
            return;

        _balances = balances;
        _launched = false;

        OnChanged?.Invoke();
    }

    /// <summary>
    /// Starts the animation for the held reward. The balances increase as sprites land. Called when the player
    /// dismisses the reveal. Does nothing if no reward is held or the animation already started.
    /// </summary>
    public void Launch()
    {
        if (_balances == null || _launched)
            return;

        _launched         = true;
        _launchedAtTickMs = Environment.TickCount64;
        BurstId++;

        _frameTimer.Start();
        OnChanged?.Invoke();
    }

    /// <summary>
    /// Ends any hold or animation immediately, so every balance shows the model value. Called when a reveal closes
    /// without being dismissed and when the player navigates away from one, so the HUD never keeps a held-back
    /// balance with no reveal on screen.
    /// </summary>
    public void Stop()
    {
        if (_balances == null)
            return;

        _frameTimer.Stop();
        _balances = null;
        _launched = false;
        OnChanged?.Invoke();
    }

    private void OnFrame(object? sender, ElapsedEventArgs args)
    {
        if (_balances != null && ElapsedMs < _balances.DurationMs)
            OnChanged?.Invoke();
        else
            Stop();
    }

    /// <summary>
    /// The animation sequence length in milliseconds: the <c>burstMs</c> query parameter if a test set it
    /// (<c>docs/testing.md</c>, "Forcing timers"), otherwise <see cref="WalletBurstPlan.DefaultBurstMs"/>. Every
    /// stage duration is a fraction of this value, so all stages scale together.
    /// </summary>
    private int BurstMs =>
        TestQuerySettings.TryGetInt(new Uri(_navigation.Uri).Query, "burstMs") ?? WalletBurstPlan.DefaultBurstMs;

    /// <summary>
    /// Whether the browser requests reduced motion. The hold is timed in C#, so CSS cannot shorten it
    /// (see <see cref="MotionPreference"/>).
    /// </summary>
    private static bool PrefersReducedMotion => MotionPreference.ReducedMotion;

    public void Dispose()
    {
        _frameTimer.Stop();
        _frameTimer.Elapsed -= OnFrame;
        _frameTimer.Dispose();
    }
}
