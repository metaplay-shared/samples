using WebClientBase.Services;
using Game.Logic;
using Metaplay.Core.Client;
using Metaplay.Unity;

namespace WebClient.Services;

/// <summary>Read-only community snapshots travel through the authenticated player session.</summary>
public class CommunityService
{
    readonly MetaplayClientService _client;
    DateTime _receivedAt;
    DateTime _nextRequest;
    CommunitySnapshot? _snapshot;
    public event Action? Changed;
    public bool IsOffline => IEnvironmentConfigProvider.Get().ConnectionEndpointConfig.IsOfflineMode;
    public CommunitySnapshot? Snapshot => _client.ConnectionStatus == ConnectionStatus.Connected
        && DateTime.UtcNow - _receivedAt < TimeSpan.FromSeconds(30) ? _snapshot : null;

    /// <summary>
    /// How often the panels ask for a snapshot. Requests are spaced at no less than half of it, so the
    /// re-renders that also ask cannot outpace the poll. A test shortens it with Home's <c>?poll=</c>, and the
    /// shortened period holds for the rest of the page's life, across in-app navigation.
    /// </summary>
    public TimeSpan PollInterval { get; private set; } = TimeSpan.FromSeconds(10);

    public CommunityService(MetaplayClientService client) { _client = client; }

    /// <summary> Sets the poll period, never below a tenth of a second. </summary>
    public void UsePollInterval(TimeSpan interval)
        => PollInterval = interval < TimeSpan.FromMilliseconds(100) ? TimeSpan.FromMilliseconds(100) : interval;

    public void BindListeners() => MetaplaySDK.MessageDispatcher.AddListener<CommunityResponse>(OnResponse);

    public void OnSessionStarted()
    {
        _snapshot = null;
        _nextRequest = default;
        Changed?.Invoke();
    }

    public void Refresh()
    {
        if (IsOffline || _client.ConnectionStatus != ConnectionStatus.Connected
            || DateTime.UtcNow < _nextRequest)
            return;
        _nextRequest = DateTime.UtcNow + PollInterval / 2;
        MetaplaySDK.MessageDispatcher.SendMessage(new CommunityRequest());
    }

    void OnResponse(CommunityResponse response)
    {
        _snapshot = response.Snapshot;
        _receivedAt = DateTime.UtcNow;
        Changed?.Invoke();
    }
}
