using Game.Logic;
using Metaplay.Cloud.Entity;
using Metaplay.Core;
using Metaplay.Core.Message;

namespace Game.Server.Community;

[MetaMessage(MessageCodes.InternalLeaderboardUpdate, MessageDirection.ServerInternal)]
public class InternalLeaderboardUpdate : MetaMessage
{
    public LeaderboardEntry Entry { get; private set; }
    public InternalLeaderboardUpdate() { }
    public InternalLeaderboardUpdate(LeaderboardEntry entry) { Entry = entry; }
}

[MetaMessage(MessageCodes.InternalMatchPopulation, MessageDirection.ServerInternal)]
public class InternalMatchPopulation : MetaMessage
{
    public int Players { get; private set; }
    public InternalMatchPopulation() { }
    public InternalMatchPopulation(int players) { Players = players; }
}

[MetaMessage(MessageCodes.InternalCommunityRead, MessageDirection.ServerInternal)]
public class InternalCommunityRead : EntityAskRequest<InternalCommunityReadResponse> { }

[MetaMessage(MessageCodes.InternalCommunityReadResponse, MessageDirection.ServerInternal)]
public class InternalCommunityReadResponse : EntityAskResponse
{
    public CommunitySnapshot Snapshot { get; private set; }
    public InternalCommunityReadResponse() { }
    public InternalCommunityReadResponse(CommunitySnapshot snapshot) { Snapshot = snapshot; }
}

[MetaMessage(MessageCodes.InternalQueuePopulationRequest, MessageDirection.ServerInternal)]
public class InternalQueuePopulationRequest : EntityAskRequest<InternalQueuePopulationResponse> { }

[MetaMessage(MessageCodes.InternalQueuePopulationResponse, MessageDirection.ServerInternal)]
public class InternalQueuePopulationResponse : EntityAskResponse
{
    public int Players { get; private set; }
    public InternalQueuePopulationResponse() { }
    public InternalQueuePopulationResponse(int players) { Players = players; }
}
