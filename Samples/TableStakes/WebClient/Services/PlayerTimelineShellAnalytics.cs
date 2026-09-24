using WebClient.Meta;

namespace WebClient.Services;

/// <summary>
/// The <see cref="IShellAnalytics"/> implementation used in the app. It executes the matching
/// <c>PlayerObserve…</c> player action for each <see cref="ShellObservation"/> through
/// <see cref="MetaplayClientService"/>. It lives in <c>WebClient.Services</c> because the <c>WebClient.Meta</c>
/// sources are compiled into <c>WebClient.Tests</c> and must stay free of session dependencies. It implements
/// <see cref="IShellObservationVisitor"/>, so a new <see cref="ShellObservation"/> subtype fails to compile until
/// this class handles it.
/// </summary>
public sealed class PlayerTimelineShellAnalytics : IShellAnalytics, IShellObservationVisitor
{
    private readonly MetaplayClientService _client;

    public PlayerTimelineShellAnalytics(MetaplayClientService client)
    {
        _client = client;
    }

    public void Observe(ShellObservation observation) => observation.Accept(this);

    public void Visit(ScreenViewed observation) => _client.ObserveScreenViewed(observation.Screen);

    public void Visit(PromotedEntrySelected observation) =>
        _client.ObservePromotedEntrySelected(observation.Placement, observation.From, observation.Destination);
}
