using Game.ClientBase.Configuration;
using Game.ClientBase.Services;
using Game.Logic;
using Metaplay.Unity;

namespace Game.Client.Services;

/// <summary>Read-only community snapshots travel through the authenticated player session.</summary>
public class CommunityService
{
    readonly MetaplayClientService _client;
    DateTime _receivedAt;
    DateTime _nextRequest;
    CommunitySnapshot? _snapshot;
    public event Action? Changed;
    public bool IsOffline => StaticEnvironmentConfigProvider.IsOfflineMode;
    public CommunitySnapshot? Snapshot => _client.ConnectionStatus == ConnectionStatus.Connected
        && DateTime.UtcNow - _receivedAt < TimeSpan.FromSeconds(30) ? _snapshot : null;

    public CommunityService(MetaplayClientService client) { _client = client; }

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
        _nextRequest = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        MetaplaySDK.MessageDispatcher.SendMessage(new CommunityRequest());
    }

    void OnResponse(CommunityResponse response)
    {
        _snapshot = response.Snapshot;
        _receivedAt = DateTime.UtcNow;
        Changed?.Invoke();
    }
}
