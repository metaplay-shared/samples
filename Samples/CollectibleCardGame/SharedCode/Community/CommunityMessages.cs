using Metaplay.Core;
using Metaplay.Core.Message;
using Metaplay.Core.Model;
using System.Collections.Generic;

namespace Game.Logic;

[MetaSerializable]
public class LeaderboardEntry
{
    [MetaMember(1)] public EntityId PlayerId { get; set; }
    [MetaMember(2)] public string DisplayName { get; set; } = "";
    [MetaMember(3)] public int Rating { get; set; }
    [MetaMember(4)] public int Wins { get; set; }
    [MetaMember(5)] public int Matches { get; set; }
}

[MetaSerializable]
public class LeaderboardRow
{
    [MetaMember(1)] public int Position { get; set; }
    [MetaMember(2)] public LeaderboardEntry Player { get; set; }
}

[MetaSerializable]
public class CommunitySnapshot
{
    [MetaMember(1)] public List<LeaderboardRow> Leaders { get; set; } = new List<LeaderboardRow>();
    [MetaMember(2)] public LeaderboardRow OwnPosition { get; set; }
    [MetaMember(3)] public int RankedPlayers { get; set; }
    [MetaMember(4)] public int Online { get; set; }
    [MetaMember(5)] public int Queued { get; set; }
    [MetaMember(6)] public int InMatch { get; set; }
    [MetaMember(7)] public MetaTime SampledAt { get; set; }
}

[MetaMessage(MessageCodes.CommunityRequest, MessageDirection.ClientToServer), MessageRoutingRuleOwnedPlayer]
public class CommunityRequest : MetaMessage { }

[MetaMessage(MessageCodes.CommunityResponse, MessageDirection.ServerToClient)]
public class CommunityResponse : MetaMessage
{
    public CommunitySnapshot Snapshot { get; private set; }
    public CommunityResponse() { }
    public CommunityResponse(CommunitySnapshot snapshot) { Snapshot = snapshot; }
}
