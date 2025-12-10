// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Game.Logic.TypeCodes;
using Metaplay.Core;
using System.Runtime.Serialization;

namespace Game.Logic.Matchmaking
{
    [MetaMessage(MessageCodes.IdleMatchingRequest, MessageDirection.ClientToServer), MessageRoutingRuleSession]
    public class IdleMatchingRequest : MetaMessage
    {
        public IdleMatchingRequest() { }
    }

    [MetaMessage(MessageCodes.IdleMatchingResponse, MessageDirection.ServerToClient)]
    public class IdleMatchingResponse : MetaMessage
    {
        public bool IsSuccess { get; set; }
        public bool DidWinBattle { get; set; }

        IdleMatchingResponse() { }
        
        public IdleMatchingResponse(bool isSuccess, bool didWinBattle)
        {
            IsSuccess = isSuccess;
            DidWinBattle = didWinBattle;
        }
    }
}

