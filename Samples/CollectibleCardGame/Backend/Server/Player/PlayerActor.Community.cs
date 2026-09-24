using Game.Logic;
using Game.Server.Community;
using Metaplay.Cloud.Entity;
using System;
using System.Threading.Tasks;

namespace Game.Server.Player;

public sealed partial class PlayerActor
{
    DateTime _nextCommunityRequest;

    void PublishLeaderboardEntry()
    {
        if (Model.Record.RankedMatchesPlayed <= 0)
            return;
        CastMessage(CommunityActor.EntityId, new InternalLeaderboardUpdate(new LeaderboardEntry
        {
            PlayerId = _entityId, DisplayName = Model.DisplayName, Rating = Model.Record.Rating,
            Wins = Model.Record.RankedWins, Matches = Model.Record.RankedMatchesPlayed,
        }));
    }

    void IPlayerModelServerListener.OnRankedRecordChanged() => PublishLeaderboardEntry();

    [MessageHandler]
    async Task HandleCommunityRequest(EntitySubscriber subscriber, CommunityRequest request)
    {
        if (subscriber != TryGetSession() || DateTime.UtcNow < _nextCommunityRequest)
            return;
        _nextCommunityRequest = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        try
        {
            PublishLeaderboardEntry();
            InternalCommunityReadResponse response = await EntityAskAsync<InternalCommunityReadResponse>(
                CommunityActor.EntityId, new InternalCommunityRead());
            SendToClient(new CommunityResponse(response.Snapshot));
        }
        catch (Exception ex)
        {
            _log.Warning("Community snapshot unavailable: {Error}", ex.GetType().Name);
            SendToClient(new CommunityResponse(null));
        }
    }
}
