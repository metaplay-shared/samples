using Game.Logic;

namespace WebClient.Meta;

/// <summary>
/// The names of the analytics events that the client is allowed to report.
/// <para>
/// The per-player event log is an audit trail of player state changes, not a UI clickstream, so the client
/// reports only screens and promoted-entry selections (<c>docs/analytics.md</c>, "Client-submitted events").
/// It reports no economy events, because the server records every grant and a client copy would double-count.
/// </para>
/// </summary>
public static class ShellAnalytics
{
    /// <summary>The player arrived at a screen.</summary>
    public const string ScreenViewed = "screen_viewed";

    /// <summary>The player selected the next-action card, a feature card or a teaser.</summary>
    public const string PromotedEntrySelected = "promoted_entry_selected";

    /// <summary>
    /// All allowed event names. Update docs/analytics.md, "Client-submitted events", before adding one.
    /// </summary>
    public static readonly IReadOnlyList<string> All = new[] { ScreenViewed, PromotedEntrySelected };

    /// <summary>
    /// Event names that the client must not report, listed explicitly.
    /// <para>
    /// Connection and app start events are already reported by the SDK, so a client copy would duplicate them. A
    /// navigation tap is covered by the <see cref="ScreenViewed"/> it causes. The others describe UI activity, not
    /// changes to the player, and would fill the per-player event log.
    /// </para>
    /// </summary>
    public static readonly IReadOnlyList<string> Excluded = new[]
    {
        "app_boot_started", "app_boot_completed", "app_boot_failed",
        "primary_nav_selected", "feature_entry_selected",
        "home_next_action_impression", "home_next_action_selected",
        "teaser_impression", "teaser_selected",
        "modal_opened", "modal_dismissed",
        "reward_reveal_started", "reward_reveal_skipped", "reward_reveal_completed",
        "retry_selected",
        "connection_lost", "connection_restored",
    };
}

/// <summary>
/// One analytics observation reported by the client.
/// <para>
/// Every field of every subtype is an enum instead of a free-form string, so an observation cannot carry
/// personal data such as a display name or a URL.
/// </para>
/// </summary>
public abstract record ShellObservation
{
    /// <summary>The event name, one of <see cref="ShellAnalytics.All"/>.</summary>
    public abstract string Name { get; }

    /// <summary>
    /// Calls the <see cref="IShellObservationVisitor"/> overload for this subtype. A new subtype needs a new
    /// overload on the interface, so every visitor fails to compile until it handles the new subtype.
    /// </summary>
    public abstract void Accept(IShellObservationVisitor visitor);
}

/// <summary>The player arrived at a screen.</summary>
public sealed record ScreenViewed(ShellScreen Screen) : ShellObservation
{
    public override string Name => ShellAnalytics.ScreenViewed;
    public override void Accept(IShellObservationVisitor visitor) => visitor.Visit(this);
}

/// <summary>The player selected a promoted entry on screen <c>From</c> that leads to <c>Destination</c>.</summary>
public sealed record PromotedEntrySelected(PromotedEntryPlacement Placement, ShellScreen From, ShellScreen Destination) : ShellObservation
{
    public override string Name => ShellAnalytics.PromotedEntrySelected;
    public override void Accept(IShellObservationVisitor visitor) => visitor.Visit(this);
}

/// <summary>
/// One method per <see cref="ShellObservation"/> subtype. Sinks dispatch through
/// <see cref="ShellObservation.Accept"/> instead of a <c>switch</c> on the runtime type, so an unhandled subtype
/// is a compile error instead of a runtime failure.
/// </summary>
public interface IShellObservationVisitor
{
    void Visit(ScreenViewed observation);
    void Visit(PromotedEntrySelected observation);
}

/// <summary>
/// The destination of shell observations. The real implementation,
/// <c>WebClient.Services.PlayerTimelineShellAnalytics</c>, needs the browser-only SDK client and is not compiled
/// into the test project, so tests use <see cref="RecordingShellAnalytics"/> instead.
/// </summary>
public interface IShellAnalytics
{
    void Observe(ShellObservation observation);
}

/// <summary>
/// Stores observations in memory and sends nothing. Used by tests of <see cref="ShellObservationPolicy"/> and
/// <see cref="ShellObservationRecorder"/>.
/// </summary>
public sealed class RecordingShellAnalytics : IShellAnalytics
{
    private readonly List<ShellObservation> _observations = new List<ShellObservation>();

    public IReadOnlyList<ShellObservation> Observations => _observations;

    public void Observe(ShellObservation observation) => _observations.Add(observation);

    public void Clear() => _observations.Clear();
}
